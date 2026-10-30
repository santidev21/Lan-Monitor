namespace LanMonitor.Core.Models;

public class Device
{
    public string Mac { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool IsKnown { get; set; }
    public bool IsOnline { get; set; }
    public bool IsSelf { get; set; }
    public bool IsGateway { get; set; }
    public bool IsRandomMac { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname) ? Nickname
        : !string.IsNullOrWhiteSpace(Hostname) ? Hostname
        : !string.IsNullOrWhiteSpace(Vendor) ? Vendor : "(sin nombre)";
}

public class PingSample
{
    public string Target { get; set; } = "";
    public DateTime Time { get; set; }
    public double? RttMs { get; set; }   // null = perdido
}

public class SpeedResult
{
    public long Id { get; set; }
    public DateTime Time { get; set; }
    public double DownMbps { get; set; }
    public double UpMbps { get; set; }
    public double PingMs { get; set; }
}

public enum EventKind { NewUnknownDevice, DeviceConnected, DeviceDisconnected, InternetDown, InternetRestored, HighLatency, LowSpeed }

public class NetEvent
{
    public long Id { get; set; }
    public DateTime Time { get; set; }
    public EventKind Kind { get; set; }
    public string Message { get; set; } = "";
    public string? Mac { get; set; }

    public string Icon => Kind switch
    {
        EventKind.NewUnknownDevice => "[!] NUEVO ",
        EventKind.DeviceConnected => "[+] ONLINE",
        EventKind.DeviceDisconnected => "[-] OFFLN ",
        EventKind.InternetDown => "[X] CAIDA ",
        EventKind.InternetRestored => "[OK] RESTA",
        EventKind.HighLatency => "[~] LATEN ",
        EventKind.LowSpeed => "[v] VELOC ",
        _ => "[?]"
    };
}

public class ConnectionRecord
{
    public DateTime Time { get; set; }
    public bool Online { get; set; }
}
