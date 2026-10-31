namespace Tweakr.Tweaks;

public static class BuiltInTweaks
{
    /// <summary>Raw JSON of every embedded definition file. Pass to TweakLoader.Load.</summary>
    public static IEnumerable<string> GetJson()
    {
        var asm = typeof(BuiltInTweaks).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            yield return r.ReadToEnd();
        }
    }
}
