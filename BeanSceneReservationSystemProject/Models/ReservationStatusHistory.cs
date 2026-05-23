using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class ReservationStatusHistory
    {
        public int ReservationStatusHistoryId { get; set; }

        [Required]
        public int ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        public ReservationStatus? OldStatus { get; set; }

        [Required]
        public ReservationStatus NewStatus { get; set; }

        [Required]
        public DateTime ChangedDate { get; set; } = DateTime.Now;

        public string? ChangedByUserId { get; set; }
        public User? ChangedByUser { get; set; }
    }
}
