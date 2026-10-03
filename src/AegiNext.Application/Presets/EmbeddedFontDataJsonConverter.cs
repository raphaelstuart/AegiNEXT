using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

internal sealed class EmbeddedFontDataJsonConverter : JsonConverter<ImmutableArray<byte>>
{
    public override ImmutableArray<byte> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var encodedLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        var maximumEncodedLength = ((SubtitleStylePresetValidator.MAXIMUM_FONT_BYTES + 2L) / 3) * 4;
        if (reader.TokenType != JsonTokenType.String || encodedLength > maximumEncodedLength)
        {
            throw new JsonException("字体必须为预算内的 base64 字符串。");
        }

        try
        {
            return ImmutableCollectionsMarshal.AsImmutableArray(reader.GetBytesFromBase64());
        }
        catch (FormatException error)
        {
            throw new JsonException("字体 base64 格式无效。", error);
        }
    }

    public override void Write(Utf8JsonWriter writer, ImmutableArray<byte> value, JsonSerializerOptions options)
    {
        writer.WriteBase64StringValue(value.AsSpan());
    }
}
