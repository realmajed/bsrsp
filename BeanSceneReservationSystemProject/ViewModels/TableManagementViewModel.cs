using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.ViewModels
{
    public class TableManagementViewModel
    {
        public List<RestaurantTable> Tables { get; set; } = new();

        public List<Area> Areas { get; set; } = new();

        public Area NewArea { get; set; } = new();
    }
}
