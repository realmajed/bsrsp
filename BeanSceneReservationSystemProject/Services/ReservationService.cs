using BeanSceneReservationSystemProject.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace BeanSceneReservationSystemProject.Services
{
    public class ReservationService : IReservationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly IDataProtector _cancellationTokenProtector;
        private readonly LinkGenerator _linkGenerator;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ReservationService(
            ApplicationDbContext context,
            IEmailSender emailSender,
            IDataProtectionProvider dataProtectionProvider,
            LinkGenerator linkGenerator,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _emailSender = emailSender;
            _cancellationTokenProtector = dataProtectionProvider.CreateProtector(ReservationCancellationToken.Purpose);
            _linkGenerator = linkGenerator;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<Reservation> CreateReservationAsync(Reservation reservation, List<int>? selectedTableIds = null, string? createdByUserId = null)
        {
            var validationErrors = await ValidateReservationAsync(reservation, selectedTableIds);
            if (validationErrors.Any())
            {
                throw new InvalidOperationException(string.Join(" ", validationErrors));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // New reservations always start pending until staff confirm or cancel them.
                reservation.Status = ReservationStatus.Pending;
                _context.Reservations.Add(reservation);
                await _context.SaveChangesAsync();

                _context.ReservationStatusHistories.Add(new ReservationStatusHistory
                {
                    ReservationId = reservation.ReservationId,
                    OldStatus = null,
                    NewStatus = ReservationStatus.Pending,
                    ChangedByUserId = createdByUserId,
                    ChangedDate = DateTime.Now
                });

                if (selectedTableIds != null)
                {
                    foreach (var tableId in selectedTableIds.Distinct())
                    {
                        _context.ReservationTables.Add(new ReservationTable
                        {
                            ReservationId = reservation.ReservationId,
                            RestaurantTableId = tableId
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send the confirmation after the database work is committed.
                await _emailSender.SendEmailAsync(
                    reservation.Email,
                    "Reservation received",
                    await BuildReservationEmailAsync(
                        reservation.ReservationId,
                        "Reservation received",
                        "We have received your reservation request.",
                        "Pending")
                );

                return reservation;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> ChangeStatusAsync(int reservationId, ReservationStatus newStatus, string? changedByUserId = null, bool tableAssignmentChanged = false)
        {
            var reservation = await _context.Reservations.FindAsync(reservationId)
                ?? throw new InvalidOperationException("Reservation was not found.");

            var oldStatus = reservation.Status;

            if (newStatus == oldStatus)
            {
                // Nothing to save or email if the status is already there.
                return false;
            }

            if (!IsValidStatusTransition(oldStatus, newStatus))
            {
                throw new InvalidOperationException($"Reservation status cannot be changed from {oldStatus} to {newStatus}.");
            }

            reservation.ChangeStatus(newStatus);
            _context.ReservationStatusHistories.Add(new ReservationStatusHistory
            {
                ReservationId = reservationId,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                ChangedByUserId = changedByUserId,
                ChangedDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            var message = $"Your reservation status changed from {oldStatus} to {newStatus}.";
            if (tableAssignmentChanged)
            {
                message += " Your assigned table has also been updated.";
            }


            // Send email for only: Pending aka creation, Confirmed and Cancelled reservations
            // This is to mitigate email span, and uneeded internal updates
            if (newStatus == ReservationStatus.Pending || newStatus == ReservationStatus.Confirmed || newStatus == ReservationStatus.Cancelled)
            {
                await _emailSender.SendEmailAsync(
                    reservation.Email,
                    $"Reservation {newStatus}",
                    await BuildReservationEmailAsync(
                        reservationId,
                        $"Reservation {newStatus}",
                        message,
                        newStatus.ToString())
                );
            }
            

            return true;
        }

        public async Task<List<string>> ValidateReservationAsync(Reservation reservation, List<int>? selectedTableIds = null)
        {
            var errors = new List<string>();
            var sitting = await _context.Sittings
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.SittingId == reservation.SittingId);

            if (sitting == null)
            {
                errors.Add("The selected sitting does not exist.");
                return errors;
            }

            if (sitting.IsClosed && reservation.Status != ReservationStatus.Cancelled)
            {
                errors.Add("The selected sitting is closed for reservations.");
            }

            if (reservation.StartTime < sitting.StartDateTime || reservation.StartTime >= sitting.EndDateTime)
            {
                errors.Add("The reservation start time must be within the selected sitting.");
            }

            if (reservation.EndTime > sitting.EndDateTime)
            {
                errors.Add("The reservation duration must end before the selected sitting ends.");
            }

            if (reservation.NumberOfGuests > 0)
            {
                // Sitting capacity is based on active reservations only.
                var bookedGuests = sitting.Reservations
                    .Where(r => r.ReservationId != reservation.ReservationId && r.Status != ReservationStatus.Cancelled)
                    .Sum(r => r.NumberOfGuests);

                if (bookedGuests + reservation.NumberOfGuests > sitting.Capacity)
                {
                    errors.Add($"The selected sitting does not have enough capacity. {sitting.Capacity - bookedGuests} guest places are available.");
                }
            }

            var tableIds = selectedTableIds?.Distinct().ToList() ?? new List<int>();
            if (!tableIds.Any())
            {
                // Online reservations do not need tables assigned straight away.
                return errors;
            }

            var tables = await _context.RestaurantTables
                .Include(t => t.Area)
                .Where(t => tableIds.Contains(t.RestaurantTableId))
                .ToListAsync();

            if (tables.Count != tableIds.Count)
            {
                errors.Add("One or more selected tables could not be found.");
                return errors;
            }

            if (tables.Any(t => !t.IsAvailable))
            {
                errors.Add("One or more selected tables are marked as unavailable.");
            }

            var reservationEndTime = reservation.EndTime;
            // Times overlap when another booking starts before this one ends and ends after this one starts.
            var conflictingTableCodes = await _context.ReservationTables
                .Where(rt =>
                    tableIds.Contains(rt.RestaurantTableId) &&
                    rt.ReservationId != reservation.ReservationId &&
                    rt.Reservation != null &&
                    rt.Reservation.Status != ReservationStatus.Cancelled &&
                    rt.Reservation.StartTime < reservationEndTime &&
                    rt.Reservation.StartTime.AddMinutes(rt.Reservation.DurationMinutes) > reservation.StartTime)
                .Select(rt => rt.RestaurantTable!.TableCode)
                .Distinct()
                .OrderBy(tableCode => tableCode)
                .ToListAsync();

            if (conflictingTableCodes.Any())
            {
                errors.Add($"The following tables are already assigned during that time: {string.Join(", ", conflictingTableCodes)}.");
            }

            return errors;
        }

        public async Task SendTableAssignmentUpdatedEmailAsync(int reservationId)
        {
            var reservation = await _context.Reservations.FindAsync(reservationId)
                ?? throw new InvalidOperationException("Reservation was not found.");

            await _emailSender.SendEmailAsync(
                reservation.Email,
                "Reservation table updated",
                await BuildReservationEmailAsync(
                    reservationId,
                    "Reservation table updated",
                    "Your assigned table has been updated.",
                    reservation.Status.ToString())
            );
        }

        public async Task SendReminderEmailAsync(int reservationId)
        {
            var reservation = await _context.Reservations.FindAsync(reservationId)
                ?? throw new InvalidOperationException("Reservation was not found.");

            if (!CanSendReminder(reservation))
            {
                throw new InvalidOperationException("Reminder emails can only be sent for pending or confirmed future reservations.");
            }

            await SendReminderEmailForReservationAsync(reservation, "Reservation reminder", "This is a reminder for your upcoming reservation.");
        }

        public async Task SendDueReminderEmailsAsync()
        {
            var now = DateTime.Now;
            var today = now.Date;
            var tomorrow = today.AddDays(1);

            var reservations = await _context.Reservations
                .Where(r =>
                    (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed) &&
                    r.StartTime > now &&
                    (
                        (r.StartTime >= today && r.StartTime < tomorrow && r.ReminderOnDaySentAt == null) ||
                        (r.StartTime <= now.AddHours(2) && r.ReminderTwoHoursSentAt == null)
                    ))
                .OrderBy(r => r.StartTime)
                .ToListAsync();

            foreach (var reservation in reservations)
            {
                // The two-hour reminder also counts as the on-day reminder.
                if (reservation.StartTime <= DateTime.Now.AddHours(2) && reservation.ReminderTwoHoursSentAt == null)
                {
                    await SendReminderEmailForReservationAsync(
                        reservation,
                        "Reservation in 2 hours",
                        "This is a reminder that your reservation is coming up soon.");
                    reservation.ReminderTwoHoursSentAt = DateTime.Now;
                    reservation.ReminderOnDaySentAt ??= DateTime.Now;
                    await _context.SaveChangesAsync();
                    continue;
                }

                if (reservation.StartTime.Date == today && reservation.ReminderOnDaySentAt == null)
                {
                    await SendReminderEmailForReservationAsync(
                        reservation,
                        "Reservation today",
                        "This is a reminder that your reservation is today.");
                    reservation.ReminderOnDaySentAt = DateTime.Now;
                    await _context.SaveChangesAsync();
                }
            }
        }

        private async Task<string> BuildReservationEmailAsync(int reservationId, string title, string message, string badgeText)
        {
            var details = await GetReservationEmailDetailsAsync(reservationId);
            var notesRow = string.IsNullOrWhiteSpace(details.Notes)
                ? string.Empty
                : BuildDetailRow("Notes", details.Notes);
            var cancelButton = BuildCancelButton(details.CancellationUrl);

            return $@"
<!doctype html>
<html>
<body style=""margin:0;padding:0;background:#f4f1ec;font-family:Arial,Helvetica,sans-serif;color:#2d2926;"">
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background:#f4f1ec;padding:28px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""max-width:640px;background:#ffffff;border:1px solid #ded7cf;border-radius:8px;overflow:hidden;"">
          <tr>
            <td style=""background:#2f4f4f;padding:22px 28px;color:#ffffff;"">
              <div style=""font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#d7e5df;"">Bean Scene</div>
              <div style=""font-size:26px;font-weight:700;margin-top:6px;"">{Html(title)}</div>
            </td>
          </tr>
          <tr>
            <td style=""padding:26px 28px 8px;"">
              <span style=""display:inline-block;background:#e6f0eb;color:#244640;border:1px solid #c8ded4;border-radius:999px;padding:6px 12px;font-size:13px;font-weight:700;"">{Html(badgeText)}</span>
              <p style=""font-size:16px;line-height:1.55;margin:18px 0 0;"">{Html(message)}</p>
            </td>
          </tr>
          <tr>
            <td style=""padding:16px 28px 28px;"">
              <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""border-collapse:collapse;border:1px solid #e5ded6;border-radius:6px;overflow:hidden;"">
                {BuildDetailRow("Reservation number", details.ReservationId.ToString())}
                {BuildDetailRow("Guest", details.GuestName)}
                {BuildDetailRow("Date", details.Date)}
                {BuildDetailRow("Time", details.Time)}
                {BuildDetailRow("Duration", details.Duration)}
                {BuildDetailRow("Guests", details.NumberOfGuests.ToString())}
                {BuildDetailRow("Sitting", details.Sitting)}
                {BuildDetailRow("Assigned table(s)", details.AssignedTables)}
                {notesRow}
              </table>
              {cancelButton}
              <p style=""font-size:13px;line-height:1.5;color:#6c625c;margin:18px 0 0;"">If you need to change your reservation, please contact store staff.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
        }

        private async Task<ReservationEmailDetails> GetReservationEmailDetailsAsync(int reservationId)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Sitting)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId)
                ?? throw new InvalidOperationException("Reservation was not found.");

            var tableCodes = await GetAssignedTableCodesAsync(reservationId);

            return new ReservationEmailDetails
            {
                ReservationId = reservation.ReservationId,
                GuestName = reservation.GuestFullName,
                Date = reservation.StartTime.ToString("dddd, d MMMM yyyy"),
                Time = reservation.StartTime.ToString("h:mm tt"),
                Duration = $"{reservation.DurationMinutes} minutes",
                NumberOfGuests = reservation.NumberOfGuests,
                Sitting = reservation.Sitting?.SittingType.ToString() ?? "Not assigned",
                AssignedTables = tableCodes.Any() ? string.Join(", ", tableCodes) : "To be assigned",
                Notes = reservation.Notes,
                CancellationUrl = BuildCancellationUrl(reservation)
            };
        }

        private string? BuildCancellationUrl(Reservation reservation)
        {
            if (!CanCancelByEmail(reservation))
            {
                return null;
            }

            // Include the email in the token so copied links cannot be reused for another booking.
            var expiresAt = DateTimeOffset.UtcNow.AddHours(ReservationCancellationToken.ExpiryHours).ToUnixTimeSeconds();
            var token = _cancellationTokenProtector.Protect($"{reservation.ReservationId}|{reservation.Email}|{expiresAt}");
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            return _linkGenerator.GetUriByAction(
                httpContext,
                action: "Cancel",
                controller: "Reservations",
                values: new { token });
        }

        private static bool CanCancelByEmail(Reservation reservation)
        {
            return reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed
                && reservation.StartTime.Date > DateTime.Today;
        }

        private async Task SendReminderEmailForReservationAsync(Reservation reservation, string subject, string message)
        {
            await _emailSender.SendEmailAsync(
                reservation.Email,
                subject,
                await BuildReservationEmailAsync(
                    reservation.ReservationId,
                    subject,
                    message,
                    reservation.Status.ToString())
            );
        }

        private static bool CanSendReminder(Reservation reservation)
        {
            return reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed
                && reservation.StartTime > DateTime.Now;
        }

        private static bool IsValidStatusTransition(ReservationStatus oldStatus, ReservationStatus newStatus)
        {
            return ReservationStatusWorkflow.CanTransition(oldStatus, newStatus);
        }

        private async Task<List<string>> GetAssignedTableCodesAsync(int reservationId)
        {
            return await _context.ReservationTables
                .Where(rt => rt.ReservationId == reservationId)
                .Join(
                    _context.RestaurantTables,
                    rt => rt.RestaurantTableId,
                    table => table.RestaurantTableId,
                    (rt, table) => table.TableCode)
                .OrderBy(tableCode => tableCode)
                .ToListAsync();
        }

        private static string BuildDetailRow(string label, string? value)
        {
            return $@"
                <tr>
                  <td style=""width:38%;padding:12px 14px;border-bottom:1px solid #e9e2db;background:#faf8f5;font-size:13px;color:#6c625c;font-weight:700;"">{Html(label)}</td>
                  <td style=""padding:12px 14px;border-bottom:1px solid #e9e2db;font-size:14px;color:#2d2926;"">{Html(value ?? string.Empty)}</td>
                </tr>";
        }

        private static string BuildCancelButton(string? cancellationUrl)
        {
            if (string.IsNullOrWhiteSpace(cancellationUrl))
            {
                return string.Empty;
            }

            return $@"
              <div style=""text-align:center;margin:24px 0 4px;"">
                <a href=""{Html(cancellationUrl)}"" style=""display:inline-block;background:#8f2f2f;color:#ffffff;text-decoration:none;border-radius:6px;padding:12px 18px;font-size:14px;font-weight:700;"">Cancel reservation</a>
              </div>";
        }

        private static string Html(string value)
        {
            // Emails are HTML, so encode all dynamic values before they go into the template.
            return WebUtility.HtmlEncode(value);
        }

        public IEnumerable<Reservation> FilterByStatus(IEnumerable<Reservation> reservations, ReservationStatus status)
        {
            return reservations.Where(r => r.Status == status);
        }

        public Dictionary<ReservationStatus, List<Reservation>> GroupByStatus(IEnumerable<Reservation> reservations)
        {
            return reservations.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.ToList());
        }

        private sealed class ReservationEmailDetails
        {
            public int ReservationId { get; set; }
            public string GuestName { get; set; } = string.Empty;
            public string Date { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
            public string Duration { get; set; } = string.Empty;
            public int NumberOfGuests { get; set; }
            public string Sitting { get; set; } = string.Empty;
            public string AssignedTables { get; set; } = string.Empty;
            public string? Notes { get; set; }
            public string? CancellationUrl { get; set; }
        }
    }
}
