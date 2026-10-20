using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace Student_Desktop_App.Core;

/// <summary>
/// Hardware fingerprint used for device binding (Module 1). The server only ever stores a hash of it.
///  • v2 (current, per the thesis): "v2|BIOS serial|system-disk serial|baseboard serial". These survive
///    switching between Wi-Fi and Ethernet, unlike the MAC address in v1.
///  • v1 (legacy): "CPU:…|BOARD:…|MAC:…". Still sent once as <c>legacyHardwareFingerprint</c> so an
///    existing v1 binding can be upgraded to v2 without an admin reset.
/// The WMI reads are thin wrappers; the composition rules (<see cref="ComposeV2"/>, <see cref="CleanSerial"/>)
/// are pure and unit-tested.
/// </summary>
public static class DeviceFingerprint
{
    public const string V2Prefix = "v2|";

    // Placeholder serials that many OEM boards ship with — identical across thousands of PCs.
    private static readonly HashSet<string> JunkSerials = new(StringComparer.OrdinalIgnoreCase)
    {
        "To be filled by O.E.M.", "To Be Filled By O.E.M.", "Default string", "Default", "None", "N/A", "NA",
        "Not Specified", "Not Applicable", "Not Available", "System Serial Number", "Base Board Serial Number",
        "Chassis Serial Number", "Serial Number", "SerialNumber", "OEM", "O.E.M.", "Unknown", "Invalid",
        "123456789", "1234567890", "0123456789", "Default_String"
    };

    // SMBIOS UUIDs that are known placeholders.
    private static readonly HashSet<string> JunkUuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "00000000-0000-0000-0000-000000000000",
        "03000200-0400-0500-0006-000700080009"
    };

    /// <summary>The current-format fingerprint sent as <c>hardwareFingerprint</c>.</summary>
    public static string CreateRawFingerprintV2()
    {
        var bios = GetWmiValue("Win32_BIOS", "SerialNumber");
        var disk = GetWmiValue("Win32_DiskDrive", "SerialNumber", "Index = 0");
        var board = GetWmiValue("Win32_BaseBoard", "SerialNumber");

        // Only read the fallbacks when they are needed.
        string? uuid = null, machineGuid = null;
        if (CountUsable(bios, disk, board) < 2)
        {
            uuid = GetWmiValue("Win32_ComputerSystemProduct", "UUID");
            machineGuid = GetMachineGuid();
        }

        return ComposeV2(bios, disk, board, uuid, machineGuid);
    }

    /// <summary>The legacy v1 fingerprint (CPU|BOARD|MAC), kept only for the one-time binding upgrade.</summary>
    public static string CreateRawFingerprint()
    {
        var cpuId = GetWmiValue("Win32_Processor", "ProcessorId") ?? "UNKNOWN";
        var boardSerial = GetWmiValue("Win32_BaseBoard", "SerialNumber") ?? "UNKNOWN";
        var macAddress = GetPrimaryMacAddress();
        return $"CPU:{cpuId}|BOARD:{boardSerial}|MAC:{macAddress}";
    }

    public static string CreateHashedFingerprint()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(CreateRawFingerprintV2()));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Builds "v2|bios|disk|board". When fewer than two of the three serials are usable (OEM junk or
    /// missing), a stable machine identifier is appended so two such PCs cannot collide: the SMBIOS UUID,
    /// or failing that the Windows MachineGuid.
    /// </summary>
    public static string ComposeV2(string? biosSerial, string? diskSerial, string? boardSerial, string? smbiosUuid = null, string? machineGuid = null)
    {
        var bios = CleanSerial(biosSerial);
        var disk = CleanSerial(diskSerial);
        var board = CleanSerial(boardSerial);
        var fingerprint = $"{V2Prefix}{bios}|{disk}|{board}";

        if (CountUsable(bios, disk, board) >= 2) return fingerprint;

        var uuid = CleanUuid(smbiosUuid);
        if (uuid.Length > 0) return $"{fingerprint}|uuid:{uuid}";
        var guid = CleanSerial(machineGuid);
        return guid.Length > 0 ? $"{fingerprint}|mguid:{guid}" : fingerprint;
    }

    /// <summary>Trimmed, upper-case serial; empty for missing or OEM placeholder values.</summary>
    public static string CleanSerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim().Trim('.').Trim();
        if (trimmed.Length == 0 || JunkSerials.Contains(trimmed) || JunkSerials.Contains(value.Trim())) return string.Empty;
        // All one repeated character ("0000000", "FFFFFFFF", "xxxxxxx") is a placeholder too.
        if (trimmed.Distinct().Count() == 1) return string.Empty;
        return trimmed.Replace("|", "/").ToUpperInvariant();
    }

    private static string CleanUuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || JunkUuids.Contains(value.Trim())) return string.Empty;
        return CleanSerial(value);
    }

    private static int CountUsable(params string?[] serials) => serials.Count(s => CleanSerial(s).Length > 0);

    private static string? GetWmiValue(string className, string propertyName, string? where = null)
    {
        try
        {
            var query = $"SELECT {propertyName} FROM {className}" + (where is null ? "" : $" WHERE {where}");
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var instance in searcher.Get())
            {
                var value = instance[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }
        catch
        {
            // WMI unavailable or access denied: treated as a missing component.
        }

        return null;
    }

    private static string? GetMachineGuid()
    {
        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    private static string GetPrimaryMacAddress()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(x => x.Speed)
                .FirstOrDefault();

            var address = nic?.GetPhysicalAddress().ToString();
            return string.IsNullOrWhiteSpace(address) ? "UNKNOWN" : address;
        }
        catch
        {
            return "UNKNOWN";
        }
    }
}
