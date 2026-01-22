using GatherBuddy.Enums;
using System.Collections.Generic;

public static class EnumLocalization
{
    // GatheringType
    public static readonly Dictionary<GatheringType, string> GatheringTypeMap = new()
    {
        { GatheringType.Mining,     "采矿" },
        { GatheringType.Quarrying,  "碎石" },
        { GatheringType.Logging,    "伐木" },
        { GatheringType.Harvesting, "割草" },
        { GatheringType.Spearfishing, "刺鱼" },
        { GatheringType.Botanist,   "园艺工" },
        { GatheringType.Miner,      "采矿工" },
        { GatheringType.Fisher,     "捕鱼人" },
        { GatheringType.Multiple,   "多职业" },
        { GatheringType.Unknown,    "未知" },
    };

    public static string Get(GatheringType type)
        => GatheringTypeMap.TryGetValue(type, out var text) ? text : type.ToString();


    // BiteType
    public static readonly Dictionary<BiteType, string> BiteTypeMap = new()
    {
        { BiteType.Unknown,   "未知" },
        { BiteType.Weak,      "轻竿" },
        { BiteType.Strong,    "普通竿" },
        { BiteType.Legendary, "鱼王竿" },
        { BiteType.None,      "无" },
    };

    public static string Get(BiteType type)
        => BiteTypeMap.TryGetValue(type, out var text) ? text : type.ToString();

    // NodeType
    public static readonly Dictionary<NodeType, string> NodeTypeMap = new()
    {
        { NodeType.Unknown,   "无" },
        { NodeType.Regular,   "常规" },
        { NodeType.Unspoiled, "未知" },
        { NodeType.Ephemeral, "限时" },
        { NodeType.Legendary, "传说" },
        { NodeType.Clouded, "云冠群岛"},
    };
    public static string Get(NodeType type)
        => NodeTypeMap.TryGetValue(type, out var text) ? text : type.ToString();


    // OceanTime
    public static readonly Dictionary<OceanTime, string> OceanTimeMap = new()
    {
        { OceanTime.Never,  "永不" },
        { OceanTime.Sunset, "日落" },
        { OceanTime.Night,  "夜晚" },
        { OceanTime.Day,    "白昼" },
        { OceanTime.Always, "总是" },
    };

    public static string GetFlags(OceanTime time)
    {
        if (OceanTimeMap.TryGetValue(time, out var direct))
            return direct;

        var list = new List<string>();
        foreach (var t in time.Enumerate())
            if (OceanTimeMap.TryGetValue(t, out var txt))
                list.Add(txt);

        return string.Join(" / ", list);
    }
}
