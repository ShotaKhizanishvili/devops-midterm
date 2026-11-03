using System.Reflection;

namespace devops_midterm.Services;

/// <summary>
/// Describes the running instance. The blue-green deploy script stamps
/// DEPLOY_SLOT / RELEASE_VERSION into the environment, so <c>/health</c> can
/// report exactly which release is live - that is what makes a rollback visible.
/// </summary>
public sealed class DeploymentInfo
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public string Slot { get; } = Environment.GetEnvironmentVariable("DEPLOY_SLOT") ?? "local";

    public string Release { get; } = Environment.GetEnvironmentVariable("RELEASE_VERSION") ?? "dev";

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    public string Host { get; } = Environment.MachineName;

    public DateTimeOffset StartTime => StartedAt;

    public double UptimeSeconds => Math.Round((DateTimeOffset.UtcNow - StartedAt).TotalSeconds, 1);
}
