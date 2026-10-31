using System.Text.Json;

namespace Tweakr.Core;

public sealed class OsRequirement
{
    public int MinBuild { get; set; }
    public int MaxBuild { get; set; } = int.MaxValue;
}

/// <summary>Shape of one tweak in a JSON file / pack.</summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public string? Why { get; set; }
    public string? Danger { get; set; }
    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public TweakKind Kind { get; set; } = TweakKind.Toggle;
    public OsRequirement? Os { get; set; }
    public bool RequiresReboot { get; set; }
    public string? PostAction { get; set; }
    public List<JsonElement> Operations { get; set; } = new();

    // presentation metadata
    public List<string> Tags { get; set; } = new();
    public bool Featured { get; set; }
    public string? Page { get; set; }
    public string? Section { get; set; }
    public string? Icon { get; set; }
    public string? Cmd { get; set; }
    public string? ButtonText { get; set; }
    public List<ChoiceDef> Choices { get; set; } = new();
}

public sealed class ChoiceDef
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}
