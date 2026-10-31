namespace Tweakr.Core;

public sealed class TweakEngine
{
    readonly HistoryStore _history;
    readonly RestorePointService _restore;
    readonly EngineOptions _options;
    readonly Dictionary<string, ITweak> _byId;
    bool _restoreAttempted, _restoreCreated;

    public IReadOnlyList<ITweak> Tweaks { get; }

    public TweakEngine(IReadOnlyList<ITweak> all, HistoryStore history, RestorePointService restore, WindowsVersionInfo os, EngineOptions options)
    {
        _history = history; _restore = restore; _options = options;
        _byId = all.ToDictionary(t => t.Id);
        Tweaks = all.Where(t => t.IsSupported(os)).ToList();   // compatibility gating
    }

    public HistoryStore History => _history;

    public async Task<IReadOnlyList<ChangeSet>> PreviewAsync(IEnumerable<PendingChange> pending)
    {
        var sets = new List<ChangeSet>();
        foreach (var p in pending) sets.Add(await p.Tweak.PreviewAsync(p.Desired));
        return sets;
    }

    public async Task<ApplyReport> ApplyAsync(IEnumerable<PendingChange> pending)
    {
        var list = pending.ToList();
        if (list.Count > 0 && _options.CreateRestorePoint && !_restoreAttempted)
        {
            _restoreAttempted = true;
            _restoreCreated = await _restore.CreateAsync("Tweakr - before applying tweaks");
        }

        var results = new List<ApplyResult>();
        foreach (var c in list)
        {
            var backup = new TransactionBackup();
            try
            {
                await c.Tweak.ApplyAsync(c.Desired, backup);
                _history.Add(new TweakTransaction
                {
                    TweakId = c.Tweak.Id,
                    TweakTitle = c.Tweak.Title,
                    DesiredOn = (bool)c.Desired,
                    Records = backup.Records.ToList()
                });
                results.Add(new(c.Tweak.Id, true, null, c.Tweak.RequiresReboot));
            }
            catch (Exception ex)
            {
                // roll back whatever this tweak already changed
                try { await c.Tweak.RevertAsync(backup); } catch { }
                results.Add(new(c.Tweak.Id, false, ex.Message, false));
            }
        }
        return new ApplyReport(_restoreCreated, results);
    }

    public async Task RevertAsync(Guid transactionId)
    {
        var tx = _history.Items.First(t => t.Id == transactionId);
        if (tx.Reverted) return;
        await _byId[tx.TweakId].RevertAsync(new TransactionBackup(tx.Records));
        tx.Reverted = true;
        _history.Save();
    }

    /// <summary>The panic button: undo everything Tweakr changed, newest first. Returns failures.</summary>
    public async Task<IReadOnlyList<string>> RevertAllAsync()
    {
        var errors = new List<string>();
        foreach (var tx in _history.Items.Where(t => !t.Reverted).OrderByDescending(t => t.Timestamp).ToList())
        {
            try { await RevertAsync(tx.Id); }
            catch (Exception ex) { errors.Add($"{tx.TweakTitle}: {ex.Message}"); }
        }
        return errors;
    }
}
