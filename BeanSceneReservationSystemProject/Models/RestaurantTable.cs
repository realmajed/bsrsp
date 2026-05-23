using System.ComponentModel.DataAnnotations;

namespace BeanSceneReservationSystemProject.Models
{
    public class RestaurantTable
    {
        public int RestaurantTableId { get; set; }

        [Required, StringLength(10)]
        public string TableCode { get; set; } = string.Empty;

        [Required, Range(1, 20)]
        public int Capacity { get; set; } = 4;

        public bool IsAvailable { get; set; } = true;

        [Required]
        public int AreaId { get; set; }
        public Area? Area { get; set; }

        public ICollection<ReservationTable> ReservationTables { get; set; } = new List<ReservationTable>();

        // Used in dropdowns so staff can see both the area and the table code.
        public string TableDisplayName => Area == null ? TableCode : $"{Area.AreaName} - {TableCode}";
    }
}
