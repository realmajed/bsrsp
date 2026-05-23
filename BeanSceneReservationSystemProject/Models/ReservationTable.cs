namespace BeanSceneReservationSystemProject.Models
{
    // Join table because one reservation can use multiple tables.
    public class ReservationTable
    {
        public int ReservationTableId { get; set; }

        public int ReservationId { get; set; }
        public Reservation? Reservation { get; set; }

        public int RestaurantTableId { get; set; }
        public RestaurantTable? RestaurantTable { get; set; }
    }
}
