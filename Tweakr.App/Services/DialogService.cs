using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tweakr.App.Services;

public sealed record DialogChange(string Target, string Old, string New);
public sealed record DialogChangeGroup(string Title, IReadOnlyList<DialogChange> Items);

/// <summary>In-app modal dialog (replaces MessageBox). The overlay lives in MainWindow and binds to this.</summary>
public sealed partial class DialogService : ObservableObject
{
    TaskCompletionSource<bool>? _tcs;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _primaryText = "OK";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSecondary))] private string? _secondaryText;
    [ObservableProperty] private bool _isDanger;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChanges))] private IReadOnlyList<DialogChangeGroup>? _changes;

    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryText);
    public bool HasChanges => Changes is { Count: > 0 };

    /// <summary>true = primary button, false = secondary / Esc. Pass secondary=null for an alert with a single button.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string primary = "OK", string? secondary = "Cancel",
        bool danger = false, IReadOnlyList<DialogChangeGroup>? changes = null)
    {
        _tcs?.TrySetResult(false);
        Title = title; Message = message; PrimaryText = primary; SecondaryText = secondary;
        IsDanger = danger; Changes = changes;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        IsOpen = true;
        return tcs.Task;
    }

    [RelayCommand] private void Primary() => Close(true);
    [RelayCommand] private void Secondary() => Close(false);

    public void Confirm() => Close(true);   // Enter
    public void Cancel() => Close(false);   // Esc

    void Close(bool result)
    {
        var t = _tcs;
        _tcs = null;
        IsOpen = false;
        t?.TrySetResult(result);
    }
}
