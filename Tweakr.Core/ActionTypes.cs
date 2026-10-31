using System.Globalization;

namespace Tweakr.Core;

public sealed record ActionResult(bool Success, string Message);
public sealed record ChoiceOption(string Label, string Value);

public static class ByteSize
{
    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : v.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[i];
    }
}
