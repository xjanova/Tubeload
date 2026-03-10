using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using TubeLoad.Services;
using TubeLoad.Tests;

namespace TubeLoad;

public partial class DebugWindow : Window
{
    private readonly DiagnosticService _diagnosticService;
    private readonly YtDlpService _ytDlpService;
    private readonly DatabaseService _dbService;
    private readonly QueueService _queueService;
    private readonly ObservableCollection<DiagnosticCheck> _checks = new();
    private bool _isRunning;

    public DebugWindow(YtDlpService ytDlpService, DatabaseService dbService, QueueService queueService)
    {
        InitializeComponent();
        _ytDlpService = ytDlpService;
        _dbService = dbService;
        _queueService = queueService;
        _diagnosticService = new DiagnosticService(ytDlpService, dbService);
        CheckList.ItemsSource = _checks;
        Loaded += async (_, _) =>
        {
            await RunDiagnosticsAsync();
            // Auto-run functional tests after diagnostics
            await RunFunctionalTestsAsync();
        };
    }

    private async Task RunDiagnosticsAsync()
    {
        if (_isRunning) return;
        _isRunning = true;
        RerunBtn.IsEnabled = false;
        TestBtn.IsEnabled = false;

        _checks.Clear();
        SummaryText.Text = "Running diagnostics...";
        SummaryDetail.Text = "Checking all systems...";
        PassCount.Text = "0";
        WarnCount.Text = "0";
        FailCount.Text = "0";

        var sw = Stopwatch.StartNew();

        var results = await _diagnosticService.RunAllChecksAsync(check =>
        {
            Dispatcher.Invoke(() =>
            {
                var existing = _checks.FirstOrDefault(c => c.Category == check.Category && c.Name == check.Name);
                if (existing != null)
                {
                    var idx = _checks.IndexOf(existing);
                    _checks[idx] = check;
                }
                else
                {
                    _checks.Add(check);
                }
                UpdateSummary();
            });
        });

        sw.Stop();

        Dispatcher.Invoke(() =>
        {
            UpdateSummary();
            var pass = results.Count(c => c.Status == CheckStatus.Pass);
            var warn = results.Count(c => c.Status == CheckStatus.Warning);
            var fail = results.Count(c => c.Status == CheckStatus.Fail);

            if (fail == 0 && warn == 0)
                SummaryText.Text = "\u2705 All systems operational!";
            else if (fail == 0)
                SummaryText.Text = $"\u26A0\uFE0F {warn} warning(s) found";
            else
                SummaryText.Text = $"\u274C {fail} issue(s) detected";

            SummaryDetail.Text = $"Completed {results.Count} checks in {sw.ElapsedMilliseconds}ms";
            FooterText.Text = $"Last run: {DateTime.Now:HH:mm:ss} | Total: {sw.ElapsedMilliseconds}ms | Debug mode only";

            // Write summary to VS Output window
            Debug.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Debug.WriteLine("║       TUBELOAD DIAGNOSTIC SUMMARY                       ║");
            Debug.WriteLine("╠══════════════════════════════════════════════════════════╣");
            Debug.WriteLine($"║  Pass: {pass}  |  Warning: {warn}  |  Fail: {fail}  |  Total: {results.Count}     ║");
            Debug.WriteLine($"║  Time: {sw.ElapsedMilliseconds}ms                                           ║");
            Debug.WriteLine("╚══════════════════════════════════════════════════════════╝");

            if (fail > 0)
            {
                Debug.WriteLine("[DIAG] FAILED CHECKS:");
                foreach (var c in results.Where(r => r.Status == CheckStatus.Fail))
                    Debug.WriteLine($"[DIAG]   \u274C {c.Category}/{c.Name}: {c.Result} - {c.Detail}");
            }

            // Also write a diagnostic log file
            try
            {
                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, $"diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                var lines = new List<string>
                {
                    $"TubeLoad Diagnostic Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"Pass: {pass} | Warning: {warn} | Fail: {fail} | Total: {results.Count}",
                    $"Duration: {sw.ElapsedMilliseconds}ms",
                    new string('-', 60)
                };
                foreach (var c in results)
                    lines.Add($"[{c.Status}] {c.Category}/{c.Name}: {c.Result} | {c.Detail} ({c.ElapsedMs}ms)");
                File.WriteAllLines(logFile, lines);
                Debug.WriteLine($"[DIAG] Log saved: {logFile}");
            }
            catch { }

            _isRunning = false;
            RerunBtn.IsEnabled = true;
            TestBtn.IsEnabled = true;
        });
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        await RunFunctionalTestsAsync();
    }

    private async Task RunFunctionalTestsAsync()
    {
        if (_isRunning) return;
        _isRunning = true;

        Dispatcher.Invoke(() =>
        {
            TestBtn.IsEnabled = false;
            RerunBtn.IsEnabled = false;

            // Add separator
            _checks.Add(new DiagnosticCheck
            {
                Category = "Tests",
                Name = "Functional Tests",
                Status = CheckStatus.Running,
                Result = "Running tests...",
                Detail = "Testing core features..."
            });
        });

        try
        {
            var (passed, failed, errors) = await TestRunner.RunAllTestsAsync(
                _ytDlpService, _dbService, _queueService);

            Dispatcher.Invoke(() =>
            {
                // Replace the "running" placeholder
                var placeholder = _checks.FirstOrDefault(c => c.Category == "Tests" && c.Name == "Functional Tests");
                if (placeholder != null)
                {
                    var idx = _checks.IndexOf(placeholder);
                    _checks[idx] = new DiagnosticCheck
                    {
                        Category = "Tests",
                        Name = "Functional Tests",
                        Status = failed == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                        Result = $"{passed} passed, {failed} failed",
                        Detail = $"Total: {passed + failed} tests"
                    };
                }

                // Add individual failures
                foreach (var err in errors)
                {
                    _checks.Add(new DiagnosticCheck
                    {
                        Category = "Tests",
                        Name = "Failed Test",
                        Status = CheckStatus.Fail,
                        Result = err,
                        Detail = ""
                    });
                }

                UpdateSummary();

                var totalPass = _checks.Count(c => c.Status == CheckStatus.Pass);
                var totalWarn = _checks.Count(c => c.Status == CheckStatus.Warning);
                var totalFail = _checks.Count(c => c.Status == CheckStatus.Fail);

                if (totalFail == 0 && totalWarn == 0)
                    SummaryText.Text = $"\u2705 All checks & tests passed!";
                else if (totalFail == 0)
                    SummaryText.Text = $"\u26A0\uFE0F {totalWarn} warning(s)";
                else
                    SummaryText.Text = $"\u274C {totalFail} issue(s) detected";

                SummaryDetail.Text = $"Diagnostics + Tests complete | {totalPass} pass, {totalWarn} warn, {totalFail} fail";
                FooterText.Text = $"Last run: {DateTime.Now:HH:mm:ss} | Tests: {passed}/{passed + failed} passed | Debug mode only";

                // Save test log
                try
                {
                    var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    Directory.CreateDirectory(logDir);
                    var logFile = Path.Combine(logDir, $"tests_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                    var lines = new List<string>
                    {
                        $"TubeLoad Test Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"Passed: {passed} | Failed: {failed} | Total: {passed + failed}",
                        new string('-', 60)
                    };
                    foreach (var err in errors)
                        lines.Add($"[FAIL] {err}");
                    if (errors.Count == 0)
                        lines.Add("[ALL PASSED]");
                    File.WriteAllLines(logFile, lines);
                    Debug.WriteLine($"[TEST] Log saved: {logFile}");
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                var placeholder = _checks.FirstOrDefault(c => c.Category == "Tests" && c.Name == "Functional Tests");
                if (placeholder != null)
                {
                    var idx = _checks.IndexOf(placeholder);
                    _checks[idx] = new DiagnosticCheck
                    {
                        Category = "Tests",
                        Name = "Test Runner Error",
                        Status = CheckStatus.Fail,
                        Result = "Exception",
                        Detail = ex.Message
                    };
                }
                UpdateSummary();
            });
        }
        finally
        {
            _isRunning = false;
            Dispatcher.Invoke(() =>
            {
                TestBtn.IsEnabled = true;
                RerunBtn.IsEnabled = true;
            });
        }
    }

    private void UpdateSummary()
    {
        PassCount.Text = _checks.Count(c => c.Status == CheckStatus.Pass).ToString();
        WarnCount.Text = _checks.Count(c => c.Status == CheckStatus.Warning).ToString();
        FailCount.Text = _checks.Count(c => c.Status == CheckStatus.Fail).ToString();
    }

    private async void RerunBtn_Click(object sender, RoutedEventArgs e)
    {
        await RunDiagnosticsAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal : WindowState.Maximized;
        }
        else
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
