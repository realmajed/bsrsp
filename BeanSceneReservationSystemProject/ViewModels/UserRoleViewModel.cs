namespace BeanSceneReservationSystemProject.ViewModels
{
    public class UserRoleViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? ProfilePicturePath { get; set; }
        public string CurrentRole { get; set; } = string.Empty;
        public string SelectedRole { get; set; } = string.Empty;
        public List<string> AvailableRoles { get; set; } = new();
    }
}
