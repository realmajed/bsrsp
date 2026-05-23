namespace BeanSceneReservationSystemProject.Services
{
    public static class ReservationCancellationToken
    {
        // Purpose keeps these tokens separate from anything else protected by Data Protection.
        public const string Purpose = "BeanScene.Reservation.EmailCancellation.v1";
        public const int ExpiryHours = 72;
    }
}
