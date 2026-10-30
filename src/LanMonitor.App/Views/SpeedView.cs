using LanMonitor.Core;
using LanMonitor.Core.Network;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class SpeedView : TabPage
{
    readonly Label _live = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = 4 };
    readonly Label _chart = new() { X = 1, Y = 5, Width = Dim.Fill(1), Height = 8 };
    readonly Grid _grid = new() { X = 0, Y = 14 };
    string _phase = ""; double _mbps; double _frac; string _error = "";
    CancellationTokenSource? _cts;

    public SpeedView(MonitorService m, IApplication app) : base("F3 Velocidad", m, app) => Add(_live, _chart, _grid);

    public override bool HandleKey(char c, Key key)
    {
        if (c == 's') { Start(); return true; }
        if (c == 'x' && _cts != null) { _cts.Cancel(); return true; }
        return false;
    }

    public void Start()
    {
        if (M.SpeedRunning) return;
        _error = ""; _phase = "Iniciando"; _mbps = 0; _frac = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<SpeedProgress>(p => App.Invoke(() => { _phase = p.Phase; _mbps = p.CurrentMbps; _frac = p.Fraction; Refresh(); }));
        _ = Task.Run(async () =>
        {
            try { await M.RunSpeedTestAsync(progress, _cts.Token); }
            catch (OperationCanceledException) { _error = "Test cancelado"; }
            catch (Exception ex) { _error = "Error: " + ex.Message; }
            finally { _cts = null; App.Invoke(Refresh); }
        });
    }

    public override void Refresh()
    {
        if (M.SpeedRunning)
            _live.Text = $"Midiendo... fase: {_phase}\n{Charts.Bar(_frac, 40)}  {_mbps,7:F1} Mbps\n(x = cancelar)";
        else
        {
            var last = M.Db.GetSpeeds(1).FirstOrDefault();
            _live.Text = (last == null ? "Sin tests todavía." : $"Último: ↓ {last.DownMbps:F1} Mbps   ↑ {last.UpMbps:F1} Mbps   ping {last.PingMs:F0} ms  ({Ago(last.Time)})")
                + "\nPresiona  s  para iniciar un test de velocidad (Cloudflare)." + (_error != "" ? "\n" + _error : "");
        }

        var hist = M.Db.GetSpeeds(30);
        int w = Math.Max(20, (Frame.Width > 0 ? Frame.Width : 100) - 12);
        var down = hist.AsEnumerable().Reverse().Select(h => (double?)h.DownMbps).ToList();
        _chart.Text = "Descarga (Mbps) — últimos tests\n" + string.Join("\n", Charts.Columns(down, Math.Min(w, Math.Max(down.Count * 2, 10)), 6, null, "Mbps"));
        _grid.SetData(new[] { "Fecha", "↓ Mbps", "↑ Mbps", "Ping ms" },
            hist.Select(h => new[] { h.Time.ToString("yyyy-MM-dd HH:mm"), h.DownMbps.ToString("F1"), h.UpMbps.ToString("F1"), h.PingMs.ToString("F0") }).ToList());
    }
}
