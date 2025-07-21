namespace VideoConverter.Models;

public class DownloadRequest
{
    public string VideoUrl { get; set; }
    public string Format { get; set; } = "mp4"; // Default format
}