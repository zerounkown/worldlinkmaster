using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
    private const string ContainerName = "avatars";

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        IStringLocalizer<SharedResource> localizer,
        ILogger<ProfileController> logger)
    {
        _userManager = userManager;
        _configuration = configuration;
        _localizer = localizer;
        _logger = logger;
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

        var containerClient = TryGetContainerClient();
        if (containerClient == null)
        {
            TempData["ProfileMessage"] = _localizer["Picture uploads aren't available right now. Please try again later."].Value;
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var extension = Path.GetExtension(file.FileName);
            var newBlobName = $"{Guid.NewGuid():N}{extension}";
            var blobClient = containerClient.GetBlobClient(newBlobName);

            await using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType }
                });
            }

            await DeleteExistingPictureAsync(containerClient, user);

            user.ProfilePictureUrl = blobClient.Uri.ToString();
            await _userManager.UpdateAsync(user);

            TempData["ProfileMessage"] = _localizer["Profile picture updated."].Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload profile picture for user {UserId}", user.Id);
            TempData["ProfileMessage"] = _localizer["Something went wrong uploading your picture. Please try again."].Value;
        }

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

        var containerClient = TryGetContainerClient();
        try
        {
            if (containerClient != null)
            {
                await DeleteExistingPictureAsync(containerClient, user);
            }
            user.ProfilePictureUrl = null;
            await _userManager.UpdateAsync(user);

            TempData["ProfileMessage"] = _localizer["Profile picture removed."].Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove profile picture for user {UserId}", user.Id);
            TempData["ProfileMessage"] = _localizer["Something went wrong removing your picture. Please try again."].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    private BlobContainerClient? TryGetContainerClient()
    {
        var connectionString = _configuration["ProductPhotos:BlobConnectionString"];
        return string.IsNullOrEmpty(connectionString) ? null : new BlobContainerClient(connectionString, ContainerName);
    }

    private static async Task DeleteExistingPictureAsync(BlobContainerClient containerClient, ApplicationUser user)
    {
        if (string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            return;
        }

        var blobName = new Uri(user.ProfilePictureUrl).Segments[^1];
        await containerClient.DeleteBlobIfExistsAsync(blobName);
    }
}
