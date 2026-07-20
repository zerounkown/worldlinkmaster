using System.Text.Json;

namespace WorldLinkMaster.Web.Services;

// Google reCAPTCHA v3 verification. Set the secret key under "Recaptcha:SecretKey" in
// appsettings — same "leave it blank for now" pattern as Email:SmtpHost: if it's not
// configured, verification is skipped (returns true) so the Lead form still works before
// reCAPTCHA is set up, rather than blocking every submission.
public class RecaptchaService : IRecaptchaService
{
    private readonly HttpClient _httpClient;
    private readonly string? _secretKey;

    public RecaptchaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _secretKey = configuration["Recaptcha:SecretKey"];
    }

    public async Task<bool> VerifyAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var response = await _httpClient.PostAsync(
            $"https://www.google.com/recaptcha/api/siteverify?secret={_secretKey}&response={token}",
            null);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var success = root.GetProperty("success").GetBoolean();

        var score = root.TryGetProperty("score", out var scoreProp)
            ? scoreProp.GetDouble()
            : 1.0;

        return success && score >= 0.5;
    }
}
