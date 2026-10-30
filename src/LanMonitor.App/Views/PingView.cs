using LanMonitor.Core;
using LanMonitor.Core.Network;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class PingView : TabPage
{
    static readonly (TimeSpan Span, string Name)[] Windows = { (TimeSpan.FromMinutes(1), "1 min"), (TimeSpan.FromMinutes(5), "5 min"), (TimeSpan.FromMinutes(15), "15 min") };
    int _win = 1;
    readonly Grid _grid = new() { X = 0, Y = 1, Height = 6 };
    readonly Label _head = new() { X = 1, Y = 0, Width = Dim.Fill(1) };
    readonly Label _charts = new() { X = 1, Y = 7, Width = Dim.Fill(1), Height = Dim.Fill() };

    public PingView(MonitorService m, IApplication app) : base("F2 Ping", m, app) => Add(_head, _grid, _charts);

    public override bool HandleKey(char c, Key key)
    {
        if (c == 'w') { _win = (_win + 1) % Windows.Length; Refresh(); return true; }
        return false;
    }

    public override void Refresh()
    {
        var (span, name) = Windows[_win];
        _head.Text = $"Ventana: {name}  (w = cambiar)   Diagnóstico: {(M.InternetUp ? "todo bien" : M.Diagnose())}";
        PingTarget[] ts; lock (M.Ping.Targets) ts = M.Ping.Targets.ToArray();

        var rows = new List<string[]>(); var sb = new System.Text.StringBuilder();
        int width = Math.Max(20, (Frame.Width > 0 ? Frame.Width : 100) - 10);
        foreach (var t in ts)
        {
            var w = PingMonitor.Window(t, span);
            var st = PingMath.Compute(w);
            string F(double? v) => v.HasValue ? $"{v.Value:F0} ms" : "-";
            rows.Add(new[] { t.Label == t.Host ? t.Host : $"{t.Label} ({t.Host})", F(st.Current), F(st.Min), F(st.Avg), F(st.Max), $"{st.Jitter:F1} ms", $"{st.LossPercent:F1}%" });
            sb.AppendLine(t.Label == t.Host ? t.Host : $"{t.Label} — {t.Host}");
            foreach (var line in Charts.Columns(w.Select(s => s.RttMs).ToList(), width, 3, null)) sb.AppendLine(line);
        }
        int gridH = Math.Min(ts.Length + 3, 8);
        _grid.Height = gridH;
        _charts.Y = 1 + gridH;
        _grid.SetData(new[] { "Objetivo", "Actual", "Mín", "Prom", "Máx", "Jitter", "Pérdida" }, rows);
        _charts.Text = sb.ToString();
    }
}
