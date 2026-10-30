using System.Net.Sockets;

namespace LanMonitor.Core.Network;

public static class PortScanner
{
    public static readonly Dictionary<int, string> CommonPorts = new()
    {
        [21] = "FTP", [22] = "SSH", [23] = "Telnet", [53] = "DNS", [80] = "HTTP", [139] = "NetBIOS", [443] = "HTTPS",
        [445] = "SMB", [554] = "RTSP", [1883] = "MQTT", [3389] = "RDP", [5900] = "VNC", [8000] = "HTTP-alt",
        [8080] = "HTTP-proxy", [8443] = "HTTPS-alt", [9100] = "Impresora", [32400] = "Plex"
    };

    public static async Task<List<(int Port, string Name)>> ScanAsync(string ip, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var open = new List<(int, string)>(); var gate = new object(); int done = 0;
        await Parallel.ForEachAsync(CommonPorts, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct }, async (kv, token) =>
        {
            try
            {
                using var c = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(700);
                await c.ConnectAsync(ip, kv.Key, cts.Token);
                lock (gate) open.Add((kv.Key, kv.Value));
            }
            catch { }
            progress?.Report(Interlocked.Increment(ref done));
        });
        return open.OrderBy(p => p.Item1).ToList();
    }
}
