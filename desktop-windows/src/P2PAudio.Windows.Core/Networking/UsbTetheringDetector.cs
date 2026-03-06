using System.Net.NetworkInformation;
using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.Core.Networking;

public static class UsbTetheringDetector
{
    public static NetworkPathType ClassifyPrimaryPath()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .ToArray();

        if (interfaces.Any(IsUsbLike))
        {
            return NetworkPathType.UsbTether;
        }
        if (interfaces.Any(IsWifiLike))
        {
            return NetworkPathType.WifiLan;
        }
        return NetworkPathType.Unknown;
    }

    public static NetworkPathType ClassifyFromCandidateAddress(string address)
    {
        var match = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .FirstOrDefault(nic => nic.GetIPProperties().UnicastAddresses.Any(x => x.Address.ToString() == address));
        if (match is null)
        {
            return ClassifyPrimaryPath();
        }
        if (IsUsbLike(match))
        {
            return NetworkPathType.UsbTether;
        }
        if (IsWifiLike(match))
        {
            return NetworkPathType.WifiLan;
        }
        return NetworkPathType.Unknown;
    }

    public static string? ExtractCandidateAddress(string candidateSdp)
    {
        var parts = candidateSdp.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
        {
            return null;
        }
        return parts[4];
    }

    private static bool IsUsbLike(NetworkInterface nic)
    {
        var name = $"{nic.Name} {nic.Description}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        return name.Contains("usb")
            || name.Contains("rndis")
            || name.Contains("ethernet gadget")
            || name.Contains("remote ndis")
            || (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet && name.Contains("apple mobile device"));
    }

    private static bool IsWifiLike(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            return true;
        }
        var name = $"{nic.Name} {nic.Description}".ToLowerInvariant();
        return name.Contains("wi-fi") || name.Contains("wifi") || name.Contains("wlan");
    }
}
