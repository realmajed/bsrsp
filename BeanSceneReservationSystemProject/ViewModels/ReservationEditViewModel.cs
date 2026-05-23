using System.ComponentModel.DataAnnotations;
using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.ViewModels
{
    public class ReservationEditViewModel
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

        [Required, Display(Name = "Reservation Start Time")]
        public DateTime StartTime { get; set; }

        [Required, Range(15, 360), Display(Name = "Duration (minutes)")]
        public int DurationMinutes { get; set; } = 90;

        [Required, Range(1, 50), Display(Name = "Number of Guests")]
        public int NumberOfGuests { get; set; }

        [Required]
        public ReservationStatus Status { get; set; }

        [Required, Display(Name = "Reservation Source")]
        public ReservationSource Source { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public byte[]? RowVersion { get; set; }

        [Display(Name = "Assign Tables")]
        public List<int> SelectedTableIds { get; set; } = new();
    }
}
