using System.IO;
using Tweakr.Core;

namespace Tweakr.App.Services;

/// <summary>What happens when Tweakr is launched with --startup: apply the selected tweaks silently, then exit.</summary>
public sealed class StartupRunner
{
    readonly TweakEngine _engine;
    readonly SettingsStore _store;
    readonly EngineOptions _options;

    public StartupRunner(TweakEngine engine, SettingsStore store, EngineOptions options)
    { _engine = engine; _store = store; _options = options; }

    public async Task RunAsync()
    {
        var settings = _store.Load();
        if (!settings.StartupEnabled || settings.StartupTweakIds.Count == 0) return;

        _options.CreateRestorePoint = false;      // never create a restore point at every sign-in
        int applied = 0, already = 0, ran = 0, failed = 0;
        var log = new List<string>();

        foreach (var id in settings.StartupTweakIds)
        {
            var tweak = _engine.Tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null) continue;          // not available on this Windows build

            try
            {
                if (tweak.Kind == TweakKind.Action)
                {
                    var r = await tweak.ExecuteAsync();
                    if (r.Success) { ran++; log.Add($"ran      {tweak.Title}: {r.Message.Replace('\n', ' ')}"); }
                    else { failed++; log.Add($"FAILED   {tweak.Title}: {r.Message}"); }
                }
                else
                {
                    if ((await tweak.DetectAsync()).IsOn) { already++; continue; }       // still on: nothing to do
                    var report = await _engine.ApplyAsync(new[] { new PendingChange(tweak, true) });
                    var res = report.Results[0];
                    if (res.Success) { applied++; log.Add($"applied  {tweak.Title}"); }
                    else { failed++; log.Add($"FAILED   {tweak.Title}: {res.Error}"); }
                }
            }
            catch (Exception ex) { failed++; log.Add($"FAILED   {tweak.Title}: {ex.Message}"); }
        }

        var summary = $"applied {applied}, ran {ran}, already on {already}, failed {failed}";
        settings = _store.Load();                 // re-read: the UI may have saved in the meantime
        settings.LastStartupRun = DateTime.Now.ToString("g");
        settings.LastStartupSummary = summary;
        _store.Save(settings);

        try
        {
            File.AppendAllLines(Path.Combine(_store.Directory, "startup.log"),
                log.Prepend($"[{DateTime.Now:s}] {summary}"));
        }
        catch { }
    }
}
