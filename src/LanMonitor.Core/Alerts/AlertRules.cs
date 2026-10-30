using LanMonitor.Core.Models;

namespace LanMonitor.Core.Alerts;

/// <summary>Reglas puras (sin hilos ni BD) que deciden cuándo disparar cada alerta.</summary>
public static class AlertRules
{
    public const int DownSamples = 4;
    public const int LatencyWindow = 10;
    public const int LatencyMinSamples = 8;

    /// <summary>Caído = los últimos 4 pings de TODOS los objetivos externos fallaron.</summary>
    public static bool IsInternetDown(IReadOnlyList<IReadOnlyList<PingSample>> external) =>
        external.Count > 0 && external.All(s => s.Count >= DownSamples && s.TakeLast(DownSamples).All(x => x.RttMs == null));

    /// <summary>Restaurado = el último ping de al menos un objetivo externo respondió.</summary>
    public static bool IsInternetBack(IReadOnlyList<IReadOnlyList<PingSample>> external) =>
        external.Any(s => s.Count > 0 && s[^1].RttMs != null);

    /// <summary>Transición de estado de internet. Devuelve el evento a emitir o null si no cambia nada.</summary>
    public static EventKind? InternetTransition(bool currentlyUp, IReadOnlyList<IReadOnlyList<PingSample>> external)
    {
        if (external.Count == 0) return null;
        if (currentlyUp && IsInternetDown(external)) return EventKind.InternetDown;
        if (!currentlyUp && IsInternetBack(external)) return EventKind.InternetRestored;
        return null;
    }

    /// <summary>Latencia alta = promedio de las últimas 10 muestras > umbral (con al menos 8 válidas).</summary>
    public static bool IsHighLatency(IReadOnlyList<PingSample> samples, double thresholdMs, out double avg)
    {
        var last = samples.TakeLast(LatencyWindow).Where(s => s.RttMs != null).Select(s => s.RttMs!.Value).ToList();
        avg = last.Count > 0 ? last.Average() : 0;
        return last.Count >= LatencyMinSamples && avg > thresholdMs;
    }

    /// <summary>¿Falla el router (LAN/WiFi) o solo internet (proveedor)?</summary>
    public static string Diagnose(IReadOnlyList<PingSample>? gateway)
    {
        if (gateway == null) return "sin datos";
        bool gwOk = gateway.TakeLast(3).Any(s => s.RttMs != null);
        return gwOk ? "el router responde pero no hay salida: problema del proveedor (ISP)"
                    : "no responde el router: problema en tu WiFi/LAN";
    }
}
