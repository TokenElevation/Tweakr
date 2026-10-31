using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Tweakr.App.Services;

/// <summary>Loads the real Start-menu icon of an installed Store (MSIX/Appx) app from its package folder.</summary>
public static class AppIconLoader
{
    /// <summary>Safe to call on a background thread. Returns a frozen ImageSource, or null if no icon was found.</summary>
    public static ImageSource? Load(string? installLocation)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) return null;

            var manifest = Path.Combine(installLocation, "AppxManifest.xml");
            if (!File.Exists(manifest)) return null;

            var doc = XDocument.Load(manifest);
            var visual = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            var logo = visual?.Attribute("Square44x44Logo")?.Value
                    ?? visual?.Attribute("Square150x150Logo")?.Value
                    ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
            if (string.IsNullOrWhiteSpace(logo)) return null;

            var logoPath = Path.Combine(installLocation, logo.Replace('/', '\\'));
            var dir = Path.GetDirectoryName(logoPath);
            var name = Path.GetFileNameWithoutExtension(logoPath);
            if (dir is null || !Directory.Exists(dir)) return null;

            // Assets ship as Name.scale-200.png, Name.targetsize-48.png ... pick the best one for a ~26px tile
            var best = Directory.EnumerateFiles(dir, name + "*.png")
                .Select(f => (file: f, score: Score(Path.GetFileName(f))))
                .Where(c => c.score > 0)
                .OrderByDescending(c => c.score)
                .Select(c => c.file)
                .FirstOrDefault();
            if (best is null) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(best);
            bmp.DecodePixelWidth = 64;
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // file handle released immediately
            bmp.EndInit();
            bmp.Freeze();                                  // required to hand it to the UI thread
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Fallback if your scan doesn't give you InstallLocation (needs admin; Tweakr already runs elevated).</summary>
    public static string? FindInstallLocation(string packageName)
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            return Directory.EnumerateDirectories(root, packageName + "_*").OrderByDescending(d => d).FirstOrDefault();
        }
        catch { return null; }
    }

    static int Score(string file)
    {
        var f = file.ToLowerInvariant();
        if (f.Contains("contrast") || f.Contains("lightunplated") || f.Contains("devicefamily")) return 0;
        int s = 1;
        if (f.Contains("altform-unplated")) s += 5;                // transparent background looks best on our tile
        if (f.Contains("targetsize-48")) s += 40;
        else if (f.Contains("targetsize-64") || f.Contains("targetsize-44") || f.Contains("targetsize-56")) s += 35;
        else if (f.Contains("targetsize-96") || f.Contains("targetsize-72")) s += 30;
        else if (f.Contains("scale-200")) s += 25;
        else if (f.Contains("scale-100") || f.Contains("scale-150")) s += 20;
        else if (f.Contains("targetsize-256")) s += 10;
        return s;
    }
}
