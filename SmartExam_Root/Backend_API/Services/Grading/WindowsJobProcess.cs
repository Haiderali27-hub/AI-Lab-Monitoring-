using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Backend_API.Services.Grading;

/// <summary>
/// Runs one student program on Windows inside a Job Object (the local runner's only containment):
/// the process is created suspended, assigned to the job, then resumed, so it never runs a single instruction outside it.
/// The job allows exactly one process (no child processes, no fork bombs), caps memory and CPU time, blocks UI access
/// (desktop, clipboard, global atoms), turns an unhandled crash into an exit code instead of an error dialog, and kills
/// everything when its handle closes. Memory-limit and CPU-time breaches are read from the job's completion port.
/// There is no network or file-system isolation — the local runner is labelled "not isolated" for that reason.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsJobProcess
{
    public sealed record Result(int? ExitCode, string Stdout, bool TimedOut, bool MemoryExceeded, int DurationMs, bool OutputTruncated);

    public static async Task<Result> RunAsync(string exePath, string workDir, string stdin, int timeLimitMs, int memoryLimitMb,
        int maxOutputBytes, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        using var job = CreateJob(timeLimitMs, memoryLimitMb, out var port);
        using var portHandle = port;

        SafeFileHandle stdinRead, stdinWrite, stdoutRead, stdoutWrite;
        var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = true };
        if (!CreatePipe(out stdinRead, out stdinWrite, ref sa, 0)) throw Error("CreatePipe(stdin)");
        if (!CreatePipe(out stdoutRead, out stdoutWrite, ref sa, 64 * 1024)) throw Error("CreatePipe(stdout)");
        using var nul = File.OpenHandle("NUL", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        PROCESS_INFORMATION pi;
        var envBlock = IntPtr.Zero;
        try
        {
            // Only the child's ends may be inherited.
            SetHandleInformation(stdinWrite, HANDLE_FLAG_INHERIT, 0);
            SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0);
            SetHandleInformation(nul, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT);

            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESTDHANDLES,
                hStdInput = stdinRead.DangerousGetHandle(),
                hStdOutput = stdoutWrite.DangerousGetHandle(),
                hStdError = nul.DangerousGetHandle()
            };
            var block = new StringBuilder();
            foreach (var (k, v) in environment.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) block.Append(k).Append('=').Append(v).Append('\0');
            block.Append('\0');
            envBlock = Marshal.StringToHGlobalUni(block.ToString());

            var commandLine = new StringBuilder($"\"{exePath}\"");
            lock (GradingProcess.LaunchLock)
            {
                if (!CreateProcessW(exePath, commandLine, IntPtr.Zero, IntPtr.Zero, true,
                        CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, envBlock, workDir, ref si, out pi))
                    throw Error("CreateProcess");
                // Close the child's ends in this process right away, still under the lock.
                stdinRead.Dispose();
                stdoutWrite.Dispose();
            }
        }
        catch
        {
            stdinRead.Dispose(); stdinWrite.Dispose(); stdoutRead.Dispose(); stdoutWrite.Dispose();
            throw;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) Marshal.FreeHGlobal(envBlock);
        }

        using var process = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
        using var thread = new SafeProcessHandle(pi.hThread, ownsHandle: true);
        if (!AssignProcessToJobObject(job, process))
        {
            TerminateProcess(process, 1);
            stdinWrite.Dispose(); stdoutRead.Dispose();
            throw Error("AssignProcessToJobObject");
        }

        var watch = Stopwatch.StartNew();
        ResumeThread(thread);

        var writer = WriteStdinAsync(stdinWrite, stdin);
        var reader = ReadStdoutAsync(stdoutRead, maxOutputBytes);

        var exited = await WaitAsync(process, timeLimitMs, cancellationToken);
        var timedOut = false;
        if (!exited)
        {
            TerminateJobObject(job, 1);
            await WaitAsync(process, 5000, CancellationToken.None);
            timedOut = !cancellationToken.IsCancellationRequested;
        }
        watch.Stop();
        cancellationToken.ThrowIfCancellationRequested();

        await writer;
        var (output, truncated) = await reader;
        GetExitCodeProcess(process, out var exitCode);

        // Job notifications: CPU-time and memory-limit breaches (the program may have just crashed on a failed malloc).
        var memory = false;
        while (GetQueuedCompletionStatus(port, out var message, out _, out _, 0))
        {
            if (message is JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT or JOB_OBJECT_MSG_JOB_MEMORY_LIMIT) memory = true;
            if (message is JOB_OBJECT_MSG_END_OF_PROCESS_TIME or JOB_OBJECT_MSG_END_OF_JOB_TIME) timedOut = true;
        }
        if (QueryPeakMemory(job) >= (ulong)memoryLimitMb * 1024 * 1024 * 95 / 100) memory = true;

        return new Result(timedOut ? null : unchecked((int)exitCode), output, timedOut, memory && !timedOut,
            (int)watch.ElapsedMilliseconds, truncated);
    }

    private static SafeJobHandle CreateJob(int timeLimitMs, int memoryLimitMb, out SafeJobHandle port)
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw Error("CreateJobObject");

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_ACTIVE_PROCESS | JOB_OBJECT_LIMIT_PROCESS_MEMORY |
                             JOB_OBJECT_LIMIT_JOB_MEMORY | JOB_OBJECT_LIMIT_PROCESS_TIME | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION,
                ActiveProcessLimit = 1,
                PerProcessUserTimeLimit = (long)timeLimitMs * 10_000   // 100 ns units
            },
            ProcessMemoryLimit = (UIntPtr)((ulong)memoryLimitMb * 1024 * 1024),
            JobMemoryLimit = (UIntPtr)((ulong)memoryLimitMb * 1024 * 1024)
        };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            throw Error("SetInformationJobObject(limits)");

        var ui = new JOBOBJECT_BASIC_UI_RESTRICTIONS { UIRestrictionsClass = JOB_OBJECT_UILIMIT_ALL };
        if (!SetInformationJobObject(job, JobObjectBasicUIRestrictions, ref ui, Marshal.SizeOf<JOBOBJECT_BASIC_UI_RESTRICTIONS>()))
            throw Error("SetInformationJobObject(ui)");

        port = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
        if (port.IsInvalid) throw Error("CreateIoCompletionPort");
        var assoc = new JOBOBJECT_ASSOCIATE_COMPLETION_PORT { CompletionKey = IntPtr.Zero, CompletionPort = port.DangerousGetHandle() };
        if (!SetInformationJobObject(job, JobObjectAssociateCompletionPortInformation, ref assoc, Marshal.SizeOf<JOBOBJECT_ASSOCIATE_COMPLETION_PORT>()))
            throw Error("SetInformationJobObject(port)");
        return job;
    }

    private static ulong QueryPeakMemory(SafeJobHandle job)
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        return QueryInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), IntPtr.Zero)
            ? (ulong)info.PeakProcessMemoryUsed
            : 0;
    }

    private static Task WriteStdinAsync(SafeFileHandle handle, string stdin) => Task.Run(() =>
    {
        using var stream = new FileStream(handle, FileAccess.Write, 1, false);
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(stdin);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (IOException)
        {
            // The program exited (or was killed) without reading all of its input.
        }
    });

    private static Task<(string Output, bool Truncated)> ReadStdoutAsync(SafeFileHandle handle, int maxBytes) => Task.Run(() =>
    {
        using var stream = new FileStream(handle, FileAccess.Read, 1, false);
        using var kept = new MemoryStream();
        var buffer = new byte[8192];
        var truncated = false;
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                var room = maxBytes - (int)kept.Length;
                if (room > 0) kept.Write(buffer, 0, Math.Min(room, read));
                if (read > room) truncated = true;
            }
        }
        catch (IOException)
        {
            // Broken pipe: the process is gone.
        }
        return (Encoding.UTF8.GetString(kept.ToArray()), truncated);
    });

    private static Task<bool> WaitAsync(SafeProcessHandle process, int timeoutMs, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = new ProcessWaitHandle(process);
        var registration = ThreadPool.RegisterWaitForSingleObject(wait, (_, timedOut) => tcs.TrySetResult(!timedOut), null, timeoutMs, executeOnlyOnce: true);
        var cancel = cancellationToken.Register(() => tcs.TrySetResult(false));
        return tcs.Task.ContinueWith(t =>
        {
            registration.Unregister(null);
            cancel.Dispose();
            wait.Dispose();
            return t.Result;
        }, TaskScheduler.Default);
    }

    private static Win32Exception Error(string what) => new(Marshal.GetLastWin32Error(), $"{what} failed");

    // A wait handle over the process handle (signalled when the process exits); does not own the handle.
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process) =>
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }

    // ── Win32 ────────────────────────────────────────────────────────────────

    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    private const uint JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002;
    private const uint JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const uint JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
    private const uint JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;
    private const uint JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint JOB_OBJECT_UILIMIT_ALL = 0x000000FF;

    private const uint JOB_OBJECT_MSG_END_OF_JOB_TIME = 1;
    private const uint JOB_OBJECT_MSG_END_OF_PROCESS_TIME = 2;
    private const uint JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT = 9;
    private const uint JOB_OBJECT_MSG_JOB_MEMORY_LIMIT = 10;

    private const int JobObjectBasicUIRestrictions = 4;
    private const int JobObjectAssociateCompletionPortInformation = 7;
    private const int JobObjectExtendedLimitInformation = 9;

    internal sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_UI_RESTRICTIONS
    {
        public uint UIRestrictionsClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
    {
        public IntPtr CompletionKey;
        public IntPtr CompletionPort;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass, ref JOBOBJECT_BASIC_UI_RESTRICTIONS info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeJobHandle hJob, int infoClass, ref JOBOBJECT_ASSOCIATE_COMPLETION_PORT info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(SafeJobHandle hJob, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length, IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle hJob, SafeProcessHandle hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeJobHandle hJob, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeJobHandle CreateIoCompletionPort(IntPtr fileHandle, IntPtr existingPort, UIntPtr completionKey, uint threads);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetQueuedCompletionStatus(SafeJobHandle port, out uint bytes, out UIntPtr key, out IntPtr overlapped, uint timeoutMs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, ref SECURITY_ATTRIBUTES sa, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeProcessHandle thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
