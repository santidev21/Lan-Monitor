namespace LanMonitor.Core.Network;

public record ArpEntry(string Ip, string Mac, string Device);

public static class ArpTable
{
    /// <summary>Parsea el contenido de /proc/net/arp. Solo entradas completas (flag 0x2) con MAC válida.</summary>
    public static List<ArpEntry> Parse(IEnumerable<string> lines)
    {
        var result = new List<ArpEntry>();
        foreach (var line in lines.Skip(1))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 6) continue;
            var flags = p[2];
            var mac = p[3].ToUpperInvariant();
            if (flags != "0x2" || mac == "00:00:00:00:00:00") continue;
            result.Add(new ArpEntry(p[0], mac, p[5]));
        }
        return result;
    }

    public static List<ArpEntry> Read()
    {
        try { return Parse(File.ReadAllLines("/proc/net/arp")); }
        catch { return new(); }
    }

    /// <summary>MAC "privada/aleatoria": bit localmente administrado (2º bit menos significativo del primer octeto).</summary>
    public static bool IsRandomMac(string mac)
    {
        if (mac.Length < 2) return false;
        return int.TryParse(mac[..2], System.Globalization.NumberStyles.HexNumber, null, out var b) && (b & 0x02) != 0;
    }
}
