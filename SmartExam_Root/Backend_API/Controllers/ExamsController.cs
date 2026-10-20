using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.DTOs;
using Backend_API.Services;
using Backend_API.Services.Grading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/exams")]
[Authorize]
public class ExamsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly IHubContext<MonitoringHub> _hub;
    private readonly SubmissionService _submissions;
    private readonly SeatGuard _seats;
    private readonly ExamAccessService _access;
    private readonly ExamGroupTracker _groups;

    public ExamsController(AppDbContext db, AuditService audit, IHubContext<MonitoringHub> hub, SubmissionService submissions,
        SeatGuard seats, ExamAccessService access, ExamGroupTracker groups)
    {
        _db = db;
        _audit = audit;
        _hub = hub;
        _submissions = submissions;
        _seats = seats;
        _access = access;
        _groups = groups;
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

    // GET /api/exams  — Student sees assigned exams; Invigilator only the exams they proctor; Teacher / Admin all
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
                    a.Exam.IsMock,
                    a.IsEligible,
                    WorkstationNumber = a.Workstation != null ? a.Workstation.MachineNumber : null
                })
                .ToListAsync();
            return Ok(exams);
        }

        // Teacher / Admin — see all exams. Invigilator (Module 7) — only exams with an ExamProctors row
        // for them, so the live-monitor picker never offers an exam they are not supervising.
        IQueryable<Exam> visible = _db.Exams;
        var proctored = _access.ProctoredExamIds(userId);
        if (role == "Invigilator")
            visible = visible.Where(e => proctored.Contains(e.ExamId));
        // Same rule as the hub's JoinExamGroup, so the live monitor can open an exam the caller may
        // actually watch (a teacher lists every exam but supervises only their own and assigned ones).
        var isAdmin = ExamAccessService.IsAdmin(User);
        var isTeacher = role == "Teacher";

        var allExams = await visible
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .Include(e => e.Questions)
            .Select(e => new
            {
                e.ExamId, e.Title, e.StartTime, e.DurationMinutes, e.Status,
                e.IsMock,
                e.SectionId,
                e.EarlyJoinMinutes,
                e.MinAttendancePercent,
                e.EnforceSeat,
                CourseName = e.Section.Course.Name,
                SectionName = e.Section.Name,
                QuestionCount = e.Questions.Count,
                CanSupervise = isAdmin || (isTeacher && e.Section.TeacherId == userId) || proctored.Contains(e.ExamId)
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

        // Students: only exams they are assigned to, only visible test cases, never the answer key.
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var isStudent = role == "Student";
        if (isStudent && exam.Assignments.All(a => a.UserId != CurrentUserId))
            return NotFound();

        // Invigilators (Module 7): only exams they are assigned to proctor — 404, so exam ids can't be probed.
        // They supervise but never grade, so they get the student view of the questions (no answer key).
        var isInvigilator = role == "Invigilator";
        if (isInvigilator && await _access.GetAsync(User, id) == ExamAccess.None)
            return NotFound();
        // Module 10 hardening: teachers still see every exam (GET /api/exams), but the answer key only of their own
        // sections' exams; a co-proctoring or unrelated teacher gets the proctor view, like an invigilator.
        var isForeignTeacher = role == "Teacher" && exam.Section?.TeacherId != CurrentUserId;
        var hideAnswerKey = isStudent || isInvigilator || isForeignTeacher;

        // Explicit projection — the raw EF graph included every assigned student's PasswordHash/Salt.
        return Ok(new
        {
            exam.ExamId,
            exam.Title,
            exam.Instructions,
            exam.SectionId,
            exam.StartTime,
            exam.DurationMinutes,
            exam.AllowedApps,
            exam.AiEvaluationEnabled,
            exam.PlagiarismThreshold,
            exam.LockdownMode,
            exam.BlockMultiMonitor,
            exam.RequireReadiness,
            exam.RequireWebcam,
            exam.RequireMic,
            exam.IsMock,
            exam.EarlyJoinMinutes,
            exam.MinAttendancePercent,
            exam.EnforceSeat,
            exam.ScreenshotIntervalSeconds,
            exam.WebcamIntervalSeconds,
            exam.Status,
            exam.CreatedAt,
            Section = exam.Section is null ? null : new
            {
                exam.Section.SectionId,
                exam.Section.Name,
                Course = exam.Section.Course is null ? null : new { exam.Section.Course.CourseId, exam.Section.Course.Name, exam.Section.Course.Code }
            },
            Questions = exam.Questions.OrderBy(q => q.OrderIndex).Select(q => new
            {
                q.QuestionId,
                q.ExamId,
                q.Type,
                q.BodyText,
                q.Marks,
                q.OrderIndex,
                q.OptionsJson,
                ExpectedOutput = hideAnswerKey ? null : q.ExpectedOutput,
                CorrectOptionIndex = hideAnswerKey ? null : q.CorrectOptionIndex,
                ReferenceAnswer = hideAnswerKey ? null : q.ReferenceAnswer,
                q.Language,
                RubricKeywords = hideAnswerKey ? null : q.RubricKeywords,
                TestCases = q.TestCases.Where(tc => !hideAnswerKey || !tc.IsHidden)
                    .OrderBy(tc => tc.OrderIndex)
                    .Select(tc => new
                    {
                        tc.TestCaseId, tc.QuestionId, tc.Input, tc.ExpectedOutput, tc.IsHidden,
                        tc.Weight, tc.TimeLimitMs, tc.IsEdgeCase, tc.Category, tc.IgnoreCase, tc.FloatTolerance, tc.OrderIndex
                    })
            }),
            Assignments = isStudent
                ? []
                : exam.Assignments.Select(a => (object)new
                {
                    a.AssignmentId,
                    a.UserId,
                    a.WorkstationId,
                    a.IsEligible,
                    a.EligibilityNote,
                    a.EligibilityOverridden,
                    a.EligibilitySource,
                    Student = a.Student is null ? null : new { a.Student.UserId, a.Student.Name, a.Student.Email }
                }).ToList()
        });
    }

    // POST /api/exams  - Create Exam
    [HttpPost]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> CreateExam([FromBody] CreateExamDto req)
    {
        var validationError = await ValidateExamPayloadAsync(req);
        if (validationError is not null) return validationError;

        var exam = new Exam
        {
            Title = req.Title,
            Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim(),
            SectionId = req.SectionId,
            StartTime = req.StartTime.ToUniversalTime(),
            DurationMinutes = req.DurationMinutes,
            AllowedApps = req.AllowedApps,
            AiEvaluationEnabled = req.AiEvaluationEnabled,
            PlagiarismThreshold = req.PlagiarismThreshold,
            LockdownMode = req.LockdownMode,
            BlockMultiMonitor = req.BlockMultiMonitor,
            RequireReadiness = req.RequireReadiness,
            RequireWebcam = req.RequireWebcam,
            RequireMic = req.RequireMic,
            IsMock = req.IsMock,
            EarlyJoinMinutes = req.EarlyJoinMinutes,
            MinAttendancePercent = req.MinAttendancePercent,
            EnforceSeat = req.EnforceSeat,
            ScreenshotIntervalSeconds = req.ScreenshotIntervalSeconds,
            WebcamIntervalSeconds = req.WebcamIntervalSeconds,
            Status = ExamStatus.Scheduled,
            CreatedAt = DateTime.UtcNow
        };

        MapQuestions(req.Questions, exam);

        foreach (var studentId in req.StudentIds.Distinct())
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

    // PUT /api/exams/{id}  — Edit a Scheduled exam (fields + questions + assigned students replaced)
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateExam(Guid id, [FromBody] CreateExamDto req)
    {
        var exam = await _db.Exams
            .Include(e => e.Section)
            .Include(e => e.Questions).ThenInclude(q => q.TestCases)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.ExamId == id);

        if (exam is null) return NotFound(new { message = "Exam not found." });
        // Module 10 hardening: only the section's teacher (or an admin) edits an exam.
        if (!CanManage(exam.Section))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage exams of your own sections." });
        if (exam.Status != ExamStatus.Scheduled)
            return Conflict(new { message = "Only scheduled exams can be edited. This exam is already " + exam.Status + "." });

        var validationError = await ValidateExamPayloadAsync(req);
        if (validationError is not null) return validationError;

        exam.Title = req.Title;
        exam.Instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim();
        exam.SectionId = req.SectionId;
        exam.StartTime = req.StartTime.ToUniversalTime();
        exam.DurationMinutes = req.DurationMinutes;
        exam.AllowedApps = req.AllowedApps;
        exam.AiEvaluationEnabled = req.AiEvaluationEnabled;
        exam.PlagiarismThreshold = req.PlagiarismThreshold;
        exam.LockdownMode = req.LockdownMode;
        exam.BlockMultiMonitor = req.BlockMultiMonitor;
        exam.RequireReadiness = req.RequireReadiness;
        exam.RequireWebcam = req.RequireWebcam;
        exam.RequireMic = req.RequireMic;
        exam.IsMock = req.IsMock;
        exam.EarlyJoinMinutes = req.EarlyJoinMinutes;
        exam.MinAttendancePercent = req.MinAttendancePercent;
        exam.EnforceSeat = req.EnforceSeat;
        exam.ScreenshotIntervalSeconds = req.ScreenshotIntervalSeconds;
        exam.WebcamIntervalSeconds = req.WebcamIntervalSeconds;

        // Replace questions (safe: Scheduled exams have no sessions/answers yet).
        // Materialize first — RemoveRange triggers EF fixup that mutates the live nav collections mid-iteration.
        var oldTestCases = exam.Questions.SelectMany(q => q.TestCases).ToList();
        var oldQuestions = exam.Questions.ToList();
        _db.TestCases.RemoveRange(oldTestCases);
        _db.Questions.RemoveRange(oldQuestions);
        exam.Questions.Clear();
        MapQuestions(req.Questions, exam);
        // Explicitly mark as Added — graph-discovered entities with pre-set Guid keys are otherwise inferred Modified
        _db.Questions.AddRange(exam.Questions);

        // Replace student assignments, preserving eligibility flags for students who remain
        var requested = req.StudentIds.Distinct().ToHashSet();
        var removed = exam.Assignments.Where(a => !requested.Contains(a.UserId)).ToList();
        _db.ExamAssignments.RemoveRange(removed);
        var existingIds = exam.Assignments.Where(a => requested.Contains(a.UserId)).Select(a => a.UserId).ToHashSet();
        foreach (var studentId in requested.Where(idNew => !existingIds.Contains(idNew)))
        {
            var newAssignment = new ExamAssignment { ExamId = exam.ExamId, UserId = studentId, IsEligible = true };
            _db.ExamAssignments.Add(newAssignment);
        }

        await _db.SaveChangesAsync();
        return Ok(new { exam.ExamId, exam.Title, message = "Exam updated successfully." });
    }

    // DELETE /api/exams/{id}  — Delete a Scheduled exam
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteExam(Guid id)
    {
        // Module 10 hardening: only the section's teacher (or an admin) deletes an exam.
        var (exam, denied) = await LoadManagedExamAsync(id);
        if (denied is not null) return denied;
        if (exam!.Status != ExamStatus.Scheduled)
            return Conflict(new { message = "Only scheduled exams can be deleted. This exam is already " + exam.Status + "." });

        // Proctor rows cascade in Postgres; removed explicitly so the InMemory provider leaves no orphans.
        _db.ExamProctors.RemoveRange(_db.ExamProctors.Where(p => p.ExamId == id));
        _db.Exams.Remove(exam);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Exam deleted successfully." });
    }

    public const int MaxEarlyJoinMinutes = 60;
    public const int MaxRubricKeywordsLength = 1000;

    // Shared validation for create + edit. Returns null when the payload is valid.
    private async Task<IActionResult?> ValidateExamPayloadAsync(CreateExamDto req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Title is required." });
        if (req.DurationMinutes <= 0)
            return BadRequest(new { message = "Duration must be a positive number of minutes." });
        if (req.PlagiarismThreshold is < 0 or > 100)
            return BadRequest(new { message = "Plagiarism threshold must be between 0 and 100." });
        if (req.EarlyJoinMinutes is < 0 or > MaxEarlyJoinMinutes)
            return BadRequest(new { message = $"Early join must be between 0 and {MaxEarlyJoinMinutes} minutes." });
        if (double.IsNaN(req.MinAttendancePercent) || req.MinAttendancePercent is < 0 or > 100)
            return BadRequest(new { message = "Minimum attendance must be between 0 and 100 percent." });
        if (req.ScreenshotIntervalSeconds is < Exam.MinCaptureIntervalSeconds or > Exam.MaxCaptureIntervalSeconds)
            return BadRequest(new { message = $"Screenshot interval must be between {Exam.MinCaptureIntervalSeconds} and {Exam.MaxCaptureIntervalSeconds} seconds." });
        if (req.WebcamIntervalSeconds is < Exam.MinCaptureIntervalSeconds or > Exam.MaxCaptureIntervalSeconds)
            return BadRequest(new { message = $"Webcam interval must be between {Exam.MinCaptureIntervalSeconds} and {Exam.MaxCaptureIntervalSeconds} seconds." });
        var section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.SectionId == req.SectionId);
        if (section is null)
            return NotFound(new { message = "Section not found." });
        // Module 10 hardening: a teacher creates exams for (or moves an exam into) their own sections only.
        if (!CanManage(section))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only create exams for your own sections." });
        if (req.Questions.Count == 0)
            return BadRequest(new { message = "An exam must contain at least one question." });

        for (int i = 0; i < req.Questions.Count; i++)
        {
            var q = req.Questions[i];
            var label = $"Question {i + 1}";
            if (string.IsNullOrWhiteSpace(q.BodyText))
                return BadRequest(new { message = $"{label}: question text is required." });
            if (q.Marks <= 0)
                return BadRequest(new { message = $"{label}: marks must be greater than 0." });

            if (q.Type == QuestionType.Mcq)
            {
                if (q.Options is null || q.Options.Count < 2)
                    return BadRequest(new { message = $"{label}: an MCQ needs at least 2 options." });
                if (q.Options.Any(string.IsNullOrWhiteSpace))
                    return BadRequest(new { message = $"{label}: MCQ options cannot be empty." });
                if (q.CorrectOptionIndex is null || q.CorrectOptionIndex < 0 || q.CorrectOptionIndex >= q.Options.Count)
                    return BadRequest(new { message = $"{label}: a valid correct option must be selected." });
                if (q.TestCases.Count > 0)
                    return BadRequest(new { message = $"{label}: MCQ questions cannot have test cases." });
            }
            else
            {
                if (q.Type == QuestionType.Coding && q.TestCases.Count == 0)
                    return BadRequest(new { message = $"{label}: a coding question needs at least one test case." });
                // Module 11: the sandbox compiles C and C++ (null = detect from the answer).
                if (q.Type == QuestionType.Coding && !string.IsNullOrWhiteSpace(q.Language) &&
                    CodeLanguages.Normalise(q.Language) is not (CodeLanguages.Cpp or CodeLanguages.C))
                    return BadRequest(new { message = $"{label}: language must be C or C++." });
                if (q.RubricKeywords is { Length: > MaxRubricKeywordsLength })
                    return BadRequest(new { message = $"{label}: rubric keywords are limited to {MaxRubricKeywordsLength} characters." });
                foreach (var tc in q.TestCases)
                {
                    if (string.IsNullOrWhiteSpace(tc.ExpectedOutput))
                        return BadRequest(new { message = $"{label}: every test case needs an expected output." });
                    if (double.IsNaN(tc.Weight) || tc.Weight <= 0 || tc.Weight > TestCase.MaxWeight)
                        return BadRequest(new { message = $"{label}: test case weights must be greater than 0 and at most {TestCase.MaxWeight}." });
                    if (tc.TimeLimitMs is < TestCase.MinTimeLimitMs or > TestCase.MaxTimeLimitMs)
                        return BadRequest(new { message = $"{label}: test case time limits must be between {TestCase.MinTimeLimitMs} and {TestCase.MaxTimeLimitMs} ms." });
                    if (tc.FloatTolerance is { } tol && (double.IsNaN(tol) || double.IsInfinity(tol) || tol < 0))
                        return BadRequest(new { message = $"{label}: the numeric tolerance must be zero or a positive number." });
                    if (tc.Category is { Length: > TestCase.MaxCategoryLength })
                        return BadRequest(new { message = $"{label}: test case categories are limited to {TestCase.MaxCategoryLength} characters." });
                }
            }
        }

        if (req.StudentIds.Count > 0)
        {
            var distinctIds = req.StudentIds.Distinct().ToList();
            var validCount = await _db.Users.CountAsync(u => distinctIds.Contains(u.UserId) && u.Role == UserRole.Student);
            if (validCount != distinctIds.Count)
                return BadRequest(new { message = "One or more student IDs are invalid." });
        }

        return null;
    }

    // Maps question DTOs onto an exam (create + edit)
    private static void MapQuestions(List<CreateQuestionDto> questions, Exam exam)
    {
        int order = 1;
        foreach (var q in questions)
        {
            var question = new Question
            {
                Type = q.Type,
                BodyText = q.BodyText,
                Marks = q.Marks,
                OrderIndex = order++,
                OptionsJson = q.Type == QuestionType.Mcq && q.Options is not null
                    ? System.Text.Json.JsonSerializer.Serialize(q.Options)
                    : null,
                CorrectOptionIndex = q.Type == QuestionType.Mcq ? q.CorrectOptionIndex : null,
                ReferenceAnswer = q.Type != QuestionType.Mcq && !string.IsNullOrWhiteSpace(q.ReferenceAnswer)
                    ? q.ReferenceAnswer.Trim()
                    : null,
                Language = q.Type == QuestionType.Coding ? CodeLanguages.Normalise(q.Language) : null,
                RubricKeywords = q.Type == QuestionType.Theory && !string.IsNullOrWhiteSpace(q.RubricKeywords)
                    ? q.RubricKeywords.Trim()
                    : null
            };

            if (q.Type != QuestionType.Mcq)
            {
                var tcOrder = 0;
                foreach (var tc in q.TestCases)
                {
                    question.TestCases.Add(new TestCase
                    {
                        Input = tc.Input,
                        ExpectedOutput = tc.ExpectedOutput,
                        IsHidden = tc.IsHidden,
                        Weight = tc.Weight,
                        TimeLimitMs = tc.TimeLimitMs,
                        IsEdgeCase = tc.IsEdgeCase,
                        Category = string.IsNullOrWhiteSpace(tc.Category) ? null : tc.Category.Trim(),
                        IgnoreCase = tc.IgnoreCase,
                        FloatTolerance = tc.FloatTolerance,
                        OrderIndex = tcOrder++
                    });
                }
            }

            exam.Questions.Add(question);
        }
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

    // ── Module 9: who may manage an exam's roster, eligibility and seats ──────
    // A teacher only manages exams of the sections they teach; admins any exam. Without this a
    // teacher could read another class's attendance or flip another teacher's eligibility decisions.

    private bool CanManage(Section section) =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value != "Teacher" || section.TeacherId == CurrentUserId;

    // (exam, null) when the caller may manage the exam; (null, 404/403 result) otherwise.
    private async Task<(Exam? exam, IActionResult? denied)> LoadManagedExamAsync(Guid examId)
    {
        var exam = await _db.Exams.Include(e => e.Section).FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return (null, NotFound(new { message = "Exam not found." }));
        if (!CanManage(exam.Section))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only manage exams of your own sections." }));
        return (exam, null);
    }

    // ── Module 7 / 10: proctor assignment (ExamProctors) ─────────────────────
    // The section's teacher (or an admin) assigns invigilators — or other teachers — to supervise an
    // exam. Only an assignment lets an invigilator list the exam, open it, read its readiness results
    // and join its live-monitor hub group (see ExamAccessService).

    private static object ProctorView(ExamProctor p, User u) => new
    {
        p.ExamId,
        u.UserId,
        u.Name,
        u.Email,
        Role = u.Role.ToString(),
        u.IsActive,
        p.AssignedAt,
        p.AssignedBy
    };

    // GET /api/exams/{examId}/proctors
    [HttpGet("{examId:guid}/proctors")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetProctors(Guid examId)
    {
        var (_, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var rows = await _db.ExamProctors
            .Where(p => p.ExamId == examId)
            .Include(p => p.User)
            .OrderBy(p => p.User.Name)
            .ToListAsync();
        return Ok(rows.Select(p => ProctorView(p, p.User)));
    }

    // POST /api/exams/{examId}/proctors  {userId} — 201 for a new assignment, 200 when it already exists (idempotent)
    [HttpPost("{examId:guid}/proctors")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> AssignProctor(Guid examId, [FromBody] AssignProctorRequest req)
    {
        var (exam, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var target = await _db.Users.FindAsync(req.UserId);
        if (target is null) return NotFound(new { message = "User not found." });
        if (target.Role is not (UserRole.Invigilator or UserRole.Teacher))
            return BadRequest(new { code = "INVALID_PROCTOR_ROLE", message = "Only invigilators and teachers can be assigned to supervise an exam." });
        if (!target.IsActive)
            return BadRequest(new { code = "USER_INACTIVE", message = "This account is deactivated." });
        if (target.UserId == exam!.Section.TeacherId)
            return BadRequest(new { code = "ALREADY_OWNER", message = "This teacher already supervises the exam as the section's teacher." });

        var existing = await _db.ExamProctors.FindAsync(examId, target.UserId);
        if (existing is not null) return Ok(ProctorView(existing, target));

        var row = new ExamProctor { ExamId = examId, UserId = target.UserId, AssignedBy = CurrentUserId, AssignedAt = DateTime.UtcNow };
        _db.ExamProctors.Add(row);
        _audit.Add(CurrentUserId, "PROCTOR_ASSIGNED", "Exam", examId.ToString(),
            new { proctorId = target.UserId, target.Email, role = target.Role.ToString() });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two concurrent assigns of the same person: the composite key let only one insert through.
            _db.ChangeTracker.Clear();
            var winner = await _db.ExamProctors.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ExamId == examId && p.UserId == target.UserId);
            if (winner is null) throw;
            return Ok(ProctorView(winner, target));
        }

        return CreatedAtAction(nameof(GetProctors), new { examId }, ProctorView(row, target));
    }

    // DELETE /api/exams/{examId}/proctors/{userId}
    [HttpDelete("{examId:guid}/proctors/{userId:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> UnassignProctor(Guid examId, Guid userId)
    {
        var (_, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var row = await _db.ExamProctors.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.ExamId == examId && p.UserId == userId);
        if (row is null) return NotFound(new { message = "This user is not assigned to the exam." });

        _db.ExamProctors.Remove(row);
        _audit.Add(CurrentUserId, "PROCTOR_UNASSIGNED", "Exam", examId.ToString(), new { proctorId = userId, row.User.Email });
        await _db.SaveChangesAsync();

        // A live monitor the proctor already has open stops receiving this exam's telemetry now,
        // not at its next reconnect (the hub only checks access on join). Not for the section's own
        // teacher, who still supervises the exam without the row.
        if (!await _db.Exams.AnyAsync(e => e.ExamId == examId && e.Section.TeacherId == userId))
            await _groups.RevokeAsync(userId, examId);
        return NoContent();
    }

    // GET /api/exams/{examId}/assignments  — Get exam assignments for eligibility/workstation tracking
    [HttpGet("{examId:guid}/assignments")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetAssignments(Guid examId)
    {
        var (exam, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;
        Guid? sectionId = exam!.SectionId;

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
                a.WorkstationId,
                WorkstationNumber = a.Workstation != null ? a.Workstation.MachineNumber : null,
                WorkstationHostName = a.Workstation != null ? a.Workstation.HostName : null,
                WorkstationIp = a.Workstation != null ? a.Workstation.IpAddress : null,
                a.IsEligible,
                a.EligibilityNote,
                a.EligibilityOverridden,
                a.EligibilitySource
            })
            .ToListAsync();

        // Module 9: attendance of the exam's section, joined in memory (one row per student)
        var attendance = sectionId is null
            ? new Dictionary<Guid, AttendanceRecord>()
            : await _db.AttendanceRecords.Where(r => r.SectionId == sectionId).ToDictionaryAsync(r => r.UserId);

        return Ok(assignments.Select(a =>
        {
            attendance.TryGetValue(a.UserId, out var rec);
            return new
            {
                a.AssignmentId, a.ExamId, a.UserId, a.StudentName, a.StudentEmail,
                a.WorkstationId, a.WorkstationNumber, a.WorkstationHostName, a.WorkstationIp,
                a.IsEligible, a.EligibilityNote, a.EligibilityOverridden, a.EligibilitySource,
                AttendedClasses = rec?.AttendedClasses,
                TotalClasses = rec?.TotalClasses,
                AttendancePercent = rec is null ? null : EligibilityCalculator.Percent(rec.AttendedClasses, rec.TotalClasses)
            };
        }));
    }

    // PUT /api/exams/{examId}/eligibility  — Bulk update student eligibility
    [HttpPut("{examId:guid}/eligibility")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateEligibility(Guid examId, [FromBody] UpdateEligibilityDto req)
    {
        var (_, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var dbAssignments = await _db.ExamAssignments
            .Where(a => a.ExamId == examId)
            .ToListAsync();

        int overridden = 0, cleared = 0;
        foreach (var update in req.Assignments)
        {
            var existing = dbAssignments.FirstOrDefault(a => a.UserId == update.UserId);
            if (existing is null) continue;

            if (update.ClearOverride)
            {
                // Back to attendance-driven: the next recalculation decides again.
                if (!existing.EligibilityOverridden) continue;
                existing.EligibilityOverridden = false;
                existing.EligibilitySource = null;
                cleared++;
                _audit.Add(CurrentUserId, "ELIGIBILITY_OVERRIDE_CLEARED", "ExamAssignment", existing.AssignmentId.ToString(),
                    new { examId, studentId = existing.UserId });
                continue;
            }

            // Only rows the teacher actually changed become manual overrides — the page sends every
            // row on Save, and flagging untouched rows would make recalculation skip the whole class.
            var note = string.IsNullOrWhiteSpace(update.EligibilityNote) ? null : update.EligibilityNote.Trim();
            var previousNote = string.IsNullOrWhiteSpace(existing.EligibilityNote) ? null : existing.EligibilityNote.Trim();
            if (existing.IsEligible == update.IsEligible && string.Equals(previousNote, note, StringComparison.Ordinal))
                continue;

            _audit.Add(CurrentUserId, "ELIGIBILITY_OVERRIDDEN", "ExamAssignment", existing.AssignmentId.ToString(), new
            {
                examId,
                studentId = existing.UserId,
                isEligible = update.IsEligible,
                note,
                previous = new { isEligible = existing.IsEligible, note = previousNote, source = existing.EligibilitySource?.ToString() }
            });
            existing.IsEligible = update.IsEligible;
            existing.EligibilityNote = note;
            existing.EligibilityOverridden = true;
            existing.EligibilitySource = EligibilitySource.Manual;
            overridden++;
        }

        // Thesis FR9: eligibility decisions must be logged
        _audit.Add(CurrentUserId, "ELIGIBILITY_UPDATED", "Exam", examId.ToString(),
            new { updatedCount = req.Assignments.Count, overridden, cleared });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Eligibility updated successfully.", overridden, cleared });
    }

    // POST /api/exams/{examId}/eligibility/recalculate — eligibility from imported attendance (Module 9)
    [HttpPost("{examId:guid}/eligibility/recalculate")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> RecalculateEligibility(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Section)
            .Include(e => e.Assignments)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return NotFound(new { message = "Exam not found." });
        if (!CanManage(exam.Section))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You can only recalculate eligibility for your own exams." });
        if (exam.Status == ExamStatus.Ended)
            return Conflict(new { message = "This exam has ended; eligibility can no longer be recalculated." });

        var records = await _db.AttendanceRecords
            .Where(r => r.SectionId == exam.SectionId)
            .ToDictionaryAsync(r => r.UserId);

        int computed = 0, skippedOverridden = 0, notComputed = 0, ineligible = 0;
        foreach (var a in exam.Assignments)
        {
            if (a.EligibilityOverridden)
            {
                skippedOverridden++;
                continue;
            }

            var hasRecord = records.TryGetValue(a.UserId, out var rec);
            var decision = EligibilityCalculator.Decide(hasRecord, rec?.AttendedClasses ?? 0, rec?.TotalClasses ?? 0, exam.MinAttendancePercent);
            a.EligibilityNote = decision.Note;
            if (!decision.Computed)
            {
                // No data: the existing decision stands (no division by zero, nothing to audit).
                notComputed++;
                continue;
            }

            a.IsEligible = decision.IsEligible!.Value;
            a.EligibilitySource = EligibilitySource.Attendance;
            computed++;
            if (!a.IsEligible) ineligible++;
            _audit.Add(CurrentUserId, "ELIGIBILITY_COMPUTED", "ExamAssignment", a.AssignmentId.ToString(), new
            {
                examId,
                studentId = a.UserId,
                percent = Math.Round(decision.Percent!.Value, 2),
                min = exam.MinAttendancePercent,
                isEligible = a.IsEligible
            });
        }

        _audit.Add(CurrentUserId, "ELIGIBILITY_RECALCULATED", "Exam", examId.ToString(),
            new { computed, ineligible, skippedOverridden, notComputed, min = exam.MinAttendancePercent });
        await _db.SaveChangesAsync();

        return Ok(new { computed, ineligible, skippedOverridden, notComputed, minAttendancePercent = exam.MinAttendancePercent });
    }

    // POST /api/exams/{examId}/assignments  — Add a student to an existing exam (Module 9)
    [HttpPost("{examId:guid}/assignments")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> AddAssignment(Guid examId, [FromBody] AddAssignmentRequest req)
    {
        var (exam, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;
        if (exam!.Status == ExamStatus.Ended)
            return Conflict(new { message = "Cannot add students to an ended exam." });

        var isStudent = await _db.Users.AnyAsync(u => u.UserId == req.UserId && u.Role == UserRole.Student);
        if (!isStudent) return BadRequest(new { message = "User is not a valid student." });

        if (await _db.ExamAssignments.AnyAsync(a => a.ExamId == examId && a.UserId == req.UserId))
            return Conflict(new { message = "Student is already assigned to this exam." });

        var assignment = new ExamAssignment { ExamId = examId, UserId = req.UserId, IsEligible = true };
        _db.ExamAssignments.Add(assignment);
        _audit.Add(CurrentUserId, "STUDENT_ASSIGNED", "ExamAssignment", assignment.AssignmentId.ToString(),
            new { examId, studentId = req.UserId });
        await _db.SaveChangesAsync();

        return Ok(new { assignment.AssignmentId, message = "Student assigned to exam." });
    }

    // DELETE /api/exams/{examId}/assignments/{userId}  — Remove a student from an exam (Module 9)
    [HttpDelete("{examId:guid}/assignments/{userId:guid}")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> RemoveAssignment(Guid examId, Guid userId)
    {
        var (_, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var assignment = await _db.ExamAssignments
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.UserId == userId);
        if (assignment is null) return NotFound(new { message = "Assignment not found." });

        // Preserve exam history — a student who already sat (or is sitting) can't be unassigned
        if (await _db.ExamSessions.AnyAsync(s => s.ExamId == examId && s.UserId == userId))
            return Conflict(new { message = "This student has an exam session. Mark them ineligible instead of removing." });

        _db.ExamAssignments.Remove(assignment);
        _audit.Add(CurrentUserId, "STUDENT_UNASSIGNED", "ExamAssignment", assignment.AssignmentId.ToString(),
            new { examId, studentId = userId });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Student removed from exam." });
    }

    // PUT /api/exams/{examId}/assignments/{userId}/workstation  — Seat mapping (Module 9)
    [HttpPut("{examId:guid}/assignments/{userId:guid}/workstation")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> AssignWorkstation(Guid examId, Guid userId, [FromBody] AssignWorkstationRequest req)
    {
        var (_, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        var assignment = await _db.ExamAssignments
            .FirstOrDefaultAsync(a => a.ExamId == examId && a.UserId == userId);
        if (assignment is null) return NotFound(new { message = "Assignment not found." });

        if (req.WorkstationId.HasValue)
        {
            var ws = await _db.Workstations.FindAsync(req.WorkstationId.Value);
            if (ws is null) return NotFound(new { message = "Workstation not found." });

            // Thesis seat-conflict detection: one seat per student within the same exam
            var seatTaken = await _db.ExamAssignments.AnyAsync(a =>
                a.ExamId == examId && a.WorkstationId == req.WorkstationId.Value && a.UserId != userId);
            if (seatTaken)
                return Conflict(new { message = $"Workstation {ws.MachineNumber} is already assigned to another student in this exam." });

            assignment.WorkstationId = req.WorkstationId.Value;
            _audit.Add(CurrentUserId, "WORKSTATION_ASSIGNED", "ExamAssignment", assignment.AssignmentId.ToString(),
                new { examId, studentId = userId, workstation = ws.MachineNumber });
        }
        else
        {
            assignment.WorkstationId = null;
            _audit.Add(CurrentUserId, "WORKSTATION_UNASSIGNED", "ExamAssignment", assignment.AssignmentId.ToString(),
                new { examId, studentId = userId });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Seat assignment updated." });
    }

    // POST /api/exams/{examId}/extend-time  — Grant extra minutes to a running/scheduled exam (Module 10)
    [HttpPost("{examId:guid}/extend-time")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> ExtendTime(Guid examId, [FromBody] ExtendTimeRequest req)
    {
        if (req.AdditionalMinutes is <= 0 or > 180)
            return BadRequest(new { message = "Additional minutes must be between 1 and 180." });

        // Module 10 hardening: the whole exam is extended by its section's teacher or an admin only (a proctor gives
        // one student extra minutes through the hub's ExtendStudentTime instead).
        var (exam, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;
        if (exam!.Status == ExamStatus.Ended)
            return Conflict(new { message = "Cannot extend an ended exam." });

        exam.DurationMinutes += req.AdditionalMinutes;
        _audit.Add(CurrentUserId, "EXAM_TIME_EXTENDED", "Exam", examId.ToString(),
            new { additionalMinutes = req.AdditionalMinutes, newDuration = exam.DurationMinutes });
        await _db.SaveChangesAsync();

        // Notify the live proctoring dashboard(s) watching this exam
        await _hub.Clients.Group($"exam:{examId}").SendAsync("TimeExtended", new
        {
            examId,
            additionalMinutes = req.AdditionalMinutes,
            newDurationMinutes = exam.DurationMinutes
        });

        return Ok(new { exam.ExamId, exam.DurationMinutes, message = $"Exam extended by {req.AdditionalMinutes} minutes." });
    }

    // ── Module 11: results, plagiarism and overrides are the exam owner's (or an admin's) — D-13 ──────────
    // Proctors supervise but never see or change grades; another section's teacher gets 403.

    private async Task<IActionResult?> DenyUnlessGraderAsync(Guid examId)
    {
        if (!await _db.Exams.AnyAsync(e => e.ExamId == examId)) return NotFound(new { message = "Exam not found." });
        return await _access.GetAsync(User, examId) >= ExamAccess.Owner
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = "Only the exam's teacher or an admin can see or change its grades." });
    }

    // GET /api/exams/{examId}/results  — Fetch submissions with AI grades (per-test-case detail) & teacher overrides
    [HttpGet("{examId:guid}/results")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetResults(Guid examId)
    {
        var denied = await DenyUnlessGraderAsync(examId);
        if (denied is not null) return denied;

        var sessions = await _db.ExamSessions
            .Where(s => s.ExamId == examId)
            .Include(s => s.Student)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult).ThenInclude(g => g!.TestCaseResults)
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
            // Module 6: the receipt the student was given on submit (same hash on both sides).
            ReceiptSha256 = s.ReceiptSha256,
            ReceiptAnswerCount = s.ReceiptAnswerCount,
            ExtraMinutes = s.ExtraMinutes,
            Answers = s.Answers.OrderBy(a => a.Question.OrderIndex).Select(a => new
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
                    a.AiGradingResult.Confidence,
                    // Module 11: Pending (queued) / Graded / Failed, what graded it, and the evidence behind it.
                    a.AiGradingResult.Status,
                    a.AiGradingResult.Engine,
                    a.AiGradingResult.CompileOutput,
                    a.AiGradingResult.DurationMs,
                    a.AiGradingResult.GradedAt,
                    a.AiGradingResult.LlmSummary,
                    Analysis = ParseJson(a.AiGradingResult.AnalysisJson),
                    // Staff see every case, hidden ones included (input, expected and actual output).
                    TestCaseResults = a.AiGradingResult.TestCaseResults.OrderBy(r => r.OrderIndex).Select(r => new
                    {
                        r.TestCaseId,
                        r.Name,
                        r.Input,
                        r.ExpectedOutput,
                        r.ActualOutput,
                        r.IsHidden,
                        r.IsEdgeCase,
                        r.Category,
                        r.Weight,
                        r.Status,
                        r.Passed,
                        r.ExitCode,
                        r.DurationMs,
                        r.CompareMode
                    }).ToList()
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

    private static System.Text.Json.JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // GET /api/exams/{examId}/plagiarism  — Fetch plagiarism reports
    [HttpGet("{examId:guid}/plagiarism")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> GetPlagiarism(Guid examId)
    {
        var denied = await DenyUnlessGraderAsync(examId);
        if (denied is not null) return denied;

        var plagResults = await _db.PlagiarismResults
            .Where(pr => pr.ExamId == examId && !pr.Exam.IsMock)
            .Include(pr => pr.Question)
            .Include(pr => pr.StudentA)
            .Include(pr => pr.StudentB)
            .OrderByDescending(pr => pr.SimilarityScore)
            .ToListAsync();

        var results = plagResults.Select(pr => new
        {
            pr.PlagId,
            pr.QuestionId,
            QuestionText = pr.Question.BodyText,
            QuestionType = pr.Question.Type.ToString(),
            pr.UserIdA,
            pr.UserIdB,
            StudentAName = pr.StudentA.Name,
            StudentBName = pr.StudentB.Name,
            pr.SimilarityScore,
            // JSON: [{ aStartLine, aEndLine, bStartLine, bEndLine, A, B }] (Module 11 winnowing segments).
            pr.MatchingSegments
        }).ToList();

        return Ok(results);
    }

    // POST /api/answers/{answerId}/override  — Override a grade (the final mark; the AI suggestion is kept for audit)
    [HttpPost("/api/answers/{answerId:guid}/override")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> OverrideGrade(Guid answerId, [FromBody] OverrideGradeRequest req)
    {
        var teacherId = CurrentUserId;

        var answer = await _db.Answers
            .Include(a => a.Question)
            .Include(a => a.ExamSession)
            .Include(a => a.AiGradingResult)
            .FirstOrDefaultAsync(a => a.AnswerId == answerId);
        if (answer is null) return NotFound(new { message = "Answer not found." });

        var denied = await DenyUnlessGraderAsync(answer.ExamSession.ExamId);
        if (denied is not null) return denied;
        if (double.IsNaN(req.FinalMarks) || req.FinalMarks < 0 || req.FinalMarks > answer.Question.Marks)
            return BadRequest(new { message = $"Final marks must be between 0 and {answer.Question.Marks}." });
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > MaxOverrideNoteLength })
            return BadRequest(new { message = $"The note is limited to {MaxOverrideNoteLength} characters." });

        var existingOverride = await _db.TeacherGradeOverrides
            .FirstOrDefaultAsync(tgo => tgo.AnswerId == answerId);
        var previous = existingOverride?.FinalMarks;

        if (existingOverride is not null)
        {
            existingOverride.TeacherId = teacherId;
            existingOverride.FinalMarks = req.FinalMarks;
            existingOverride.Note = note;
            existingOverride.OverriddenAt = DateTime.UtcNow;
        }
        else
        {
            var gradeOverride = new TeacherGradeOverride
            {
                AnswerId = answerId,
                TeacherId = teacherId,
                FinalMarks = req.FinalMarks,
                Note = note,
                OverriddenAt = DateTime.UtcNow
            };
            _db.TeacherGradeOverrides.Add(gradeOverride);
        }

        _audit.Add(teacherId, "GRADE_OVERRIDDEN", "Answer", answerId.ToString(), new
        {
            examId = answer.ExamSession.ExamId,
            sessionId = answer.SessionId,
            questionId = answer.QuestionId,
            finalMarks = req.FinalMarks,
            previousOverride = previous,
            aiSuggestion = answer.AiGradingResult?.SuggestedMarks
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = "Grade override saved successfully." });
    }

    public const int MaxOverrideNoteLength = 1000;

    // POST /api/exams/{examId}/force-submit-all  — Force submit all active exam sessions
    [HttpPost("{examId:guid}/force-submit-all")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> ForceSubmitAll(Guid examId)
    {
        // Module 10 hardening: ending the exam for everyone is the section's teacher's (or an admin's) call.
        var (exam, denied) = await LoadManagedExamAsync(examId);
        if (denied is not null) return denied;

        // End the exam first: from here on no save is accepted from anyone (extra time included).
        exam!.Status = ExamStatus.Ended;
        await _db.SaveChangesAsync();

        var sessionIds = await _db.ExamSessions
            .Where(s => s.ExamId == examId && s.Status == SessionStatus.InProgress)
            .Select(s => s.SessionId)
            .ToListAsync();

        var finalised = 0;
        foreach (var sessionId in sessionIds)
        {
            var s = await _db.ExamSessions.Include(x => x.Exam).FirstAsync(x => x.SessionId == sessionId);
            if (s.Status != SessionStatus.InProgress) continue;
            try
            {
                await _submissions.FinalizeAsync(s, SessionStatus.ForceSubmitted);
                finalised++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The student submitted (or the sweeper timed it out) in the same instant: already final.
                _db.ChangeTracker.Clear();
            }
        }

        return Ok(new { message = $"Exam ended. Force-submitted {finalised} session(s)." });
    }

    // POST /api/exams/{id}/start-session  — Student starts the exam
    [HttpPost("{id:guid}/start-session")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> StartSession(Guid id)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);

        var now = DateTime.UtcNow;

        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .Include(a => a.Workstation)
            .FirstOrDefaultAsync(a => a.ExamId == id && a.UserId == userId);

        if (assignment is null) return Forbid();
        if (!assignment.IsEligible)
            return BadRequest(new { message = "You are not eligible for this exam." });
        // A student with per-student extra time can still resume their running session after the exam's
        // own window closed; nobody can start a new one.
        if (SubmissionService.IsClosed(assignment.Exam, now) && await OpenSessionAsync(id, userId, now, withGrace: true) is null)
            return BadRequest(new { message = "This exam has already ended." });

        // Module 3: server-side start gate — the client's clock and Start button are never trusted.
        var opensAt = OpensAt(assignment.Exam);
        if (now < opensAt)
            return BadRequest(new
            {
                code = "EXAM_NOT_STARTED",
                message = NotStartedMessage(opensAt),
                startsAtUtc = assignment.Exam.StartTime,
                opensAtUtc = opensAt,
                serverTimeUtc = now
            });

        // Module 9: the seat is checked before "already active" so moving seats can't resume a session.
        var seatRefusal = await CheckSeatOnStartAsync(assignment);
        if (seatRefusal is not null) return seatRefusal;

        // Check if session already exists. Practice exams allow a fresh attempt after each submit.
        var existing = await _db.ExamSessions
            .Where(s => s.ExamId == id && s.UserId == userId &&
                        (!assignment.Exam.IsMock || s.Status == SessionStatus.InProgress))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
        if (existing is not null)
            return Ok(new { existing.SessionId, message = "Session already active." });

        if (assignment.Exam.RequireReadiness && !await HasFreshPassingReadinessAsync(id, userId))
            return BadRequest(new { code = "READINESS_REQUIRED", message = ReadinessRequiredMessage });

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
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);

        if (session is null) return NotFound();
        if (session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Session is not active.", status = session.Status.ToString() });

        // A late submit is still accepted so the session gets locked and graded — answers
        // themselves were already frozen at the deadline by save-answer.
        SubmissionReceipt receipt;
        try
        {
            receipt = await _submissions.FinalizeAsync(session, SessionStatus.Submitted);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Finalised in the same instant by the timeout sweeper or a proctor (Status is a concurrency token).
            return BadRequest(new { message = "Session is not active." });
        }

        // Module 6 receipt, flat: { message, submittedAt, answerCount, sha256 }. The desktop app recomputes the
        // hash over the answers it sent (Core/Submission/ReceiptHasher) and shows "Verified" or a mismatch.
        return Ok(new
        {
            message = "Exam submitted successfully.",
            sessionId = receipt.SessionId,
            submittedAt = receipt.SubmittedAt,
            answerCount = receipt.AnswerCount,
            sha256 = receipt.Sha256
        });
    }

    // Longest answer text accepted (a long program is a few thousand characters).
    public const int MaxAnswerLength = 100_000;

    private const string TimeOverMessage = "Exam time is over. Answers can no longer be changed.";

    // POST /api/exams/sessions/{sessionId}/save-answer  — Auto-save
    // Module 6: an optional `revision` (the client's edit counter for this answer) makes saves idempotent and
    // order-safe: a lower revision than the stored one is refused with 409 STALE_REVISION (an offline queue
    // replayed late cannot overwrite a newer answer); the same revision with the same text is a no-op 200
    // (replay); the same revision with different text is a 409 as well. Saves without a revision (older
    // clients, the web simulator) behave as before: last write wins, the stored revision is left unchanged.
    // `clientSavedAt` is stored for diagnostics only — the deadline is always the server's clock.
    [HttpPost("sessions/{sessionId:guid}/save-answer")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> SaveAnswer(Guid sessionId, [FromBody] SaveAnswerRequest req)
    {
        var userId = CurrentUserId;
        var session = await _db.ExamSessions.Include(s => s.Exam).FirstOrDefaultAsync(s => s.SessionId == sessionId);
        // Ownership check: a student may only write to their own session (prevents IDOR).
        if (session is null || session.UserId != userId)
            return NotFound(new { message = "Session not found." });
        // A session the timeout sweeper closed reports the deadline, not a generic "inactive", so a late
        // save racing the sweeper gets the same answer either way.
        if (session.Status == SessionStatus.TimedOut)
            return BadRequest(new { message = TimeOverMessage });
        if (session.Status != SessionStatus.InProgress)
            return BadRequest(new { message = "Invalid or inactive session." });
        // Server-side deadline (this student's: exam end + extra minutes + grace): the client timer alone can be bypassed.
        if (SubmissionService.IsClosed(session, DateTime.UtcNow))
            return BadRequest(new { message = TimeOverMessage });

        if (req.Revision is < 0)
            return BadRequest(new { message = "Revision must not be negative." });
        var text = req.AnswerText ?? string.Empty;
        if (text.Length > MaxAnswerLength)
            return BadRequest(new { message = $"Answer is too long (maximum {MaxAnswerLength:N0} characters)." });
        // The question must belong to this session's exam: a client cannot plant answers to another exam's questions.
        if (!await _db.Questions.AnyAsync(q => q.QuestionId == req.QuestionId && q.ExamId == session.ExamId))
            return BadRequest(new { message = "Question does not belong to this exam." });

        // Two attempts: a racing save of the same answer (unique (SessionId, QuestionId) on insert, the
        // Revision concurrency token on update) is re-evaluated against the row that won.
        for (var attempt = 1; ; attempt++)
        {
            var answer = await _db.Answers
                .FirstOrDefaultAsync(a => a.SessionId == sessionId && a.QuestionId == req.QuestionId);

            if (answer is not null && req.Revision is int revision)
            {
                if (answer.Revision > revision || (answer.Revision == revision && answer.AnswerText != text))
                    return Conflict(new
                    {
                        code = "STALE_REVISION",
                        message = "A newer version of this answer is already saved.",
                        currentRevision = answer.Revision,
                        lastSavedAt = answer.LastSavedAt
                    });
                if (answer.Revision == revision)
                    return Ok(new { message = "Answer already saved.", answer.LastSavedAt, revision = answer.Revision });
            }

            var now = DateTime.UtcNow;
            if (answer is null)
            {
                answer = new Answer
                {
                    SessionId = sessionId,
                    QuestionId = req.QuestionId,
                    AnswerText = text,
                    Revision = req.Revision ?? 0,
                    ClientSavedAt = req.ClientSavedAt,
                    LastSavedAt = now
                };
                _db.Answers.Add(answer);
            }
            else
            {
                answer.AnswerText = text;
                if (req.Revision is int newRevision) answer.Revision = newRevision;
                answer.ClientSavedAt = req.ClientSavedAt;
                answer.LastSavedAt = now;
            }

            try
            {
                await _db.SaveChangesAsync();
                return Ok(new { message = "Answer saved.", answer.LastSavedAt, revision = answer.Revision });
            }
            catch (DbUpdateException) when (attempt < 2)
            {
                _db.Entry(answer).State = EntityState.Detached;
            }
        }
    }

    // GET /api/exams/sessions/{sessionId}/answers  — the student's own saved answers. Module 6 crash recovery:
    // on resume the desktop app merges these with its encrypted local backup (higher revision wins).
    [HttpGet("sessions/{sessionId:guid}/answers")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetSessionAnswers(Guid sessionId)
    {
        var session = await _db.ExamSessions.AsNoTracking().Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == CurrentUserId);
        // 404 (not 403) for someone else's session, as save-answer does.
        if (session is null) return NotFound(new { message = "Session not found." });

        var answers = await _db.Answers.AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.LastSavedAt)
            .Select(a => new { a.QuestionId, a.AnswerText, a.Revision, a.LastSavedAt })
            .ToListAsync();

        return Ok(new
        {
            session.SessionId,
            Status = session.Status.ToString(),
            RemainingSeconds = session.Status == SessionStatus.InProgress ? SubmissionService.RemainingSeconds(session, DateTime.UtcNow) : 0,
            session.ExtraMinutes,
            Receipt = session.ReceiptSha256 is null ? null : new
            {
                submittedAt = session.SubmittedAt,
                answerCount = session.ReceiptAnswerCount ?? 0,
                sha256 = session.ReceiptSha256
            },
            Answers = answers
        });
    }

    // POST /api/exams/sessions/{sessionId}/monitoring-event
    [HttpPost("sessions/{sessionId:guid}/monitoring-event")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> RecordMonitoringEvent(Guid sessionId, [FromBody] MonitoringEventRequest req)
    {
        var userId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!);
        var session = await _db.ExamSessions.FindAsync(sessionId);
        // Ownership check: a student may only record events on their own session (prevents IDOR).
        if (session is null || session.UserId != userId) return NotFound();

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

        // A student can hold several assignments (old demo exams, past exams whose status
        // was never flipped, plus the real upcoming one). Pick the most relevant:
        //   1. an Active exam whose window is still open,
        //   2. else the Scheduled exam with the NEAREST end-time still in the future,
        //   3. else fall back to the most recent past exam (so the "window ended" message shows).
        // Module 6: a session the student is still running (inside their own extra time) wins over all three.
        var candidates = await _db.ExamAssignments
            .Include(a => a.Exam)
            .Include(a => a.Exam.Section).ThenInclude(s => s.Teacher)
            .Include(a => a.Workstation).ThenInclude(w => w!.Lab)
            .Where(a => a.UserId == studentId && !a.Exam.IsMock && (a.Exam.Status == ExamStatus.Active || a.Exam.Status == ExamStatus.Scheduled))
            .ToListAsync();

        var assignment =
            await RunningAssignmentAsync(candidates, studentId, now)
            ?? candidates
                .Where(a => a.Exam.Status == ExamStatus.Active &&
                            a.Exam.StartTime.AddMinutes(a.Exam.DurationMinutes) > now)
                .OrderBy(a => a.Exam.StartTime)
                .FirstOrDefault()
            ?? candidates
                .Where(a => a.Exam.StartTime.AddMinutes(a.Exam.DurationMinutes) > now)
                .OrderBy(a => a.Exam.StartTime)
                .FirstOrDefault()
            ?? candidates
                .OrderByDescending(a => a.Exam.StartTime)
                .FirstOrDefault();

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

        // Module 6: once a session exists the clock is this student's own (exam end + extra minutes).
        var examEnd = assignment.Exam.StartTime.AddMinutes(assignment.Exam.DurationMinutes);
        if (session is { Status: SessionStatus.InProgress })
        {
            session.Exam = assignment.Exam;
            examEnd = SubmissionService.EffectiveEnd(session);
        }
        var remainingSeconds = assignment.Exam.Status == ExamStatus.Ended ? 0 : Math.Max(0, (int)(examEnd - now).TotalSeconds);
        var status = session?.Status.ToString() ?? "NotStarted";

        var opensAt = OpensAt(assignment.Exam);
        var secondsUntilStart = Math.Max(0, (int)Math.Ceiling((opensAt - now).TotalSeconds));
        var seat = _seats.Check(assignment, User, HttpContext);
        var readinessPassed = await HasFreshPassingReadinessAsync(assignment.ExamId, studentId);

        var message = !assignment.IsEligible
            ? (assignment.EligibilityNote ?? "Student is not eligible for this exam.")
            : seat.IsMismatch
                ? seat.StudentMessage
                : now < opensAt
                    ? "Exam has not started yet."
                    : examEnd < now
                        ? "Exam window has ended."
                        : "Student can proceed.";

        // What a start request would do right now (same rules as student/start).
        var canStartNow = assignment.IsEligible &&
                          now >= opensAt && now < examEnd && assignment.Exam.Status != ExamStatus.Ended &&
                          !seat.IsMismatch &&
                          (!assignment.Exam.RequireReadiness || readinessPassed) &&
                          (session is null || session.Status == SessionStatus.InProgress);

        var response = new StudentExamStatus(
            assignment.ExamId,
            assignment.Exam.Title,
            status,
            assignment.Exam.StartTime,
            examEnd,
            remainingSeconds,
            assignment.IsEligible,
            message,
            string.IsNullOrWhiteSpace(assignment.Exam.Instructions) ? "No instructions provided." : assignment.Exam.Instructions,
            assignment.Workstation?.Lab?.Name ?? "Unassigned",
            assignment.Exam.Section?.Teacher?.Name ?? "Not assigned",
            assignment.Workstation?.MachineNumber ?? "Unassigned",
            assignment.Exam.AllowedApps,
            assignment.Exam.LockdownMode.ToString(),
            assignment.Exam.BlockMultiMonitor,
            assignment.Exam.RequireReadiness,
            assignment.Exam.RequireWebcam,
            assignment.Exam.RequireMic,
            readinessPassed,
            now,
            secondsUntilStart,
            opensAt,
            assignment.Exam.EarlyJoinMinutes,
            canStartNow,
            seat.Status.ToString(),
            assignment.Exam.ScreenshotIntervalSeconds,
            assignment.Exam.WebcamIntervalSeconds
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

        // Mirror the selection logic of GET /student/current so the student starts
        // exactly the exam their dashboard is showing.
        var startCandidates = await _db.ExamAssignments
            .Include(a => a.Exam)
            .Include(a => a.Workstation)
            .Where(a => a.UserId == studentId && a.IsEligible && !a.Exam.IsMock && (a.Exam.Status == ExamStatus.Active || a.Exam.Status == ExamStatus.Scheduled))
            .ToListAsync();

        var assignment =
            await RunningAssignmentAsync(startCandidates, studentId, now)
            ?? startCandidates
                .Where(a => a.Exam.StartTime.AddMinutes(a.Exam.DurationMinutes) > now)
                .OrderBy(a => a.Exam.StartTime)
                .FirstOrDefault()
            ?? startCandidates.OrderByDescending(a => a.Exam.StartTime).FirstOrDefault();

        if (assignment is null)
        {
            return BadRequest(new ApiEnvelope<object>(false, "NOT_ELIGIBLE", "No eligible exam assignment found.", null));
        }

        var examEnd = assignment.Exam.StartTime.AddMinutes(assignment.Exam.DurationMinutes);
        // Resume (crash recovery) stays possible until this student's own deadline (extra minutes included).
        if ((assignment.Exam.Status == ExamStatus.Ended || examEnd < now) &&
            await OpenSessionAsync(assignment.ExamId, studentId, now, withGrace: false) is null)
            return BadRequest(new ApiEnvelope<object>(false, "EXAM_CLOSED", "This exam has already ended.", null));

        var opensAt = OpensAt(assignment.Exam);
        if (now < opensAt)
            return BadRequest(new ApiEnvelope<object>(false, "EXAM_NOT_STARTED", NotStartedMessage(opensAt),
                new { startsAtUtc = assignment.Exam.StartTime, opensAtUtc = opensAt, serverTimeUtc = now }));

        var seatRefusal = await CheckSeatOnStartAsync(assignment);
        if (seatRefusal is not null) return seatRefusal;

        var existing = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.ExamId == assignment.ExamId && s.UserId == studentId && s.Status == SessionStatus.InProgress);

        if (existing is not null)
        {
            var res = new StartExamResult(existing.SessionId, "InProgress", existing.StartedAt);
            return Ok(new ApiEnvelope<StartExamResult>(true, "SUCCESS", "Success", res));
        }

        if (assignment.Exam.RequireReadiness && !await HasFreshPassingReadinessAsync(assignment.ExamId, studentId))
            return BadRequest(new ApiEnvelope<object>(false, "READINESS_REQUIRED", ReadinessRequiredMessage, null));

        // Activate the exam on the first student start (as start-session does) so it
        // appears on the proctor's live monitor.
        if (assignment.Exam.Status == ExamStatus.Scheduled)
            assignment.Exam.Status = ExamStatus.Active;

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

    // ── Module 3 / 9: start gates shared by start-session and student/start ────

    /// Module 6: the candidate assignment whose session this student is still running and may resume (exam not
    /// ended, own deadline incl. extra minutes ahead). student/current and student/start prefer it over their
    /// window-based pick, so a student working in extra time after the exam's own end is not pointed at their
    /// next scheduled exam instead (which would hide "Resume Exam" and refuse the resume as not started yet).
    private async Task<ExamAssignment?> RunningAssignmentAsync(IReadOnlyCollection<ExamAssignment> candidates, Guid studentId, DateTime now)
    {
        if (candidates.Count == 0) return null;
        var examIds = candidates.Select(a => a.ExamId).ToList();
        var running = await _db.ExamSessions.AsNoTracking().Include(s => s.Exam)
            .Where(s => s.UserId == studentId && s.Status == SessionStatus.InProgress && examIds.Contains(s.ExamId))
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();
        var session = running.FirstOrDefault(s => s.Exam.Status != ExamStatus.Ended && now <= SubmissionService.EffectiveEnd(s));
        return session is null ? null : candidates.First(a => a.ExamId == session.ExamId);
    }

    /// The student's in-progress session of this exam while it can still be resumed: the exam was not
    /// ended and the student's own deadline (exam end + extra minutes, plus the grace when asked) is ahead.
    private async Task<ExamSession?> OpenSessionAsync(Guid examId, Guid userId, DateTime now, bool withGrace)
    {
        var session = await _db.ExamSessions.AsNoTracking().Include(s => s.Exam)
            .Where(s => s.ExamId == examId && s.UserId == userId && s.Status == SessionStatus.InProgress)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
        if (session is null || session.Exam.Status == ExamStatus.Ended) return null;
        var deadline = SubmissionService.EffectiveEnd(session) + (withGrace ? SubmissionService.DeadlineGrace : TimeSpan.Zero);
        return now <= deadline ? session : null;
    }

    /// When start-session starts accepting: StartTime minus the exam's early-join window.
    public static DateTime OpensAt(Exam exam) => exam.StartTime.AddMinutes(-Math.Max(0, exam.EarlyJoinMinutes));

    private static string NotStartedMessage(DateTime opensAtUtc) =>
        $"The exam has not started yet. It opens at {opensAtUtc:yyyy-MM-dd HH:mm} UTC.";

    /// <summary>
    /// Desktop-client requirement + seat enforcement. Returns the 403 to send, or null to continue.
    /// A mismatch is audited (SEAT_MISMATCH) and raised on the proctors' live monitor; no session is created.
    /// </summary>
    private async Task<IActionResult?> CheckSeatOnStartAsync(ExamAssignment assignment)
    {
        if (_seats.DesktopClientRequired && !SeatGuard.IsDesktopClient(User))
            return StatusCode(StatusCodes.Status403Forbidden, new ApiEnvelope<object>(false, "DESKTOP_CLIENT_REQUIRED",
                "Exams can only be started from the SmartExam desktop app on a lab workstation.", null));

        var seat = _seats.Check(assignment, User, HttpContext);
        if (!seat.IsMismatch) return null;

        await _seats.ReportMismatchAsync(assignment.ExamId, assignment.UserId, null, seat, "start");
        await _db.SaveChangesAsync();
        return StatusCode(StatusCodes.Status403Forbidden, new ApiEnvelope<object>(false, SeatGuard.MismatchCode, seat.StudentMessage, null));
    }

    // ── Module 2: pre-exam readiness + practice exams ─────────────────────────

    // A passing check unlocks start for this long (thesis FR2: verify shortly before the exam).
    public static readonly TimeSpan ReadinessValidity = TimeSpan.FromMinutes(30);
    private const string ReadinessRequiredMessage =
        "Run the pre-exam system check on this computer and pass it before starting the exam.";

    private async Task<bool> HasFreshPassingReadinessAsync(Guid examId, Guid userId)
    {
        var latest = await _db.ReadinessChecks
            .Where(r => r.ExamId == examId && r.UserId == userId)
            .OrderByDescending(r => r.CheckedAt)
            .FirstOrDefaultAsync();
        return latest is not null && latest.PassedAll && latest.CheckedAt >= DateTime.UtcNow - ReadinessValidity;
    }

    // POST /api/exams/{id}/readiness  — desktop client reports its pre-exam system check
    [HttpPost("{id:guid}/readiness")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> SubmitReadiness(Guid id, [FromBody] ReadinessSubmitRequest req)
    {
        var userId = CurrentUserId;
        var assignment = await _db.ExamAssignments
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.ExamId == id && a.UserId == userId);
        if (assignment is null) return NotFound(new { message = "Exam not found." });

        var results = (req.Results ?? new()).Take(20).Select(r => new ReadinessItem(
            (r.Name ?? "").Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 64)] : "Unnamed",
            r.Passed,
            r.Detail is null ? null : r.Detail[..Math.Min(r.Detail.Length, 500)],
            Math.Max(0, r.DurationMs))).ToList();

        // The server decides "passed": every reported item must pass, and the devices this
        // exam requires must have been checked — a client can't skip a check to unlock start.
        var missing = new List<string>();
        if (assignment.Exam.RequireWebcam && !results.Any(r => r.Name == "Camera" && r.Passed)) missing.Add("Camera");
        if (assignment.Exam.RequireMic && !results.Any(r => r.Name == "Microphone" && r.Passed)) missing.Add("Microphone");
        var passedAll = req.PassedAll && results.Count > 0 && results.All(r => r.Passed) && missing.Count == 0;

        var check = new ReadinessCheck
        {
            ExamId = id,
            UserId = userId,
            PassedAll = passedAll,
            ResultsJson = System.Text.Json.JsonSerializer.Serialize(results),
            MachineName = req.MachineName is null ? null : req.MachineName[..Math.Min(req.MachineName.Length, 128)],
            CheckedAt = DateTime.UtcNow
        };
        _db.ReadinessChecks.Add(check);
        _audit.Add(userId, passedAll ? "READINESS_PASSED" : "READINESS_FAILED", "Exam", id.ToString(), new
        {
            machine = check.MachineName,
            failed = results.Where(r => !r.Passed).Select(r => r.Name).Concat(missing).ToList()
        });
        await _db.SaveChangesAsync();

        await _hub.Clients.Group($"exam:{id}").SendAsync("ReadinessUpdated", new
        {
            examId = id, userId, passedAll, checkedAt = check.CheckedAt, machineName = check.MachineName
        });

        return Ok(new
        {
            check.Id,
            passedAll,
            check.CheckedAt,
            validUntil = check.CheckedAt + ReadinessValidity,
            missingRequired = missing
        });
    }

    // GET /api/exams/{id}/readiness  — staff: latest readiness result per assigned student
    [HttpGet("{id:guid}/readiness")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
    public async Task<IActionResult> GetReadiness(Guid id)
    {
        if (!await _db.Exams.AnyAsync(e => e.ExamId == id)) return NotFound(new { message = "Exam not found." });
        // Section teacher, assigned proctor or admin only (Module 7 scoping).
        if (await _access.GetAsync(User, id) == ExamAccess.None)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "You are not assigned to supervise this exam." });

        var checks = await _db.ReadinessChecks
            .Where(r => r.ExamId == id)
            .Include(r => r.User)
            .ToListAsync();

        var cutoff = DateTime.UtcNow - ReadinessValidity;
        var latest = checks
            .GroupBy(r => r.UserId)
            .Select(g => g.OrderByDescending(r => r.CheckedAt).First())
            .Select(r => new
            {
                r.UserId,
                StudentName = r.User.Name,
                r.PassedAll,
                r.CheckedAt,
                IsFresh = r.CheckedAt >= cutoff,
                r.MachineName,
                Results = System.Text.Json.JsonSerializer.Deserialize<List<ReadinessItem>>(r.ResultsJson) ?? new()
            })
            .OrderBy(r => r.StudentName)
            .ToList();

        return Ok(latest);
    }

    // GET /api/exams/student/practice  — practice (mock) exams this student can rehearse with
    [HttpGet("student/practice")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetPracticeExams()
    {
        var studentId = CurrentUserId;
        var now = DateTime.UtcNow;

        var exams = await _db.Exams
            .Include(e => e.Questions)
            .Where(e => e.IsMock && e.Status != ExamStatus.Ended &&
                        e.Assignments.Any(a => a.UserId == studentId && a.IsEligible))
            .ToListAsync();

        var sessions = await _db.ExamSessions
            .Where(s => s.UserId == studentId && s.Exam.IsMock)
            .ToListAsync();

        var practice = exams
            .Where(e => e.StartTime <= now && !SubmissionService.IsClosed(e, now))
            .OrderBy(e => e.Title)
            .Select(e => new PracticeExamSummary(
                e.ExamId,
                e.Title,
                e.DurationMinutes,
                e.Questions.Count,
                sessions.Count(s => s.ExamId == e.ExamId && s.Status != SessionStatus.InProgress),
                sessions.Where(s => s.ExamId == e.ExamId && s.Status == SessionStatus.InProgress)
                        .Select(s => (Guid?)s.SessionId).FirstOrDefault()))
            .ToList();

        return Ok(new ApiEnvelope<List<PracticeExamSummary>>(true, "SUCCESS", "Success", practice));
    }
}

// ── DTOs ──────────────────────────────────────────────────────
public class CreateExamDto
{
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public Guid SectionId { get; set; }
    public DateTime StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public string AllowedApps { get; set; } = "[]";
    public bool AiEvaluationEnabled { get; set; } = true;
    public int PlagiarismThreshold { get; set; } = 70;
    public LockdownMode LockdownMode { get; set; } = LockdownMode.Report;
    public bool BlockMultiMonitor { get; set; } = true;
    // Opt-in over the API (keeps older clients/tests working); the web wizard ticks it by default.
    public bool RequireReadiness { get; set; }
    public bool RequireWebcam { get; set; }
    public bool RequireMic { get; set; }
    public bool IsMock { get; set; }
    // Module 3: minutes before StartTime that start-session already accepts (0–60).
    public int EarlyJoinMinutes { get; set; }
    // Module 9: attendance threshold for "Recalculate from attendance", and seat enforcement.
    public double MinAttendancePercent { get; set; } = 75;
    public bool EnforceSeat { get; set; } = true;
    // Module 8: mean capture intervals (10–600 s) read by the desktop client from /student/current.
    public int ScreenshotIntervalSeconds { get; set; } = Exam.DefaultScreenshotIntervalSeconds;
    public int WebcamIntervalSeconds { get; set; } = Exam.DefaultWebcamIntervalSeconds;
    public List<CreateQuestionDto> Questions { get; set; } = new();
    public List<Guid> StudentIds { get; set; } = new();
}

public class CreateQuestionDto
{
    public QuestionType Type { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public int Marks { get; set; }
    public List<CreateTestCaseDto> TestCases { get; set; } = new();
    public List<string>? Options { get; set; }          // MCQ only
    public int? CorrectOptionIndex { get; set; }        // MCQ only
    public string? ReferenceAnswer { get; set; }        // Coding/Theory reference key / sample solution
    public string? Language { get; set; }               // Coding: "cpp" | "c" (null = detect from the answer)
    public string? RubricKeywords { get; set; }         // Theory: comma-separated key terms for the offline grader
}

public class CreateTestCaseDto
{
    public string Input { get; set; } = string.Empty;
    public string ExpectedOutput { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    // Module 11 scoring / comparison settings (all optional).
    public double Weight { get; set; } = 1;
    public int? TimeLimitMs { get; set; }
    public bool IsEdgeCase { get; set; }
    public string? Category { get; set; }
    public bool IgnoreCase { get; set; }
    public double? FloatTolerance { get; set; }
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
    // Hand the decision back to "Recalculate from attendance" (drops the Manual override).
    public bool ClearOverride { get; set; }
}

public class OverrideGradeRequest
{
    public double FinalMarks { get; set; }
    public string? Note { get; set; }
}

public record SaveAnswerRequest(Guid QuestionId, string? AnswerText, int? Revision = null, DateTime? ClientSavedAt = null);
public record MonitoringEventRequest(MonitoringEventType EventType, string Payload);
public record AddAssignmentRequest(Guid UserId);
public record AssignWorkstationRequest(Guid? WorkstationId);
public record AssignProctorRequest(Guid UserId);
public record ExtendTimeRequest(int AdditionalMinutes);

public class ReadinessSubmitRequest
{
    public bool PassedAll { get; set; }
    public string? MachineName { get; set; }
    public List<ReadinessItemRequest>? Results { get; set; }
}

public class ReadinessItemRequest
{
    public string? Name { get; set; }
    public bool Passed { get; set; }
    public string? Detail { get; set; }
    public int DurationMs { get; set; }
}

public record ReadinessItem(string Name, bool Passed, string? Detail, int DurationMs);

