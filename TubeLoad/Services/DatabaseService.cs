using System.IO;
using Microsoft.Data.Sqlite;
using TubeLoad.Models;

namespace TubeLoad.Services;

public class DatabaseService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public DatabaseService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "tubeload.db");
        Initialize();
    }

    private void Initialize()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                quality TEXT NOT NULL DEFAULT '',
                platform TEXT NOT NULL DEFAULT '',
                file_path TEXT NOT NULL DEFAULT '',
                file_size TEXT NOT NULL DEFAULT '',
                thumbnail TEXT NOT NULL DEFAULT '',
                downloaded_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                is_success INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_history_date ON history(downloaded_at DESC);

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL DEFAULT ''
            );
        ";
        cmd.ExecuteNonQuery();
    }

    // ==================== SETTINGS ====================
    public void SetSetting(string key, string value)
    {
        if (_connection == null) return;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        if (_connection == null) return defaultValue;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result?.ToString() ?? defaultValue;
    }

    public void AddHistory(HistoryItem item)
    {
        if (_connection == null) return;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO history (title, url, quality, platform, file_path, file_size, thumbnail, downloaded_at, is_success)
            VALUES ($title, $url, $quality, $platform, $filePath, $fileSize, $thumbnail, $downloadedAt, $isSuccess)";
        cmd.Parameters.AddWithValue("$title", item.Title);
        cmd.Parameters.AddWithValue("$url", item.Url);
        cmd.Parameters.AddWithValue("$quality", item.Quality);
        cmd.Parameters.AddWithValue("$platform", item.Platform);
        cmd.Parameters.AddWithValue("$filePath", item.FilePath);
        cmd.Parameters.AddWithValue("$fileSize", item.FileSize);
        cmd.Parameters.AddWithValue("$thumbnail", item.Thumbnail);
        cmd.Parameters.AddWithValue("$downloadedAt", item.DownloadedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$isSuccess", item.IsSuccess ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public List<HistoryItem> GetHistory(int limit = 100)
    {
        var items = new List<HistoryItem>();
        if (_connection == null) return items;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM history ORDER BY downloaded_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new HistoryItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                Quality = reader.GetString(reader.GetOrdinal("quality")),
                Platform = reader.GetString(reader.GetOrdinal("platform")),
                FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                FileSize = reader.GetString(reader.GetOrdinal("file_size")),
                Thumbnail = reader.GetString(reader.GetOrdinal("thumbnail")),
                DownloadedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("downloaded_at"))),
                IsSuccess = reader.GetInt32(reader.GetOrdinal("is_success")) == 1
            });
        }
        return items;
    }

    public List<HistoryItem> SearchHistory(string keyword)
    {
        var items = new List<HistoryItem>();
        if (_connection == null) return items;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM history WHERE title LIKE $kw OR url LIKE $kw ORDER BY downloaded_at DESC LIMIT 100";
        cmd.Parameters.AddWithValue("$kw", $"%{keyword}%");

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new HistoryItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                Quality = reader.GetString(reader.GetOrdinal("quality")),
                Platform = reader.GetString(reader.GetOrdinal("platform")),
                FilePath = reader.GetString(reader.GetOrdinal("file_path")),
                FileSize = reader.GetString(reader.GetOrdinal("file_size")),
                Thumbnail = reader.GetString(reader.GetOrdinal("thumbnail")),
                DownloadedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("downloaded_at"))),
                IsSuccess = reader.GetInt32(reader.GetOrdinal("is_success")) == 1
            });
        }
        return items;
    }

    public void DeleteHistory(int id)
    {
        if (_connection == null) return;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM history WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void ClearAllHistory()
    {
        if (_connection == null) return;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM history";
        cmd.ExecuteNonQuery();
    }

    public int GetHistoryCount()
    {
        if (_connection == null) return 0;
        var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
