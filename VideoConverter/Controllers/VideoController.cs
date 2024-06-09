using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.IO.Compression;
using VideoConverter.Hubs;
using VideoConverter.Models;
using Xabe.FFmpeg;

using Xabe.FFmpeg.Downloader;

namespace VideoConverter.Controllers
{
    public class VideoController : Controller
    {
        public IHubContext<VideoConverterHub> _hubContext { get; }

        public VideoController(IHubContext<VideoConverterHub> hubContext)
        { 
            _hubContext = hubContext;
        }
       
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult ConvertMultiple() {
            return View();
        } 
        public IActionResult ConvertAndMerge() {
            var vm = new UploadFileVm();
            vm.MergeFiles = true;
            return View(vm);
        }
        [HttpPost]
        public async Task<IActionResult> ConvertMultiple([FromForm]UploadFileVm vm)
        {
            if (vm.Files == null || vm.Files.Count == 0)
            {
                return BadRequest("No files uploaded");
            }

            var convertedFiles = new List<string>();
           
            var fileAddition = DateTime.Now.ToShortTimeString();
            // Create a ZIP archive of the converted files
            var zipFilePath = Path.Combine("wwwroot", "converted", $"CV{fileAddition}.zip");

            try
            {
                foreach (var file in vm.Files)
                {
                    if (file.Length == 0)
                        continue;
                    
                    var filePath = await ConvertFile(file);
                        convertedFiles.Add(filePath);
                    
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
                var fileName = $"ConvertedVideos{fileAddition}.zip";

               
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
                        DeleteFile(file);   
                    }
                }
                if (System.IO.File.Exists(zipFilePath))
                {
                    DeleteFile(zipFilePath);
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
            
            try
            {
               var convertedFilePath = await ConvertFile(vm.File);

                return await ReturnMemoryStream(convertedFilePath);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error during conversion: {ex.Message}");
            }
           
           
        }

        private async Task<IActionResult> ReturnMemoryStream(string filePath)
        {
            // Return the converted file for download
            var memory = new MemoryStream();
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;
            var contentType = "application/octet-stream";
            var fileName = Path.GetFileName(filePath);
            if (System.IO.File.Exists(filePath))
            {
                DeleteFile(filePath);
            }
            return File(memory, contentType, fileName);
        } 
        [HttpPost]
        public async Task<IActionResult> ConvertAndMerge(UploadFileVm vm) {

            if (vm.File == null || vm.File1== null)
            {
                return BadRequest("No files uploaded");
            }

            var convertedFiles = new List<string>();     
            try
            {

                    convertedFiles.Add(await ConvertFile(vm.File));    
                    convertedFiles.Add(await ConvertFile(vm.File1));    
                if(vm.File2!=null)
                    convertedFiles.Add(await ConvertFile(vm.File2));


                if (vm.MergeFiles && convertedFiles.Count > 1)
                {
                   return await MergeFiles(convertedFiles);  
                }
                else
                {
                    return await AddToZip(convertedFiles);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error during conversion: {ex.Message}");
            }
        
        }
        private async Task<IActionResult> MergeFile(List<string> convertedFiles)
        {

            var convertedFolder = Path.Combine("wwwroot", "converted");
            // Merge files if the checkbox is ticked
            var mergedFilePath = Path.Combine(convertedFolder, "MergedVideo.mp4");

            var conversion = FFmpeg.Conversions.New();
            foreach (var file in convertedFiles)
            {
                var mediaInfo = await FFmpeg.GetMediaInfo(file);
                conversion.AddStream(mediaInfo.Streams);
            }
            await conversion.SetOutput(mergedFilePath).Start();

            // Clean up individual converted files
            foreach (var file in convertedFiles)
            {
                if (System.IO.File.Exists(file))
                {
                    DeleteFile(file);
                }
            }

            // Return the merged file for download
            var memory = new MemoryStream();
            using (var stream = new FileStream(mergedFilePath, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;
            var contentType = "application/octet-stream";
            var fileName = "MergedVideo.mp4";

            if (System.IO.File.Exists(mergedFilePath))
            {
                DeleteFile(mergedFilePath);
            }

            return File(memory, contentType, fileName);
        }
        private void DeleteFile(string filePath)
        {
            System.IO.File.Delete(filePath);
        }
        private async Task<string> ConvertFile(IFormFile file)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"Starting conversion of {file.FileName}");
            // Get a unique filename for the uploaded video
            var uploadsFolder = Path.Combine("wwwroot", "uploads");
            // Define the output path for the converted video
            var convertsFolder = Path.Combine("wwwroot", "converted");
            Directory.CreateDirectory(uploadsFolder);
            var uploadFilePath = Path.Combine(uploadsFolder, file.FileName);

            // Save the uploaded file
            using (var fileStream = new FileStream(uploadFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // Define the output path for the converted video
            var convertedFileName = $"{Path.GetFileNameWithoutExtension(file.FileName)}.mp4";
            var convertedFilePath = Path.Combine(convertsFolder, convertedFileName);

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
            // Delete the uploaded file
            if (System.IO.File.Exists(uploadFilePath))
            {
                DeleteFile(uploadFilePath);
            }
            // Send status update to the client
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"Finished conversion of {file.FileName}");

            return convertedFilePath;
        }

        private async Task<IActionResult> AddToZip(List<string> convertedFiles)
        {

            var convertedFolder = Path.Combine("wwwroot", "converted");
            // Create a ZIP archive of the converted files
            var zipFilePath = Path.Combine(convertedFolder, "ConvertedVideos.zip");
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
            var fileName = $"{new Guid()}.zip";

            // Clean up the converted files and the ZIP file
            foreach (var file in convertedFiles)
            {
                if (System.IO.File.Exists(file))
                {
                   DeleteFile(file);
                }
            }

            if (System.IO.File.Exists(zipFilePath))
            {
               DeleteFile(zipFilePath);
            }

            return File(memory, contentType, fileName);

        }

        private async Task<IActionResult> MergeFiles(List<string> convertedFiles)
        {
            try
            {
                var convertedFolder = Path.Combine("wwwroot", "converted");
                var conversion = FFmpeg.Conversions.New();
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                foreach (var inputFile in convertedFiles)
                {
                    conversion.AddParameter($"-i \"{inputFile}\"");
                }

                conversion.AddParameter($"-filter_complex \"concat=n={convertedFiles.Count}:v=1:a=1 [v] [a]\" -map \"[v]\" -map \"[a]\" \"{convertedFolder}\"");

                // Subscribe to the OnProgress event
                conversion.OnProgress += (sender, args) =>
                {
                    double percentComplete = args.Percent;
                    TimeSpan elapsed = stopwatch.Elapsed;
                    TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * 100);
                    TimeSpan estimatedRemaining = estimatedTotal - elapsed;

                    Console.WriteLine($"Merging files: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedRemaining:hh\\:mm\\:ss}");
                };

                // Subscribe to the OnDataReceived event to get FFmpeg output messages
                conversion.OnDataReceived += (sender, args) =>
                {
                    Console.WriteLine(args.Data);
                };

                // Start the merging process
                await conversion.Start();
                stopwatch.Stop();

                // Clean up individual converted files
                foreach (var file in convertedFiles)
                {
                    if (System.IO.File.Exists(file))
                    {
                        DeleteFile(file);
                    }
                }

                // Return the merged file for download
                var memory = new MemoryStream();
                using (var stream = new FileStream(conversion.OutputFilePath, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                var contentType = "application/octet-stream";
                var fileName = "MergedVideo.mp4";

                if (System.IO.File.Exists(conversion.OutputFilePath))
                {
                    DeleteFile(conversion.OutputFilePath);
                }

                return File(memory, contentType, fileName);
            }
            catch (Exception ex)
            {
                throw new Exception($"An error occurred during merging: {ex.Message}");
            }
        }
    }
}
