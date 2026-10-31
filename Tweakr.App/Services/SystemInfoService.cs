using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Tweakr.App.Services;

public sealed record SystemSnapshot(string CpuModel, string CpuCores, string GpuModel, string GpuVram,
    string MemTotal, string MemType, string OsName, string OsVersion, string DiskModel, string DiskSize);

/// <summary>Hardware / OS summary for the dashboard. Every field fails soft to "Unknown".</summary>
public sealed class SystemInfoService
{
    public Task<SystemSnapshot> GetAsync() => Task.Run(Collect);

    static SystemSnapshot Collect()
    {
        const string na = "Unknown";
        string cpu = na, cores = na, gpu = na, vram = na, mem = na, memType = na, os = na, ver = na, disk = na, size = na;

        Try(() =>
        {
            var r = Query("SELECT Name, NumberOfCores FROM Win32_Processor").FirstOrDefault();
            if (r is null) return;
            cpu = Str(r, "Name").Trim();
            cores = $"{Num(r, "NumberOfCores")} Cores";
        });

        Try(() =>
        {
            var g = ReadGpu();
            if (g is null) return;
            gpu = g.Value.name;
            vram = g.Value.vram;
        });

        Try(() =>
        {
            var r = Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (r is not null) mem = F(Num(r, "TotalPhysicalMemory") / 1073741824.0, "0.00") + " GB";
        });

        Try(() =>
        {
            var t = Query("SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory").Select(r => (int)Num(r, "SMBIOSMemoryType")).FirstOrDefault();
            memType = t switch
            {
                20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2",
                29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => na
            };
        });

        Try(() =>
        {
            var r = Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
            if (r is null) return;
            os = Str(r, "Caption").Trim();
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            ver = Registry.GetValue(key, "DisplayVersion", null) as string
               ?? Registry.GetValue(key, "ReleaseId", null) as string
               ?? $"Build {Str(r, "BuildNumber")}";
        });

        Try(() =>
        {
            var r = Query("SELECT Model, Size, Index FROM Win32_DiskDrive").OrderBy(d => Num(d, "Index")).FirstOrDefault();
            if (r is null) return;
            disk = Str(r, "Model").Trim();
            size = F(Num(r, "Size") / 1073741824.0, "0.0") + " GB";
        });

        return new SystemSnapshot(cpu, cores, gpu, vram, mem, memType, os, ver, disk, size);
    }

    /// <summary>
    /// 1) DXGI: the same source Task Manager uses; reports real 64-bit dedicated VRAM (WMI AdapterRAM caps at 4 GB).
    /// 2) Fallback: WMI names + driver registry value + AdapterRAM.
    /// With an iGPU + dGPU the adapter with the most dedicated memory wins.
    /// </summary>
    static (string name, string vram)? ReadGpu()
    {
        try
        {
            var dx = Dxgi.Adapters()
                .Where(a => !a.name.Contains("Basic", StringComparison.OrdinalIgnoreCase) && a.vram > 0)
                .OrderByDescending(a => a.vram)
                .ToList();
            if (dx.Count > 0) return (dx[0].name, FormatVram(dx[0].vram));
        }
        catch { /* fall through */ }

        var gpus = Query("SELECT Name, AdapterRAM FROM Win32_VideoController")
            .Select(g => (name: Str(g, "Name"), vram: RegistryVram(Str(g, "Name")) ?? Num(g, "AdapterRAM")))
            .Where(g => g.name.Length > 0
                     && !g.name.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                     && !g.name.Contains("Remote", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.vram)
            .ToList();
        return gpus.Count == 0 ? null : (gpus[0].name, FormatVram(gpus[0].vram));
    }

    static List<Dictionary<string, object?>> Query(string wql)
    {
        var result = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(wql);
        using var items = searcher.Get();
        foreach (ManagementBaseObject o in items)
        {
            using (o)
            {
                var d = new Dictionary<string, object?>();
                foreach (PropertyData p in o.Properties) d[p.Name] = p.Value;
                result.Add(d);
            }
        }
        return result;
    }

    static long? RegistryVram(string name)
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11cd-bfc1-08002be10318}");
            if (cls is null) return null;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
                using var k = cls.OpenSubKey(sub);
                if (k is null || !string.Equals((k.GetValue("DriverDesc") as string)?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                switch (k.GetValue("HardwareInformation.qwMemorySize"))
                {
                    case long l: return l;
                    case int i: return i;
                    case byte[] b when b.Length >= 8: return BitConverter.ToInt64(b, 0);
                    case byte[] b4 when b4.Length >= 4: return BitConverter.ToUInt32(b4, 0);
                }
            }
        }
        catch { }
        return null;
    }

    static string FormatVram(long bytes) =>
        bytes <= 0 ? "Unknown"
        : bytes >= (1L << 30) ? $"{Math.Round(bytes / 1073741824.0).ToString(CultureInfo.InvariantCulture)} GB"
        : $"{bytes >> 20} MB";

    static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
    static string Str(Dictionary<string, object?> d, string k) => d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    static long Num(Dictionary<string, object?> d, string k) => d.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v) : 0;
    static void Try(Action a) { try { a(); } catch { } }

    // ---------- minimal DXGI interop (vtable order matters; unused slots are placeholders) ----------
    static class Dxgi
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIFactory1
        {
            void _SetPrivateData(); void _SetPrivateDataInterface(); void _GetPrivateData(); void _GetParent();               // IDXGIObject
            void _EnumAdapters(); void _MakeWindowAssociation(); void _GetWindowAssociation();                                // IDXGIFactory
            void _CreateSwapChain(); void _CreateSoftwareAdapter();
            [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);   // IDXGIFactory1
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIAdapter1
        {
            void _SetPrivateData(); void _SetPrivateDataInterface(); void _GetPrivateData(); void _GetParent();               // IDXGIObject
            void _EnumOutputs(); void _GetDesc(); void _CheckInterfaceSupport();                                              // IDXGIAdapter
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);                                                          // IDXGIAdapter1
        }

        [DllImport("dxgi.dll", ExactSpelling = true)]
        static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);

        public static List<(string name, long vram)> Adapters()
        {
            var list = new List<(string, long)>();
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out var factory) < 0) return list;
            try
            {
                for (uint i = 0; ; i++)
                {
                    if (factory.EnumAdapters1(i, out var adapter) != 0) break;
                    try
                    {
                        if (adapter.GetDesc1(out var d) == 0 && (d.Flags & 2) == 0)       // 2 = software adapter
                            list.Add((d.Description, (long)d.DedicatedVideoMemory.ToUInt64()));
                    }
                    finally { Marshal.ReleaseComObject(adapter); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); }
            return list;
        }
    }
}
