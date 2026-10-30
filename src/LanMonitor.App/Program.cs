using LanMonitor.App.Views;
using LanMonitor.Core;
using LanMonitor.Core.Config;
using LanMonitor.Core.Storage;
using Terminal.Gui.App;

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
