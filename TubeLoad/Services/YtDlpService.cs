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

    // Browser สำหรับดึง cookies (TikTok ต้องการ auth)
    public string CookieBrowser { get; set; } = "";

    // รายชื่อ browser ที่รองรับ
    public static readonly string[] SupportedBrowsers = ["chrome", "edge", "firefox", "brave", "opera", "vivaldi"];

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

    /// <summary>สร้าง cookie flags — YouTube + TikTok ต้องการ cookies เพื่อผ่าน bot detection</summary>
    private string GetCookieFlags(string url)
    {
        if (string.IsNullOrEmpty(CookieBrowser)) return "";

        // YouTube และ TikTok ต้องการ cookies เพื่อผ่าน auth / bot detection
        if (url.Contains("youtube.com") || url.Contains("youtu.be") || url.Contains("tiktok.com"))
        {
            return $"--cookies-from-browser {CookieBrowser}";
        }
        return "";
    }

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
            // ใช้ essentials build (เล็กกว่า ~30MB แทน ~130MB)
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

            // หา ffmpeg.exe ใน zip
            var ffmpegExe = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (ffmpegExe != null)
            {
                File.Copy(ffmpegExe, _ffmpegPath, true);

                // คัดลอก ffprobe.exe ด้วย (จำเป็นสำหรับบาง format)
                var ffprobeExe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (ffprobeExe != null)
                {
                    File.Copy(ffprobeExe, Path.Combine(_toolsDir, "ffprobe.exe"), true);
                }
            }

            // Cleanup
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

            statusCallback?.Invoke("Fetching video information...");

            var cookieFlags = GetCookieFlags(url);
            var args = $"--dump-json --no-warnings --no-check-certificates {cookieFlags} \"{url}\"";
            var (output, errorOutput, exitCode) = await RunProcessAsync(_ytDlpPath, args, TimeSpan.FromSeconds(60));

            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                var errMsg = !string.IsNullOrWhiteSpace(errorOutput)
                    ? errorOutput
                    : "yt-dlp returned no data. Check the URL.";
                statusCallback?.Invoke($"Error: {errMsg}");
                return (null, errMsg);
            }

            var json = JObject.Parse(output);

            // Safe duration parse (handles null JToken)
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

            statusCallback?.Invoke($"Found {info.Formats.Count} quality options.");
            return (info, "");
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke($"Error: {ex.Message}");
            return (null, ex.Message);
        }
    }

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

        // สร้าง output path ที่ safe (ลบ characters พิเศษ)
        var outputTemplate = Path.Combine(outputDir, "%(title).100s.%(ext)s");

        // --force-overwrites ป้องกัน skip เมื่อไฟล์มีอยู่แล้ว (จะไม่แสดง progress)
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
            errorCallback?.Invoke($"Cannot start yt-dlp: {ex.Message}");
            statusCallback?.Invoke(DownloadStatus.Failed);
            return;
        }

        var progressRegex = new Regex(@"(\d+\.?\d*)%");
        var speedRegex = new Regex(@"at\s+(.+?)\s");
        var etaRegex = new Regex(@"ETA\s+(\S+)");

        // อ่าน stderr แยก thread
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
            errorCallback?.Invoke(stderr);
            statusCallback?.Invoke(DownloadStatus.Failed);
        }
    }

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

        // อ่าน stdout + stderr พร้อมกัน (ไม่ deadlock)
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
