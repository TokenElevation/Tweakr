using System.Diagnostics;

namespace Tweakr.Core;

public static class PostActions
{
    public static void Run(string? action)
    {
        switch (action)
        {
            case null or "": return;
            case "restartExplorer":
                // Windows relaunches the shell automatically.
                foreach (var p in Process.GetProcessesByName("explorer"))
                    try { p.Kill(); } catch { }
                return;
            default:
                throw new NotSupportedException($"Unknown post action '{action}'");
        }
    }
}
