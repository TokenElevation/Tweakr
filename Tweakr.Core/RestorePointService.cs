using System.Diagnostics;

namespace Tweakr.Core;

public sealed class RestorePointService
{
    /// <summary>Note: Windows throttles restore points to one per 24h by default, and System Restore must be enabled.</summary>
    public async Task<bool> CreateAsync(string description)
    {
        try
        {
            var safe = description.Replace("'", "''");
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS\"")
            { CreateNoWindow = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
