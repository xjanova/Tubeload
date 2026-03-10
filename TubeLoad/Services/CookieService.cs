using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TubeLoad.Services;

/// <summary>
/// อ่าน cookies จาก Chromium-based browsers โดยไม่ต้องปิด browser
/// Copy cookie DB → decrypt ด้วย DPAPI + AES-GCM → export เป็น cookies.txt (Netscape format)
/// ใช้ร่วมกับ yt-dlp --cookies cookies.txt
/// </summary>
public static class CookieService
{
    // Chromium browser user data paths บน Windows
    private static readonly Dictionary<string, string[]> BrowserPaths = new()
    {
        ["chrome"] = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "User Data")
        ],
        ["edge"] = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data")
        ],
        ["brave"] = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BraveSoftware", "Brave-Browser", "User Data")
        ],
        ["opera"] = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Opera Software", "Opera Stable")
        ],
        ["vivaldi"] = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vivaldi", "User Data")
        ],
    };

    // Domains ที่ต้องการ cookies สำหรับ YouTube + TikTok
    private static readonly string[] YoutubeDomains = [".youtube.com", ".google.com", ".googlevideo.com", "youtube.com", "google.com"];
    private static readonly string[] TiktokDomains = [".tiktok.com", "tiktok.com", ".tiktokv.com"];

    /// <summary>
    /// Export cookies จาก browser เป็นไฟล์ cookies.txt (Netscape format)
    /// คืน null = สำเร็จ, string = error message
    /// </summary>
    public static string? ExportCookies(string browser, string outputPath, string[]? domains = null)
    {
        try
        {
            var userDataPath = FindBrowserPath(browser);
            if (userDataPath == null)
                return $"Browser data not found for: {browser}";

            // อ่าน master encryption key
            var localStatePath = Path.Combine(userDataPath, "Local State");

            // Opera เก็บ Local State ในโฟลเดอร์เดียวกับ profile
            if (!File.Exists(localStatePath))
            {
                // ลองหาใน parent directory
                var parent = Directory.GetParent(userDataPath)?.FullName;
                if (parent != null)
                    localStatePath = Path.Combine(parent, "Local State");
            }

            if (!File.Exists(localStatePath))
                return "Local State file not found (needed for cookie decryption)";

            byte[]? masterKey = GetMasterKey(localStatePath);
            if (masterKey == null)
                return "Failed to read browser encryption key";

            // หา cookies database
            var cookiesPath = FindCookiesDb(userDataPath);
            if (cookiesPath == null)
                return "Cookies database not found";

            Debug.WriteLine($"[Cookie] Found cookies DB: {cookiesPath}");

            // Copy cookies DB ไปที่ temp (เพื่อ bypass file lock จาก browser)
            var tempDb = Path.Combine(Path.GetTempPath(), $"TubeLoad_cookies_{Guid.NewGuid():N}.db");
            try
            {
                CopyLockedFile(cookiesPath, tempDb);

                // Copy WAL/journal ถ้ามี
                foreach (var suffix in new[] { "-wal", "-journal", "-shm" })
                {
                    var walPath = cookiesPath + suffix;
                    if (File.Exists(walPath))
                    {
                        try { CopyLockedFile(walPath, tempDb + suffix); }
                        catch { /* WAL ไม่จำเป็น */ }
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Cannot copy cookie database (is another program locking it?): {ex.Message}";
            }

            try
            {
                return ReadAndExportCookies(tempDb, masterKey, outputPath, domains);
            }
            finally
            {
                // Cleanup temp files
                foreach (var suffix in new[] { "", "-wal", "-journal", "-shm" })
                {
                    try { File.Delete(tempDb + suffix); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cookie] Export failed: {ex}");
            return $"Cookie export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Export cookies สำหรับ YouTube + TikTok domains
    /// </summary>
    public static string? ExportCookiesForVideo(string browser, string outputPath, string url)
    {
        var domains = new List<string>();

        if (url.Contains("youtube.com") || url.Contains("youtu.be") || url.Contains("google.com"))
            domains.AddRange(YoutubeDomains);

        if (url.Contains("tiktok.com"))
            domains.AddRange(TiktokDomains);

        if (domains.Count == 0)
            domains.AddRange(YoutubeDomains); // default

        return ExportCookies(browser, outputPath, domains.ToArray());
    }

    /// <summary>ตรวจสอบว่ารองรับ browser นี้หรือไม่</summary>
    public static bool IsSupported(string browser)
        => BrowserPaths.ContainsKey(browser.ToLowerInvariant());

    // ==================== PRIVATE METHODS ====================

    private static string? FindBrowserPath(string browser)
    {
        var key = browser.ToLowerInvariant();
        if (!BrowserPaths.TryGetValue(key, out var paths))
            return null;

        return paths.FirstOrDefault(Directory.Exists);
    }

    private static byte[]? GetMasterKey(string localStatePath)
    {
        try
        {
            // อ่าน Local State JSON ด้วย FileShare เพื่อไม่ conflict กับ browser
            string json;
            using (var fs = new FileStream(localStatePath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs))
            {
                json = reader.ReadToEnd();
            }

            using var doc = JsonDocument.Parse(json);
            var encryptedKeyB64 = doc.RootElement
                .GetProperty("os_crypt")
                .GetProperty("encrypted_key")
                .GetString();

            if (string.IsNullOrEmpty(encryptedKeyB64))
                return null;

            var encryptedKey = Convert.FromBase64String(encryptedKeyB64);

            // ลบ prefix "DPAPI" (5 bytes)
            if (encryptedKey.Length <= 5) return null;
            if (Encoding.ASCII.GetString(encryptedKey[..5]) != "DPAPI")
                return null;

            var keyBytes = encryptedKey[5..];

            // Decrypt ด้วย Windows DPAPI
            return ProtectedData.Unprotect(keyBytes, null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cookie] GetMasterKey failed: {ex.Message}");
            return null;
        }
    }

    private static string? FindCookiesDb(string userDataPath)
    {
        // Chrome 96+ ย้าย cookies ไปอยู่ใน Network subfolder
        var paths = new[]
        {
            Path.Combine(userDataPath, "Default", "Network", "Cookies"),
            Path.Combine(userDataPath, "Default", "Cookies"),
            Path.Combine(userDataPath, "Network", "Cookies"), // Opera
            Path.Combine(userDataPath, "Cookies"),             // Opera fallback
        };

        return paths.FirstOrDefault(File.Exists);
    }

    private static void CopyLockedFile(string source, string dest)
    {
        // ใช้ FileShare.ReadWrite เพื่ออ่านไฟล์ที่ browser ยังเปิดอยู่
        using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var destStream = new FileStream(dest, FileMode.Create, FileAccess.Write);
        sourceStream.CopyTo(destStream);
    }

    private static string? ReadAndExportCookies(string dbPath, byte[] masterKey,
        string outputPath, string[]? domains)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        sb.AppendLine("# Exported by TubeLoad");
        sb.AppendLine();

        int count = 0;

        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        // ตรวจสอบ table structure (Chrome versions ต่างกัน)
        var hasIsHttpOnly = TableHasColumn(conn, "cookies", "is_httponly");

        // สร้าง query
        var query = "SELECT host_key, path, is_secure, expires_utc, name, encrypted_value FROM cookies";

        // Filter by domains
        if (domains != null && domains.Length > 0)
        {
            var conditions = new List<string>();
            for (int i = 0; i < domains.Length; i++)
                conditions.Add($"host_key LIKE @d{i}");
            query += $" WHERE ({string.Join(" OR ", conditions)})";
        }

        using var cmd = new SqliteCommand(query, conn);

        if (domains != null)
        {
            for (int i = 0; i < domains.Length; i++)
                cmd.Parameters.AddWithValue($"@d{i}", $"%{domains[i]}%");
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var host = reader.GetString(0);
                var path = reader.GetString(1);
                var secure = reader.GetInt64(2) == 1;
                var expiresUtc = reader.GetInt64(3);
                var name = reader.GetString(4);
                var encryptedValue = (byte[])reader[5];

                // Decrypt cookie value
                string value;
                try
                {
                    value = DecryptCookieValue(encryptedValue, masterKey);
                }
                catch
                {
                    continue; // ข้าม cookies ที่ decrypt ไม่ได้
                }

                if (string.IsNullOrEmpty(value)) continue;

                // Convert Chrome timestamp → Unix timestamp
                // Chrome: microseconds since 1601-01-01
                // Unix: seconds since 1970-01-01
                long unixExpires = 0;
                if (expiresUtc > 0)
                {
                    unixExpires = (expiresUtc / 1_000_000) - 11_644_473_600;
                    if (unixExpires < 0) unixExpires = 0;
                }

                var domainFlag = host.StartsWith('.') ? "TRUE" : "FALSE";
                var secureFlag = secure ? "TRUE" : "FALSE";

                // Netscape format: domain  flag  path  secure  expires  name  value
                sb.AppendLine($"{host}\t{domainFlag}\t{path}\t{secureFlag}\t{unixExpires}\t{name}\t{value}");
                count++;
            }
            catch
            {
                continue; // ข้ามแถวที่ error
            }
        }

        if (count == 0)
            return "No cookies found for the specified domains. Are you logged in?";

        File.WriteAllText(outputPath, sb.ToString());
        Debug.WriteLine($"[Cookie] Exported {count} cookies to {outputPath}");
        return null; // null = สำเร็จ
    }

    private static bool TableHasColumn(SqliteConnection conn, string table, string column)
    {
        try
        {
            using var cmd = new SqliteCommand($"PRAGMA table_info({table})", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetString(1) == column) return true;
            }
        }
        catch { }
        return false;
    }

    private static string DecryptCookieValue(byte[] encryptedValue, byte[] masterKey)
    {
        if (encryptedValue == null || encryptedValue.Length < 3)
            return "";

        // ตรวจ v10/v20 prefix (Chromium AES-GCM encryption ตั้งแต่ Chrome v80+)
        if (encryptedValue.Length > 3 &&
            encryptedValue[0] == (byte)'v' &&
            (encryptedValue[1] == (byte)'1' || encryptedValue[1] == (byte)'2') &&
            encryptedValue[2] == (byte)'0')
        {
            // Format: version(3) + nonce(12) + ciphertext + tag(16)
            if (encryptedValue.Length < 3 + 12 + 16)
                return "";

            var nonce = encryptedValue[3..15];          // 12 bytes
            var ciphertextWithTag = encryptedValue[15..];

            if (ciphertextWithTag.Length < 16)
                return "";

            var ciphertext = ciphertextWithTag[..^16];
            var tag = ciphertextWithTag[^16..];

            var plaintext = new byte[ciphertext.Length];

            using var aesGcm = new AesGcm(masterKey, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        else
        {
            // Legacy DPAPI encryption (Chrome < v80)
            try
            {
                var decrypted = ProtectedData.Unprotect(encryptedValue, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return "";
            }
        }
    }
}
