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
                reservation.CreatedByUserId = createdByUserId;
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

            if (newStatus == ReservationStatus.Seated && (DateTime.Now < reservation.StartTime || DateTime.Now > reservation.EndTime))
            {
                // Cant be seated if outside reservation time.
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

            if (!sitting.ContainsReservation(reservation.StartTime, reservation.EndTime))
            {
                errors.Add($"The reservation must fit within the sitting's daily time window ({sitting.StartDateTime:h:mm tt} - {sitting.EndDateTime:h:mm tt}).");
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
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <meta name=""color-scheme"" content=""light"">
  <meta name=""supported-color-schemes"" content=""light"">
  <title>{Html(title)} - Bean Scene</title>
  <!--[if mso]>
  <style type=""text/css"">
    table {{ border-collapse: collapse; }}
    td {{ font-family: Arial, sans-serif; }}
  </style>
  <![endif]-->
</head>
<body style=""margin: 0; padding: 0; background-color: #E0E0E0; font-family: 'Open Sans', Arial, Helvetica, sans-serif; color: #083944; -webkit-font-smoothing: antialiased;"">
  <!-- Preheader text (hidden) -->
  <div style=""display: none; max-height: 0; overflow: hidden;"">
    {Html(message)}
    &nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;
  </div>
  
  <!-- Email wrapper -->
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #E0E0E0; padding: 32px 16px;"">
    <tr>
      <td align=""center"">
        <!-- Main container -->
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width: 600px; background-color: #FFFFFF; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 8px rgba(8, 57, 68, 0.08);"">
          
          <!-- Header -->
          <tr>
            <td style=""background-color: #083944; padding: 28px 32px; text-align: center;"">
              <!-- Logo placeholder - replace with actual logo -->
              <div style=""font-family: 'Tangerine', Georgia, serif; font-size: 36px; color: #FFFFFF; margin-bottom: 8px;"">Bean Scene</div>
              <div style=""font-size: 24px; font-weight: 300; color: #4AA1B5; letter-spacing: 0.5px;"">{Html(title)}</div>
            </td>
          </tr>
          
          <!-- Status Badge & Message -->
          <tr>
            <td style=""padding: 28px 32px 16px;"">
              <!-- Status Badge -->
              <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"">
                <tr>
                  <td style=""background-color: #F8E8B5; color: #083944; border: 1px solid #EBC136; border-radius: 24px; padding: 8px 16px; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;"">
                    {Html(badgeText)}
                  </td>
                </tr>
              </table>
              <!-- Message -->
              <p style=""font-size: 16px; line-height: 1.6; color: #083944; margin: 20px 0 0;"">{Html(message)}</p>
            </td>
          </tr>
          
          <!-- Reservation Details Card -->
          <tr>
            <td style=""padding: 0 32px 28px;"">
              <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #FFFFFF; border: 1px solid #E0E0E0; border-radius: 8px; overflow: hidden;"">
                <!-- Card Header -->
                <tr>
                  <td colspan=""2"" style=""background-color: #2F6672; padding: 14px 20px;"">
                    <span style=""font-size: 14px; font-weight: 600; color: #FFFFFF; text-transform: uppercase; letter-spacing: 0.5px;"">Reservation Details</span>
                  </td>
                </tr>
                <!-- Reservation Number -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; width: 40%; font-size: 14px; color: #2F6672; font-weight: 600;"">Reservation #</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{details.ReservationId}</td>
                </tr>
                <!-- Guest -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Guest</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.GuestName)}</td>
                </tr>
                <!-- Date -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Date</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.Date)}</td>
                </tr>
                <!-- Time -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Time</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.Time)}</td>
                </tr>
                <!-- Duration -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Duration</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.Duration)}</td>
                </tr>
                <!-- Number of Guests -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Guests</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{details.NumberOfGuests}</td>
                </tr>
                <!-- Sitting -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Sitting</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.Sitting)}</td>
                </tr>
                <!-- Assigned Tables -->
                <tr>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #2F6672; font-weight: 600;"">Table(s)</td>
                  <td style=""padding: 14px 20px; border-bottom: 1px solid #E0E0E0; font-size: 14px; color: #083944;"">{Html(details.AssignedTables)}</td>
                </tr>
                <!-- Notes (optional - include row or empty) -->
                {notesRow}
              </table>
            </td>
          </tr>
          
          <!-- Cancel Button (optional) -->
          {cancelButton}
          
          <!-- Help Text -->
          <tr>
            <td style=""padding: 0 32px 28px;"">
              <p style=""font-size: 14px; line-height: 1.6; color: #2F6672; margin: 0;"">
                Need to make changes? Please contact our staff at <a href=""mailto:reservations@beanscene.com"" style=""color: #4AA1B5; text-decoration: underline;"">reservations@beanscene.com</a> or call us directly.
              </p>
            </td>
          </tr>
          
          <!-- Divider -->
          <tr>
            <td style=""padding: 0 32px;"">
              <div style=""border-top: 1px solid #E0E0E0;""></div>
            </td>
          </tr>
          
          <!-- Footer -->
          <tr>
            <td style=""padding: 24px 32px; text-align: center;"">
              <p style=""font-size: 13px; color: #2F6672; margin: 0 0 8px;"">Bean Scene Coffee House</p>
              <p style=""font-size: 12px; color: #2F6672; margin: 0; opacity: 0.7;"">
                123 Coffee Lane, Melbourne VIC 3000<br>
                <a href=""tel:+61312345678"" style=""color: #4AA1B5; text-decoration: none;"">+61 3 1234 5678</a>
              </p>
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
              <tr>
    <td style=""padding: 0 32px 20px;"">
      <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"">
        <tr>
          <td style=""background-color: #4AA1B5; border-radius: 6px;"">
            <a href=""{Html(cancellationUrl)}"" style=""display: inline-block; padding: 14px 28px; font-size: 14px; font-weight: 600; color: #FFFFFF; text-decoration: none;"">Cancel Reservation</a>
          </td>
        </tr>
      </table>
    </td>
  </tr>";
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
