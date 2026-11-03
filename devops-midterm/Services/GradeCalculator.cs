namespace devops_midterm.Services;

/// <summary>One course the student took: its name, credit weight and 0-100 score.</summary>
public sealed record CourseEntry(string Name, int Credits, double Score);

/// <summary>A single scored course after the grading scale has been applied.</summary>
public sealed record CourseGrade(
    string Name,
    int Credits,
    double Score,
    string Letter,
    double GradePoints,
    bool Passed);

/// <summary>The full result of grading a transcript.</summary>
public sealed record TranscriptResult(
    double Gpa,
    double AverageScore,
    string Letter,
    int TotalCredits,
    int EarnedCredits,
    bool Passed,
    IReadOnlyList<CourseGrade> Courses);

/// <summary>
/// Converts 0-100 course scores into the Georgian higher-education letter scale
/// (A, B, C, D, E, FX, F) and a credit-weighted GPA on a 4.0 scale.
///
/// This is the unit under test: it is deliberately pure (no I/O, no dependencies),
/// so the CI "test" stage can verify it in isolation.
/// </summary>
public static class GradeCalculator
{
    /// <summary>Lowest score that still earns credit.</summary>
    public const double PassingScore = 51d;

    /// <summary>Credits a single course may carry.</summary>
    public const int MinCourseCredits = 1;
    public const int MaxCourseCredits = 30;

    /// <summary>A semester cannot be loaded with more than this many credits.</summary>
    public const int MaxTotalCredits = 60;

    /// <summary>Returns the letter grade for a 0-100 score.</summary>
    public static string LetterFor(double score)
    {
        ValidateScore(score, nameof(score));

        return score switch
        {
            >= 91d => "A",
            >= 81d => "B",
            >= 71d => "C",
            >= 61d => "D",
            >= 51d => "E",
            >= 41d => "FX",
            _ => "F"
        };
    }

    /// <summary>Returns the 4.0-scale grade points for a 0-100 score.</summary>
    public static double GradePointsFor(double score) => LetterFor(score) switch
    {
        "A" => 4.0d,
        "B" => 3.0d,
        "C" => 2.0d,
        "D" => 1.0d,
        "E" => 0.5d,
        _ => 0.0d
    };

    /// <summary>True when the score earns credit for the course.</summary>
    public static bool IsPassing(double score)
    {
        ValidateScore(score, nameof(score));
        return score >= PassingScore;
    }

    /// <summary>
    /// Grades a whole transcript: per-course letters plus a credit-weighted GPA.
    /// Heavier courses pull the GPA harder, which is the behaviour the tests pin down.
    /// </summary>
    /// <exception cref="ArgumentNullException">The course list is null.</exception>
    /// <exception cref="ArgumentException">The list is empty, a name is blank, or the credit load is too high.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A score or credit value is outside its allowed range.</exception>
    public static TranscriptResult Calculate(IEnumerable<CourseEntry> courses)
    {
        ArgumentNullException.ThrowIfNull(courses);

        var list = courses.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one course is required.", nameof(courses));
        }

        var graded = new List<CourseGrade>(list.Count);
        var totalCredits = 0;

        foreach (var course in list)
        {
            if (string.IsNullOrWhiteSpace(course.Name))
            {
                throw new ArgumentException("Course name must not be empty.", nameof(courses));
            }

            if (course.Credits is < MinCourseCredits or > MaxCourseCredits)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(courses),
                    course.Credits,
                    $"Course '{course.Name}' must carry between {MinCourseCredits} and {MaxCourseCredits} credits.");
            }

            ValidateScore(course.Score, nameof(courses));

            totalCredits += course.Credits;

            graded.Add(new CourseGrade(
                Name: course.Name.Trim(),
                Credits: course.Credits,
                Score: course.Score,
                Letter: LetterFor(course.Score),
                GradePoints: GradePointsFor(course.Score),
                Passed: course.Score >= PassingScore));
        }

        if (totalCredits > MaxTotalCredits)
        {
            throw new ArgumentException(
                $"A semester cannot exceed {MaxTotalCredits} credits (got {totalCredits}).",
                nameof(courses));
        }

        var gpa = Round2(graded.Sum(c => c.GradePoints * c.Credits) / totalCredits);
        var averageScore = Round2(graded.Sum(c => c.Score * c.Credits) / totalCredits);

        return new TranscriptResult(
            Gpa: gpa,
            AverageScore: averageScore,
            Letter: LetterFor(averageScore),
            TotalCredits: totalCredits,
            EarnedCredits: graded.Where(c => c.Passed).Sum(c => c.Credits),
            Passed: graded.All(c => c.Passed),
            Courses: graded);
    }

    private static void ValidateScore(double score, string paramName)
    {
        if (double.IsNaN(score) || score is < 0d or > 100d)
        {
            throw new ArgumentOutOfRangeException(paramName, score, "Score must be between 0 and 100.");
        }
    }

    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
