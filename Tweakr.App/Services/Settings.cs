using System.IO;
using System.Text.Json;

namespace Tweakr.App.Services;

public sealed class AppSettings
{
    public string Backdrop { get; set; } = "Mica";
    public string Accent { get; set; } = "Sky";
    public string Theme { get; set; } = "Charcoal";
    public bool DiscordPresence { get; set; } = true;

    // Startup tab
    public bool StartupEnabled { get; set; }
    public List<string> StartupTweakIds { get; set; } = new();
    public string? LastStartupRun { get; set; }
    public string? LastStartupSummary { get; set; }
    public bool Animations { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = true;
}

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly string _file;

    public string Directory { get; }

    public SettingsStore()
    {
        Directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakr");
        System.IO.Directory.CreateDirectory(Directory);
        _file = Path.Combine(Directory, "settings.json");
    }

    public AppSettings Load()
    {
        try { return File.Exists(_file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save(AppSettings s)
    {
        try { File.WriteAllText(_file, JsonSerializer.Serialize(s, Json)); } catch { /* non-fatal */ }
    }
}
