using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tweakr.App.Services;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

/// <summary>One row on the Utilities page: a tool launcher / quick fix (button), a setting (switch) or a dropdown.</summary>
public sealed partial class UtilityItemViewModel : ObservableObject
{
    readonly Func<UtilityItemViewModel, Task> _toggle, _choice;
    bool _silent;

    public UtilityItemViewModel(ITweak tweak, Func<UtilityItemViewModel, Task> run,
        Func<UtilityItemViewModel, Task> toggle, Func<UtilityItemViewModel, Task> choice)
    {
        Tweak = tweak; _toggle = toggle; _choice = choice;
        Choices = tweak.Choices;
        RunCommand = new AsyncRelayCommand(() => run(this));
    }

    public ITweak Tweak { get; }
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string? Cmd => Tweak.Cmd;
    public bool HasCmd => !string.IsNullOrEmpty(Tweak.Cmd);
    public string Glyph => string.IsNullOrEmpty(Tweak.Icon) ? "\uE713" : Tweak.Icon;
    public string ButtonText => Tweak.ButtonText ?? "Run";
    public bool IsToggle => Tweak.Kind == TweakKind.Toggle;
    public bool IsAction => Tweak.Kind == TweakKind.Action;
    public bool IsChoice => Tweak.Kind == TweakKind.Choice;
    public IReadOnlyList<ChoiceOption> Choices { get; }
    public IAsyncRelayCommand RunCommand { get; }

    [ObservableProperty] private bool _isOn;
    [ObservableProperty] private ChoiceOption? _selectedChoice;

    partial void OnIsOnChanged(bool value) { if (!_silent) _ = _toggle(this); }
    partial void OnSelectedChoiceChanged(ChoiceOption? value) { if (!_silent && value is not null) _ = _choice(this); }

    public void SetOnSilently(bool value) { _silent = true; IsOn = value; _silent = false; }
    public void SetChoiceSilently(ChoiceOption? value) { _silent = true; SelectedChoice = value; _silent = false; }
}

public sealed record UtilityGroup(string Title, IReadOnlyList<UtilityItemViewModel> Items);

public sealed class UtilitiesViewModel
{
    static readonly string[] SectionOrder = { "Quick fixes", "Maintenance", "System tools", "Settings" };

    readonly TweakEngine _engine;
    readonly DialogService _dialog;
    readonly ToastService _toasts;

    public UtilitiesViewModel(TweakEngine engine, DialogService dialog, ToastService toasts)
    { _engine = engine; _dialog = dialog; _toasts = toasts; }

    public ObservableCollection<UtilityGroup> Groups { get; } = new();

    public async Task LoadAsync(IEnumerable<ITweak> tweaks)
    {
        var items = tweaks.Where(t => t.Page == "utilities")
            .Select(t => new UtilityItemViewModel(t, RunAsync, ToggleAsync, ChoiceAsync)).ToList();

        foreach (var item in items)
        {
            try
            {
                if (item.IsToggle) item.SetOnSilently((await item.Tweak.DetectAsync()).IsOn);
                else if (item.IsChoice)
                {
                    var current = await item.Tweak.GetChoiceAsync();
                    item.SetChoiceSilently(item.Choices.FirstOrDefault(c => c.Value == current));
                }
            }
            catch { /* leave the control in its default state */ }
        }

        Groups.Clear();
        foreach (var g in items.GroupBy(i => i.Tweak.Section ?? "Other")
                               .OrderBy(g => { int ix = Array.IndexOf(SectionOrder, g.Key); return ix < 0 ? 99 : ix; }))
            Groups.Add(new UtilityGroup(g.Key.ToUpperInvariant(), g.ToList()));
    }

    // buttons: launch a tool / run a quick fix
    async Task RunAsync(UtilityItemViewModel item)
    {
        try
        {
            var r = await item.Tweak.ExecuteAsync();
            _toasts.Show(r.Success ? ToastKind.Success : ToastKind.Error, item.Title, r.Message);
        }
        catch (Exception ex) { _toasts.Show(ToastKind.Error, $"{item.Title} failed", ex.Message, 7); }
    }

    // switches apply immediately (and are logged, so Revert all still covers them)
    async Task ToggleAsync(UtilityItemViewModel item)
    {
        bool desired = item.IsOn;
        try
        {
            var report = await _engine.ApplyAsync(new[] { new PendingChange(item.Tweak, desired) });
            var r = report.Results[0];
            if (!r.Success)
            {
                item.SetOnSilently(!desired);
                _toasts.Show(ToastKind.Error, $"Couldn't change {item.Title}", r.Error, 7);
                return;
            }
            _toasts.Show(ToastKind.Success, $"{item.Title} {(desired ? "enabled" : "disabled")}");
            if (r.NeedsReboot) await RebootHelper.PromptAsync(_dialog, new[] { item.Title });
        }
        catch (Exception ex)
        {
            item.SetOnSilently(!desired);
            _toasts.Show(ToastKind.Error, $"Couldn't change {item.Title}", ex.Message, 7);
        }
    }

    // dropdowns (power plan)
    async Task ChoiceAsync(UtilityItemViewModel item)
    {
        var selected = item.SelectedChoice;
        if (selected is null) return;
        try
        {
            await item.Tweak.SetChoiceAsync(selected.Value);
            _toasts.Show(ToastKind.Success, item.Title, $"Switched to {selected.Label}.");
        }
        catch (Exception ex)
        {
            _toasts.Show(ToastKind.Error, $"Couldn't change {item.Title}", ex.Message, 7);
            try
            {
                var current = await item.Tweak.GetChoiceAsync();
                item.SetChoiceSilently(item.Choices.FirstOrDefault(c => c.Value == current));
            }
            catch { }
        }
    }
}
