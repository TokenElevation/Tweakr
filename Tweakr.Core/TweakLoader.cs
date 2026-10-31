using System.Text.Json;
using System.Text.Json.Serialization;
using Tweakr.Core.Operations;

namespace Tweakr.Core;

public static class TweakLoader
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Each document is a single tweak object or an array (a "pack"). Bad tweaks are skipped with a warning.</summary>
    public static IReadOnlyList<ITweak> Load(IEnumerable<string> jsonDocuments, Action<string>? warn = null)
    {
        var result = new List<ITweak>();
        var ids = new HashSet<string>();
        var docOpts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        foreach (var json in jsonDocuments)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, docOpts);
                var items = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray().ToList()
                    : new List<JsonElement> { doc.RootElement };

                foreach (var el in items)
                {
                    try
                    {
                        var def = el.Deserialize<TweakDefinition>(Options)!;
                        if (!ids.Add(def.Id)) { warn?.Invoke($"Duplicate tweak id '{def.Id}' skipped"); continue; }
                        result.Add(Build(def));
                    }
                    catch (Exception ex) { warn?.Invoke($"Skipped a tweak: {ex.Message}"); }
                }
            }
            catch (Exception ex) { warn?.Invoke($"Bad tweak file: {ex.Message}"); }
        }
        return result;
    }

    static ITweak Build(TweakDefinition def) => def.Kind switch
    {
        TweakKind.Action => new JsonTweak(def, Array.Empty<IOperation>(),
            def.Operations.Select(ActionOperationFactory.Create).ToList()),
        TweakKind.Choice => new JsonTweak(def, Array.Empty<IOperation>(), null,
            ChoiceOperationFactory.Create(def.Operations.First())),
        _ => new JsonTweak(def, def.Operations.Select(OperationFactory.Create).ToList())
    };
}
