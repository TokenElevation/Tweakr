using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;

namespace Tweakr.Core.Operations;

public sealed record ActionOutcome(long BytesFreed = 0, int Items = 0, int Skipped = 0, string? Note = null);

/// <summary>A one-shot operation (cleanup, empty bin, ...). Unlike IOperation it has no on/off state and can't be reverted.</summary>
public interface IActionOperation
{
    Task<long?> EstimateBytesAsync();
    Task<ActionOutcome> ExecuteAsync();
}

public static class ActionOperationFactory
{
    public static IActionOperation Create(JsonElement e) => e.GetProperty("type").GetString() switch
    {
        "cleanup" => CleanupOperation.FromJson(e),
        "recycleBin" => new RecycleBinOperation(),
        "flushDns" => new FlushDnsOperation(),
        "launch" => LaunchOperation.FromJson(e),
        "killProcesses" => KillProcessesOperation.FromJson(e),
        "restartGraphicsDriver" => new GraphicsDriverResetOperation(),
        var t => throw new NotSupportedException($"Action operation '{t}' is not implemented yet")
    };
}

/// <summary>Deletes the contents of folders (never the folders themselves). Locked files are skipped, junctions are never followed.</summary>
public sealed class CleanupOperation : IActionOperation
{
    static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint      // never follow symlinks / junctions out of the folder
    };

    readonly string[] _paths, _services;
    readonly string _pattern;

    CleanupOperation(string[] paths, string pattern, string[] services) { _paths = paths; _pattern = pattern; _services = services; }

    public static CleanupOperation FromJson(JsonElement e) => new(
        e.GetProperty("paths").EnumerateArray().Select(x => x.GetString()!).ToArray(),
        e.TryGetProperty("pattern", out var p) ? p.GetString() ?? "*" : "*",
        e.TryGetProperty("stopServices", out var s) ? s.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>());

    IEnumerable<string> Roots()
    {
        foreach (var raw in _paths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.Contains('%')) continue;                         // unresolved variable
            var full = Path.GetFullPath(expanded);
            if (full.Length < 10 || Path.GetPathRoot(full) == full) continue;   // refuse drive roots / suspiciously short paths
            if (Directory.Exists(full)) yield return full;
        }
    }

    public Task<long?> EstimateBytesAsync() => Task.Run<long?>(() =>
    {
        long total = 0;
        foreach (var root in Roots())
            foreach (var f in Directory.EnumerateFiles(root, _pattern, Opts))
                try { total += new FileInfo(f).Length; } catch { }
        return total;
    });

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
    {
        var stopped = new List<ServiceController>();
        try
        {
            foreach (var name in _services)
            {
                try
                {
                    var sc = new ServiceController(name);
                    if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                        stopped.Add(sc);
                    }
                    else sc.Dispose();
                }
                catch { /* best effort; files held by the service will simply be skipped */ }
            }

            long freed = 0; int items = 0, skipped = 0;
            foreach (var root in Roots())
            {
                foreach (var f in Directory.EnumerateFiles(root, _pattern, Opts).ToList())
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        long len = fi.Length;
                        if (fi.IsReadOnly) fi.IsReadOnly = false;
                        fi.Delete();
                        freed += len; items++;
                    }
                    catch { skipped++; }
                }
                if (_pattern == "*")
                    foreach (var d in Directory.EnumerateDirectories(root, "*", Opts).OrderByDescending(x => x.Length).ToList())
                        try { Directory.Delete(d, false); } catch { }        // succeeds only for folders that are now empty
            }
            return new ActionOutcome(freed, items, skipped);
        }
        finally
        {
            foreach (var sc in stopped)
            {
                try { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20)); } catch { }
                sc.Dispose();
            }
        }
    });
}

public sealed class RecycleBinOperation : IActionOperation
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    const uint NoConfirmation = 1, NoProgressUi = 2, NoSound = 4;

    static (long size, long items) Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public Task<long?> EstimateBytesAsync() => Task.Run<long?>(() => Query().size);

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
    {
        var (size, items) = Query();
        if (items == 0) return new ActionOutcome(Note: "The Recycle Bin was already empty.");
        int hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (hr < 0) throw new InvalidOperationException($"Windows couldn't empty the Recycle Bin (0x{hr:X8}).");
        return new ActionOutcome(size, (int)items);
    });
}

public sealed class FlushDnsOperation : IActionOperation
{
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    static extern bool DnsFlushResolverCache();

    public Task<long?> EstimateBytesAsync() => Task.FromResult<long?>(null);

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
        DnsFlushResolverCache()
            ? new ActionOutcome(Note: "The DNS resolver cache was flushed.")
            : throw new InvalidOperationException("Windows refused to flush the DNS cache."));
}

/// <summary>Opens a Windows tool (cleanmgr, msinfo32, ms-settings:..., a console command, ...).</summary>
public sealed class LaunchOperation : IActionOperation
{
    readonly string _file, _args, _message;
    LaunchOperation(string file, string args, string message) { _file = file; _args = args; _message = message; }

    public static LaunchOperation FromJson(JsonElement e) => new(
        e.GetProperty("file").GetString()!,
        e.TryGetProperty("args", out var a) ? a.GetString() ?? "" : "",
        e.TryGetProperty("message", out var m) ? m.GetString() ?? "Opened." : "Opened.");

    public Task<long?> EstimateBytesAsync() => Task.FromResult<long?>(null);

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
    {
        Process.Start(new ProcessStartInfo(_file, _args) { UseShellExecute = true });
        return new ActionOutcome(Note: _message);
    });
}

/// <summary>Ends processes that Windows relaunches by itself (Explorer, Start menu, Search): a "restart the Windows UI" fix.</summary>
public sealed class KillProcessesOperation : IActionOperation
{
    readonly string[] _names;
    readonly string _message;
    KillProcessesOperation(string[] names, string message) { _names = names; _message = message; }

    public static KillProcessesOperation FromJson(JsonElement e) => new(
        e.GetProperty("names").EnumerateArray().Select(x => x.GetString()!).ToArray(),
        e.TryGetProperty("message", out var m) ? m.GetString() ?? "Done." : "Done.");

    public Task<long?> EstimateBytesAsync() => Task.FromResult<long?>(null);

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
    {
        int killed = 0;
        foreach (var name in _names)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(); killed++; } catch { }
                p.Dispose();
            }
        return new ActionOutcome(Note: _message);
    });
}

/// <summary>Sends Win+Ctrl+Shift+B, the built-in shortcut that resets the graphics driver (the screen flickers for a second).</summary>
public sealed class GraphicsDriverResetOperation : IActionOperation
{
    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    const uint KeyUp = 2;
    const byte LWin = 0x5B, Ctrl = 0x11, Shift = 0x10, B = 0x42;

    public Task<long?> EstimateBytesAsync() => Task.FromResult<long?>(null);

    public Task<ActionOutcome> ExecuteAsync() => Task.Run(() =>
    {
        byte[] keys = { LWin, Ctrl, Shift, B };
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        Thread.Sleep(80);
        foreach (var k in keys.Reverse()) keybd_event(k, 0, KeyUp, UIntPtr.Zero);
        return new ActionOutcome(Note: "Graphics driver reset requested. The screen may flicker for a moment.");
    });
}
