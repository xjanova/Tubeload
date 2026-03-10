using System.Collections.ObjectModel;
using TubeLoad.Models;

namespace TubeLoad.Services;

public class QueueItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Url { get; set; } = "";
    public string FormatId { get; set; } = "best";
    public string Title { get; set; } = "";
    public string Quality { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public DownloadItemVM ViewModel { get; set; } = null!;
    public CancellationTokenSource Cts { get; set; } = new();
}

public class QueueService
{
    private readonly Queue<QueueItem> _pendingQueue = new();
    private readonly object _lock = new();
    private bool _isProcessing;
    private QueueItem? _currentItem;

    public event Action? QueueChanged;
    public event Func<QueueItem, Task>? ProcessItem;

    public int PendingCount { get { lock (_lock) return _pendingQueue.Count; } }
    public int TotalCount { get { lock (_lock) return _pendingQueue.Count + (_isProcessing ? 1 : 0); } }
    public bool IsProcessing { get { lock (_lock) return _isProcessing; } }
    public QueueItem? CurrentItem => _currentItem;

    public void Enqueue(QueueItem item)
    {
        lock (_lock)
        {
            _pendingQueue.Enqueue(item);
        }
        QueueChanged?.Invoke();
        _ = ProcessQueueAsync();
    }

    public void CancelCurrent()
    {
        _currentItem?.Cts.Cancel();
    }

    public void ClearPending()
    {
        lock (_lock)
        {
            _pendingQueue.Clear();
        }
        QueueChanged?.Invoke();
    }

    private async Task ProcessQueueAsync()
    {
        lock (_lock)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

        while (true)
        {
            QueueItem? item;
            lock (_lock)
            {
                if (_pendingQueue.Count == 0)
                {
                    _isProcessing = false;
                    _currentItem = null;
                    QueueChanged?.Invoke();
                    return;
                }
                item = _pendingQueue.Dequeue();
            }

            _currentItem = item;
            QueueChanged?.Invoke();

            try
            {
                if (ProcessItem != null)
                {
                    await ProcessItem.Invoke(item);
                }
            }
            catch
            {
                item.ViewModel.Status = DownloadStatus.Failed;
            }
        }
    }
}
