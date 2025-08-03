using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoConverter.Helpers;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VideoConverter.Services;
public class VideoProcessingService
{
    private readonly string _uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
    private readonly string _outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "merged");

    public VideoProcessingService()
    {
        if (!Directory.Exists(_uploadDirectory))
            Directory.CreateDirectory(_uploadDirectory);

        if (!Directory.Exists(_outputDirectory))
            Directory.CreateDirectory(_outputDirectory);
    }

    public async Task<string> PrepareVideoWithPadding(string inputPath)
    {
        string outputPath = Path.Combine(_outputDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}_padded.mp4");

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
        var videoStream = mediaInfo.VideoStreams.First();
        int width = videoStream.Width;
        int height = videoStream.Height;

        string filter;

        if (width < height)
        {
            // Portrait: Rotate and pad
            filter = "transpose=1,scale=-2:1080,pad=1920:1080:(ow-iw)/2:(oh-ih)/2";
        }
        else
        {
            // Landscape: Scale to fit and pad if needed
            filter = "scale=-2:1080,pad=1920:1080:(ow-iw)/2:(oh-ih)/2";
        }

        var conversion = FFmpeg.Conversions.New()
            .AddParameter($"-i \"{inputPath}\"", ParameterPosition.PreInput)
            .AddParameter($"-vf \"{filter}\"")
            .AddParameter("-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p")
            .AddParameter("-c:a aac -strict experimental")
            .SetOutput(outputPath)
            .SetOverwriteOutput(true);

        await conversion.Start();

        return outputPath;
    }

    public async Task<string> MergeVideos(List<string> inputFilePaths)
    {
        var paddedFiles = new List<string>();

        foreach (var file in inputFilePaths)
        {
            string padded = await PrepareVideoWithPadding(file);
            paddedFiles.Add(padded);
        }

        string concatFilePath = Path.Combine(_outputDirectory, $"concat_list.txt");
        await File.WriteAllLinesAsync(concatFilePath, paddedFiles.Select(p => $"file '{p.Replace("\\", "/")}'"));

        string outputMergedPath = Path.Combine(_outputDirectory, $"merged_{DateTime.Now:yyyyMMddHHmmss}.mp4");

        var conversion = FFmpeg.Conversions.New()
            .AddParameter($"-f concat -safe 0 -i \"{concatFilePath}\"", ParameterPosition.PreInput)
            .AddParameter("-c copy")
            .SetOutput(outputMergedPath)
            .SetOverwriteOutput(true);

        await conversion.Start();

        return outputMergedPath;
    }

    public List<string> GetUploadedFiles()
    {
        return Directory.GetFiles(_uploadDirectory)
                        .Where(f => f.EndsWith(".mp4") || f.EndsWith(".mov") || f.EndsWith(".mkv"))
                        .ToList();
    }
    public List<string> GetUploadedImageFiles()
    {
        HeicConverterHelper.ConvertHeicImages(_uploadDirectory);
        return Directory.GetFiles(_uploadDirectory)
                        .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                                    )
                        .ToList();
    }
}
