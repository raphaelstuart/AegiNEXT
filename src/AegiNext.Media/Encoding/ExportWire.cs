using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegiNext.Media.Encoding;

internal static class ExportWire
{
    internal static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
