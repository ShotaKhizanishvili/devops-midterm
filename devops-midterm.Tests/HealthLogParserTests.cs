using devops_midterm.Services;

namespace devops_midterm.Tests;

/// <summary>
/// Unit tests for the health-log parser that feeds the monitoring dashboard.
/// The format under test is exactly what scripts/health-check.ps1 writes.
/// </summary>
public class HealthLogParserTests
{
    private const string UpLine =
        "2026-11-03T20:15:04Z | UP | 200 | 37ms | http://localhost:5080/health | slot=blue release=20261103-201500";

    private const string DownLine =
        "2026-11-03T20:15:24Z | DOWN | 0 | 2000ms | http://localhost:5080/health | connection refused";

    [Fact]
    public void ParseLine_reads_every_field_of_a_healthy_check()
    {
        var entry = HealthLogParser.ParseLine(UpLine);

        Assert.NotNull(entry);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 20, 15, 4, TimeSpan.Zero), entry!.Timestamp);
        Assert.Equal("UP", entry.Status);
        Assert.Equal(200, entry.HttpStatus);
        Assert.Equal(37, entry.LatencyMs);
        Assert.Equal("http://localhost:5080/health", entry.Url);
        Assert.Equal("slot=blue release=20261103-201500", entry.Note);
        Assert.True(entry.IsUp);
    }

    [Fact]
    public void ParseLine_reads_a_failed_check()
    {
        var entry = HealthLogParser.ParseLine(DownLine);

        Assert.NotNull(entry);
        Assert.False(entry!.IsUp);
        Assert.Equal(0, entry.HttpStatus);
        Assert.Equal(2000, entry.LatencyMs);
        Assert.Equal("connection refused", entry.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a log line at all")]
    [InlineData("2026-11-03T20:15:04Z | UP | 200")]
    [InlineData("nonsense-date | UP | 200 | 10ms | url | note")]
    [InlineData("2026-11-03T20:15:04Z | SIDEWAYS | 200 | 10ms | url | note")]
    public void ParseLine_returns_null_for_blank_or_malformed_lines(string? line)
    {
        Assert.Null(HealthLogParser.ParseLine(line));
    }

    [Fact]
    public void Summarize_computes_uptime_over_all_parsed_checks()
    {
        var summary = HealthLogParser.Summarize([UpLine, DownLine, UpLine, UpLine]);

        Assert.Equal(4, summary.TotalChecks);
        Assert.Equal(3, summary.UpChecks);
        Assert.Equal(1, summary.DownChecks);
        Assert.Equal(75d, summary.UptimePercent);
    }

    [Fact]
    public void Summarize_skips_unparseable_lines_instead_of_throwing()
    {
        var summary = HealthLogParser.Summarize([UpLine, "### rubbish ###", "", DownLine]);

        Assert.Equal(2, summary.TotalChecks);
    }

    [Fact]
    public void Summarize_counts_only_the_current_failure_streak()
    {
        var lines = new[]
        {
            Line("2026-11-03T20:00:00Z", "DOWN"),
            Line("2026-11-03T20:01:00Z", "UP"),
            Line("2026-11-03T20:02:00Z", "DOWN"),
            Line("2026-11-03T20:03:00Z", "DOWN")
        };

        var summary = HealthLogParser.Summarize(lines);

        Assert.Equal(2, summary.ConsecutiveFailures);
    }

    [Fact]
    public void Summarize_reports_zero_consecutive_failures_while_the_service_is_up()
    {
        var summary = HealthLogParser.Summarize([
            Line("2026-11-03T20:00:00Z", "DOWN"),
            Line("2026-11-03T20:01:00Z", "UP")
        ]);

        Assert.Equal(0, summary.ConsecutiveFailures);
    }

    [Fact]
    public void Summarize_orders_entries_chronologically_and_exposes_the_newest_as_latest()
    {
        var lines = new[]
        {
            Line("2026-11-03T20:05:00Z", "UP"),
            Line("2026-11-03T20:01:00Z", "UP"),
            Line("2026-11-03T20:09:00Z", "DOWN")
        };

        var summary = HealthLogParser.Summarize(lines);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 3, 20, 1, 0, TimeSpan.Zero),
            summary.Entries[0].Timestamp);
        Assert.Equal(
            new DateTimeOffset(2026, 11, 3, 20, 9, 0, TimeSpan.Zero),
            summary.Latest!.Timestamp);
    }

    [Fact]
    public void Summarize_averages_latency_of_successful_checks_only()
    {
        var lines = new[]
        {
            "2026-11-03T20:00:00Z | UP | 200 | 10ms | u | n",
            "2026-11-03T20:01:00Z | UP | 200 | 30ms | u | n",
            "2026-11-03T20:02:00Z | DOWN | 0 | 5000ms | u | n"
        };

        var summary = HealthLogParser.Summarize(lines);

        Assert.Equal(20d, summary.AverageLatencyMs);
    }

    [Fact]
    public void Summarize_returns_an_empty_summary_for_an_empty_log()
    {
        var summary = HealthLogParser.Summarize([]);

        Assert.Equal(0, summary.TotalChecks);
        Assert.Null(summary.Latest);
        Assert.Empty(summary.Entries);
    }

    [Fact]
    public void Summarize_rejects_a_null_input()
    {
        Assert.Throws<ArgumentNullException>(() => HealthLogParser.Summarize(null!));
    }

    private static string Line(string timestamp, string status) =>
        $"{timestamp} | {status} | 200 | 12ms | http://localhost:5080/health | slot=green";
}
