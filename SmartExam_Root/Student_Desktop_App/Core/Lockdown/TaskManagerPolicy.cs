using System.IO;
using System.Text.Json;

namespace Student_Desktop_App.Core.Lockdown;

/// <summary>
/// Module 4: Task Manager policy while the exam window is up. Ctrl+Shift+Esc is swallowed by the keyboard
/// hook, but Ctrl+Alt+Del → Task Manager cannot be intercepted, so the per-user policy value
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System\DisableTaskMgr = 1</c> is set for the
/// exam's duration (HKCU is writable by a standard user, no elevation needed) and always restored:
/// on exam exit, on a crash (unhandled exception / process exit), and — if the process died too hard
/// for any of those — at the next app start, from a restore marker written <b>before</b> the value is set.
/// </summary>
public static class TaskManagerPolicy
{
    public const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string RegistryValueName = "DisableTaskMgr";

    /// Task Manager is disabled whenever lockdown is active and the (default-on) config switch allows it.
    /// Enforce mode additionally kills a running taskmgr (LockdownPolicy's escape-tool list).
    public static bool ShouldDisable(LockdownMode mode, bool configEnabled) => mode != LockdownMode.Off && configEnabled;

    /// <summary>What restoring from <paramref name="marker"/> must do to the registry value.</summary>
    public static RestoreAction PlanRestore(TaskManagerRestoreMarker? marker) =>
        marker is null ? RestoreAction.Nothing
        : marker.HadValue ? RestoreAction.WritePrevious
        : RestoreAction.DeleteValue;

    /// <summary>
    /// Startup crash recovery: restore unless the marker belongs to another SmartExam instance that is
    /// still running an exam (a second launch must not unlock the first one's Task Manager policy).
    /// </summary>
    public static bool ShouldRecoverAtStartup(TaskManagerRestoreMarker? marker, int currentPid, Func<int, bool> isSmartExamProcessAlive) =>
        marker is not null && (marker.Pid == currentPid || !isSmartExamProcessAlive(marker.Pid));
}

public enum RestoreAction
{
    Nothing,
    WritePrevious,
    DeleteValue
}

public enum TaskManagerApplyResult
{
    Applied,
    /// A marker from an earlier, unrecovered run already exists: it keeps the true original value.
    AlreadyApplied,
    /// The restore marker could not be written, so the policy was deliberately not applied.
    MarkerWriteFailed,
    RegistryFailed
}

/// <summary>Written to disk before DisableTaskMgr is set, so the original state can always be restored.</summary>
public sealed record TaskManagerRestoreMarker(bool HadValue, int? PreviousValue, DateTime AppliedAtUtc, int Pid);

/// <summary>The DisableTaskMgr DWORD (abstracted so tests never touch the real registry).</summary>
public interface ITaskManagerPolicyStore
{
    int? Read();
    void Write(int value);
    void Delete();
}

/// <summary>Persistence for <see cref="TaskManagerRestoreMarker"/>.</summary>
public interface IRestoreMarkerStore
{
    TaskManagerRestoreMarker? Load();
    void Save(TaskManagerRestoreMarker marker);
    void Delete();
}

/// <summary>
/// Applies and restores the Task Manager policy through the two abstractions. All decisions live here,
/// so apply / restore / absent-value / crash-recovery are unit-tested with fakes.
/// </summary>
public sealed class TaskManagerGuard
{
    private readonly ITaskManagerPolicyStore _registry;
    private readonly IRestoreMarkerStore _markers;
    private readonly Func<DateTime> _utcNow;
    private readonly int _pid;
    private readonly object _sync = new();

    public TaskManagerGuard(ITaskManagerPolicyStore registry, IRestoreMarkerStore markers, int pid, Func<DateTime>? utcNow = null)
    {
        _registry = registry;
        _markers = markers;
        _pid = pid;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public TaskManagerApplyResult Apply()
    {
        lock (_sync)
        {
            TaskManagerRestoreMarker? existing;
            try { existing = _markers.Load(); }
            catch { existing = null; }

            if (existing is null)
            {
                int? previous;
                try { previous = _registry.Read(); }
                catch { return TaskManagerApplyResult.RegistryFailed; }

                // Marker first: if we crash after setting the value, the next start can still undo it.
                try { _markers.Save(new TaskManagerRestoreMarker(previous.HasValue, previous, _utcNow(), _pid)); }
                catch { return TaskManagerApplyResult.MarkerWriteFailed; }
            }

            try
            {
                _registry.Write(1);
            }
            catch
            {
                if (existing is null) TryDeleteMarker();
                return TaskManagerApplyResult.RegistryFailed;
            }

            return existing is null ? TaskManagerApplyResult.Applied : TaskManagerApplyResult.AlreadyApplied;
        }
    }

    /// <summary>Idempotent. Returns true when a marker was found and the original state restored.</summary>
    public bool Restore()
    {
        lock (_sync)
        {
            TaskManagerRestoreMarker? marker;
            try { marker = _markers.Load(); }
            catch { return false; }

            try
            {
                switch (TaskManagerPolicy.PlanRestore(marker))
                {
                    case RestoreAction.Nothing:
                        return false;
                    case RestoreAction.WritePrevious:
                        _registry.Write(marker!.PreviousValue ?? 0);
                        break;
                    case RestoreAction.DeleteValue:
                        _registry.Delete();
                        break;
                }
            }
            catch
            {
                // Keep the marker so the next start retries the restore.
                return false;
            }

            TryDeleteMarker();
            return true;
        }
    }

    /// <summary>Called once at app start: undoes a policy left behind by a crashed exam.</summary>
    public bool RecoverIfNeeded(Func<int, bool> isSmartExamProcessAlive)
    {
        TaskManagerRestoreMarker? marker;
        try { marker = _markers.Load(); }
        catch { return false; }

        return TaskManagerPolicy.ShouldRecoverAtStartup(marker, _pid, isSmartExamProcessAlive) && Restore();
    }

    private void TryDeleteMarker()
    {
        try { _markers.Delete(); }
        catch { /* a stale marker only causes a harmless re-restore next start */ }
    }
}

/// <summary>JSON restore marker in %LOCALAPPDATA%\SmartExam (per Windows user, like the HKCU value).</summary>
public sealed class FileRestoreMarkerStore : IRestoreMarkerStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartExam", "lockdown-restore.json");

    private readonly string _path;

    public FileRestoreMarkerStore(string? path = null) => _path = path ?? DefaultPath;

    public TaskManagerRestoreMarker? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<TaskManagerRestoreMarker>(File.ReadAllText(_path));
        }
        catch (JsonException)
        {
            // Corrupt marker (e.g. power loss mid-write): we cannot know the original value, so restore
            // to "no policy" — the Windows default — rather than leave Task Manager disabled forever.
            return new TaskManagerRestoreMarker(false, null, DateTime.MinValue, 0);
        }
    }

    public void Save(TaskManagerRestoreMarker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Write-then-rename so a crash never leaves a half-written marker behind.
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(marker));
        File.Move(temp, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
