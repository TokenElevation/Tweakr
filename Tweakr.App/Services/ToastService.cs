using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tweakr.App.Services;

public enum ToastKind { Info, Success, Warning, Error }

public sealed partial class ToastItem : ObservableObject
{
    readonly Action<ToastItem> _dismiss;

    public ToastItem(ToastKind kind, string title, string? message, Action<ToastItem> dismiss)
    {
        Kind = kind; Title = title; Message = message; _dismiss = dismiss;
    }

    public ToastKind Kind { get; }
    public string Title { get; }
    public string? Message { get; }
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string Glyph => Kind switch
    {
        ToastKind.Success => "\uE73E",
        ToastKind.Warning => "\uE7BA",
        ToastKind.Error => "\uE711",
        _ => "\uE946"
    };

    [ObservableProperty] private bool _isClosing;

    [RelayCommand] private void Close() => _dismiss(this);
}

/// <summary>Stackable, auto-dismissing in-app notifications (bottom-right).</summary>
public sealed class ToastService
{
    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(ToastKind kind, string title, string? message = null, double seconds = 4.5)
    {
        var t = new ToastItem(kind, title, message, Dismiss);
        Items.Add(t);
        while (Items.Count(i => !i.IsClosing) > 4) Dismiss(Items.First(i => !i.IsClosing));
        _ = AutoDismiss(t, seconds);
    }

    async Task AutoDismiss(ToastItem t, double seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        Dismiss(t);
    }

    public async void Dismiss(ToastItem t)
    {
        if (t.IsClosing) return;
        t.IsClosing = true;            // XAML plays the exit animation
        await Task.Delay(300);
        Items.Remove(t);
    }
}
