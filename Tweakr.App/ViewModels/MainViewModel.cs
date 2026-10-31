using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakr.App.Services;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

public enum AppPage { Dashboard, Tweaks, Utilities, Debloat, Startup, Settings }

public sealed partial class MainViewModel : ObservableObject
{
    // Material-style 24x24 icon paths for the dashboard cards
    const string CpuIcon = "M15 9H9v6h6V9zm-2 4h-2v-2h2v2zm8-2V9h-2V7c0-1.1-.9-2-2-2h-2V3h-2v2h-2V3H9v2H7c-1.1 0-2 .9-2 2v2H3v2h2v2H3v2h2v2c0 1.1.9 2 2 2h2v2h2v-2h2v2h2v-2h2c1.1 0 2-.9 2-2v-2h2v-2h-2v-2h2zm-4 6H7V7h10v10z";
    const string GpuIcon = "M22 9V7h-2V5c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2v-2h2v-2h-2v-2h2v-2h-2V9h2zm-4 10H4V5h14v14zM6 13h5v4H6zm6-6h4v3h-4zM6 7h5v5H6zm6 4h4v6h-4z";
    const string RamIcon = "M18 2h-8L4.02 8 4 20c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-6 6h-2V4h2v4zm3 0h-2V4h2v4zm3 0h-2V4h2v4z";
    const string SystemIcon = "M21 2H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h7v2H8v2h8v-2h-2v-2h7c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm0 14H3V4h18v12z";
    const string DiskIcon = "M2 20h20v-4H2v4zm2-3h2v2H4v-2zM2 4v4h20V4H2zm4 3H4V5h2v2zm-4 7h20v-4H2v4zm2-3h2v2H4v-2z";
    public const string WrenchIcon = "M22.7 19l-9.1-9.1c.9-2.3.4-5-1.5-6.9-2-2-5-2.4-7.4-1.3L9 6 6 9 1.6 4.7C.4 7.1.9 10.1 2.9 12.1c1.9 1.9 4.6 2.4 6.9 1.5l9.1 9.1c.4.4 1 .4 1.4 0l2.3-2.3c.5-.4.5-1.1.1-1.4z";

    readonly TweakEngine _engine;
    readonly SystemInfoService _sys;
    readonly DiscordPresenceService _discord;
    readonly InfoCardViewModel _cpu, _gpu, _mem, _os, _disk, _tweaks;
    bool _restoreWarned;

    public ObservableCollection<TweakCardViewModel> Cards { get; } = new();
    public ObservableCollection<InfoCardViewModel> InfoCards { get; } = new();
    public ICollectionView View { get; }
    public SettingsViewModel Settings { get; }
    public StartupViewModel Startup { get; }
    public UtilitiesViewModel Utilities { get; }
    public DebloatViewModel Debloat { get; }
    public DialogService Dialog { get; }
    public ToastService Toasts { get; }
    public string UserName => Environment.UserName;
    public string Greeting { get; } = PickGreeting();

    /// <summary>Different greeting every launch; one option follows the time of day.</summary>
    static string PickGreeting()
    {
        int h = DateTime.Now.Hour;
        var pool = new List<string>
        {
            "Hello", "Hey", "Hi there", "What's up", "Yo", "Sup", "Howdy", "Welcome back", "Good to see you", "Hey hey",
            h < 5 ? "Still up" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening"
        };
        return pool[Random.Shared.Next(pool.Count)];
    }

    [ObservableProperty] private AppPage _page = AppPage.Dashboard;
    [ObservableProperty] private IReadOnlyList<CategoryItem> _categories = new[] { new CategoryItem("All") };
    [ObservableProperty] private string? _selectedCategory;              // null while on Dashboard / Startup / Settings
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSearch))] private string _searchText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isLoadingInfo = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPending))] private int _pendingCount;

    public bool HasPending => PendingCount > 0;
    public bool HasSearch => !string.IsNullOrEmpty(SearchText);

    public MainViewModel(TweakEngine engine, SettingsViewModel settings, StartupViewModel startup, UtilitiesViewModel utilities,
        DebloatViewModel debloat, SystemInfoService sys, DialogService dialog, ToastService toasts, DiscordPresenceService discord)
    {
        _engine = engine; _sys = sys; _discord = discord;
        Settings = settings; Startup = startup; Utilities = utilities; Debloat = debloat; Dialog = dialog; Toasts = toasts;

        _cpu = new("CPU", "Processor Information", CpuIcon, Color.FromRgb(0x3B, 0x82, 0xF6), "Model", "Cores");
        _gpu = new("GPU", "Graphics Information", GpuIcon, Color.FromRgb(0x14, 0xB8, 0xA6), "Model", "VRAM");
        _mem = new("Memory", "RAM Information", RamIcon, Color.FromRgb(0xA8, 0x55, 0xF7), "Total Memory", "Type");
        _os = new("System", "OS Information", SystemIcon, Color.FromRgb(0xEF, 0x44, 0x44), "Operating System", "Version");
        _disk = new("Storage", "Disk Information", DiskIcon, Color.FromRgb(0xF9, 0x73, 0x16), "Primary Disk", "Total Space");
        _tweaks = new("Tweaks", "Applied Tweaks", WrenchIcon, Color.FromRgb(0x22, 0xC5, 0x5E), "Available Tweaks", "Active Tweaks");
        foreach (var c in new[] { _cpu, _gpu, _mem, _os, _disk, _tweaks }) InfoCards.Add(c);

        View = CollectionViewSource.GetDefaultView(Cards);
        View.Filter = o => o is TweakCardViewModel c
            && (SelectedCategory is null or "All" || (SelectedCategory == "Featured" ? c.IsFeatured : c.Category == SelectedCategory))
            && (string.IsNullOrWhiteSpace(SearchText)
                || c.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || c.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        if (value is not null) Page = AppPage.Tweaks;   // picking a category leaves Dashboard / Startup / Settings
        View.Refresh();
        UpdatePresence();
    }

    partial void OnPageChanged(AppPage value) => UpdatePresence();

    partial void OnSearchTextChanged(string value)
    {
        if (value.Length > 0 && Page != AppPage.Tweaks) { Page = AppPage.Tweaks; SelectedCategory ??= "All"; }
        View.Refresh();
    }

    /// <summary>Generic, non-sensitive Discord status: which page you're on and tweak counts. Nothing else.</summary>
    void UpdatePresence()
    {
        string details = Page switch
        {
            AppPage.Dashboard => "On the dashboard",
            AppPage.Utilities => "Using Windows utilities",
            AppPage.Debloat => "Debloating Windows",
            AppPage.Startup => "Choosing startup tweaks",
            AppPage.Settings => "Tuning Tweakr's settings",
            _ => SelectedCategory is null or "All" ? "Browsing tweaks" : $"Browsing {SelectedCategory} tweaks"
        };
        string? state = Cards.Count == 0 ? null : $"{Cards.Count} tweaks · {Cards.Count(c => c.Current)} active";
        _discord.Update(details, state);
    }

    public async Task InitializeAsync()
    {
        var info = LoadSystemInfoAsync();      // runs in parallel with tweak detection

        foreach (var tweak in _engine.Tweaks.Where(t => t.Page != "utilities"))
        {
            var card = new TweakCardViewModel(tweak);
            await DetectInto(card);
            card.PendingChanged += RefreshPending;
            Cards.Add(card);
            if (card.IsAction) _ = RefreshEstimateAsync(card);     // size preview for cleanup actions
        }
        var names = new List<string> { "All" };
        if (Cards.Any(c => c.IsFeatured)) names.Add("Featured");
        names.AddRange(Cards.Select(c => c.Category).Distinct().Order());
        Categories = names.Select(n => new CategoryItem(n)).ToList();
        RefreshStats();

        await Startup.LoadAsync(_engine.Tweaks.Where(t => t.Page != "utilities"));
        await Utilities.LoadAsync(_engine.Tweaks);
        await info;
    }

    static async Task RefreshEstimateAsync(TweakCardViewModel card)
    {
        try { card.Estimate = await card.Tweak.EstimateAsync(); } catch { card.Estimate = null; }
    }

    async Task LoadSystemInfoAsync()
    {
        try
        {
            var s = await _sys.GetAsync();
            _cpu.Value1 = s.CpuModel; _cpu.Value2 = s.CpuCores;
            _gpu.Value1 = s.GpuModel; _gpu.Value2 = s.GpuVram;
            _mem.Value1 = s.MemTotal; _mem.Value2 = s.MemType;
            _os.Value1 = s.OsName; _os.Value2 = s.OsVersion;
            _disk.Value1 = s.DiskModel; _disk.Value2 = s.DiskSize;
        }
        catch { /* cards keep their placeholders */ }
        finally { IsLoadingInfo = false; }
    }

    static async Task DetectInto(TweakCardViewModel card)
    {
        try
        {
            var state = await card.Tweak.DetectAsync();
            card.Current = state.IsOn;
            card.Desired = state.IsOn;
            card.Error = null;
        }
        catch (Exception ex) { card.Error = "Couldn't read state: " + ex.Message; }
    }

    void RefreshPending()
    {
        PendingCount = Cards.Count(c => c.IsPending);
        RefreshStats();
    }

    void RefreshStats()
    {
        int active = Cards.Count(c => c.Current);
        StatusText = $"{Cards.Count} tweaks · {active} active";
        _tweaks.Value1 = $"{Cards.Count} Tweaks";
        _tweaks.Value2 = $"{active} Active";
        UpdatePresence();
    }

    List<PendingChange> Pending() =>
        Cards.Where(c => c.IsPending).Select(c => new PendingChange(c.Tweak, c.Desired)).ToList();

    // ---------------- navigation ----------------
    [RelayCommand] private void ClearSearch() => SearchText = "";
    [RelayCommand] private void OpenDashboard() { Page = AppPage.Dashboard; SelectedCategory = null; }
    [RelayCommand] private void OpenStartup() { Page = AppPage.Startup; SelectedCategory = null; }
    [RelayCommand] private void OpenUtilities() { Page = AppPage.Utilities; SelectedCategory = null; }
    [RelayCommand] private void OpenDebloat() { Page = AppPage.Debloat; SelectedCategory = null; _ = Debloat.EnsureLoadedAsync(); }
    [RelayCommand] private void OpenDebloatTweaks() { Page = AppPage.Tweaks; SelectedCategory = "Debloat"; }
    [RelayCommand] private void OpenSettings() { Page = AppPage.Settings; SelectedCategory = null; }
    [RelayCommand] private void OpenTweaks() { Page = AppPage.Tweaks; SelectedCategory = "All"; }

    // ---------------- one-shot tweaks (cleanup etc.) ----------------
    [RelayCommand]
    private async Task ExecuteAsync(TweakCardViewModel? card)
    {
        if (card is null || !card.IsAction) return;

        if (card.Tweak.Risk != RiskLevel.Safe)       // moderate / advanced actions ask first
        {
            if (!await Dialog.ConfirmAsync($"Run “{card.Title}”?", card.Tweak.Danger ?? "This can't be undone.",
                    "Execute", "Cancel", danger: card.Tweak.Risk == RiskLevel.Advanced)) return;
        }

        card.IsBusy = true;
        card.Error = null;
        try
        {
            var r = await card.Tweak.ExecuteAsync();
            card.NotifyResult(r.Success);
            await Dialog.ConfirmAsync(r.Success ? $"{card.Title} finished" : $"{card.Title} failed", r.Message, "OK", null, danger: !r.Success);
            await RefreshEstimateAsync(card);
        }
        catch (Exception ex)
        {
            card.NotifyResult(false);
            await Dialog.ConfirmAsync($"{card.Title} failed", ex.Message, "OK", null, danger: true);
        }
        finally { card.IsBusy = false; }
    }

    // ---------------- apply flow ----------------
    [RelayCommand]
    private async Task PreviewAsync()
    {
        var pending = Pending();
        if (pending.Count == 0) return;

        var sets = (await _engine.PreviewAsync(pending)).Where(s => s.Changes.Count > 0).ToList();
        if (sets.Count == 0)
        {
            await Dialog.ConfirmAsync("Nothing to change", "Your system already matches these settings.", "OK", null);
            return;
        }

        int total = sets.Sum(s => s.Changes.Count);
        var groups = sets.Select(s => new DialogChangeGroup(s.Title,
            s.Changes.Select(c => new DialogChange(c.Target, c.OldValue, c.NewValue)).ToList())).ToList();

        bool apply = await Dialog.ConfirmAsync("Review changes",
            $"{total} value{(total == 1 ? "" : "s")} across {sets.Count} tweak{(sets.Count == 1 ? "" : "s")} will change. Nothing is applied until you confirm.",
            "Apply now", "Close", false, groups);
        if (apply) await ApplyCoreAsync(pending);
    }

    [RelayCommand]
    private Task ApplyAsync() => ApplyCoreAsync(Pending());

    async Task ApplyCoreAsync(List<PendingChange> pending)
    {
        if (pending.Count == 0) return;

        // Advanced tweaks need an explicit, informed confirmation.
        var risky = pending.Where(p => p.Tweak.Risk == RiskLevel.Advanced).ToList();
        if (risky.Count > 0)
        {
            var text = "These changes are marked Advanced and can affect your security or stability:\n\n"
                + string.Join("\n\n", risky.Select(p => $"• {p.Tweak.Title}\n{p.Tweak.Danger ?? "Use with care."}"));
            if (!await Dialog.ConfirmAsync("Advanced changes", text, "Apply anyway", "Cancel", danger: true)) return;
        }

        var report = await _engine.ApplyAsync(pending);

        foreach (var r in report.Results)
        {
            var card = Cards.First(c => c.Tweak.Id == r.TweakId);
            if (r.Success) { card.Current = card.Desired; card.Error = null; }
            else { card.Error = r.Error; card.Desired = card.Current; }
            card.NotifyResult(r.Success);

            if (!r.Success) Toasts.Show(ToastKind.Error, $"Couldn't apply “{card.Title}”", r.Error, 7);
        }

        var ok = report.Results.Where(r => r.Success).ToList();
        if (ok.Count == 1)
            Toasts.Show(ToastKind.Success, "Change applied", Cards.First(c => c.Tweak.Id == ok[0].TweakId).Title);
        else if (ok.Count > 1)
            Toasts.Show(ToastKind.Success, $"{ok.Count} changes applied");

        if (Settings.CreateRestorePoint && !report.RestorePointCreated && !_restoreWarned)
        {
            _restoreWarned = true;
            Toasts.Show(ToastKind.Warning, "No restore point created",
                "System Restore may be off, or Windows already made one in the last 24 hours.", 7);
        }

        var needReboot = ok.Where(r => r.NeedsReboot).Select(r => Cards.First(c => c.Tweak.Id == r.TweakId).Title).ToList();
        if (needReboot.Count > 0) await PromptRebootAsync(needReboot);
    }

    /// <summary>Some tweaks only take full effect after a restart: ask, and offer to do it now.</summary>
    Task PromptRebootAsync(IReadOnlyList<string> titles) => RebootHelper.PromptAsync(Dialog, titles);

    [RelayCommand]
    private void Discard()
    {
        foreach (var c in Cards.Where(c => c.IsPending)) c.Desired = c.Current;
    }

    [RelayCommand]
    private async Task RevertAllAsync()
    {
        if (!await Dialog.ConfirmAsync("Revert everything?",
                "This undoes every change Tweakr has made, newest first.", "Revert all", "Cancel", danger: true)) return;

        // tweaks that need a restart also need one when they are reverted
        var rebootTitles = _engine.History.Items.Where(t => !t.Reverted)
            .Select(t => _engine.Tweaks.FirstOrDefault(x => x.Id == t.TweakId))
            .Where(t => t is { RequiresReboot: true })
            .Select(t => t!.Title).Distinct().ToList();

        var errors = await _engine.RevertAllAsync();
        foreach (var c in Cards) await DetectInto(c);

        if (errors.Count == 0) Toasts.Show(ToastKind.Success, "Everything reverted");
        else Toasts.Show(ToastKind.Error, $"Reverted with {errors.Count} error{(errors.Count == 1 ? "" : "s")}", errors[0], 8);

        if (rebootTitles.Count > 0) await PromptRebootAsync(rebootTitles);
    }
}
