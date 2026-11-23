using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using GatherBuddy.AutoGather.Collectables;
using GatherBuddy.Config;
using GatherBuddy.Helpers;
using GatherBuddy.RebornUI.Settings;
using GatherBuddy.Vulcan.Vendors;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GatherBuddy.Gui;

public sealed class CollectablesWindow : M3PanelWindow
{
    public const string WindowId = "收藏品###GatherBuddyCollectablesWindow";
    private const string SetupGuideDialogId = "Collectables Setup Guide###GatherBuddyCollectablesSetupGuide";

    private static readonly M3PluginRequirement[] RequiredCollectablePlugins =
    [
        new("InventoryTools", "Allagan Tools"),
        new(CollectableTurnInRequirements.AllaganItemSearchInternalName, "Allagan Item Search"),
    ];

    private static readonly M3WindowAction[] CollectablesWindowActions =
    [
        new("##collectables_guide", FontAwesomeIcon.QuestionCircle, "设置指南"),
        new("##collectables_vulcan", FontAwesomeIcon.Hammer, "打开 Vulcan"),
        new("##collectables_vendor_lists", FontAwesomeIcon.ShoppingCart, "打开商店购买清单"),
    ];

    private static readonly Vector2 DefaultSize = new(760f, 680f);
    private static readonly WindowSizeConstraints DefaultConstraints = new()
    {
        MinimumSize = new Vector2(420f, 320f),
        MaximumSize = new Vector2(5000f, 5000f),
    };

    private bool _openSetupGuide;

    public CollectablesWindow()
        : base(WindowId, "收藏品", DefaultSize, DefaultConstraints)
    {
        RespectCloseHotkey = true;
        IsOpen = false;
    }

    public void Open()
        => OpenAndRestore();

    protected override M3WindowAction[] WindowActions
        => CollectablesWindowActions;

    protected override void OnWindowAction(int index)
    {
        switch (index)
        {
            case 0:
                _openSetupGuide = true;
                break;
            case 1:
                GatherBuddy.VulcanWindow?.RestoreWindow();
                break;
            case 2:
                GatherBuddy.VendorBuyListWindow?.Open();
                break;
        }
    }

    protected override (string Text, Vector4? Color) Status
    {
        get
        {
            var manager = GatherBuddy.CollectableManager;
            if (manager == null)
            {
                return (string.Empty, null);
            }

            var text = string.IsNullOrWhiteSpace(manager.StatusText) ? "空闲" : manager.StatusText;
            return (text, manager.IsRunning ? M3.Scheme.Primary : null);
        }
    }

    protected override void DrawContents()
    {
        var manager = GatherBuddy.CollectableManager;
        var config = GatherBuddy.Config.CollectableConfig;
        var routes = CollectableTurnInRouteResolver.GetAvailableRoutes();
        var selectedRoute = CollectableTurnInRouteResolver.ResolvePreferredRoute(config.PreferredTurnInRoute, routes);
        var vendorBuyListManager = GatherBuddy.VendorBuyListManager;
        var selectedGatheringList = VendorBuyListManager.GetList(config.GatheringPurchaseListId);
        var selectedCraftingList = VendorBuyListManager.GetList(config.CraftingPurchaseListId);

        if (_openSetupGuide)
        {
            _openSetupGuide = false;
            M3Dialog.Open(SetupGuideDialogId);
        }

        DrawTurnInCard(manager, config);
        DrawAutomationCard(manager, config);
        DrawRouteCard(routes, selectedRoute);
        DrawPurchaseCard(config, selectedGatheringList, selectedCraftingList);
        DrawStatusCard(selectedGatheringList, selectedCraftingList);
        DrawSetupGuide();
    }

    private static void DrawTurnInCard(CollectableManager manager, CollectableConfig config)
    {
        using var card = M3Card.Begin("collectables_turn_in", "交付", FontAwesomeIcon.ExchangeAlt, M3.AccentFor(0), M3CardStyle.Filled,
            "为自动采集和 Vulcan 提供共用的收藏品自动交付与购买。");

        var turnInsAvailable = CollectableTurnInRequirements.IsAvailable;
        if (!turnInsAvailable)
        {
            M3Widgets.Banner("##collectables_unavailable", CollectableTurnInRequirements.UnavailableHelpText, M3Severity.Warning,
                FontAwesomeIcon.ExclamationTriangle);
            ImGui.Dummy(new Vector2(0f, M3.Space1));
        }

        if (!config.AutoTurnInCollectables && !string.IsNullOrWhiteSpace(config.AutoTurnInHardFailReason))
        {
            M3Widgets.Banner("##collectables_hard_fail",
                $"收藏品处理失败，已关闭自动交付：{config.AutoTurnInHardFailReason}", M3Severity.Error,
                FontAwesomeIcon.ExclamationCircle);
            ImGui.Dummy(new Vector2(0f, M3.Space1));
        }

        var running = manager.IsRunning;
        var buttonLabel = running ? "停止交付" : "开始交付";
        var buttonIcon = running ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play;
        var pill = M3Widgets.PluginPillSize(RequiredCollectablePlugins, true);
        var button = new Vector2(M3Widgets.ButtonWidth(buttonIcon, buttonLabel), M3Widgets.ButtonHeight);
        var gap = M3.Space2;
        var controlSize = new Vector2(pill.X + gap + button.X, MathF.Max(pill.Y, button.Y));

        var row = M3SettingRow.Begin(running ? "正在缴纳收藏品" : "立即缴纳",
            running ? "可在此停止本次缴纳。" : "立即开始缴纳收藏品。", controlSize);
        var origin = row.ControlPosition;
        ImGui.SetCursorScreenPos(origin + new Vector2(0f, (controlSize.Y - pill.Y) * 0.5f));
        M3Widgets.PluginPill("##collectables_plugins", RequiredCollectablePlugins, true);
        ImGui.SetCursorScreenPos(origin + new Vector2(pill.X + gap, (controlSize.Y - button.Y) * 0.5f));
        if (running)
        {
            if (M3Widgets.Button("##collectables_stop", buttonLabel, M3ButtonStyle.Danger, buttonIcon))
            {
                manager.Stop();
            }
        }
        else if (M3Widgets.Button("##collectables_run", buttonLabel, M3ButtonStyle.Filled, buttonIcon, enabled: turnInsAvailable,
                     tooltip: turnInsAvailable ? null : CollectableTurnInRequirements.UnavailableHelpText) && turnInsAvailable)
        {
            manager.Start(CollectableRunSource.Manual);
        }

        M3SettingRow.End(row);
    }

    private static void DrawAutomationCard(CollectableManager manager, CollectableConfig config)
    {
        using var card = M3Card.Begin("collectables_automation", "自动化", FontAwesomeIcon.Robot, M3.AccentFor(1));

        var turnInsAvailable = CollectableTurnInRequirements.IsAvailable;
        var previousHardFailReason = config.AutoTurnInHardFailReason;
        SettingRows.PluginSwitch("自动缴纳收藏品",
            turnInsAvailable ? "允许自动采集和 Vulcan 制作队列自动触发收藏品缴纳。" : CollectableTurnInRequirements.UnavailableHelpText,
            config.AutoTurnInCollectables, v =>
            {
                config.AutoTurnInCollectables = v;
                if (!v)
                {
                    return;
                }

                config.AutoTurnInHardFailReason = string.Empty;
                if (!manager.IsRunning && string.Equals(manager.StatusText, previousHardFailReason, StringComparison.Ordinal))
                {
                    manager.ClearStatus();
                }
            }, true, turnInsAvailable || config.AutoTurnInCollectables, RequiredCollectablePlugins);

        SettingRows.Switch("缴纳后执行商店购买清单",
            "缴纳后执行所选购买清单。票据快达到上限时，也可先执行清单以腾出余量。",
            config.BuyAfterEachCollect, v => config.BuyAfterEachCollect = v);
        SettingRows.Switch("Vulcan 队列缴纳后返回住宅",
            "制作队列因缴纳收藏品暂停后，返回住宅再继续制作。",
            HomeNavigationHelper.ShouldReturnHomeAfterCollectables(), v => GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle = v);
        SettingRows.Switch("按背包占用量触发缴纳",
            "按已占用的背包格数触发缴纳。",
            config.UseInventoryFullThreshold, v => config.UseInventoryFullThreshold = v);

        using var sub = M3SubGroup.Begin();
        if (config.UseInventoryFullThreshold)
        {
            SettingRows.Number("背包占用阈值", "占用的背包格数达到此值时缴纳。",
                config.InventoryFullThreshold, 1, 140, v => config.InventoryFullThreshold = v, 1, "格");
        }
        else
        {
            SettingRows.Number("收藏品数量阈值", "持有的收藏品数量达到此值时缴纳。",
                config.CollectableInventoryThreshold, 1, 140, v => config.CollectableInventoryThreshold = v);
        }
    }

    private static void DrawRouteCard(IReadOnlyList<CollectableTurnInRouteOption> routes, CollectableTurnInRouteOption? selectedRoute)
    {
        using var card = M3Card.Begin("collectables_route", "交付路线", FontAwesomeIcon.Route, M3.AccentFor(2), M3CardStyle.Filled,
            "选择收藏品交付地点。");

        if (routes.Count == 0)
        {
            var status = CollectableTurnInRouteResolver.HasLookupData
                ? VendorNpcLocationCache.IsInitializing
                    ? "收藏品交付地点仍在加载。"
                    : "目前没有可用的收藏品交付路线。"
                : "收藏品交付路线数据不可用。";
            M3Text.Draw(status, M3TextStyle.Muted, true);
            return;
        }

        var index = -1;
        if (selectedRoute != null)
        {
            for (var i = 0; i < routes.Count; ++i)
            {
                if (SameRoute(routes[i], selectedRoute))
                {
                    index = i;
                    break;
                }
            }
        }

        var supporting = selectedRoute != null
            ? $"NPC：{selectedRoute.Vendor.Name} · 区域：{selectedRoute.ZoneName} · 来源：{selectedRoute.Location.Source}"
            : "选择交付收藏品的位置。";
        var previous = index;
        if (M3Widgets.RowCombo("首选缴纳路线", ref index, routes.Count, i => routes[i].DisplayName, supporting, search: routes.Count > 8)
         && index != previous && index >= 0 && index < routes.Count)
        {
            GatherBuddy.Config.CollectableConfig.PreferredTurnInRoute = CollectableTurnInRouteResolver.ToPreference(routes[index]);
            GatherBuddy.Config.Save();
        }
    }

    private static bool SameRoute(CollectableTurnInRouteOption route, CollectableTurnInRouteOption selected)
        => route.ShopId == selected.ShopId
         && route.Vendor.NpcId == selected.Vendor.NpcId
         && route.Location.TerritoryId == selected.Location.TerritoryId
         && route.Location.MapRowId == selected.Location.MapRowId
         && Vector3.DistanceSquared(route.Location.Position, selected.Location.Position) < 0.01f;

    private static void DrawPurchaseCard(CollectableConfig config, VendorBuyListDefinition? selectedGatheringList,
        VendorBuyListDefinition? selectedCraftingList)
    {
        using var card = M3Card.Begin("collectables_purchase", "购买清单", FontAwesomeIcon.ShoppingCart, M3.AccentFor(0), M3CardStyle.Filled,
            "选择收藏品交付后消耗工票时使用的购买清单。");

        SettingRows.Number("保留票据", "购买清单消耗票据时，每种票据至少保留此数量。",
            config.ReserveScripAmount, 0, 4000, v => config.ReserveScripAmount = v, 50);

        if (VendorBuyListManager.Lists.Count == 0)
        {
            M3Text.Draw("还没有商店购买清单。可在商店购买清单窗口或 Vulcan 的商店页创建。", M3TextStyle.Muted, true);
            return;
        }

        DrawPurchaseList("采集购买清单", "用于自动采集触发的缴纳以及手动缴纳采集收藏品。", config.GatheringPurchaseListId,
            id => config.GatheringPurchaseListId = id, selectedGatheringList);
        DrawPurchaseList("制作购买清单", "用于 Vulcan 队列触发的缴纳以及手动缴纳制作收藏品。", config.CraftingPurchaseListId,
            id => config.CraftingPurchaseListId = id, selectedCraftingList);
    }

    private static void DrawPurchaseList(string label, string purpose, Guid selectedListId, Action<Guid> setter,
        VendorBuyListDefinition? selectedList)
    {
        var lists = VendorBuyListManager.GetListsByCreation(false);
        var index = selectedListId == Guid.Empty ? 0 : lists.FindIndex(list => list.Id == selectedListId) + 1;
        var supporting = selectedList != null ? $"{purpose} {GetPurchaseListSummary(selectedList)}." : purpose;
        var previous = index;
        if (M3Widgets.RowCombo($"{label}##{label}", ref index, lists.Count + 1, i => i == 0 ? "未选择清单" : lists[i - 1].Name, supporting,
                search: lists.Count > 8)
         && index != previous && index >= 0 && index <= lists.Count)
        {
            setter(index == 0 ? Guid.Empty : lists[index - 1].Id);
            GatherBuddy.Config.Save();
        }

        var active = VendorBuyListManager.ActiveList;
        using var sub = M3SubGroup.Begin();
        if (M3Widgets.RowButton($"使用当前商店购买清单##{label}_active", "使用当前清单",
                active != null ? $"当前清单为 {active.Name}." : "当前没有启用的商店购买清单。", M3ButtonStyle.Text, FontAwesomeIcon.ArrowLeft,
                active != null && active.Id != selectedListId)
         && active != null)
        {
            setter(active.Id);
            GatherBuddy.Config.Save();
        }
    }

    private static string GetPurchaseListSummary(VendorBuyListDefinition selectedList)
    {
        var pendingCount = VendorBuyListManager.GetPendingEntryCount(selectedList);
        return $"{selectedList.Entries.Count} 个条目 · {pendingCount} 个待处理 (基于当前物品栏)";
    }

    private static void DrawStatusCard(VendorBuyListDefinition? selectedGatheringList, VendorBuyListDefinition? selectedCraftingList)
    {
        using var card = M3Card.Begin("collectables_status", "背包", FontAwesomeIcon.Box, M3.AccentFor(1));

        CollectableInventoryHelper.InitializeAsync();
        if (!CollectableInventoryHelper.IsTurnInItemMetadataReady)
        {
            M3Text.Draw(CollectableInventoryHelper.IsTurnInItemMetadataLoading
                ? "收藏品数据仍在加载。"
                : "收藏品数据不可用。", M3TextStyle.Muted, true);
        }
        else
        {
            var thresholdState = CollectableInventoryHelper.GetThresholdState(GatherBuddy.Config.CollectableConfig);
            M3Widgets.RowValue("携带的收藏品", thresholdState.CollectableCount.ToString());
            M3Widgets.RowValue("背包", $"{thresholdState.UsedSlots} / {thresholdState.TotalSlots}");
        }

        if (!GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect)
        {
            return;
        }

        if (selectedGatheringList == null)
        {
            ImGui.Dummy(new Vector2(0f, M3.Space1));
            M3Widgets.Banner("##collectables_no_gathering_list", "为自动采集及手动交付采集收藏品选择购买清单。",
                M3Severity.Warning, FontAwesomeIcon.ExclamationTriangle);
        }

        if (selectedCraftingList == null)
        {
            ImGui.Dummy(new Vector2(0f, M3.Space1));
            M3Widgets.Banner("##collectables_no_crafting_list", "为 Vulcan 及手动交付制作收藏品选择购买清单。",
                M3Severity.Warning, FontAwesomeIcon.ExclamationTriangle);
        }
    }

    private static void DrawSetupGuide()
    {
        using var dialog = M3Dialog.Begin(SetupGuideDialogId, "收藏品设置指南",
            "先在 Vulcan 的商店页创建票据购买清单，再在此选用该清单，指定缴纳后要购买的物品。",
            FontAwesomeIcon.QuestionCircle, 600f);
        if (!dialog.IsOpen)
        {
            return;
        }

        var width = ImGui.GetContentRegionAvail().X;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        GuideStep("1. 在 Vulcan 的商店页创建购买清单",
            "打开 Vulcan 并进入商店页，搜索要用票据兑换的物品，设置数量，再点击加号将其加入当前购买清单。 "
          + "右键点击加号可新建清单，或将物品添加到其他清单。");
        GuideStep("2. 在商店购买清单中检查物品",
            "打开商店购买清单，可重命名清单、调整目标数量，并为有多个商人的物品指定购买地点。");
        GuideStep("3. 在收藏品窗口指定购买清单",
            "采集购买清单用于自动采集和手动缴纳采集收藏品，制作购买清单用于 Vulcan 队列和 "
          + "手动缴纳制作收藏品。点击「使用当前清单」可选用当前商店购买清单。");
        GuideStep("4. 启用需要的自动化选项",
            "开启「缴纳后执行商店购买清单」可在缴纳后使用票据购物。「保留票据」用于设置每种票据至少留下的数量。 "
          + "开启「自动缴纳收藏品」后，自动采集和 Vulcan 队列可以自行触发缴纳。");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, M3.Space1));
        M3Widgets.Banner("##collectables_guide_plugins",
            "如果无法自动交付或购买，请先安装或启用 Allagan Tools 或 Allagan Item Search。", M3Severity.Info,
            FontAwesomeIcon.InfoCircle);

        var pressed = M3Dialog.Actions(
        [
            new M3DialogAction("打开 Vulcan", M3ButtonStyle.Text, FontAwesomeIcon.Hammer),
            new M3DialogAction("打开商店购买清单", M3ButtonStyle.Text, FontAwesomeIcon.ShoppingCart),
            new M3DialogAction("关闭", M3ButtonStyle.Filled),
        ]);
        switch (pressed)
        {
            case 0:
                GatherBuddy.VulcanWindow?.RestoreWindow();
                break;
            case 1:
                GatherBuddy.VendorBuyListWindow?.Open();
                break;
        }
    }

    private static void GuideStep(string title, string description)
    {
        M3Text.Draw(title, M3TextStyle.Title);
        M3Text.Draw(description, M3TextStyle.Muted, true);
        ImGui.Dummy(new Vector2(0f, M3.Space2));
    }
}
