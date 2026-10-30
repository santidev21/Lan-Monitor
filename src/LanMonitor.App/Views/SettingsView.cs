using System.Text;
using LanMonitor.App.Views.Dialogs;
using LanMonitor.Core;
using LanMonitor.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class SettingsView : TabPage
{
    readonly Label _head = new() { X = 1, Y = 0, Width = Dim.Fill(1), Height = 3 };
    readonly Grid _grid = new() { X = 0, Y = 3 };
    record Setting(string Name, Func<string> Get, Action<string> Set, bool Toggle = false);
    readonly List<Setting> _items;
    string _msg = "";

    public SettingsView(MonitorService m, IApplication app) : base("F6 Ajustes", m, app)
    {
        var c = m.Config;
        _items = new()
        {
            new("Intervalo de escaneo (s)", () => c.ScanIntervalSeconds.ToString(), v => c.ScanIntervalSeconds = int.Parse(v)),
            new("Objetivos de ping extra (coma)", () => string.Join(",", c.ExtraPingTargets), v => { c.ExtraPingTargets = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); M.RefreshNetwork(); }),
            new("Umbral latencia alta (ms)", () => c.HighLatencyMs.ToString("F0"), v => c.HighLatencyMs = double.Parse(v)),
            new("Velocidad mínima ↓ (Mbps)", () => c.MinDownMbps.ToString("F0"), v => c.MinDownMbps = double.Parse(v)),
            new("Test de velocidad automático", () => c.AutoSpeedTest ? "Sí" : "No", _ => c.AutoSpeedTest = !c.AutoSpeedTest, true),
            new("Cada cuántas horas", () => c.AutoSpeedTestHours.ToString(), v => c.AutoSpeedTestHours = int.Parse(v)),
            new("Notificaciones de escritorio", () => c.DesktopNotifications ? "Sí" : "No", _ => c.DesktopNotifications = !c.DesktopNotifications, true),
            new("Retención de datos (días)", () => c.RetentionDays.ToString(), v => c.RetentionDays = int.Parse(v)),
        };
        Add(_head, _grid);
        _grid.Accepting += (_, e) => { Edit(); e.Handled = true; };
    }

    public override bool HandleKey(char c, Key key)
    {
        if (key == Key.Enter) { Edit(); return true; }
        if (c == 'e') { Export(); return true; }
        return false;
    }

    void Edit()
    {
        int i = _grid.SelectedRow; if (i < 0 || i >= _items.Count) return;
        var s = _items[i];
        try
        {
            if (s.Toggle) s.Set("");
            else { var v = Prompts.Input(App, s.Name, "Nuevo valor:", s.Get()); if (v == null) return; s.Set(v); }
            M.Config.Save(); _msg = "Guardado.";
        }
        catch (Exception ex) { _msg = "Valor inválido: " + ex.Message; }
        Refresh();
    }

    void Export()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lan-monitor-export");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            File.WriteAllLines(Path.Combine(dir, $"dispositivos-{stamp}.csv"),
                new[] { "mac,ip,apodo,hostname,fabricante,conocido,online,primera_vez,ultima_vez,notas" }.Concat(
                M.Db.GetDevices().Select(d => string.Join(",", Q(d.Mac), Q(d.Ip), Q(d.Nickname), Q(d.Hostname), Q(d.Vendor), d.IsKnown, d.IsOnline, d.FirstSeen.ToString("s"), d.LastSeen.ToString("s"), Q(d.Notes)))));
            File.WriteAllLines(Path.Combine(dir, $"velocidad-{stamp}.csv"),
                new[] { "fecha,down_mbps,up_mbps,ping_ms" }.Concat(M.Db.GetSpeeds(1000).Select(s => $"{s.Time:s},{s.DownMbps:F2},{s.UpMbps:F2},{s.PingMs:F1}")));
            File.WriteAllLines(Path.Combine(dir, $"alertas-{stamp}.csv"),
                new[] { "fecha,tipo,mensaje" }.Concat(M.Db.GetEvents(5000).Select(e => $"{e.Time:s},{e.Kind},{Q(e.Message)}")));
            _msg = $"Exportado a {dir}";
        }
        catch (Exception ex) { _msg = "Error al exportar: " + ex.Message; }
        Refresh();
    }

    public override void Refresh()
    {
        _head.Text = $"Enter = editar · e = exportar CSV\nConfig: {AppConfig.ConfigPath}\nDatos : {AppConfig.DbPath}   {_msg}";
        _grid.SetData(new[] { "Ajuste", "Valor" }, _items.Select(s => new[] { s.Name, s.Get() }).ToList());
    }
}
