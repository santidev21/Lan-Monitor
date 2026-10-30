using LanMonitor.Core.Health;
using LanMonitor.Core.Models;

namespace LanMonitor.Core.Tests;

public class ModemHealthTests
{
    static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0);

    /// <summary>`count` muestras, una por segundo, terminando en Now. lossEvery=n => una de cada n se pierde.</summary>
    static List<PingSample> Samples(int count, double rtt = 20, int lossEvery = 0, double jitter = 0) =>
        Enumerable.Range(0, count).Select(k => new PingSample
        {
            Time = Now.AddSeconds(-(count - k)),
            RttMs = lossEvery > 0 && k % lossEvery == 0 ? null : rtt + (k % 2 == 0 ? 0 : jitter)
        }).ToList();

    static NetEvent Drop(TimeSpan ago) => new() { Kind = EventKind.InternetDown, Time = Now - ago, Message = "x" };
    static SpeedResult Speed(double down, int minsAgo = 0) => new() { DownMbps = down, UpMbps = 10, PingMs = 20, Time = Now.AddMinutes(-minsAgo) };

    static HealthInput Input(
        List<PingSample>? gw = null, List<PingSample>? ext = null, bool up = true, DateTime? downSince = null,
        List<NetEvent>? events = null, List<SpeedResult>? speeds = null,
        List<(DateTime, double, double)>? history = null) =>
        new(Now, up, downSince, gw ?? Samples(900, 3), new[] { (IReadOnlyList<PingSample>)(ext ?? Samples(900, 25)) },
            events ?? new(), speeds ?? new(), 10, history ?? new());

    [Fact] public void HealthyConnectionIsGood()
    {
        var r = ModemHealth.Analyze(Input(speeds: new() { Speed(120) }));
        Assert.Equal(HealthLevel.Good, r.Level);
        Assert.Empty(r.Reasons);
    }

    [Fact] public void NoDataIsUnknown()
    {
        var r = ModemHealth.Analyze(Input(gw: new(), ext: new()));
        Assert.Equal(HealthLevel.Unknown, r.Level);
    }

    [Fact] public void OutageWithLiveRouterOverTwoMinutesSuggestsRestart()
    {
        var r = ModemHealth.Analyze(Input(up: false, downSince: Now.AddMinutes(-3), ext: Samples(900, 25, lossEvery: 1)));
        Assert.Equal(HealthLevel.Restart, r.Level);
        Assert.Contains(r.Reasons, x => x.Contains("router responde"));
        Assert.NotEmpty(r.Tips);
    }

    [Fact] public void ShortOutageOnlyWatch() =>
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(up: false, downSince: Now.AddSeconds(-40))).Level);

    [Fact] public void OutageWithDeadRouterIsNotAModemRestart()
    {
        var r = ModemHealth.Analyze(Input(up: false, downSince: Now.AddMinutes(-10), gw: Samples(900, 3, lossEvery: 1)));
        Assert.Equal(HealthLevel.Watch, r.Level);
        Assert.Contains(r.Reasons, x => x.Contains("ni el router"));
    }

    [Fact] public void ThreeDropsInThreeHoursSuggestRestart()
    {
        var ev = new List<NetEvent> { Drop(TimeSpan.FromMinutes(10)), Drop(TimeSpan.FromMinutes(60)), Drop(TimeSpan.FromMinutes(150)) };
        Assert.Equal(HealthLevel.Restart, ModemHealth.Analyze(Input(events: ev)).Level);
    }

    [Fact] public void TwoDropsOnlyWatchAndOldDropsDontCount()
    {
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(events: new() { Drop(TimeSpan.FromMinutes(10)), Drop(TimeSpan.FromMinutes(90)) })).Level);
        var old = new List<NetEvent> { Drop(TimeSpan.FromHours(4)), Drop(TimeSpan.FromHours(5)), Drop(TimeSpan.FromHours(6)) };
        Assert.Equal(HealthLevel.Good, ModemHealth.Analyze(Input(events: old)).Level);
    }

    [Fact] public void UpstreamLossWithHealthyRouterSuggestsRestart()
    {
        var r = ModemHealth.Analyze(Input(ext: Samples(900, 25, lossEvery: 10)));      // 10% pérdida a internet
        Assert.Equal(HealthLevel.Restart, r.Level);
        Assert.Contains(r.Reasons, x => x.Contains("router sano"));
    }

    [Fact] public void LossAlreadyAtRouterBlamesWifiNotModem()
    {
        var r = ModemHealth.Analyze(Input(gw: Samples(900, 3, lossEvery: 8), ext: Samples(900, 25, lossEvery: 8)));
        Assert.Equal(HealthLevel.Watch, r.Level);
        Assert.Contains(r.Reasons, x => x.Contains("WiFi"));
        Assert.DoesNotContain(r.Reasons, x => x.Contains("router sano"));
    }

    [Fact] public void HighJitterToInternetWithHealthyRouter()
    {
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(ext: Samples(900, 25, jitter: 60))).Level);
        Assert.Equal(HealthLevel.Restart, ModemHealth.Analyze(Input(ext: Samples(900, 25, jitter: 120))).Level);
    }

    static List<(DateTime, double, double)> Baseline(double avg) =>
        Enumerable.Range(60, 120).Select(m => (Now.AddMinutes(-m), avg, 0.0)).ToList();

    [Fact] public void LatencyCreepComparedToBaseline()
    {
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(ext: Samples(900, 70), history: Baseline(25))).Level);     // ~2.8x, +45
        Assert.Equal(HealthLevel.Restart, ModemHealth.Analyze(Input(ext: Samples(900, 130), history: Baseline(25))).Level);  // >3x, +105
        Assert.Equal(HealthLevel.Good, ModemHealth.Analyze(Input(ext: Samples(900, 30), history: Baseline(25))).Level);
    }

    [Fact] public void LatencyBaselineNeedsEnoughHistory() =>
        Assert.Equal(HealthLevel.Good, ModemHealth.Analyze(Input(ext: Samples(900, 130), history: Baseline(25).Take(10).ToList())).Level);

    [Fact] public void SpeedDropsAgainstOwnMedian()
    {
        List<SpeedResult> S(params double[] v) => v.Select((d, i) => Speed(d, i * 60)).ToList();   // más reciente primero
        Assert.Equal(HealthLevel.Restart, ModemHealth.Analyze(Input(speeds: S(30, 35, 100, 110, 105, 98))).Level);   // 2 seguidos < 50%
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(speeds: S(30, 100, 110, 105, 98, 102))).Level);    // 1 solo
        Assert.Equal(HealthLevel.Good, ModemHealth.Analyze(Input(speeds: S(95, 100, 110, 105, 98, 102))).Level);
    }

    [Fact] public void SpeedBelowUserMinimumIsWatch() =>
        Assert.Equal(HealthLevel.Watch, ModemHealth.Analyze(Input(speeds: new() { Speed(4) })).Level);

    [Fact] public void WorstSignalWins()
    {
        var r = ModemHealth.Analyze(Input(events: new() { Drop(TimeSpan.FromMinutes(10)), Drop(TimeSpan.FromMinutes(20)) },   // Watch
                                          ext: Samples(900, 25, lossEvery: 10)));                                                // Restart
        Assert.Equal(HealthLevel.Restart, r.Level);
        Assert.True(r.Reasons.Count >= 2);
    }
}
