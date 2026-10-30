using LanMonitor.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace LanMonitor.App.Views;

public abstract class TabPage : View
{
    protected MonitorService M { get; }
    protected IApplication App { get; }

    protected TabPage(string title, MonitorService m, IApplication app)
    {
        Title = title; M = m; App = app;
        Width = Dim.Fill(); Height = Dim.Fill(); CanFocus = true;
    }

    public abstract void Refresh();

    /// <summary>Atajo de teclado propio de la pestaña. Devuelve true si lo consumió.</summary>
    public virtual bool HandleKey(char c, Key key) => false;

    protected static string Ago(DateTime t)
    {
        var s = DateTime.Now - t;
        if (s.TotalSeconds < 60) return "hace segundos";
        if (s.TotalMinutes < 60) return $"hace {(int)s.TotalMinutes} min";
        if (s.TotalHours < 24) return $"hace {(int)s.TotalHours} h";
        return $"hace {(int)s.TotalDays} d";
    }
}
