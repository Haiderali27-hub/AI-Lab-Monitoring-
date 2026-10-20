using System.Security.Cryptography;
using Student_Desktop_App.Core.Backup;

namespace Student_Desktop_App.Services.Backup;

/// <summary>
/// Wraps a backup data key with Windows DPAPI for the current user (<see cref="DataProtectionScope.CurrentUser"/>):
/// only the same Windows account on the same PC can unwrap it, and a modified blob fails to unprotect.
/// The session id is passed as entropy, so a key cannot be lifted from one session's backup into another's.
/// </summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] key, byte[] entropy) =>
        ProtectedData.Protect(key, entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] wrappedKey, byte[] entropy) =>
        ProtectedData.Unprotect(wrappedKey, entropy, DataProtectionScope.CurrentUser);

    /// <summary>The store the exam window and dashboard use: DPAPI keys under %LOCALAPPDATA%\SmartExam\backup.</summary>
    public static AnswerBackupStore CreateDefaultStore() =>
        new(AnswerBackupStore.DefaultDirectory, new DpapiKeyProtector());
}
