using System.Text.Json;
using System.Text.Json.Serialization;

namespace Supermonitor.Core.Overlay;

// Unknown name (file from another version or hand-edited) becomes an undefined value instead of failing the
// whole file; Normalize swaps in a default or drops the item.
public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var named))
        {
            return named;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return (T)Enum.ToObject(typeof(T), number);
        }

        reader.Skip();
        return (T)Enum.ToObject(typeof(T), -1);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
