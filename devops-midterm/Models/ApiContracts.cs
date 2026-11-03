namespace devops_midterm.Models;

/// <summary>Request body for <c>POST /api/grades/calculate</c>.</summary>
public sealed record CalculateRequest(IReadOnlyList<CourseDto> Courses);

public sealed record CourseDto(string Name, int Credits, double Score);
