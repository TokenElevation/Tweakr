namespace Tweakr.Core;

public interface IBackupContext
{
    void Record(UndoRecord record);
    IReadOnlyList<UndoRecord> Records { get; }
}

public interface ITweak
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    string Category { get; }
    string? Why { get; }
    /// <summary>Why this tweak can be risky (shown in a tooltip on the icon).</summary>
    string? Danger { get; }
    RiskLevel Risk { get; }
    TweakKind Kind { get; }
    bool RequiresReboot { get; }

    // ---- presentation metadata ----
    /// <summary>appearance, privacy, performance, gaming, network, security, cleanup. Empty = derived from the category.</summary>
    IReadOnlyList<string> Tags { get; }
    bool Featured { get; }
    /// <summary>"utilities" = shown on the Utilities page instead of the Tweaks grid.</summary>
    string? Page { get; }
    string? Section { get; }
    string? Icon { get; }
    /// <summary>Command shown as a "CMD: xyz" chip on Utilities rows.</summary>
    string? Cmd { get; }
    string? ButtonText { get; }
    IReadOnlyList<ChoiceOption> Choices { get; }

    bool IsSupported(WindowsVersionInfo os);
    Task<TweakState> DetectAsync();
    Task<ChangeSet> PreviewAsync(object desired);
    Task ApplyAsync(object desired, IBackupContext backup);
    Task RevertAsync(IBackupContext backup);

    /// <summary>Action tweaks (Kind == Action) are one-shot: they run instead of toggling. Not undoable.</summary>
    Task<ActionResult> ExecuteAsync();
    /// <summary>Optional size preview for actions, e.g. "≈ 1.2 GB to clean". Null when unknown / not applicable.</summary>
    Task<string?> EstimateAsync();

    /// <summary>Choice tweaks (dropdowns, e.g. power plan): current value and setter.</summary>
    Task<string?> GetChoiceAsync();
    Task SetChoiceAsync(string value);
}
