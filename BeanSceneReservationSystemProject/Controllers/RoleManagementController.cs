using BeanSceneReservationSystemProject.Models;
using BeanSceneReservationSystemProject.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace BeanSceneReservationSystemProject.Controllers
{
    [Authorize(Roles = "Owner,Manager")]
    public class RoleManagementController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public RoleManagementController(UserManager<User> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: RoleManagementController
        public async Task<ActionResult> Index()
        {
            var users = await _userManager.Users
                .Include(u => u.MemberProfile)
                .OrderBy(u => u.Email)
                .ToListAsync();
            var roles = await _roleManager.Roles
                .Select(r => r.Name!)
                .Where(r => r != UserRole.Owner.ToString())
                .OrderBy(r => r)
                .ToListAsync();
            var model = new List<UserRoleViewModel>();

            foreach (var user in users)
            {
                // Only real member accounts are shown here, not guests.
                if (user.MemberProfile == null) continue;
                //if (user.Role is UserRole.Owner or UserRole.Manager) continue;
                var userRoles = await _userManager.GetRolesAsync(user);
                model.Add(new UserRoleViewModel
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email!,
                    ProfilePicturePath = user.ProfilePicturePath,
                    CurrentRole = userRoles.FirstOrDefault() ?? "None",
                    SelectedRole = "Member",
                    AvailableRoles = roles
                });
            }
            return View(model);
        }

        // POST: 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRole(string userId, string selectedRole)
        {
            var user = await _userManager.Users
                .Include(u => u.MemberProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return NotFound();

            // Make sure its not a guest user. For a manager to make someone staff etc,
            // they must actually be a member of the service first.
            if (user.MemberProfile == null)
            {
                TempData["ErrorMessage"] = "User must create a member account first.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _roleManager.RoleExistsAsync(selectedRole))
            {
                TempData["ErrorMessage"] = "Selected role does not exist.";
                return RedirectToAction(nameof(Index));
            }

            // Don't let the person changing the role be able to manage their own role -
            // this makes sure a manager/owner can't accidentally remove
            // themselves from their role.
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser!.Id == user.Id)
            {
                TempData["ErrorMessage"] = "You cannot change your own role.";
                return RedirectToAction(nameof(Index));
            }

            // Nobody can manage the Owner.
            if (user.Role == UserRole.Owner)
            {
                TempData["ErrorMessage"] =
                    $"{user.FirstName} {user.LastName} has the Owner role, which is protected and cannot be changed.";
                return RedirectToAction(nameof(Index));

            }

            // Managers cannot manage other Managers.
            // Owners can manage Managers.
            if (user.Role == UserRole.Manager && currentUser.Role != UserRole.Owner)
            {
                TempData["ErrorMessage"] =
                    $"{user.FirstName} {user.LastName} is a Manager. Only the Owner can change Manager roles.";
                return RedirectToAction(nameof(Index));
            }

            // convert string role to enum role
            if (!Enum.TryParse<UserRole>(selectedRole, out var newRole))
            {
                TempData["ErrorMessage"] = "Invalid selected role.";
                return RedirectToAction(nameof(Index));
            }

            if (newRole == UserRole.Owner)
            {
                TempData["ErrorMessage"] = "Owner role cannot be assigned here.";
                return RedirectToAction(nameof(Index));
            }
            // check if they already have the role
            if (user.Role == newRole)
            {
                TempData["ErrorMessage"] = "User already has this role.";
                return RedirectToAction(nameof(Index));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Any())
            {
                // Identity only needs one app role at a time for this project.
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    TempData["ErrorMessage"] = string.Join(" ", removeResult.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
            }

            var addResult = await _userManager.AddToRoleAsync(user, selectedRole);
            if (!addResult.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", addResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            user.Role = newRole;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", updateResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            TempData["Message"] = $"Role updated for {user.Email}.";
            return RedirectToAction(nameof(Index));
        }
    }
}
