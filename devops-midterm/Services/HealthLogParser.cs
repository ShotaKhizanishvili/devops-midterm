using System.Globalization;

namespace devops_midterm.Services;

/// <summary>One line of the health-check log file.</summary>
public sealed record HealthCheckEntry(
    DateTimeOffset Timestamp,
    string Status,
    int HttpStatus,
    int LatencyMs,
    string Url,
    string Note)
{
    public bool IsUp => string.Equals(Status, "UP", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Aggregated view of the log, used by the monitoring dashboard.</summary>
public sealed record HealthSummary(
    int TotalChecks,
    int UpChecks,
    int DownChecks,
    double UptimePercent,
    int ConsecutiveFailures,
    double AverageLatencyMs,
    HealthCheckEntry? Latest,
    IReadOnlyList<HealthCheckEntry> Entries)
{
    public static HealthSummary Empty { get; } = new(0, 0, 0, 0d, 0, 0d, null, []);
}

/// <summary>
/// Parses the pipe-delimited lines written by <c>scripts/health-check.ps1</c>.
///
/// Line format:
/// <code>2026-11-03T20:15:04Z | UP | 200 | 37ms | http://localhost:5080/health | slot=blue release=20261103-201500</code>
///
/// Kept free of file I/O so it is directly unit-testable.
/// </summary>
public static class HealthLogParser
{
    private const int FieldCount = 6;

    /// <summary>Parses a single log line, or returns null when the line is blank/malformed.</summary>
    public static HealthCheckEntry? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = line.Split('|', FieldCount, StringSplitOptions.TrimEntries);
        if (parts.Length < FieldCount)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                parts[0],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return null;
        }

        var status = parts[1].ToUpperInvariant();
        if (status is not ("UP" or "DOWN"))
        {
            return null;
        }

        _ = int.TryParse(parts[2], CultureInfo.InvariantCulture, out var httpStatus);

        var latencyText = parts[3].EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            ? parts[3][..^2].Trim()
            : parts[3];
        _ = int.TryParse(latencyText, CultureInfo.InvariantCulture, out var latencyMs);

        return new HealthCheckEntry(timestamp, status, httpStatus, latencyMs, parts[4], parts[5]);
    }

    /// <summary>Parses every readable line and aggregates uptime statistics.</summary>
    public static HealthSummary Summarize(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = lines
            .Select(ParseLine)
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.Timestamp)
            .ToList();

        if (entries.Count == 0)
        {
            return HealthSummary.Empty;
        }

        var upChecks = entries.Count(entry => entry.IsUp);

        // Walk backwards from the newest entry to find the current failure streak.
        var consecutiveFailures = 0;
        for (var i = entries.Count - 1; i >= 0 && !entries[i].IsUp; i--)
        {
            consecutiveFailures++;
        }

        var upLatencies = entries.Where(entry => entry.IsUp).Select(entry => (double)entry.LatencyMs).ToList();

        return new HealthSummary(
            TotalChecks: entries.Count,
            UpChecks: upChecks,
            DownChecks: entries.Count - upChecks,
            UptimePercent: Math.Round(upChecks * 100d / entries.Count, 2, MidpointRounding.AwayFromZero),
            ConsecutiveFailures: consecutiveFailures,
            AverageLatencyMs: upLatencies.Count == 0 ? 0d : Math.Round(upLatencies.Average(), 1, MidpointRounding.AwayFromZero),
            Latest: entries[^1],
            Entries: entries);
    }
}
