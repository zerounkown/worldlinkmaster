using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace WorldLinkMaster.Web.HealthChecks;

/// <summary>Writes a compact JSON body for health endpoints (overall status + per-check status).</summary>
public static class HealthResponseWriter
{
    public static Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
