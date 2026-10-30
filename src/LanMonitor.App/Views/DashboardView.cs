using LanMonitor.Core;
using LanMonitor.Core.Network;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class DashboardView : TabPage
{
    readonly Label _info = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = 6 };
    readonly Label _latency = new() { X = 1, Y = 7, Width = Dim.Fill(1), Height = 5 };
    readonly Label _devices = new() { X = 1, Y = 13, Width = Dim.Fill(1), Height = 1 };
    readonly Label _unknown = new() { X = 1, Y = 14, Width = Dim.Fill(1), Height = 1, SchemeName = "Error" };
    readonly Label _speed = new() { X = 1, Y = 16, Width = Dim.Fill(1), Height = 2 };
    readonly Label _events = new() { X = 1, Y = 19, Width = Dim.Fill(1), Height = Dim.Fill() };

    public DashboardView(MonitorService m, IApplication app) : base("F1 Dashboard", m, app)
        => Add(_info, _latency, _devices, _unknown, _speed, _events);

    public override void Refresh()
    {
        var n = M.Net;
        string net = n == null ? "Sin red detectada" :
            $"Interfaz : {n.InterfaceName}   SSID: {(string.IsNullOrEmpty(n.Ssid) ? "-" : n.Ssid)}\n" +
            $"IP local : {n.LocalIp}  ({n.Cidr})\n" +
            $"Gateway  : {n.Gateway}\n" +
            $"IP pública: {(string.IsNullOrEmpty(M.PublicIp) ? "..." : M.PublicIp)}";
        _info.Text = $"Internet : {(M.InternetUp ? "● ONLINE" : "✖ CAÍDO  — " + M.Diagnose())}\n{net}";

        var sb = new System.Text.StringBuilder("LATENCIA (últimos 5 min)\n");
        PingTarget[] ts; lock (M.Ping.Targets) ts = M.Ping.Targets.ToArray();
        foreach (var t in ts.Take(3))
        {
            var w = PingMonitor.Window(t, TimeSpan.FromMinutes(5));
            var st = PingMath.Compute(w);
            var spark = Charts.Spark(w.Select(s => s.RttMs).ToList(), 50, 100);
            sb.AppendLine($"{t.Label,-10} {(st.Current.HasValue ? st.Current.Value.ToString("F0") + " ms" : "perdido"),9}  {spark}  pérd {st.LossPercent:F0}%");
        }
        _latency.Text = sb.ToString();

        var devs = M.Db.GetDevices();
        int online = devs.Count(d => d.IsOnline);
        int unk = devs.Count(d => d.IsOnline && !d.IsKnown);
        _devices.Text = $"DISPOSITIVOS: {online} online · {devs.Count(d => d.IsOnline && d.IsKnown)} conocidos · {devs.Count} registrados" +
                        (M.Scanning ? "  (escaneando...)" : M.LastScan.HasValue ? $"  (último escaneo {Ago(M.LastScan.Value)})" : "");
        _unknown.Text = unk > 0 ? $"⚠ {unk} DESCONOCIDO(S) en línea — ve a F4 para revisarlos" : "";
        _unknown.Visible = unk > 0;

        var last = M.Db.GetSpeeds(1).FirstOrDefault();
        _speed.Text = last == null ? "VELOCIDAD: sin tests aún (F3 → s para medir)"
            : $"ÚLTIMO TEST ({Ago(last.Time)}): ↓ {last.DownMbps:F1} Mbps   ↑ {last.UpMbps:F1} Mbps   ping {last.PingMs:F0} ms";

        var ev = M.Db.GetEvents(5);
        _events.Text = "ÚLTIMAS ALERTAS\n" + (ev.Count == 0 ? "  (ninguna)" :
            string.Join("\n", ev.Select(e => $"  {e.Time:HH:mm:ss} {e.Icon} {e.Message}")));
    }
}
