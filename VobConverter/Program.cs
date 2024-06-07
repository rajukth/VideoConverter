using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

class Program
{
    static async Task Main(string[] args)
    {
       

        string outputDirectory = @"E:\\dilliram\puran\3\Convert";
        string inputDirectory = @"E:\\dilliram\puran\3\";
        var inputFiles = new List<string>()
       {
            $"{inputDirectory}7.vob",
            $"{inputDirectory}8.vob",
            $"{inputDirectory}9.vob",            
            $"{inputDirectory}10.vob",            
           // Add more VOB files as needed
       };
        Directory.CreateDirectory(outputDirectory);

        Directory.CreateDirectory(outputDirectory);

        List<string> convertedFiles = new List<string>();

        // Ensure FFmpeg executables are available
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

        Console.WriteLine("What do you want to do?");
        Console.WriteLine("1. Convert only");
        Console.WriteLine("2. Merge only");
        Console.WriteLine("3. Convert and merge");
        Console.WriteLine("Make your choice : ");
        string choice = Console.ReadLine();
        switch (choice) {
            case "1": //convert only
                {
                    // Convert each VOB file to MP4
                    foreach (var inputFile in inputFiles)
                    {
                        string outputFilePath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".mp4");
                        await ConvertVobToMp4(inputFile, outputFilePath);
                        convertedFiles.Add(outputFilePath);
                    }

                    break;
                }
            case "3": //convert and merge only
                {
                    // Convert each VOB file to MP4
                    foreach (var inputFile in inputFiles)
                    {
                        string outputFilePath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputFile) + ".mp4");
                        await ConvertVobToMp4(inputFile, outputFilePath);
                        convertedFiles.Add(outputFilePath);
                    }

                    // Merge converted MP4 files into a single MP4 file
                    string finalOutputFilePath = Path.Combine(outputDirectory, $"out{Guid.NewGuid()}.mp4");
                    await MergeMp4Files(convertedFiles, finalOutputFilePath);
                    break;
                }
            case "2"://convert and merge only
                {
                    // Merge converted MP4 files into a single MP4 file
                    string finalOutputFilePath = Path.Combine(outputDirectory, $"out{Guid.NewGuid()}.mp4");
                    await MergeMp4Files(inputFiles, finalOutputFilePath);
                    break;
                }
            default:
                {
                    Console.WriteLine("No any operation performed...");
                    break;
                }       
        }          

        Console.WriteLine("All files have been converted and merged successfully!");
    }

    static async Task ConvertVobToMp4(string inputFilePath, string outputFilePath)
    {
        try
        {
            var conversion = await FFmpeg.Conversions.FromSnippet.Convert(inputFilePath, outputFilePath);
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // Subscribe to the OnProgress event
            conversion.OnProgress += (sender, args) =>
            {
                double percentComplete = args.Percent==0?1:args.Percent;
                TimeSpan elapsed = stopwatch.Elapsed;
                TimeSpan estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / percentComplete * 100);
                TimeSpan estimatedRemaining = estimatedTotal - elapsed;

                Console.WriteLine($"Converting {Path.GetFileName(inputFilePath)}: [{args.Duration} / {args.TotalLength}] {args.Percent}% - Estimated time remaining: {estimatedRemaining:hh\\:mm\\:ss}");
            };

            // Subscribe to the OnDataReceived event to get FFmpeg output messages
            conversion.OnDataReceived += (sender, args) =>
            {
                Console.WriteLine(args.Data);
            };

            // Start the conversion process
            await conversion.Start();
            stopwatch.Stop();

            Console.WriteLine($"Conversion of {Path.GetFileName(inputFilePath)} completed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during conversion of {Path.GetFileName(inputFilePath)}: {ex.Message}");
        }
    }

    static async Task MergeMp4Files(List<string> inputFiles, string outputFilePath)
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

            // Subscribe to the OnProgress event
            conversion.OnProgress += (sender, args) =>
            {
                double percentComplete = args.Percent==0?1:args.Percent;
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

            Console.WriteLine("Merging completed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred during merging: {ex.Message}");
        }
    }
}
