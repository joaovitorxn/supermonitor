using System.Text.Json;
using System.Text.Json.Serialization;

namespace Supermonitor.Core.Overlay;

// Nome desconhecido (arquivo de outra versão ou editado à mão) vira um valor indefinido em vez de derrubar o
// arquivo inteiro; o Normalize troca por padrão ou descarta o item.
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
