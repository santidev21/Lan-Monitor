using Dapper;
using LanMonitor.Core.Models;
using Microsoft.Data.Sqlite;

namespace LanMonitor.Core.Storage;

/// <summary>SQLite con una única conexión compartida (serializada con lock).</summary>
public class Database : IDisposable
{
    readonly SqliteConnection _conn;
    readonly object _lock = new();

    public Database(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();
        Migrate();
    }

    void Migrate()
    {
        _conn.Execute("""
        CREATE TABLE IF NOT EXISTS devices(
            mac TEXT PRIMARY KEY, ip TEXT, nickname TEXT, hostname TEXT, vendor TEXT, notes TEXT,
            is_known INTEGER, is_self INTEGER, is_gateway INTEGER, is_random INTEGER,
            first_seen TEXT, last_seen TEXT, is_online INTEGER);
        CREATE TABLE IF NOT EXISTS connections(id INTEGER PRIMARY KEY AUTOINCREMENT, mac TEXT, time TEXT, online INTEGER);
        CREATE TABLE IF NOT EXISTS ping_minutes(target TEXT, minute TEXT, avg REAL, min REAL, max REAL, loss REAL, PRIMARY KEY(target, minute));
        CREATE TABLE IF NOT EXISTS speed_results(id INTEGER PRIMARY KEY AUTOINCREMENT, time TEXT, down REAL, up REAL, ping REAL);
        CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT, time TEXT, kind INTEGER, message TEXT, mac TEXT);
        """);
    }

    T Run<T>(Func<SqliteConnection, T> f) { lock (_lock) return f(_conn); }

    // ---- Devices
    public List<Device> GetDevices() => Run(c => c.Query(
        "SELECT * FROM devices ORDER BY is_online DESC, ip").Select(Map).ToList());

    public Device? GetDevice(string mac) => Run(c => c.Query(
        "SELECT * FROM devices WHERE mac=@mac", new { mac }).Select(Map).FirstOrDefault());

    public void UpsertDevice(Device d) => Run(c => c.Execute("""
        INSERT INTO devices(mac,ip,nickname,hostname,vendor,notes,is_known,is_self,is_gateway,is_random,first_seen,last_seen,is_online)
        VALUES(@Mac,@Ip,@Nickname,@Hostname,@Vendor,@Notes,@k,@s,@g,@r,@fs,@ls,@o)
        ON CONFLICT(mac) DO UPDATE SET ip=@Ip,nickname=@Nickname,hostname=@Hostname,vendor=@Vendor,notes=@Notes,
            is_known=@k,is_self=@s,is_gateway=@g,is_random=@r,last_seen=@ls,is_online=@o
        """, new
    {
        d.Mac, d.Ip, d.Nickname, d.Hostname, d.Vendor, d.Notes,
        k = d.IsKnown ? 1 : 0, s = d.IsSelf ? 1 : 0, g = d.IsGateway ? 1 : 0, r = d.IsRandomMac ? 1 : 0,
        fs = d.FirstSeen.ToString("O"), ls = d.LastSeen.ToString("O"), o = d.IsOnline ? 1 : 0
    }));

    public void DeleteDevice(string mac) => Run(c =>
    {
        c.Execute("DELETE FROM devices WHERE mac=@mac; DELETE FROM connections WHERE mac=@mac", new { mac });
        return 0;
    });

    public void AddConnection(string mac, bool online, DateTime time) => Run(c => c.Execute(
        "INSERT INTO connections(mac,time,online) VALUES(@mac,@t,@o)", new { mac, t = time.ToString("O"), o = online ? 1 : 0 }));

    public List<ConnectionRecord> GetConnections(string mac, int limit = 50) => Run(c => c.Query(
        "SELECT time, online FROM connections WHERE mac=@mac ORDER BY id DESC LIMIT @limit", new { mac, limit })
        .Select(r => new ConnectionRecord { Time = DateTime.Parse((string)r.time), Online = (long)r.online == 1 }).ToList());

    static Device Map(dynamic r) => new()
    {
        Mac = r.mac, Ip = r.ip ?? "", Nickname = r.nickname ?? "", Hostname = r.hostname ?? "", Vendor = r.vendor ?? "", Notes = r.notes ?? "",
        IsKnown = r.is_known == 1, IsSelf = r.is_self == 1, IsGateway = r.is_gateway == 1, IsRandomMac = r.is_random == 1, IsOnline = r.is_online == 1,
        FirstSeen = DateTime.Parse((string)r.first_seen), LastSeen = DateTime.Parse((string)r.last_seen)
    };

    // ---- Ping aggregates
    public void SaveMinute(string target, DateTime minute, double avg, double min, double max, double loss) => Run(c => c.Execute(
        "INSERT OR REPLACE INTO ping_minutes VALUES(@target,@m,@avg,@min,@max,@loss)",
        new { target, m = minute.ToString("O"), avg, min, max, loss }));

    public List<(DateTime Minute, double Avg, double Loss)> GetMinutes(string target, TimeSpan window) => Run(c => c.Query(
        "SELECT minute, avg, loss FROM ping_minutes WHERE target=@target AND minute>=@from ORDER BY minute",
        new { target, from = DateTime.Now.Subtract(window).ToString("O") })
        .Select(r => (DateTime.Parse((string)r.minute), (double)r.avg, (double)r.loss)).ToList());

    // ---- Speed
    public void AddSpeed(SpeedResult s) => Run(c =>
    {
        s.Id = c.ExecuteScalar<long>("INSERT INTO speed_results(time,down,up,ping) VALUES(@t,@d,@u,@p); SELECT last_insert_rowid();",
            new { t = s.Time.ToString("O"), d = s.DownMbps, u = s.UpMbps, p = s.PingMs });
        return 0;
    });

    public List<SpeedResult> GetSpeeds(int limit = 30) => Run(c => c.Query(
        "SELECT * FROM speed_results ORDER BY id DESC LIMIT @limit", new { limit })
        .Select(r => new SpeedResult { Id = r.id, Time = DateTime.Parse((string)r.time), DownMbps = r.down, UpMbps = r.up, PingMs = r.ping }).ToList());

    // ---- Events
    public void AddEvent(NetEvent e) => Run(c =>
    {
        e.Id = c.ExecuteScalar<long>("INSERT INTO events(time,kind,message,mac) VALUES(@t,@k,@m,@mac); SELECT last_insert_rowid();",
            new { t = e.Time.ToString("O"), k = (int)e.Kind, m = e.Message, mac = e.Mac });
        return 0;
    });

    public List<NetEvent> GetEvents(int limit = 200) => Run(c => c.Query(
        "SELECT * FROM events ORDER BY id DESC LIMIT @limit", new { limit })
        .Select(r => new NetEvent { Id = r.id, Time = DateTime.Parse((string)r.time), Kind = (EventKind)(int)r.kind, Message = r.message, Mac = r.mac }).ToList());

    public void Purge(int retentionDays) => Run(c =>
    {
        var cut = DateTime.Now.AddDays(-retentionDays).ToString("O");
        c.Execute("DELETE FROM events WHERE time<@cut; DELETE FROM ping_minutes WHERE minute<@cut; DELETE FROM connections WHERE time<@cut", new { cut });
        return 0;
    });

    public void Dispose() => _conn.Dispose();
}
