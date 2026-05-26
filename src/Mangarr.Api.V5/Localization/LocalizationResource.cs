using System.Text.Json;
using System.Text.Json.Serialization;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.Localization;

public class LocalizationResourceSerializer : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Intentional: this is a write-only JsonConverter (Write serializes localization strings to the client).
        // Localization resources are never deserialized from the wire, so Read is an intentional unreachable
        // override required by the JsonConverter<T> base. NOT a bug (CQ-05 / DOCS-07).
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> dictionary, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, value) in dictionary)
        {
            var propertyName = key;
            writer.WritePropertyName(propertyName);
            writer.WriteStringValue(value);
        }

        writer.WriteEndObject();
    }
}

public class LocalizationResource : RestResource
{
    [JsonConverter(typeof(LocalizationResourceSerializer))]
    public Dictionary<string, string> Strings { get; set; } = new();
}

public static class LocalizationResourceMapper
{
    public static LocalizationResource ToResource(this Dictionary<string, string> localization)
    {
        return new LocalizationResource
        {
            Strings = localization,
        };
    }
}
