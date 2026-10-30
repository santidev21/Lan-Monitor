using System.Text;

namespace LanMonitor.App;

/// <summary>Gráficos de consola con bloques Unicode.</summary>
public static class Charts
{
    const string Blocks = " ▁▂▃▄▅▆▇█";

    /// <summary>Reduce una serie a `width` puntos promediando por tramos (null = todo perdido).</summary>
    public static List<double?> Resample(IReadOnlyList<double?> data, int width)
    {
        if (width <= 0) return new();
        if (data.Count <= width) return data.ToList();
        var res = new List<double?>(width);
        for (int i = 0; i < width; i++)
        {
            int a = i * data.Count / width, b = Math.Max(a + 1, (i + 1) * data.Count / width);
            var vals = data.Skip(a).Take(b - a).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            res.Add(vals.Count == 0 ? null : vals.Average());
        }
        return res;
    }

    public static string Spark(IReadOnlyList<double?> data, int width, double? max = null)
    {
        var d = Resample(data, width);
        if (d.Count == 0) return "";
        double m = max ?? Math.Max(1, d.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(1).Max());
        var sb = new StringBuilder();
        foreach (var v in d)
        {
            if (v == null) { sb.Append('×'); continue; }
            int idx = (int)Math.Clamp(Math.Round(v.Value / m * 8), 1, 8);
            sb.Append(Blocks[idx]);
        }
        return sb.ToString();
    }

    /// <summary>Gráfico de columnas de varias filas. Devuelve líneas con eje Y a la izquierda y una fila de pérdidas.</summary>
    public static List<string> Columns(IReadOnlyList<double?> data, int width, int height, double? maxOverride = null, string unit = "ms")
    {
        var d = Resample(data, width);
        double max = maxOverride ?? Math.Max(10, d.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(10).Max() * 1.1);
        var lines = new List<string>();
        for (int row = 0; row < height; row++)
        {
            string label = row == 0 ? $"{max,5:F0} " : row == height / 2 ? $"{max / 2,5:F0} " : row == height - 1 ? $"{0,5} " : "      ";
            var sb = new StringBuilder(label);
            foreach (var v in d)
            {
                if (v == null) { sb.Append(' '); continue; }
                double units = v.Value / max * height;           // altura en filas
                double fill = Math.Clamp(units - (height - 1 - row), 0, 1);
                sb.Append(fill <= 0 ? ' ' : Blocks[(int)Math.Clamp(Math.Round(fill * 8), 1, 8)]);
            }
            lines.Add(sb.ToString());
        }
        if (d.Any(v => v == null))
            lines.Add("  " + unit.PadRight(4) + new string(d.Select(v => v == null ? '×' : ' ').ToArray()));
        return lines;
    }

    public static string Bar(double fraction, int width)
    {
        int filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
