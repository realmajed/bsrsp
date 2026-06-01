using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.ViewModels
{
    public class ReportsViewModel
    {
        public DateTime Today { get; set; }

        public int FutureReservationCount { get; set; }

        public int FutureGuestCount { get; set; }

        public string MostActiveDayName { get; set; } = "No bookings yet";

        public int MostActiveDayCount { get; set; }

        public int ManualStaffBookings { get; set; }

        public int MemberBookings { get; set; }

        public int GuestOnlineBookings { get; set; }

        public string AveragePendingToConfirmedTime { get; set; } = "No confirmations yet";

        public List<FutureReservationRow> FutureReservations { get; set; } = new();

        public List<ChartRow> ActiveDayRows { get; set; } = new();

        public List<ChartRow> BookingChannelRows { get; set; } = new();

        public List<ChartRow> ConfirmationTimeRows { get; set; } = new();

        public List<TableUsageRow> MostSeatedTables { get; set; } = new();
    }

    public class FutureReservationRow
    {
        public int ReservationId { get; set; }

        public DateTime StartTime { get; set; }

        public string GuestName { get; set; } = string.Empty;

        public int NumberOfGuests { get; set; }

        public ReservationStatus Status { get; set; }

        public ReservationSource Source { get; set; }

        public string Sitting { get; set; } = string.Empty;

        public string Tables { get; set; } = string.Empty;
    }

    public class ChartRow
    {
        public string Label { get; set; } = string.Empty;

        public int Count { get; set; }

        public decimal Percentage { get; set; }
    }

    public class TableUsageRow
    {
        public string TableCode { get; set; } = string.Empty;

        public string AreaName { get; set; } = string.Empty;

        public int SeatedCount { get; set; }

        public int GuestCount { get; set; }
    }
}
