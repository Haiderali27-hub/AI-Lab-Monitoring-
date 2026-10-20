using System.Security.Cryptography;
using System.Text;

namespace Backend_API.Services;

/// <summary>
/// Module 6 — the submission receipt hash. Computed by the server when a session is finalised and,
/// with the identical algorithm, by the desktop app over the answers it believes it sent
/// (<c>Student_Desktop_App/Core/Submission/ReceiptHasher.cs</c>), so a student can prove what was
/// submitted and a mismatch shows at once. Both test projects assert the same fixed vector (M6-U-05).
/// <code>
/// canonical = "SMARTEXAM-RECEIPT-v1\n" + sessionId("D") + "\n"
///           + for each answer ordered by questionId("D") (ordinal): questionId + U+001F + answerText + U+001E
/// sha256    = lowercase hex of SHA-256(UTF-8(canonical))
/// </code>
/// </summary>
public static class ReceiptHasher
{
    public const string Prefix = "SMARTEXAM-RECEIPT-v1\n";
    private const char UnitSeparator = '\u001F';
    private const char RecordSeparator = '\u001E';

    public static string Compute(Guid sessionId, IEnumerable<(Guid QuestionId, string? AnswerText)> answers)
    {
        var sb = new StringBuilder(Prefix).Append(sessionId.ToString("D")).Append('\n');
        foreach (var (questionId, text) in answers
                     .Select(a => (Id: a.QuestionId.ToString("D"), Text: a.AnswerText ?? string.Empty))
                     .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            sb.Append(questionId).Append(UnitSeparator).Append(text).Append(RecordSeparator);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
