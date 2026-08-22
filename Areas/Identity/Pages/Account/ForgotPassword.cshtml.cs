#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
                {
                    // Don't reveal that the user does not exist or is not confirmed.
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);
                var encodedCallbackUrl = HtmlEncoder.Default.Encode(callbackUrl);

                var resetHtml = $@"
                    <div style='font-family: Arial, Helvetica, sans-serif; max-width: 480px; margin: 0 auto; border: 1px solid #e5e0d8; border-radius: 6px; overflow: hidden;'>
                        <div style='background: #0d0d0d; padding: 24px 32px; text-align: center;'>
                            <span style='color: #f0d264; font-size: 20px; font-weight: bold; letter-spacing: 1px;'>WORLD LINK MASTER</span>
                        </div>
                        <div style='padding: 32px;'>
                            <h1 style='font-size: 20px; color: #111111; margin: 0 0 16px;'>Reset your password</h1>
                            <p style='font-size: 15px; color: #333333; line-height: 1.5; margin: 0 0 24px;'>
                                We received a request to reset the password for your World Link Master account. Click the button below to choose a new password.
                            </p>
                            <div style='text-align: center; margin: 0 0 24px;'>
                                <a href='{encodedCallbackUrl}' style='background: #f0d264; color: #0d0d0d; text-decoration: none; font-weight: bold; padding: 14px 32px; border-radius: 4px; display: inline-block; font-size: 15px;'>Reset My Password</a>
                            </div>
                            <p style='font-size: 13px; color: #777777; line-height: 1.5;'>
                                If the button above doesn't work, copy and paste this link into your browser:<br/>
                                <a href='{encodedCallbackUrl}' style='color: #0d6efd; word-break: break-all;'>{encodedCallbackUrl}</a>
                            </p>
                        </div>
                        <div style='background: #f7f5f0; padding: 20px 32px; font-size: 12px; color: #888888; text-align: center;'>
                            <p style='margin: 0 0 6px;'>You're receiving this email because a password reset was requested for this address at World Link Master.</p>
                            <p style='margin: 0;'>If you didn't request this, you can safely ignore this email — your password will not be changed.</p>
                            <p style='margin: 12px 0 0;'>&copy; {DateTime.UtcNow.Year} World Link Master. All rights reserved.</p>
                        </div>
                    </div>";

                await _emailSender.SendEmailAsync(Input.Email, "Reset your password", resetHtml);

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
