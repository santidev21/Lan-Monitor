using System.Text.Json;

namespace LanMonitor.Core.Config;

public class AppConfig
{
    public int ScanIntervalSeconds { get; set; } = 60;
    public List<string> ExtraPingTargets { get; set; } = new() { "1.1.1.1", "8.8.8.8" };
    public double HighLatencyMs { get; set; } = 150;
    public double MinDownMbps { get; set; } = 10;
    public bool AutoSpeedTest { get; set; } = false;
    public int AutoSpeedTestHours { get; set; } = 6;
    public bool DesktopNotifications { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int OfflineScansBeforeDisconnect { get; set; } = 2;

    public static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "lan-monitor");
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "lan-monitor");
    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public static string DbPath => Path.Combine(DataDir, "lanmonitor.db");

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
        }
        catch { }
        var cfg = new AppConfig();
        cfg.Save();
        return cfg;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }
}
