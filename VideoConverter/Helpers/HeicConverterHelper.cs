using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ImageMagick;
namespace VideoConverter.Helpers;

public static class HeicConverterHelper
{
    /// <summary>
    /// Scans the uploads folder for .HEIC files and converts them to .JPG
    /// </summary>
    /// <param name="uploadsFolder">Absolute path to uploads folder</param>
    /// <returns>List of converted JPG file paths</returns>
    public static List<string> ConvertHeicImages(string uploadsFolder)
    {
        var convertedFiles = new List<string>();

        if (!Directory.Exists(uploadsFolder))
            return convertedFiles;

        var heicFiles = Directory.GetFiles(uploadsFolder, "*.heic", SearchOption.TopDirectoryOnly);

        foreach (var heicFile in heicFiles)
        {
            try
            {
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(heicFile);
                string jpgFilePath = Path.Combine(uploadsFolder, fileNameWithoutExt + ".jpg");

                // Skip if already exists
                if (File.Exists(jpgFilePath))
                    continue;

                using (var image = new MagickImage(heicFile))
                {
                    image.Format = MagickFormat.Jpg;
                    image.Write(jpgFilePath);
                }

                // Delete original HEIC file after successful conversion
                File.Delete(heicFile);

                convertedFiles.Add(jpgFilePath);
            }
            catch (Exception ex)
            {
                // Optional: Log the error
                Console.WriteLine($"Failed to convert {heicFile}: {ex.Message}");
            }
        }

        return convertedFiles;
    }
}
