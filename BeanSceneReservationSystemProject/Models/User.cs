using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class User : IdentityUser
    {
        [Required, StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        // Commented out due to UI.Identity already having Email and PhoneNumber so User inherits it.
        //[Required, EmailAddress, StringLength(100)]
        //public string Email { get; set; } = string.Empty;

        //[Required, Phone, StringLength(20)]
        //public string PhoneNumber { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.Member;

        [StringLength(260)]
        public string? ProfilePicturePath { get; set; }

        public Member? MemberProfile { get; set; }
        public ICollection<ReservationStatusHistory> StatusChanges { get; set; } = new List<ReservationStatusHistory>();
        public ICollection<Reservation> CreatedReservations { get; set; } = new List<Reservation>();

        public string FullName => $"{FirstName} {LastName}";
    }
}
