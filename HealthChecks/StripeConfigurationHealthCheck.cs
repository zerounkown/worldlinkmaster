using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorldLinkMaster.Web.HealthChecks;

/// <summary>Reports whether Stripe payment + webhook secrets are configured (no secret values are exposed).</summary>
public class StripeConfigurationHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public StripeConfigurationHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var hasSecretKey = !string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]);
        var hasWebhookSecret = !string.IsNullOrEmpty(_configuration["Stripe:WebhookSecret"]);

        if (!hasSecretKey)
        {
            // Degraded (not Unhealthy): the site still serves pages, but checkout won't work.
            return Task.FromResult(HealthCheckResult.Degraded("Stripe:SecretKey is not configured — checkout is disabled."));
        }

        if (!hasWebhookSecret)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Stripe:WebhookSecret is not configured — webhook signatures cannot be verified."));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Stripe secret key and webhook secret are configured."));
    }
}
