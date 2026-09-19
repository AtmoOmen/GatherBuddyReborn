using GatherBuddy.Plugin;

namespace GatherBuddy.Vulcan.Vendors;

internal static class VendorAutomationRequirements
{
    public const string AllaganItemSearchInternalName = "AllaganItemSearch";
    public const string UnavailableStatusText = "商人自动化需要 Allagan Tools 或 Allagan Item Search。";
    public const string UnavailableHelpText = "加载 Allagan Tools 或 Allagan Item Search 以使用商人自动化。";

    public static bool IsAvailable
        => AllaganTools.Enabled || IPCSubscriber.IsReady(AllaganItemSearchInternalName);
}
