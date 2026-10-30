using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanMonitor.Core.Network;

public class NetworkInfo
{
    public string InterfaceName { get; init; } = "";
    public IPAddress? LocalIp { get; init; }
    public IPAddress? Mask { get; init; }
    public IPAddress? Gateway { get; init; }
    public string Mac { get; init; } = "";
    public string Ssid { get; set; } = "";

    public string Cidr => LocalIp != null && Mask != null ? $"{LocalIp}/{PrefixLength(Mask)}" : "";

    public static int PrefixLength(IPAddress mask) =>
        mask.GetAddressBytes().Sum(b => System.Numerics.BitOperations.PopCount(b));

    /// <summary>Todas las IPs de host de la subred (excluye red y broadcast), limitado a maxHosts.</summary>
    public static List<IPAddress> HostsOf(IPAddress ip, IPAddress mask, int maxHosts = 1024)
    {
        var ipB = ip.GetAddressBytes(); var mB = mask.GetAddressBytes();
        uint ipU = ToUint(ipB), mU = ToUint(mB);
        uint net = ipU & mU, bcast = net | ~mU;
        var list = new List<IPAddress>();
        // Si la subred es demasiado grande, escanear solo una ventana centrada en nuestra IP.
        uint start = net + 1, end = bcast - 1;
        if (end >= start && end - start + 1 > (uint)maxHosts)
        {
            start = Math.Max(start, ipU - (uint)maxHosts / 2);
            end = Math.Min(end, start + (uint)maxHosts - 1);
        }
        for (uint u = start; u <= end && u >= start; u++)
            list.Add(new IPAddress(new[] { (byte)(u >> 24), (byte)(u >> 16), (byte)(u >> 8), (byte)u }));
        return list;
    }

    static uint ToUint(byte[] b) => (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);

    public static NetworkInfo? Detect()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)))
        {
            var props = nic.GetIPProperties();
            var uni = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(u.Address));
            if (uni == null) continue;
            var gw = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (gw == null || gw.Equals(IPAddress.Any)) continue;
            var info = new NetworkInfo
            {
                InterfaceName = nic.Name, LocalIp = uni.Address, Mask = uni.IPv4Mask, Gateway = gw,
                Mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))
            };
            info.Ssid = TryGetSsid();
            return info;
        }
        return null;
    }

    static string TryGetSsid()
    {
        try
        {
            var psi = new ProcessStartInfo("nmcli", "-t -f active,ssid dev wifi")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return output.Split('\n').FirstOrDefault(l => l.StartsWith("yes:"))?[4..].Trim() ?? "";
        }
        catch { return ""; }
    }

    public static async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return (await http.GetStringAsync("https://api.ipify.org", ct)).Trim();
        }
        catch { return ""; }
    }
}
