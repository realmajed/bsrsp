namespace BeanSceneReservationSystemProject.Models
{
        public enum UserRole { Owner, Manager, Staff, Member }
        public enum SittingType { Breakfast, Lunch, Dinner, SpecialEvent }
        public enum ReservationSource { Online, Mobile, InPerson, Email, Phone }
        public enum ReservationStatus { Pending, Confirmed, Cancelled, Seated, Completed }
}
