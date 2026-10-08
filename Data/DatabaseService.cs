using System.IO;
using DotaPingMonitor.Models;
using Microsoft.Data.Sqlite;

namespace DotaPingMonitor.Data;

public class DatabaseService
{
    public static string DatabasePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "DotaPingMonitor");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, "ping.db");
        }
    }

    public static string ConnectionString => $"Data Source={DatabasePath}";

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                CREATE TABLE IF NOT EXISTS PingRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    PingMs INTEGER NOT NULL,
                    IsSpike INTEGER NOT NULL,
                    IsTimeout INTEGER NOT NULL,
                    Server TEXT DEFAULT '',
                    RouterPingMs INTEGER DEFAULT -1,
                    IspPingMs INTEGER DEFAULT -1,
                    RouterIp TEXT DEFAULT '',
                    IspIp TEXT DEFAULT '',
                    IncidentCategory TEXT DEFAULT '',
                    IncidentDescription TEXT DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS CustomTargets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameId TEXT NOT NULL DEFAULT 'all',
                    Name TEXT NOT NULL,
                    Host TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }).ConfigureAwait(false);
    }

    public async Task<PingRecord> SavePingAsync(
        long pingMs,
        bool isSpike = false,
        bool isTimeout = false,
        string server = "",
        long routerPingMs = -1,
        long ispPingMs = -1,
        string routerIp = "",
        string ispIp = "",
        string incidentCategory = "",
        string incidentDescription = "")
    {
        var record = new PingRecord
        {
            Timestamp = DateTime.Now,
            PingMs = pingMs,
            IsSpike = isSpike,
            IsTimeout = isTimeout,
            Server = server,
            RouterPingMs = routerPingMs,
            IspPingMs = ispPingMs,
            RouterIp = routerIp,
            IspIp = ispIp,
            IncidentCategory = incidentCategory,
            IncidentDescription = incidentDescription
        };

        await Task.Run(() =>
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO PingRecords (Timestamp, PingMs, IsSpike, IsTimeout, Server, RouterPingMs, IspPingMs, RouterIp, IspIp, IncidentCategory, IncidentDescription)
                VALUES (@ts, @ping, @spike, @timeout, @server, @rPing, @ispPing, @rIp, @ispIp, @cat, @desc);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@ts", record.Timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("@ping", record.PingMs);
            cmd.Parameters.AddWithValue("@spike", record.IsSpike ? 1 : 0);
            cmd.Parameters.AddWithValue("@timeout", record.IsTimeout ? 1 : 0);
            cmd.Parameters.AddWithValue("@server", record.Server ?? "");
            cmd.Parameters.AddWithValue("@rPing", record.RouterPingMs);
            cmd.Parameters.AddWithValue("@ispPing", record.IspPingMs);
            cmd.Parameters.AddWithValue("@rIp", record.RouterIp ?? "");
            cmd.Parameters.AddWithValue("@ispIp", record.IspIp ?? "");
            cmd.Parameters.AddWithValue("@cat", record.IncidentCategory ?? "");
            cmd.Parameters.AddWithValue("@desc", record.IncidentDescription ?? "");

            var scalar = cmd.ExecuteScalar();
            if (scalar != null && scalar != DBNull.Value)
            {
                record.Id = Convert.ToInt32(scalar);
            }
        }).ConfigureAwait(false);

        return record;
    }

    public async Task<List<PingRecord>> GetRecentAsync(int count = 100)
    {
        return await Task.Run(() =>
        {
            var list = new List<PingRecord>();
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Timestamp, PingMs, IsSpike, IsTimeout, Server, RouterPingMs, IspPingMs, RouterIp, IspIp, IncidentCategory, IncidentDescription
                FROM PingRecords
                ORDER BY Timestamp DESC
                LIMIT @count;
            ";
            cmd.Parameters.AddWithValue("@count", count);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadPingRecord(reader));
            }

            list.Reverse();
            return list;
        }).ConfigureAwait(false);
    }

    public async Task<List<PingRecord>> GetRecentByMinutesAsync(int minutes)
    {
        return await Task.Run(() =>
        {
            var list = new List<PingRecord>();
            var cutoff = DateTime.Now.AddMinutes(-minutes);

            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Timestamp, PingMs, IsSpike, IsTimeout, Server, RouterPingMs, IspPingMs, RouterIp, IspIp, IncidentCategory, IncidentDescription
                FROM PingRecords
                WHERE Timestamp >= @cutoff
                ORDER BY Timestamp ASC;
            ";
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadPingRecord(reader));
            }

            return list;
        }).ConfigureAwait(false);
    }

    public async Task<List<PingRecord>> GetAllAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<PingRecord>();
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Timestamp, PingMs, IsSpike, IsTimeout, Server, RouterPingMs, IspPingMs, RouterIp, IspIp, IncidentCategory, IncidentDescription
                FROM PingRecords
                ORDER BY Timestamp ASC;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(ReadPingRecord(reader));
            }

            return list;
        }).ConfigureAwait(false);
    }

    public async Task ClearAllAsync()
    {
        await Task.Run(() =>
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM PingRecords;";
            cmd.ExecuteNonQuery();
        }).ConfigureAwait(false);
    }

    public void ClearAll()
    {
        try
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM PingRecords;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Ignore during process shutdown
        }
    }

    public async Task CleanupOldRecordsAsync(int keepHours = 24)
    {
        await Task.Run(() =>
        {
            var cutoff = DateTime.Now.AddHours(-keepHours);
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM PingRecords WHERE Timestamp < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
            cmd.ExecuteNonQuery();
        }).ConfigureAwait(false);
    }

    private static PingRecord ReadPingRecord(SqliteDataReader reader)
    {
        DateTime ts = DateTime.MinValue;
        if (!reader.IsDBNull(1))
        {
            DateTime.TryParse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out ts);
        }

        return new PingRecord
        {
            Id = reader.GetInt32(0),
            Timestamp = ts,
            PingMs = reader.GetInt64(2),
            IsSpike = reader.GetInt32(3) == 1,
            IsTimeout = reader.GetInt32(4) == 1,
            Server = reader.IsDBNull(5) ? "" : reader.GetString(5),
            RouterPingMs = reader.IsDBNull(6) ? -1 : reader.GetInt64(6),
            IspPingMs = reader.IsDBNull(7) ? -1 : reader.GetInt64(7),
            RouterIp = reader.IsDBNull(8) ? "" : reader.GetString(8),
            IspIp = reader.IsDBNull(9) ? "" : reader.GetString(9),
            IncidentCategory = reader.IsDBNull(10) ? "" : reader.GetString(10),
            IncidentDescription = reader.IsDBNull(11) ? "" : reader.GetString(11)
        };
    }

    public async Task<List<CustomPingTarget>> GetCustomTargetsAsync(string? gameId = null)
    {
        return await Task.Run(() =>
        {
            var list = new List<CustomPingTarget>();
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();

            if (string.IsNullOrEmpty(gameId) || gameId == "all")
            {
                cmd.CommandText = "SELECT Id, GameId, Name, Host, CreatedAt FROM CustomTargets ORDER BY Id ASC;";
            }
            else
            {
                cmd.CommandText = "SELECT Id, GameId, Name, Host, CreatedAt FROM CustomTargets WHERE GameId = @gid OR GameId = 'all' ORDER BY Id ASC;";
                cmd.Parameters.AddWithValue("@gid", gameId);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime.TryParse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ct);
                list.Add(new CustomPingTarget
                {
                    Id = reader.GetInt32(0),
                    GameId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Host = reader.GetString(3),
                    CreatedAt = ct
                });
            }
            return list;
        }).ConfigureAwait(false);
    }

    public async Task<CustomPingTarget> AddCustomTargetAsync(string name, string host, string gameId = "all")
    {
        return await Task.Run(() =>
        {
            var target = new CustomPingTarget
            {
                Name = name.Trim(),
                Host = host.Trim(),
                GameId = string.IsNullOrWhiteSpace(gameId) ? "all" : gameId.Trim().ToLowerInvariant(),
                CreatedAt = DateTime.Now
            };

            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CustomTargets (GameId, Name, Host, CreatedAt)
                VALUES (@gid, @name, @host, @created);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@gid", target.GameId);
            cmd.Parameters.AddWithValue("@name", target.Name);
            cmd.Parameters.AddWithValue("@host", target.Host);
            cmd.Parameters.AddWithValue("@created", target.CreatedAt.ToString("o"));

            target.Id = Convert.ToInt32(cmd.ExecuteScalar());
            return target;
        }).ConfigureAwait(false);
    }

    public async Task<bool> DeleteCustomTargetAsync(int id)
    {
        return await Task.Run(() =>
        {
            using var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomTargets WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }).ConfigureAwait(false);
    }
}