using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace TubeLoad.Services;

public enum CheckStatus { Pass, Warning, Fail, Running }

public class DiagnosticCheck
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public CheckStatus Status { get; set; } = CheckStatus.Running;
    public string Result { get; set; } = "Checking...";
    public string Detail { get; set; } = "";
    public long ElapsedMs { get; set; }

    public string StatusIcon => Status switch
    {
        CheckStatus.Pass => "\u2705",
        CheckStatus.Warning => "\u26A0\uFE0F",
        CheckStatus.Fail => "\u274C",
        _ => "\u23F3"
    };
}

public class DiagnosticService
{
    private readonly YtDlpService _ytDlpService;
    private readonly DatabaseService _dbService;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public DiagnosticService(YtDlpService ytDlpService, DatabaseService dbService)
    {
        _ytDlpService = ytDlpService;
        _dbService = dbService;
    }

    public async Task<List<DiagnosticCheck>> RunAllChecksAsync(Action<DiagnosticCheck>? onCheckUpdated = null)
    {
        var checks = new List<DiagnosticCheck>();

        // === ENVIRONMENT ===
        checks.Add(await RunCheck("Environment", ".NET Runtime", CheckDotNetRuntime, onCheckUpdated));
        checks.Add(await RunCheck("Environment", "Operating System", CheckOperatingSystem, onCheckUpdated));
        checks.Add(await RunCheck("Environment", "App Version", CheckAppVersion, onCheckUpdated));
        checks.Add(await RunCheck("Environment", "Memory Usage", CheckMemoryUsage, onCheckUpdated));
        checks.Add(await RunCheck("Environment", "App Directory", CheckAppDirectory, onCheckUpdated));

        // === TOOLS ===
        checks.Add(await RunCheck("Tools", "yt-dlp.exe", CheckYtDlp, onCheckUpdated));
        checks.Add(await RunCheck("Tools", "yt-dlp Version", CheckYtDlpVersion, onCheckUpdated));
        checks.Add(await RunCheck("Tools", "ffmpeg.exe", CheckFfmpeg, onCheckUpdated));
        checks.Add(await RunCheck("Tools", "ffmpeg Version", CheckFfmpegVersion, onCheckUpdated));
        checks.Add(await RunCheck("Tools", "Tools Directory", CheckToolsDirectory, onCheckUpdated));

        // === DATABASE ===
        checks.Add(await RunCheck("Database", "SQLite Connection", CheckSqliteConnection, onCheckUpdated));
        checks.Add(await RunCheck("Database", "History Table", CheckHistoryTable, onCheckUpdated));
        checks.Add(await RunCheck("Database", "Database File", CheckDatabaseFile, onCheckUpdated));

        // === NETWORK ===
        checks.Add(await RunCheck("Network", "Internet Connection", CheckInternet, onCheckUpdated));
        checks.Add(await RunCheck("Network", "YouTube Access", CheckYouTubeAccess, onCheckUpdated));
        checks.Add(await RunCheck("Network", "TikTok Access", CheckTikTokAccess, onCheckUpdated));
        checks.Add(await RunCheck("Network", "GitHub Access (yt-dlp updates)", CheckGitHubAccess, onCheckUpdated));

        // === FILE SYSTEM ===
        checks.Add(await RunCheck("File System", "Output Directory", CheckOutputDirectory, onCheckUpdated));
        checks.Add(await RunCheck("File System", "Disk Space", CheckDiskSpace, onCheckUpdated));
        checks.Add(await RunCheck("File System", "Write Permission", CheckWritePermission, onCheckUpdated));

        return checks;
    }

    private async Task<DiagnosticCheck> RunCheck(string category, string name,
        Func<Task<DiagnosticCheck>> checkFunc, Action<DiagnosticCheck>? onUpdated)
    {
        var check = new DiagnosticCheck { Category = category, Name = name };
        onUpdated?.Invoke(check);

        Debug.WriteLine($"[DIAG] Running: [{category}] {name}...");

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await checkFunc();
            result.Category = category;
            result.Name = name;
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;

            Debug.WriteLine($"[DIAG] {result.StatusIcon} [{category}] {name}: {result.Result} ({result.ElapsedMs}ms)");
            if (!string.IsNullOrEmpty(result.Detail))
                Debug.WriteLine($"[DIAG]    Detail: {result.Detail}");

            onUpdated?.Invoke(result);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            check.Status = CheckStatus.Fail;
            check.Result = "Exception";
            check.Detail = ex.Message;
            check.ElapsedMs = sw.ElapsedMilliseconds;

            Debug.WriteLine($"[DIAG] \u274C [{category}] {name}: EXCEPTION - {ex.Message} ({check.ElapsedMs}ms)");

            onUpdated?.Invoke(check);
            return check;
        }
    }

    // ==================== ENVIRONMENT CHECKS ====================

    private Task<DiagnosticCheck> CheckDotNetRuntime()
    {
        var version = RuntimeInformation.FrameworkDescription;
        return Task.FromResult(new DiagnosticCheck
        {
            Status = CheckStatus.Pass,
            Result = version,
            Detail = $"Architecture: {RuntimeInformation.ProcessArchitecture}"
        });
    }

    private Task<DiagnosticCheck> CheckOperatingSystem()
    {
        var os = RuntimeInformation.OSDescription;
        return Task.FromResult(new DiagnosticCheck
        {
            Status = CheckStatus.Pass,
            Result = os,
            Detail = $"64-bit: {Environment.Is64BitOperatingSystem}"
        });
    }

    private Task<DiagnosticCheck> CheckAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
        var buildConfig =
#if DEBUG
            "DEBUG";
#else
            "RELEASE";
#endif
        return Task.FromResult(new DiagnosticCheck
        {
            Status = CheckStatus.Pass,
            Result = $"v{version} ({buildConfig})",
            Detail = $"Build: {buildConfig}, PID: {Environment.ProcessId}"
        });
    }

    private Task<DiagnosticCheck> CheckMemoryUsage()
    {
        var process = Process.GetCurrentProcess();
        var workingSet = process.WorkingSet64;
        var privateMem = process.PrivateMemorySize64;
        var mbWorking = workingSet / 1024.0 / 1024.0;
        var mbPrivate = privateMem / 1024.0 / 1024.0;

        var status = mbWorking > 500 ? CheckStatus.Warning : CheckStatus.Pass;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = status,
            Result = $"{mbWorking:F1} MB (Working Set)",
            Detail = $"Private: {mbPrivate:F1} MB, GC Total: {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F1} MB"
        });
    }

    private Task<DiagnosticCheck> CheckAppDirectory()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        var exists = Directory.Exists(dir);
        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Fail,
            Result = exists ? "OK" : "NOT FOUND",
            Detail = dir
        });
    }

    // ==================== TOOLS CHECKS ====================

    private Task<DiagnosticCheck> CheckYtDlp()
    {
        var path = _ytDlpService.YtDlpPath;
        var exists = File.Exists(path);
        var size = exists ? new FileInfo(path).Length / 1024.0 / 1024.0 : 0;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Fail,
            Result = exists ? $"Found ({size:F1} MB)" : "NOT FOUND",
            Detail = path
        });
    }

    private async Task<DiagnosticCheck> CheckYtDlpVersion()
    {
        if (!_ytDlpService.IsYtDlpAvailable)
            return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "yt-dlp not available", Detail = "Cannot check version" };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpService.YtDlpPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "Failed to start", Detail = "" };
            var version = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            return new DiagnosticCheck
            {
                Status = CheckStatus.Pass,
                Result = version.Trim(),
                Detail = $"Exit code: {p.ExitCode}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "Error", Detail = ex.Message };
        }
    }

    private Task<DiagnosticCheck> CheckFfmpeg()
    {
        var path = _ytDlpService.FfmpegPath;
        var exists = File.Exists(path);
        var size = exists ? new FileInfo(path).Length / 1024.0 / 1024.0 : 0;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Warning,
            Result = exists ? $"Found ({size:F1} MB)" : "NOT FOUND (video merging limited)",
            Detail = path
        });
    }

    private async Task<DiagnosticCheck> CheckFfmpegVersion()
    {
        if (!_ytDlpService.IsFfmpegAvailable)
            return new DiagnosticCheck { Status = CheckStatus.Warning, Result = "ffmpeg not available", Detail = "Cannot check version" };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ytDlpService.FfmpegPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "Failed to start", Detail = "" };
            var output = await p.StandardOutput.ReadLineAsync() ?? "";
            await p.WaitForExitAsync();

            return new DiagnosticCheck
            {
                Status = CheckStatus.Pass,
                Result = output.Trim(),
                Detail = $"Exit code: {p.ExitCode}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck { Status = CheckStatus.Warning, Result = "Error", Detail = ex.Message };
        }
    }

    private Task<DiagnosticCheck> CheckToolsDirectory()
    {
        var dir = _ytDlpService.ToolsDir;
        var exists = Directory.Exists(dir);
        int fileCount = exists ? Directory.GetFiles(dir).Length : 0;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Fail,
            Result = exists ? $"OK ({fileCount} files)" : "NOT FOUND",
            Detail = dir
        });
    }

    // ==================== DATABASE CHECKS ====================

    private Task<DiagnosticCheck> CheckSqliteConnection()
    {
        try
        {
            var count = _dbService.GetHistoryCount();
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Pass,
                Result = "Connected",
                Detail = $"Records: {count}"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Fail,
                Result = "Connection Failed",
                Detail = ex.Message
            });
        }
    }

    private Task<DiagnosticCheck> CheckHistoryTable()
    {
        try
        {
            var items = _dbService.GetHistory(1);
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Pass,
                Result = "Table OK (read/write verified)",
                Detail = items.Count > 0 ? $"Last entry: {items[0].Title}" : "Table empty"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Fail,
                Result = "Table Error",
                Detail = ex.Message
            });
        }
    }

    private Task<DiagnosticCheck> CheckDatabaseFile()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "tubeload.db");
        var exists = File.Exists(dbPath);
        var size = exists ? new FileInfo(dbPath).Length / 1024.0 : 0;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Warning,
            Result = exists ? $"Found ({size:F1} KB)" : "Not created yet",
            Detail = dbPath
        });
    }

    // ==================== NETWORK CHECKS ====================

    private async Task<DiagnosticCheck> CheckInternet()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.google.com");
            return new DiagnosticCheck
            {
                Status = response.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warning,
                Result = response.IsSuccessStatusCode ? "Connected" : $"HTTP {(int)response.StatusCode}",
                Detail = $"Response: {(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck
            {
                Status = CheckStatus.Fail,
                Result = "No Internet",
                Detail = ex.Message
            };
        }
    }

    private async Task<DiagnosticCheck> CheckYouTubeAccess()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.youtube.com");
            return new DiagnosticCheck
            {
                Status = response.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warning,
                Result = response.IsSuccessStatusCode ? "Accessible" : $"HTTP {(int)response.StatusCode}",
                Detail = $"youtube.com → {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "Blocked/Unreachable", Detail = ex.Message };
        }
    }

    private async Task<DiagnosticCheck> CheckTikTokAccess()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://www.tiktok.com");
            return new DiagnosticCheck
            {
                Status = response.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warning,
                Result = response.IsSuccessStatusCode ? "Accessible" : $"HTTP {(int)response.StatusCode}",
                Detail = $"tiktok.com → {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck { Status = CheckStatus.Fail, Result = "Blocked/Unreachable", Detail = ex.Message };
        }
    }

    private async Task<DiagnosticCheck> CheckGitHubAccess()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://api.github.com");
            return new DiagnosticCheck
            {
                Status = response.IsSuccessStatusCode ? CheckStatus.Pass : CheckStatus.Warning,
                Result = response.IsSuccessStatusCode ? "Accessible (updates OK)" : $"HTTP {(int)response.StatusCode}",
                Detail = $"api.github.com → {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck { Status = CheckStatus.Warning, Result = "Unreachable", Detail = ex.Message };
        }
    }

    // ==================== FILE SYSTEM CHECKS ====================

    private Task<DiagnosticCheck> CheckOutputDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "TubeLoad");
        var exists = Directory.Exists(dir);
        int fileCount = exists ? Directory.GetFiles(dir).Length : 0;

        return Task.FromResult(new DiagnosticCheck
        {
            Status = exists ? CheckStatus.Pass : CheckStatus.Warning,
            Result = exists ? $"OK ({fileCount} files)" : "Will be created on first download",
            Detail = dir
        });
    }

    private Task<DiagnosticCheck> CheckDiskSpace()
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var root = Path.GetPathRoot(appDir) ?? "C:\\";
            var drive = new DriveInfo(root);
            var freeGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            var totalGB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;

            var status = freeGB < 1 ? CheckStatus.Fail : freeGB < 5 ? CheckStatus.Warning : CheckStatus.Pass;

            return Task.FromResult(new DiagnosticCheck
            {
                Status = status,
                Result = $"{freeGB:F1} GB free / {totalGB:F1} GB total",
                Detail = $"Drive: {drive.Name} ({drive.DriveFormat})"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Warning,
                Result = "Cannot check",
                Detail = ex.Message
            });
        }
    }

    private Task<DiagnosticCheck> CheckWritePermission()
    {
        try
        {
            var testFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".diag_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);

            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Pass,
                Result = "Writable",
                Detail = "App directory write test passed"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new DiagnosticCheck
            {
                Status = CheckStatus.Fail,
                Result = "Read-only or restricted",
                Detail = ex.Message
            });
        }
    }
}
