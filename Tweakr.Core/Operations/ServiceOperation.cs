using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace Tweakr.Core.Operations;

/// <summary>Changes a service's start type via its registry Start value and stops/starts it.</summary>
public sealed class ServiceOperation : IOperation
{
    readonly string _service;
    readonly int _on, _off;
    public string Type => "service";

    ServiceOperation(string service, int on, int off) { _service = service; _on = on; _off = off; }

    public static ServiceOperation FromJson(JsonElement e) => new(
        e.GetProperty("name").GetString()!,
        ParseStart(e.GetProperty("on").GetString()!),
        ParseStart(e.GetProperty("off").GetString()!));

    static int ParseStart(string s) => s.ToLowerInvariant() switch
    {
        "auto" => 2, "manual" => 3, "disabled" => 4,
        _ => throw new NotSupportedException($"Unknown start type '{s}'")
    };

    static string StartName(int? n) => n switch { 2 => "Automatic", 3 => "Manual", 4 => "Disabled", null => "(missing)", _ => n.ToString()! };
    string KeyPath => $@"SYSTEM\CurrentControlSet\Services\{_service}";

    int? ReadStart()
    {
        using var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = b.OpenSubKey(KeyPath);
        return k?.GetValue("Start") as int?;
    }

    void WriteStart(int value)
    {
        using var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var k = b.OpenSubKey(KeyPath, true) ?? throw new InvalidOperationException($"Service '{_service}' not found");
        k.SetValue("Start", value, RegistryValueKind.DWord);
    }

    public bool IsInDesiredState(bool on) => ReadStart() == (on ? _on : _off);

    public IEnumerable<Change> Preview(bool on)
    {
        yield return new Change($"Service {_service} start type", StartName(ReadStart()), StartName(on ? _on : _off));
    }

    public UndoRecord Apply(bool on)
    {
        var old = ReadStart() ?? throw new InvalidOperationException($"Service '{_service}' not found");
        using var sc = new ServiceController(_service);
        bool wasRunning = sc.Status == ServiceControllerStatus.Running;
        var record = new UndoRecord(Type, new Dictionary<string, string?>
        {
            ["start"] = old.ToString(), ["wasRunning"] = wasRunning.ToString()
        });

        var target = on ? _on : _off;
        WriteStart(target);
        if (target == 4 && wasRunning && sc.CanStop)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        }
        return record;
    }

    public void Revert(UndoRecord r)
    {
        var start = int.Parse(r.Data["start"]!);
        WriteStart(start);
        if (bool.Parse(r.Data["wasRunning"]!) && start != 4)
        {
            try
            {
                using var sc = new ServiceController(_service);
                if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
            }
            catch { /* best effort; it will start on next boot anyway */ }
        }
    }
}
