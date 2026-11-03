using devops_midterm.Services;

namespace devops_midterm.Tests;

/// <summary>
/// Unit tests for the pure grading logic. These are what the CI "test" stage runs.
/// </summary>
public class GradeCalculatorTests
{
    // ----------------------------------------------------------------- letters

    [Theory]
    [InlineData(100, "A")]
    [InlineData(91, "A")]
    [InlineData(90.9, "B")]
    [InlineData(81, "B")]
    [InlineData(80, "C")]
    [InlineData(71, "C")]
    [InlineData(70, "D")]
    [InlineData(61, "D")]
    [InlineData(60, "E")]
    [InlineData(51, "E")]
    [InlineData(50, "FX")]
    [InlineData(41, "FX")]
    [InlineData(40, "F")]
    [InlineData(0, "F")]
    public void LetterFor_maps_every_band_of_the_scale(double score, string expected)
    {
        Assert.Equal(expected, GradeCalculator.LetterFor(score));
    }

    [Theory]
    [InlineData(95, 4.0)]
    [InlineData(85, 3.0)]
    [InlineData(75, 2.0)]
    [InlineData(65, 1.0)]
    [InlineData(55, 0.5)]
    [InlineData(45, 0.0)]
    [InlineData(10, 0.0)]
    public void GradePointsFor_maps_letters_onto_the_four_point_scale(double score, double expected)
    {
        Assert.Equal(expected, GradeCalculator.GradePointsFor(score));
    }

    [Theory]
    [InlineData(51, true)]
    [InlineData(50.9, false)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public void IsPassing_uses_51_as_the_threshold(double score, bool expected)
    {
        Assert.Equal(expected, GradeCalculator.IsPassing(score));
    }

    // -------------------------------------------------------------- happy path

    [Fact]
    public void Calculate_returns_a_perfect_gpa_when_every_course_is_an_A()
    {
        var result = GradeCalculator.Calculate([
            new CourseEntry("DevOps", 6, 95),
            new CourseEntry("Networks", 4, 91)
        ]);

        Assert.Equal(4.0, result.Gpa);
        Assert.Equal("A", result.Letter);
        Assert.Equal(10, result.TotalCredits);
        Assert.Equal(10, result.EarnedCredits);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Calculate_weights_the_gpa_by_credits_not_by_course_count()
    {
        // 10 credits of A (4.0) and 2 credits of D (1.0):
        // (4.0*10 + 1.0*2) / 12 = 3.5 -- a plain average would have given 2.5.
        var result = GradeCalculator.Calculate([
            new CourseEntry("Heavy course", 10, 95),
            new CourseEntry("Light course", 2, 65)
        ]);

        Assert.Equal(3.5, result.Gpa);
        Assert.NotEqual(2.5, result.Gpa);
    }

    [Fact]
    public void Calculate_rounds_the_gpa_to_two_decimals()
    {
        // (4.0*3 + 3.0*4) / 7 = 3.42857...
        var result = GradeCalculator.Calculate([
            new CourseEntry("A course", 3, 93),
            new CourseEntry("B course", 4, 84)
        ]);

        Assert.Equal(3.43, result.Gpa);
    }

    [Fact]
    public void Calculate_reports_a_failed_transcript_and_only_counts_earned_credits()
    {
        var result = GradeCalculator.Calculate([
            new CourseEntry("Passed", 5, 72),
            new CourseEntry("Failed", 5, 38)
        ]);

        Assert.False(result.Passed);
        Assert.Equal(10, result.TotalCredits);
        Assert.Equal(5, result.EarnedCredits);
        Assert.Equal("F", result.Courses.Single(c => c.Name == "Failed").Letter);
        Assert.False(result.Courses.Single(c => c.Name == "Failed").Passed);
    }

    [Fact]
    public void Calculate_computes_the_credit_weighted_average_score_and_its_letter()
    {
        // (90*6 + 60*4) / 10 = 78 -> "C"
        var result = GradeCalculator.Calculate([
            new CourseEntry("Six credits", 6, 90),
            new CourseEntry("Four credits", 4, 60)
        ]);

        Assert.Equal(78, result.AverageScore);
        Assert.Equal("C", result.Letter);
    }

    [Fact]
    public void Calculate_trims_course_names()
    {
        var result = GradeCalculator.Calculate([new CourseEntry("  Spaced  ", 5, 80)]);

        Assert.Equal("Spaced", result.Courses[0].Name);
    }

    // -------------------------------------------------------------- validation

    [Fact]
    public void Calculate_rejects_a_null_course_list()
    {
        Assert.Throws<ArgumentNullException>(() => GradeCalculator.Calculate(null!));
    }

    [Fact]
    public void Calculate_rejects_an_empty_course_list()
    {
        Assert.Throws<ArgumentException>(() => GradeCalculator.Calculate([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Calculate_rejects_a_blank_course_name(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            GradeCalculator.Calculate([new CourseEntry(name, 5, 80)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(31)]
    public void Calculate_rejects_credits_outside_the_allowed_range(int credits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GradeCalculator.Calculate([new CourseEntry("Course", credits, 80)]));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    [InlineData(double.NaN)]
    public void Calculate_rejects_scores_outside_zero_to_one_hundred(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GradeCalculator.Calculate([new CourseEntry("Course", 5, score)]));
    }

    [Fact]
    public void Calculate_rejects_a_semester_over_sixty_credits()
    {
        var overloaded = Enumerable.Range(0, 3)
            .Select(i => new CourseEntry($"Course {i}", 25, 80))
            .ToList();

        var error = Assert.Throws<ArgumentException>(() => GradeCalculator.Calculate(overloaded));
        Assert.Contains("60 credits", error.Message);
    }

    [Fact]
    public void LetterFor_rejects_an_impossible_score()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GradeCalculator.LetterFor(120));
    }
}
