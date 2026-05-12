using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace GatherBuddy.Enums;

[JsonConverter(typeof(StringEnumConverter))]
public enum HookSet : byte
{
    Unknown    = 0,
    Precise    = 1,
    Powerful   = 2,
    Hook       = 3,
    DoubleHook = 4,
    TripleHook = 5,
    Stellar    = 6,
    None       = 255,
}

public static class HookSetExtensions
{
    public static string ToName(this HookSet value)
        => value switch
        {
            HookSet.Unknown    => "未知",
            HookSet.Precise    => "精准",
            HookSet.Powerful   => "强力",
            HookSet.Hook       => "提钩",
            HookSet.DoubleHook => "双重",
            HookSet.TripleHook => "三重",
            HookSet.Stellar    => "华丽",
            HookSet.None       => "无",
            _                  => "不可用",
        };
}
