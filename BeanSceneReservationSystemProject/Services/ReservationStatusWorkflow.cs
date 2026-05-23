using BeanSceneReservationSystemProject.DataStructures;
using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.Services
{
    public static class ReservationStatusWorkflow
    {
        private static readonly DoublyLinkedList<ReservationStatus> PrimaryFlow = BuildPrimaryFlow();

        public static bool CanTransition(ReservationStatus oldStatus, ReservationStatus newStatus)
        {
            if (newStatus == ReservationStatus.Cancelled)
            {
                return oldStatus is ReservationStatus.Pending or ReservationStatus.Confirmed;
            }

            return PrimaryFlow.ContainsForwardStep(oldStatus, newStatus);
        }

        private static DoublyLinkedList<ReservationStatus> BuildPrimaryFlow()
        {
            var statuses = new DoublyLinkedList<ReservationStatus>();
            statuses.AddLast(ReservationStatus.Pending);
            statuses.AddLast(ReservationStatus.Confirmed);
            statuses.AddLast(ReservationStatus.Seated);
            statuses.AddLast(ReservationStatus.Completed);
            return statuses;
        }
    }
}
