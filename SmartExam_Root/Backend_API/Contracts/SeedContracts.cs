using System.ComponentModel.DataAnnotations;

namespace Backend_API.Contracts;

public record SeedDemoRequest(
    [property: Range(1, 10)] int TeacherCount = 1,
    [property: Range(1, 100)] int StudentCount = 3,
    [property: Range(1, 5)] int ExamCount = 1);

public record SeedDemoResponse(
    int TeachersCreated,
    int StudentsCreated,
    int ExamsCreated,
    int AssignmentsCreated);