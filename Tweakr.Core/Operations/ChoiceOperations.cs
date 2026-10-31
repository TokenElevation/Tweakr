using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tweakr.Core.Operations;

/// <summary>A dropdown-style setting: read the current value, set a new one.</summary>
public interface IChoiceOperation
{
    Task<string?> GetAsync(IReadOnlyList<ChoiceOption> options);
    Task SetAsync(string value);
}

public static class ChoiceOperationFactory
{
    public static IChoiceOperation Create(JsonElement e) => e.GetProperty("type").GetString() switch
    {
        "powerPlan" => new PowerPlanOperation(),
        var t => throw new NotSupportedException($"Choice operation '{t}' is not implemented yet")
    };
}

/// <summary>Switches the active power plan with powercfg. Plans Windows hides (High / Ultimate performance) are created on demand.</summary>
public sealed class PowerPlanOperation : IChoiceOperation
{
    static readonly Regex GuidRx = new(@"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    static readonly Regex NameRx = new(@"\(([^)]*)\)", RegexOptions.Compiled);

    static (int exit, string output) Run(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, text);
    }

    public Task<string?> GetAsync(IReadOnlyList<ChoiceOption> options) => Task.Run<string?>(() =>
    {
        var (_, output) = Run("/getactivescheme");
        var m = GuidRx.Match(output);
        if (!m.Success) return null;

        var byGuid = options.FirstOrDefault(o => o.Value.Equals(m.Value, StringComparison.OrdinalIgnoreCase));
        if (byGuid is not null) return byGuid.Value;

        // a plan we duplicated has a new GUID but keeps its name, e.g. "(Ultimate Performance)"
        var name = NameRx.Match(output);
        return name.Success
            ? options.FirstOrDefault(o => o.Label.Equals(name.Groups[1].Value.Trim(), StringComparison.OrdinalIgnoreCase))?.Value
            : null;
    });

    public Task SetAsync(string value) => Task.Run(() =>
    {
        if (!Regex.IsMatch(value, @"^[0-9a-fA-F\-]{36}$")) throw new ArgumentException("Invalid power plan id.");

        if (Run($"/setactive {value}").exit == 0) return;

        // not present on this PC (hidden on many laptops): create it from the built-in template, then activate that copy
        var (exit, output) = Run($"/duplicatescheme {value}");
        var m = GuidRx.Match(output);
        if (exit != 0 || !m.Success) throw new InvalidOperationException("This power plan isn't available on this PC.");
        if (Run($"/setactive {m.Value}").exit != 0) throw new InvalidOperationException("Windows refused to switch to that power plan.");
    });
}
