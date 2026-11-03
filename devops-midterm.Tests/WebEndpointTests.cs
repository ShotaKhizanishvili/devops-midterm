using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace devops_midterm.Tests;

/// <summary>
/// Integration tests that boot the real ASP.NET Core pipeline in-memory.
/// They guard the two surfaces the pipeline depends on: the /health endpoint
/// the monitoring script polls, and the JSON grading API.
/// </summary>
public class WebEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(body);
        Assert.Equal("healthy", body!.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Slot));
        Assert.False(string.IsNullOrWhiteSpace(body.Release));
    }

    [Fact]
    public async Task Home_page_renders()
    {
        var response = await _client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("GradeBook", html);
    }

    [Fact]
    public async Task Status_dashboard_renders()
    {
        var response = await _client.GetAsync("/Home/Status");

        response.EnsureSuccessStatusCode();
        Assert.Contains("Health Check", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Grades_api_returns_a_credit_weighted_gpa()
    {
        var response = await _client.PostAsJsonAsync("/api/grades/calculate", new
        {
            courses = new[]
            {
                new { name = "Heavy", credits = 10, score = 95.0 },
                new { name = "Light", credits = 2, score = 65.0 }
            }
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GpaPayload>();
        Assert.NotNull(body);
        Assert.Equal(3.5, body!.Gpa);
        Assert.Equal(12, body.TotalCredits);
    }

    [Fact]
    public async Task Grades_api_rejects_an_invalid_score()
    {
        var response = await _client.PostAsJsonAsync("/api/grades/calculate", new
        {
            courses = new[] { new { name = "Bad", credits = 5, score = 150.0 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Grades_api_rejects_an_empty_request()
    {
        var response = await _client.PostAsJsonAsync("/api/grades/calculate", new { courses = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Monitoring_api_exposes_the_log_path()
    {
        var response = await _client.GetAsync("/api/monitoring/history");

        response.EnsureSuccessStatusCode();
        Assert.Contains("logPath", await response.Content.ReadAsStringAsync());
    }

    private sealed record HealthPayload(string Status, string Slot, string Release);

    private sealed record GpaPayload(double Gpa, int TotalCredits);
}
