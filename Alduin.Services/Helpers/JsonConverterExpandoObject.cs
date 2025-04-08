using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alduin.Core.Helpers
{
    public class JsonConverterExpandoObject : JsonConverter<ExpandoObject>
    {
        public override ExpandoObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options)
                .ToExpando();

        public override void Write(Utf8JsonWriter writer, ExpandoObject value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, (object)value, options);
    }

    public static class ExpandoExtensions
    {
        public static ExpandoObject ToExpando(this IDictionary<string, object> dictionary)
        {
            var expando = new ExpandoObject();
            foreach (var (key, value) in dictionary)
            {
                ((IDictionary<string, object>)expando)[key] =
                    value is JsonElement jsonElement ? ParseJsonElement(jsonElement) :
                    value is Dictionary<string, object> nestedDict ? nestedDict.ToExpando() :
                    value;
            }
            return expando;
        }

        private static object ParseJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.Deserialize<Dictionary<string, object>>()!.ToExpando(),
                JsonValueKind.Array => element.EnumerateArray().Select(ParseJsonElement).ToList(),
                JsonValueKind.String => element.GetString()!,
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null!
            };
        }
    }
}
