using BeanSceneReservationSystemProject.Models;
using BeanSceneReservationSystemProject.Services;
using BeanSceneReservationSystemProject.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeanSceneReservationSystemProject.Controllers
{
    [Authorize(Roles = "Owner,Manager,Staff,Member")]
    public class ReservationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IReservationService _reservationService;
        private readonly UserManager<User> _userManager;
        private readonly IDataProtector _cancellationTokenProtector;

        public ReservationsController(
            ApplicationDbContext context,
            IReservationService reservationService,
            UserManager<User> userManager,
            IDataProtectionProvider dataProtectionProvider)
        {
            _context = context;
            _reservationService = reservationService;
            _userManager = userManager;
            _cancellationTokenProtector = dataProtectionProvider.CreateProtector(ReservationCancellationToken.Purpose);
        }

        // GET: Reservations
        public async Task<IActionResult> Index(string? search, ReservationStatus? status, int? sittingId, DateTime? date)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.Role == UserRole.Member)
            {
                // Pick up older guest bookings once a member confirms the same email.
                await LinkGuestReservationsToMemberByEmailAsync(currentUser);
            }

            var reservationsQuery = _context.Reservations
                .Include(r => r.Member)
                .Include(r => r.Sitting)
                .Include(r => r.ReservationTables)
                .ThenInclude(rt => rt.RestaurantTable)
                .AsQueryable();

            if (currentUser?.Role == UserRole.Member)
            {
                // Members only see their own bookings, staff see the full reservation list.
                reservationsQuery = reservationsQuery.Where(r => r.Member != null && r.Member.UserId == currentUser.Id);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                reservationsQuery = reservationsQuery.Where(r =>
                    r.GuestFirstName.Contains(search) ||
                    r.GuestLastName.Contains(search) ||
                    r.Email.Contains(search) ||
                    r.Phone.Contains(search));
            }

            if (status.HasValue)
            {
                reservationsQuery = reservationsQuery.Where(r => r.Status == status.Value);
            }

            if (sittingId.HasValue)
            {
                reservationsQuery = reservationsQuery.Where(r => r.SittingId == sittingId.Value);
            }

            if (date.HasValue)
            {
                var startDate = date.Value.Date;
                var endDate = startDate.AddDays(1);
                reservationsQuery = reservationsQuery.Where(r => r.StartTime >= startDate && r.StartTime < endDate);
            }

            ViewData["Search"] = search;
            ViewData["Status"] = status?.ToString();
            ViewData["SittingIdFilter"] = BuildSittingSelectList(sittingId);
            ViewData["Date"] = date?.ToString("yyyy-MM-dd");
            ViewData["IsMemberReservationsList"] = currentUser?.Role == UserRole.Member;

            var reservations = await reservationsQuery
                .OrderBy(r => r.StartTime < DateTime.Today)
                .ThenBy(r => r.StartTime)
                .ToListAsync();

            return View(reservations);
        }

        // GET: Reservations/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Member)
                .Include(r => r.Sitting)
                .Include(r => r.ReservationTables)
                .ThenInclude(rt => rt.RestaurantTable)
                .FirstOrDefaultAsync(m => m.ReservationId == id);
            if (reservation == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var memberAccessResult = DenyMemberAccessIfNeeded(currentUser, reservation, allowOwnReservation: true);
            if (memberAccessResult != null)
            {
                return memberAccessResult;
            }

            return View(reservation);
        }

        // GET: Reservations/Create
        [AllowAnonymous]
        public async Task<IActionResult> Create()
        {
            LoadDropdowns();
            var reservation = new Reservation
            {
                StartTime = DateTime.Now.AddDays(1),
                DurationMinutes = 90,
                Source = ReservationSource.Online
            };

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.Role == UserRole.Member)
            {
                FillReservationFromMember(reservation, currentUser);
            }

            LoadCreateViewOptions(currentUser);
            return View(reservation);
        }

        // POST: Reservations/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Create(Reservation reservation, List<int>? selectedTableIds)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var isStaffUser = IsStaffUser(currentUser);

            if (!isStaffUser)
            {
                // Public and member bookings should not choose source or tables.
                reservation.Source = ReservationSource.Online;
                selectedTableIds = null;
            }

            if (currentUser?.Role == UserRole.Member)
            {
                // Use the member account details to fill in details quicker for a member.
                FillReservationFromMember(reservation, currentUser);
                ClearMemberContactValidation();

                reservation.MemberId = await _context.Members
                    .Where(m => m.UserId == currentUser.Id)
                    .Select(m => (int?)m.MemberId)
                    .FirstOrDefaultAsync();

                if (reservation.MemberId == null)
                {
                    ModelState.AddModelError(string.Empty, "Your member profile could not be found.");
                }
            }

            if (ModelState.IsValid)
            {
                AddReservationValidationErrors(await _reservationService.ValidateReservationAsync(reservation, selectedTableIds));
            }

            if (ModelState.IsValid)
            {
                await _reservationService.CreateReservationAsync(reservation, selectedTableIds, currentUser?.Id);
                TempData["SuccessMessage"] = "Reservation created.";
                return User.Identity?.IsAuthenticated == true
                    ? RedirectToAction(nameof(Index))
                    : RedirectToAction("Index", "Home");
            }
            LoadDropdowns(selectedTableIds);
            LoadCreateViewOptions(currentUser);
            return View(reservation);
        }

        // GET: Reservations/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Member)
                .Include(r => r.ReservationTables)
                .FirstOrDefaultAsync(r => r.ReservationId == id);
            if (reservation == null)
            {
                return NotFound();
            }

            // check if a member is trying to manage a reservation, even if its theirs - lets them callthe store to change.

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser!.Role == UserRole.Member && currentUser.Id == reservation.Member?.UserId)
            {
                TempData["ErrorMessage"] = "Members cannot modify their reservations, please contact store staff.";
                return RedirectToAction(nameof(Index));
            }

            if (currentUser!.Role == UserRole.Member)
            {
                TempData["ErrorMessage"] = "Members cannot modify reservations that are not theirs.";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new ReservationEditViewModel
            {
                ReservationId = reservation.ReservationId,
                GuestFirstName = reservation.GuestFirstName,
                GuestLastName = reservation.GuestLastName,
                Email = reservation.Email,
                Phone = reservation.Phone,
                SittingId = reservation.SittingId,
                StartTime = reservation.StartTime,
                DurationMinutes = reservation.DurationMinutes,
                NumberOfGuests = reservation.NumberOfGuests,
                Status = reservation.Status,
                Source = reservation.Source,
                Notes = reservation.Notes,
                RowVersion = reservation.RowVersion,
                SelectedTableIds = reservation.ReservationTables
                    .Select(rt => rt.RestaurantTableId)
                    .ToList()
            };

            LoadDropdowns(viewModel.SelectedTableIds);
            return View(viewModel);
        }

        // POST: Reservations/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ReservationEditViewModel viewModel)
        {
            if (id != viewModel.ReservationId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var reservation = await _context.Reservations
                        .Include(r => r.Member)
                        .Include(r => r.ReservationTables)
                        .FirstOrDefaultAsync(r => r.ReservationId == id);

                    if (reservation == null)
                    {
                        return NotFound();
                    }

                    var currentUser = await _userManager.GetUserAsync(User);
                    if (currentUser!.Role == UserRole.Member && currentUser.Id == reservation.Member?.UserId)
                    {
                        TempData["ErrorMessage"] = "Members cannot modify their reservations, please contact store staff.";
                        return RedirectToAction(nameof(Index));
                    }

                    if (currentUser!.Role == UserRole.Member)
                    {
                        TempData["ErrorMessage"] = "Members cannot modify reservations that are not theirs.";
                        return RedirectToAction(nameof(Index));
                    }

                    var oldStatus = reservation.Status;
                    var tableAssignmentChanged = HaveTableAssignmentsChanged(reservation, viewModel.SelectedTableIds);

                    // Validate the changed booking details before touching the tracked reservation.
                    var validationReservation = new Reservation
                    {
                        ReservationId = reservation.ReservationId,
                        SittingId = viewModel.SittingId,
                        StartTime = viewModel.StartTime,
                        DurationMinutes = viewModel.DurationMinutes,
                        NumberOfGuests = viewModel.NumberOfGuests,
                        Status = viewModel.Status
                    };
                    AddReservationValidationErrors(await _reservationService.ValidateReservationAsync(validationReservation, viewModel.SelectedTableIds));

                    if (oldStatus != viewModel.Status && !CanChangeStatus(oldStatus, viewModel.Status))
                    {
                        ModelState.AddModelError(nameof(viewModel.Status), $"Reservation status cannot be changed from {oldStatus} to {viewModel.Status}.");
                    }

                    if (!ModelState.IsValid)
                    {
                        LoadDropdowns(viewModel.SelectedTableIds);
                        return View(viewModel);
                    }

                    reservation.GuestFirstName = viewModel.GuestFirstName;
                    reservation.GuestLastName = viewModel.GuestLastName;
                    reservation.Email = viewModel.Email;
                    reservation.Phone = viewModel.Phone;
                    reservation.SittingId = viewModel.SittingId;
                    reservation.StartTime = viewModel.StartTime;
                    reservation.DurationMinutes = viewModel.DurationMinutes;
                    reservation.NumberOfGuests = viewModel.NumberOfGuests;
                    reservation.Source = viewModel.Source;
                    reservation.Notes = viewModel.Notes;

                    _context.Entry(reservation).Property(r => r.RowVersion).OriginalValue = viewModel.RowVersion;

                    UpdateReservationTables(reservation, viewModel.SelectedTableIds);
                    await _context.SaveChangesAsync();

                    if (oldStatus != viewModel.Status)
                    {
                        // Status changes go through the service so history and emails stay consistent.
                        await _reservationService.ChangeStatusAsync(id, viewModel.Status, currentUser.Id, tableAssignmentChanged);
                    }
                    else if (tableAssignmentChanged)
                    {
                        await _reservationService.SendTableAssignmentUpdatedEmailAsync(id);
                    }

                    TempData["SuccessMessage"] = "Reservation updated.";
                }
                // handly concurrency error - let them know someone beat them to the reservation update.
                catch (DbUpdateConcurrencyException)
                {
                    if (!ReservationExists(viewModel.ReservationId))
                    {
                        return NotFound();
                    }

                    ModelState.AddModelError(string.Empty,
                        "This reservation was changed by another user. Please reload the page and try again.");

                    LoadDropdowns(viewModel.SelectedTableIds);
                    return View(viewModel);
                }
                return RedirectToAction(nameof(Index));
            }
            LoadDropdowns(viewModel.SelectedTableIds);
            return View(viewModel);
        }

        // POST: Reservations/ChangeStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, ReservationStatus newStatus)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Member)
                .FirstOrDefaultAsync(r => r.ReservationId == id);

            if (reservation == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var memberAccessResult = DenyMemberAccessIfNeeded(currentUser, reservation, allowOwnReservation: false);
            if (memberAccessResult != null)
            {
                return memberAccessResult;
            }

            try
            {
                var statusChanged = await _reservationService.ChangeStatusAsync(id, newStatus, currentUser?.Id);
                if (statusChanged)
                {
                    TempData["SuccessMessage"] = $"Reservation status changed to {newStatus}.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Reservation is already {newStatus}.";
                }
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Owner,Manager,Staff")]
        public async Task<IActionResult> SendReminder(int id)
        {
            try
            {
                await _reservationService.SendReminderEmailAsync(id);
                TempData["SuccessMessage"] = "Reminder email sent.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Roles = "Owner,Manager,Staff")]
        public async Task<IActionResult> TableAvailability(int? reservationId, int sittingId, DateTime startTime, int durationMinutes, int numberOfGuests)
        {
            var tables = await _context.RestaurantTables
                .ToListAsync();
            tables = TableOrdering.OrderTables(tables);

            var sitting = await _context.Sittings
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.SittingId == sittingId);

            if (sitting == null)
            {
                return Json(new
                {
                    allUnavailable = true,
                    message = "Select a valid sitting first.",
                    tables = tables.Select(t => new
                    {
                        tableId = t.RestaurantTableId,
                        unavailable = true,
                        reason = "Invalid sitting"
                    })
                });
            }

            var reservationEndTime = startTime.AddMinutes(durationMinutes);
            var globalReason = GetSittingAvailabilityReason(sitting, reservationId, startTime, reservationEndTime, numberOfGuests);
            var unavailableTableReasons = new Dictionary<int, string>();

            foreach (var table in tables.Where(t => !t.IsAvailable))
            {
                unavailableTableReasons[table.RestaurantTableId] = "Marked unavailable";
            }

            if (globalReason == null)
            {
                var conflictingTables = await _context.ReservationTables
                    .Where(rt =>
                        rt.ReservationId != reservationId &&
                        rt.Reservation != null &&
                        rt.Reservation.Status != ReservationStatus.Cancelled &&
                        rt.Reservation.StartTime < reservationEndTime &&
                        rt.Reservation.StartTime.AddMinutes(rt.Reservation.DurationMinutes) > startTime)
                    .Select(rt => new
                    {
                        rt.RestaurantTableId,
                        rt.Reservation!.StartTime,
                        rt.Reservation.DurationMinutes
                    })
                    .ToListAsync();

                foreach (var conflict in conflictingTables)
                {
                    var endTime = conflict.StartTime.AddMinutes(conflict.DurationMinutes);
                    unavailableTableReasons[conflict.RestaurantTableId] = $"Booked {conflict.StartTime:h:mm tt}-{endTime:h:mm tt}";
                }
            }

            return Json(new
            {
                allUnavailable = globalReason != null,
                message = globalReason,
                tables = tables.Select(t =>
                {
                    var reason = globalReason ?? unavailableTableReasons.GetValueOrDefault(t.RestaurantTableId);
                    return new
                    {
                        tableId = t.RestaurantTableId,
                        unavailable = reason != null,
                        reason
                    };
                })
            });
        }

        // GET: Reservations/Cancel?token=...
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Cancel(string? token)
        {
            // Email cancellation links use a protected token so guests do not need to log in.
            var reservation = await GetReservationFromCancellationTokenAsync(token);
            if (reservation == null)
            {
                TempData["ErrorMessage"] = "The cancellation link is invalid or has expired.";
                return RedirectToAction("Index", "Home");
            }

            ViewData["CancellationToken"] = token;
            ViewData["CanCancel"] = CanCancelFromEmail(reservation);
            return View(reservation);
        }

        // POST: Reservations/Cancel
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> CancelConfirmed(string? token)
        {
            var reservation = await GetReservationFromCancellationTokenAsync(token);
            if (reservation == null)
            {
                TempData["ErrorMessage"] = "The cancellation link is invalid or has expired.";
                return RedirectToAction("Index", "Home");
            }

            if (!CanCancelFromEmail(reservation))
            {
                TempData["ErrorMessage"] = "This reservation can no longer be cancelled online.";
                return RedirectToAction("Index", "Home");
            }

            await _reservationService.ChangeStatusAsync(reservation.ReservationId, ReservationStatus.Cancelled);
            TempData["SuccessMessage"] = "Reservation cancelled.";
            return RedirectToAction("Index", "Home");
        }

        // GET: Reservations/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var reservation = await _context.Reservations
                .Include(r => r.Member)
                .FirstOrDefaultAsync(m => m.ReservationId == id);
            if (reservation == null)
            {
                return NotFound();
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var memberAccessResult = DenyMemberAccessIfNeeded(currentUser, reservation, allowOwnReservation: false);
            if (memberAccessResult != null)
            {
                return memberAccessResult;
            }

            return View(reservation);
        }

        // POST: Reservations/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var reservation = await _context.Reservations
                .Include(r => r.Member)
                .FirstOrDefaultAsync(r => r.ReservationId == id);
            if (reservation != null)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                var memberAccessResult = DenyMemberAccessIfNeeded(currentUser, reservation, allowOwnReservation: false);
                if (memberAccessResult != null)
                {
                    return memberAccessResult;
                }

                _context.Reservations.Remove(reservation);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Reservation deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        // CRUD

        [HttpGet]
        public async Task<IActionResult> ApiList()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.Role == UserRole.Member)
            {
                await LinkGuestReservationsToMemberByEmailAsync(currentUser);
            }

            var reservationsQuery = BuildReservationApiQuery();
            if (currentUser?.Role == UserRole.Member)
            {
                reservationsQuery = reservationsQuery.Where(r => r.Member != null && r.Member.UserId == currentUser.Id);
            }

            var reservations = await reservationsQuery
                .OrderBy(r => r.StartTime)
                .ToListAsync();

            return Json(reservations.Select(BuildReservationApiDto));
        }

        [HttpPost]
        public async Task<IActionResult> ApiCreate([FromBody] Reservation reservation)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var validationErrors = await _reservationService.ValidateReservationAsync(reservation);
            if (validationErrors.Any()) return BadRequest(validationErrors);

            var currentUser = await _userManager.GetUserAsync(User);
            await _reservationService.CreateReservationAsync(reservation, createdByUserId: currentUser?.Id);

            var createdReservation = await BuildReservationApiQuery()
                .FirstAsync(r => r.ReservationId == reservation.ReservationId);

            return CreatedAtAction(nameof(Details), new { id = reservation.ReservationId }, BuildReservationApiDto(createdReservation));
        }

        [HttpPut]
        public async Task<IActionResult> ApiUpdate(int id, [FromBody] Reservation reservation)
        {
            if (id != reservation.ReservationId) return BadRequest();
            var validationErrors = await _reservationService.ValidateReservationAsync(reservation);
            if (validationErrors.Any()) return BadRequest(validationErrors);

            var existingReservation = await _context.Reservations.FindAsync(id);
            if (existingReservation == null) return NotFound();

            var oldStatus = existingReservation.Status;
            if (oldStatus != reservation.Status && !CanChangeStatus(oldStatus, reservation.Status))
            {
                return BadRequest($"Reservation status cannot be changed from {oldStatus} to {reservation.Status}.");
            }

            existingReservation.GuestFirstName = reservation.GuestFirstName;
            existingReservation.GuestLastName = reservation.GuestLastName;
            existingReservation.Email = reservation.Email;
            existingReservation.Phone = reservation.Phone;
            existingReservation.SittingId = reservation.SittingId;
            existingReservation.MemberId = reservation.MemberId;
            existingReservation.StartTime = reservation.StartTime;
            existingReservation.DurationMinutes = reservation.DurationMinutes;
            existingReservation.NumberOfGuests = reservation.NumberOfGuests;
            existingReservation.Source = reservation.Source;
            existingReservation.Notes = reservation.Notes;
            await _context.SaveChangesAsync();

            if (oldStatus != reservation.Status)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                await _reservationService.ChangeStatusAsync(id, reservation.Status, currentUser?.Id);
            }

            var updatedReservation = await BuildReservationApiQuery()
                .FirstAsync(r => r.ReservationId == id);

            return Json(BuildReservationApiDto(updatedReservation));
        }

        [HttpDelete]
        public async Task<IActionResult> ApiDelete(int id)
        {
            var reservation = await _context.Reservations.FindAsync(id);
            if (reservation == null) return NotFound();
            _context.Reservations.Remove(reservation);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private bool ReservationExists(int id)
        {
            return _context.Reservations.Any(e => e.ReservationId == id);
        }

        private IQueryable<Reservation> BuildReservationApiQuery()
        {
            return _context.Reservations
                .Include(r => r.Sitting)
                .Include(r => r.Member)
                .ThenInclude(m => m!.User)
                .Include(r => r.ReservationTables)
                .ThenInclude(rt => rt.RestaurantTable)
                .ThenInclude(t => t!.Area)
                .Include(r => r.StatusHistory)
                .ThenInclude(h => h.ChangedByUser);
        }

        private static object BuildReservationApiDto(Reservation reservation)
        {
            return new
            {
                reservation.ReservationId,
                reservation.GuestFirstName,
                reservation.GuestLastName,
                reservation.Email,
                reservation.Phone,
                reservation.SittingId,
                Sitting = reservation.Sitting == null ? null : new
                {
                    reservation.Sitting.SittingId,
                    reservation.Sitting.SittingType,
                    reservation.Sitting.StartDateTime,
                    reservation.Sitting.EndDateTime,
                    reservation.Sitting.Capacity,
                    reservation.Sitting.IsClosed
                },
                reservation.MemberId,
                Member = reservation.Member == null ? null : new
                {
                    reservation.Member.MemberId,
                    reservation.Member.UserId,
                    reservation.Member.JoinDate,
                    User = reservation.Member.User == null ? null : new
                    {
                        reservation.Member.User.Id,
                        reservation.Member.User.FirstName,
                        reservation.Member.User.LastName,
                        reservation.Member.User.Email,
                        reservation.Member.User.PhoneNumber,
                        reservation.Member.User.Role,
                        reservation.Member.User.ProfilePicturePath,
                        reservation.Member.User.FullName
                    }
                },
                reservation.StartTime,
                reservation.DurationMinutes,
                reservation.NumberOfGuests,
                reservation.Source,
                reservation.Notes,
                reservation.Status,
                reservation.ReminderOnDaySentAt,
                reservation.ReminderTwoHoursSentAt,
                ReservationTables = reservation.ReservationTables.Select(rt => new
                {
                    rt.ReservationTableId,
                    rt.RestaurantTableId,
                    RestaurantTable = rt.RestaurantTable == null ? null : new
                    {
                        rt.RestaurantTable.RestaurantTableId,
                        rt.RestaurantTable.TableCode,
                        rt.RestaurantTable.Capacity,
                        rt.RestaurantTable.IsAvailable,
                        rt.RestaurantTable.AreaId,
                        Area = rt.RestaurantTable.Area == null ? null : new
                        {
                            rt.RestaurantTable.Area.AreaId,
                            rt.RestaurantTable.Area.AreaName
                        }
                    }
                }),
                StatusHistory = reservation.StatusHistory
                    .OrderBy(h => h.ChangedDate)
                    .Select(h => new
                    {
                        h.ReservationStatusHistoryId,
                        h.OldStatus,
                        h.NewStatus,
                        h.ChangedDate,
                        h.ChangedByUserId,
                        ChangedByUser = h.ChangedByUser == null ? null : new
                        {
                            h.ChangedByUser.Id,
                            h.ChangedByUser.FirstName,
                            h.ChangedByUser.LastName,
                            h.ChangedByUser.Email,
                            h.ChangedByUser.Role,
                            h.ChangedByUser.FullName
                        }
                    }),
                reservation.GuestFullName,
                reservation.EndTime
            };
        }

        private void LoadDropdowns(IEnumerable<int>? selectedTableIds = null)
        {
            var selectedIds = selectedTableIds?.Distinct().ToHashSet() ?? new HashSet<int>();
            var tables = _context.RestaurantTables
                .Include(t => t.Area)
                .ToList();
            tables = TableOrdering.OrderTables(tables);

            ViewData["SittingId"] = BuildSittingSelectList();
            ViewData["TablePickerTables"] = tables;
            ViewData["SelectedTableIdSet"] = selectedIds;
            ViewData["SelectedTableIds"] = new MultiSelectList(
                tables,
                "RestaurantTableId",
                "TableDisplayName",
                selectedIds);
        }

        private List<SelectListItem> BuildSittingSelectList(int? selectedSittingId = null)
        {
            return _context.Sittings
                .OrderBy(s => s.StartDateTime)
                .ToList()
                .Select(s => new SelectListItem
                {
                    Value = s.SittingId.ToString(),
                    Text = $"{s.SittingType} {s.StartDateTime:dd-MM-yyyy h:mm tt} - {s.EndDateTime:h:mm tt}" + (s.IsClosed ? " (Closed)" : string.Empty),
                    Selected = selectedSittingId.HasValue && s.SittingId == selectedSittingId.Value
                })
                .ToList();
        }

        private void UpdateReservationTables(Reservation reservation, List<int>? selectedTableIds)
        {
            var selectedIds = selectedTableIds?.Distinct().ToHashSet() ?? new HashSet<int>();
            var currentReservationTables = reservation.ReservationTables.ToList();

            // Remove table links staff unticked in the edit form.
            foreach (var reservationTable in currentReservationTables.Where(rt => !selectedIds.Contains(rt.RestaurantTableId)))
            {
                _context.ReservationTables.Remove(reservationTable);
            }

            // Add new links without duplicating the ones already on the reservation.
            var currentTableIds = currentReservationTables.Select(rt => rt.RestaurantTableId).ToHashSet();
            foreach (var tableId in selectedIds.Where(tableId => !currentTableIds.Contains(tableId)))
            {
                reservation.ReservationTables.Add(new ReservationTable
                {
                    ReservationId = reservation.ReservationId,
                    RestaurantTableId = tableId
                });
            }
        }

        private static bool HaveTableAssignmentsChanged(Reservation reservation, List<int>? selectedTableIds)
        {
            var selectedIds = selectedTableIds?.Distinct().ToHashSet() ?? new HashSet<int>();
            var currentTableIds = reservation.ReservationTables
                .Select(rt => rt.RestaurantTableId)
                .ToHashSet();

            return !currentTableIds.SetEquals(selectedIds);
        }

        private void AddReservationValidationErrors(IEnumerable<string> errors)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }

        private static bool CanChangeStatus(ReservationStatus oldStatus, ReservationStatus newStatus)
        {
            return oldStatus == newStatus || ReservationStatusWorkflow.CanTransition(oldStatus, newStatus);
        }

        private static string? GetSittingAvailabilityReason(Sitting sitting, int? reservationId, DateTime startTime, DateTime endTime, int numberOfGuests)
        {
            if (sitting.IsClosed)
            {
                return "Sitting closed";
            }

            if (startTime < sitting.StartDateTime || startTime >= sitting.EndDateTime || endTime > sitting.EndDateTime)
            {
                return "Outside sitting time";
            }

            var bookedGuests = sitting.Reservations
                .Where(r => r.ReservationId != reservationId && r.Status != ReservationStatus.Cancelled)
                .Sum(r => r.NumberOfGuests);

            if (numberOfGuests > 0 && bookedGuests + numberOfGuests > sitting.Capacity)
            {
                return $"Sitting full ({Math.Max(0, sitting.Capacity - bookedGuests)} places left)";
            }

            return null;
        }

        private static bool IsStaffUser(User? user)
        {
            return user?.Role is UserRole.Owner or UserRole.Manager or UserRole.Staff;
        }

        private void LoadCreateViewOptions(User? currentUser)
        {
            ViewData["IsMemberReservation"] = currentUser?.Role == UserRole.Member;
            ViewData["CanManageReservationSource"] = IsStaffUser(currentUser);
            ViewData["CanAssignTables"] = IsStaffUser(currentUser);
        }

        private static void FillReservationFromMember(Reservation reservation, User currentUser)
        {
            reservation.GuestFirstName = currentUser.FirstName;
            reservation.GuestLastName = currentUser.LastName;
            reservation.Email = currentUser.Email ?? string.Empty;
            reservation.Phone = currentUser.PhoneNumber ?? string.Empty;
            reservation.Source = ReservationSource.Online;
        }

        private void ClearMemberContactValidation()
        {
            // These fields are filled from the logged in member account.
            ModelState.Remove(nameof(Reservation.GuestFirstName));
            ModelState.Remove(nameof(Reservation.GuestLastName));
            ModelState.Remove(nameof(Reservation.Email));
            ModelState.Remove(nameof(Reservation.Phone));
            ModelState.Remove(nameof(Reservation.Source));
        }

        private IActionResult? DenyMemberAccessIfNeeded(User? currentUser, Reservation reservation, bool allowOwnReservation)
        {
            if (currentUser?.Role != UserRole.Member)
            {
                return null;
            }

            // Members can read their own booking, but edits/deletes/certain actions are staff only.
            var isOwnReservation = currentUser.Id == reservation.Member?.UserId;
            if (isOwnReservation && allowOwnReservation)
            {
                return null;
            }

            TempData["ErrorMessage"] = isOwnReservation
                ? "Members cannot modify their reservations, please contact store staff."
                : "Members cannot access reservations that are not theirs.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<Reservation?> GetReservationFromCancellationTokenAsync(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                // Token format is reservation id, email, and expiry timestamp.
                var parts = _cancellationTokenProtector.Unprotect(token).Split('|');
                if (parts.Length != 3 ||
                    !int.TryParse(parts[0], out var reservationId) ||
                    !long.TryParse(parts[2], out var expiresAt) ||
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
                {
                    return null;
                }

                var email = parts[1];
                var reservation = await _context.Reservations
                    .Include(r => r.Sitting)
                    .Include(r => r.ReservationTables)
                    .ThenInclude(rt => rt.RestaurantTable)
                    .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

                if (reservation == null || !string.Equals(reservation.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return reservation;
            }
            catch
            {
                return null;
            }
        }

        private static bool CanCancelFromEmail(Reservation reservation)
        {
            return reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed
                && reservation.StartTime.Date > DateTime.Today;
        }

        private async Task LinkGuestReservationsToMemberByEmailAsync(User currentUser)
        {
            if (!currentUser.EmailConfirmed || string.IsNullOrWhiteSpace(currentUser.Email))
            {
                return;
            }

            var memberId = await _context.Members
                .Where(m => m.UserId == currentUser.Id)
                .Select(m => (int?)m.MemberId)
                .FirstOrDefaultAsync();

            if (memberId == null)
            {
                return;
            }

            var guestReservations = await _context.Reservations
                .Where(r => r.MemberId == null && r.Email == currentUser.Email)
                .ToListAsync();

            if (!guestReservations.Any())
            {
                return;
            }

            foreach (var reservation in guestReservations)
            {
                reservation.MemberId = memberId;
            }

            await _context.SaveChangesAsync();
        }
    }
}
