namespace Backend_API.Models.Analytics;

// Stores generated PDF report metadata
// The actual PDF file is saved to disk
public class ExamReport
{
    public Guid ReportId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid GeneratedBy { get; set; }       // Teacher or Admin who requested it
    public string ReportType { get; set; } = string.Empty; // "ExamSummary" | "StudentDetail" | "PlagiarismReport"
    public string FilePath { get; set; } = string.Empty;   // Path on server disk
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User GeneratedByUser { get; set; } = null!;
}
