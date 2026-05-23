using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class Member
    {
        public int MemberId { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        public User? User { get; set; }

        [Required]
        public DateTime JoinDate { get; set; } = DateTime.Now;

        // Guest reservations can later be attached to this member by matching confirmed email.
        public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    }
}
