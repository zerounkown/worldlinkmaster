using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private const long MaxFileSizeBytes = 3 * 1024 * 1024; // 3 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ProfileController(UserManager<ApplicationUser> userManager, IWebHostEnvironment environment, IStringLocalizer<SharedResource> localizer)
    {
        _userManager = userManager;
        _environment = environment;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPicture(IFormFile? file)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        if (file == null || file.Length == 0)
        {
            TempData["ProfileMessage"] = _localizer["Choose a picture to upload."].Value;
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > MaxFileSizeBytes)
        {
            TempData["ProfileMessage"] = _localizer["That picture is too large (3 MB max)."].Value;
            return RedirectToAction(nameof(Index));
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            TempData["ProfileMessage"] = _localizer["Please upload a JPG, PNG, GIF, or WEBP image."].Value;
            return RedirectToAction(nameof(Index));
        }

        var avatarsRoot = Path.Combine(_environment.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(avatarsRoot);

        var extension = Path.GetExtension(file.FileName);
        var newFileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(avatarsRoot, newFileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        DeleteExistingPicture(user);

        user.ProfilePictureFileName = newFileName;
        await _userManager.UpdateAsync(user);

        TempData["ProfileMessage"] = _localizer["Profile picture updated."].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePicture()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        DeleteExistingPicture(user);
        user.ProfilePictureFileName = null;
        await _userManager.UpdateAsync(user);

        TempData["ProfileMessage"] = _localizer["Profile picture removed."].Value;
        return RedirectToAction(nameof(Index));
    }

    private void DeleteExistingPicture(ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.ProfilePictureFileName))
        {
            return;
        }

        var existingPath = Path.Combine(_environment.WebRootPath, "uploads", "avatars", user.ProfilePictureFileName);
        if (System.IO.File.Exists(existingPath))
        {
            System.IO.File.Delete(existingPath);
        }
    }
}
