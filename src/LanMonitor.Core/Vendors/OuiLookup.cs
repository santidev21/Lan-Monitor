using LanMonitor.Core.Config;

namespace LanMonitor.Core.Vendors;

/// <summary>MAC → fabricante usando el CSV oficial de la IEEE (descargado una vez y cacheado).</summary>
public class OuiLookup
{
    const string Url = "https://standards-oui.ieee.org/oui/oui.csv";
    readonly Dictionary<string, string> _map = new();
    public bool Loaded => _map.Count > 0;

    public static string CachePath => Path.Combine(AppConfig.DataDir, "oui.csv");

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                Directory.CreateDirectory(AppConfig.DataDir);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 lan-monitor");
                var data = await http.GetByteArrayAsync(Url, ct);
                await File.WriteAllBytesAsync(CachePath, data, ct);
            }
            Parse(File.ReadLines(CachePath));
        }
        catch { /* sin fabricantes, no es crítico */ }
    }

    public void Parse(IEnumerable<string> lines)
    {
        foreach (var line in lines.Skip(1))
        {
            // Registry,Assignment,"Organization Name","Organization Address"
            var f = SplitCsv(line);
            if (f.Count < 3 || f[1].Length != 6) continue;
            _map[f[1].ToUpperInvariant()] = f[2];
        }
    }

    public string Lookup(string mac)
    {
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length >= 6 && _map.TryGetValue(hex[..6], out var v) ? v : "";
    }

    static List<string> SplitCsv(string line)
    {
        var res = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
        foreach (var ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == ',' && !q) { res.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        res.Add(sb.ToString());
        return res;
    }
}
