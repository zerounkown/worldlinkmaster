using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using WorldLinkMaster.Web.Extensions;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Email:SmtpHost"]);

    public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        if (!IsConfigured)
        {
            _logger.LogInformation("Email not configured — skipping send to {ToEmail}: {Subject}", toEmail, subject);
            return;
        }

        var host = _configuration["Email:SmtpHost"]!;
        var port = int.TryParse(_configuration["Email:SmtpPort"], out var parsedPort) ? parsedPort : 587;
        var username = _configuration["Email:Username"];
        var password = _configuration["Email:Password"];
        var enableSsl = !bool.TryParse(_configuration["Email:EnableSsl"], out var sslValue) || sslValue;
        var fromAddress = _configuration["Email:FromAddress"] ?? username ?? "no-reply@worldlinkmaster.com";
        var fromName = _configuration["Email:FromName"] ?? "World Link Master";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, enableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None);

            if (!string.IsNullOrEmpty(username))
            {
                await client.AuthenticateAsync(username, password ?? string.Empty);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Email sent to {ToEmail}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Swallow so a mail-server hiccup never breaks the request flow (e.g. checkout).
            // Never log credentials or body — only the recipient and subject.
            _logger.LogError(ex, "Failed to send email to {ToEmail}: {Subject}", toEmail, subject);
        }
    }

    public Task SendOtpAsync(string toEmail, string otpCode)
    {
        var html = $@"
            <div style='font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto;'>
                <h2 style='color: #1b4332;'>World Link Master</h2>
                <p>Your one-time verification code is:</p>
                <p style='font-size: 32px; font-weight: bold; letter-spacing: 6px; color: #10281f;'>{otpCode}</p>
                <p>This code expires in 10 minutes. Enter it on the checkout page to confirm your order.</p>
                <p style='color: #777; font-size: 12px;'>If you didn't try to place an order, you can safely ignore this email.</p>
            </div>";

        return SendEmailAsync(toEmail, "Your World Link Master verification code", html);
    }

    public Task SendOrderConfirmationAsync(Order order, string toEmail)
    {
        var itemsHtml = new StringBuilder();
        foreach (var item in order.Items)
        {
            var variant = string.Join(" / ", new[] { item.Color, item.Size }.Where(v => !string.IsNullOrEmpty(v)));
            itemsHtml.Append($@"
                <tr>
                    <td style='padding: 8px 0;'>{item.ProductName}{(string.IsNullOrEmpty(variant) ? "" : $"<br/><small style='color:#777;'>{variant}</small>")} &times; {item.Quantity}</td>
                    <td style='padding: 8px 0; text-align: right;'>{item.LineTotal.ToAed()}</td>
                </tr>");
        }

        var html = $@"
            <div style='font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto;'>
                <h2 style='color: #1b4332;'>Thank you for your order!</h2>
                <p>Order #{order.Id} has been confirmed and is now <strong>{order.Status}</strong>.</p>
                <table style='width: 100%; border-collapse: collapse; margin-top: 16px;'>
                    {itemsHtml}
                    <tr><td colspan='2'><hr/></td></tr>
                    <tr>
                        <td style='padding: 8px 0;'>Subtotal</td>
                        <td style='padding: 8px 0; text-align: right;'>{order.Subtotal.ToAed()}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'>Shipping</td>
                        <td style='padding: 8px 0; text-align: right;'>{(order.ShippingCost == 0 ? "Free" : order.ShippingCost.ToAed())}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0; font-weight: bold;'>Total</td>
                        <td style='padding: 8px 0; text-align: right; font-weight: bold;'>{order.Total.ToAed()}</td>
                    </tr>
                </table>
                <p style='margin-top: 16px;'>Shipping to:<br/>{order.ShippingName}<br/>{order.ShippingAddress}<br/>{order.ShippingCity}, {order.ShippingState} {order.ShippingZip}</p>
                <p style='color: #777; font-size: 12px;'>You can review this order any time in My Orders on the World Link Master website.</p>
            </div>";

        return SendEmailAsync(toEmail, $"Your World Link Master order #{order.Id} is confirmed!", html);
    }

    public Task SendQuoteRequestNotificationAsync(QuoteRequest request)
    {
        var notifyAddress = _configuration["Email:NotificationAddress"];
        if (string.IsNullOrWhiteSpace(notifyAddress))
        {
            notifyAddress = "support@worldlinkmaster.com";
        }

        var requestNumber = $"WLM-Q-{request.Id:D6}";
        var html = $@"
            <div style='font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto;'>
                <h2 style='color: #1b4332;'>New quote request: {requestNumber}</h2>
                <p><strong>Company:</strong> {WebUtility.HtmlEncode(request.CompanyName)}</p>
                <p><strong>Email:</strong> {WebUtility.HtmlEncode(request.Email)}</p>
                <p><strong>Phone:</strong> {WebUtility.HtmlEncode(request.Phone)}</p>
                <p><strong>Trade license:</strong> {(string.IsNullOrWhiteSpace(request.TradeLicenseNumber) ? "—" : WebUtility.HtmlEncode(request.TradeLicenseNumber))}</p>
                <p><strong>Quantity:</strong> {request.Quantity}</p>
                <p><strong>Details:</strong><br/>{WebUtility.HtmlEncode(request.ProductDetails).Replace("\n", "<br/>")}</p>
                <p style='color: #777; font-size: 12px; margin-top: 16px;'>Review and respond to this request under Admin &rarr; Quote Requests.</p>
            </div>";

        return SendEmailAsync(notifyAddress, $"New quote request {requestNumber} — {request.CompanyName}", html);
    }

    public Task SendQuoteResponseAsync(QuoteRequest request, string confirmationUrl)
    {
        var requestNumber = $"WLM-Q-{request.Id:D6}";
        var statusText = request.Status switch
        {
            QuoteStatus.Quoted => "We've prepared a quote for your request.",
            QuoteStatus.Approved => "Your request has been approved.",
            QuoteStatus.Rejected => "We're unable to fulfill this request.",
            _ => "Your request has been updated."
        };

        var priceHtml = request.QuotedPrice.HasValue
            ? $"<p><strong>Quoted price:</strong> {request.QuotedPrice.Value.ToAed()}</p>"
            : "";
        var notesHtml = !string.IsNullOrWhiteSpace(request.AdminNotes)
            ? $"<p><strong>Message from our team:</strong><br/>{WebUtility.HtmlEncode(request.AdminNotes).Replace("\n", "<br/>")}</p>"
            : "";

        var html = $@"
            <div style='font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto;'>
                <h2 style='color: #1b4332;'>Update on your quote request {requestNumber}</h2>
                <p>{statusText}</p>
                {priceHtml}
                {notesHtml}
                <p style='margin-top: 16px;'><a href='{confirmationUrl}' style='color: #1b4332;'>View your request online</a></p>
                <p style='color: #777; font-size: 12px; margin-top: 16px;'>Company: {WebUtility.HtmlEncode(request.CompanyName)}</p>
            </div>";

        return SendEmailAsync(request.Email, $"Update on your quote request {requestNumber}", html);
    }

    public Task SendLeadNotificationAsync(Lead lead)
    {
        var notifyAddress = _configuration["Email:NotificationAddress"];
        if (string.IsNullOrWhiteSpace(notifyAddress))
        {
            notifyAddress = "support@worldlinkmaster.com";
        }

        var html = $@"
            <div style='font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto;'>
                <h2 style='color: #1b4332;'>عميل محتمل جديد: {WebUtility.HtmlEncode(lead.Name)}</h2>
                <p><strong>اسم العميل:</strong> {WebUtility.HtmlEncode(lead.Name)}</p>
                <p><strong>البريد:</strong> {WebUtility.HtmlEncode(lead.Email)}</p>
                <p><strong>الهاتف:</strong> {WebUtility.HtmlEncode(lead.Phone)}</p>
                <p><strong>الرسالة:</strong><br/>{(string.IsNullOrWhiteSpace(lead.Message) ? "—" : WebUtility.HtmlEncode(lead.Message).Replace("\n", "<br/>"))}</p>
                <p style='color: #777; font-size: 12px; margin-top: 16px;'>التاريخ: {lead.CreatedAt:yyyy-MM-dd HH:mm} UTC</p>
            </div>";

        return SendEmailAsync(notifyAddress, $"عميل محتمل جديد: {lead.Name}", html);
    }
}
