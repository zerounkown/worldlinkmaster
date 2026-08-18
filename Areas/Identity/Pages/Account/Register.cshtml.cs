#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserStore<ApplicationUser> _userStore;
        private readonly IUserEmailStore<ApplicationUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            IUserStore<ApplicationUser> userStore,
            SignInManager<ApplicationUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public void OnGet(string returnUrl = null)
        {
            ReturnUrl = returnUrl ?? Url.Action("Index", "Home", new { area = "" });
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Action("Index", "Home", new { area = "" });

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    var userId = await _userManager.GetUserIdAsync(user);
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code, returnUrl },
                        protocol: Request.Scheme);
                    var encodedCallbackUrl = HtmlEncoder.Default.Encode(callbackUrl);

                    var confirmationHtml = $@"
                        <div style='font-family: Arial, Helvetica, sans-serif; max-width: 480px; margin: 0 auto; border: 1px solid #e5e0d8; border-radius: 6px; overflow: hidden;'>
                            <div style='background: #0d0d0d; padding: 24px 32px; text-align: center;'>
                                <span style='color: #f0d264; font-size: 20px; font-weight: bold; letter-spacing: 1px;'>WORLD LINK MASTER</span>
                            </div>
                            <div style='padding: 32px;'>
                                <h1 style='font-size: 20px; color: #111111; margin: 0 0 16px;'>Confirm your email address</h1>
                                <p style='font-size: 15px; color: #333333; line-height: 1.5; margin: 0 0 24px;'>
                                    Thanks for creating an account with World Link Master. Please confirm your email address to activate your account.
                                </p>
                                <div style='text-align: center; margin: 0 0 24px;'>
                                    <a href='{encodedCallbackUrl}' style='background: #f0d264; color: #0d0d0d; text-decoration: none; font-weight: bold; padding: 14px 32px; border-radius: 4px; display: inline-block; font-size: 15px;'>Confirm My Account</a>
                                </div>
                                <p style='font-size: 13px; color: #777777; line-height: 1.5;'>
                                    If the button above doesn't work, copy and paste this link into your browser:<br/>
                                    <a href='{encodedCallbackUrl}' style='color: #0d6efd; word-break: break-all;'>{encodedCallbackUrl}</a>
                                </p>
                            </div>
                            <div style='background: #f7f5f0; padding: 20px 32px; font-size: 12px; color: #888888; text-align: center;'>
                                <p style='margin: 0 0 6px;'>You're receiving this email because this address was used to create an account at World Link Master.</p>
                                <p style='margin: 0;'>If you didn't request this, you can safely ignore this email.</p>
                                <p style='margin: 12px 0 0;'>&copy; {DateTime.UtcNow.Year} World Link Master. All rights reserved.</p>
                            </div>
                        </div>";

                    await _emailSender.SendEmailAsync(Input.Email, "Confirm your email", confirmationHtml);

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return Page();
        }

        private ApplicationUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<ApplicationUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(ApplicationUser)}'. " +
                    $"Ensure that '{nameof(ApplicationUser)}' is not an abstract class and has a parameterless constructor.");
            }
        }

        private IUserEmailStore<ApplicationUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<ApplicationUser>)_userStore;
        }
    }
}
