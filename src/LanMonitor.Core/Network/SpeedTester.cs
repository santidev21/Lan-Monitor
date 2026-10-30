using System.Diagnostics;

namespace LanMonitor.Core.Network;

public record SpeedProgress(string Phase, double CurrentMbps, double Fraction);

/// <summary>Test de velocidad contra speed.cloudflare.com (sin API key).</summary>
public class SpeedTester
{
    const int Streams = 4;
    const double PhaseSeconds = 8;

    public async Task<Models.SpeedResult> RunAsync(IProgress<SpeedProgress> progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("lan-monitor");

        // Latencia
        var rtts = new List<double>();
        progress.Report(new("Latencia", 0, 0));
        for (int i = 0; i < 8; i++)
        {
            var sw = Stopwatch.StartNew();
            using var r = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=0", ct);
            sw.Stop();
            if (i > 0) rtts.Add(sw.Elapsed.TotalMilliseconds); // descartar la primera (handshake TLS)
        }
        rtts.Sort();
        double ping = rtts.Count > 0 ? rtts[rtts.Count / 2] : 0;

        double down = await MeasureAsync("Descarga", progress, ct, async (counter, token) =>
        {
            while (!token.IsCancellationRequested)
            {
                using var resp = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=25000000", HttpCompletionOption.ResponseHeadersRead, token);
                await using var s = await resp.Content.ReadAsStreamAsync(token);
                var buf = new byte[81920]; int n;
                while ((n = await s.ReadAsync(buf, token)) > 0) Interlocked.Add(ref counter.Bytes, n);
            }
        });

        double up = await MeasureAsync("Subida", progress, ct, async (counter, token) =>
        {
            var chunk = new byte[2_000_000];
            while (!token.IsCancellationRequested)
            {
                using var content = new CountingContent(chunk, counter);
                using var resp = await http.PostAsync("https://speed.cloudflare.com/__up", content, token);
            }
        });

        return new Models.SpeedResult { Time = DateTime.Now, DownMbps = down, UpMbps = up, PingMs = ping };
    }

    class Counter { public long Bytes; }

    class CountingContent : HttpContent
    {
        readonly byte[] _data; readonly Counter _c;
        public CountingContent(byte[] data, Counter c) { _data = data; _c = c; Headers.ContentLength = data.Length; }
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            const int step = 65536;
            for (int off = 0; off < _data.Length; off += step)
            {
                int len = Math.Min(step, _data.Length - off);
                await stream.WriteAsync(_data.AsMemory(off, len));
                Interlocked.Add(ref _c.Bytes, len);
            }
        }
        protected override bool TryComputeLength(out long length) { length = _data.Length; return true; }
    }

    static async Task<double> MeasureAsync(string phase, IProgress<SpeedProgress> progress, CancellationToken ct,
        Func<Counter, CancellationToken, Task> worker)
    {
        var counter = new Counter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(PhaseSeconds));
        var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(async () =>
        {
            try { await worker(counter, cts.Token); } catch (OperationCanceledException) { } catch (HttpRequestException) { }
        })).ToArray();

        var sw = Stopwatch.StartNew();
        long last = 0; var lastT = TimeSpan.Zero;
        while (!cts.IsCancellationRequested)
        {
            try { await Task.Delay(250, cts.Token); } catch (OperationCanceledException) { break; }
            long b = Interlocked.Read(ref counter.Bytes);
            double dt = (sw.Elapsed - lastT).TotalSeconds;
            double inst = (b - last) * 8 / 1e6 / Math.Max(dt, 0.001);
            last = b; lastT = sw.Elapsed;
            progress.Report(new(phase, inst, Math.Min(1, sw.Elapsed.TotalSeconds / PhaseSeconds)));
        }
        await Task.WhenAll(workers);
        ct.ThrowIfCancellationRequested();
        // Ignorar el primer segundo de arranque no es necesario para una estimación simple.
        return Interlocked.Read(ref counter.Bytes) * 8 / 1e6 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
    }
}
