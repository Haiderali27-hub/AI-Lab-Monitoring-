using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace Student_Desktop_App.Core;

public static class DeviceFingerprint
{
    public static string CreateRawFingerprint()
    {
        var cpuId = GetWmiValue("Win32_Processor", "ProcessorId");
        var boardSerial = GetWmiValue("Win32_BaseBoard", "SerialNumber");
        var macAddress = GetPrimaryMacAddress();
        return $"CPU:{cpuId}|BOARD:{boardSerial}|MAC:{macAddress}";
    }

    public static string CreateHashedFingerprint()
    {
        var raw = CreateRawFingerprint();
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static string GetWmiValue(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
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
            // Intentionally fall through to deterministic fallback.
        }

        return "UNKNOWN";
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