using System.Collections.Generic;
using System.Numerics;
using GatherBuddy.AutoGather.Collectables.Data;

namespace GatherBuddy.AutoGather.Collectables;

public static class CollectableNpcLocations
{
    public static List<CollectableShop> CollectableShops = new()
    {
        new CollectableShop()
        {
            Name = "九号方案",
            Location = new Vector3(-162.17f, 0.9219f, -30.458f),
            ScripShopLocation = new Vector3(-161.84605f, 0.921f, -42.06536f),
            AetheryteId = 186,
            TerritoryId = 1186,
            NpcId = 1027542,
            ScripShopNpcId = 1027541,
            IsLifestreamRequired = true,
            LifestreamCommand = "Nexus Arcade"
        },
        new CollectableShop()
        {
            Name = "游末邦",
            Location = new Vector3(16.94f, 82.05f, -19.177f),
            AetheryteId = 134,
            TerritoryId = 820,
            NpcId = 1027542,
            ScripShopNpcId = 1027541
        },
        new CollectableShop()
        {
            Name = "格里达尼亚旧街",
            Location = new Vector3(143.62454f, 13.74769f, -105.33799f),
            AetheryteId = 2,
            TerritoryId = 133,
            NpcId = 1027542,
            ScripShopNpcId = 1027541,
            IsLifestreamRequired = true,
            LifestreamCommand = "Leatherworkers"
        }
    };
    
    public static CollectableShop GetDefaultShop()
    {
        var eulmore = new CollectableShop()
        {
            Name = "游末邦",
            Location = new Vector3(16.94f, 82.05f, -19.177f),
            AetheryteId = 134,
            TerritoryId = 820,
            NpcId = 1027542,
            ScripShopNpcId = 1027541
        };
        return eulmore;
    }
}
