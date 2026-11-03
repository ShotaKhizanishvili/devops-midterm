using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using devops_midterm.Models;
using devops_midterm.Services;

namespace devops_midterm.Controllers;

public class HomeController(MonitoringService monitoring, DeploymentInfo deployment) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(TranscriptViewModel.WithSampleData());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(TranscriptViewModel model)
    {
        model.Courses ??= [];

        if (model.Courses.Count == 0)
        {
            model.ErrorMessage = "დაამატეთ მინიმუმ ერთი საგანი.";
            return View(model);
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            model.Result = GradeCalculator.Calculate(model.Courses.Select(c => c.ToEntry()));
        }
        catch (ArgumentException ex)
        {
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    /// <summary>Bonus: visual monitoring dashboard built from the health-check log file.</summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status()
    {
        ViewData["LogPath"] = monitoring.ResolvedLogPath;
        ViewData["Deployment"] = deployment;
        return View(monitoring.Read());
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
