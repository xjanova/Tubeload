namespace TubeLoad.Models;

public class VideoFormat
{
    public string FormatId { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Resolution { get; set; } = "";
    public string FileSize { get; set; } = "";
    public string Note { get; set; } = "";
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public int Height { get; set; }

    public string DisplayText => $"{Resolution}  ({Extension.ToUpper()})  {FileSize}";

    // Visual selector helpers
    public string Icon => FormatId switch
    {
        "best" => "\u2B50",    // star
        "audio" => "\u266B",   // music note
        _ => "\u25B6"          // play
    };

    public string QualityLabel => FormatId switch
    {
        "best" => "Best Quality",
        "audio" => "Audio Only",
        _ => Resolution
    };

    public string FormatBadge => Extension.ToUpper();

    public string TypeLabel => (HasVideo, HasAudio) switch
    {
        (true, true) => "Video + Audio",
        (true, false) => "Video Only",
        (false, true) => "Audio Only",
        _ => ""
    };

    public string QualityTag => Height switch
    {
        >= 2160 => "4K Ultra HD",
        >= 1440 => "2K QHD",
        >= 1080 => "Full HD",
        >= 720 => "HD",
        >= 480 => "SD",
        >= 360 => "Low",
        > 0 => "Basic",
        _ => FormatId == "best" ? "Auto Select" : "MP3 128kbps"
    };
}

public class VideoInfo
{
    public string Title { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public string Duration { get; set; } = "";
    public string Uploader { get; set; } = "";
    public string Url { get; set; } = "";
    public string Platform { get; set; } = "";
    public List<VideoFormat> Formats { get; set; } = new();
}

public enum DownloadStatus
{
    Waiting,
    Downloading,
    Merging,
    Completed,
    Failed
}

public class DownloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Quality { get; set; } = "";
    public double Progress { get; set; }
    public string Speed { get; set; } = "";
    public string ETA { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DownloadStatus Status { get; set; } = DownloadStatus.Waiting;
    public string StatusText => Status switch
    {
        DownloadStatus.Waiting => "Waiting...",
        DownloadStatus.Downloading => $"{Progress:F1}%  |  {Speed}  |  ETA: {ETA}",
        DownloadStatus.Merging => "Merging audio & video...",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Failed => "Failed",
        _ => ""
    };
}
