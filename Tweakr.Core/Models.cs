namespace Tweakr.Core;

public enum RiskLevel { Safe, Moderate, Advanced }
public enum TweakKind { Toggle, Choice, Slider, Action }

public sealed record WindowsVersionInfo(int Build)
{
    public bool IsWindows11 => Build >= 22000;
    public static WindowsVersionInfo Current => new(Environment.OSVersion.Version.Build);
}

public sealed record TweakState(bool IsOn);
public sealed record Change(string Target, string OldValue, string NewValue);
public sealed record ChangeSet(string TweakId, string Title, IReadOnlyList<Change> Changes);

/// <summary>Snapshot of what one operation changed; enough to undo it.</summary>
public sealed record UndoRecord(string Type, Dictionary<string, string?> Data)
{
    public int OpIndex { get; init; }
}

public sealed record PendingChange(ITweak Tweak, object Desired);
public sealed record ApplyResult(string TweakId, bool Success, string? Error, bool NeedsReboot);
public sealed record ApplyReport(bool RestorePointCreated, IReadOnlyList<ApplyResult> Results);
