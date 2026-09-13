using System;
using System.Diagnostics;
using System.IO;

namespace WallpaperControl
{
    internal static class WallpaperFileActions
    {
        internal static bool IsSupportedImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                !File.Exists(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);

            return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        internal static void OpenImage(string path)
        {
            if (!IsSupportedImagePath(path))
            {
                throw new InvalidOperationException("The wallpaper path is not a supported image file.");
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(path),
                    UseShellExecute = true
                });
        }

        internal static void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                !Directory.Exists(path))
            {
                throw new InvalidOperationException("The folder path is not valid.");
            }

            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false
                };

            startInfo.ArgumentList.Add(Path.GetFullPath(path));
            Process.Start(startInfo);
        }

        internal static void RevealInExplorer(string path)
        {
            if (!IsSupportedImagePath(path))
            {
                throw new InvalidOperationException("The wallpaper path is not a supported image file.");
            }

            ProcessStartInfo startInfo =
                new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false
                };

            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(Path.GetFullPath(path));

            Process.Start(startInfo);
        }
    }
}
