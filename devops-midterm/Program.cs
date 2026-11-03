using devops_midterm.Models;
using devops_midterm.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Monitoring: the dashboard reads the log file that scripts/health-check.ps1 appends to.
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
builder.Services.AddSingleton<MonitoringService>();
builder.Services.AddSingleton<DeploymentInfo>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// NOTE: no HTTPS redirection / HSTS on purpose - the blue-green deployment target
// serves plain HTTP on a loopback port, and the health check must not be redirected.

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

// ---------------------------------------------------------------------------
// Health check endpoint - the single source of truth for the monitoring script.
// It does not just answer "the process is up"; it re-runs the core domain logic
// so a broken deployment is reported as unhealthy (503) and can be rolled back.
// ---------------------------------------------------------------------------
app.MapGet("/health", (DeploymentInfo deployment) =>
{
    string status;
    string? error = null;

    try
    {
        var probe = GradeCalculator.Calculate([new CourseEntry("probe", 5, 95d)]);
        status = probe is { Gpa: 4.0d, Letter: "A" } ? "healthy" : "degraded";
    }
    catch (Exception ex)
    {
        status = "unhealthy";
        error = ex.Message;
    }

    var payload = new
    {
        status,
        slot = deployment.Slot,
        release = deployment.Release,
        version = deployment.Version,
        host = deployment.Host,
        startedAt = deployment.StartTime,
        uptimeSeconds = deployment.UptimeSeconds,
        timestamp = DateTimeOffset.UtcNow,
        error
    };

    return status == "healthy" ? Results.Ok(payload) : Results.Json(payload, statusCode: 503);
}).WithName("Health");

// ---------------------------------------------------------------------------
// JSON API for the grade calculator (same logic the web form uses).
// ---------------------------------------------------------------------------
app.MapPost("/api/grades/calculate", ([FromBody] CalculateRequest? request) =>
{
    if (request?.Courses is null || request.Courses.Count == 0)
    {
        return Results.BadRequest(new { error = "At least one course is required." });
    }

    try
    {
        var result = GradeCalculator.Calculate(
            request.Courses.Select(c => new CourseEntry(c.Name, c.Credits, c.Score)));
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).WithName("CalculateGrades");

// Raw monitoring history, so the dashboard can refresh without a full page load.
app.MapGet("/api/monitoring/history", (MonitoringService monitoring) => Results.Ok(new
{
    logPath = monitoring.ResolvedLogPath,
    logExists = monitoring.LogExists,
    summary = monitoring.Read()
})).WithName("MonitoringHistory");

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
