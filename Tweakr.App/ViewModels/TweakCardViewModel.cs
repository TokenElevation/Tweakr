using CommunityToolkit.Mvvm.ComponentModel;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

public sealed partial class TweakCardViewModel : ObservableObject
{
    public TweakCardViewModel(ITweak tweak)
    {
        Tweak = tweak;
        Badges = TweakTags.For(tweak);
    }

    /// <summary>Icon marks: featured star + what kind of optimization this is.</summary>
    public IReadOnlyList<TagBadge> Badges { get; }
    public bool IsFeatured => Tweak.Featured;

    public ITweak Tweak { get; }
    public string Title => Tweak.Title;
    public string Description => Tweak.Description;
    public string Category => Tweak.Category;
    public string Why => Tweak.Why ?? "No extra notes for this tweak.";
    public RiskLevel Risk => Tweak.Risk;
    public string RiskLabel => Tweak.Risk.ToString().ToUpperInvariant();
    public bool RequiresReboot => Tweak.RequiresReboot;

    /// <summary>One-shot tweaks (cleanup etc.) show an Execute button instead of a toggle.</summary>
    public bool IsAction => Tweak.Kind == TweakKind.Action;
    public bool IsToggle => !IsAction;
    [ObservableProperty] private bool _isBusy;
    /// <summary>e.g. "≈ 1.2 GB to clean" (actions only).</summary>
    [ObservableProperty] private string? _estimate;

    /// <summary>Why this tweak can be risky; shown in a tooltip (icon / chip) and in the details section.</summary>
    public string? DangerReason => Tweak.Danger;
    public bool HasDanger => Tweak.Risk != RiskLevel.Safe && !string.IsNullOrWhiteSpace(Tweak.Danger);

    // Segoe Fluent Icons / MDL2 glyph per category
    public string Glyph => CategoryIcons.Glyph(Category);

    /// <summary>What the system is actually set to.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPending))] private bool _current;
    /// <summary>What the card is toggled to (not applied until Apply).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPending))] private bool _desired;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string? _error;

    public bool IsPending => Current != Desired;

    public event Action? PendingChanged;
    /// <summary>true = applied OK (green pulse), false = failed (shake). Handled by CardMotion.</summary>
    public event Action<bool>? Feedback;

    public void NotifyResult(bool ok) => Feedback?.Invoke(ok);

    partial void OnDesiredChanged(bool value) => PendingChanged?.Invoke();
    partial void OnCurrentChanged(bool value) => PendingChanged?.Invoke();
}
