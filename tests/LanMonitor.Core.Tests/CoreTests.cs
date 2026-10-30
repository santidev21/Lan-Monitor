using System.Net;
using LanMonitor.Core.Models;
using LanMonitor.Core.Network;
using LanMonitor.Core.Storage;
using LanMonitor.Core.Vendors;

namespace LanMonitor.Core.Tests;

public class ArpTests
{
    static readonly string[] Sample =
    {
        "IP address       HW type     Flags       HW address            Mask     Device",
        "192.168.1.48     0x1         0x2         fc:12:63:2f:ba:fe     *        wlo1",
        "192.168.1.254    0x1         0x2         e4:f7:5b:2c:84:8c     *        wlo1",
        "192.168.1.77     0x1         0x0         00:00:00:00:00:00     *        wlo1",
    };

    [Fact] public void ParsesOnlyCompleteEntries()
    {
        var r = ArpTable.Parse(Sample);
        Assert.Equal(2, r.Count);
        Assert.Equal("FC:12:63:2F:BA:FE", r[0].Mac);
        Assert.Equal("wlo1", r[0].Device);
    }

    [Theory]
    [InlineData("FA:12:63:2F:BA:FE", true)]   // 0xFA -> bit 0x02 puesto
    [InlineData("FC:12:63:2F:BA:FE", false)]  // 0xFC -> bit 0x02 en 0
    [InlineData("E4:F7:5B:2C:84:8C", false)]  // 0xE4
    [InlineData("02:00:00:00:00:01", true)]
    public void DetectsRandomMac(string mac, bool expected) => Assert.Equal(expected, ArpTable.IsRandomMac(mac));
}

public class SubnetTests
{
    [Fact] public void Slash24HasHostsWithoutNetworkAndBroadcast()
    {
        var h = NetworkInfo.HostsOf(IPAddress.Parse("192.168.1.10"), IPAddress.Parse("255.255.255.0"));
        Assert.Equal(254, h.Count);
        Assert.Equal("192.168.1.1", h[0].ToString());
        Assert.Equal("192.168.1.254", h[^1].ToString());
    }

    [Fact] public void BigSubnetIsLimited()
    {
        var h = NetworkInfo.HostsOf(IPAddress.Parse("10.0.5.5"), IPAddress.Parse("255.255.0.0"), 512);
        Assert.Equal(512, h.Count);
    }

    [Fact] public void PrefixLength() => Assert.Equal(24, NetworkInfo.PrefixLength(IPAddress.Parse("255.255.255.0")));
}

public class PingMathTests
{
    static PingSample S(double? r) => new() { RttMs = r, Time = DateTime.Now };

    [Fact] public void ComputesStatsJitterAndLoss()
    {
        var st = PingMath.Compute(new[] { S(10), S(20), S(null), S(10) });
        Assert.Equal(10, st.Current);
        Assert.Equal(10, st.Min);
        Assert.Equal(20, st.Max);
        Assert.Equal(25, st.LossPercent);
        Assert.Equal(10, st.Jitter, 3); // |20-10|, |10-20| -> 10
    }

    [Fact] public void EmptyIsSafe() => Assert.Equal(0, PingMath.Compute(Array.Empty<PingSample>()).Count);
}

public class MiscTests
{
    [Fact] public void WolPacketIs102Bytes()
    {
        var p = WakeOnLan.BuildPacket("AA:BB:CC:DD:EE:FF");
        Assert.Equal(102, p.Length);
        Assert.All(p.Take(6), b => Assert.Equal(0xFF, b));
        Assert.Equal(0xAA, p[6]); Assert.Equal(0xFF, p[101]);
    }

    [Fact] public void OuiLookupFindsVendor()
    {
        var o = new OuiLookup();
        o.Parse(new[] { "Registry,Assignment,Organization Name,Organization Address",
                        "MA-L,FC1263,\"ASUSTek COMPUTER INC.\",\"Taipei\"" });
        Assert.Equal("ASUSTek COMPUTER INC.", o.Lookup("fc:12:63:2f:ba:fe"));
        Assert.Equal("", o.Lookup("00:00:00:00:00:00"));
    }
}

public class DatabaseTests
{
    [Fact] public void DevicePersistsAndUpdates()
    {
        using var db = new Database(":memory:");
        var d = new Device { Mac = "AA:BB:CC:00:00:01", Ip = "192.168.1.5", FirstSeen = DateTime.Now, LastSeen = DateTime.Now, IsOnline = true };
        db.UpsertDevice(d);
        d.Nickname = "Celu"; d.IsKnown = true; db.UpsertDevice(d);
        var back = db.GetDevice(d.Mac)!;
        Assert.Equal("Celu", back.Nickname);
        Assert.True(back.IsKnown);
        Assert.Single(db.GetDevices());
        db.DeleteDevice(d.Mac);
        Assert.Empty(db.GetDevices());
    }

    [Fact] public void SpeedAndEventsRoundTrip()
    {
        using var db = new Database(":memory:");
        db.AddSpeed(new SpeedResult { Time = DateTime.Now, DownMbps = 100, UpMbps = 20, PingMs = 12 });
        db.AddEvent(new NetEvent { Time = DateTime.Now, Kind = EventKind.InternetDown, Message = "x" });
        Assert.Equal(100, db.GetSpeeds()[0].DownMbps);
        Assert.Equal(EventKind.InternetDown, db.GetEvents()[0].Kind);
    }
}
