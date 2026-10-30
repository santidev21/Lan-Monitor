using LanMonitor.Core;
using LanMonitor.Core.Network;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class DashboardView : TabPage
{
    readonly Label _info = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = 3 };
    readonly Label _latency = new() { X = 1, Y = 3, Width = Dim.Fill(1), Height = 4 };
    readonly Label _devices = new() { X = 1, Y = 7, Width = Dim.Fill(1), Height = 1 };
    readonly Label _unknown = new() { X = 1, Y = 8, Width = Dim.Fill(1), Height = 1, SchemeName = "Error" };
    readonly Label _speed = new() { X = 1, Y = 9, Width = Dim.Fill(1), Height = 1 };
    readonly Label _health = new() { X = 1, Y = 10, Width = Dim.Fill(1), Height = 1 };
    readonly Label _events = new() { X = 1, Y = 12, Width = Dim.Fill(1), Height = Dim.Fill() };

    public DashboardView(MonitorService m, IApplication app) : base("F1 Dashboard", m, app)
        => Add(_info, _latency, _devices, _unknown, _speed, _health, _events);

    public override void Refresh()
    {
        var n = M.Net;
        string pub = string.IsNullOrEmpty(M.PublicIp) ? "..." : M.PublicIp;
        string net = n == null ? "Sin red detectada" :
            $"{n.InterfaceName}{(string.IsNullOrEmpty(n.Ssid) ? "" : " · " + n.Ssid)} · IP {n.Cidr}\n" +
            $"Gateway {n.Gateway} · IP pública {pub}";
        _info.Text = Txt.FitLines($"Internet : {(M.InternetUp ? "● ONLINE" : "✖ CAÍDO — " + M.Diagnose())}\n{net}", W);

        var sb = new System.Text.StringBuilder("LATENCIA (últimos 5 min)\n");
        PingTarget[] ts; lock (M.Ping.Targets) ts = M.Ping.Targets.ToArray();
        foreach (var t in ts.Take(3))
        {
            var w = PingMonitor.Window(t, TimeSpan.FromMinutes(5));
            var st = PingMath.Compute(w);
            var spark = Charts.Spark(w.Select(s => s.RttMs).ToList(), Math.Clamp(W - 38, 8, 70), 100);
            sb.AppendLine($"{t.Label,-10} {(st.Current.HasValue ? st.Current.Value.ToString("F0") + " ms" : "perdido"),9}  {spark}  pérd {st.LossPercent:F0}%");
        }
        _latency.Text = Txt.FitLines(sb.ToString(), W);

        var devs = M.Db.GetDevices();
        int online = devs.Count(d => d.IsOnline);
        int unk = devs.Count(d => d.IsOnline && !d.IsKnown);
        string scan = M.Scanning ? "  (escaneando...)" : M.LastScan.HasValue ? $"  (escaneo {Ago(M.LastScan.Value)})" : "";
        string dev = $"DISPOSITIVOS: {online} online · {devs.Count(d => d.IsOnline && d.IsKnown)} conocidos · {devs.Count} total";
        _devices.Text = Txt.Trunc(dev.Length + scan.Length <= W ? dev + scan : dev, W);
        _unknown.Text = unk > 0 ? Txt.Trunc($"⚠ {unk} DESCONOCIDO(S) en línea — F4 para revisarlos", W) : "";
        _unknown.Visible = unk > 0;

        var last = M.Db.GetSpeeds(1).FirstOrDefault();
        _speed.Text = Txt.FitLines(last == null ? "VELOCIDAD: sin tests aún (F3 → s para medir)"
            : $"ÚLTIMO TEST ({Ago(last.Time)}): ↓ {last.DownMbps:F1} Mbps   ↑ {last.UpMbps:F1} Mbps   ping {last.PingMs:F0} ms", W);

        var hr = M.Health;
        _health.Text = Txt.Trunc("MÓDEM: " + hr.Label + (hr.Level >= LanMonitor.Core.Health.HealthLevel.Watch && hr.Reasons.Count > 0 ? " — F7 para ver por qué" : ""), W);
        _health.SchemeName = hr.Level == LanMonitor.Core.Health.HealthLevel.Restart ? "Error" : "Base";

        int evRows = Math.Clamp(H - 8 - 12 - 1, 1, 8);
        var ev = M.Db.GetEvents(evRows);
        _events.Text = Txt.FitLines("ÚLTIMAS ALERTAS\n" + (ev.Count == 0 ? "  (ninguna)" :
            string.Join("\n", ev.Select(e => $"  {e.Time:HH:mm:ss} {e.Icon} {e.Message}"))), W);
    }
}
