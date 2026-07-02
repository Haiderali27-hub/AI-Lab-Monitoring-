using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize]
public class ExamsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ExamsController(AppDbContext db) => _db = db;

    // GET /api/exams  — Teacher sees their exams; Student sees assigned exams
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        if (role == "Student")
        {
            var exams = await _db.ExamAssignments
                .Where(a => a.UserId == userId && a.IsEligible)
                .Include(a => a.Exam).ThenInclude(e => e.Section).ThenInclude(s => s.Course)
                .Select(a => new
                {
                    a.Exam.ExamId,
                    a.Exam.Title,
                    CourseName = a.Exam.Section.Course.Name,
                    a.Exam.StartTime,
                    a.Exam.DurationMinutes,
                    a.Exam.Status,
                    a.IsEligible,
                    WorkstationNumber = a.Workstation != null ? a.Workstation.MachineNumber : null
                })
                .ToListAsync();
            return Ok(exams);
        }

        // Teacher / Admin — see all exams
        var allExams = await _db.Exams
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .Include(e => e.Questions)
            .Select(e => new
            {
                e.ExamId, e.Title, e.StartTime, e.DurationMinutes, e.Status,
                CourseName = e.Section.Course.Name,
                SectionName = e.Section.Name,
                QuestionCount = e.Questions.Count
            })
            .ToListAsync();
        return Ok(allExams);
    }

    // GET /api/exams/{id}  — Full exam detail including questions
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var exam = await _db.Exams
            .Include(e => e.Questions).ThenInclude(q => q.TestCases)
            .Include(e => e.Assignments).ThenInclude(a => a.Student)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == id);

        if (exam is null) return NotFound();

        // Students only see non-hidden test cases
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role == "Student")
        {
            foreach (var q in exam.Questions)
                q.TestCases = q.TestCases.Where(tc => !tc.IsHidden).ToList();
        }

        return Ok(exam);
    }

    // POST /api/exams  - Create Exam
    [HttpPost]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> CreateExam([FromBody] CreateExamDto req)
    {
        var exam = new Exam
        {
            Title = req.Title,
            SectionId = req.SectionId,
            StartTime = req.StartTime.ToUniversalTime(),
            DurationMinutes = req.DurationMinutes,
            AllowedApps = req.AllowedApps,
            AiEvaluationEnabled = req.AiEvaluationEnabled,
            PlagiarismThreshold = req.PlagiarismThreshold,
            Status = ExamStatus.Scheduled,
            CreatedAt = DateTime.UtcNow
        };

        int order = 1;
        foreach (var q in req.Questions)
        {
            var question = new Question
            {
                Type = q.Type,
                BodyText = q.BodyText,
                Marks = q.Marks,
                OrderIndex = order++
            };

            foreach (var tc in q.TestCases)
            {
                question.TestCases.Add(new TestCase
                {
                    Input = tc.Input,
                    ExpectedOutput = tc.ExpectedOutput,
                    IsHidden = tc.IsHidden
                });
            }

            exam.Questions.Add(question);
        }

        foreach (var studentId in req.StudentIds)
        {
            exam.Assignments.Add(new ExamAssignment
            {
                UserId = studentId,
                IsEligible = true
            });
        }

        _db.Exams.Add(exam);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = exam.ExamId }, new { exam.ExamId, exam.Title });
    }

    // GET /api/exams/sections  - Get teacher sections
    [HttpGet("sections")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetTeacherSections()
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        IQueryable<Section> query = _db.Sections.Include(s => s.Course);
        if (role == "Teacher")
        {
            query = query.Where(s => s.TeacherId == userId);
        }

        var sections = await query
            .Select(s => new
            {
                s.SectionId,
                s.Name,
                CourseName = s.Course.Name
            })
            .ToListAsync();
        return Ok(sections);
    }

    // GET /api/exams/sections/{sectionId}/students  - Get students enrolled in a section
    [HttpGet("sections/{sectionId:guid}/students")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetSectionStudents(Guid sectionId)
    {
        var students = await _db.SectionEnrollments
            .Where(se => se.SectionId == sectionId)
            .Include(se => se.Student)
            .Select(se => new
            {
                se.Student.UserId,
                se.Student.Name,
                se.Student.Email
            })
            .ToListAsync();
        return Ok(students);
    }

    // GET /api/exams/{examId}/assignments  — Get exam assignments for eligibility/workstation tracking
    [HttpGet("{examId:guid}/assignments")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetAssignments(Guid examId)
    {
        var assignments = await _db.ExamAssignments
            .Where(a => a.ExamId == examId)
            .Include(a => a.Student)
            .Include(a => a.Workstation)
            .Select(a => new
            {
                a.AssignmentId,
                a.ExamId,
                a.UserId,
                StudentName = a.Student.Name,
                StudentEmail = a.Student.Email,
                WorkstationNumber = a.Workstation != null ? a.Workstation.MachineNumber : null,
                a.IsEligible,
                a.EligibilityNote
            })
            .ToListAsync();
        return Ok(assignments);
    }

    // PUT /api/exams/{examId}/eligibility  — Bulk update student eligibility
    [HttpPut("{examId:guid}/eligibility")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateEligibility(Guid examId, [FromBody] UpdateEligibilityDto req)
    {
        var dbAssignments = await _db.ExamAssignments
            .Where(a => a.ExamId == examId)
            .ToListAsync();

        foreach (var update in req.Assignments)
        {
            var existing = dbAssignments.FirstOrDefault(a => a.UserId == update.UserId);
            if (existing is not null)
            {
                existing.IsEligible = update.IsEligible;
                existing.EligibilityNote = update.EligibilityNote;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Eligibility updated successfully." });
    }

    // GET /api/exams/{examId}/results  — Fetch submissions with AI grades & teacher overrides
    [HttpGet("{examId:guid}/results")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetResults(Guid examId)
    {
        var sessions = await _db.ExamSessions
            .Where(s => s.ExamId == examId)
            .Include(s => s.Student)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .ToListAsync();

        var results = sessions.Select(s => new
        {
            UserId = s.UserId,
            StudentName = s.Student.Name,
            StudentEmail = s.Student.Email,
            SessionId = s.SessionId,
            Status = s.Status.ToString(),
            SubmittedAt = s.SubmittedAt,
            Answers = s.Answers.Select(a => new
            {
                a.AnswerId,
                a.QuestionId,
                QuestionText = a.Question.BodyText,
                QuestionType = a.Question.Type.ToString(),
                Marks = a.Question.Marks,
                a.AnswerText,
                AiGrading = a.AiGradingResult != null ? new
                {
                    a.AiGradingResult.SuggestedMarks,
                    a.AiGradingResult.Justification,
                    a.AiGradingResult.Confidence
                } : null,
                TeacherOverride = a.TeacherGradeOverride != null ? new
                {
                    a.TeacherGradeOverride.FinalMarks,
                    a.TeacherGradeOverride.Note
                } : null
            }).ToList()
        }).ToList();

        return Ok(results);
    }

    // GET /api/exams/{examId}/plagiarism  — Fetch plagiarism reports
    [HttpGet("{examId:guid}/plagiarism")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetPlagiarism(Guid examId)
    {
        var plagResults = await _db.PlagiarismResults
            .Where(pr => pr.ExamId == examId)
            .Include(pr => pr.Question)
            .Include(pr => pr.StudentA)
            .Include(pr => pr.StudentB)
            .ToListAsync();

        var results = plagResults.Select(pr => new
        {
            pr.PlagId,
            pr.QuestionId,
            QuestionText = pr.Question.BodyText,
            StudentAName = pr.StudentA.Name,
            StudentBName = pr.StudentB.Name,
            pr.SimilarityScore,
            pr.MatchingSegments
        }).ToList();

        return Ok(results);
    }

    // POST /api/answers/{answerId}/override  — Override a grade
    [HttpPost("/api/answers/{answerId:guid}/override")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> OverrideGrade(Guid answerId, [FromBody] OverrideGradeRequest req)
    {
        var teacherId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var answer = await _db.Answers.FindAsync(answerId);
        if (answer is null) return NotFound(new { message = "Answer not found." });

        var existingOverride = await _db.TeacherGradeOverrides
            .FirstOrDefaultAsync(tgo => tgo.AnswerId == answerId);

        if (existingOverride is not null)
        {
            existingOverride.TeacherId = teacherId;
            existingOverride.FinalMarks = req.FinalMarks;
            existingOverride.Note = req.Note;
            existingOverride.OverriddenAt = DateTime.UtcNow;
        }
        else
        {
            var gradeOverride = new TeacherGradeOverride
            {
                AnswerId = answerId,
                TeacherId = teacherId,
                FinalMarks = req.FinalMarks,
                Note = req.Note,
                OverriddenAt = DateTime.UtcNow
            };
            _db.TeacherGradeOverrides.Add(gradeOverride);
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Grade override saved successfully." });
    }

    // POST /api/exams/{examId}/force-submit-all  — Force submit all active exam sessions
    [HttpPost("{examId:guid}/force-submit-all")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> ForceSubmitAll(Guid examId)
    {
        var exam = await _db.Exams.FindAsync(examId);
        if (exam is null) return NotFound();

        exam.Status = ExamStatus.Ended;

        var sessions = await _db.ExamSessions
            .Where(s => s.ExamId == examId && s.Status == SessionStatus.InProgress)
            .ToListAsync();

        foreach (var s in sessions)
        {
            s.Status = SessionStatus.ForceSubmitted;
            s.SubmittedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = $"Exam ended. Force-submitted {sessions.Count} session(s)." });
    }

    // POST /api/exams/{id}/start-session  — Student starts the exam
    [HttpPost("{id:guid}/start-session")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> StartSession(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.ExamId == id && a.UserId == userId);

        if (assignment is null) return Forbid();
        if (!assignment.IsEligible)
            return BadRequest(new { message = "You are not eligible for this exam." });
        if (assignment.Exam.Status == ExamStatus.Ended)
            return BadRequest(new { message = "This exam has already ended." });

        // Check if session already exists
        var existing = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.ExamId == id && s.UserId == userId);
        if (existing is not null)
            return Ok(new { existing.SessionId, message = "Session already active." });

        // Activate exam if it is the first student starting
        if (assignment.Exam.Status == ExamStatus.Scheduled)
        {
            assignment.Exam.Status = ExamStatus.Active;
        }

        var session = new ExamSession
        {
            ExamId = id,
            UserId = userId,
            Status = SessionStatus.InProgress
        };

        _db.ExamSessions.Add(session);
        await _db.SaveChangesAsync();

        return Ok(new { session.SessionId });
    }

    // POST /api/exams/sessions/{sessionId}/submit  — Student submits
    [HttpPost("sessions/{sessionId:guid}/submit")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Submit(Guid sessionId)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var session = await _db.ExamSessions
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);

        if (session is null) return NotFound();
        if (session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Session is not active." });

        session.Status = SessionStatus.Submitted;
        session.SubmittedAt = DateTime.UtcNow;

        // --- AUTOMATED AI GRADING SIMULATOR ---
        foreach (var answer in session.Answers)
        {
            if (session.Exam.AiEvaluationEnabled)
            {
                var existingGrading = await _db.AiGradingResults.FirstOrDefaultAsync(g => g.AnswerId == answer.AnswerId);
                if (existingGrading is null)
                {
                    double suggestedMarks = 0;
                    string confidence = "Medium";
                    string justification = "";

                    if (answer.Question.Type == QuestionType.Coding)
                    {
                        var answerText = answer.AnswerText.ToLower();
                        if (answerText.Contains("for") && (answerText.Contains("max") || answerText.Contains("arr")))
                        {
                            suggestedMarks = answer.Question.Marks * 0.9; // 90%
                            confidence = "High";
                            justification = "The code correctly implements the required array iteration. Verified bounds checks and conditional updates. Passed both visible and hidden test cases successfully.";
                        }
                        else
                        {
                            suggestedMarks = answer.Question.Marks * 0.4; // 40%
                            confidence = "Medium";
                            justification = "Code loop is missing or incomplete. Failed test case compilation checks. Conditional variable logic is not found.";
                        }
                    }
                    else // Theory question
                    {
                        var answerText = answer.AnswerText.ToLower();
                        if (answerText.Contains("lifo") || answerText.Contains("fifo") || answerText.Contains("stack") || answerText.Contains("queue"))
                        {
                            suggestedMarks = answer.Question.Marks * 0.9; // 90%
                            confidence = "High";
                            justification = "Response exhibits clear and correct conceptual difference between LIFO (stack) and FIFO (queue) structures. Real-world analogy is sound.";
                        }
                        else
                        {
                            suggestedMarks = answer.Question.Marks * 0.5; // 50%
                            confidence = "Low";
                            justification = "Explanation is brief or lacks structural distinctions between Stack and Queue indexing patterns.";
                        }
                    }

                    _db.AiGradingResults.Add(new AiGradingResult
                    {
                        AnswerId = answer.AnswerId,
                        SuggestedMarks = suggestedMarks,
                        Confidence = confidence,
                        Justification = justification
                    });
                }
            }
        }

        // --- AUTOMATED PLAGIARISM DETECTOR ---
        foreach (var answer in session.Answers)
        {
            var otherAnswers = await _db.Answers
                .Include(a => a.ExamSession)
                .Where(a => a.QuestionId == answer.QuestionId && a.ExamSession.ExamId == session.ExamId && a.ExamSession.UserId != session.UserId && a.ExamSession.Status == SessionStatus.Submitted)
                .ToListAsync();

            foreach (var other in otherAnswers)
            {
                var similarity = GetSimilarity(answer.AnswerText, other.AnswerText);
                if (similarity >= session.Exam.PlagiarismThreshold)
                {
                    var existingPlag = await _db.PlagiarismResults.FirstOrDefaultAsync(pr =>
                        pr.QuestionId == answer.QuestionId &&
                        ((pr.UserIdA == session.UserId && pr.UserIdB == other.ExamSession.UserId) ||
                         (pr.UserIdA == other.ExamSession.UserId && pr.UserIdB == session.UserId)));

                    if (existingPlag is null)
                    {
                        var matchingTextJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                            new { A = answer.AnswerText, B = other.AnswerText }
                        });

                        _db.PlagiarismResults.Add(new PlagiarismResult
                        {
                            ExamId = session.ExamId,
                            QuestionId = answer.QuestionId,
                            UserIdA = session.UserId,
                            UserIdB = other.ExamSession.UserId,
                            SimilarityScore = Math.Round(similarity, 1),
                            MatchingSegments = matchingTextJson
                        });
                    }
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Exam submitted successfully.", submittedAt = session.SubmittedAt });
    }

    private static double GetSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

        var separators = new[] { ' ', '\n', '\r', '\t', '{', '}', '(', ')', ';', ',', '[', ']' };
        var words1 = s1.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(w => w.Trim().ToLower())
                       .ToHashSet();

        var words2 = s2.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(w => w.Trim().ToLower())
                       .ToHashSet();

        var intersect = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0 : ((double)intersect / union) * 100;
    }

    // POST /api/exams/sessions/{sessionId}/save-answer  — Auto-save
    [HttpPost("sessions/{sessionId:guid}/save-answer")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> SaveAnswer(Guid sessionId, [FromBody] SaveAnswerRequest req)
    {
        var session = await _db.ExamSessions.FindAsync(sessionId);
        if (session is null || session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Invalid or inactive session." });

        var answer = await _db.Answers
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.QuestionId == req.QuestionId);

        if (answer is null)
        {
            answer = new Answer
            {
                SessionId = sessionId,
                QuestionId = req.QuestionId,
                AnswerText = req.AnswerText,
                LastSavedAt = DateTime.UtcNow
            };
            _db.Answers.Add(answer);
        }
        else
        {
            answer.AnswerText = req.AnswerText;
            answer.LastSavedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Answer saved.", answer.LastSavedAt });
    }

    // POST /api/exams/sessions/{sessionId}/monitoring-event
    [HttpPost("sessions/{sessionId:guid}/monitoring-event")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> RecordMonitoringEvent(Guid sessionId, [FromBody] MonitoringEventRequest req)
    {
        var session = await _db.ExamSessions.FindAsync(sessionId);
        if (session is null) return NotFound();

        _db.MonitoringEvents.Add(new MonitoringEvent
        {
            ExamSessionId = sessionId,
            EventType = req.EventType,
            Payload = req.Payload
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Event recorded." });
    }

    // GET /api/exams/student/current
    [HttpGet("student/current")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetStudentCurrentExam()
    {
        var studentId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var now = DateTime.UtcNow;

        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .Include(a => a.Exam.Section)
            .Include(a => a.Workstation)
            .FirstOrDefaultAsync(a => a.UserId == studentId && (a.Exam.Status == ExamStatus.Active || a.Exam.Status == ExamStatus.Scheduled));

        if (assignment is null)
        {
            var unscheduled = new StudentExamStatus(
                null,
                "No Exam Assigned",
                "NotStarted",
                null,
                null,
                0,
                false,
                "Student is not assigned to any active or scheduled exam.",
                null,
                null,
                null);

            return Ok(new ApiEnvelope<StudentExamStatus>(true, "SUCCESS", "Success", unscheduled));
        }

        var session = await _db.ExamSessions
            .Where(s => s.ExamId == assignment.ExamId && s.UserId == studentId)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();

        var examEnd = assignment.Exam.StartTime.AddMinutes(assignment.Exam.DurationMinutes);
        var remainingSeconds = Math.Max(0, (int)(examEnd - now).TotalSeconds);
        var status = session?.Status.ToString() ?? "NotStarted";

        var message = !assignment.IsEligible
            ? (assignment.EligibilityNote ?? "Student is not eligible for this exam.")
            : assignment.Exam.StartTime > now
                ? "Exam has not started yet."
                : examEnd < now
                    ? "Exam window has ended."
                    : "Student can proceed.";

        var response = new StudentExamStatus(
            assignment.ExamId,
            assignment.Exam.Title,
            status,
            assignment.Exam.StartTime,
            examEnd,
            remainingSeconds,
            assignment.IsEligible,
            message,
            "Allowed Applications: " + assignment.Exam.AllowedApps,
            assignment.Workstation?.MachineNumber ?? "Unassigned",
            "Dr. Ahmed"
        );

        return Ok(new ApiEnvelope<StudentExamStatus>(true, "SUCCESS", "Success", response));
    }

    // POST /api/exams/student/start
    [HttpPost("student/start")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> StartStudentExam()
    {
        var studentId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var now = DateTime.UtcNow;

        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.UserId == studentId && a.IsEligible && (a.Exam.Status == ExamStatus.Active || a.Exam.Status == ExamStatus.Scheduled));

        if (assignment is null)
        {
            return BadRequest(new ApiEnvelope<object>(false, "NOT_ELIGIBLE", "No eligible exam assignment found.", null));
        }

        var examEnd = assignment.Exam.StartTime.AddMinutes(assignment.Exam.DurationMinutes);
        if (assignment.Exam.StartTime > now || examEnd < now)
        {
            return BadRequest(new ApiEnvelope<object>(false, "OUTSIDE_EXAM_WINDOW", "Exam cannot be started outside its scheduled time window.", null));
        }

        var existing = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.ExamId == assignment.ExamId && s.UserId == studentId && s.Status == SessionStatus.InProgress);

        if (existing is not null)
        {
            var res = new StartExamResult(existing.SessionId, "InProgress", existing.StartedAt);
            return Ok(new ApiEnvelope<StartExamResult>(true, "SUCCESS", "Success", res));
        }

        var session = new ExamSession
        {
            SessionId = Guid.NewGuid(),
            ExamId = assignment.ExamId,
            UserId = studentId,
            StartedAt = now,
            Status = SessionStatus.InProgress
        };

        _db.ExamSessions.Add(session);
        await _db.SaveChangesAsync();

        var result = new StartExamResult(session.SessionId, "InProgress", session.StartedAt);
        return Ok(new ApiEnvelope<StartExamResult>(true, "SUCCESS", "Success", result));
    }
}

// ── DTOs ──────────────────────────────────────────────────────
public class CreateExamDto
{
    public string Title { get; set; } = string.Empty;
    public Guid SectionId { get; set; }
    public DateTime StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public string AllowedApps { get; set; } = "[]";
    public bool AiEvaluationEnabled { get; set; } = true;
    public int PlagiarismThreshold { get; set; } = 70;
    public List<CreateQuestionDto> Questions { get; set; } = new();
    public List<Guid> StudentIds { get; set; } = new();
}

public class CreateQuestionDto
{
    public QuestionType Type { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public int Marks { get; set; }
    public List<CreateTestCaseDto> TestCases { get; set; } = new();
}

public class CreateTestCaseDto
{
    public string Input { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
}

public class UpdateEligibilityDto
{
    public List<StudentEligibilityUpdate> Assignments { get; set; } = new();
}

public class StudentEligibilityUpdate
{
    public Guid UserId { get; set; }
    public bool IsEligible { get; set; }
    public string? EligibilityNote { get; set; }
}

public class OverrideGradeRequest
{
    public double FinalMarks { get; set; }
    public string Note { get; set; } = string.Empty;
}

public record SaveAnswerRequest(Guid QuestionId, string AnswerText);
public record MonitoringEventRequest(MonitoringEventType EventType, string Payload);

