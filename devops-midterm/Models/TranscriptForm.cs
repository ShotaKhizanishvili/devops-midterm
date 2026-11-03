using System.ComponentModel.DataAnnotations;
using devops_midterm.Services;

namespace devops_midterm.Models;

/// <summary>One row of the grade-entry form.</summary>
public sealed class CourseInput
{
    [Required(ErrorMessage = "საგნის სახელი სავალდებულოა")]
    [StringLength(80, ErrorMessage = "სახელი ძალიან გრძელია (მაქს. 80 სიმბოლო)")]
    public string Name { get; set; } = string.Empty;

    [Range(GradeCalculator.MinCourseCredits, GradeCalculator.MaxCourseCredits,
        ErrorMessage = "კრედიტი უნდა იყოს 1-დან 30-მდე")]
    public int Credits { get; set; } = 5;

    [Range(0, 100, ErrorMessage = "ქულა უნდა იყოს 0-დან 100-მდე")]
    public double Score { get; set; }

    public CourseEntry ToEntry() => new(Name, Credits, Score);
}

/// <summary>Everything the home page needs to render.</summary>
public sealed class TranscriptViewModel
{
    public List<CourseInput> Courses { get; set; } = [];

    public TranscriptResult? Result { get; set; }

    public string? ErrorMessage { get; set; }

    public static TranscriptViewModel WithSampleData() => new()
    {
        Courses =
        [
            new CourseInput { Name = "DevOps პრაქტიკა", Credits = 6, Score = 94 },
            new CourseInput { Name = "ალგორითმები", Credits = 5, Score = 78 },
            new CourseInput { Name = "მონაცემთა ბაზები", Credits = 4, Score = 65 }
        ]
    };
}
