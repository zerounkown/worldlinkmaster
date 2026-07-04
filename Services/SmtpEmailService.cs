using System.Net;
using System.Net.Mail;
using System.Text;
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
        var username = _configuration["Email:SmtpUsername"];
        var password = _configuration["Email:SmtpPassword"];
        var enableSsl = !bool.TryParse(_configuration["Email:EnableSsl"], out var sslValue) || sslValue;
        var fromAddress = _configuration["Email:FromAddress"] ?? username ?? "no-reply@worldlinkmaster.com";
        var fromName = _configuration["Email:FromName"] ?? "World Link Master";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl
        };

        if (!string.IsNullOrEmpty(username))
        {
            client.Credentials = new NetworkCredential(username, password);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
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
}
