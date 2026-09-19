using System.Collections.Generic;
using System.Linq;
using GatherBuddy.Interfaces;
using GatherBuddy.Time;

namespace GatherBuddy.GatherGroup;

public static class GroupData
{
    private static TimedGroup.Config CreateDefaultGroup(string name, string description, params (uint, int)[] items)
        => new()
        {
            Name        = name,
            Description = description,
            Nodes = items.Concat(items.Select(i => (i.Item1, i.Item2 + 12)))
                .Select(i => new TimedGroupNode.Config()
                {
                    Type        = ObjectType.Gatherable,
                    ItemId      = i.Item1,
                    StartMinute = (short)(i.Item2 * RealTime.MinutesPerHour),
                    EndMinute   = (short)((i.Item2 + 2) * RealTime.MinutesPerHour % RealTime.MinutesPerDay),
                    Annotation  = string.Empty,
                }).ToArray(),
        };

    private static TimedGroup.Config CreateEphemeralGroup(string name, string description, params (uint, int)[] items)
        => new()
        {
            Name        = name,
            Description = description,
            Nodes = items
                .Select(i => new TimedGroupNode.Config()
                {
                    Type        = ObjectType.Gatherable,
                    ItemId      = i.Item1,
                    StartMinute = (short)(i.Item2 * RealTime.MinutesPerHour),
                    EndMinute   = (short)((i.Item2 + 4) * RealTime.MinutesPerHour % RealTime.MinutesPerDay),
                    Annotation  = string.Empty,
                }).ToArray(),
        };

    private static TimedGroup.Config CreateDarkMatterGroup()
        => new()
        {
            Name        = "darkmatter",
            Description = "采集所有产出暗物质晶簇的采集点。",
            Nodes = new (uint, short, short)[]
            {
                (215, 0, 80),
                (250, 80, 103),
                (222, 103, 126),
                (216, 126, 149),
                (340, 149, 172),
                (233, 172, 195),
                (242, 195, 218),
                (246, 218, 241),
                (230, 241, 264),
                (236, 264, 287),
                (248, 287, 310),
                (247, 310, 333),
                (237, 333, 356),
                (217, 356, 379),
                (241, 379, 402),
                (226, 402, 425),
                (215, 425, 448),
                (219, 448, 471),
                (240, 471, 494),
                (235, 494, 517),
                (245, 517, 540),
                (232, 540, 565),
                (234, 565, 590),
                (215, 590, 616),
                (231, 616, 642),
                (223, 642, 668),
                (211, 668, 694),
                (214, 694, 720),
                (249, 720, 780),
                (221, 780, 816),
                (227, 816, 852),
                (220, 852, 888),
                (212, 888, 924),
                (340, 924, 960),
                (213, 960, 1050),
                (252, 1050, 1085),
                (227, 1085, 1155),
                (224, 1155, 1190),
                (238, 1190, 1225),
                (239, 1225, 1260),
                (218, 1260, 1320),
                (225, 1320, 1380),
                (227, 1380, 0),
            }.Select(n => new TimedGroupNode.Config()
            {
                Type           = ObjectType.Gatherable,
                ItemId         = 10335,
                StartMinute    = n.Item2,
                EndMinute      = n.Item3,
                Annotation     = string.Empty,
                PreferLocation = n.Item1,
            }).ToArray(),
        };

    // @formatter:off
    internal static readonly List<TimedGroup.Config> DefaultGroups = 
    [
        CreateDefaultGroup("80***", "包含总督制作采集点。",                    (32954, 0), (32952, 2), (32955, 4), (32950, 6), (32951, 8), (32953, 10)),
        CreateDefaultGroup("80**",  "包含新伊修加德 / 唯美制作采集点。",   (30485, 0), (30486, 2), (29974, 4), (29970, 6), (29972, 8), (29976, 10)),
        CreateDefaultGroup("90*",   "包含古典制作采集点。",                   (36216, 0), (36167, 2), (36179, 4), (36195, 6), (36207, 8), (36217, 10)),
        CreateDefaultGroup("90**",  "包含复兴制作采集点。",                   (37819, 0), (37818, 2), (37821, 4), (37817, 6), (37820, 8), (37822, 10)),
        CreateDefaultGroup("90***", "包含继业者制作采集点。",                   (39710, 0), (39705, 2), (39706, 4), (39708, 6), (39707, 8), (39709, 10)),
        CreateDefaultGroup("100*", "包含旧日王国制作采集点。",               (44137, 0), (44140, 2), (44138, 4), (44135, 6), (44139, 8), (44136, 10)),
        CreateDefaultGroup("100**", "包含仪仗制作采集点。",                  (45968, 0), (45973, 2), (45971, 4), (45972, 6), (45970, 8), (45969, 10)),
        CreateDefaultGroup("100***", "包含宫廷制作采集点。",                    (49209, 0), (49212, 2), (49210, 4), (49211, 6), (49207, 8), (49208, 10)),
        CreateDefaultGroup("100purple", "包含 100 级紫色工票采集点。",           (43926, 0), (43927, 2), (44233, 4), (44234, 6), (43920, 8), (43919, 10)),
        CreateDefaultGroup("100orange", "包含 100 级橙色工票采集点。",           (43923, 0), (43928, 2), (43922, 4), (43929, 6), (43921, 8), (43930, 10)),
        CreateDarkMatterGroup(),
        CreateEphemeralGroup("90sand", "包含 90 级灵砂组的采集点。", (36285, 0), (36287, 8), (36288, 12), (36286, 20), (36287, 4), (36286, 16)), // last two nodes are temporary because there is nothing there
    ];
    // @formatter:on
}
