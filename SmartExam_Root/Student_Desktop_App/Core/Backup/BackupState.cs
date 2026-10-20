namespace Student_Desktop_App.Core.Backup;

/// <summary>
/// One answer as the exam window last held it. <see cref="Text"/> is the value sent to the server (the
/// option index for an MCQ). <see cref="Revision"/> counts local edits; <see cref="AckedRevision"/> is the
/// highest revision the server confirmed, so <c>Revision &gt; AckedRevision</c> means "not on the server yet".
/// </summary>
public sealed record BackupAnswer(Guid QuestionId, string Text, int Revision, int AckedRevision)
{
    public bool Unsent => Revision > AckedRevision;
}

/// <summary>The receipt shown to the student on submit, kept next to the answers it covers.</summary>
public sealed record BackupReceipt(DateTime SubmittedAtUtc, int AnswerCount, string ServerSha256, string LocalSha256, bool Verified);

/// <summary>
/// Module 6 — everything needed to rebuild an exam window after a crash: whose session it is, every
/// edited answer with its sync state, and (after submit) the receipt. Serialized to JSON, then sealed
/// with AES-256-GCM by <see cref="AnswerBackupCodec"/>.
/// </summary>
public sealed record BackupState(
    Guid SessionId,
    Guid ExamId,
    Guid UserId,
    DateTime SavedAtUtc,
    IReadOnlyList<BackupAnswer> Answers,
    bool Submitted = false,
    BackupReceipt? Receipt = null)
{
    public int UnsentCount => Answers.Count(a => a.Unsent);
}

/// <summary>An encrypted backup failed authentication (edited, truncated, swapped from another session, or
/// its key cannot be unwrapped on this Windows account). The backup must be ignored and the event reported.</summary>
public sealed class BackupTamperedException(string message, Exception? inner = null) : Exception(message, inner);
