using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Transit.Data;

namespace Transit.Web;

public class FeedHealthOptions
{
    public const string SectionName = "FeedHealth";

    public TimeSpan MaxFeedAge { get; set; } = TimeSpan.FromMinutes(2);
}

public class FeedFreshnessHealthCheck(
    FeedOutageRecorder recorder,
    TimeProvider timeProvider,
    IOptions<FeedHealthOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var latest = await recorder.GetLatestFeedTimestampAsync(cancellationToken);
        if (latest is null)
        {
            return HealthCheckResult.Unhealthy("No vehicle positions feed has been recorded.");
        }

        var age = timeProvider.GetUtcNow() - latest.Value;
        var data = new Dictionary<string, object> { ["latestFeedTimestamp"] = latest.Value, ["ageSeconds"] = (int)age.TotalSeconds };

        return age <= options.Value.MaxFeedAge
            ? HealthCheckResult.Healthy("Vehicle positions feed is current.", data)
            : HealthCheckResult.Unhealthy($"Vehicle positions feed last updated {(int)age.TotalSeconds} s ago.", data: data);
    }
}
