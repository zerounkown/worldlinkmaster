using Microsoft.Extensions.Diagnostics.HealthChecks;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.HealthChecks;

/// <summary>Reports whether SMTP email delivery is configured (no credentials are exposed).</summary>
public class SmtpConfigurationHealthCheck : IHealthCheck
{
    private readonly IEmailService _emailService;

    public SmtpConfigurationHealthCheck(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_emailService.IsConfigured
            ? HealthCheckResult.Healthy("SMTP host is configured.")
            : HealthCheckResult.Degraded("SMTP is not configured — OTP and confirmation emails will not be delivered."));
    }
}
