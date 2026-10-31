using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakr.App.Services;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

public sealed partial class BloatItemViewModel : ObservableObject
{
    readonly Action _changed;

    public BloatItemViewModel(InstalledApp app, BloatDef def, Action changed)
    {
        PackageName = app.Name; Title = def.Title; Description = def.Description; Level = def.Level; _changed = changed;
    }

    public string PackageName { get; }
    public string Title { get; }
    public string Description { get; }
    public BloatLevel Level { get; }
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";
    public string LevelLabel => Level.ToString().ToUpperInvariant();

    /// <summary>Reuses the green / amber / red chip style of the tweak cards.</summary>
    public RiskLevel Risk => Level switch { BloatLevel.Recommended => RiskLevel.Safe, BloatLevel.Optional => RiskLevel.Moderate, _ => RiskLevel.Advanced };

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _isRemoved;
    /// <summary>The app's real icon; null until loaded (the UI shows the first letter meanwhile).</summary>
    [ObservableProperty] private ImageSource? _icon;

    partial void OnIsSelectedChanged(bool value) => _changed();
}

/// <summary>The Debloat+ page: scan for preinstalled apps from a curated list, pick some, remove them.</summary>
public sealed partial class DebloatViewModel : ObservableObject
{
    readonly DebloatService _service;
    readonly DialogService _dialog;
    readonly ToastService _toasts;
    bool _scanned;
    bool _bulkSelecting;   // SelectRecommended / Clear touch every item; recount once at the end instead of N times

    public DebloatViewModel(DebloatService service, DialogService dialog, ToastService toasts)
    {
        _service = service; _dialog = dialog; _toasts = toasts;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = o => o is BloatItemViewModel i && (Filter == "All" || i.Level.ToString() == Filter);
    }

    public ObservableCollection<BloatItemViewModel> Items { get; } = new();
    public ICollectionView View { get; }
    public IReadOnlyList<string> FilterOptions { get; } = new[] { "All", "Recommended", "Optional", "Careful" };

    [ObservableProperty] private string _filter = "All";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isScanning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _isWorking;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoveLabel)), NotifyPropertyChangedFor(nameof(HasSelection))] private int _selectedCount;
    [ObservableProperty] private bool _includeImage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmpty))] private bool _scanDone;

    public bool IsIdle => !IsScanning && !IsWorking;
    public bool HasSelection => SelectedCount > 0;
    public bool ShowEmpty => ScanDone && Items.Count == 0;
    public string RemoveLabel => SelectedCount == 0 ? "Remove selected" : $"Remove selected ({SelectedCount})";

    partial void OnFilterChanged(string value) => View.Refresh();

    void RefreshSelection()
    {
        if (_bulkSelecting) return;
        SelectedCount = Items.Count(i => i.IsSelected && !i.IsRemoved);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_scanned) await RescanAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (!IsIdle) return;
        IsScanning = true;
        List<BloatItemViewModel> loaded = new();
        try
        {
            var apps = await _service.ScanAsync();

            // build + sort first, then fill the collection once (recommended first, then optional, then careful).
            // The old version cleared and re-added everything twice, which made the filtered view re-process every row.
            var found = new List<BloatItemViewModel>();
            foreach (var app in apps)
                if (BloatCatalog.Match(app.Name) is { } def)
                    found.Add(new BloatItemViewModel(app, def, RefreshSelection));

            loaded = found.OrderBy(i => i.Level).ThenBy(i => i.Title).ToList();

            Items.Clear();
            foreach (var i in loaded) Items.Add(i);
            _scanned = true;
        }
        catch (Exception ex)
        {
            _toasts.Show(ToastKind.Error, "Couldn't scan installed apps", ex.Message, 7);
        }
        finally
        {
            IsScanning = false;
            ScanDone = true;
            OnPropertyChanged(nameof(ShowEmpty));
            RefreshSelection();
        }

        if (loaded.Count > 0) _ = LoadIconsAsync(loaded);   // after the list is on screen; letters show until icons arrive
    }

    /// <summary>Reads each app's logo from its package folder on a background thread and hands it to the row.</summary>
    static async Task LoadIconsAsync(List<BloatItemViewModel> items)
    {
        var dispatcher = Application.Current.Dispatcher;
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                var icon = AppIconLoader.Load(AppIconLoader.FindInstallLocation(item.PackageName));
                if (icon is not null)
                    dispatcher.BeginInvoke(new Action(() => item.Icon = icon), DispatcherPriority.Background);
            }
        });
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        _bulkSelecting = true;
        try { foreach (var i in Items) i.IsSelected = !i.IsRemoved && i.Level == BloatLevel.Recommended; }
        finally { _bulkSelecting = false; RefreshSelection(); }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _bulkSelecting = true;
        try { foreach (var i in Items) i.IsSelected = false; }
        finally { _bulkSelecting = false; RefreshSelection(); }
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        if (!IsIdle) return;
        var selected = Items.Where(i => i.IsSelected && !i.IsRemoved).ToList();
        if (selected.Count == 0) return;

        bool careful = selected.Any(i => i.Level == BloatLevel.Careful);
        var names = string.Join("\n", selected.Take(8).Select(i => "• " + i.Title)) + (selected.Count > 8 ? $"\n…and {selected.Count - 8} more" : "");
        var message = $"These apps will be removed for all users:\n\n{names}\n\nYou can reinstall any of them from the Microsoft Store later."
                    + (careful ? "\n\nSome of the selected apps are marked Careful: removing them can break other apps or games." : "");

        if (!await _dialog.ConfirmAsync($"Remove {selected.Count} app{(selected.Count == 1 ? "" : "s")}?", message, "Remove", "Cancel", danger: careful)) return;

        IsWorking = true;
        int ok = 0, failed = 0;
        string? firstError = null;
        try
        {
            foreach (var item in selected)
            {
                item.Status = "Removing…";
                var (success, error) = await _service.RemoveAsync(item.PackageName, IncludeImage);
                if (success) { item.IsRemoved = true; item.IsSelected = false; item.Status = "Removed"; ok++; }
                else { item.Status = "Failed"; failed++; firstError ??= $"{item.Title}: {error}"; }
            }
        }
        finally { IsWorking = false; }

        if (ok > 0) _toasts.Show(ToastKind.Success, $"Removed {ok} app{(ok == 1 ? "" : "s")}");
        if (failed > 0) _toasts.Show(ToastKind.Error, $"{failed} app{(failed == 1 ? "" : "s")} couldn't be removed", firstError, 8);

        await Task.Delay(1200);                       // let "Removed" be seen, then drop the rows
        foreach (var gone in Items.Where(i => i.IsRemoved).ToList()) Items.Remove(gone);
        OnPropertyChanged(nameof(ShowEmpty));
        RefreshSelection();
    }
}