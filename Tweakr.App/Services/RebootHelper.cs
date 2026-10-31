using System.Diagnostics;

namespace Tweakr.App.Services;

/// <summary>"These changes need a restart. Restart now?" shared by the Tweaks, Utilities and Revert flows.</summary>
public static class RebootHelper
{
    public static async Task PromptAsync(DialogService dialog, IReadOnlyList<string> titles)
    {
        var list = string.Join("\n", titles.Select(t => "• " + t));
        bool restart = await dialog.ConfirmAsync("Restart required",
            $"These changes only take full effect after a restart:\n\n{list}\n\nSave your work before restarting.",
            "Restart now", "Later");
        if (restart) Restart();
    }

    public static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Tweakr: restarting to finish applying your changes\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { /* the user can restart manually */ }
    }
}
