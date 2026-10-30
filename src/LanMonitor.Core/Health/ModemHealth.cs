using LanMonitor.Core.Models;
using LanMonitor.Core.Network;

namespace LanMonitor.Core.Health;

public enum HealthLevel { Unknown, Good, Watch, Restart }

public record HealthInput(
    DateTime Now,
    bool InternetUp,
    DateTime? DownSince,
    IReadOnlyList<PingSample> Gateway,                       // muestras en vivo al router
    IReadOnlyList<IReadOnlyList<PingSample>> External,       // muestras en vivo a objetivos de internet
    IReadOnlyList<NetEvent> Events,                          // eventos recientes
    IReadOnlyList<SpeedResult> Speeds,                       // más reciente primero
    double MinDownMbps,
    IReadOnlyList<(DateTime Minute, double Avg, double Loss)> ExternalHistory);  // promedios por minuto (24 h)

public record HealthReport(HealthLevel Level, string Headline, IReadOnlyList<string> Reasons, IReadOnlyList<string> Tips, IReadOnlyList<string> Info)
{
    public string Label => Level switch
    {
        HealthLevel.Good => "● TODO BIEN",
        HealthLevel.Watch => "▲ VIGILAR",
        HealthLevel.Restart => "✖ CONVIENE REINICIAR EL MÓDEM",
        _ => "… RECOPILANDO DATOS"
    };
}

/// <summary>
/// Heurísticas para decidir si conviene reiniciar el módem. Reiniciar ayuda cuando el módem/ISP
/// se degrada pero tu WiFi hacia el router va bien; si falla ya el tramo WiFi→router, el consejo es otro.
/// </summary>
public static class ModemHealth
{
    public const int MinLiveSamples = 60;                 // ~1 min de datos para opinar sobre degradación
    public static readonly TimeSpan OutageGrace = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DropsWindow = TimeSpan.FromHours(3);
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LatencyWindow = TimeSpan.FromMinutes(30);

    public static HealthReport Analyze(HealthInput i)
    {
        var level = HealthLevel.Good;
        var reasons = new List<string>();
        var info = new List<string>();
        var tips = new List<string>();
        void Flag(HealthLevel l, string reason) { reasons.Add(reason); if (l > level) level = l; }

        bool gwAlive = i.Gateway.TakeLast(3).Any(s => s.RttMs != null);
        var from = i.Now - LiveWindow;
        var gw = i.Gateway.Where(s => s.Time >= from).ToList();
        var ext = i.External.SelectMany(t => t.Where(s => s.Time >= from)).ToList();
        bool enoughLive = ext.Count >= MinLiveSamples;
        bool anyData = enoughLive || i.Events.Count > 0 || i.Speeds.Count > 0 || !i.InternetUp;

        // 1) Corte en curso
        if (!i.InternetUp && i.DownSince is { } since)
        {
            var dur = i.Now - since;
            if (!gwAlive)
                Flag(HealthLevel.Watch, "No responde ni el router: revisa que esté encendido y conectado (probable problema local, no del módem).");
            else if (dur >= OutageGrace)
                Flag(HealthLevel.Restart, $"Sin internet hace {Fmt(dur)} pero el router responde: el módem/proveedor no está pasando tráfico.");
            else
                Flag(HealthLevel.Watch, $"Corte de internet hace {Fmt(dur)}. Si sigue pasados 2 min, conviene reiniciar.");
        }

        // 2) Cortes repetidos
        int drops = i.Events.Count(e => e.Kind == EventKind.InternetDown && i.Now - e.Time <= DropsWindow);
        if (drops >= 3) Flag(HealthLevel.Restart, $"{drops} cortes de internet en las últimas 3 h.");
        else if (drops == 2) Flag(HealthLevel.Watch, "2 cortes de internet en las últimas 3 h.");

        // 3) Degradación sostenida (últimos 15 min)
        if (enoughLive && i.InternetUp)
        {
            var extStats = PingMath.Compute(ext);
            var gwStats = PingMath.Compute(gw);
            bool wifiBad = gw.Count >= MinLiveSamples / 2 && (gwStats.LossPercent >= 3 || gwStats.Jitter >= 30);

            if (wifiBad)
                Flag(HealthLevel.Watch, $"Tu WiFi hacia el router está inestable (pérdida {gwStats.LossPercent:F0}%, jitter {gwStats.Jitter:F0} ms). Reiniciar el módem no lo arregla: acércate al router o cambia el canal WiFi.");
            else
            {
                if (extStats.LossPercent >= 5)
                    Flag(HealthLevel.Restart, $"Pérdida de paquetes a internet de {extStats.LossPercent:F0}% en 15 min con el router sano.");
                else if (extStats.LossPercent >= 2)
                    Flag(HealthLevel.Watch, $"Pérdida de paquetes a internet de {extStats.LossPercent:F1}% en 15 min.");

                if (extStats.Jitter >= 100) Flag(HealthLevel.Restart, $"Jitter muy alto hacia internet ({extStats.Jitter:F0} ms) con el router sano.");
                else if (extStats.Jitter >= 50) Flag(HealthLevel.Watch, $"Jitter alto hacia internet ({extStats.Jitter:F0} ms).");
            }
        }

        // 4) Latencia que se arrastra respecto a lo normal (línea base de 24 h)
        var recent = i.Now - LatencyWindow;
        var nowAvg = i.External.SelectMany(t => t.Where(s => s.Time >= recent && s.RttMs != null)).Select(s => s.RttMs!.Value).DefaultIfEmpty(-1).Average();
        var baseline = i.ExternalHistory.Where(h => h.Minute < recent && h.Loss < 100).Select(h => h.Avg).OrderBy(x => x).ToList();
        if (i.InternetUp && nowAvg > 0 && baseline.Count >= 60)
        {
            double median = baseline[baseline.Count / 2];
            if (nowAvg >= 3 * median && nowAvg - median >= 80) Flag(HealthLevel.Restart, $"Latencia a internet {nowAvg:F0} ms, unas {nowAvg / median:F1}× su valor normal ({median:F0} ms).");
            else if (nowAvg >= 2 * median && nowAvg - median >= 40) Flag(HealthLevel.Watch, $"Latencia a internet {nowAvg:F0} ms, el doble de lo normal ({median:F0} ms).");
        }

        // 5) Velocidad
        if (i.Speeds.Count > 0)
        {
            var latest = i.Speeds[0];
            if (latest.DownMbps < i.MinDownMbps) Flag(HealthLevel.Watch, $"Último test de velocidad: {latest.DownMbps:F1} Mbps, por debajo de tu mínimo ({i.MinDownMbps:F0}).");
            var older = i.Speeds.Skip(2).Select(s => s.DownMbps).OrderBy(x => x).ToList();
            if (i.Speeds.Count >= 5 && older.Count >= 3)
            {
                double med = older[older.Count / 2];
                bool l1 = i.Speeds[0].DownMbps < med * 0.5, l2 = i.Speeds[1].DownMbps < med * 0.5;
                if (l1 && l2) Flag(HealthLevel.Restart, $"Los últimos 2 tests de velocidad rindieron menos de la mitad de lo habitual (~{med:F0} Mbps).");
                else if (l1) Flag(HealthLevel.Watch, $"El último test de velocidad rindió menos de la mitad de lo habitual (~{med:F0} Mbps).");
            }
        }

        // Info
        var lastDrop = i.Events.Where(e => e.Kind == EventKind.InternetDown).OrderByDescending(e => e.Time).FirstOrDefault();
        info.Add(lastDrop == null ? "Sin cortes de internet registrados." : $"Último corte de internet: hace {Fmt(i.Now - lastDrop.Time)}.");
        if (i.Speeds.Count > 0) info.Add($"Último test: ↓{i.Speeds[0].DownMbps:F1} / ↑{i.Speeds[0].UpMbps:F1} Mbps (hace {Fmt(i.Now - i.Speeds[0].Time)}).");
        if (!enoughLive) info.Add("Aún hay pocos datos de ping para evaluar degradación (espera ~1 min).");

        if (level == HealthLevel.Good && !anyData) level = HealthLevel.Unknown;
        if (level == HealthLevel.Restart)
        {
            tips.Add("Desconecta el módem de la corriente 30 segundos, vuelve a conectarlo y espera 2–3 min a que sincronice.");
            tips.Add("Si el problema reaparece en menos de 24 h, contacta a tu proveedor con las razones de arriba.");
        }
        else if (level == HealthLevel.Watch)
            tips.Add("Aún no hace falta reiniciar. Si persiste o empeora, la app te lo recomendará.");

        string headline = level switch
        {
            HealthLevel.Restart => "Conviene reiniciar el módem",
            HealthLevel.Watch => "Hay señales a vigilar",
            HealthLevel.Good => "Tu conexión se ve sana",
            _ => "Recopilando datos"
        };
        return new HealthReport(level, headline, reasons, tips, info);
    }

    static string Fmt(TimeSpan t) =>
        t.TotalMinutes < 1 ? $"{(int)t.TotalSeconds} s" :
        t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min" :
        t.TotalDays < 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{(int)t.TotalDays} d";
}
