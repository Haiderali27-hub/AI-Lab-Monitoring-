using System.Security.Cryptography;
using System.Text;

namespace Student_Desktop_App.Core.Submission;

/// <summary>
/// Module 6 — the submission receipt hash, byte-for-byte the algorithm of the server's
/// <c>Backend_API/Services/ReceiptHasher.cs</c>. The exam window hashes the answers it believes the
/// server holds and compares the result with the <c>sha256</c> the submit call returned: equal means
/// the server stored exactly what the student wrote. Both test projects assert the same fixed vectors (M6-U-05).
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

    /// <summary>True when the server's receipt equals the local hash (case-insensitive hex).</summary>
    public static bool Matches(string? localSha256, string? serverSha256) =>
        !string.IsNullOrWhiteSpace(localSha256) && !string.IsNullOrWhiteSpace(serverSha256) &&
        string.Equals(localSha256.Trim(), serverSha256.Trim(), StringComparison.OrdinalIgnoreCase);
}
