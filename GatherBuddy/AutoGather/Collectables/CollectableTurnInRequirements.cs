using GatherBuddy.Plugin;

namespace GatherBuddy.AutoGather.Collectables;

internal static class CollectableTurnInRequirements
{
    public const string AllaganItemSearchInternalName = "AllaganItemSearch";
    public const string UnavailableStatusText = "收藏品缴纳需要 Allagan Tools 或 Allagan Item Search。";
    public const string UnavailableHelpText = "加载 Allagan Tools 或 Allagan Item Search 以使用收藏品缴纳。";

    public static bool IsAvailable
        => AllaganTools.Enabled || IPCSubscriber.IsReady(AllaganItemSearchInternalName);
}
