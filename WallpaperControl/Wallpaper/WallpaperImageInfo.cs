using System.Drawing;

namespace WallpaperControl
{
    internal static class WallpaperImageInfo
    {
        /// <summary>Checks extensions accepted by both the slideshow and statistics browser.</summary>
        internal static bool IsSupportedWallpaperExtension(string path)
        {
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

        /// <summary>Reads pixel dimensions and returns the localized fallback for unreadable images.</summary>
        internal static string GetImageResolutionText(string path)
        {
            try
            {
                using Image image =
                    Image.FromFile(path);

                return
                    $"{image.Width} × {image.Height}";
            }
            catch
            {
                return Localization.Get("NotAvailable");
            }
        }

    }
}
