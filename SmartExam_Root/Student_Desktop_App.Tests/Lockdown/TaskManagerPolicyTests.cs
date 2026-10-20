using System.IO;
using FluentAssertions;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Tests.Lockdown;

/// <summary>
/// Module 4 Task Manager policy (unit half of M4-D-07): apply / restore / absent value / crash recovery,
/// all against fakes — the real registry is never touched.
/// </summary>
public class TaskManagerPolicyTests : IDisposable
{
    private sealed class FakeRegistry : ITaskManagerPolicyStore
    {
        public int? Value;
        public bool FailWrites;
        public List<string> Log { get; } = new();
        public int? Read() => Value;
        public void Write(int value)
        {
            if (FailWrites) throw new UnauthorizedAccessException("policy locked by GPO");
            Log.Add($"write:{value}");
            Value = value;
        }
        public void Delete()
        {
            Log.Add("delete");
            Value = null;
        }
    }

    private sealed class FakeMarkers : IRestoreMarkerStore
    {
        public TaskManagerRestoreMarker? Marker;
        public bool FailSaves;
        public TaskManagerRestoreMarker? Load() => Marker;
        public void Save(TaskManagerRestoreMarker marker)
        {
            if (FailSaves) throw new IOException("disk full");
            Marker = marker;
        }
        public void Delete() => Marker = null;
    }

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "smartexam-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static TaskManagerGuard Guard(FakeRegistry reg, FakeMarkers markers, int pid = 42) => new(reg, markers, pid, () => new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(LockdownMode.Enforce, true, true)]
    [InlineData(LockdownMode.Report, true, true)]
    [InlineData(LockdownMode.Off, true, false)]
    [InlineData(LockdownMode.Enforce, false, false)]
    public void ShouldDisable_FollowsModeAndConfig(LockdownMode mode, bool configEnabled, bool expected) =>
        TaskManagerPolicy.ShouldDisable(mode, configEnabled).Should().Be(expected);

    [Fact]
    public void Apply_WhenValueAbsent_WritesMarkerFirst_ThenDisables_AndRestoreDeletesTheValue()
    {
        var reg = new FakeRegistry();
        var markers = new FakeMarkers();
        var guard = Guard(reg, markers);

        guard.Apply().Should().Be(TaskManagerApplyResult.Applied);
        reg.Value.Should().Be(1);
        markers.Marker.Should().Be(new TaskManagerRestoreMarker(false, null, new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), 42));

        guard.Restore().Should().BeTrue();
        reg.Value.Should().BeNull("the value did not exist before the exam");
        reg.Log.Should().Equal("write:1", "delete");
        markers.Marker.Should().BeNull();
    }

    [Fact]
    public void Apply_WhenValueExisted_RestoresThePreviousValue()
    {
        var reg = new FakeRegistry { Value = 0 };
        var markers = new FakeMarkers();
        var guard = Guard(reg, markers);

        guard.Apply();
        reg.Value.Should().Be(1);
        guard.Restore().Should().BeTrue();
        reg.Value.Should().Be(0);
    }

    [Fact]
    public void Apply_WhenAdminPolicyAlreadyDisablesTaskManager_RestoreKeepsItDisabled()
    {
        var reg = new FakeRegistry { Value = 1 };
        var guard = Guard(reg, new FakeMarkers());
        guard.Apply();
        guard.Restore();
        reg.Value.Should().Be(1);
    }

    [Fact]
    public void Restore_IsIdempotent()
    {
        var reg = new FakeRegistry();
        var guard = Guard(reg, new FakeMarkers());
        guard.Apply();
        guard.Restore().Should().BeTrue();
        guard.Restore().Should().BeFalse();
        reg.Log.Should().Equal("write:1", "delete");
    }

    [Fact]
    public void Apply_IfMarkerCannotBeWritten_DoesNotTouchTheRegistry()
    {
        var reg = new FakeRegistry();
        var guard = Guard(reg, new FakeMarkers { FailSaves = true });
        guard.Apply().Should().Be(TaskManagerApplyResult.MarkerWriteFailed);
        reg.Log.Should().BeEmpty();
    }

    [Fact]
    public void Apply_IfRegistryWriteFails_RemovesTheMarker()
    {
        var markers = new FakeMarkers();
        var guard = Guard(new FakeRegistry { FailWrites = true }, markers);
        guard.Apply().Should().Be(TaskManagerApplyResult.RegistryFailed);
        markers.Marker.Should().BeNull();
    }

    [Fact]
    public void Apply_Twice_KeepsTheOriginalValueInTheMarker()
    {
        var reg = new FakeRegistry { Value = 0 };
        var markers = new FakeMarkers();
        var guard = Guard(reg, markers);
        guard.Apply().Should().Be(TaskManagerApplyResult.Applied);
        guard.Apply().Should().Be(TaskManagerApplyResult.AlreadyApplied);
        markers.Marker!.PreviousValue.Should().Be(0, "the second apply must not record the exam's own value 1");
        guard.Restore();
        reg.Value.Should().Be(0);
    }

    [Fact]
    public void FailedRestore_KeepsTheMarker_SoTheNextStartRetries()
    {
        var reg = new FakeRegistry { Value = 0 };
        var markers = new FakeMarkers();
        var guard = Guard(reg, markers);
        guard.Apply();
        reg.FailWrites = true;
        guard.Restore().Should().BeFalse();
        markers.Marker.Should().NotBeNull();
    }

    [Fact]
    public void M4_D_07_CrashRecovery_AtNextStart_RestoresWhenTheExamProcessIsGone()
    {
        // The exam process (pid 42) set the policy and was killed before it could restore it.
        var reg = new FakeRegistry();
        var markers = new FakeMarkers();
        Guard(reg, markers, pid: 42).Apply();

        // Next launch (pid 77): pid 42 no longer exists.
        var next = Guard(reg, markers, pid: 77);
        next.RecoverIfNeeded(pid => false).Should().BeTrue();
        reg.Value.Should().BeNull();
        markers.Marker.Should().BeNull();
    }

    [Fact]
    public void CrashRecovery_DoesNotUnlockAnotherRunningExamInstance()
    {
        var reg = new FakeRegistry();
        var markers = new FakeMarkers();
        Guard(reg, markers, pid: 42).Apply();

        Guard(reg, markers, pid: 77).RecoverIfNeeded(pid => pid == 42).Should().BeFalse();
        reg.Value.Should().Be(1);
    }

    [Fact]
    public void CrashRecovery_WithoutMarker_DoesNothing()
    {
        var reg = new FakeRegistry { Value = 0 };
        Guard(reg, new FakeMarkers()).RecoverIfNeeded(_ => false).Should().BeFalse();
        reg.Log.Should().BeEmpty();
    }

    [Fact]
    public void PlanRestore_MapsMarkerToAction()
    {
        TaskManagerPolicy.PlanRestore(null).Should().Be(RestoreAction.Nothing);
        TaskManagerPolicy.PlanRestore(new(true, 0, DateTime.UtcNow, 1)).Should().Be(RestoreAction.WritePrevious);
        TaskManagerPolicy.PlanRestore(new(false, null, DateTime.UtcNow, 1)).Should().Be(RestoreAction.DeleteValue);
    }

    // ── File marker store (temp directory, no registry) ──────────────────────

    [Fact]
    public void FileMarkerStore_RoundTrips_AndDeletes()
    {
        var store = new FileRestoreMarkerStore(Path.Combine(_tempDir, "sub", "lockdown-restore.json"));
        store.Load().Should().BeNull();

        var marker = new TaskManagerRestoreMarker(true, 0, new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), 1234);
        store.Save(marker);
        store.Load().Should().Be(marker);

        store.Delete();
        store.Load().Should().BeNull();
        store.Delete(); // idempotent
    }

    [Fact]
    public void FileMarkerStore_CorruptMarker_RestoresToWindowsDefault()
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, "lockdown-restore.json");
        File.WriteAllText(path, "{ not json");

        var marker = new FileRestoreMarkerStore(path).Load();
        TaskManagerPolicy.PlanRestore(marker).Should().Be(RestoreAction.DeleteValue);
    }

    [Fact]
    public void Guard_WithFileStore_SurvivesARestartBetweenApplyAndRecovery()
    {
        var path = Path.Combine(_tempDir, "lockdown-restore.json");
        var reg = new FakeRegistry { Value = 0 };
        new TaskManagerGuard(reg, new FileRestoreMarkerStore(path), 42).Apply();

        // "Process restarted": brand-new guard and store instances, same file.
        new TaskManagerGuard(reg, new FileRestoreMarkerStore(path), 77).RecoverIfNeeded(_ => false).Should().BeTrue();
        reg.Value.Should().Be(0);
        File.Exists(path).Should().BeFalse();
    }
}
