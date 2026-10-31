namespace Tweakr.App.ViewModels;

public static class CategoryIcons
{
    public static string Glyph(string category) => category switch
    {
        "All" => "\uE71D",
        "Featured" => "\uE735",
        "Performance" => "\uE945",
        "Privacy" => "\uE890",
        "Security" => "\uE72E",
        "Network" => "\uE774",
        "UI / Explorer" => "\uE8B7",
        "Gaming" => "\uE7FC",
        "Debloat" or "Cleanup" => "\uE74D",
        _ => "\uE713"
    };
}

public sealed record CategoryItem(string Name)
{
    public string Glyph => CategoryIcons.Glyph(Name);
}
