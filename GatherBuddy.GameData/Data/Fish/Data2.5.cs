using GatherBuddy.Enums;

namespace GatherBuddy.Data;

public static partial class Fish
{
    // @formatter:off
    private static void ApplyBeforeTheFall(this GameData data)
    {
        data.Apply     (10123, Patch.BeforeTheFall) // Gigant Clam
            .Bait      (data, 2619)
            .Bite      (data, HookSet.Powerful, BiteType.鱼王竿)
            .Time      (1380, 120)
            .Snag      (data, Snagging.None)
            .ForceBig  (false);
    }
    // @formatter:on
}
