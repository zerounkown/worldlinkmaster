using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface IEmailService
{
    bool IsConfigured { get; }

    Task SendEmailAsync(string toEmail, string subject, string htmlBody);

    Task SendOtpAsync(string toEmail, string otpCode);

    Task SendOrderConfirmationAsync(Order order, string toEmail);

    Task SendQuoteRequestNotificationAsync(QuoteRequest request);

    // Sent to the customer when an admin responds (status/price/notes) so they don't have to
    // remember to check back on their confirmation page.
    Task SendQuoteResponseAsync(QuoteRequest request, string confirmationUrl);

    Task SendLeadNotificationAsync(Lead lead);
}
