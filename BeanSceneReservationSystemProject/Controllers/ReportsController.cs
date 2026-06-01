using BeanSceneReservationSystemProject.Models;
using BeanSceneReservationSystemProject.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeanSceneReservationSystemProject.Controllers
{
    [Authorize(Roles = "Owner,Manager")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var reservations = await _context.Reservations
                .Include(r => r.CreatedByUser)
                .Include(r => r.Sitting)
                .Include(r => r.StatusHistory)
                .Include(r => r.ReservationTables)
                .ThenInclude(rt => rt.RestaurantTable)
                .ThenInclude(t => t!.Area)
                .AsNoTracking()
                .ToListAsync();

            var futureReservations = reservations
                .Where(r => r.StartTime >= today && r.Status != ReservationStatus.Cancelled)
                .OrderBy(r => r.StartTime)
                .ToList();

            var activeDayRows = BuildActiveDayRows(reservations);
            var mostActiveDay = activeDayRows
                .OrderByDescending(r => r.Count)
                .FirstOrDefault();

            var bookingChannelRows = BuildBookingChannelRows(reservations);
            var confirmationTimeRows = BuildConfirmationTimeRows(reservations);
            var averageConfirmationMinutes = GetAverageConfirmationMinutes(reservations);

            var viewModel = new ReportsViewModel
            {
                Today = today,
                FutureReservationCount = futureReservations.Count,
                FutureGuestCount = futureReservations.Sum(r => r.NumberOfGuests),
                MostActiveDayName = mostActiveDay?.Label ?? "No bookings yet",
                MostActiveDayCount = mostActiveDay?.Count ?? 0,
                ManualStaffBookings = bookingChannelRows.FirstOrDefault(r => r.Label == "Manual staff")?.Count ?? 0,
                MemberBookings = bookingChannelRows.FirstOrDefault(r => r.Label == "Members")?.Count ?? 0,
                GuestOnlineBookings = bookingChannelRows.FirstOrDefault(r => r.Label == "Guests")?.Count ?? 0,
                AveragePendingToConfirmedTime = FormatDuration(averageConfirmationMinutes),
                FutureReservations = futureReservations
                    .Take(25)
                    .Select(BuildFutureReservationRow)
                    .ToList(),
                ActiveDayRows = activeDayRows,
                BookingChannelRows = bookingChannelRows,
                ConfirmationTimeRows = confirmationTimeRows,
                MostSeatedTables = BuildMostSeatedTableRows(reservations)
            };

            return View(viewModel);
        }

        private static FutureReservationRow BuildFutureReservationRow(Reservation reservation)
        {
            var tableCodes = reservation.ReservationTables
                .Select(rt => rt.RestaurantTable?.TableCode)
                .Where(tableCode => !string.IsNullOrWhiteSpace(tableCode))
                .OrderBy(tableCode => tableCode)
                .ToList();

            return new FutureReservationRow
            {
                ReservationId = reservation.ReservationId,
                StartTime = reservation.StartTime,
                GuestName = reservation.GuestFullName,
                NumberOfGuests = reservation.NumberOfGuests,
                Status = reservation.Status,
                Source = reservation.Source,
                Sitting = reservation.Sitting?.SittingType.ToString() ?? "Not assigned",
                Tables = tableCodes.Any() ? string.Join(", ", tableCodes) : "To be assigned"
            };
        }

        private static List<ChartRow> BuildActiveDayRows(IEnumerable<Reservation> reservations)
        {
            var dayOrder = new[]
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday,
                DayOfWeek.Saturday,
                DayOfWeek.Sunday
            };

            var groupedReservations = reservations
                .Where(r => r.Status != ReservationStatus.Cancelled)
                .GroupBy(r => r.StartTime.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Count());
            var maxCount = groupedReservations.Values.DefaultIfEmpty(0).Max();

            return dayOrder
                .Select(day =>
                {
                    var count = groupedReservations.GetValueOrDefault(day);
                    return new ChartRow
                    {
                        Label = day.ToString(),
                        Count = count,
                        Percentage = maxCount == 0 ? 0 : Math.Round((decimal)count / maxCount * 100, 1)
                    };
                })
                .ToList();
        }

        private static List<ChartRow> BuildBookingChannelRows(IEnumerable<Reservation> reservations)
        {
            var rows = new List<ChartRow>
            {
                new()
                {
                    Label = "Manual staff",
                    Count = reservations.Count(IsManualStaffBooking)
                },
                new()
                {
                    Label = "Members",
                    Count = reservations.Count(r => r.CreatedByUser?.Role == UserRole.Member)
                },
                new()
                {
                    Label = "Guests",
                    Count = reservations.Count(r => r.CreatedByUserId == null)
                }
            };
            var maxCount = rows.Select(r => r.Count).DefaultIfEmpty(0).Max();

            foreach (var row in rows)
            {
                row.Percentage = maxCount == 0 ? 0 : Math.Round((decimal)row.Count / maxCount * 100, 1);
            }

            return rows;
        }

        private static bool IsManualStaffBooking(Reservation reservation)
        {
            return reservation.CreatedByUser?.Role is UserRole.Owner or UserRole.Manager or UserRole.Staff;
        }

        private static List<ChartRow> BuildConfirmationTimeRows(IEnumerable<Reservation> reservations)
        {
            var confirmedDurations = reservations
                .Select(GetPendingToConfirmedDuration)
                .Where(duration => duration != null)
                .Select(duration => duration!.Value.TotalMinutes)
                .ToList();

            var rows = new List<ChartRow>
            {
                new()
                {
                    Label = "Under 15 min",
                    Count = confirmedDurations.Count(minutes => minutes < 15)
                },
                new()
                {
                    Label = "15-60 min",
                    Count = confirmedDurations.Count(minutes => minutes >= 15 && minutes < 60)
                },
                new()
                {
                    Label = "1-4 hours",
                    Count = confirmedDurations.Count(minutes => minutes >= 60 && minutes < 240)
                },
                new()
                {
                    Label = "Over 4 hours",
                    Count = confirmedDurations.Count(minutes => minutes >= 240)
                }
            };
            var maxCount = rows.Select(r => r.Count).DefaultIfEmpty(0).Max();

            foreach (var row in rows)
            {
                row.Percentage = maxCount == 0 ? 0 : Math.Round((decimal)row.Count / maxCount * 100, 1);
            }

            return rows;
        }

        private static double? GetAverageConfirmationMinutes(IEnumerable<Reservation> reservations)
        {
            var durations = reservations
                .Select(GetPendingToConfirmedDuration)
                .Where(duration => duration != null)
                .Select(duration => duration!.Value.TotalMinutes)
                .ToList();

            return durations.Any() ? durations.Average() : null;
        }

        private static TimeSpan? GetPendingToConfirmedDuration(Reservation reservation)
        {
            var pendingHistory = reservation.StatusHistory
                .Where(h => h.NewStatus == ReservationStatus.Pending)
                .OrderBy(h => h.ChangedDate)
                .FirstOrDefault();
            if (pendingHistory == null)
            {
                return null;
            }

            var confirmedHistory = reservation.StatusHistory
                .Where(h => h.NewStatus == ReservationStatus.Confirmed && h.ChangedDate >= pendingHistory.ChangedDate)
                .OrderBy(h => h.ChangedDate)
                .FirstOrDefault();

            return confirmedHistory == null
                ? null
                : confirmedHistory.ChangedDate - pendingHistory.ChangedDate;
        }

        private static string FormatDuration(double? totalMinutes)
        {
            if (totalMinutes == null)
            {
                return "No confirmations yet";
            }

            var duration = TimeSpan.FromMinutes(totalMinutes.Value);
            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            }

            return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))}m";
        }

        private static List<TableUsageRow> BuildMostSeatedTableRows(IEnumerable<Reservation> reservations)
        {
            return reservations
                .Where(r => r.Status is ReservationStatus.Seated or ReservationStatus.Completed)
                .SelectMany(r => r.ReservationTables.Select(rt => new { Reservation = r, ReservationTable = rt }))
                .Where(x => x.ReservationTable.RestaurantTable != null)
                .GroupBy(x => x.ReservationTable.RestaurantTable!)
                .Select(g => new TableUsageRow
                {
                    TableCode = g.Key.TableCode,
                    AreaName = g.Key.Area?.AreaName ?? "Unassigned",
                    SeatedCount = g.Count(),
                    GuestCount = g.Sum(x => x.Reservation.NumberOfGuests)
                })
                .OrderByDescending(row => row.SeatedCount)
                .ThenByDescending(row => row.GuestCount)
                .ThenBy(row => row.TableCode)
                .Take(10)
                .ToList();
        }
    }
}
