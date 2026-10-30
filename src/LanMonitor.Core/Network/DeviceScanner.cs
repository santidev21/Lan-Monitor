using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LanMonitor.Core.Models;
using LanMonitor.Core.Vendors;

namespace LanMonitor.Core.Network;

public record ScanHit(string Ip, string Mac, string Hostname);

public class DeviceScanner
{
    readonly OuiLookup _oui;
    public DeviceScanner(OuiLookup oui) => _oui = oui;

    /// <summary>Barrido de subred (ping + UDP) y lectura de la tabla ARP del kernel. Sin root.</summary>
    public async Task<List<ScanHit>> ScanAsync(NetworkInfo net, CancellationToken ct = default)
    {
        if (net.LocalIp == null || net.Mask == null) return new();
        var hosts = NetworkInfo.HostsOf(net.LocalIp, net.Mask);
        using var gate = new SemaphoreSlim(64);
        var tasks = hosts.Where(h => !h.Equals(net.LocalIp)).Select(async h =>
        {
            await gate.WaitAsync(ct);
            try { await ProbeAsync(h, ct); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        await Task.Delay(300, ct); // dejar que el kernel complete ARP

        var subnetIps = hosts.Select(h => h.ToString()).ToHashSet();
        var hits = ArpTable.Read()
            .Where(e => e.Device == net.InterfaceName && subnetIps.Contains(e.Ip))
            .ToList();

        var result = new List<ScanHit>();
        await Task.WhenAll(hits.Select(async e =>
        {
            var name = await ResolveHostnameAsync(e.Ip, ct);
            lock (result) result.Add(new ScanHit(e.Ip, e.Mac, name));
        }));
        return result;
    }

    static async Task ProbeAsync(IPAddress ip, CancellationToken ct)
    {
        // UDP al puerto 9 fuerza la resolución ARP aunque el equipo ignore el ping.
        try
        {
            using var udp = new UdpClient();
            await udp.SendAsync(new byte[] { 0 }, 1, new IPEndPoint(ip, 9));
        }
        catch { }
        try
        {
            using var ping = new Ping();
            await ping.SendPingAsync(ip, 500);
        }
        catch { }
    }

    static async Task<string> ResolveHostnameAsync(string ip, CancellationToken ct)
    {
        try
        {
            var t = Dns.GetHostEntryAsync(ip, ct);
            if (await Task.WhenAny(t, Task.Delay(1000, ct)) == t)
            {
                var name = (await t).HostName;
                return name == ip ? "" : name;
            }
        }
        catch { }
        return "";
    }

    /// <summary>Convierte los resultados del barrido en dispositivos (sin tocar la BD).</summary>
    public Device ToDevice(ScanHit hit, NetworkInfo net)
    {
        var now = DateTime.Now;
        return new Device
        {
            Mac = hit.Mac, Ip = hit.Ip, Hostname = hit.Hostname, Vendor = _oui.Lookup(hit.Mac),
            IsGateway = net.Gateway?.ToString() == hit.Ip, IsRandomMac = ArpTable.IsRandomMac(hit.Mac),
            IsOnline = true, FirstSeen = now, LastSeen = now
        };
    }
}
