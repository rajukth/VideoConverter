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
        private static readonly ConcurrentDictionary<string, ProcessingStatus> _processingStatuses = new ConcurrentDictionary<string, ProcessingStatus>();

        public IActionResult Index()
        {
            return View();
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

                            zipFilePath = CreateZipFile(convertedFiles,taskId);
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
                    progressText=status.ProgressText,
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
                var conversion = await FFmpeg.Conversions.FromSnippet.Convert(inputFilePath, outputFilePath);
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                conversion.OnProgress += (sender, args) =>
                {
                    double percentComplete = args.Percent == 0 ? 1 : args.Percent;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * 100);
                    var status = _processingStatuses[taskId];
                    status.TotalTime = (long)estimatedTotal.TotalMilliseconds;
                    status.EstimatedTime = (long)(estimatedTotal - elapsed).TotalMilliseconds;
                    status.Percentage = percentComplete;
                    status.ProgressText = $"Converting {Path.GetFileName(inputFilePath)}: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedTotal - elapsed:hh\\:mm\\:ss}";
                    Debug.WriteLine($"Converting {Path.GetFileName(inputFilePath)}: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedTotal - elapsed:hh\\:mm\\:ss}");
                };

                conversion.OnDataReceived += (sender, args) =>
                {
                    Debug.WriteLine(args.Data);
                };

                await conversion.Start();

                DeleteFile(inputFilePath);
                stopwatch.Stop();
                _processingStatuses[taskId].ProgressText=$"Conversion of {Path.GetFileName(inputFilePath)} completed!";
                Debug.WriteLine($"Conversion of {Path.GetFileName(inputFilePath)} completed!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred during conversion of {Path.GetFileName(inputFilePath)}: {ex.Message}");
            }
        }

        private async Task MergeMp4Files(List<string> inputFiles, string outputFilePath, string taskId)
        {
            try
            {
                var conversion = FFmpeg.Conversions.New();
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                foreach (var inputFile in inputFiles)
                {
                    conversion.AddParameter($"-i \"{inputFile}\"");
                }

                conversion.AddParameter($"-filter_complex \"concat=n={inputFiles.Count}:v=1:a=1 [v] [a]\" -map \"[v]\" -map \"[a]\" \"{outputFilePath}\"");

                conversion.OnProgress += (sender, args) =>
                {
                    double percentComplete = args.Percent == 0 ? 1 : args.Percent;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * 100);
                    var status = _processingStatuses[taskId];
                    status.TotalTime = (long)estimatedTotal.TotalMilliseconds;
                    status.EstimatedTime = (long)(estimatedTotal - elapsed).TotalMilliseconds;
                    status.Percentage = percentComplete;
                    status.ProgressText = $"Merging files: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedTotal - elapsed:hh\\:mm\\:ss}";
                    Debug.WriteLine($"Merging files: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedTotal - elapsed:hh\\:mm\\:ss}");
                };

                conversion.OnDataReceived += (sender, args) =>
                {
                    Debug.WriteLine(args.Data);
                };

                await conversion.Start();
                stopwatch.Stop();
                _processingStatuses[taskId].ProgressText = $"Merging completed";
                Debug.WriteLine("Merging completed!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred during merging: {ex.Message}");
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
