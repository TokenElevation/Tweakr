namespace Tweakr.Core.Operations;

/// <summary>One atomic system change (registry value, service start type, ...).</summary>
public interface IOperation
{
    string Type { get; }
    /// <summary>True if the system currently matches the on (true) or off (false) state.</summary>
    bool IsInDesiredState(bool on);
    IEnumerable<Change> Preview(bool on);
    /// <summary>Applies the state and returns a snapshot of the old state.</summary>
    UndoRecord Apply(bool on);
    void Revert(UndoRecord record);
}
