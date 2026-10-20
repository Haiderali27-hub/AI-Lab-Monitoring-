using FluentAssertions;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Tests.Lockdown;

/// <summary>
/// M4-U-18: lockdown teardown is isolated per step. LockdownCoordinator.Stop() also runs from the crash and
/// process-exit handlers on non-UI threads, where the clipboard listener's HwndSource throws; that must not
/// skip restoring the Task Manager policy (verifier regression).
/// </summary>
public class LockdownTeardownTests
{
    [Fact]
    public void AllSteps_RunInOrder_WhenNoneFail()
    {
        var ran = new List<string>();

        var failed = LockdownTeardown.RunAll(
            ("a", () => ran.Add("a")),
            ("b", () => ran.Add("b")),
            ("c", () => ran.Add("c")));

        failed.Should().BeEmpty();
        ran.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void AThrowingStep_DoesNotStopLaterSteps_AndIsReported()
    {
        var ran = new List<string>();

        var failed = LockdownTeardown.RunAll(
            ("keyboard-hook", () => ran.Add("keyboard-hook")),
            ("clipboard", () => throw new InvalidOperationException("The calling thread cannot access this object")),
            ("process-guard", () => ran.Add("process-guard")),
            ("device-guard", () => throw new ObjectDisposedException("timer")),
            ("handlers", () => ran.Add("handlers")));

        ran.Should().Equal("keyboard-hook", "process-guard", "handlers");
        failed.Should().Equal("clipboard", "device-guard");
    }

    [Fact]
    public void TaskManagerPolicy_IsRestored_EvenWhenAUiBoundStepThrows()
    {
        // The real TaskManagerGuard against in-memory stores: applied, then torn down by a crash handler
        // whose clipboard step throws because it is not on the UI thread.
        var registry = new MemoryRegistry { Value = null };
        var markers = new MemoryMarkers();
        var guard = new TaskManagerGuard(registry, markers, pid: 42);
        guard.Apply().Should().Be(TaskManagerApplyResult.Applied);
        registry.Value.Should().Be(1);

        LockdownTeardown.RunAll(
            ("taskmgr-policy", () => guard.Restore()),
            ("clipboard", () => throw new InvalidOperationException("cross-thread")));

        registry.Value.Should().BeNull("the value was absent before the exam");
        markers.Marker.Should().BeNull();
    }

    private sealed class MemoryRegistry : ITaskManagerPolicyStore
    {
        public int? Value;
        public int? Read() => Value;
        public void Write(int value) => Value = value;
        public void Delete() => Value = null;
    }

    private sealed class MemoryMarkers : IRestoreMarkerStore
    {
        public TaskManagerRestoreMarker? Marker;
        public TaskManagerRestoreMarker? Load() => Marker;
        public void Save(TaskManagerRestoreMarker marker) => Marker = marker;
        public void Delete() => Marker = null;
    }
}
