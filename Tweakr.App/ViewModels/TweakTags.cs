using System.Windows.Media;
using Tweakr.Core;

namespace Tweakr.App.ViewModels;

/// <summary>A small icon badge on a card: what kind of optimization it is (or "featured").</summary>
public sealed record TagBadge(string Key, string Label, string Glyph, Brush Fg, Brush Bg);

public static class TweakTags
{
    static Brush B(string hex, byte alpha = 0xFF)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = alpha;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static TagBadge Make(string key, string label, string glyph, string hex) => new(key, label, glyph, B(hex), B(hex, 0x2A));

    public static readonly TagBadge Featured = Make("featured", "Featured tweak", "\uE735", "#FBBF24");

    static readonly Dictionary<string, TagBadge> Map = new()
    {
        ["appearance"] = Make("appearance", "Appearance Customization", "\uE790", "#F472B6"),
        ["privacy"] = Make("privacy", "Privacy Optimization", "\uE890", "#34D399"),
        ["performance"] = Make("performance", "Performance Optimization", "\uE945", "#FBBF24"),
        ["gaming"] = Make("gaming", "Gaming Optimization", "\uE7FC", "#4ADE80"),
        ["network"] = Make("network", "Network Optimization", "\uE774", "#38BDF8"),
        ["security"] = Make("security", "Security Hardening", "\uE72E", "#FB7185"),
        ["cleanup"] = Make("cleanup", "Storage Cleanup", "\uE74D", "#FB923C"),
    };

    static string? FromCategory(string category) => category switch
    {
        "UI / Explorer" => "appearance",
        "Privacy" => "privacy",
        "Performance" => "performance",
        "Gaming" => "gaming",
        "Network" => "network",
        "Security" => "security",
        "Cleanup" => "cleanup",
        _ => null
    };

    /// <summary>Featured star first, then the tweak's tags (or the one implied by its category).</summary>
    public static IReadOnlyList<TagBadge> For(ITweak t)
    {
        IEnumerable<string> keys = t.Tags.Count > 0
            ? t.Tags
            : FromCategory(t.Category) is { } k ? new[] { k } : Array.Empty<string>();

        var list = new List<TagBadge>();
        if (t.Featured) list.Add(Featured);
        foreach (var key in keys)
            if (Map.TryGetValue(key.ToLowerInvariant(), out var badge)) list.Add(badge);
        return list;
    }
}
