using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.Services
{
    public interface IReservationService
    {
        Task<List<string>> ValidateReservationAsync(Reservation reservation, List<int>? selectedTableIds = null);

        Task<Reservation> CreateReservationAsync(Reservation reservation, List<int>? selectedTableIds = null, string? createdByUserId = null);

        Task<bool> ChangeStatusAsync(int reservationId, ReservationStatus newStatus, string? changedbyUserId = null, bool tableAssignmentChanged = false);

        Task SendTableAssignmentUpdatedEmailAsync(int reservationId);

        Task SendReminderEmailAsync(int reservationId);

        Task SendDueReminderEmailsAsync();

        IEnumerable<Reservation> FilterByStatus(IEnumerable<Reservation> reservations, ReservationStatus status);

        Dictionary<ReservationStatus, List<Reservation>> GroupByStatus(IEnumerable<Reservation> reservations);
    }
}
