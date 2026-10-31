using System.Text.Json;

namespace Tweakr.Core.Operations;

public static class OperationFactory
{
    // Add scheduledTask, powercfg, file, powershell, appx here as they get implemented.
    public static IOperation Create(JsonElement e) => e.GetProperty("type").GetString() switch
    {
        "registry" => RegistryOperation.FromJson(e),
        "service" => ServiceOperation.FromJson(e),
        var t => throw new NotSupportedException($"Operation type '{t}' is not implemented yet")
    };
}
