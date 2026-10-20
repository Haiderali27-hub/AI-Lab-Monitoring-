using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Student_Desktop_App.Core.Monitoring;

/// <summary>What a capture is: Screen or Webcam; why it was taken: Interval, Violation or Manual.</summary>
public sealed record CaptureInfo(Guid CaptureId, string Kind, string Trigger, string? Reason, DateTime CapturedAtUtc, string ContentType = "image/jpeg");

/// <summary>
/// Module 5 — captures waiting for upload, kept on disk (encrypted, one file per capture) so a network outage does
/// not lose evidence: a capture is spooled first and deleted only once the server accepted (or refused it for
/// good). Bounded: beyond <see cref="MaxFiles"/> captures the oldest <em>interval</em> capture is dropped first —
/// violation captures are kept as long as possible. Each file is <c>length(int32 LE) | sealed(info JSON) | sealed(image)</c>
/// with <see cref="RecordCipher"/>, so nobody can swap an image on disk before it is uploaded. Thread-safe.
/// </summary>
public sealed class CaptureSpool
{
    public const string Extension = ".cap";
    public const int DefaultMaxFiles = 40;
    private const int MaxInfoBytes = 4096;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _directory;
    private readonly RecordCipher _cipher;
    private readonly object _gate = new();

    public CaptureSpool(string directory, RecordCipher cipher, int maxFiles = DefaultMaxFiles)
    {
        _directory = directory;
        _cipher = cipher;
        MaxFiles = Math.Max(1, maxFiles);
        Directory.CreateDirectory(directory);
    }

    public int MaxFiles { get; }

    /// <summary>Captures dropped because the spool was full (reported to the server as a monitoring gap).</summary>
    public int Dropped { get; private set; }

    public int Count
    {
        get { lock (_gate) return Files().Count(); }
    }

    /// <summary>Stores a capture durably. When full, drops the oldest interval capture (or, failing that, the oldest one).</summary>
    public void Add(CaptureInfo info, byte[] image)
    {
        lock (_gate)
        {
            var sealedInfo = _cipher.Seal(JsonSerializer.SerializeToUtf8Bytes(info, Json));
            var sealedImage = _cipher.Seal(image);
            var file = new byte[4 + sealedInfo.Length + sealedImage.Length];
            BinaryPrimitives.WriteInt32LittleEndian(file, sealedInfo.Length);
            sealedInfo.CopyTo(file, 4);
            sealedImage.CopyTo(file, 4 + sealedInfo.Length);

            // Name sorts by capture time, so Pending() is oldest first without opening every file.
            var name = $"{info.CapturedAtUtc:yyyyMMddHHmmssfff}_{info.Trigger}_{info.CaptureId:N}{Extension}";
            var path = Path.Combine(_directory, name);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(file);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);

            var all = Files().ToList();
            while (all.Count > MaxFiles)
            {
                var victim = all.FirstOrDefault(f => Path.GetFileName(f).Contains("_Interval_", StringComparison.Ordinal)) ?? all[0];
                File.Delete(victim);
                all.Remove(victim);
                Dropped++;
            }
        }
    }

    /// <summary>Pending capture files, oldest first.</summary>
    public IReadOnlyList<string> Pending()
    {
        lock (_gate) return Files().ToList();
    }

    /// <summary>Opens a spooled capture; null (and the file is removed) when it is not authentic or unreadable.</summary>
    public (CaptureInfo Info, byte[] Image)? Read(string path)
    {
        lock (_gate)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var infoLength = BinaryPrimitives.ReadInt32LittleEndian(bytes);
                if (infoLength <= 0 || infoLength > MaxInfoBytes || 4 + infoLength > bytes.Length) throw new CryptographicException("Bad capture header.");
                var info = JsonSerializer.Deserialize<CaptureInfo>(_cipher.Open(bytes.AsSpan(4, infoLength)), Json)
                           ?? throw new CryptographicException("Empty capture info.");
                var image = _cipher.Open(bytes.AsSpan(4 + infoLength));
                return (info, image);
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or ArgumentOutOfRangeException)
            {
                TryDelete(path);
                return null;
            }
        }
    }

    /// <summary>The server accepted the capture (or refused it for good): forget it.</summary>
    public void Remove(string path)
    {
        lock (_gate) TryDelete(path);
    }

    private IEnumerable<string> Files() =>
        Directory.Exists(_directory)
            ? Directory.EnumerateFiles(_directory, "*" + Extension).OrderBy(Path.GetFileName, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // still open by a reader; the next pass deletes it
        }
    }
}
