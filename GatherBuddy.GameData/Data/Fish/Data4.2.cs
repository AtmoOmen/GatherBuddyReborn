using GatherBuddy.Enums;

namespace GatherBuddy.Data;

public static partial class Fish
{
    // @formatter:off
    private static void ApplyRiseOfANewSun(this GameData data)
    {
        data.Apply     (22389, Patch.RiseOfANewSun) // Mirage Mahi
            .Bait      (data, 20675)
            .Bite      (data, HookSet.Powerful, BiteType.鱼王竿)
            .Time      (240, 480);
        data.Apply     (22390, Patch.RiseOfANewSun) // Triplespine
            .Bait      (data, 20676)
            .Bite      (data, HookSet.Precise, BiteType.轻竿)
            .Time      (300, 420)
            .Snag      (data, Snagging.None);
        data.Apply     (22391, Patch.RiseOfANewSun) // Alligator Snapping Turtle
            .Bait      (data, 20619)
            .Bite      (data, HookSet.Powerful, BiteType.普通竿)
            .Weather   (data, 2, 1);
        data.Apply     (22392, Patch.RiseOfANewSun) // Redtail
            .Mooch     (data, 20613, 20064)
            .Bite      (data, HookSet.Powerful, BiteType.鱼王竿)
            .Weather   (data, 3, 4);
        data.Apply     (22393, Patch.RiseOfANewSun) // Usuginu Octopus
            .Bait      (data, 20617)
            .Bite      (data, HookSet.Powerful, BiteType.鱼王竿);
        data.Apply     (22394, Patch.RiseOfANewSun) // Saltmill
            .Mooch     (data, 20616, 20025)
            .Bite      (data, HookSet.Powerful, BiteType.鱼王竿)
            .Weather   (data, 3, 4);
        data.Apply     (22395, Patch.RiseOfANewSun) // Bonsai Fish
            .Bait      (data, 20614)
            .Bite      (data, HookSet.Precise, BiteType.轻竿);
        data.Apply     (22396, Patch.RiseOfANewSun) // Ribbon Eel
            .Mooch     (data, 20617, 20112)
            .Bite      (data, HookSet.Precise, BiteType.轻竿);
        data.Apply     (22397, Patch.RiseOfANewSun) // Red Prismfish
            .Bait      (data, 20675)
            .Bite      (data, HookSet.Powerful, BiteType.普通竿)
            .Time      (240, 480);
        data.Apply     (22398, Patch.RiseOfANewSun) // Elder Gourami
            .Mooch     (data, 20614, 20127)
            .Bite      (data, HookSet.Powerful, BiteType.普通竿)
            .Snag      (data, Snagging.None)
            .Weather   (data, 3, 4, 5);
    }
    // @formatter:on
}
