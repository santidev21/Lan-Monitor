using LanMonitor.Core;
using LanMonitor.Core.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class EventsView : TabPage
{
    static readonly string[] Filters = { "Todas", "Dispositivos", "Internet / latencia", "Velocidad" };
    int _f;
    readonly Label _head = new() { X = 1, Y = 0, Width = Dim.Fill(1) };
    readonly Grid _grid = new() { X = 0, Y = 1 };

    public EventsView(MonitorService m, IApplication app) : base("F5 Alertas", m, app) => Add(_head, _grid);

    public override bool HandleKey(char c, Key key)
    {
        if (c == 'f') { _f = (_f + 1) % Filters.Length; Refresh(); return true; }
        return false;
    }

    bool Match(NetEvent e) => _f switch
    {
        1 => e.Kind is EventKind.NewUnknownDevice or EventKind.DeviceConnected or EventKind.DeviceDisconnected,
        2 => e.Kind is EventKind.InternetDown or EventKind.InternetRestored or EventKind.HighLatency,
        3 => e.Kind == EventKind.LowSpeed,
        _ => true
    };

    public override void Refresh()
    {
        _head.Text = $"Filtro: {Filters[_f]}  (f = cambiar)";
        var ev = M.Db.GetEvents(300).Where(Match).ToList();
        _grid.SetData(new[] { "Hora", "Tipo", "Mensaje" },
            ev.Select(e => new[] { e.Time.ToString("MM-dd HH:mm:ss"), e.Icon, e.Message }).ToList());
    }
}
