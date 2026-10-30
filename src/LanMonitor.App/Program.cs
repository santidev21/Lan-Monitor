using LanMonitor.App.Views;
using LanMonitor.Core;
using LanMonitor.Core.Config;
using LanMonitor.Core.Storage;
using Terminal.Gui.App;

var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";
if (args.Contains("--version") || args.Contains("-v")) { Console.WriteLine($"lan-monitor {version}"); return 0; }
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine($"""
        lan-monitor {version} — monitor de red para terminal

        Uso: lan-monitor [--help | --version]

        Teclas: F1–F7 pestañas · r escanear · s test de velocidad · q salir
        Config: {AppConfig.ConfigPath}
        Datos:  {AppConfig.DbPath}
        """);
    return 0;
}
if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("lan-monitor solo funciona en Linux (lee la tabla ARP del kernel en /proc/net/arp).");
    return 1;
}

var config = AppConfig.Load();
using var db = new Database(AppConfig.DbPath);
db.Purge(config.RetentionDays);
var monitor = new MonitorService(config, db);
using var cts = new CancellationTokenSource();

using IApplication app = Application.Create().Init();
var main = new MainWindow(app, monitor);

monitor.DevicesUpdated += () => app.Invoke(main.RefreshAll);
monitor.StatusChanged += () => app.Invoke(main.RefreshAll);
monitor.EventAdded += _ => app.Invoke(main.RefreshAll);

await monitor.StartAsync(cts.Token);
main.RefreshAll();
app.Run(main);

cts.Cancel();
main.Dispose();
return 0;
