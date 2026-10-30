using System.Net;
using LanMonitor.Core.Alerts;
using LanMonitor.Core.Config;
using LanMonitor.Core.Models;
using LanMonitor.Core.Network;
using LanMonitor.Core.Storage;

namespace LanMonitor.Core.Tests;

public class AlertRulesTests
{
    static PingSample S(double? r) => new() { RttMs = r, Time = DateTime.Now };
    static IReadOnlyList<PingSample> Seq(params double?[] v) => v.Select(S).ToList();
    static IReadOnlyList<PingSample> Fails(int n) => Enumerable.Range(0, n).Select(_ => S(null)).ToList();
    static IReadOnlyList<IReadOnlyList<PingSample>> Ext(params IReadOnlyList<PingSample>[] t) => t;

    [Fact] public void DownWhenAllExternalTargetsFailFourTimes() =>
        Assert.Equal(EventKind.InternetDown, AlertRules.InternetTransition(true, Ext(Fails(4), Fails(5))));

    [Fact] public void NotDownIfOneTargetStillAnswers() =>
        Assert.Null(AlertRules.InternetTransition(true, Ext(Fails(6), Seq(null, null, null, 20))));

    [Fact] public void NotDownWithFewerThanFourSamples() =>
        Assert.Null(AlertRules.InternetTransition(true, Ext(Fails(3), Fails(3))));

    [Fact] public void NoTransitionWithoutExternalTargets() =>
        Assert.Null(AlertRules.InternetTransition(true, Ext()));

    [Fact] public void RestoredWhenLastSampleAnswers() =>
        Assert.Equal(EventKind.InternetRestored, AlertRules.InternetTransition(false, Ext(Seq(null, null, null, null, 25), Fails(5))));

    [Fact] public void StaysDownWhileStillFailing() =>
        Assert.Null(AlertRules.InternetTransition(false, Ext(Fails(5))));

    [Fact] public void StaysUpWhileAnswering() =>
        Assert.Null(AlertRules.InternetTransition(true, Ext(Seq(10, 12, 11, 10))));

    [Fact] public void HighLatencyNeedsEightValidSamplesAboveThreshold()
    {
        var high = Enumerable.Range(0, 10).Select(_ => S(200)).ToList();
        Assert.True(AlertRules.IsHighLatency(high, 150, out var avg));
        Assert.Equal(200, avg);
        Assert.False(AlertRules.IsHighLatency(high.Take(7).ToList(), 150, out _));           // pocas muestras
        Assert.False(AlertRules.IsHighLatency(Enumerable.Range(0, 10).Select(_ => S(100)).ToList(), 150, out _)); // bajo umbral
    }

    [Fact] public void LostSamplesDoNotCountTowardLatencyAverage()
    {
        var s = Enumerable.Range(0, 5).Select(_ => S(null)).Concat(Enumerable.Range(0, 5).Select(_ => S(500))).ToList();
        Assert.False(AlertRules.IsHighLatency(s, 150, out _)); // solo 5 válidas
    }

    [Fact] public void DiagnoseDistinguishesLanFromIsp()
    {
        Assert.Contains("proveedor", AlertRules.Diagnose(Seq(5, 6, 5)));
        Assert.Contains("WiFi/LAN", AlertRules.Diagnose(Fails(3)));
        Assert.Equal("sin datos", AlertRules.Diagnose(null));
    }
}

public class DeviceMergeTests
{
    const string RouterMac = "E4:F7:5B:2C:84:8C", SelfMac = "3C:55:76:79:2B:4F";

    static NetworkInfo Net() => new()
    {
        InterfaceName = "wlo1", LocalIp = IPAddress.Parse("192.168.1.10"), Mask = IPAddress.Parse("255.255.255.0"),
        Gateway = IPAddress.Parse("192.168.1.254"), Mac = SelfMac
    };

    static (MonitorService svc, Database db) Make(Action<Database>? seed = null)
    {
        var db = new Database(":memory:");
        seed?.Invoke(db);
        var cfg = new AppConfig { DesktopNotifications = false };
        return (new MonitorService(cfg, db) { Net = Net() }, db);
    }

    static ScanHit Router => new("192.168.1.254", RouterMac, "router");
    static ScanHit Hit(string ip, string mac, string host = "") => new(ip, mac, host);
    static List<EventKind> Kinds(Database db) => db.GetEvents().Select(e => e.Kind).ToList();

    [Fact] public void FirstScanEverIsSilentButRegistersDevices()
    {
        var (svc, db) = Make();
        svc.MergeScan(new() { Router, Hit("192.168.1.5", "00:11:22:33:44:55") });
        Assert.Empty(db.GetEvents());
        Assert.Equal(3, db.GetDevices().Count); // router + celular + este equipo
        Assert.True(db.GetDevice(RouterMac)!.IsKnown && db.GetDevice(RouterMac)!.IsGateway);
        Assert.True(db.GetDevice(SelfMac)!.IsSelf);
        Assert.False(db.GetDevice("00:11:22:33:44:55")!.IsKnown);
    }

    [Fact] public void NewUnknownDeviceAfterFirstRunRaisesAlert()
    {
        var (svc, db) = Make();
        svc.MergeScan(new() { Router });
        svc.MergeScan(new() { Router, Hit("192.168.1.77", "00:11:22:33:44:99") });
        Assert.Contains(EventKind.NewUnknownDevice, Kinds(db));
        Assert.Single(db.GetEvents(), e => e.Kind == EventKind.NewUnknownDevice);
    }

    [Fact] public void SameUnknownDeviceDoesNotAlertTwice()
    {
        var (svc, db) = Make();
        svc.MergeScan(new() { Router });
        var phone = Hit("192.168.1.77", "00:11:22:33:44:99");
        svc.MergeScan(new() { Router, phone });
        svc.MergeScan(new() { Router, phone });
        Assert.Single(db.GetEvents(), e => e.Kind == EventKind.NewUnknownDevice);
    }

    [Fact] public void KnownDeviceNeedsTwoMissedScansToDisconnect()
    {
        var (svc, db) = Make();
        var tv = Hit("192.168.1.20", "00:AA:BB:CC:DD:01");
        svc.MergeScan(new() { Router, tv });
        svc.SetKnown(tv.Mac, true);

        svc.MergeScan(new() { Router });
        Assert.True(db.GetDevice(tv.Mac)!.IsOnline);                     // 1 escaneo perdido: aún online
        Assert.DoesNotContain(EventKind.DeviceDisconnected, Kinds(db));

        svc.MergeScan(new() { Router });
        Assert.False(db.GetDevice(tv.Mac)!.IsOnline);                    // 2: desconectado
        Assert.Contains(EventKind.DeviceDisconnected, Kinds(db));

        svc.MergeScan(new() { Router, tv });
        Assert.Contains(EventKind.DeviceConnected, Kinds(db));           // vuelve
    }

    [Fact] public void UnknownDeviceDisconnectIsNotAlerted()
    {
        var (svc, db) = Make();
        var d = Hit("192.168.1.30", "00:AA:BB:CC:DD:02");
        svc.MergeScan(new() { Router, d });
        svc.MergeScan(new() { Router }); svc.MergeScan(new() { Router });
        Assert.False(db.GetDevice(d.Mac)!.IsOnline);
        Assert.DoesNotContain(EventKind.DeviceDisconnected, Kinds(db));
    }

    [Fact] public void RotatedPrivateMacKeepsNicknameAndDoesNotAlert()
    {
        var (svc, db) = Make();
        var before = Hit("192.168.1.40", "AA:11:22:33:44:55", "Pixel-de-Ana");   // 0xAA => MAC privada
        svc.MergeScan(new() { Router, before });
        svc.SetNickname(before.Mac, "Celu Ana");

        var after = Hit("192.168.1.41", "BA:99:88:77:66:55", "Pixel-de-Ana");
        svc.MergeScan(new() { Router, after });

        Assert.Null(db.GetDevice(before.Mac));
        var d = db.GetDevice(after.Mac)!;
        Assert.Equal("Celu Ana", d.Nickname);
        Assert.True(d.IsKnown);
        Assert.Equal(1, db.GetDevices().Count(x => x.Hostname == "Pixel-de-Ana"));
        Assert.DoesNotContain(EventKind.NewUnknownDevice, Kinds(db));
    }

    [Fact] public void AmbiguousRotationIsTreatedAsNewDevice()
    {
        var (svc, db) = Make();
        svc.MergeScan(new() { Router, Hit("192.168.1.40", "AA:11:22:33:44:55", "iPhone"), Hit("192.168.1.41", "AA:11:22:33:44:66", "iPhone") });
        svc.MergeScan(new() { Router, Hit("192.168.1.42", "BA:99:88:77:66:55", "iPhone") });
        Assert.NotNull(db.GetDevice("AA:11:22:33:44:55"));   // no se fusiona a ciegas
        Assert.Contains(EventKind.NewUnknownDevice, Kinds(db));
    }

    [Fact] public void NonRandomMacIsNeverMergedByHostname()
    {
        var (svc, db) = Make();
        svc.MergeScan(new() { Router, Hit("192.168.1.50", "00:AA:BB:00:00:01", "laptop") });
        svc.MergeScan(new() { Router, Hit("192.168.1.51", "00:AA:BB:00:00:02", "laptop") });
        Assert.NotNull(db.GetDevice("00:AA:BB:00:00:01"));
        Assert.NotNull(db.GetDevice("00:AA:BB:00:00:02"));
    }

    [Fact] public void PurgeRemovesOnlyStaleUnknownPrivateMacDevices()
    {
        var (_, db) = Make();
        DateTime old = DateTime.Now.AddDays(-40);
        Device D(string mac, bool known, bool rnd) => new() { Mac = mac, Ip = "1.1.1.1", FirstSeen = old, LastSeen = old, IsKnown = known, IsRandomMac = rnd };
        db.UpsertDevice(D("AA:00:00:00:00:01", false, true));   // se borra
        db.UpsertDevice(D("AA:00:00:00:00:02", true, true));    // conocido: se queda
        db.UpsertDevice(D("00:00:00:00:00:03", false, false));  // MAC real: se queda
        db.Purge(30);
        Assert.Equal(new[] { "00:00:00:00:00:03", "AA:00:00:00:00:02" }, db.GetDevices().Select(x => x.Mac).OrderBy(x => x).ToArray());
    }
}
