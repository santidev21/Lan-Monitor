using LanMonitor.Core.Config;
using LanMonitor.Core.Events;
using LanMonitor.Core.Models;
using LanMonitor.Core.Network;
using LanMonitor.Core.Storage;
using LanMonitor.Core.Vendors;

namespace LanMonitor.Core;

/// <summary>Orquesta escaneo de dispositivos, ping, alertas, tests de velocidad y persistencia.</summary>
public class MonitorService
{
    public AppConfig Config { get; }
    public Database Db { get; }
    public OuiLookup Oui { get; } = new();
    public PingMonitor Ping { get; } = new();
    public SpeedTester Speed { get; } = new();
    public NetworkInfo? Net { get; private set; }
    public string PublicIp { get; private set; } = "";
    public bool InternetUp { get; private set; } = true;
    public bool Scanning { get; private set; }
    public bool SpeedRunning { get; private set; }
    public DateTime? LastScan { get; private set; }

    public event Action? DevicesUpdated;
    public event Action<NetEvent>? EventAdded;
    public event Action? StatusChanged;
    public event Action<SpeedResult>? SpeedFinished;

    readonly DeviceScanner _scanner;
    readonly Dictionary<string, int> _missed = new();
    readonly SemaphoreSlim _scanLock = new(1, 1);
    DateTime _lastHighLatencyAlert = DateTime.MinValue;
    DateTime _lastMinuteSaved = DateTime.MinValue;
    bool _firstRunEver;

    public MonitorService(AppConfig config, Database db)
    {
        Config = config; Db = db;
        _scanner = new DeviceScanner(Oui);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _firstRunEver = Db.GetDevices().Count == 0;
        RefreshNetwork();
        _ = Task.Run(async () => { await Oui.LoadAsync(ct); DevicesUpdated?.Invoke(); }, ct);
        _ = Task.Run(async () => { PublicIp = await NetworkInfo.GetPublicIpAsync(ct); StatusChanged?.Invoke(); }, ct);
        _ = Ping.RunAsync(ct);
        _ = Task.Run(() => WatchLoop(ct), ct);
        _ = Task.Run(() => ScanLoop(ct), ct);
    }

    public void RefreshNetwork()
    {
        Net = NetworkInfo.Detect();
        if (Net?.Gateway != null) Ping.SetTargets(Net.Gateway.ToString(), Config.ExtraPingTargets);
        else Ping.SetTargets("127.0.0.1", Config.ExtraPingTargets);
        StatusChanged?.Invoke();
    }

    // ------------------------------------------------------------ Escaneo
    async Task ScanLoop(CancellationToken ct)
    {
        DateTime lastAutoSpeed = DateTime.Now;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanNowAsync(ct);
                if (Config.AutoSpeedTest && !SpeedRunning && DateTime.Now - lastAutoSpeed > TimeSpan.FromHours(Config.AutoSpeedTestHours))
                {
                    lastAutoSpeed = DateTime.Now;
                    _ = RunSpeedTestAsync(null, ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, Config.ScanIntervalSeconds)), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { try { await Task.Delay(5000, ct); } catch { break; } }
        }
    }

    public async Task ScanNowAsync(CancellationToken ct = default)
    {
        if (!await _scanLock.WaitAsync(0, ct)) return;
        try
        {
            Scanning = true; StatusChanged?.Invoke();
            if (Net == null) RefreshNetwork();
            if (Net == null) return;

            var hits = await _scanner.ScanAsync(Net, ct);
            MergeScan(hits);
            LastScan = DateTime.Now;
        }
        finally { Scanning = false; _scanLock.Release(); StatusChanged?.Invoke(); DevicesUpdated?.Invoke(); }
    }

    void MergeScan(List<ScanHit> hits)
    {
        var net = Net!;
        var now = DateTime.Now;
        var stored = Db.GetDevices().ToDictionary(d => d.Mac);
        var seen = new HashSet<string>();

        // Este equipo no aparece en su propia tabla ARP: se añade a mano.
        var all = hits.ToList();
        if (!string.IsNullOrEmpty(net.Mac) && net.LocalIp != null)
            all.Add(new ScanHit(net.LocalIp.ToString(), net.Mac, Environment.MachineName));

        foreach (var hit in all)
        {
            seen.Add(hit.Mac);
            _missed[hit.Mac] = 0;
            var fresh = _scanner.ToDevice(hit, net);
            fresh.IsSelf = hit.Mac == net.Mac;

            if (stored.TryGetValue(hit.Mac, out var d))
            {
                bool wasOnline = d.IsOnline;
                d.Ip = hit.Ip; d.LastSeen = now; d.IsOnline = true;
                d.IsGateway = fresh.IsGateway; d.IsSelf = fresh.IsSelf; d.IsRandomMac = fresh.IsRandomMac;
                if (!string.IsNullOrEmpty(fresh.Hostname)) d.Hostname = fresh.Hostname;
                if (!string.IsNullOrEmpty(fresh.Vendor)) d.Vendor = fresh.Vendor;
                Db.UpsertDevice(d);
                if (!wasOnline)
                {
                    Db.AddConnection(d.Mac, true, now);
                    if (d.IsKnown) Raise(EventKind.DeviceConnected, $"{d.DisplayName} se conectó ({d.Ip})", d.Mac);
                }
            }
            else
            {
                fresh.IsKnown = fresh.IsSelf || fresh.IsGateway;
                Db.UpsertDevice(fresh);
                Db.AddConnection(fresh.Mac, true, now);
                if (!fresh.IsKnown && !_firstRunEver)
                    Raise(EventKind.NewUnknownDevice, $"Dispositivo desconocido: {fresh.Ip} {fresh.Mac} {fresh.Vendor}".TrimEnd(), fresh.Mac, notify: true);
            }
        }

        foreach (var d in stored.Values.Where(x => x.IsOnline && !seen.Contains(x.Mac)))
        {
            _missed[d.Mac] = _missed.GetValueOrDefault(d.Mac) + 1;
            if (_missed[d.Mac] < Config.OfflineScansBeforeDisconnect) continue;
            d.IsOnline = false;
            Db.UpsertDevice(d);
            Db.AddConnection(d.Mac, false, now);
            if (d.IsKnown) Raise(EventKind.DeviceDisconnected, $"{d.DisplayName} se desconectó", d.Mac);
        }
        _firstRunEver = false;
    }

    // ------------------------------------------------------------ Dispositivos
    public void SetKnown(string mac, bool known)
    {
        var d = Db.GetDevice(mac); if (d == null) return;
        d.IsKnown = known; Db.UpsertDevice(d); DevicesUpdated?.Invoke();
    }

    public void SetNickname(string mac, string nick)
    {
        var d = Db.GetDevice(mac); if (d == null) return;
        d.Nickname = nick.Trim(); if (d.Nickname != "") d.IsKnown = true;
        Db.UpsertDevice(d); DevicesUpdated?.Invoke();
    }

    public void SetNotes(string mac, string notes)
    {
        var d = Db.GetDevice(mac); if (d == null) return;
        d.Notes = notes; Db.UpsertDevice(d);
    }

    public void Forget(string mac) { Db.DeleteDevice(mac); DevicesUpdated?.Invoke(); }

    // ------------------------------------------------------------ Vigilancia de internet / latencia
    async Task WatchLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, ct);
                CheckInternet();
                CheckLatency();
                SaveMinuteAggregates();
                StatusChanged?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    void CheckInternet()
    {
        PingTarget[] ext;
        lock (Ping.Targets) ext = Ping.Targets.Skip(1).ToArray();
        if (ext.Length == 0) return;
        // Caído = los últimos 4 pings de TODOS los objetivos externos fallaron.
        bool down = ext.All(t => { lock (t.Samples) return t.Samples.Count >= 4 && t.Samples.TakeLast(4).All(s => s.RttMs == null); });
        bool up = ext.Any(t => { lock (t.Samples) return t.Samples.Count > 0 && t.Samples[^1].RttMs != null; });
        if (InternetUp && down)
        {
            InternetUp = false;
            Raise(EventKind.InternetDown, "Internet caído: " + Diagnose(), null, notify: true);
        }
        else if (!InternetUp && up)
        {
            InternetUp = true;
            Raise(EventKind.InternetRestored, "Internet restaurado", null, notify: true);
        }
    }

    /// <summary>Diagnóstico simple: ¿falla el gateway (LAN/WiFi) o solo internet (proveedor)?</summary>
    public string Diagnose()
    {
        PingTarget? gw;
        lock (Ping.Targets) gw = Ping.Targets.FirstOrDefault();
        if (gw == null) return "sin datos";
        bool gwOk;
        lock (gw.Samples) gwOk = gw.Samples.TakeLast(3).Any(s => s.RttMs != null);
        return gwOk ? "el router responde pero no hay salida: problema del proveedor (ISP)"
                    : "no responde el router: problema en tu WiFi/LAN";
    }

    void CheckLatency()
    {
        if (!InternetUp || DateTime.Now - _lastHighLatencyAlert < TimeSpan.FromMinutes(5)) return;
        PingTarget[] ext;
        lock (Ping.Targets) ext = Ping.Targets.Skip(1).ToArray();
        foreach (var t in ext)
        {
            List<double> last;
            lock (t.Samples) last = t.Samples.TakeLast(10).Where(s => s.RttMs != null).Select(s => s.RttMs!.Value).ToList();
            if (last.Count >= 8 && last.Average() > Config.HighLatencyMs)
            {
                _lastHighLatencyAlert = DateTime.Now;
                Raise(EventKind.HighLatency, $"Latencia alta a {t.Host}: {last.Average():F0} ms (umbral {Config.HighLatencyMs:F0})", null, notify: true);
                break;
            }
        }
    }

    void SaveMinuteAggregates()
    {
        var minute = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, 0);
        if (minute == _lastMinuteSaved) return;
        // Guardar el minuto anterior ya completo.
        var prev = minute.AddMinutes(-1);
        if (_lastMinuteSaved != DateTime.MinValue || DateTime.Now.Second > 5)
        {
            PingTarget[] all; lock (Ping.Targets) all = Ping.Targets.ToArray();
            foreach (var t in all)
            {
                List<PingSample> s; lock (t.Samples) s = t.Samples.Where(x => x.Time >= prev && x.Time < minute).ToList();
                if (s.Count == 0) continue;
                var st = PingMath.Compute(s);
                if (st.Avg.HasValue) Db.SaveMinute(t.Host, prev, st.Avg.Value, st.Min!.Value, st.Max!.Value, st.LossPercent);
                else Db.SaveMinute(t.Host, prev, 0, 0, 0, 100);
            }
        }
        _lastMinuteSaved = minute;
    }

    // ------------------------------------------------------------ Velocidad
    public async Task<SpeedResult?> RunSpeedTestAsync(IProgress<SpeedProgress>? progress, CancellationToken ct)
    {
        if (SpeedRunning) return null;
        SpeedRunning = true; StatusChanged?.Invoke();
        try
        {
            var r = await Speed.RunAsync(progress ?? new Progress<SpeedProgress>(), ct);
            Db.AddSpeed(r);
            if (r.DownMbps < Config.MinDownMbps)
                Raise(EventKind.LowSpeed, $"Velocidad baja: ↓{r.DownMbps:F1} Mbps (mínimo {Config.MinDownMbps:F0})", null, notify: true);
            SpeedFinished?.Invoke(r);
            return r;
        }
        finally { SpeedRunning = false; StatusChanged?.Invoke(); }
    }

    // ------------------------------------------------------------ Eventos
    void Raise(EventKind kind, string message, string? mac, bool notify = false)
    {
        var e = new NetEvent { Time = DateTime.Now, Kind = kind, Message = message, Mac = mac };
        Db.AddEvent(e);
        if (notify && Config.DesktopNotifications) Notifier.Send("Lan-Monitor", message);
        EventAdded?.Invoke(e);
    }
}
