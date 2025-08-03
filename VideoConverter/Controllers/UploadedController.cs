using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using VideoConverter.Hubs;
using VideoConverter.Services;
using Xabe.FFmpeg;

namespace VideoConverter.Controllers;

public class UploadedController : Controller
{
    private readonly VideoProcessingService _videoService;
    private readonly string _uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
    private readonly string _outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "converted");
    private readonly string _audioDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets","audio");
    private readonly IHubContext<ProgressHub> _hubContext;
    private readonly IWebHostEnvironment _env;
    string defaultAudioPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets","audio", "default-audio.mp3");

    public UploadedController(IWebHostEnvironment env, IHubContext<ProgressHub> hubContext)
    {
        _videoService = new VideoProcessingService();
        _env = env;
        _hubContext = hubContext;
        Directory.CreateDirectory(_outputDir);
        Directory.CreateDirectory(_uploadDir);
        Directory.CreateDirectory(_audioDir);
    }
    [HttpGet]
    public IActionResult Index()
    {
        var files = _videoService.GetUploadedFiles().Select(Path.GetFileName).ToList();
        return View(files);
    }
    [HttpGet]
    public IActionResult ImageMerge()
    {
        var files = _videoService.GetUploadedImageFiles().Select(Path.GetFileName).ToList();
        return View(files);
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(List<IFormFile> files)
    {
        if (files == null || !files.Any())
        {
            TempData["Error"] = "No files uploaded.";
            return RedirectToAction("Index");
        }
        Directory.CreateDirectory(_uploadDir);

        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var filePath = Path.Combine(_uploadDir, Path.GetFileName(file.FileName));
                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }
        }

        TempData["Success"] = "Files uploaded successfully.";
        return RedirectToAction("Index");
    }

   [HttpPost]
public async Task<IActionResult> Merge([FromBody] List<string> selectedFiles)
{
    if (selectedFiles == null || selectedFiles.Count < 2)
        return BadRequest("Select at least two files.");

    string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "merged");
    Directory.CreateDirectory(outputDir);

    List<string> converted = new();
    for (int i = 0; i < selectedFiles.Count; i++)
    {
        string relPath = selectedFiles[i];
        string fullPath = Path.Combine(_uploadDir, relPath);
        string output = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(fullPath) + "_converted.mp4");

        await _hubContext.Clients.All.SendAsync("UpdateProgress", new
        {
            message = $"Converting ({(i+1)} of {selectedFiles.Count}) {Path.GetFileName(relPath)}...",
            progress = (i + 1) * 100 / selectedFiles.Count
        });
        if (System.IO.File.Exists(output))
        {
            converted.Add(output);
            continue;
        }
        var mediaInfo = await FFmpeg.GetMediaInfo(fullPath);
        var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
        var audioStream = mediaInfo.AudioStreams.FirstOrDefault();

        var conversion = FFmpeg.Conversions.New();

        if (videoStream != null)
            conversion.AddStream(videoStream.SetCodec(VideoCodec.h264));

        if (audioStream != null)
            conversion.AddStream(audioStream.SetCodec(AudioCodec.aac)); // or .SetCodec(AudioCodec.copy)
        else
        {
            // No audio: add default audio file as input
            conversion.AddParameter($"-i \"{defaultAudioPath}\"", ParameterPosition.PreInput);

            // Map video stream from main input (index 0) and audio from default audio (index 1)
            conversion.AddParameter("-map 0:v:0 -map 1:a:0", ParameterPosition.PreInput);

            // Set audio codec to aac
            conversion.AddParameter("-c:a aac", ParameterPosition.PostInput);
        }
        conversion.SetOutput(output)
            .SetOverwriteOutput(true);

        await conversion.Start();
        converted.Add(output);
        System.IO.File.Delete(fullPath); // Clean original
    }

    // Merge
    string concatFile = Path.Combine(outputDir, "concat.txt");
    await System.IO.File.WriteAllLinesAsync(concatFile, converted.Select(p => $"file '{p.Replace("\\", "/")}'"));

    string mergedFileName = $"merged_{DateTime.Now.Ticks}.mp4";
    string mergedFile = Path.Combine(outputDir, mergedFileName);

    await _hubContext.Clients.All.SendAsync("UpdateProgress", new
    {
        message = "Merging videos...",
        progress = 100
    });

    var merge = FFmpeg.Conversions.New()
        .AddParameter($"-f concat -safe 0 -i \"{concatFile}\" -c copy \"{mergedFile}\"", ParameterPosition.PreInput);
    await merge.Start();

    // Cleanup
    foreach (var file in converted) System.IO.File.Delete(file);
    System.IO.File.Delete(concatFile);
    await _hubContext.Clients.All.SendAsync("UpdateProgress", new
    {
        message = "Merged successfully!",
        progress = 100,
        file = $"/merged/{mergedFileName}"
    });
    return Ok();
}
[HttpPost]
public IActionResult DeleteMergedFile([FromBody] string relativePath)
{
    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath.TrimStart('/'));
    if (System.IO.File.Exists(fullPath))
        System.IO.File.Delete(fullPath);

    return Ok();
}

    
[HttpPost]
public async Task<IActionResult> MergeSelectedImages([FromForm] List<string> selectedImages)
{
    if (selectedImages == null || !selectedImages.Any())
        return BadRequest("No images selected.");

    string uploadDir = Path.Combine(_env.WebRootPath, "uploads");
    string outputDir = Path.Combine(_env.WebRootPath, "converted");
    Directory.CreateDirectory(outputDir);

    // Step 1: Create a temporary file list for FFmpeg
    string listFilePath = Path.Combine(outputDir, "images.txt");
    var listFileContent = selectedImages.Select(f =>
        $"file '{Path.Combine(uploadDir, f).Replace("\\", "/")}'\nduration 2").ToList(); // each image for 2 seconds
    // Add last image again (FFmpeg concat bug workaround)
    listFileContent.Add($"file '{Path.Combine(uploadDir, selectedImages.Last()).Replace("\\", "/")}'");

    System.IO.File.WriteAllLines(listFilePath, listFileContent);

    string tempVideoPath = Path.Combine(outputDir, $"slideshow_{DateTime.Now.Ticks}.mp4");

    // Step 2: Generate video from images
    var conversion = FFmpeg.Conversions.New()
        .AddParameter($"-f concat -safe 0 -i \"{listFilePath}\" -vf \"scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2:black,format=yuv420p,transpose=0\" -r 1 -fps_mode cfr -pix_fmt yuv420p \"{tempVideoPath}\"", ParameterPosition.PreInput);


    await conversion.Start();

    // Step 3: Add background audio (shortest match)
    string audioDir = Path.Combine(_env.WebRootPath, "assets", "audio");
    var audioFiles = Directory.GetFiles(audioDir, "*.mp3");
    if (!audioFiles.Any())
        return BadRequest("No audio file found in assets/audio.");

    var audioPath = audioFiles[new Random().Next(audioFiles.Length)];

    string finalVideoPath = Path.Combine(outputDir, $"image_merged_{DateTime.Now.Ticks}.mp4");

    var finalMerge = FFmpeg.Conversions.New()
        .AddParameter($"-i \"{tempVideoPath}\" -i \"{audioPath}\" -shortest -c:v copy -c:a aac \"{finalVideoPath}\"", ParameterPosition.PreInput);

    await finalMerge.Start();

    // Step 4: Clean up temp files
    System.IO.File.Delete(tempVideoPath);
    System.IO.File.Delete(listFilePath);

    // Step 5: Return downloadable link
    return Ok(new { url = $"/converted/{Path.GetFileName(finalVideoPath)}" });
}


}
