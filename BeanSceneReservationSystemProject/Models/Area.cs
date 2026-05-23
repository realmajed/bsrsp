using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class Area
    {
        public int AreaId { get; set; }

        [Required, StringLength(30)]
        public string AreaName { get; set; } = string.Empty;

        public ICollection<RestaurantTable> RestaurantTables { get; set; } = new List<RestaurantTable>();
    }
}
