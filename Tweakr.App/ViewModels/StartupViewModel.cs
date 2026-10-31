using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tweakr.App.Services;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

public sealed partial class StartupItemViewModel : ObservableObject
{
    readonly Func<StartupItemViewModel, Task> _changed;
    bool _silent;

    public StartupItemViewModel(ITweak tweak, bool selected, Func<StartupItemViewModel, Task> changed)
    {
        Tweak = tweak; _isSelected = selected; _changed = changed;
    }

    public ITweak Tweak { get; }
    public string Title => Tweak.Title;
    public RiskLevel Risk => Tweak.Risk;
    public string RiskLabel => Tweak.Risk.ToString().ToUpperInvariant();
    public string Glyph => CategoryIcons.Glyph(Tweak.Category);
    public string KindText => $"{Tweak.Category} · " + (Tweak.Kind == TweakKind.Action ? "runs every sign-in" : "re-applied every sign-in if it was reset");

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) { if (!_silent) _ = _changed(this); }

    public void SetSilently(bool value) { _silent = true; IsSelected = value; _silent = false; }
}

/// <summary>The Startup tab: pick tweaks to apply at every sign-in, and add/remove the Task Scheduler entry that makes it happen.</summary>
public sealed partial class StartupViewModel : ObservableObject
{
    readonly StartupService _service;
    readonly SettingsStore _store;
    readonly DialogService _dialog;
    readonly ToastService _toasts;
    bool _loading;

    public StartupViewModel(StartupService service, SettingsStore store, DialogService dialog, ToastService toasts)
    { _service = service; _store = store; _dialog = dialog; _toasts = toasts; }

    public ObservableCollection<StartupItemViewModel> Items { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTaskHint))] private bool _runAtStartup;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTaskHint)), NotifyPropertyChangedFor(nameof(SelectedSummary))] private int _selectedCount;
    [ObservableProperty] private string _lastRunText = "Hasn't run yet.";

    public string SelectedSummary => SelectedCount == 0 ? "No tweaks selected" : $"{SelectedCount} selected";
    /// <summary>Picked tweaks but the startup task is off: they won't run until it's turned on.</summary>
    public bool ShowTaskHint => !RunAtStartup && SelectedCount > 0;

    public async Task LoadAsync(IEnumerable<ITweak> tweaks)
    {
        var s = _store.Load();
        _loading = true;
        Items.Clear();
        foreach (var t in tweaks.OrderBy(t => t.Category).ThenBy(t => t.Title))
            Items.Add(new StartupItemViewModel(t, s.StartupTweakIds.Contains(t.Id), OnItemChanged));
        SelectedCount = Items.Count(i => i.IsSelected);
        RunAtStartup = await _service.IsRegisteredAsync();       // reflect what Windows really has, not just our setting
        LastRunText = string.IsNullOrEmpty(s.LastStartupRun) ? "Hasn't run yet." : $"Last run {s.LastStartupRun}: {s.LastStartupSummary}";
        _loading = false;
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (!_loading) _ = ApplyTaskAsync(value);
    }

    async Task ApplyTaskAsync(bool on)
    {
        var (ok, error) = on ? await _service.RegisterAsync() : await _service.UnregisterAsync();
        if (!ok)
        {
            _loading = true; RunAtStartup = !on; _loading = false;                    // put the switch back
            _toasts.Show(ToastKind.Error, on ? "Couldn't add Tweakr to startup" : "Couldn't remove the startup task", error, 8);
            return;
        }
        Save(s => s.StartupEnabled = on);
        _toasts.Show(ToastKind.Success, on ? "Tweakr will run at sign-in" : "Removed from startup");
        if (on && SelectedCount == 0)
            _toasts.Show(ToastKind.Info, "Pick some tweaks below", "Nothing is applied until you select at least one.");
    }

    async Task OnItemChanged(StartupItemViewModel item)
    {
        try
        {
            if (item.IsSelected && item.Tweak.Risk == RiskLevel.Advanced)
            {
                var ok = await _dialog.ConfirmAsync($"Apply “{item.Title}” at every startup?",
                    (item.Tweak.Danger ?? "This is an advanced change.") + "\n\nIt will be applied silently every time you sign in.",
                    "Add to startup", "Cancel", danger: true);
                if (!ok) { item.SetSilently(false); return; }
            }
            SelectedCount = Items.Count(i => i.IsSelected);
            Save(s => s.StartupTweakIds = Items.Where(i => i.IsSelected).Select(i => i.Tweak.Id).ToList());
        }
        catch { /* never let a settings write take the UI down */ }
    }

    void Save(Action<AppSettings> change)
    {
        var s = _store.Load();      // load-modify-save: other settings stay untouched
        change(s);
        _store.Save(s);
    }
}
