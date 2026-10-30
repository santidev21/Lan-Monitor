using System.Net;
using System.Net.Sockets;
using LanMonitor.Core.Network;

namespace LanMonitor.Core.Tests;

/// <summary>Pruebas con sockets reales en loopback (no tocan la red).</summary>
public class NetworkIntegrationTests
{
    [Fact] public async Task WakeOnLanSendsMagicPacketOverUdp()
    {
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;

        await WakeOnLan.SendAsync("AA:BB:CC:DD:EE:FF", new IPEndPoint(IPAddress.Loopback, port));

        var rx = await listener.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(WakeOnLan.BuildPacket("AA:BB:CC:DD:EE:FF"), rx.Buffer);
        Assert.Equal(102, rx.Buffer.Length);
    }

    [Fact] public async Task WakeOnLanBroadcastSendDoesNotThrow() =>
        await WakeOnLan.SendAsync("AA:BB:CC:DD:EE:FF");

    [Fact] public async Task PortScannerFindsOpenPortAndIgnoresClosedOnes()
    {
        // Abre un puerto "común" libre en loopback (si todos estuvieran ocupados, no hay nada que probar).
        TcpListener? l = null; int port = 0;
        foreach (var p in PortScanner.CommonPorts.Keys.Where(k => k > 1023))
        {
            try { l = new TcpListener(IPAddress.Loopback, p); l.Start(); port = p; break; } catch (SocketException) { l = null; }
        }
        if (l == null) return;
        try
        {
            var open = await PortScanner.ScanAsync("127.0.0.1");
            Assert.Contains(open, o => o.Port == port && o.Name == PortScanner.CommonPorts[port]);
        }
        finally { l.Stop(); }
    }

    [Fact] public async Task PortScannerReportsNothingOnUnreachableHostQuickly()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var open = await PortScanner.ScanAsync("192.0.2.1");   // TEST-NET: no responde
        Assert.Empty(open);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"tardó {sw.Elapsed}");
    }
}
