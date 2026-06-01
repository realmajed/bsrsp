using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class Reservation
    {
        public int ReservationId { get; set; }

        [Required, StringLength(50), Display(Name = "Guest First Name")]
        public string GuestFirstName { get; set; } = string.Empty;

        [Required, StringLength(50), Display(Name = "Guest Last Name")]
        public string GuestLastName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required, Phone, StringLength(20)]
        public string Phone { get; set; } = string.Empty;

        [Required, Display(Name = "Sitting")]
        public int SittingId { get; set; }
        public Sitting? Sitting { get; set; }

        public int? MemberId { get; set; }
        public Member? Member { get; set; }
        public string? CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        [Required, Display(Name = "Reservation Start Time")]
        public DateTime StartTime { get; set; }

        [Required, Range(15, 360), Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; } = 90;

        [Required, Range(1, 50), Display(Name = "Number of Guests")]
        public int NumberOfGuests { get; set; } = 1;

        [Required, Display(Name = "Reservation Source")]
        public ReservationSource Source { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }

        [Required]
        public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

        public DateTime? ReminderOnDaySentAt { get; set; }

        public DateTime? ReminderTwoHoursSentAt { get; set; }

        // Many-to-many link so staff can assign one or more physical tables to a booking.
        public ICollection<ReservationTable> ReservationTables { get; set; } = new List<ReservationTable>();
        public ICollection<ReservationStatusHistory> StatusHistory { get; set; } = new List<ReservationStatusHistory>();

        public string GuestFullName => $"{GuestFirstName} {GuestLastName}";

        public DateTime EndTime => StartTime.AddMinutes(DurationMinutes);

        public static class StatusOptions
        {
            public const string Pending = "Pending";
            public const string Confirmed = "Confirmed";
            public const string Cancelled = "Cancelled";
            public const string Seated = "Seated";
            public const string Completed = "Completed";
        }

        public void ChangeStatus(ReservationStatus newStatus)
        {
            Status = newStatus;
        }
    }
}
