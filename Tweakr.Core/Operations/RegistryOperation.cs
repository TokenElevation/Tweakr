using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace Tweakr.Core.Operations;

public sealed class RegistryOperation : IOperation
{
    static readonly object DeleteValueMark = new();
    static readonly object DeleteKeyMark = new();

    readonly RegistryHive _hive;
    readonly string _path, _name, _valueName;
    readonly RegistryValueKind _kind;
    readonly object _on, _off;

    public string Type => "registry";

    RegistryOperation(RegistryHive hive, string path, string name, RegistryValueKind kind, object on, object off)
    {
        _hive = hive; _path = path; _name = name; _kind = kind; _on = on; _off = off;
        _valueName = name == "(Default)" ? "" : name;
    }

    public static RegistryOperation FromJson(JsonElement e)
    {
        var hive = e.GetProperty("hive").GetString() switch
        {
            "HKCU" => RegistryHive.CurrentUser,
            "HKLM" => RegistryHive.LocalMachine,
            var h => throw new NotSupportedException($"Hive '{h}' not supported")
        };
        var kind = Enum.Parse<RegistryValueKind>(e.TryGetProperty("kind", out var k) ? k.GetString()! : "DWord", true);
        return new RegistryOperation(hive, e.GetProperty("path").GetString()!, e.GetProperty("name").GetString()!,
            kind, ParseTarget(e.GetProperty("on"), kind), ParseTarget(e.GetProperty("off"), kind));
    }

    static object ParseTarget(JsonElement e, RegistryValueKind kind)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString()!;
            if (s == "<delete>") return DeleteValueMark;
            if (s == "<delete-key>") return DeleteKeyMark;
        }
        return kind switch
        {
            RegistryValueKind.DWord => unchecked((int)e.GetInt64()),   // allows 4294967295
            RegistryValueKind.QWord => e.GetInt64(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => e.GetString()!,
            _ => throw new NotSupportedException($"Registry kind {kind} not supported yet")
        };
    }

    (bool keyExists, object? value, RegistryValueKind kind) Read()
    {
        using var b = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        using var k = b.OpenSubKey(_path);
        if (k is null) return (false, null, default);
        var v = k.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return (true, v, v is null ? default : k.GetValueKind(_valueName));
    }

    public bool IsInDesiredState(bool on)
    {
        var t = on ? _on : _off;
        var (keyExists, v, _) = Read();
        if (ReferenceEquals(t, DeleteKeyMark)) return !keyExists;
        if (ReferenceEquals(t, DeleteValueMark)) return v is null;
        return v is not null && v.Equals(t);
    }

    static string Show(object? v)
    {
        if (v is null) return "(not set)";
        if (ReferenceEquals(v, DeleteValueMark)) return "(deleted)";
        if (ReferenceEquals(v, DeleteKeyMark)) return "(key removed)";
        if (v is string { Length: 0 }) return "(empty)";
        return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
    }

    public IEnumerable<Change> Preview(bool on)
    {
        var (_, cur, _) = Read();
        var hive = _hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        yield return new Change($@"{hive}\{_path}\{_name}", Show(cur), Show(on ? _on : _off));
    }

    public UndoRecord Apply(bool on)
    {
        var target = on ? _on : _off;
        var (keyExists, old, oldKind) = Read();
        var record = new UndoRecord(Type, new Dictionary<string, string?>
        {
            ["keyExisted"] = keyExists.ToString(),
            ["valueExisted"] = (old is not null).ToString(),
            ["kind"] = oldKind.ToString(),
            ["data"] = old is null ? null : Convert.ToString(old, CultureInfo.InvariantCulture)
        });

        using var b = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        if (ReferenceEquals(target, DeleteKeyMark))
            b.DeleteSubKeyTree(_path, false);
        else if (ReferenceEquals(target, DeleteValueMark))
        {
            using var k = b.OpenSubKey(_path, true);
            k?.DeleteValue(_valueName, false);
        }
        else
        {
            using var k = b.CreateSubKey(_path, true);
            k.SetValue(_valueName, target, _kind);
        }
        return record;
    }

    public void Revert(UndoRecord r)
    {
        bool keyExisted = bool.Parse(r.Data["keyExisted"]!);
        bool valueExisted = bool.Parse(r.Data["valueExisted"]!);
        using var b = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);

        if (!keyExisted) { b.DeleteSubKeyTree(_path, false); return; }
        if (!valueExisted)
        {
            using var k = b.OpenSubKey(_path, true);
            k?.DeleteValue(_valueName, false);
            return;
        }
        var kind = Enum.Parse<RegistryValueKind>(r.Data["kind"]!);
        var raw = r.Data["data"]!;
        object val = kind switch
        {
            RegistryValueKind.DWord => int.Parse(raw, CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => long.Parse(raw, CultureInfo.InvariantCulture),
            _ => raw
        };
        using var key = b.CreateSubKey(_path, true);
        key.SetValue(_valueName, val, kind);
    }
}
