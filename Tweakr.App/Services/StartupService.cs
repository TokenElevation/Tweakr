using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Tweakr.App.Services;

/// <summary>
/// Registers Tweakr to run at sign-in via a Task Scheduler task with "highest privileges".
/// (A plain Run-key entry can't start an app that needs admin rights, so a scheduled task is the correct way.)
/// The task launches the exe with --startup: no window, it applies the selected tweaks and exits.
/// </summary>
public sealed class StartupService
{
    const string TaskName = "Tweakr Startup";

    public async Task<bool> IsRegisteredAsync() => (await RunAsync("/Query", "/TN", TaskName)).exit == 0;

    public async Task<(bool ok, string? error)> RegisterAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"tweakr-task-{Guid.NewGuid():N}.xml");
        try
        {
            var exe = ExePath();
            await File.WriteAllTextAsync(tmp, BuildXml(exe, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);   // schtasks wants UTF-16
            var r = await RunAsync("/Create", "/TN", TaskName, "/XML", tmp, "/F");
            return r.exit == 0 ? (true, null) : (false, r.output.Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    public async Task<(bool ok, string? error)> UnregisterAsync()
    {
        var r = await RunAsync("/Delete", "/TN", TaskName, "/F");
        if (r.exit == 0 || !await IsRegisteredAsync()) return (true, null);    // already gone is fine
        return (false, r.output.Trim());
    }

    static string ExePath()
    {
        var p = Environment.ProcessPath;
        if (p is null || Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            p = Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
        return p;
    }

    static string BuildXml(string exe, string user)
    {
        string e(string s) => SecurityElement.Escape(s) ?? "";
        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Applies the tweaks you selected in Tweakr when you sign in.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{e(user)}</UserId>
              <Delay>PT20S</Delay>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{e(user)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <ExecutionTimeLimit>PT15M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{e(exe)}</Command>
              <Arguments>--startup</Arguments>
              <WorkingDirectory>{e(Path.GetDirectoryName(exe) ?? "")}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    static async Task<(int exit, string output)> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, (await stdout) + (await stderr));
    }
}
