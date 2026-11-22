using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace GatherBuddy.Enums;

[JsonConverter(typeof(BiteTypeJsonConverter))]
public enum BiteType : byte
{
    Unknown = 0,
    Weak = 36,
    Strong = 37,
    Legendary = 38,
    None = 255,
}

// AtmoOmen serialized the previous Chinese member names in fish exports and
// overrides; accept those alongside upstream names and numeric values.
internal sealed class BiteTypeJsonConverter : StringEnumConverter
{
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.String)
        {
            var legacy = (reader.Value as string)?.Trim() switch
            {
                "未知" => (BiteType?)BiteType.Unknown,
                "轻竿" => BiteType.Weak,
                "普通竿" => BiteType.Strong,
                "鱼王竿" => BiteType.Legendary,
                "无" => BiteType.None,
                _ => null,
            };
            if (legacy.HasValue)
            {
                return legacy.Value;
            }
        }

        return base.ReadJson(reader, objectType, existingValue, serializer);
    }
}
