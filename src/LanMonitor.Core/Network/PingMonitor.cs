using System.Net.NetworkInformation;
using LanMonitor.Core.Models;

namespace LanMonitor.Core.Network;

public record PingStats(double? Current, double? Min, double? Avg, double? Max, double Jitter, double LossPercent, int Count);

public class PingTarget
{
    public string Host { get; init; } = "";
    public string Label { get; init; } = "";
    public List<PingSample> Samples { get; } = new();
}

public static class PingMath
{
    public static PingStats Compute(IReadOnlyList<PingSample> samples)
    {
        if (samples.Count == 0) return new(null, null, null, null, 0, 0, 0);
        var ok = samples.Where(s => s.RttMs.HasValue).Select(s => s.RttMs!.Value).ToList();
        double loss = 100.0 * (samples.Count - ok.Count) / samples.Count;
        double jitter = 0;
        if (ok.Count > 1) jitter = ok.Zip(ok.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new(samples[^1].RttMs, ok.Count > 0 ? ok.Min() : null, ok.Count > 0 ? ok.Average() : null,
            ok.Count > 0 ? ok.Max() : null, jitter, loss, samples.Count);
    }
}

/// <summary>Ping continuo (1 Hz) a varios objetivos con buffer circular de muestras.</summary>
public class PingMonitor
{
    public const int MaxSamples = 900; // 15 min a 1 Hz
    public List<PingTarget> Targets { get; } = new();
    public event Action<PingTarget, PingSample>? SampleAdded;

    public void SetTargets(string gateway, IEnumerable<string> extra)
    {
        lock (Targets)
        {
            Targets.Clear();
            Targets.Add(new PingTarget { Host = gateway, Label = "Gateway" });
            foreach (var h in extra.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                Targets.Add(new PingTarget { Host = h, Label = h });
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            PingTarget[] snapshot;
            lock (Targets) snapshot = Targets.ToArray();
            await Task.WhenAll(snapshot.Select(t => PingOnce(t)));
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    async Task PingOnce(PingTarget t)
    {
        double? rtt = null;
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(t.Host, 1500);
            if (r.Status == IPStatus.Success) rtt = r.RoundtripTime;
        }
        catch { }
        var s = new PingSample { Target = t.Host, Time = DateTime.Now, RttMs = rtt };
        lock (t.Samples)
        {
            t.Samples.Add(s);
            if (t.Samples.Count > MaxSamples) t.Samples.RemoveAt(0);
        }
        SampleAdded?.Invoke(t, s);
    }

    public static IReadOnlyList<PingSample> Window(PingTarget t, TimeSpan span)
    {
        var from = DateTime.Now - span;
        lock (t.Samples) return t.Samples.Where(s => s.Time >= from).ToList();
    }
}
