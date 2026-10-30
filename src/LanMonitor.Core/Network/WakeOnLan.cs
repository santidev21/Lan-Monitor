using System.Net;
using System.Net.Sockets;

namespace LanMonitor.Core.Network;

public static class WakeOnLan
{
    public static byte[] BuildPacket(string mac)
    {
        var bytes = mac.Split(':', '-').Select(h => Convert.ToByte(h, 16)).ToArray();
        if (bytes.Length != 6) throw new ArgumentException("MAC inválida");
        var packet = new byte[102];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Array.Copy(bytes, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    public static async Task SendAsync(string mac)
    {
        using var udp = new UdpClient { EnableBroadcast = true };
        var pkt = BuildPacket(mac);
        await udp.SendAsync(pkt, new IPEndPoint(IPAddress.Broadcast, 9));
    }
}
