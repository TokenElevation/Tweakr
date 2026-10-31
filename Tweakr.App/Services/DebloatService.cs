using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tweakr.App.Services;

public enum BloatLevel { Recommended, Optional, Careful }

public sealed record BloatDef(string Pattern, string Title, string Description, BloatLevel Level);
public sealed record InstalledApp(string Name, string FullName);

/// <summary>
/// Curated list of removable preinstalled apps. Only apps in this list are ever shown, so system components
/// (Store, Shell, Edge, frameworks, ...) can never be offered for removal. Patterns may use * wildcards.
/// </summary>
public static class BloatCatalog
{
    static BloatDef R(string p, string t, string d) => new(p, t, d, BloatLevel.Recommended);
    static BloatDef O(string p, string t, string d) => new(p, t, d, BloatLevel.Optional);
    static BloatDef C(string p, string t, string d) => new(p, t, d, BloatLevel.Careful);

    public static readonly IReadOnlyList<BloatDef> All = new[]
    {
        // sponsored / promotional apps
        R("*Disney*", "Disney+", "Promotional streaming app preinstalled by some PC makers."),
        R("*Spotify*", "Spotify", "Music app that Windows sometimes installs automatically."),
        R("*TikTok*", "TikTok", "Short-video app that Windows sometimes installs automatically."),
        R("*Netflix*", "Netflix", "Streaming app that Windows sometimes installs automatically."),
        R("*Facebook*", "Facebook", "Social network app that Windows sometimes installs automatically."),
        R("*Instagram*", "Instagram", "Social network app that Windows sometimes installs automatically."),
        R("*Twitter*", "Twitter / X", "Social network app that Windows sometimes installs automatically."),
        R("*LinkedIn*", "LinkedIn", "Professional network app."),
        R("*CandyCrush*", "Candy Crush", "Sponsored game."),
        R("*PrimeVideo*", "Prime Video", "Promotional streaming app."),
        R("*Duolingo*", "Duolingo", "Sponsored language-learning app."),

        // Microsoft apps most people never open
        R("Microsoft.BingNews", "News", "Microsoft's news feed app."),
        R("Microsoft.BingWeather", "Weather", "Weather app. The taskbar weather widget is separate."),
        R("Microsoft.BingSearch", "Bing Search", "Bing integration for the Start menu."),
        R("Microsoft.GetHelp", "Get Help", "Support chat and troubleshooting launcher."),
        R("Microsoft.Getstarted", "Tips", "Windows tips and tutorials."),
        R("Microsoft.MicrosoftOfficeHub", "Microsoft 365 hub", "A launcher and ad for Microsoft 365. Your Office apps are not affected."),
        R("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Ad-supported card games."),
        R("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Send feedback to Microsoft."),
        R("Microsoft.549981C3F5F10", "Cortana", "The retired voice assistant."),
        R("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Only needed for Windows Mixed Reality headsets."),
        R("Microsoft.SkypeApp", "Skype", "Consumer Skype app."),
        R("Microsoft.People", "People", "Contacts app."),
        R("Microsoft.3DBuilder", "3D Builder", "Old 3D printing app."),
        R("Microsoft.Microsoft3DViewer", "3D Viewer", "Old 3D model viewer."),
        R("Microsoft.Print3D", "Print 3D", "Old 3D printing app."),
        R("MicrosoftTeams", "Microsoft Teams (personal)", "The consumer Teams chat app."),
        R("MSTeams", "Microsoft Teams (personal)", "The consumer Teams chat app."),
        R("Microsoft.Windows.DevHome", "Dev Home", "Developer dashboard."),

        // fine to remove if you don't use them
        O("Clipchamp.Clipchamp", "Clipchamp", "Microsoft's video editor."),
        O("Microsoft.Todos", "Microsoft To Do", "Task list app."),
        O("Microsoft.PowerAutomateDesktop", "Power Automate", "Desktop automation tool."),
        O("Microsoft.YourPhone", "Phone Link", "Connects your phone to your PC. Remove it if you don't use that."),
        O("Microsoft.WindowsMaps", "Maps", "Offline maps app."),
        O("Microsoft.ZuneMusic", "Media Player", "Music and media player."),
        O("Microsoft.ZuneVideo", "Movies & TV", "Video player and store."),
        O("Microsoft.WindowsAlarms", "Clock", "Alarms, timers and world clocks."),
        O("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Simple voice recorder."),
        O("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Desktop notes. Your notes are kept in your Microsoft account."),
        O("Microsoft.OutlookForWindows", "New Outlook", "The new mail app that replaces Mail and Calendar."),
        O("Microsoft.Copilot", "Copilot", "The Copilot app."),
        O("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Remote help tool."),
        O("MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family", "Parental controls app."),
        O("Microsoft.XboxApp", "Xbox Console Companion", "Old Xbox companion app."),
        O("Microsoft.XboxSpeechToTextOverlay", "Xbox speech overlay", "Speech-to-text overlay for Xbox games."),
        O("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "The Win+G overlay used for screen capture. Remove it only if you never use that."),
        O("Microsoft.GamingApp", "Xbox app", "Needed to install and launch Game Pass games."),

        // can break other things
        C("Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "Handles Xbox sign-in. Removing it can break Game Pass and some Store games."),
        C("Microsoft.Xbox.TCUI", "Xbox Live in-game UI", "Sign-in and social pop-ups used by Xbox games. Removing it can break some games."),
        C("Microsoft.Windows.Photos", "Photos", "Windows' default image viewer. Without it you need another viewer."),
        C("Microsoft.WindowsCalculator", "Calculator", "Windows' calculator. Some apps and shortcuts expect it."),
        C("Microsoft.WindowsCamera", "Camera", "Needed by apps that use the built-in camera."),
        C("Microsoft.ScreenSketch", "Snipping Tool", "Screenshot tool that handles Win+Shift+S."),
    };

    public static BloatDef? Match(string packageName)
    {
        foreach (var d in All)
        {
            var rx = "^" + Regex.Escape(d.Pattern).Replace("\\*", ".*") + "$";
            if (Regex.IsMatch(packageName, rx, RegexOptions.IgnoreCase)) return d;
        }
        return null;
    }
}

/// <summary>Lists and removes Store (Appx) apps through PowerShell.</summary>
public sealed class DebloatService
{
    const string ScanScript =
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
        "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | " +
        "Select-Object Name,PackageFullName | ConvertTo-Json -Compress";

    public async Task<IReadOnlyList<InstalledApp>> ScanAsync()
    {
        var (exit, output) = await RunPowerShellAsync(ScanScript);
        if (exit != 0 || string.IsNullOrWhiteSpace(output)) return Array.Empty<InstalledApp>();

        var list = new List<InstalledApp>();
        using var doc = JsonDocument.Parse(output.Trim());
        void Add(JsonElement e)
        {
            var name = e.GetProperty("Name").GetString();
            var full = e.GetProperty("PackageFullName").GetString();
            if (name is not null && full is not null) list.Add(new InstalledApp(name, full));
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var e in doc.RootElement.EnumerateArray()) Add(e);
        else Add(doc.RootElement);   // PowerShell emits a bare object when there is only one result
        return list;
    }

    public async Task<(bool ok, string? error)> RemoveAsync(string name, bool alsoFromImage)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9._\-]+$")) return (false, "Unexpected package name.");   // it goes into a script: be strict

        var script = $"$ErrorActionPreference='Stop'; $n='{name}'; " +
                     "try { Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers } " +
                     "catch { Get-AppxPackage -Name $n | Remove-AppxPackage }";
        if (alsoFromImage)
            script += "; Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq $n } | Remove-AppxProvisionedPackage -Online | Out-Null";

        var (exit, output) = await RunPowerShellAsync(script);
        if (exit == 0) return (true, null);
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return (false, firstLine ?? "Windows refused to remove it.");
    }

    static async Task<(int exit, string output)> RunPowerShellAsync(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, p.ExitCode == 0 ? await stdout : (await stderr) + (await stdout));
    }
}
