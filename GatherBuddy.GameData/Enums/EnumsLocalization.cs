using GatherBuddy.Enums;

public static class EnumLocalization
{
    // GatheringType
    public static readonly Dictionary<GatheringType, string> GatheringTypeMap = new()
    {
        { GatheringType.采掘, "采掘" },
        { GatheringType.碎石, "碎石" },
        { GatheringType.采伐, "采伐" },
        { GatheringType.割草, "割草" },
        { GatheringType.刺鱼, "刺鱼" },
        { GatheringType.园艺工, "园艺工" },
        { GatheringType.采矿工, "采矿工" },
        { GatheringType.捕鱼人, "捕鱼人" },
        { GatheringType.多职业, "多职业" },
        { GatheringType.未知, "未知" },
    };

    public static string Get(GatheringType type)
        => GatheringTypeMap.TryGetValue(type, out var text) ? text : type.ToString();


    // BiteType
    public static readonly Dictionary<BiteType, string> BiteTypeMap = new()
    {
        { BiteType.未知, "未知" },
        { BiteType.轻竿, "轻竿" },
        { BiteType.普通竿, "普通竿" },
        { BiteType.鱼王竿, "鱼王竿" },
        { BiteType.无, "无" },
    };

    public static string Get(BiteType type)
        => BiteTypeMap.TryGetValue(type, out var text) ? text : type.ToString();

    // NodeType
    public static readonly Dictionary<NodeType, string> NodeTypeMap = new()
    {
        { NodeType.无, "无" },
        { NodeType.常规, "常规" },
        { NodeType.未知, "未知" },
        { NodeType.限时, "限时" },
        { NodeType.传说, "传说" },
        { NodeType.梦幻, "云冠群岛" },
    };

    public static string Get(NodeType type)
        => NodeTypeMap.TryGetValue(type, out var text) ? text : type.ToString();


    // OceanTime
    public static readonly Dictionary<OceanTime, string> OceanTimeMap = new()
    {
        { OceanTime.永不, "永不" },
        { OceanTime.日落, "日落" },
        { OceanTime.夜晚, "夜晚" },
        { OceanTime.白昼, "白昼" },
        { OceanTime.总是, "总是" },
    };

    public static string GetFlags(OceanTime time)
    {
        if(OceanTimeMap.TryGetValue(time, out var direct))
            return direct;

        var list = new List<string>();

        foreach (var t in time.Enumerate())
        {
            if(OceanTimeMap.TryGetValue(t, out var txt))
                list.Add(txt);
        }

        return string.Join(" / ", list);
    }
}
