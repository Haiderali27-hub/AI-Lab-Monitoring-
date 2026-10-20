using System.Diagnostics;
using Microsoft.Win32;
using Student_Desktop_App.Core.Lockdown;

namespace Student_Desktop_App.Services.Lockdown;

/// <summary>
/// The real HKCU DisableTaskMgr value behind <see cref="ITaskManagerPolicyStore"/>. Thin by design: every
/// decision is in <see cref="TaskManagerGuard"/>. Never exercised by the unit tests.
/// </summary>
public sealed class HkcuTaskManagerPolicyStore : ITaskManagerPolicyStore
{
    public int? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TaskManagerPolicy.RegistryKeyPath, writable: false);
        return key?.GetValue(TaskManagerPolicy.RegistryValueName) is int value ? value : null;
    }

    public void Write(int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(TaskManagerPolicy.RegistryKeyPath, writable: true);
        key.SetValue(TaskManagerPolicy.RegistryValueName, value, RegistryValueKind.DWord);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.OpenSubKey(TaskManagerPolicy.RegistryKeyPath, writable: true);
        key?.DeleteValue(TaskManagerPolicy.RegistryValueName, throwOnMissingValue: false);
    }
}

/// <summary>Wires <see cref="TaskManagerGuard"/> to the real registry and the marker in %LOCALAPPDATA%.</summary>
public static class TaskManagerRegistryGuard
{
    public static TaskManagerGuard Create() =>
        new(new HkcuTaskManagerPolicyStore(), new FileRestoreMarkerStore(), Environment.ProcessId);

    /// <summary>App start: undo a DisableTaskMgr left behind by an exam that crashed or was killed.</summary>
    public static void RecoverAtStartup()
    {
        try
        {
            Create().RecoverIfNeeded(IsOtherSmartExamInstanceAlive);
        }
        catch
        {
            // Recovery must never prevent the client from starting.
        }
    }

    private static bool IsOtherSmartExamInstanceAlive(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return false;
        try
        {
            using var other = Process.GetProcessById(pid);
            using var self = Process.GetCurrentProcess();
            return !other.HasExited && string.Equals(other.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
    }
}
