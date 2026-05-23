using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class Sitting
    {
        public int SittingId { get; set; }

        [Required]
        public SittingType SittingType { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; }

        [Required]
        public DateTime EndDateTime { get; set; }

        [Required, Range(1, 200)]
        public int Capacity { get; set; }

        public bool IsClosed { get; set; }

        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

        // Cancelled reservations do not take up sitting capacity anymore.
        public int GuestsBooked => Reservations
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .Sum(r => r.NumberOfGuests);

        public bool HasAvailableCapacity(int requestedGuests)
        {
            return !IsClosed && GuestsBooked + requestedGuests <= Capacity;
        }
    }
}
