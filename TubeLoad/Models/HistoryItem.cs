namespace TubeLoad.Models;

public class HistoryItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Quality { get; set; } = "";
    public string Platform { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string FileSize { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public DateTime DownloadedAt { get; set; } = DateTime.Now;
    public bool IsSuccess { get; set; } = true;

    // Display helpers
    public string DateDisplay => DownloadedAt.ToString("dd MMM yyyy  HH:mm");
    public string StatusIcon => IsSuccess ? "\u2705" : "\u274C";
}
