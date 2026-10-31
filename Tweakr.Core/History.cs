using System.Text.Json;

namespace Tweakr.Core;

public sealed class TweakTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string TweakId { get; init; } = "";
    public string TweakTitle { get; init; } = "";
    public bool DesiredOn { get; init; }
    public List<UndoRecord> Records { get; init; } = new();
    public bool Reverted { get; set; }
}

public sealed class TransactionBackup : IBackupContext
{
    readonly List<UndoRecord> _records;
    public TransactionBackup(List<UndoRecord>? records = null) => _records = records ?? new();
    public void Record(UndoRecord record) => _records.Add(record);
    public IReadOnlyList<UndoRecord> Records => _records;
}

/// <summary>Append-only change log in %LocalAppData%\Tweakr\history.json.</summary>
public sealed class HistoryStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly string _file;
    readonly List<TweakTransaction> _items;

    public HistoryStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakr");
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "history.json");
        try { _items = File.Exists(_file) ? JsonSerializer.Deserialize<List<TweakTransaction>>(File.ReadAllText(_file)) ?? new() : new(); }
        catch { _items = new(); }
    }

    public IReadOnlyList<TweakTransaction> Items => _items;

    public void Add(TweakTransaction tx) { _items.Add(tx); Save(); }

    public void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _file, overwrite: true);
    }
}
