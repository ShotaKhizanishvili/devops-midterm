using Microsoft.Extensions.Options;

namespace devops_midterm.Services;

/// <summary>Where the monitoring dashboard looks for the health-check log.</summary>
public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>Absolute path, or a path relative to the application content root.</summary>
    public string LogPath { get; set; } = Path.Combine("logs", "health-check.log");

    /// <summary>How many of the most recent checks the dashboard renders.</summary>
    public int MaxEntries { get; set; } = 40;
}

/// <summary>
/// Reads the log file produced by <c>scripts/health-check.ps1</c> and turns it into
/// dashboard data. The file is opened with a shared lock so a running health check
/// can keep appending while the page is being rendered.
/// </summary>
public sealed class MonitoringService(
    IOptions<MonitoringOptions> options,
    IHostEnvironment environment,
    ILogger<MonitoringService> logger)
{
    private readonly MonitoringOptions _options = options.Value;

    /// <summary>The fully resolved path the dashboard is watching.</summary>
    public string ResolvedLogPath => Path.IsPathRooted(_options.LogPath)
        ? _options.LogPath
        : Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.LogPath));

    public bool LogExists => File.Exists(ResolvedLogPath);

    /// <summary>Returns the aggregated health history, trimmed to the newest <c>MaxEntries</c>.</summary>
    public HealthSummary Read()
    {
        var path = ResolvedLogPath;
        if (!File.Exists(path))
        {
            logger.LogDebug("Health log {Path} does not exist yet.", path);
            return HealthSummary.Empty;
        }

        try
        {
            var summary = HealthLogParser.Summarize(ReadLines(path));
            if (summary.Entries.Count <= _options.MaxEntries)
            {
                return summary;
            }

            var trimmed = summary.Entries.TakeLast(_options.MaxEntries).ToList();
            return summary with { Entries = trimmed };
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read health log {Path}.", path);
            return HealthSummary.Empty;
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
