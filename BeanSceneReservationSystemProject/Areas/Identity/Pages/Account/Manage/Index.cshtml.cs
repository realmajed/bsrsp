// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BeanSceneReservationSystemProject.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BeanSceneReservationSystemProject.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IWebHostEnvironment _environment;
        private static readonly string[] AllowedProfilePictureExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
        private const long MaxProfilePictureBytes = 2 * 1024 * 1024;

        public IndexModel(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IWebHostEnvironment environment)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _environment = environment;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string Username { get; set; }
        public string Role { get; set; }
        public bool CanUploadProfilePicture { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Phone]
            [Display(Name = "Phone number")]
            public string PhoneNumber { get; set; }

            [Display(Name = "Profile picture")]
            public IFormFile ProfilePicture { get; set; }

            public string ProfilePicturePath { get; set; }
        }

        private async Task LoadAsync(User user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var roles = await _userManager.GetRolesAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);

            Username = userName;
            Role = roles.FirstOrDefault();
            CanUploadProfilePicture = roles.Any(IsStaffRole);

            Input = new InputModel
            {
                PhoneNumber = phoneNumber,
                ProfilePicturePath = user.ProfilePicturePath
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    StatusMessage = "Unexpected error when trying to set phone number.";
                    return RedirectToPage();
                }
            }

            var roles = await _userManager.GetRolesAsync(user);
            var canUploadProfilePicture = roles.Any(IsStaffRole);
            if (Input.ProfilePicture != null && Input.ProfilePicture.Length > 0)
            {
                if (!canUploadProfilePicture)
                {
                    ModelState.AddModelError("Input.ProfilePicture", "Only owners, managers and staff can upload profile pictures.");
                    await LoadAsync(user);
                    return Page();
                }

                var uploadResult = await SaveProfilePictureAsync(Input.ProfilePicture);
                if (!uploadResult.Succeeded)
                {
                    ModelState.AddModelError("Input.ProfilePicture", uploadResult.ErrorMessage);
                    await LoadAsync(user);
                    return Page();
                }

                DeleteExistingProfilePicture(user.ProfilePicturePath);
                user.ProfilePicturePath = uploadResult.Path;

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    StatusMessage = "Unexpected error when trying to update profile picture.";
                    return RedirectToPage();
                }
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }

        private static bool IsStaffRole(string role)
        {
            return role == UserRole.Owner.ToString()
                || role == UserRole.Manager.ToString()
                || role == UserRole.Staff.ToString();
        }

        private async Task<(bool Succeeded, string Path, string ErrorMessage)> SaveProfilePictureAsync(IFormFile profilePicture)
        {
            if (profilePicture.Length > MaxProfilePictureBytes)
            {
                return (false, null, "Profile picture must be 2 MB or smaller.");
            }

            var extension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();
            if (!AllowedProfilePictureExtensions.Contains(extension))
            {
                return (false, null, "Profile picture must be a JPG, PNG, GIF or WEBP image.");
            }

            if (profilePicture.ContentType == null || !profilePicture.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, "Profile picture must be an image file.");
            }

            var uploadFolder = Path.Combine(_environment.WebRootPath, "uploads", "profile-pictures");
            Directory.CreateDirectory(uploadFolder);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadFolder, fileName);

            await using var stream = System.IO.File.Create(filePath);
            await profilePicture.CopyToAsync(stream);

            return (true, $"/uploads/profile-pictures/{fileName}", null);
        }

        private void DeleteExistingProfilePicture(string profilePicturePath)
        {
            if (string.IsNullOrWhiteSpace(profilePicturePath) || !profilePicturePath.StartsWith("/uploads/profile-pictures/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var relativePath = profilePicturePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(_environment.WebRootPath, relativePath);

            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
    }
}
