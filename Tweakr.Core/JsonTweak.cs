using Tweakr.Core.Operations;

namespace Tweakr.Core;

/// <summary>
/// A declarative tweak. Toggle tweaks have on/off operations; Action tweaks are one-shot (cleanup, launch a tool, ...);
/// Choice tweaks are dropdowns (power plan).
/// </summary>
public sealed class JsonTweak : ITweak
{
    readonly TweakDefinition _def;
    readonly IReadOnlyList<IOperation> _ops;
    readonly IReadOnlyList<IActionOperation> _actions;
    readonly IChoiceOperation? _choice;
    readonly IReadOnlyList<ChoiceOption> _choices;

    public JsonTweak(TweakDefinition def, IReadOnlyList<IOperation> ops,
        IReadOnlyList<IActionOperation>? actions = null, IChoiceOperation? choice = null)
    {
        if (string.IsNullOrWhiteSpace(def.Id)) throw new ArgumentException("Tweak has no id");
        _actions = actions ?? Array.Empty<IActionOperation>();
        _choice = choice;
        _choices = def.Choices.Select(c => new ChoiceOption(c.Label, c.Value)).ToList();

        bool missing = def.Kind switch
        {
            TweakKind.Action => _actions.Count == 0,
            TweakKind.Choice => _choice is null || _choices.Count == 0,
            _ => ops.Count == 0
        };
        if (missing) throw new ArgumentException($"Tweak '{def.Id}' has no operations");
        _def = def; _ops = ops;
    }

    public string Id => _def.Id;
    public string Title => _def.Title;
    public string Description => _def.Description;
    public string Category => _def.Category;
    public string? Why => _def.Why;
    public string? Danger => _def.Danger;
    public RiskLevel Risk => _def.Risk;
    public TweakKind Kind => _def.Kind;
    public bool RequiresReboot => _def.RequiresReboot;

    public IReadOnlyList<string> Tags => _def.Tags;
    public bool Featured => _def.Featured;
    public string? Page => _def.Page;
    public string? Section => _def.Section;
    public string? Icon => _def.Icon;
    public string? Cmd => _def.Cmd;
    public string? ButtonText => _def.ButtonText;
    public IReadOnlyList<ChoiceOption> Choices => _choices;

    bool IsToggle => _def.Kind == TweakKind.Toggle || _def.Kind == TweakKind.Slider;
    bool IsAction => _def.Kind == TweakKind.Action;

    public bool IsSupported(WindowsVersionInfo os) =>
        os.Build >= (_def.Os?.MinBuild ?? 0) && os.Build <= (_def.Os?.MaxBuild ?? int.MaxValue);

    public Task<TweakState> DetectAsync() => IsToggle
        ? Task.Run(() => new TweakState(_ops.All(o => o.IsInDesiredState(true))))
        : Task.FromResult(new TweakState(false));

    public Task<ChangeSet> PreviewAsync(object desired) => !IsToggle
        ? Task.FromResult(new ChangeSet(Id, Title, Array.Empty<Change>()))
        : Task.Run(() =>
        {
            bool on = (bool)desired;
            var changes = _ops.SelectMany(o => o.Preview(on)).Where(c => c.OldValue != c.NewValue).ToList();
            return new ChangeSet(Id, Title, changes);
        });

    public Task ApplyAsync(object desired, IBackupContext backup)
    {
        if (!IsToggle) throw new InvalidOperationException("Only toggle tweaks are applied.");
        return Task.Run(() =>
        {
            bool on = (bool)desired;
            for (int i = 0; i < _ops.Count; i++)
                backup.Record(_ops[i].Apply(on) with { OpIndex = i });
            PostActions.Run(_def.PostAction);
        });
    }

    public Task RevertAsync(IBackupContext backup)
    {
        if (!IsToggle) return Task.CompletedTask;
        return Task.Run(() =>
        {
            foreach (var rec in backup.Records.Reverse())
                _ops[rec.OpIndex].Revert(rec);
            if (backup.Records.Count > 0) PostActions.Run(_def.PostAction);
        });
    }

    public async Task<ActionResult> ExecuteAsync()
    {
        if (!IsAction) return new ActionResult(false, "This tweak is not an action.");

        long bytes = 0; int items = 0, skipped = 0;
        var notes = new List<string>();
        foreach (var op in _actions)
        {
            var r = await op.ExecuteAsync();
            bytes += r.BytesFreed; items += r.Items; skipped += r.Skipped;
            if (!string.IsNullOrEmpty(r.Note)) notes.Add(r.Note);
        }

        var parts = new List<string>();
        if (bytes > 0 || items > 0) parts.Add($"Freed {ByteSize.Format(bytes)} by removing {items:N0} item{(items == 1 ? "" : "s")}.");
        if (skipped > 0) parts.Add($"{skipped:N0} item{(skipped == 1 ? " was" : "s were")} in use and skipped.");
        parts.AddRange(notes);
        if (parts.Count == 0) parts.Add("Nothing needed cleaning.");
        PostActions.Run(_def.PostAction);
        return new ActionResult(true, string.Join("\n", parts));
    }

    public async Task<string?> EstimateAsync()
    {
        if (!IsAction) return null;
        long total = 0; bool known = false;
        foreach (var op in _actions)
        {
            var b = await op.EstimateBytesAsync();
            if (b is not null) { total += b.Value; known = true; }
        }
        return known ? (total == 0 ? "Already clean" : $"≈ {ByteSize.Format(total)} to clean") : null;
    }

    public Task<string?> GetChoiceAsync() => _choice is null ? Task.FromResult<string?>(null) : _choice.GetAsync(_choices);

    public Task SetChoiceAsync(string value) => _choice is null ? Task.CompletedTask : _choice.SetAsync(value);
}
