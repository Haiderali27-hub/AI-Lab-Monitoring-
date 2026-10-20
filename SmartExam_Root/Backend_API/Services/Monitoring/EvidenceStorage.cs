using System.Security.Cryptography;

namespace Backend_API.Services.Monitoring;

/// <summary>Where an uploaded evidence file ended up (path relative to the evidence root).</summary>
public sealed record StoredEvidence(string RelativePath, int SizeBytes, string Sha256);

/// <summary>
/// Module 5 — the server's evidence folder for screen and webcam captures (<c>Storage:EvidencePath</c>, default
/// <c>Evidence</c> under the content root). Files are laid out as <c>{examId}/{sessionId}/{capturedAt}_{id}.jpg</c>;
/// every name is generated here, never taken from the client, and every path read or deleted is checked to stay
/// inside the root, so a stored row can never point the server at another file. Writes go to a temporary name
/// first and are then moved into place, so a half-written upload is never served.
/// </summary>
public sealed class EvidenceStorage
{
    public const string PathKey = "Storage:EvidencePath";
    public const string DefaultFolder = "Evidence";

    /// <summary>Largest accepted capture. The desktop client sends JPEG q60 at ≤ 1280 px (≈ 50–200 KB).</summary>
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    private readonly ILogger<EvidenceStorage> _logger;

    public EvidenceStorage(IConfiguration configuration, IWebHostEnvironment environment, ILogger<EvidenceStorage> logger)
    {
        _logger = logger;
        var configured = configuration[PathKey];
        var path = string.IsNullOrWhiteSpace(configured) ? DefaultFolder : configured;
        Root = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
    }

    /// <summary>Absolute evidence root.</summary>
    public string Root { get; }

    /// <summary>
    /// Content type decided by the file's first bytes (JPEG <c>FF D8 FF</c>, PNG <c>89 50 4E 47 0D 0A 1A 0A</c>);
    /// null for anything else. The client's declared content type and file name are ignored.
    /// </summary>
    public static string? DetectImageType(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        return null;
    }

    public static string ExtensionFor(string contentType) => contentType == "image/png" ? ".png" : ".jpg";

    /// <summary>Stores <paramref name="content"/> for a session and returns where it went, its size and SHA-256.</summary>
    public async Task<StoredEvidence> SaveAsync(Guid examId, Guid sessionId, Guid evidenceId, DateTime capturedAtUtc,
        string contentType, Stream content, CancellationToken cancellationToken = default)
    {
        var relative = Path.Combine(examId.ToString("D"), sessionId.ToString("D"),
            $"{capturedAtUtc:yyyyMMddHHmmssfff}_{evidenceId:N}{ExtensionFor(contentType)}");
        var full = Resolve(relative) ?? throw new InvalidOperationException("Evidence path escaped the evidence root.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var tmp = full + ".part";
        int size;
        string sha;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxUploadBytes) throw new InvalidDataException("Evidence file is larger than the upload limit.");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                await file.FlushAsync(cancellationToken);
                size = (int)total;
            }
            sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            File.Move(tmp, full);
        }
        catch
        {
            TryDeleteFile(tmp);
            throw;
        }

        return new StoredEvidence(relative.Replace('\\', '/'), size, sha);
    }

    /// <summary>Opens a stored file for reading; null when it is missing (expired) or the path is not inside the root.</summary>
    public Stream? OpenRead(string relativePath)
    {
        var full = Resolve(relativePath);
        if (full is null || !File.Exists(full)) return null;
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
    }

    /// <summary>Deletes a stored file. True when it is gone afterwards (a file that was already missing counts).</summary>
    public bool Delete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (full is null) return false;
        if (!TryDeleteFile(full)) return false;
        TryRemoveEmptyParents(Path.GetDirectoryName(full));
        return true;
    }

    /// <summary>
    /// Removes files older than <paramref name="cutoffUtc"/> that no database row refers to any more (a session
    /// deleted with its rows, an upload whose row was never committed). Returns how many were deleted.
    /// </summary>
    public int DeleteOrphansOlderThan(DateTime cutoffUtc, ISet<string> referencedRelativePaths)
    {
        if (!Directory.Exists(Root)) return 0;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoffUtc) continue;
                var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (referencedRelativePaths.Contains(relative)) continue;
                if (TryDeleteFile(file))
                {
                    deleted++;
                    TryRemoveEmptyParents(Path.GetDirectoryName(file));
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not inspect evidence file {File}.", file);
            }
        }
        return deleted;
    }

    /// <summary>Absolute path for a stored relative path, or null when it would leave the evidence root.</summary>
    public string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        var rootWithSeparator = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private bool TryDeleteFile(string full)
    {
        try
        {
            if (File.Exists(full)) File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete evidence file {File}.", full);
            return false;
        }
    }

    // Keeps the tree tidy: {exam}/{session} folders disappear with their last file (the root itself stays).
    private void TryRemoveEmptyParents(string? directory)
    {
        try
        {
            while (directory is not null &&
                   !string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                   directory.StartsWith(Root, StringComparison.OrdinalIgnoreCase) &&
                   Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another upload raced into the folder: leave it.
        }
    }
}
