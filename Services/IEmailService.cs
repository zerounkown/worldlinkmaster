using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface IEmailService
{
    bool IsConfigured { get; }

    Task SendEmailAsync(string toEmail, string subject, string htmlBody);

    Task SendOtpAsync(string toEmail, string otpCode);

    Task SendOrderConfirmationAsync(Order order, string toEmail);
}
