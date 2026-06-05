using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GatherBuddy.Enums;

[JsonConverter(typeof(StringEnumConverter))]
public enum BiteType : byte
{
    未知   = 0,
    轻竿      = 36,
    普通竿    = 37,
    鱼王竿 = 38,
    无      = 255,
}
