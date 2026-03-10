using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TubeLoad.Models;
using TubeLoad.Services;

namespace TubeLoad;

public partial class MainWindow : Window
{
    private readonly YtDlpService _ytDlpService = new();
    private readonly DatabaseService _dbService = new();
    private readonly QueueService _queueService = new();
    private readonly ObservableCollection<DownloadItemVM> _downloads = new();
    private VideoInfo? _currentVideoInfo;
    private VideoFormat? _selectedFormat;
    private Border? _selectedQualityBorder;
    private DebugWindow? _debugWindow;
    private string _outputDir;

    public MainWindow()
    {
        InitializeComponent();
        _outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TubeLoad");
        Directory.CreateDirectory(_outputDir);
        OutputDirText.Text = _outputDir;
        DownloadList.ItemsSource = _downloads;

        // Queue events
        _queueService.ProcessItem += ProcessQueueItemAsync;
        _queueService.QueueChanged += () => Dispatcher.Invoke(UpdateQueueUI);

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _dbService.Dispose();
    }

    // ==================== STARTUP ====================
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitDebugMode();
        await EnsureToolsAsync();
        RefreshHistoryBadge();
    }

    // ==================== DEBUG ====================
    private void InitDebugMode()
    {
#if DEBUG
        DebugBtn.Visibility = Visibility.Visible;
        // Auto-open diagnostic on first Debug launch
        OpenDebugWindow();
#endif
    }

    private void DebugBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenDebugWindow();
    }

    private void OpenDebugWindow()
    {
        // ป้องกันเปิดซ้ำ - ถ้ามีอยู่แล้วให้ focus
        if (_debugWindow != null && _debugWindow.IsLoaded)
        {
            _debugWindow.Activate();
            _debugWindow.Focus();
            return;
        }

        _debugWindow = new DebugWindow(_ytDlpService, _dbService, _queueService);
        _debugWindow.Owner = this;
        _debugWindow.Closed += (_, _) => _debugWindow = null;
        _debugWindow.Show();
    }

    private async Task EnsureToolsAsync()
    {
        bool needYtDlp = !_ytDlpService.IsYtDlpAvailable;
        bool needFfmpeg = !_ytDlpService.IsFfmpegAvailable;

        if (!needYtDlp && !needFfmpeg)
        {
            SetStatus("Ready - Paste a URL and click Fetch Info", StatusType.Success);
            return;
        }

        ShowLoading(true, "First-time setup: downloading required tools...");

        if (needYtDlp)
        {
            var (ok, msg) = await _ytDlpService.DownloadYtDlpAsync(
                s => Dispatcher.Invoke(() => LoadingText.Text = s));
            if (!ok)
            {
                ShowLoading(false);
                SetStatus("yt-dlp download failed!", StatusType.Error);
                MessageBox.Show(
                    $"yt-dlp download failed:\n\n{msg}\n\nPlease download yt-dlp.exe manually and place it in:\n{_ytDlpService.ToolsDir}",
                    "TubeLoad - Setup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (needFfmpeg)
        {
            var (ok, msg) = await _ytDlpService.DownloadFfmpegAsync(
                s => Dispatcher.Invoke(() => LoadingText.Text = s));
            if (!ok)
            {
                ShowLoading(false);
                SetStatus("ffmpeg download failed (video merging may not work)", StatusType.Warning);
                MessageBox.Show(
                    $"ffmpeg download failed:\n\n{msg}\n\nYou can download it manually.\nPath: {_ytDlpService.ToolsDir}",
                    "TubeLoad - Setup Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        ShowLoading(false);
        if (_ytDlpService.IsYtDlpAvailable)
            SetStatus("Ready - Paste a URL and click Fetch Info", StatusType.Success);
        else
            SetStatus("Tools not ready. Check setup.", StatusType.Error);
    }

    // ==================== TITLE BAR ====================
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }
    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeBtn_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaxBtn.Content = WindowState == WindowState.Maximized ? "\u2752" : "\u25A1";
    }

    // ==================== URL INPUT ====================
    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlTextBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

        var url = UrlTextBox.Text.Trim();
        var platform = _ytDlpService.DetectPlatform(url);
        if (platform != "Unknown")
        {
            PlatformBadge.Visibility = Visibility.Visible;
            PlatformText.Text = $"{platform} detected";
        }
        else
            PlatformBadge.Visibility = Visibility.Collapsed;
    }

    private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) FetchBtn_Click(sender, e);
    }

    private void PasteBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                UrlTextBox.Text = Clipboard.GetText().Trim();
                UrlTextBox.CaretIndex = UrlTextBox.Text.Length;
                SetStatus("URL pasted from clipboard", StatusType.Info);
            }
            else
                SetStatus("Clipboard is empty", StatusType.Warning);
        }
        catch { SetStatus("Cannot access clipboard", StatusType.Warning); }
    }

    // ==================== FETCH ====================
    private async void FetchBtn_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            SetStatus("Please enter a video URL first", StatusType.Warning);
            MessageBox.Show("Please paste a YouTube or TikTok URL first.",
                "TubeLoad", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_ytDlpService.IsYtDlpAvailable)
        {
            await EnsureToolsAsync();
            if (!_ytDlpService.IsYtDlpAvailable)
            {
                MessageBox.Show($"yt-dlp is not available.\nPlease download yt-dlp.exe to:\n{_ytDlpService.ToolsDir}",
                    "TubeLoad - Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        FetchBtn.IsEnabled = false;
        ShowLoading(true, "Fetching video information...");
        SetStatus("Fetching video info...", StatusType.Info);

        try
        {
            var (info, error) = await _ytDlpService.GetVideoInfoAsync(url,
                s => Dispatcher.Invoke(() => LoadingText.Text = s));

            if (info != null)
            {
                _currentVideoInfo = info;
                VideoTitle.Text = info.Title;
                UploaderText.Text = info.Uploader;
                DurationText.Text = info.Duration;

                try
                {
                    if (!string.IsNullOrEmpty(info.Thumbnail))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(info.Thumbnail);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        ThumbnailImage.Source = bi;
                    }
                }
                catch { }

                // Populate quality selector
                QualityList.ItemsSource = info.Formats;
                FormatCountText.Text = $"{info.Formats.Count} options available";

                // Auto-select first format (Best Quality)
                _selectedFormat = info.Formats.FirstOrDefault();
                _selectedQualityBorder = null;
                UpdateSelectedQualityDisplay();

                VideoInfoCard.Visibility = Visibility.Visible;
                SetStatus($"Ready to download: {info.Title}", StatusType.Success);

                // Highlight first item after render
                _ = Dispatcher.InvokeAsync(() => AutoSelectFirstQuality(), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                SetStatus("Failed to fetch video info", StatusType.Error);
                var shortErr = error.Length > 300 ? error[..300] + "..." : error;
                MessageBox.Show($"Could not fetch video info.\n\nError:\n{shortErr}",
                    "TubeLoad - Fetch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", StatusType.Error);
            MessageBox.Show($"Unexpected error:\n{ex.Message}",
                "TubeLoad - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            FetchBtn.IsEnabled = true;
            ShowLoading(false);
        }
    }

    // ==================== DOWNLOAD (Queue) ====================
    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentVideoInfo == null || _selectedFormat == null)
        {
            MessageBox.Show("Please fetch video info first, then select a quality.",
                "TubeLoad", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var formatId = _selectedFormat.FormatId;
        var qualityText = _selectedFormat.DisplayText;

        var vm = new DownloadItemVM
        {
            Title = _currentVideoInfo.Title,
            Url = _currentVideoInfo.Url,
            Quality = qualityText,
            Status = DownloadStatus.Waiting,
            Progress = 0
        };

        _downloads.Insert(0, vm);

        var queueItem = new QueueItem
        {
            Url = _currentVideoInfo.Url,
            FormatId = formatId,
            Title = _currentVideoInfo.Title,
            Quality = qualityText,
            Platform = _currentVideoInfo.Platform,
            Thumbnail = _currentVideoInfo.Thumbnail,
            ViewModel = vm
        };

        SwitchTab(true);
        UpdateQueueUI(); // force show download list immediately
        _queueService.Enqueue(queueItem);
        SetStatus($"Added to queue: {vm.Title} ({_queueService.TotalCount} in queue)", StatusType.Info);

        // Auto-scroll main content ลงไปที่ downloads section ให้เห็น progress
        _ = Dispatcher.InvokeAsync(() =>
        {
            // Find parent ScrollViewer and scroll to Downloads section
            var sv = FindChild<ScrollViewer>(this);
            if (sv != null)
            {
                // scroll to show download list area
                var transform = DownloadsPanel.TransformToAncestor(sv);
                var point = transform.Transform(new Point(0, 0));
                sv.ScrollToVerticalOffset(sv.VerticalOffset + point.Y - 100);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ==================== QUALITY SELECTOR ====================
    private void QualityItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoFormat fmt)
        {
            _selectedFormat = fmt;

            // Reset previous selection
            if (_selectedQualityBorder != null)
            {
                _selectedQualityBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                _selectedQualityBorder.Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
            }

            // Highlight new selection
            var border = FindChildBorder(btn);
            if (border != null)
            {
                border.BorderBrush = MakeGradient("#00B4D8", "#7C3AED");
                border.Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
                _selectedQualityBorder = border;
            }

            UpdateSelectedQualityDisplay();
        }
    }

    private void UpdateSelectedQualityDisplay()
    {
        if (_selectedFormat != null)
        {
            var sizeText = string.IsNullOrEmpty(_selectedFormat.FileSize) ? "" : $" - {_selectedFormat.FileSize}";
            SelectedQualityText.Text = $"{_selectedFormat.QualityLabel} ({_selectedFormat.FormatBadge}){sizeText}";
        }
        else
        {
            SelectedQualityText.Text = "None";
        }
    }

    private void AutoSelectFirstQuality()
    {
        // Find the first button in the QualityList and simulate selection
        var firstContainer = QualityList.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter;
        if (firstContainer != null)
        {
            var btn = FindChild<Button>(firstContainer);
            if (btn != null)
            {
                var border = FindChildBorder(btn);
                if (border != null)
                {
                    border.BorderBrush = MakeGradient("#00B4D8", "#7C3AED");
                    border.Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
                    _selectedQualityBorder = border;
                }
            }
        }
    }

    private static Border? FindChildBorder(Button btn)
    {
        if (btn.Content is Border b) return b;
        // Find in visual tree
        return FindChild<Border>(btn);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target) return target;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private async Task ProcessQueueItemAsync(QueueItem item)
    {
        var vm = item.ViewModel;
        Dispatcher.Invoke(() =>
        {
            vm.Status = DownloadStatus.Downloading;
            SetStatus($"Downloading: {item.Title}", StatusType.Info);
        });

        try
        {
            await _ytDlpService.DownloadVideoAsync(
                item.Url, _outputDir, item.FormatId,
                progress => Dispatcher.Invoke(() => vm.Progress = progress),
                speed => Dispatcher.Invoke(() => vm.Speed = speed),
                eta => Dispatcher.Invoke(() => vm.ETA = eta),
                status => Dispatcher.Invoke(() =>
                {
                    vm.Status = status;
                    if (status == DownloadStatus.Completed)
                    {
                        SetStatus($"Completed: {item.Title}", StatusType.Success);
                        _dbService.AddHistory(new HistoryItem
                        {
                            Title = item.Title, Url = item.Url,
                            Quality = item.Quality, Platform = item.Platform,
                            Thumbnail = item.Thumbnail, FilePath = _outputDir,
                            IsSuccess = true, DownloadedAt = DateTime.Now
                        });
                        RefreshHistoryBadge();
                    }
                    else if (status == DownloadStatus.Failed)
                    {
                        SetStatus($"Failed: {item.Title}", StatusType.Error);
                        _dbService.AddHistory(new HistoryItem
                        {
                            Title = item.Title, Url = item.Url,
                            Quality = item.Quality, Platform = item.Platform,
                            IsSuccess = false, DownloadedAt = DateTime.Now
                        });
                        RefreshHistoryBadge();
                    }
                }),
                error => Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        var shortErr = error.Length > 300 ? error[..300] + "..." : error;
                        MessageBox.Show($"Download failed:\n\n{shortErr}",
                            "TubeLoad - Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }),
                item.Cts.Token
            );
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                vm.Status = DownloadStatus.Failed;
                SetStatus($"Error: {ex.Message}", StatusType.Error);
            });
        }
    }

    // ==================== TABS ====================
    private void TabDownloads_Click(object sender, RoutedEventArgs e) => SwitchTab(true);
    private void TabHistory_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(false);
        LoadHistory();
    }

    private void SwitchTab(bool showDownloads)
    {
        DownloadsPanel.Visibility = showDownloads ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = showDownloads ? Visibility.Collapsed : Visibility.Visible;

        TabDownloads.Foreground = new SolidColorBrush(showDownloads ? Color.FromRgb(230, 237, 243) : Color.FromRgb(74, 85, 104));
        TabDownloads.FontWeight = showDownloads ? FontWeights.SemiBold : FontWeights.Normal;
        TabDownloads.BorderBrush = showDownloads ? MakeGradient("#00B4D8", "#7C3AED") : Brushes.Transparent;

        TabHistory.Foreground = new SolidColorBrush(!showDownloads ? Color.FromRgb(230, 237, 243) : Color.FromRgb(74, 85, 104));
        TabHistory.FontWeight = !showDownloads ? FontWeights.SemiBold : FontWeights.Normal;
        TabHistory.BorderBrush = !showDownloads ? MakeGradient("#00B4D8", "#7C3AED") : Brushes.Transparent;
    }

    // ==================== HISTORY ====================
    private void LoadHistory(string? keyword = null)
    {
        var items = string.IsNullOrWhiteSpace(keyword)
            ? _dbService.GetHistory()
            : _dbService.SearchHistory(keyword);

        HistoryList.ItemsSource = items;
        bool hasItems = items.Count > 0;
        HistoryEmpty.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        HistoryContent.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHistoryBadge()
    {
        var count = _dbService.GetHistoryCount();
        HistoryCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryCountText.Text = count.ToString();
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => LoadHistory(HistorySearchBox.Text.Trim());

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear all download history?", "TubeLoad - Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _dbService.ClearAllHistory();
            LoadHistory();
            RefreshHistoryBadge();
            SetStatus("History cleared", StatusType.Info);
        }
    }

    private void RedownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
        {
            UrlTextBox.Text = url;
            SwitchTab(true);
            SetStatus("URL loaded from history. Click Fetch Info to start.", StatusType.Info);
        }
    }

    // ==================== BROWSE / OPEN ====================
    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Download Folder", InitialDirectory = _outputDir };
        if (dialog.ShowDialog() == true)
        {
            _outputDir = dialog.FolderName;
            OutputDirText.Text = _outputDir;
        }
    }

    private void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(_outputDir); Process.Start("explorer.exe", _outputDir); }
        catch (Exception ex) { MessageBox.Show($"Cannot open folder:\n{ex.Message}", "TubeLoad"); }
    }

    // ==================== HELPERS ====================
    private void ShowLoading(bool show, string text = "Loading...")
    {
        LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Text = text;
    }

    private enum StatusType { Info, Success, Warning, Error }
    private void SetStatus(string text, StatusType type)
    {
        StatusText.Text = text;
        StatusDot.Background = type switch
        {
            StatusType.Success => new SolidColorBrush(Color.FromRgb(0, 230, 118)),
            StatusType.Warning => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
            StatusType.Error => new SolidColorBrush(Color.FromRgb(255, 82, 82)),
            _ => new SolidColorBrush(Color.FromRgb(0, 180, 216))
        };
        StatusText.Foreground = type switch
        {
            StatusType.Error => new SolidColorBrush(Color.FromRgb(255, 120, 120)),
            StatusType.Warning => new SolidColorBrush(Color.FromRgb(255, 210, 100)),
            StatusType.Success => new SolidColorBrush(Color.FromRgb(0, 230, 118)),
            _ => new SolidColorBrush(Color.FromRgb(139, 148, 158))
        };
    }

    private void UpdateQueueUI()
    {
        bool hasItems = _downloads.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        DownloadList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        var qc = _queueService.TotalCount;
        QueueCountBadge.Visibility = qc > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueCountText.Text = qc.ToString();
    }

    private static LinearGradientBrush MakeGradient(string c1, string c2) =>
        new((Color)ColorConverter.ConvertFromString(c1), (Color)ColorConverter.ConvertFromString(c2), 0);
}

// ==================== VIEWMODEL ====================
public class DownloadItemVM : INotifyPropertyChanged
{
    private string _title = "";
    private string _url = "";
    private string _quality = "";
    private double _progress;
    private string _speed = "";
    private string _eta = "";
    private DownloadStatus _status = DownloadStatus.Waiting;

    public string Title { get => _title; set { _title = value; N(); } }
    public string Url { get => _url; set { _url = value; N(); } }
    public string Quality { get => _quality; set { _quality = value; N(); } }

    public double Progress
    {
        get => _progress;
        set { _progress = value; N(); N(nameof(ProgressText)); N(nameof(ShowProgress)); N(nameof(StatusText)); }
    }
    public string Speed
    {
        get => _speed;
        set { _speed = value; N(); N(nameof(SpeedDisplay)); }
    }
    public string ETA
    {
        get => _eta;
        set { _eta = value; N(); N(nameof(EtaDisplay)); }
    }
    public DownloadStatus Status
    {
        get => _status;
        set { _status = value; N(); N(nameof(StatusText)); N(nameof(ShowProgress)); }
    }

    public bool ShowProgress => Status == DownloadStatus.Downloading;
    public string ProgressText => $"{Progress:F0}%";
    public string SpeedDisplay => !string.IsNullOrEmpty(Speed) ? $"Speed: {Speed}" : "";
    public string EtaDisplay => !string.IsNullOrEmpty(ETA) ? $"ETA: {ETA}" : "";

    public string StatusText => Status switch
    {
        DownloadStatus.Waiting => "In queue, waiting...",
        DownloadStatus.Downloading => $"Downloading {Progress:F1}%",
        DownloadStatus.Merging => "Merging audio & video...",
        DownloadStatus.Completed => "Download completed!",
        DownloadStatus.Failed => "Download failed",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void N([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
