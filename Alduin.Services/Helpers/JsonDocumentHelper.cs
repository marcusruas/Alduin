using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Alduin.Core.Helpers
{
    public static class JsonDocumentHelper
    {
        public static bool TryParseToJson(this string json, out JsonElement? root)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                root = null;
                return false;
            }

            try
            {
                var document = JsonDocument.Parse(json);
                root = document.RootElement;
                return true;
            }
            catch
            {
                root = null;
                return false;
            }
        }

        public static string? GetStringProperty(this JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            try
            {
                if (element.TryGetProperty(property, out var result))
                    return result.GetString();
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static int? GetIntProperty(this JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return null;

            try
            {
                var result = element.GetStringProperty(property);
                return int.Parse(result);
            }
            catch
            {
                return null;
            }
        }

        public static string? GetStringProperty(this JsonElement? element, string property)
        {
            if (!element.HasValue)
                return null;

            return element.Value.GetStringProperty(property);
        }

        public static JsonElement? FirstOrDefault(this JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() <= 0)
                return null;

            return element[0];
        }
    }
}
