using System.Text;
using LanMonitor.App.Views.Dialogs;
using LanMonitor.Core;
using LanMonitor.Core.Models;
using LanMonitor.Core.Network;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

public class DevicesView : TabPage
{
    readonly Label _head = new() { X = 1, Y = 0, Width = Dim.Fill(1) };
    readonly Grid _grid = new() { X = 0, Y = 2 };
    List<Device> _shown = new();
    string _filter = ""; bool _onlyOnline;

    public DevicesView(MonitorService m, IApplication app) : base("F4 Dispositivos", m, app)
    {
        Add(_head, _grid);
        _grid.Accepting += (_, e) => { OpenDetail(); e.Handled = true; };
    }

    Device? Selected => _grid.SelectedRow >= 0 && _grid.SelectedRow < _shown.Count ? _shown[_grid.SelectedRow] : null;

    public override bool HandleKey(char c, Key key)
    {
        var d = Selected;
        switch (c)
        {
            case 'r': _ = M.ScanNowAsync(); return true;
            case 'o': _onlyOnline = !_onlyOnline; Refresh(); return true;
            case '/':
                _filter = Prompts.Input(App, "Filtrar", "Texto (apodo, IP, MAC, fabricante). Vacío = sin filtro:", _filter) ?? _filter;
                Refresh(); return true;
            case 'k' when d != null:
                M.SetKnown(d.Mac, !d.IsKnown); return true;
            case 'n' when d != null:
                Rename(d); return true;
        }
        if (key == Key.Delete && d != null) { Forget(d); return true; }
        if (key == Key.Enter && d != null) { OpenDetail(); return true; }
        return false;
    }

    void Rename(Device d)
    {
        var nick = Prompts.Input(App, "Apodo", $"Apodo para {d.Ip} ({d.Mac}):", d.Nickname);
        if (nick != null) M.SetNickname(d.Mac, nick);
    }

    void Forget(Device d)
    {
        if (Prompts.Choose(App, "Olvidar dispositivo", $"¿Olvidar {d.DisplayName} ({d.Mac})?\nSe borra su historial.", "No", "Sí") == 1)
            M.Forget(d.Mac);
    }

    public override void Refresh()
    {
        var all = M.Db.GetDevices();
        IEnumerable<Device> q = all;
        if (_onlyOnline) q = q.Where(x => x.IsOnline);
        if (_filter != "")
            q = q.Where(x => string.Join(' ', x.Nickname, x.Ip, x.Mac, x.Vendor, x.Hostname).Contains(_filter, StringComparison.OrdinalIgnoreCase));
        // Desconocidos primero, luego online, luego por IP.
        _shown = q.OrderBy(x => x.IsKnown).ThenByDescending(x => x.IsOnline).ThenBy(x => IpKey(x.Ip)).ToList();

        int unk = all.Count(x => x.IsOnline && !x.IsKnown);
        _head.Text = $"{all.Count(x => x.IsOnline)} online / {all.Count} registrados · {unk} desconocidos online" +
                     (_filter != "" ? $" · filtro: \"{_filter}\"" : "") + (_onlyOnline ? " · solo online" : "") +
                     (M.Scanning ? " · escaneando..." : "") +
                     "\n  r escanear · k conocido · n apodo · Enter detalle · / filtrar · o solo online · Supr olvidar";

        _grid.SetData(new[] { "●", "Apodo / Nombre", "IP", "MAC", "Fabricante", "Hostname", "Estado", "Primera vez", "Última vez" },
            _shown.Select(d => new[]
            {
                d.IsOnline ? "●" : "○",
                (d.IsSelf ? "[yo] " : d.IsGateway ? "[router] " : "") + d.DisplayName,
                d.Ip, d.Mac + (d.IsRandomMac ? " ⚑" : ""), Trunc(d.Vendor, 24), Trunc(d.Hostname, 24),
                d.IsKnown ? "Conocido" : "⚠ DESCONOCIDO",
                d.FirstSeen.ToString("MM-dd HH:mm"), d.IsOnline ? "ahora" : d.LastSeen.ToString("MM-dd HH:mm")
            }).ToList());
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    static long IpKey(string ip) => System.Net.IPAddress.TryParse(ip, out var a) ? a.GetAddressBytes().Aggregate(0L, (x, b) => x * 256 + b) : 0;

    // ------------------------------------------------------------ Detalle
    void OpenDetail()
    {
        var d = Selected; if (d == null) return;
        while (true)
        {
            d = M.Db.GetDevice(d.Mac); if (d == null) return;
            double? ping = null;
            if (d.IsOnline)
                ping = Task.Run(async () =>
                {
                    try { using var p = new System.Net.NetworkInformation.Ping(); var r = await p.SendPingAsync(d.Ip, 800); return r.Status == System.Net.NetworkInformation.IPStatus.Success ? (double?)r.RoundtripTime : null; }
                    catch { return null; }
                }).GetAwaiter().GetResult();

            var sb = new StringBuilder();
            sb.AppendLine($"Nombre    : {d.DisplayName}   [{(d.IsKnown ? "Conocido" : "DESCONOCIDO")}]");
            sb.AppendLine($"Estado    : {(d.IsOnline ? "● online" : "○ offline")}   Ping: {(ping.HasValue ? ping.Value + " ms" : "-")}");
            sb.AppendLine($"IP / MAC  : {d.Ip}  /  {d.Mac}{(d.IsRandomMac ? "  (MAC privada/aleatoria: puede cambiar)" : "")}");
            sb.AppendLine($"Fabricante: {(d.Vendor == "" ? "-" : d.Vendor)}");
            sb.AppendLine($"Hostname  : {(d.Hostname == "" ? "-" : d.Hostname)}");
            sb.AppendLine($"Visto     : {d.FirstSeen:yyyy-MM-dd HH:mm} → {d.LastSeen:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Notas     : {(d.Notes == "" ? "-" : d.Notes)}");
            sb.AppendLine("\nHistorial de conexión:");
            var hist = M.Db.GetConnections(d.Mac, 8);
            foreach (var h in hist) sb.AppendLine($"  {h.Time:MM-dd HH:mm:ss}  {(h.Online ? "conectó" : "se desconectó")}");
            if (hist.Count == 0) sb.AppendLine("  (sin registros)");

            var dlg = new Dialog { Title = "Detalle del dispositivo", Width = 78 };
            dlg.Add(new Label { Text = sb.ToString(), X = 1, Y = 0 });
            foreach (var b in new[] { "Cerrar", "Notas", "Wake-on-LAN", "Puertos", d.IsKnown ? "Marcar desconocido" : "Marcar conocido", "Apodo" })
                dlg.AddButton(new Button { Text = b });
            App.Run(dlg);
            int r = dlg.Result ?? 0; dlg.Dispose();
            switch (r)
            {
                case 1: var n = Prompts.Input(App, "Notas", "Notas libres:", d.Notes); if (n != null) M.SetNotes(d.Mac, n); break;
                case 2: _ = WakeAsync(d); break;
                case 3: ScanPorts(d); break;
                case 4: M.SetKnown(d.Mac, !d.IsKnown); break;
                case 5: Rename(d); break;
                default: return;
            }
        }
    }

    async Task WakeAsync(Device d)
    {
        try { await WakeOnLan.SendAsync(d.Mac); App.Invoke(() => Prompts.Info(App, "Wake-on-LAN", $"Paquete mágico enviado a {d.Mac}.")); }
        catch (Exception ex) { App.Invoke(() => Prompts.Info(App, "Wake-on-LAN", "Error: " + ex.Message)); }
    }

    void ScanPorts(Device d)
    {
        var open = Task.Run(() => PortScanner.ScanAsync(d.Ip)).GetAwaiter().GetResult();
        Prompts.Info(App, $"Puertos abiertos en {d.Ip}",
            open.Count == 0 ? "Ningún puerto común abierto." : string.Join("\n", open.Select(p => $"{p.Port,5}  {p.Name}")));
    }
}
