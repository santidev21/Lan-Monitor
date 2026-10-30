using LanMonitor.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class MainWindow : Window
{
    readonly IApplication _app;
    readonly MonitorService _m;
    readonly Tabs _tabs = new() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1) };
    readonly Label _status = new() { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
    readonly List<TabPage> _pages;
    readonly SpeedView _speed;
    static readonly Key[] FKeys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6 };

    public MainWindow(IApplication app, MonitorService m)
    {
        _app = app; _m = m;
        Title = "Lan-Monitor";
        _speed = new SpeedView(m, app);
        _pages = new() { new DashboardView(m, app), new PingView(m, app), _speed, new DevicesView(m, app), new EventsView(m, app), new SettingsView(m, app) };
        foreach (var p in _pages) _tabs.Add(p);
        Add(_tabs, _status);

        app.Keyboard.KeyDown += OnGlobalKey;
        _tabs.ValueChanged += (_, _) => RefreshAll();
        app.AddTimeout(TimeSpan.FromSeconds(1), () => { RefreshAll(); return true; });
    }

    TabPage? Current => _tabs.Value as TabPage;

    void OnGlobalKey(object? sender, Key key)
    {
        // Solo cuando no hay un diálogo modal encima de la ventana principal.
        if (!ReferenceEquals(_app.TopRunnableView, this)) return;

        for (int i = 0; i < _pages.Count; i++)
            if (key == FKeys[i]) { _tabs.Value = _pages[i]; key.Handled = true; return; }

        char c = char.ToLowerInvariant((char)key.AsRune.Value);
        if (Current?.HandleKey(c, key) == true) { key.Handled = true; RefreshAll(); return; }

        switch (c)
        {
            case 'q': _app.RequestStop(); key.Handled = true; break;
            case 'r': _ = _m.ScanNowAsync(); key.Handled = true; break;
            case 's': _tabs.Value = _speed; _speed.Start(); key.Handled = true; break;
        }
    }

    public void RefreshAll()
    {
        try { Current?.Refresh(); } catch { }
        var n = _m.Net;
        var devs = _m.Db.GetDevices();
        int unk = devs.Count(d => d.IsOnline && !d.IsKnown);
        var gw = _m.Ping.Targets.Count > 1 ? _m.Ping.Targets[1] : null;
        double? lat = null;
        if (gw != null) lock (gw.Samples) lat = gw.Samples.LastOrDefault()?.RttMs;
        _status.Text = $" Lan-Monitor │ {(n == null ? "sin red" : $"{n.InterfaceName} {n.LocalIp}")} │ Internet {(_m.InternetUp ? "●" : "✖ CAÍDO")} {(lat.HasValue ? lat.Value + "ms" : "")}" +
                       $" │ {devs.Count(d => d.IsOnline)} dispositivos ({unk} desconocidos) │ F1-F6 · r escanear · s speedtest · q salir";
        SetNeedsDraw();
    }
}
