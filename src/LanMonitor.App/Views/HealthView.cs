using LanMonitor.Core;
using LanMonitor.Core.Health;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class HealthView : TabPage
{
    readonly Label _level = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = 1 };
    readonly Label _body = new() { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill() };

    public HealthView(MonitorService m, IApplication app) : base("F7 Salud", m, app) => Add(_level, _body);

    public override void Refresh()
    {
        var h = M.Health;
        _level.Text = h.Label;
        _level.SchemeName = h.Level == HealthLevel.Restart ? "Error" : "Base";

        var lines = new List<string> { h.Headline, "" };
        if (h.Reasons.Count > 0)
        {
            lines.Add("Por qué:");
            foreach (var r in h.Reasons) lines.AddRange(Txt.Wrap("• " + r, W, "  "));
            lines.Add("");
        }
        if (h.Tips.Count > 0)
        {
            lines.Add("Qué hacer:");
            foreach (var t in h.Tips) lines.AddRange(Txt.Wrap("→ " + t, W, "  "));
            lines.Add("");
        }
        lines.Add("Datos:");
        foreach (var i in h.Info) lines.AddRange(Txt.Wrap("· " + i, W, "  "));
        lines.Add("");
        lines.AddRange(Txt.Wrap("Se evalúa cada 30 s con los pings, cortes y tests de velocidad guardados. Reiniciar el módem solo se sugiere cuando el router responde pero internet falla o se degrada; si el problema está en tu WiFi, te lo dice aparte.", W));
        _body.Text = string.Join("\n", lines);
    }
}
