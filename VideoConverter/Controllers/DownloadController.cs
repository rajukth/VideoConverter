using Microsoft.AspNetCore.Mvc;
using VideoConverter.Models;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace VideoConverter.Controllers;

public class DownloadController : Controller
{
    private readonly YoutubeClient _youtubeClient;

    public DownloadController()
    {
        _youtubeClient = new YoutubeClient();
    }

    public IActionResult Index()
    {
        return View(new DownloadRequest());
    }

    [HttpPost]
    public async Task<IActionResult> Download(DownloadRequest request)
    {
        if (!ModelState.IsValid)
            return View("Index", request);

        try
        {
            // Get video metadata
            var video = await _youtubeClient.Videos.GetAsync(request.VideoUrl);
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(video.Id);

            // Get the appropriate stream based on requested format
            var streamInfo = request.Format.ToLower() switch
            {
                "mp4" => streamManifest.GetMuxedStreams().TryGetWithHighestVideoQuality(),
                "webm" => streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate(),
                _ => throw new NotSupportedException($"Format '{request.Format}' is not supported.")
            };
            if (streamInfo == null)
            {
                ModelState.AddModelError("", "No suitable stream found for the requested format.");
                return View("Index", request);
            }

            // Get the stream
            var stream = await _youtubeClient.Videos.Streams.GetAsync(streamInfo);

            // Generate safe filename
            var fileName = $"{SanitizeFileName(video.Title)}.{streamInfo.Container}";

            return File(stream, $"video/{streamInfo.Container}", fileName);
        }
        catch
        {
            ModelState.AddModelError("VideoUrl", "Failed to download the video. Please check the URL.");
            return View("Index", request);
        }
    }
    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(fileName
                .Where(ch => !invalidChars.Contains(ch))
                .ToArray())
            .Replace(" ", "_");
    }
}