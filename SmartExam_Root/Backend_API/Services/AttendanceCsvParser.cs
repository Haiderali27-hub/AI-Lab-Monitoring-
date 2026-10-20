using System.Globalization;
using System.Text;

namespace Backend_API.Services;

/// <summary>One parsed data row. <c>Error</c> set = the row is invalid and must not be imported.</summary>
public sealed record AttendanceCsvRow(int Line, string Email, int Total, int Attended, string? Error);

public sealed record AttendanceCsvResult(IReadOnlyList<AttendanceCsvRow> Rows, string? HeaderError);

/// <summary>
/// Module 9 — parses the teacher's attendance CSV. Columns are found BY HEADER NAME
/// (case-insensitive, any order, extra columns ignored), so both "email,total,attended" and
/// "email,attended,total" exports work. Handles a UTF-8 BOM, CRLF, blank lines, quoted fields and
/// ';' as an alternative delimiter (Excel in some locales). Pure — no DB access.
/// </summary>
public static class AttendanceCsvParser
{
    public const int MaxRows = 5000;
    public const int MaxClasses = 10_000;

    private static readonly string[] EmailHeaders = { "email", "studentemail", "student_email", "e-mail" };
    private static readonly string[] TotalHeaders = { "total", "totalclasses", "total_classes", "classes", "held" };
    private static readonly string[] AttendedHeaders = { "attended", "attendedclasses", "attended_classes", "present" };

    public static AttendanceCsvResult Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0) return new AttendanceCsvResult(Array.Empty<AttendanceCsvRow>(), "The file is empty.");

        var delimiter = lines[headerIndex].Count(c => c == ';') > lines[headerIndex].Count(c => c == ',') ? ';' : ',';
        var header = SplitLine(lines[headerIndex], delimiter)
            .Select(h => h.Trim().Replace(" ", "").ToLowerInvariant()).ToList();

        int Find(string[] names) => header.FindIndex(names.Contains);
        int emailCol = Find(EmailHeaders), totalCol = Find(TotalHeaders), attendedCol = Find(AttendedHeaders);
        if (emailCol < 0 || totalCol < 0 || attendedCol < 0)
            return new AttendanceCsvResult(Array.Empty<AttendanceCsvRow>(),
                "The header row must contain the columns: email, total, attended (in any order).");

        var rows = new List<AttendanceCsvRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var lineNo = i + 1;
            if (rows.Count >= MaxRows)
            {
                rows.Add(new AttendanceCsvRow(lineNo, "", 0, 0, $"Too many rows — at most {MaxRows} per file."));
                break;
            }

            var cells = SplitLine(lines[i], delimiter);
            string Cell(int col) => col < cells.Count ? cells[col].Trim() : "";

            var email = Cell(emailCol);
            string? error = null;
            if (email.Length == 0) error = "missing email";
            else if (!int.TryParse(Cell(totalCol), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ||
                     !int.TryParse(Cell(attendedCol), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                error = "total and attended must be whole numbers";
            else if (t < 0 || a < 0) error = "counts cannot be negative";
            else if (a > t) error = "attended is greater than total";
            else if (t > MaxClasses) error = $"total is larger than {MaxClasses}";
            else if (!seen.Add(email)) error = "duplicate row for this email";

            int.TryParse(Cell(totalCol), NumberStyles.Integer, CultureInfo.InvariantCulture, out var total);
            int.TryParse(Cell(attendedCol), NumberStyles.Integer, CultureInfo.InvariantCulture, out var attended);
            rows.Add(new AttendanceCsvRow(lineNo, email, total, attended, error));
        }

        return new AttendanceCsvResult(rows, null);
    }

    // RFC 4180-style split: quotes group a field, "" inside quotes is a literal quote.
    private static List<string> SplitLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
