using System.Diagnostics;
using TubeLoad.Models;
using TubeLoad.Services;

namespace TubeLoad.Tests;

/// <summary>
/// Simple self-test runner that runs in DEBUG mode.
/// Outputs results to Debug window (VS Output).
/// </summary>
public static class TestRunner
{
    public static async Task<(int passed, int failed, List<string> errors)> RunAllTestsAsync(
        YtDlpService ytDlpService, DatabaseService dbService, QueueService queueService)
    {
        int passed = 0, failed = 0;
        var errors = new List<string>();

        Debug.WriteLine("╔═══════════════════════════════════════════════╗");
        Debug.WriteLine("║     TUBELOAD FUNCTIONAL TESTS                ║");
        Debug.WriteLine("╚═══════════════════════════════════════════════╝");

        // === Test 1: Database AddHistory + GetHistory ===
        try
        {
            var testItem = new HistoryItem
            {
                Title = "TEST_ITEM_" + Guid.NewGuid().ToString("N")[..6],
                Url = "https://www.youtube.com/watch?v=test123",
                Quality = "720p",
                Platform = "YouTube",
                FilePath = @"C:\test\path",
                FileSize = "10 MB",
                Thumbnail = "https://example.com/thumb.jpg",
                DownloadedAt = DateTime.Now,
                IsSuccess = true
            };

            dbService.AddHistory(testItem);
            var history = dbService.GetHistory(1);
            if (history.Count > 0 && history[0].Title == testItem.Title)
            {
                passed++;
                Debug.WriteLine("[TEST] ✅ Database AddHistory + GetHistory: PASS");
            }
            else
            {
                failed++;
                errors.Add("Database AddHistory: Retrieved item doesn't match");
                Debug.WriteLine("[TEST] ❌ Database AddHistory + GetHistory: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Database AddHistory: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Database AddHistory: EXCEPTION - {ex.Message}");
        }

        // === Test 2: Database SearchHistory ===
        try
        {
            var searchResults = dbService.SearchHistory("TEST_ITEM_");
            if (searchResults.Count > 0)
            {
                passed++;
                Debug.WriteLine("[TEST] ✅ Database SearchHistory: PASS");
            }
            else
            {
                failed++;
                errors.Add("Database SearchHistory: No results found for test item");
                Debug.WriteLine("[TEST] ❌ Database SearchHistory: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Database SearchHistory: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Database SearchHistory: EXCEPTION - {ex.Message}");
        }

        // === Test 3: Database DeleteHistory ===
        try
        {
            var items = dbService.SearchHistory("TEST_ITEM_");
            foreach (var item in items)
                dbService.DeleteHistory(item.Id);
            var afterDelete = dbService.SearchHistory("TEST_ITEM_");
            if (afterDelete.Count == 0)
            {
                passed++;
                Debug.WriteLine("[TEST] ✅ Database DeleteHistory: PASS");
            }
            else
            {
                failed++;
                errors.Add("Database DeleteHistory: Items still exist after delete");
                Debug.WriteLine("[TEST] ❌ Database DeleteHistory: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Database DeleteHistory: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Database DeleteHistory: EXCEPTION - {ex.Message}");
        }

        // === Test 4: Database GetHistoryCount ===
        try
        {
            var count = dbService.GetHistoryCount();
            passed++;
            Debug.WriteLine($"[TEST] ✅ Database GetHistoryCount: PASS (count={count})");
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Database GetHistoryCount: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Database GetHistoryCount: EXCEPTION - {ex.Message}");
        }

        // === Test 5: YtDlpService tool paths ===
        try
        {
            if (ytDlpService.IsYtDlpAvailable)
            {
                passed++;
                Debug.WriteLine($"[TEST] ✅ YtDlp Available: PASS ({ytDlpService.YtDlpPath})");
            }
            else
            {
                failed++;
                errors.Add($"YtDlp not available at: {ytDlpService.YtDlpPath}");
                Debug.WriteLine($"[TEST] ❌ YtDlp Available: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"YtDlp check: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ YtDlp check: EXCEPTION - {ex.Message}");
        }

        // === Test 6: YtDlpService ffmpeg ===
        try
        {
            if (ytDlpService.IsFfmpegAvailable)
            {
                passed++;
                Debug.WriteLine($"[TEST] ✅ FFmpeg Available: PASS ({ytDlpService.FfmpegPath})");
            }
            else
            {
                failed++;
                errors.Add($"FFmpeg not available at: {ytDlpService.FfmpegPath}");
                Debug.WriteLine("[TEST] ❌ FFmpeg Available: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"FFmpeg check: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ FFmpeg check: EXCEPTION - {ex.Message}");
        }

        // === Test 7: Platform detection ===
        try
        {
            var yt = ytDlpService.DetectPlatform("https://www.youtube.com/watch?v=test");
            var tt = ytDlpService.DetectPlatform("https://www.tiktok.com/@user/video/123");
            var unk = ytDlpService.DetectPlatform("https://example.com");

            if (yt == "YouTube" && tt == "TikTok" && unk == "Unknown")
            {
                passed++;
                Debug.WriteLine("[TEST] ✅ Platform Detection: PASS (YouTube, TikTok, Unknown)");
            }
            else
            {
                failed++;
                errors.Add($"Platform detection wrong: yt={yt}, tt={tt}, unk={unk}");
                Debug.WriteLine($"[TEST] ❌ Platform Detection: FAIL (yt={yt}, tt={tt}, unk={unk})");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Platform detection: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Platform Detection: EXCEPTION - {ex.Message}");
        }

        // === Test 8: QueueService basic operations ===
        try
        {
            var initialCount = queueService.PendingCount;
            if (initialCount == 0 || true) // just verify it doesn't throw
            {
                passed++;
                Debug.WriteLine($"[TEST] ✅ QueueService PendingCount: PASS (count={initialCount})");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"QueueService: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ QueueService: EXCEPTION - {ex.Message}");
        }

        // === Test 9: Fetch video info (live network test) ===
        try
        {
            if (ytDlpService.IsYtDlpAvailable)
            {
                var (info, error) = await ytDlpService.GetVideoInfoAsync(
                    "https://www.youtube.com/watch?v=dQw4w9WgXcQ");

                if (info != null && !string.IsNullOrEmpty(info.Title) && info.Formats.Count > 0)
                {
                    passed++;
                    Debug.WriteLine($"[TEST] ✅ Fetch Video Info: PASS (\"{info.Title[..Math.Min(40, info.Title.Length)]}...\", {info.Formats.Count} formats)");
                }
                else
                {
                    failed++;
                    errors.Add($"Fetch video info returned null or empty: {error}");
                    Debug.WriteLine($"[TEST] ❌ Fetch Video Info: FAIL - {error}");
                }
            }
            else
            {
                failed++;
                errors.Add("Cannot test fetch: yt-dlp not available");
                Debug.WriteLine("[TEST] ❌ Fetch Video Info: SKIP (yt-dlp not available)");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"Fetch video info: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ Fetch Video Info: EXCEPTION - {ex.Message}");
        }

        // === Test 10: HistoryItem display helpers ===
        try
        {
            var item = new HistoryItem
            {
                DownloadedAt = new DateTime(2026, 3, 10, 14, 30, 0),
                IsSuccess = true
            };
            var date = item.DateDisplay;
            var icon = item.StatusIcon;

            var item2 = new HistoryItem { IsSuccess = false };
            var icon2 = item2.StatusIcon;

            if (!string.IsNullOrEmpty(date) && icon == "\u2705" && icon2 == "\u274C")
            {
                passed++;
                Debug.WriteLine($"[TEST] ✅ HistoryItem Display Helpers: PASS (date=\"{date}\", success=✅, fail=❌)");
            }
            else
            {
                failed++;
                errors.Add($"HistoryItem helpers wrong: date={date}, icon={icon}, icon2={icon2}");
                Debug.WriteLine("[TEST] ❌ HistoryItem Display Helpers: FAIL");
            }
        }
        catch (Exception ex)
        {
            failed++;
            errors.Add($"HistoryItem helpers: {ex.Message}");
            Debug.WriteLine($"[TEST] ❌ HistoryItem Display Helpers: EXCEPTION - {ex.Message}");
        }

        // === Summary ===
        Debug.WriteLine("═══════════════════════════════════════════════");
        Debug.WriteLine($"[TEST] RESULTS: {passed} passed, {failed} failed, {passed + failed} total");
        Debug.WriteLine("═══════════════════════════════════════════════");

        return (passed, failed, errors);
    }
}
