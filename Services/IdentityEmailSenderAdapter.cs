using Microsoft.AspNetCore.Identity.UI.Services;

namespace WorldLinkMaster.Web.Services;

// Bridges ASP.NET Core Identity's own IEmailSender (used by the scaffolded Register/ForgotPassword
// pages to send confirmation/reset links) to the app's real SMTP-backed IEmailService. Without this,
// AddDefaultIdentity() falls back to its built-in no-op IEmailSender, which silently "succeeds"
// without ever sending anything — new accounts can never confirm their email and every login then
// fails with the generic "Invalid login attempt." message.
public class IdentityEmailSenderAdapter : IEmailSender
{
    private readonly IEmailService _emailService;

    public IdentityEmailSenderAdapter(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        _emailService.SendEmailAsync(email, subject, htmlMessage);
}
