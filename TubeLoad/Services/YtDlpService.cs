using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TubeLoad.Models;

namespace TubeLoad.Services;

public class YtDlpService
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly string _toolsDir;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    // Browser สำหรับดึง cookies (YouTube + TikTok ต้องการ auth)
    public string CookieBrowser { get; set; } = "";

    // รายชื่อ browser ที่รองรับ
    public static readonly string[] SupportedBrowsers = ["chrome", "edge", "firefox", "brave", "opera", "vivaldi"];

    // Track ว่า update แล้วหรือยังใน session นี้
    private bool _hasUpdatedThisSession;

    // Last error สำหรับ debug
    public string LastError { get; private set; } = "";
    public string LastArgs { get; private set; } = "";

    public YtDlpService()
    {
        // ใช้โฟลเดอร์ข้าง exe เสมอ (ไม่ว่าจะ debug หรือ release)
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _toolsDir = Path.Combine(appDir, "tools");
        Directory.CreateDirectory(_toolsDir);
        _ytDlpPath = Path.Combine(_toolsDir, "yt-dlp.exe");
        _ffmpegPath = Path.Combine(_toolsDir, "ffmpeg.exe");

        // Auto-detect browser ที่ติดตั้งอยู่
        CookieBrowser = DetectBrowser();
    }

    public bool IsYtDlpAvailable => File.Exists(_ytDlpPath);
    public bool IsFfmpegAvailable => File.Exists(_ffmpegPath);
    public string YtDlpPath => _ytDlpPath;
    public string FfmpegPath => _ffmpegPath;
    public string ToolsDir => _toolsDir;

    /// <summary>ตรวจจับ browser ที่ติดตั้งอยู่ในเครื่อง</summary>
    public static string DetectBrowser()
    {
        var browserPaths = new Dictionary<string, string[]>
        {
            ["chrome"] = [
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
            ],
            ["edge"] = [
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            ],
            ["firefox"] = [
                @"C:\Program Files\Mozilla Firefox\firefox.exe",
                @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
            ],
            ["brave"] = [
                @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"C:\Program Files (x86)\BraveSoftware\Brave-Browser\Application\brave.exe"
            ],
            ["opera"] = [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Opera\opera.exe")
            ],
            ["vivaldi"] = [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Vivaldi\Application\vivaldi.exe")
            ]
        };

        foreach (var (name, paths) in browserPaths)
        {
            if (paths.Any(File.Exists))
                return name;
        }
        return "chrome"; // fallback
    }

    // ==================== YT-DLP UPDATE ====================

    /// <summary>อัพเดต yt-dlp เป็นเวอร์ชันล่าสุด (ดาวน์โหลดใหม่จาก GitHub)</summary>
    public async Task<(bool success, string message)> UpdateYtDlpAsync(Action<string>? statusCallback = null)
    {
        if (_hasUpdatedThisSession)
            return (true, "Already updated this session.");

        try
        {
            statusCallback?.Invoke("Updating yt-dlp to latest version...");

            // ดาวน์โหลดเวอร์ชันล่าสุดจาก GitHub ทับไฟล์เดิม
            var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            var tempPath = _ytDlpPath + ".tmp";

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (double)downloaded / totalBytes * 100;
                    statusCallback?.Invoke($"Updating yt-dlp... {pct:F0}%");
                }
            }

            fileStream.Close();

            // แทนที่ไฟล์เดิม
            if (File.Exists(_ytDlpPath))
                File.Delete(_ytDlpPath);
            File.Move(tempPath, _ytDlpPath);

            _hasUpdatedThisSession = true;

            // ตรวจสอบเวอร์ชันใหม่
            var (ver, _, _) = await RunProcessAsync(_ytDlpPath, "--version", TimeSpan.FromSeconds(10));
            var version = ver.Trim();

            statusCallback?.Invoke($"yt-dlp updated to {version}");
            return (true, $"Updated to {version}");
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke($"Update failed: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>ดึงเวอร์ชัน yt-dlp ปัจจุบัน</summary>
    public async Task<string> GetYtDlpVersionAsync()
    {
        if (!IsYtDlpAvailable) return "not installed";
        try
        {
            var (output, _, _) = await RunProcessAsync(_ytDlpPath, "--version", TimeSpan.FromSeconds(10));
            return output.Trim();
        }
        catch { return "unknown"; }
    }

    // ==================== COOKIE HANDLING ====================

    /// <summary>สร้าง cookie flags — ใช้กับทุก platform ที่ต้องการ auth</summary>
    private string GetCookieFlags(string url, bool forceCookies = false)
    {
        if (string.IsNullOrEmpty(CookieBrowser)) return "";

        bool needsCookies = forceCookies
            || url.Contains("youtube.com") || url.Contains("youtu.be")
            || url.Contains("tiktok.com");

        if (needsCookies)
        {
            // ใช้ --cookies-from-browser browser เพื่อดึง cookies
            return $"--cookies-from-browser {CookieBrowser}";
        }
        return "";
    }

    /// <summary>ตรวจสอบว่า error เป็น bot detection / auth required หรือไม่</summary>
    private static bool IsBotDetectionError(string error)
    {
        if (string.IsNullOrEmpty(error)) return false;
        var lowerErr = error.ToLowerInvariant();
        return lowerErr.Contains("sign in to confirm")
            || lowerErr.Contains("cookies")
            || lowerErr.Contains("not a bot")
            || lowerErr.Contains("bot detection")
            || lowerErr.Contains("login required")
            || lowerErr.Contains("age-restricted")
            || lowerErr.Contains("private video")
            || lowerErr.Contains("authentication");
    }

    /// <summary>แปลง error เป็นข้อความที่ผู้ใช้เข้าใจ</summary>
    public static string GetFriendlyError(string error)
    {
        if (string.IsNullOrEmpty(error)) return "Unknown error occurred.";

        var lower = error.ToLowerInvariant();

        if (lower.Contains("sign in to confirm") || lower.Contains("not a bot"))
            return "YouTube requires authentication. Please:\n" +
                   "1. Make sure you're logged into YouTube in your browser\n" +
                   "2. Close your browser completely\n" +
                   "3. Try again\n\n" +
                   "If it still fails, try updating yt-dlp (Settings > Update yt-dlp)";

        if (lower.Contains("video unavailable") || lower.Contains("not available"))
            return "This video is unavailable. It may be:\n" +
                   "- Removed by the uploader\n" +
                   "- Region-restricted\n" +
                   "- A live stream that hasn't ended";

        if (lower.Contains("private video"))
            return "This is a private video. You need to be logged in with access.";

        if (lower.Contains("age-restricted"))
            return "This video is age-restricted. Please:\n" +
                   "1. Log into YouTube in your browser\n" +
                   "2. Close the browser\n" +
                   "3. Try again with cookies enabled";

        if (lower.Contains("429") || lower.Contains("too many requests") || lower.Contains("rate limit"))
            return "Too many requests. YouTube has rate-limited you.\n" +
                   "Please wait a few minutes and try again.";

        if (lower.Contains("network") || lower.Contains("connection") || lower.Contains("timeout"))
            return "Network error. Please check your internet connection.";

        if (lower.Contains("format") || lower.Contains("requested format"))
            return "The selected quality is not available.\n" +
                   "Try selecting 'Best Quality (Auto)' instead.";

        if (lower.Contains("ffmpeg") || lower.Contains("merger") || lower.Contains("muxer"))
            return "Error merging video and audio.\n" +
                   "ffmpeg may be missing or corrupted. Try restarting the app.";

        if (lower.Contains("cookies") || lower.Contains("--cookies-from-browser"))
            return "Cookie reading failed. Please:\n" +
                   "1. Close your browser completely (check Task Manager)\n" +
                   "2. Make sure you're logged in to the site\n" +
                   "3. Try a different browser in Cookie Browser settings";

        // ย่อ error ยาวๆ
        if (error.Length > 300) error = error[..300] + "...";
        return error;
    }

    // ==================== DOWNLOAD TOOLS ====================

    public async Task<(bool success, string message)> DownloadYtDlpAsync(Action<string>? statusCallback = null)
    {
        try
        {
            statusCallback?.Invoke("Downloading yt-dlp...");
            var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(_ytDlpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (double)downloaded / totalBytes * 100;
                    statusCallback?.Invoke($"Downloading yt-dlp... {pct:F0}%");
                }
            }

            _hasUpdatedThisSession = true;
            statusCallback?.Invoke("yt-dlp downloaded successfully!");
            return (true, "yt-dlp downloaded successfully.");
        }
        catch (Exception ex)
        {
            var msg = $"Failed to download yt-dlp: {ex.Message}";
            statusCallback?.Invoke(msg);
            return (false, msg);
        }
    }

    public async Task<(bool success, string message)> DownloadFfmpegAsync(Action<string>? statusCallback = null)
    {
        try
        {
            statusCallback?.Invoke("Downloading ffmpeg (this may take a few minutes)...");
            var url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
            var zipPath = Path.Combine(Path.GetTempPath(), "ffmpeg_tubeload.zip");
            var extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_tubeload_extract");

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;
                if (totalBytes > 0)
                {
                    var pct = (double)downloaded / totalBytes * 100;
                    var mb = downloaded / 1024.0 / 1024.0;
                    statusCallback?.Invoke($"Downloading ffmpeg... {pct:F0}% ({mb:F1} MB)");
                }
            }

            fileStream.Close();

            statusCallback?.Invoke("Extracting ffmpeg...");

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            var ffmpegExe = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegExe != null)
            {
                File.Copy(ffmpegExe, _ffmpegPath, true);

                var ffprobeExe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (ffprobeExe != null)
                    File.Copy(ffprobeExe, Path.Combine(_toolsDir, "ffprobe.exe"), true);
            }

            try { File.Delete(zipPath); } catch { }
            try { Directory.Delete(extractDir, true); } catch { }

            if (File.Exists(_ffmpegPath))
            {
                statusCallback?.Invoke("ffmpeg downloaded successfully!");
                return (true, "ffmpeg downloaded successfully.");
            }
            else
            {
                return (false, "ffmpeg.exe not found in downloaded package.");
            }
        }
        catch (Exception ex)
        {
            var msg = $"Failed to download ffmpeg: {ex.Message}";
            statusCallback?.Invoke(msg);
            return (false, msg);
        }
    }

    // ==================== VIDEO INFO (with smart retry) ====================

    public string DetectPlatform(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Unknown";
        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
            return "YouTube";
        if (url.Contains("tiktok.com"))
            return "TikTok";
        return "Unknown";
    }

    public async Task<(VideoInfo? info, string error)> GetVideoInfoAsync(string url, Action<string>? statusCallback = null)
    {
        try
        {
            if (!IsYtDlpAvailable)
                return (null, $"yt-dlp.exe not found at:\n{_ytDlpPath}");

            // Strategy: ลองกับ cookies ก่อน (YouTube + TikTok มักต้องการ)
            // ถ้า cookies ล้มเหลว → ลองไม่ใช้ cookies
            // ถ้ายังล้มเหลว → update yt-dlp แล้วลองใหม่

            var strategies = new List<(string name, string flags)>();

            bool needsCookies = url.Contains("youtube.com") || url.Contains("youtu.be") || url.Contains("tiktok.com");

            if (needsCookies && !string.IsNullOrEmpty(CookieBrowser))
            {
                // ลอง cookies ก่อน
                strategies.Add(("with cookies", $"--cookies-from-browser {CookieBrowser}"));
                // fallback: ลองไม่ใช้ cookies
                strategies.Add(("without cookies", ""));
            }
            else
            {
                // ลองไม่ใช้ cookies ก่อน
                strategies.Add(("direct", ""));
                if (!string.IsNullOrEmpty(CookieBrowser))
                    strategies.Add(("with cookies", $"--cookies-from-browser {CookieBrowser}"));
            }

            string lastError = "";

            for (int i = 0; i < strategies.Count; i++)
            {
                var (stratName, cookieFlags) = strategies[i];
                statusCallback?.Invoke(i == 0
                    ? "Fetching video information..."
                    : $"Retrying ({stratName})...");

                var args = $"--dump-json --no-warnings --no-check-certificates {cookieFlags} \"{url}\"";
                LastArgs = args;

                var (output, errorOutput, exitCode) = await RunProcessAsync(_ytDlpPath, args, TimeSpan.FromSeconds(60));

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    // สำเร็จ!
                    var info = ParseVideoInfo(output, url);
                    if (info != null)
                    {
                        statusCallback?.Invoke($"Found {info.Formats.Count} quality options.");
                        return (info, "");
                    }
                }

                lastError = errorOutput;
                LastError = errorOutput;

                // ถ้ายังไม่ใช่ strategy สุดท้าย → ถ้าเป็น bot detection ให้ลองต่อ
                if (i < strategies.Count - 1)
                {
                    if (IsBotDetectionError(errorOutput))
                    {
                        Debug.WriteLine($"[YtDlp] Strategy '{stratName}' failed (bot detection), trying next...");
                        continue;
                    }
                    // ถ้าไม่ใช่ bot detection error → อาจเป็น error อื่น ลองต่อเผื่อ
                    Debug.WriteLine($"[YtDlp] Strategy '{stratName}' failed: {errorOutput}");
                    continue;
                }
            }

            // ทุก strategy ล้มเหลว — ลอง update yt-dlp แล้วลองอีกรอบ
            if (!_hasUpdatedThisSession)
            {
                statusCallback?.Invoke("Updating yt-dlp and retrying...");
                var (updateOk, _) = await UpdateYtDlpAsync(statusCallback);

                if (updateOk)
                {
                    // ลองอีกครั้งหลัง update กับ strategy แรก (cookies)
                    var cookieFlags = needsCookies && !string.IsNullOrEmpty(CookieBrowser)
                        ? $"--cookies-from-browser {CookieBrowser}"
                        : "";
                    var args = $"--dump-json --no-warnings --no-check-certificates {cookieFlags} \"{url}\"";
                    LastArgs = args;

                    statusCallback?.Invoke("Retrying with updated yt-dlp...");
                    var (output, errorOutput, exitCode) = await RunProcessAsync(_ytDlpPath, args, TimeSpan.FromSeconds(60));

                    if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        var info = ParseVideoInfo(output, url);
                        if (info != null)
                        {
                            statusCallback?.Invoke($"Found {info.Formats.Count} quality options.");
                            return (info, "");
                        }
                    }

                    lastError = errorOutput;
                    LastError = errorOutput;
                }
            }

            // ทุกอย่างล้มเหลว
            var friendlyError = GetFriendlyError(lastError);
            statusCallback?.Invoke($"Error: {friendlyError}");
            return (null, friendlyError);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            statusCallback?.Invoke($"Error: {ex.Message}");
            return (null, ex.Message);
        }
    }

    /// <summary>Parse JSON output จาก yt-dlp เป็น VideoInfo</summary>
    private VideoInfo? ParseVideoInfo(string jsonOutput, string url)
    {
        try
        {
            var json = JObject.Parse(jsonOutput);

            double durationSec = 0;
            var durToken = json["duration"];
            if (durToken != null && durToken.Type != JTokenType.Null)
            {
                try { durationSec = durToken.Value<double>(); } catch { }
            }

            var info = new VideoInfo
            {
                Title = json["title"]?.ToString() ?? "Unknown",
                Thumbnail = json["thumbnail"]?.ToString() ?? "",
                Duration = FormatDuration(durationSec),
                Uploader = json["uploader"]?.ToString() ?? json["channel"]?.ToString() ?? "Unknown",
                Url = url,
                Platform = DetectPlatform(url)
            };

            // Parse formats
            var formats = json["formats"] as JArray;
            if (formats != null)
            {
                var seen = new HashSet<string>();
                foreach (var f in formats)
                {
                    var ext = f["ext"]?.ToString() ?? "";

                    int height = 0;
                    try { if (f["height"] is JToken ht && ht.Type != JTokenType.Null) height = ht.Value<int>(); } catch { }

                    var vcodec = f["vcodec"]?.ToString() ?? "none";
                    var acodec = f["acodec"]?.ToString() ?? "none";
                    var hasVideo = vcodec != "none" && vcodec != "";
                    var hasAudio = acodec != "none" && acodec != "";

                    long filesize = 0;
                    try
                    {
                        var fsToken = f["filesize"] ?? f["filesize_approx"];
                        if (fsToken != null && fsToken.Type != JTokenType.Null) filesize = fsToken.Value<long>();
                    }
                    catch { }

                    if (!hasVideo || height == 0) continue;

                    var resolution = $"{height}p";
                    var key = $"{resolution}_{ext}";
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    info.Formats.Add(new VideoFormat
                    {
                        FormatId = f["format_id"]?.ToString() ?? "",
                        Extension = ext,
                        Resolution = resolution,
                        FileSize = FormatFileSize(filesize),
                        Note = f["format_note"]?.ToString() ?? "",
                        HasVideo = hasVideo,
                        HasAudio = hasAudio,
                        Height = height
                    });
                }

                info.Formats = info.Formats
                    .OrderByDescending(f => f.Height)
                    .GroupBy(f => f.Resolution)
                    .Select(g => g.First())
                    .ToList();
            }

            // เพิ่ม Best Quality ด้านบน
            info.Formats.Insert(0, new VideoFormat
            {
                FormatId = "best",
                Extension = "mp4",
                Resolution = "Best Quality (Auto)",
                FileSize = "",
                HasVideo = true,
                HasAudio = true,
                Height = 9999
            });

            // เพิ่ม Audio Only ด้านล่าง
            info.Formats.Add(new VideoFormat
            {
                FormatId = "audio",
                Extension = "mp3",
                Resolution = "Audio Only (MP3)",
                FileSize = "",
                HasVideo = false,
                HasAudio = true,
                Height = 0
            });

            return info;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[YtDlp] ParseVideoInfo failed: {ex.Message}");
            return null;
        }
    }

    // ==================== DOWNLOAD VIDEO ====================

    public async Task DownloadVideoAsync(
        string url,
        string outputDir,
        string formatId,
        Action<double>? progressCallback = null,
        Action<string>? speedCallback = null,
        Action<string>? etaCallback = null,
        Action<DownloadStatus>? statusCallback = null,
        Action<string>? errorCallback = null,
        CancellationToken cancellationToken = default)
    {
        string args;
        var ffmpegDir = Path.GetDirectoryName(_ffmpegPath) ?? "";
        var outputTemplate = Path.Combine(outputDir, "%(title).100s.%(ext)s");

        var cookieFlags = GetCookieFlags(url);
        var commonFlags = $"--no-warnings --no-check-certificates --newline --force-overwrites {cookieFlags}";

        if (formatId == "audio")
        {
            args = $"--ffmpeg-location \"{ffmpegDir}\" -x --audio-format mp3 --audio-quality 0 " +
                   $"-o \"{outputTemplate}\" " +
                   $"{commonFlags} \"{url}\"";
        }
        else if (formatId == "best")
        {
            args = $"--ffmpeg-location \"{ffmpegDir}\" " +
                   $"-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best\" " +
                   $"--merge-output-format mp4 " +
                   $"-o \"{outputTemplate}\" " +
                   $"{commonFlags} \"{url}\"";
        }
        else
        {
            args = $"--ffmpeg-location \"{ffmpegDir}\" -f \"{formatId}+bestaudio/best\" " +
                   $"--merge-output-format mp4 " +
                   $"-o \"{outputTemplate}\" " +
                   $"{commonFlags} \"{url}\"";
        }

        LastArgs = args;
        statusCallback?.Invoke(DownloadStatus.Downloading);

        var psi = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            LastError = $"Cannot start yt-dlp: {ex.Message}";
            errorCallback?.Invoke(GetFriendlyError(LastError));
            statusCallback?.Invoke(DownloadStatus.Failed);
            return;
        }

        var progressRegex = new Regex(@"(\d+\.?\d*)%");
        var speedRegex = new Regex(@"at\s+(.+?)\s");
        var etaRegex = new Regex(@"ETA\s+(\S+)");

        var stderrTask = process.StandardError.ReadToEndAsync();

        while (!process.StandardOutput.EndOfStream)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { }
                statusCallback?.Invoke(DownloadStatus.Failed);
                return;
            }

            var line = await process.StandardOutput.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.Contains("[Merger]") || line.Contains("Merging"))
            {
                statusCallback?.Invoke(DownloadStatus.Merging);
            }

            var progressMatch = progressRegex.Match(line);
            if (progressMatch.Success && double.TryParse(progressMatch.Groups[1].Value,
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                progressCallback?.Invoke(pct);
            }

            var speedMatch = speedRegex.Match(line);
            if (speedMatch.Success)
                speedCallback?.Invoke(speedMatch.Groups[1].Value.Trim());

            var etaMatch = etaRegex.Match(line);
            if (etaMatch.Success)
                etaCallback?.Invoke(etaMatch.Groups[1].Value);
        }

        var stderr = await stderrTask;
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            progressCallback?.Invoke(100);
            statusCallback?.Invoke(DownloadStatus.Completed);
        }
        else
        {
            LastError = stderr;
            errorCallback?.Invoke(GetFriendlyError(stderr));
            statusCallback?.Invoke(DownloadStatus.Failed);
        }
    }

    // ==================== HELPERS ====================

    private async Task<(string output, string error, int exitCode)> RunProcessAsync(
        string fileName, string arguments, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return ("", $"Cannot start process: {ex.Message}", -1);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var timeoutMs = (int)(timeout?.TotalMilliseconds ?? 120000);

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch { }
            return ("", "Process timed out.", -1);
        }

        var output = await outputTask;
        var error = await errorTask;

        return (output, error, process.ExitCode);
    }

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"~{size:F1} {sizes[order]}";
    }
}
