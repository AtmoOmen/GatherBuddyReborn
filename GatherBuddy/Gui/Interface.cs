using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using GatherBuddy.Alarms;
using GatherBuddy.RebornUI;
using GatherBuddy.Time;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using Functions = GatherBuddy.Plugin.Functions;

namespace GatherBuddy.Gui;

public partial class Interface : M3ShellWindow, IDisposable
{
    private const string PluginName = "GatherBuddy Reborn";

    private const string SectionBrowse = "浏览";
    private const string SectionPlan = "计划";
    private const string SectionFishing = "钓鱼";
    private const string SectionSettings = "设置";
    private const string SectionTools = "工具";

    public const string ItemsPageId = "gatherables";
    public const string FishPageId = "fish";
    public const string AutoGatherPageId = "auto_gather";
    public const string ConfigPresetsPageId = "config_presets";

    private static readonly Vector2 DefaultSize = new(1040f, 720f);
    private static readonly WindowSizeConstraints DefaultConstraints = new()
    {
        MinimumSize = new Vector2(480f, 360f),
        MaximumSize = new Vector2(6000f, 6000f),
    };

    private static readonly M3WindowAction[] MainWindowActions =
    [
        new("##gbr_open_vulcan", FontAwesomeIcon.Hammer, "打开 Vulcan 制作窗口"),
        new("##gbr_open_collectables", FontAwesomeIcon.BoxOpen, "打开收藏品窗口"),
    ];

    private static GatherBuddy _plugin = null!;
    private TimeStamp _earliestKeyboardToggle = TimeStamp.Epoch;
    private readonly GatherBuddySettings _settings;

    private static List<ExtendedFish>? _extendedFishList;

    public static IReadOnlyList<ExtendedFish> ExtendedFishList
        => _extendedFishList ??= CreateExtendedFishList();

    private static List<ExtendedFish> CreateExtendedFishList()
    {
        var extendedFish = new List<ExtendedFish>();
        foreach (var fish in GatherBuddy.GameData.Fishes.Values)
        {
            if (fish.FishingSpots.Count > 0)
            {
                extendedFish.Add(new ExtendedFish(fish));
            }
        }

        return extendedFish;
    }

    public static ExtendedFish? FindExtendedFish(Classes.Fish fish)
    {
        var extendedFishList = ExtendedFishList;
        for (var i = 0; i < extendedFishList.Count; ++i)
        {
            if (extendedFishList[i].Data == fish)
            {
                return extendedFishList[i];
            }
        }

        return null;
    }

    internal static Sounds[] AlarmSoundIds
        => AlarmCache.SoundIds;

    internal static string[] AlarmSoundNames
        => AlarmCache.SoundIdNames;

    public Interface(GatherBuddy plugin)
        : base($"{PluginName}###GatherBuddyMain", "gbr", PluginName, PluginName, DefaultSize, DefaultConstraints)
    {
        BlocksGameInput = false;
        _plugin = plugin;
        _gatherGroupCache = new GatherGroupCache(_plugin.GatherGroupManager);
        _autoGatherListsCache = new AutoGatherListsCache();
        _gatherWindowCache = new GatherWindowCache();
        _locationTable = new LocationTable();
        _alarmCache = new AlarmCache(_plugin.AlarmManager);
        _recordTable = new RecordTable();
        _settings = new GatherBuddySettings(plugin, this);
        IsOpen = GatherBuddy.Config.OpenOnStart;
        BuildPages();
    }

    private void BuildPages()
    {
        AddPage(new M3Page(ItemsPageId, "采集物品", FontAwesomeIcon.Seedling, SectionBrowse,
            "查看采矿工和园艺工能采集的物品，以及出现地点和时间", Panels(DrawItemPage), false,
            tooltip: "用镐凿石或砍伐树木都算采集。怎么突然问这个？"));
        AddPage(new M3Page(FishPageId, "鱼类", FontAwesomeIcon.Fish, SectionBrowse,
            "查看鱼类、鱼饵、出现条件和可钓时间", Panels(DrawFishPage), false,
            tooltip: "海里有很多鱼。天上、沙里、岩浆里，甚至宇宙里也有。"));
        AddPage(new M3Page("weather", "天气", FontAwesomeIcon.CloudSun, SectionBrowse,
            "查看各区域接下来几天的天气预报和上一时段天气", Panels(DrawWeatherPage), false,
            tooltip: "没错，「妖雾」也是一种天气。"));
        AddPage(new M3Page("locations", "自定义地点", FontAwesomeIcon.MapMarkedAlt, SectionBrowse,
            "为特定采集点自定义以太之光和地图标记位置", Panels(DrawLocationsPage), false));

        AddPage(new M3Page(AutoGatherPageId, "自动采集", FontAwesomeIcon.Robot, SectionPlan,
            "管理自动采集的物品清单", Panels(DrawAutoGatherPage), false,
            tooltip: "你没看错，就是自动采集！"));
        AddPage(new M3Page("alarms", "提醒", FontAwesomeIcon.Bell, SectionPlan,
            "在采集点或鱼类出现时提醒你", Panels(DrawAlarmPage), false,
            tooltip: "常常错过重要时间？设置自己的提醒吧。"));
        AddPage(new M3Page("gather_groups", "采集组", FontAwesomeIcon.LayerGroup, SectionPlan,
            "按时段安排采集内容，并与他人分享采集组", Panels(DrawGatherGroupPage), false));
        AddPage(new M3Page("gather_window", "采集窗口", FontAwesomeIcon.ListUl, SectionPlan,
            "为仅显示关注物品的采集窗口设置预设", Panels(DrawGatherWindowPage), false));

        AddPage(new M3Page("fish_records", "钓鱼记录", FontAwesomeIcon.Book, SectionFishing,
            "查找、整理和分享已收集的钓鱼记录", Panels(DrawRecordPage), false));
        AddPage(new M3Page("fish_stats", "钓场统计", FontAwesomeIcon.ChartBar, SectionFishing,
            "按钓场汇总你的钓鱼记录", Panels(DrawStatsPage), false, () => GatherBuddy.Config.EnableFishStats));

        foreach (var page in _settings.Pages)
        {
            AddPage(new M3Page(page.Id, page.Label, page.Icon, SectionSettings, page.Description, page.Draw));
        }

        AddPage(new M3Page(ConfigPresetsPageId, "配置预设", FontAwesomeIcon.SlidersH, SectionSettings,
            "按物品配置自动采集时使用的技能和消耗品", DrawConfigPresetsPage, false));

        AddPage(new M3Page("debug", "调试", FontAwesomeIcon.Bug, SectionTools,
            "查看用于故障排查的内部数据", Panels(DrawDebugPage), false, tool: true,
            tooltip: "希望你是为了排查问题才打开这里。"));
    }

    private static Action Panels(Action draw)
        => VulcanUiStyle.Panels(draw);

    protected override M3WindowAction[] WindowActions
        => MainWindowActions;

    protected override ImGuiWindowFlags ExtraFlags
        => (GatherBuddy.Config.MainWindowLockPosition ? ImGuiWindowFlags.NoMove : ImGuiWindowFlags.None)
          | (GatherBuddy.Config.MainWindowLockResize ? ImGuiWindowFlags.NoResize : ImGuiWindowFlags.None);

    protected override void OnWindowAction(int index)
    {
        switch (index)
        {
            case 0:
                if (GatherBuddy.VulcanWindow == null)
                {
                    M3Snackbar.Show("Vulcan 窗口不可用。", severity: M3Severity.Error);
                }
                else
                {
                    GatherBuddy.VulcanWindow.RestoreWindow();
                }

                break;
            case 1:
                if (GatherBuddy.CollectablesWindow == null)
                {
                    M3Snackbar.Show("收藏品窗口不可用。", severity: M3Severity.Error);
                }
                else
                {
                    GatherBuddy.CollectablesWindow.Open();
                }

                break;
        }
    }

    protected override void BeforeDrawShell()
        => SetupValues();

    protected override void OnPageChanged(M3Page page)
        => FlushConfigPresets();

    public override void OnClose()
    {
        FlushConfigPresets();
        base.OnClose();
    }
    public new void Toggle()
    {
        if (IsOpen && IsMinimized)
        {
            Restore();
            BringToFront();
            return;
        }

        IsOpen = !IsOpen;
    }

    public void Open(string pageId)
    {
        ShowPage(pageId);
        OpenAndRestore();
    }

    public void OpenSettings()
        => Open(GatherBuddySettings.AutoGatherPageId);

    public override void PreOpenCheck()
    {
        if (_earliestKeyboardToggle > GatherBuddy.Time.ServerTime || !Functions.CheckKeyState(GatherBuddy.Config.MainInterfaceHotkey, false))
        {
            return;
        }

        _earliestKeyboardToggle = GatherBuddy.Time.ServerTime.AddMilliseconds(500);
        Toggle();
    }

    #region Search

    protected override bool SupportsSearch
        => true;

    protected override string SearchHint
        => "搜索设置";

    protected override void OnSearchChanged(string text)
        => _settings.UpdateSearch(text);

    protected override string SearchSubtitle(string text)
        => _settings.ResultCount == 1 ? "找到 1 组匹配的设置" : $"找到 {_settings.ResultCount} 组匹配的设置";

    protected override void DrawSearchResults(string text)
    {
        if (_settings.DrawSearch(text) is { } page)
        {
            ShowPage(page.Id);
        }
    }

    #endregion

    public void Dispose()
    {
        FlushConfigPresets();
        _headerCache.Dispose();
        _weatherTable.Dispose();
        _itemTable.Dispose();
        _autoGatherListsCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
