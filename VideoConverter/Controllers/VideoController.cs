using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using VideoConverter.Models;
using Xabe.FFmpeg;

using Xabe.FFmpeg.Downloader;

namespace VideoConverter.Controllers
{
    public class VideoController : Controller
    {
        public VideoController()
        {
            // Set the path to store FFmpeg binaries. This path should be writable by the application.
            string ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg-binaries");
            FFmpeg.SetExecutablesPath(ffmpegPath);

            // Download FFmpeg if not already present
            EnsureFFmpegBinaries().GetAwaiter().GetResult();
        }
        private async Task EnsureFFmpegBinaries()
        {
            var ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg-binaries");

            if (!Directory.Exists(ffmpegPath) || Directory.GetFiles(ffmpegPath).Length == 0)
            {
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
            }
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult ConvertMultiple() {
            return View();

        }
        [HttpPost]
        public async Task<IActionResult> ConvertMultiple([FromForm]UploadFileVm vm)
        {
            if (vm.Files == null || vm.Files.Count == 0)
            {
                return BadRequest("No files uploaded");
            }

            var convertedFiles = new List<string>();
            var uploadsFolder = Path.Combine("wwwroot", "uploads");
            var convertedFolder = Path.Combine("wwwroot", "converted");
            Directory.CreateDirectory(uploadsFolder);
            Directory.CreateDirectory(convertedFolder);

            // Create a ZIP archive of the converted files
            var zipFilePath = Path.Combine("wwwroot", "converted", "ConvertedVideos.zip");

            try
            {
                foreach (var file in vm.Files)
                {
                    if (file.Length == 0)
                        continue;

                    var uploadFilePath = Path.Combine(uploadsFolder, file.FileName);

                    // Save the uploaded file
                    using (var fileStream = new FileStream(uploadFilePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }

                    // Define the output path for the converted video
                    var convertedFileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}.mp4";
                    var convertedFilePath = Path.Combine(convertedFolder, convertedFileName);

                    // Convert the video to MP4 using Xabe.FFmpeg
                    var conversion = await FFmpeg.Conversions.FromSnippet.ToMp4(uploadFilePath, convertedFilePath);
                    using (var fileStream = new FileStream(conversion.OutputFilePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fileStream);
                    }

                    // Check if the conversion was successful
                    if (!System.IO.File.Exists(convertedFilePath))
                    {
                        throw new FileNotFoundException($"Converted file not found: {convertedFilePath}");
                    }

                    convertedFiles.Add(convertedFilePath);

                    // Delete the uploaded file
                    if (System.IO.File.Exists(uploadFilePath))
                    {
                        System.IO.File.Delete(uploadFilePath);
                    }
                }

                using (var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
                {
                    foreach (var convertedFile in convertedFiles)
                    {
                        zip.CreateEntryFromFile(convertedFile, Path.GetFileName(convertedFile));
                    }
                }

                // Return the ZIP file for download
                var memory = new MemoryStream();
                using (var stream = new FileStream(zipFilePath, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                var contentType = "application/zip";
                var fileName = "ConvertedVideos.zip";

               
                return File(memory, contentType, fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error during conversion: {ex.Message}");
            }
            finally
            {
                // Clean up the converted files
                foreach (var file in convertedFiles)
                {
                    if (System.IO.File.Exists(file))
                    {
                        System.IO.File.Delete(file);
                    }
                }
                if (System.IO.File.Exists(zipFilePath))
                {
                    System.IO.File.Delete(zipFilePath);
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> Index([FromForm] UploadFileVm vm)
        {
            if (vm.File == null || vm.File.Length == 0)
            {
                return BadRequest("Invalid file");
            }
            // Get a unique filename for the uploaded video
            var uploadsFolder = Path.Combine("wwwroot", "uploads");
            var uploadFilePath = Path.Combine(uploadsFolder, vm.File.FileName);

            try
            {
               
                Directory.CreateDirectory(uploadsFolder);
                using (var fileStream = new FileStream(uploadFilePath, FileMode.Create))
                {
                    await vm.File.CopyToAsync(fileStream);
                }

                // Define the output path for the converted video
                var convertedFolder = Path.Combine("wwwroot", "converted");
                var convertedFileName = $"{Path.GetFileNameWithoutExtension(vm.File.FileName)}.mp4";
                var convertedFilePath = Path.Combine(convertedFolder, convertedFileName);
                Directory.CreateDirectory(convertedFolder);

                // Convert the video to MP4 using Xabe.FFmpeg
                var conversion = await FFmpeg.Conversions.FromSnippet.ToMp4(uploadFilePath, convertedFilePath);
                using (var fileStream = new FileStream(conversion.OutputFilePath, FileMode.Create))
                {
                    await vm.File.CopyToAsync(fileStream);
                }


                // Delete the uploaded file
                if (System.IO.File.Exists(uploadFilePath))
                {
                    System.IO.File.Delete(uploadFilePath);
                }

                // Return the converted file for download
                var memory = new MemoryStream();
                using (var stream = new FileStream(convertedFilePath, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                var contentType = "application/octet-stream";
                var fileName = Path.GetFileName(convertedFilePath);

                return File(memory, contentType, fileName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error during conversion: {ex.Message}");
            }
           
        }
    }
}
