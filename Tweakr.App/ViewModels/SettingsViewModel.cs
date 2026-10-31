using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakr.App.Behaviors;
using Tweakr.App.Services;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

public sealed class AccentOption
{
    public AccentOption(string name, Color color)
    {
        Name = name; Color = color;
        Brush = new SolidColorBrush(color);
        Brush.Freeze();
    }
    public string Name { get; }
    public Color Color { get; }
    public Brush Brush { get; }

    public static IReadOnlyList<AccentOption> All() => new[]
    {
        new AccentOption("Sky",    Color.FromRgb(0x60, 0xCD, 0xFF)),
        new AccentOption("Violet", Color.FromRgb(0xA7, 0x8B, 0xFA)),
        new AccentOption("Pink",   Color.FromRgb(0xF4, 0x72, 0xB6)),
        new AccentOption("Green",  Color.FromRgb(0x4A, 0xDE, 0x80)),
        new AccentOption("Amber",  Color.FromRgb(0xFB, 0xBF, 0x24)),
        new AccentOption("Coral",  Color.FromRgb(0xFB, 0x71, 0x85)),
        new AccentOption("System", SystemParameters.WindowGlassColor)
    };
}

public sealed partial class SettingsViewModel : ObservableObject
{
    readonly SettingsStore _store;
    readonly ThemeService _theme;
    readonly EngineOptions _engine;
    readonly ToastService _toasts;
    readonly DiscordPresenceService _discord;
    readonly bool _ready;

    public IReadOnlyList<BackdropKind> BackdropOptions { get; } = Enum.GetValues<BackdropKind>();
    public IReadOnlyList<AccentOption> AccentOptions { get; } = AccentOption.All();
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = ThemeOption.All();
    public bool BackdropSupported => global::Tweakr.App.Backdrop.IsSupported;

    // About
    public string AppVersion { get; }
    public string RuntimeText => RuntimeInformation.FrameworkDescription;
    public string OsText => RuntimeInformation.OSDescription;

    [ObservableProperty] private BackdropKind _backdrop;
    [ObservableProperty] private AccentOption? _selectedAccent;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private bool _animationsEnabled;
    [ObservableProperty] private bool _createRestorePoint;
    [ObservableProperty] private bool _discordEnabled;

    // ---- custom background (static image only) ----
    // Kept in its own background.json next to the other Tweakr data, so the existing settings file is untouched.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBackground))] private ImageSource? _backgroundImage;
    [ObservableProperty] private double _backgroundDim = 0.4;      // 0..0.9, black overlay for readability
    public bool HasBackground => BackgroundImage is not null;

    string? _backgroundFile;                                        // file name inside the data folder
    CancellationTokenSource? _dimSaveCts;
    string DataDir => _store.Directory.ToString()!;
    string BackgroundJson => Path.Combine(DataDir, "background.json");

    sealed class BackgroundPrefs { public string? FileName { get; set; } public double Dim { get; set; } = 0.4; }

    public SettingsViewModel(SettingsStore store, ThemeService theme, EngineOptions engine, ToastService toasts, DiscordPresenceService discord)
    {
        _store = store; _theme = theme; _engine = engine; _toasts = toasts; _discord = discord;

        var v = typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
        AppVersion = v.Split('+')[0];

        var s = store.Load();
        _backdrop = Enum.TryParse(s.Backdrop, out BackdropKind k) ? k : BackdropKind.Mica;
        _selectedAccent = AccentOptions.FirstOrDefault(a => a.Name == s.Accent) ?? AccentOptions[0];
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Name == s.Theme) ?? ThemeOptions[0];
        _animationsEnabled = s.Animations;
        _createRestorePoint = s.CreateRestorePoint;
        _discordEnabled = s.DiscordPresence;

        LoadBackgroundPrefs();

        theme.SetBackdrop(_backdrop);
        theme.SetAccent(_selectedAccent.Color);
        theme.SetTheme(_selectedTheme);
        Motion.UserEnabled = _animationsEnabled;
        engine.CreateRestorePoint = _createRestorePoint;
        discord.SetEnabled(_discordEnabled);
        _ready = true;
    }

    partial void OnBackdropChanged(BackdropKind value) { _theme.SetBackdrop(value); Persist(); }
    partial void OnSelectedAccentChanged(AccentOption? value) { if (value is null) return; _theme.SetAccent(value.Color); Persist(); }
    partial void OnSelectedThemeChanged(ThemeOption? value) { if (value is null) return; _theme.SetTheme(value); Persist(); }
    partial void OnAnimationsEnabledChanged(bool value) { Motion.UserEnabled = value; Persist(); }
    partial void OnCreateRestorePointChanged(bool value) { _engine.CreateRestorePoint = value; Persist(); }
    partial void OnDiscordEnabledChanged(bool value) { _discord.SetEnabled(value); Persist(); }
    partial void OnBackgroundDimChanged(double value) { if (_ready) _ = SaveDimSoonAsync(); }

    void Persist()
    {
        if (!_ready) return;
        var s = _store.Load();
        s.Backdrop = Backdrop.ToString();
        s.Accent = SelectedAccent?.Name ?? "Sky";
        s.Theme = SelectedTheme?.Name ?? "Charcoal";
        s.Animations = AnimationsEnabled;
        s.CreateRestorePoint = CreateRestorePoint;
        s.DiscordPresence = DiscordEnabled;
        _store.Save(s);
    }

    // ---------- custom background ----------
    /// <summary>Decoded once at a capped width and frozen: a few MB instead of a full-size wallpaper bitmap.</summary>
    static ImageSource LoadBackground(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.DecodePixelWidth = 2560;
        bmp.CacheOption = BitmapCacheOption.OnLoad;            // releases the file handle
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    void LoadBackgroundPrefs()
    {
        try
        {
            if (!File.Exists(BackgroundJson)) return;
            var prefs = JsonSerializer.Deserialize<BackgroundPrefs>(File.ReadAllText(BackgroundJson));
            if (prefs is null) return;

            _backgroundDim = Math.Clamp(prefs.Dim, 0, 0.9);
            var path = prefs.FileName is null ? null : Path.Combine(DataDir, prefs.FileName);
            if (path is not null && File.Exists(path))
            {
                _backgroundFile = prefs.FileName;
                _backgroundImage = LoadBackground(path);
            }
        }
        catch { /* missing / corrupt image or json: silently fall back to the normal backdrop */ }
    }

    void SaveBackgroundPrefs()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DataDir);
            File.WriteAllText(BackgroundJson, JsonSerializer.Serialize(new BackgroundPrefs { FileName = _backgroundFile, Dim = BackgroundDim }));
        }
        catch { }
    }

    // the slider fires on every tick; write the file once the user stops moving it
    async Task SaveDimSoonAsync()
    {
        _dimSaveCts?.Cancel();
        var cts = _dimSaveCts = new CancellationTokenSource();
        try { await Task.Delay(400, cts.Token); SaveBackgroundPrefs(); }
        catch (TaskCanceledException) { }
    }

    [RelayCommand]
    private void ChooseBackground()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a background image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var image = LoadBackground(dlg.FileName);          // validates the file before we touch anything
            System.IO.Directory.CreateDirectory(DataDir);

            // copy into our own folder so moving/deleting the original never breaks the background
            var dest = Path.Combine(DataDir, "background" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                DeleteBackgroundFiles();
                File.Copy(dlg.FileName, dest, overwrite: true);
            }

            _backgroundFile = Path.GetFileName(dest);
            BackgroundImage = image;
            SaveBackgroundPrefs();
        }
        catch (Exception ex)
        {
            _toasts.Show(ToastKind.Error, "Couldn't use that image", ex.Message);
        }
    }

    [RelayCommand]
    private void RemoveBackground()
    {
        BackgroundImage = null;
        _backgroundFile = null;
        DeleteBackgroundFiles();
        SaveBackgroundPrefs();
    }

    void DeleteBackgroundFiles()
    {
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(DataDir, "background.*"))
                if (!f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) File.Delete(f);
        }
        catch { }
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Directory}\"") { UseShellExecute = true });

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var text = $"Tweakr {AppVersion}\n{RuntimeText}\n{OsText}\n"
                 + $"Backdrop: {Backdrop} (supported: {BackdropSupported})\n"
                 + $"Theme: {SelectedTheme?.Name}, accent: {SelectedAccent?.Name}";
        try { Clipboard.SetText(text); _toasts.Show(ToastKind.Success, "Diagnostics copied", "Paste it into a bug report."); }
        catch { _toasts.Show(ToastKind.Error, "Couldn't access the clipboard"); }
    }
}