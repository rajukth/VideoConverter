using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using VideoConverter.Models;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VideoConverter.Controllers
{
    public class VobController : Controller
    {
        private readonly string _outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "converted");
        private readonly string _uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        private static readonly ConcurrentDictionary<string, ProcessingStatus> _processingStatuses = new ConcurrentDictionary<string, ProcessingStatus>();

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult ConvertWithImage()
        {
            if (!Directory.Exists(_uploadDirectory))
                Directory.CreateDirectory(_uploadDirectory);

            var files = Directory
                .GetFiles(_uploadDirectory)
                .Select(Path.GetFileName)
                .ToList();

            return View(files);
        }
        [HttpPost]
        public async Task<IActionResult> ProcessConvertWithImageAsync(VideoProcessingModel model)
        {
            var taskId = Guid.NewGuid().ToString();
            Directory.CreateDirectory(_uploadDirectory);
            List<string> uploadedFilePaths = new List<string>();
            foreach (var formFile in model.Files)
            {
                if (formFile.Length > 0)
                {
                    var filePath = Path.Combine(_uploadDirectory, Path.GetFileName(formFile.FileName));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await formFile.CopyToAsync(stream);
                    }
                    uploadedFilePaths.Add(filePath);
                }
            }
            var status = new ProcessingStatus();
            _processingStatuses[taskId] = status;
            return Json(new { message = "Processing started.", taskId });
        }

        [HttpPost]
        public async Task<IActionResult> ProcessVideos(VideoProcessingModel model)
        {
            var taskId = Guid.NewGuid().ToString();
            Directory.CreateDirectory(_outputDirectory);

            List<string> uploadedFilePaths = new List<string>();
            foreach (var formFile in model.Files)
            {
                if (formFile.Length > 0)
                {
                    var filePath = Path.Combine(_outputDirectory, Path.GetFileName(formFile.FileName));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await formFile.CopyToAsync(stream);
                    }
                    uploadedFilePaths.Add(filePath);
                }
            }
            var status = new ProcessingStatus();
            _processingStatuses[taskId] = status;

            _ = Task.Run(async () =>
            {
                List<string> convertedFiles = new List<string>();
                string zipFilePath = string.Empty;
                string mergedFilePath = string.Empty;
                try
                {
                    switch (model.Action)
                    {
                        case "ConvertOnly":
                            foreach (var inputFile in uploadedFilePaths)
                            {
                                string outputFilePath = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".mp4");
                                await ConvertVobToMp4(inputFile, outputFilePath, taskId);
                                convertedFiles.Add(outputFilePath);
                            }
                            if (convertedFiles.Count == 1)
                            {
                                status.MergedFilePath = convertedFiles[0];
                            }
                            else
                            {
                                zipFilePath = CreateZipFile(convertedFiles, taskId);
                                status.ZipFilePath = zipFilePath;
                            }
                            status.IsCompleted = true;
                            break;

                        case "ConvertAndMerge":
                            foreach (var inputFile in uploadedFilePaths)
                            {
                                string outputFilePath = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".mp4");
                                await ConvertVobToMp4(inputFile, outputFilePath, taskId);
                                convertedFiles.Add(outputFilePath);
                            }
                            await MergeAfterConversion(taskId, convertedFiles);

                            zipFilePath = CreateZipFile(convertedFiles, taskId);
                            mergedFilePath = _processingStatuses[taskId].MergedFilePath;
                            status.ZipFilePath = zipFilePath;
                            status.MergedFilePath = mergedFilePath;
                            _processingStatuses[taskId].ProgressText = "Cmpressing to zip completed";
                            status.IsCompleted = true;
                            break;

                        case "MergeOnly":
                            string finalOutputFilePath = Path.Combine(_outputDirectory, $"out{Guid.NewGuid()}.mp4");
                            await MergeMp4Files(uploadedFilePaths, finalOutputFilePath, taskId);
                            status.MergedFilePath = finalOutputFilePath;
                            status.IsCompleted = true;
                            break;

                        default:
                            status.IsCompleted = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"An error occurred during processing: {ex.Message}");
                    status.IsCompleted = true;
                }
            });

            return Json(new { message = "Processing started.", taskId });
        }

        [HttpGet]
        public IActionResult GetProgress(string taskId)
        {
            if (_processingStatuses.TryGetValue(taskId, out var status))
            {
                return Json(new
                {
                    progressText = status.ProgressText,
                    totalTime = status.TotalTime,
                    estimatedTime = status.EstimatedTime,
                    percentage = status.Percentage,
                    isCompleted = status.IsCompleted,
                    zipFilePath = status.ZipFilePath != null ? Url.Content($"~/converted/{Path.GetFileName(status.ZipFilePath)}") : null,
                    mergedFilePath = status.MergedFilePath != null ? Url.Content($"~/converted/{Path.GetFileName(status.MergedFilePath)}") : null

                });
            }

            return NotFound();
        }

        private async Task ConvertVobToMp4(string inputFilePath, string outputFilePath, string taskId)
        {
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var conversion = FFmpeg.Conversions.New()
                    .AddParameter($"-fflags +genpts", ParameterPosition.PreInput)
                    .AddParameter($"-i \"{inputFilePath}\"", ParameterPosition.PreInput)
                    .SetOutput(outputFilePath)
                    .AddParameter("-c:v libx264")
                    .AddParameter("-c:a aac")
                    .AddParameter("-preset veryfast")
                    .AddParameter("-movflags +faststart")
                    .AddParameter("-avoid_negative_ts make_zero");

                conversion.OnProgress += (sender, args) =>
                {
                    double percentComplete = args.Percent == 0 ? 1 : args.Percent;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * 100);
                    var status = _processingStatuses[taskId];
                    status.TotalTime = (long)estimatedTotal.TotalMilliseconds;
                    status.EstimatedTime = (long)(estimatedTotal - elapsed).TotalMilliseconds;
                    status.Percentage = percentComplete;
                    status.ProgressText = $"Converting {Path.GetFileName(inputFilePath)}: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated: {estimatedTotal - elapsed:hh\\:mm\\:ss}";
                    Debug.WriteLine(status.ProgressText);
                };

                conversion.OnDataReceived += (sender, args) =>
                {
                    Debug.WriteLine(args.Data);
                };

                await conversion.Start();

                DeleteFile(inputFilePath);
                stopwatch.Stop();
                _processingStatuses[taskId].ProgressText = $"Conversion of {Path.GetFileName(inputFilePath)} completed!";
                Debug.WriteLine(_processingStatuses[taskId].ProgressText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during conversion of {Path.GetFileName(inputFilePath)}: {ex.Message}");
                _processingStatuses[taskId].ProgressText = $"Failed to convert {Path.GetFileName(inputFilePath)}.";
            }
        }

        [HttpGet]
        public IActionResult UploadedFiles()
        {
            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadPath))
            {
                return Json(new List<string>()); // empty list if folder doesn't exist
            }

            var allowedExtensions = new[] { ".vob", ".avi", ".mov", ".mkv", ".mp4",".mpg",".mpeg",".m2p" }; // adjust as needed
            var files = Directory.GetFiles(uploadPath)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .Select(f => Path.GetFileName(f))
                .ToList();

            return View(files);
        }
        [HttpPost]
        public async Task<IActionResult> ConvertSelected([FromBody] FileSelectionModel model)
        {
            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

            // Validate files exist and get full paths
            List<string> selectedFilePaths = new List<string>();
            foreach (var fileName in model.FileNames)
            {
                var filePath = Path.Combine(uploadPath, fileName);
                if (!System.IO.File.Exists(filePath))
                {
                    return BadRequest($"File {fileName} does not exist.");
                }
                selectedFilePaths.Add(filePath);
            }

            var taskId = Guid.NewGuid().ToString();
            Directory.CreateDirectory(_outputDirectory);

            var status = new ProcessingStatus();
            _processingStatuses[taskId] = status;

            _ = Task.Run(async () =>
            {
                List<string> convertedFiles = new List<string>();
                string zipFilePath = string.Empty;
                string mergedFilePath = string.Empty;

                try
                {
                    switch (model.Action)
                    {
                        case "ConvertOnly":
                            foreach (var inputFile in selectedFilePaths)
                            {
                                string outputFilePath = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".mp4");
                                await ConvertVobToMp4(inputFile, outputFilePath, taskId);
                                convertedFiles.Add(outputFilePath);
                            }
                            if (convertedFiles.Count == 1)
                            {
                                status.MergedFilePath = convertedFiles[0];
                            }
                            else
                            {
                                zipFilePath = CreateZipFile(convertedFiles, taskId);
                                status.ZipFilePath = zipFilePath;
                            }
                            status.IsCompleted = true;
                            break;

                        case "ConvertAndMerge":
                            // Normalize all videos to 1920x1080 landscape
                            foreach (var inputFile in selectedFilePaths)
                            {
                                var normalized = await NormalizeToLandscape(inputFile, taskId);
                                convertedFiles.Add(normalized);
                            }

                            string finalMergedPath = Path.Combine(_outputDirectory, $"merged_{taskId}.mp4");
                            await MergeNormalizedVideos(convertedFiles, finalMergedPath, taskId);

                            zipFilePath = CreateZipFile(convertedFiles, taskId); // Optional
                            status.MergedFilePath = finalMergedPath;
                            status.ZipFilePath = zipFilePath;
                            _processingStatuses[taskId].ProgressText = "Compressing to zip completed";
                            status.IsCompleted = true;
                            break;

                        case "MergeOnly":
                            string finalOutputFilePath = Path.Combine(_outputDirectory, $"out{Guid.NewGuid()}.mp4");
                            await MergeMp4Files(selectedFilePaths, finalOutputFilePath, taskId);
                            status.MergedFilePath = finalOutputFilePath;
                            status.IsCompleted = true;
                            break;

                        default:
                            status.IsCompleted = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"An error occurred during processing: {ex.Message}");
                    status.IsCompleted = true;
                }
            });

            return Json(new { message = "Processing started.", taskId });
        }
        
        public async Task<string> NormalizeToLandscape(string inputPath, string taskId)
        {
            var outputPath = Path.Combine(_outputDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}_landscape.mp4");

            IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.First();
            int width = videoStream.Width;
            int height = videoStream.Height;

            string filter = width < height
                ? "transpose=1,scale=1920:1080,setsar=1" // Portrait -> rotate clockwise
                : "scale=1920:1080,setsar=1";            // Already landscape

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-i \"{inputPath}\"", ParameterPosition.PreInput)
                .AddParameter($"-vf \"{filter}\"")
                .AddParameter("-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p")
                .AddParameter("-c:a aac -strict experimental")
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            _processingStatuses[taskId].ProgressText = $"Normalizing {Path.GetFileName(inputPath)}";
            await conversion.Start();

            return outputPath;
        }
        public async Task MergeNormalizedVideos(List<string> inputFiles, string outputFilePath, string taskId)
        {
            // First normalize all videos
            var normalizedVideos = new List<string>();
            foreach (var file in inputFiles)
            {
                var normalized = await NormalizeToLandscape(file, taskId);
                normalizedVideos.Add(normalized);
            }

            // Create a text file listing all inputs for concat
            var concatListPath = Path.Combine(_outputDirectory, $"concat_{taskId}.txt");
            await System.IO.File.WriteAllLinesAsync(concatListPath, normalizedVideos.Select(path => $"file '{path.Replace("\\", "/")}'"));

            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-f concat -safe 0 -i \"{concatListPath}\"", ParameterPosition.PreInput)
                .AddParameter("-c copy")
                .SetOutput(outputFilePath)
                .SetOverwriteOutput(true);

            _processingStatuses[taskId].ProgressText = "Merging videos...";
            await conversion.Start();

            _processingStatuses[taskId].MergedFilePath = outputFilePath;
        }


        private async Task MergeMp4Files(List<string> inputFiles, string outputFilePath, string taskId)
        {
            var listFilePath = Path.Combine(Path.GetTempPath(), $"concat_{Guid.NewGuid()}.txt");
            try
            {
                var lines = inputFiles.Select(path => $"file '{path.Replace("\\", "/")}'");
                await System.IO.File.WriteAllLinesAsync(listFilePath, lines);

                var conversion = FFmpeg.Conversions.New()
                    .AddParameter($"-f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outputFilePath}\"");

                var stopwatch = Stopwatch.StartNew();

                conversion.OnProgress += (s, args) =>
                {
                    double percent = args.Percent == 0 ? 1 : args.Percent;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percent * 100);
                    _processingStatuses[taskId].TotalTime = (long)estimatedTotal.TotalMilliseconds;
                    _processingStatuses[taskId].EstimatedTime = (long)(estimatedTotal - elapsed).TotalMilliseconds;
                    _processingStatuses[taskId].Percentage = percent;
                    _processingStatuses[taskId].ProgressText = $"Merging: {percent}%";
                };

                await conversion.Start();

                // Clean up
                DeleteFile(listFilePath);
                inputFiles.ForEach(DeleteFile);
                _processingStatuses[taskId].ProgressText = "Merging completed";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred: {ex.Message}");
            }
        }


        private async Task MergeAfterConversion(string taskId, List<string> convertedFiles)
        {
            if (convertedFiles.Count == 0)
            {
                _processingStatuses[taskId].IsCompleted = true;
                _processingStatuses[taskId].ProgressText = "Cannot merge file";
                return;
            }

            string finalOutputFilePath = Path.Combine(_outputDirectory, $"out{Guid.NewGuid()}.mp4");
            await MergeMp4Files(convertedFiles, finalOutputFilePath, taskId);
            
        }
        private void DeleteFile(string filePath)
        {
                System.IO.File.Delete(filePath);
        }
        // Method to create a zip file from the converted files
        private string CreateZipFile(List<string> files,string taskId)
        {
            string zipFileName = Path.Combine(_outputDirectory, $"ConvertedFiles_{Guid.NewGuid()}.zip");
            using (var zipArchive = ZipFile.Open(zipFileName, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    zipArchive.CreateEntryFromFile(file, Path.GetFileName(file));
                    _processingStatuses[taskId].ProgressText="Compressing to zip...";
                }
            }
            return zipFileName;
        }
    }

    public class ProcessingStatus
    {
        public string ProgressText { get; set; } = "Progressing .... ";
        public long TotalTime { get; set; }
        public long EstimatedTime { get; set; }
        public double Percentage { get; set; }
        public bool IsCompleted { get; set; } = false;
        public string ZipFilePath { get; set; }
        public string MergedFilePath { get; set; }
    }   
}
