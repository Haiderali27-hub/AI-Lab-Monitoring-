using System.Security.Cryptography;
using Backend_API.Services.Monitoring;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend_API.Tests.Tests.Monitoring;

/// <summary>Module 5 — evidence files on disk: content sniffing, server-side naming, path containment, cleanup.</summary>
public sealed class EvidenceStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smartexam-evidence-unit", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }

    private EvidenceStorage NewStorage(string? path = null) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [EvidenceStorage.PathKey] = path ?? _root }).Build(),
        new FakeEnvironment(), NullLogger<EvidenceStorage>.Instance);

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00 }, null)]             // a Windows executable renamed .jpg
    [InlineData(new byte[] { (byte)'<', (byte)'s', (byte)'v', (byte)'g' }, null)]  // SVG can carry script: never accepted
    [InlineData(new byte[] { 0xFF, 0xD8 }, null)]                                  // too short to be a JPEG
    public void DetectImageType_DecidesByMagicBytes(byte[] head, string? expected) =>
        EvidenceStorage.DetectImageType(head).Should().Be(expected);

    [Fact]
    public async Task Save_WritesUnderExamAndSession_WithServerName_AndHashesTheBytes()
    {
        var storage = NewStorage();
        var exam = Guid.NewGuid();
        var session = Guid.NewGuid();
        var id = Guid.NewGuid();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3, 0xFF, 0xD9 };

        var stored = await storage.SaveAsync(exam, session, id, new DateTime(2026, 10, 20, 9, 30, 0, DateTimeKind.Utc), "image/jpeg", new MemoryStream(bytes));

        stored.RelativePath.Should().Be($"{exam}/{session}/20261020093000000_{id:N}.jpg");
        stored.SizeBytes.Should().Be(bytes.Length);
        stored.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        await using (var read = storage.OpenRead(stored.RelativePath)!)
        {
            var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }
        Directory.EnumerateFiles(_root, "*.part", SearchOption.AllDirectories).Should().BeEmpty("the temporary file is moved into place");

        storage.Delete(stored.RelativePath).Should().BeTrue();
        storage.OpenRead(stored.RelativePath).Should().BeNull();
        Directory.Exists(Path.Combine(_root, exam.ToString())).Should().BeFalse("empty exam/session folders are removed");
        storage.Delete(stored.RelativePath).Should().BeTrue("a file that is already gone counts as deleted");
    }

    [Fact]
    public async Task Save_RefusesMoreThanTheUploadLimit_AndLeavesNothingBehind()
    {
        var storage = NewStorage();
        var tooBig = new MemoryStream(new byte[EvidenceStorage.MaxUploadBytes + 1]);
        var act = () => storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "image/jpeg", tooBig);
        await act.Should().ThrowAsync<InvalidDataException>();
        (Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories) : []).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("a/../../outside.jpg")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Resolve_NeverLeavesTheRoot(string relative)
    {
        var storage = NewStorage();
        storage.Resolve(relative).Should().BeNull();
        storage.OpenRead(relative).Should().BeNull();
        storage.Delete(relative).Should().BeFalse();
    }

    [Fact]
    public void RelativeConfiguredPath_IsUnderTheContentRoot()
    {
        var storage = NewStorage("Evidence");
        storage.Root.Should().Be(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Evidence")));
    }

    [Fact]
    public async Task DeleteOrphans_RemovesOnlyOldUnreferencedFiles()
    {
        var storage = NewStorage();
        var kept = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "image/jpeg", new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }));
        var orphan = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "image/jpeg", new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }));
        var young = await storage.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "image/jpeg", new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }));
        File.SetLastWriteTimeUtc(storage.Resolve(kept.RelativePath)!, DateTime.UtcNow.AddDays(-40));
        File.SetLastWriteTimeUtc(storage.Resolve(orphan.RelativePath)!, DateTime.UtcNow.AddDays(-40));

        var removed = storage.DeleteOrphansOlderThan(DateTime.UtcNow.AddDays(-30), new HashSet<string> { kept.RelativePath });

        removed.Should().Be(1);
        File.Exists(storage.Resolve(orphan.RelativePath)!).Should().BeFalse();
        File.Exists(storage.Resolve(kept.RelativePath)!).Should().BeTrue("a row still refers to it");
        File.Exists(storage.Resolve(young.RelativePath)!).Should().BeTrue("a recent file may belong to an upload still committing");
    }
}
