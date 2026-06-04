using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using GatherBuddy.AutoGather.Collectables;
using GatherBuddy.AutoGather.Collectables.Data;
using GatherBuddy.Config;
using GatherBuddy.Helpers;
using GatherBuddy.Vulcan.Vendors;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public sealed class CollectablesWindow : Window
{
    public const string WindowId = "Collectables###GatherBuddyCollectablesWindow";
    private const string SetupGuidePopupId = "Collectables Setup Guide###GatherBuddyCollectablesSetupGuide";
    private static readonly ImGuiEx.RequiredPluginInfo[] RequiredCollectablePlugins =
    [
        new("InventoryTools", "Allagan Tools"),
        new(CollectableTurnInRequirements.AllaganItemSearchInternalName, "Allagan Item Search"),
    ];

    private bool _wasFocusedLastFrame;

    public CollectablesWindow()
        : base(WindowId)
    {
        Size = VulcanUiScaling.Scaled(860f, 560f);
        SizeCondition = ImGuiCond.FirstUseEver;
        RespectCloseHotkey = true;
        ShowCloseButton = true;
        IsOpen = false;
    }

    public void Open()
        => IsOpen = true;

    public override void Draw()
    {
        using var theme = VulcanUiStyle.PushTheme();

        var isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (isFocused)
        {
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow(WindowId);
            _wasFocusedLastFrame = true;
        }
        else if (_wasFocusedLastFrame)
        {
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow(null);
            _wasFocusedLastFrame = false;
        }

        var manager = GatherBuddy.CollectableManager;
        var config = GatherBuddy.Config.CollectableConfig;
        var routes = CollectableTurnInRouteResolver.GetAvailableRoutes();
        var selectedRoute = CollectableTurnInRouteResolver.ResolvePreferredRoute(config.PreferredTurnInRoute, routes);
        var vendorBuyListManager = GatherBuddy.VendorBuyListManager;
        var selectedGatheringList = vendorBuyListManager.Lists.FirstOrDefault(list => list.Id == config.GatheringPurchaseListId);
        var selectedCraftingList = vendorBuyListManager.Lists.FirstOrDefault(list => list.Id == config.CraftingPurchaseListId);

        ImGui.TextColored(ImGuiColors.DalamudGrey3, "Configure shared collectables turn-ins, purchase automation, and manual runs.");
        ImGui.Spacing();
        DrawExecutionControls(manager);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawAutomationSettings(manager, config);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawTurnInRouteSettings(routes, selectedRoute);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawPurchaseSettings(config, vendorBuyListManager, selectedGatheringList, selectedCraftingList);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawStatus(manager, selectedGatheringList, selectedCraftingList);
        DrawSetupGuidePopup();
    }

    private static void DrawExecutionControls(CollectableManager manager)
    {
        var turnInsAvailable = CollectableTurnInRequirements.IsAvailable;
        if (ImGui.Button("Setup Guide", VulcanUiScaling.Scaled(120f, 0f)))
            ImGui.OpenPopup(SetupGuidePopupId);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Explain how to build and assign collectables purchase lists using Vulcan Vendors and Vendor Buy Lists.");

        ImGui.SameLine();
        if (manager.IsRunning)
        {
            if (ImGui.Button("Stop Collectables Run", VulcanUiScaling.Scaled(180f, 0f)))
                manager.Stop();
        }
        else
        {
            using var disabledRunButton = ImRaii.Disabled(!turnInsAvailable);
            if (ImGui.Button("Run Turn-Ins Now", VulcanUiScaling.Scaled(180f, 0f)) && turnInsAvailable)
                manager.Start(CollectableRunSource.Manual);
            if (ImGui.IsItemHovered(turnInsAvailable ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(turnInsAvailable
                    ? "Runs collectable turn-ins immediately."
                    : CollectableTurnInRequirements.UnavailableHelpText);
        }

        ImGuiEx.PluginAvailabilityIndicator(RequiredCollectablePlugins, "需要以下插件之一:", all: false);

        if (ImGui.Button("Open Vulcan", VulcanUiScaling.Scaled(120f, 0f)))
            GatherBuddy.VulcanWindow?.RestoreWindow();

        ImGui.SameLine();
        if (ImGui.Button("Open Vendor Buy Lists", VulcanUiScaling.Scaled(170f, 0f)))
            GatherBuddy.VendorBuyListWindow?.Open();
    }

    private static void DrawAutomationSettings(CollectableManager manager, CollectableConfig config)
    {
        var turnInsAvailable = CollectableTurnInRequirements.IsAvailable;
        var autoTurnIn = config.AutoTurnInCollectables;
        var previousHardFailReason = config.AutoTurnInHardFailReason;
        if (!turnInsAvailable && !autoTurnIn)
        {
            using var disabledAutoTurnIn = ImRaii.Disabled(true);
            ImGui.Checkbox("自动缴纳收藏品", ref autoTurnIn);
        }
        else if (ImGui.Checkbox("自动缴纳收藏品", ref autoTurnIn))
        {
            config.AutoTurnInCollectables = autoTurnIn;
            if (autoTurnIn)
            {
                config.AutoTurnInHardFailReason = string.Empty;
                if (!manager.IsRunning && string.Equals(manager.StatusText, previousHardFailReason, StringComparison.Ordinal))
                    manager.ClearStatus();
            }
            GatherBuddy.Config.Save();
        }
        var autoTurnInHovered = ImGui.IsItemHovered(turnInsAvailable || autoTurnIn ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled);
        ImGuiEx.PluginAvailabilityIndicator(RequiredCollectablePlugins, "需要以下插件之一:", all: false);
        if (autoTurnInHovered)
            ImGui.SetTooltip(turnInsAvailable
                ? "允许自动采集和 Vulcan 队列自动执行收藏品缴纳"
                : CollectableTurnInRequirements.UnavailableHelpText);

        if (!turnInsAvailable)
        {
            DrawWrappedColoredText(ImGuiColors.DalamudYellow, CollectableTurnInRequirements.UnavailableHelpText);
            ImGui.Spacing();
        }

        if (!config.AutoTurnInCollectables && !string.IsNullOrWhiteSpace(config.AutoTurnInHardFailReason))
        {
            DrawWrappedColoredText(ImGuiColors.DalamudRed, "收藏品发生严重失败后自动缴纳已被强制关闭");
            DrawWrappedColoredText(ImGuiColors.DalamudYellow, config.AutoTurnInHardFailReason);
            ImGui.Spacing();
        }

        var runPurchaseList = config.BuyAfterEachCollect;
        if (ImGui.Checkbox("缴纳后运行商店购买清单", ref runPurchaseList))
        {
            config.BuyAfterEachCollect = runPurchaseList;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("缴纳后运行所选购买清单, 或在工票上限时需要腾出空间时运行");

        var returnHome = HomeNavigationHelper.ShouldReturnHomeAfterCollectables();
        if (ImGui.Checkbox("Vulcan 队列缴纳后回家", ref returnHome))
        {
            GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle = returnHome;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("收藏品队列中断后, 制作恢复前先回家");

        ImGui.Spacing();
        var useInventoryFullThreshold = config.UseInventoryFullThreshold;
        if (ImGui.Checkbox("使用物品栏已满阈值", ref useInventoryFullThreshold))
        {
            config.UseInventoryFullThreshold = useInventoryFullThreshold;
            GatherBuddy.Config.Save();
        }

        ImGui.SameLine();
        if (useInventoryFullThreshold)
        {
            var inventoryThreshold = config.InventoryFullThreshold;
            ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(130f));
            if (ImGui.DragInt("物品栏阈值", ref inventoryThreshold, 1f, 1, 140))
            {
                config.InventoryFullThreshold = Math.Clamp(inventoryThreshold, 1, 140);
                GatherBuddy.Config.Save();
            }
        }
        else
        {
            var collectableThreshold = config.CollectableInventoryThreshold;
            ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(130f));
            if (ImGui.DragInt("收藏品阈值", ref collectableThreshold, 1f, 1, 140))
            {
                config.CollectableInventoryThreshold = Math.Clamp(collectableThreshold, 1, 140);
                GatherBuddy.Config.Save();
            }
        }
    }

    private static void DrawTurnInRouteSettings(IReadOnlyList<CollectableTurnInRouteOption> routes, CollectableTurnInRouteOption? selectedRoute)
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, "缴纳路线");
        if (routes.Count == 0)
        {
            var status = CollectableTurnInRouteResolver.HasLookupData
                ? VendorNpcLocationCache.IsInitializing
                    ? "收藏品路线位置仍在加载中"
                    : "当前没有可用收藏品缴纳路线"
                : "收藏品路线数据不可用";
            ImGui.TextColored(ImGuiColors.DalamudGrey3, status);
            return;
        }

        var previewLabel = selectedRoute?.DisplayName ?? "选择缴纳路线...";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("首选缴纳路线", previewLabel))
        {
            foreach (var route in routes)
            {
                var isSelected = selectedRoute != null
                    && route.ShopId == selectedRoute.ShopId
                    && route.Vendor.NpcId == selectedRoute.Vendor.NpcId
                    && route.Location.TerritoryId == selectedRoute.Location.TerritoryId
                    && route.Location.MapRowId == selectedRoute.Location.MapRowId
                    && Vector3.DistanceSquared(route.Location.Position, selectedRoute.Location.Position) < 0.01f;
                if (!ImGui.Selectable(route.DisplayName, isSelected))
                    continue;

                GatherBuddy.Config.CollectableConfig.PreferredTurnInRoute = CollectableTurnInRouteResolver.ToPreference(route);
                GatherBuddy.Config.Save();
            }
            ImGui.EndCombo();
        }

        if (selectedRoute != null)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey3,
                $"NPC: {selectedRoute.Vendor.Name} · 区域: {selectedRoute.ZoneName} · 来源: {selectedRoute.Location.Source}");
        }
    }


    private static void DrawSetupGuidePopup()
    {
        ImGui.SetNextWindowSize(VulcanUiScaling.Scaled(640f, 430f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup(SetupGuidePopupId, ImGuiWindowFlags.NoResize))
            return;

        ImGui.TextColored(ImGuiColors.ParsedGold, "收藏品设置向导");
        DrawWrappedText("使用 Vulcan 的「商店」标签页构建工票购买清单, 然后在此处分配, 收藏品运行便会知道缴纳后该购买什么");
        ImGui.Spacing();

        if (ImGui.Button("Open Vulcan", VulcanUiScaling.Scaled(120f, 0f)))
            GatherBuddy.VulcanWindow?.RestoreWindow();
        ImGui.SameLine();
        if (ImGui.Button("Open Vendor Buy Lists", VulcanUiScaling.Scaled(170f, 0f)))
            GatherBuddy.VendorBuyListWindow?.Open();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSetupGuideStep(
            "1. 在 Vulcan 商店中构建购买清单",
            "打开 Vulcan, 切换到「商店」标签页, 搜索你要的工票物品, 设定数量, 然后点击 + 按钮添加到当前商店清单。如果需要新建清单或添加到其他已有清单, 请右键点击 + 按钮");
        DrawSetupGuideStep(
            "2. 在商店购买清单中检查",
            "打开商店购买清单, 重命名清单、调整目标数量, 并确认物品有多个 NPC 选项时的所选商店路线");
        DrawSetupGuideStep(
            "3. 在收藏品中分配清单",
            "在采集收藏品购买清单中选择一个清单用于自动采集运行和手动采集缴纳。在制作收藏品购买清单中选择一个清单用于 Vulcan 队列运行和手动制作缴纳。「使用当前商店清单」按钮会将当前活跃的商店清单复制到对应栏位");
        DrawSetupGuideStep(
            "4. 启用所需购买行为",
            "如需收藏品运行自动消耗工票, 请开启「缴纳后运行商店购买清单」。保留工票会留出缓冲, 避免清单花掉你最后的工票。如需自动采集或 Vulcan 队列运行自动触发缴纳, 请开启「自动缴纳收藏品」");

        ImGui.Spacing();
        DrawWrappedColoredText(ImGuiColors.DalamudYellow,
            "如果缴纳或购买自动化不可用, 请先安装或启用 Allagan Tools 或 Allagan Item Search");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("关闭", VulcanUiScaling.Scaled(100f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }
    private static void DrawPurchaseSettings(
        CollectableConfig config,
        VendorBuyListManager manager,
        VendorBuyListDefinition? selectedGatheringList,
        VendorBuyListDefinition? selectedCraftingList)
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, "购买清单");

        var reserveScripAmount = config.ReserveScripAmount;
        ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(130f));
        if (ImGui.DragInt("保留工票", ref reserveScripAmount, 1f, 0, 4000))
        {
            config.ReserveScripAmount = Math.Clamp(reserveScripAmount, 0, 4000);
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("收藏品购买清单消费工票时, 每种工票至少保留此数量");

        ImGui.Spacing();

        if (manager.Lists.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey3, "没有可用的商店购买清单");
            return;
        }

        DrawPurchaseListSelector(
            "采集收藏品购买清单",
            config.GatheringPurchaseListId,
            id => config.GatheringPurchaseListId = id,
            manager);
        if (selectedGatheringList != null)
            ImGui.TextColored(ImGuiColors.DalamudGrey3, GetPurchaseListSummary(manager, selectedGatheringList));

        using (var disabled = ImRaii.Disabled(manager.ActiveList == null || manager.ActiveList.Id == config.GatheringPurchaseListId))
        {
            if (ImGui.Button("使用当前商店清单 (采集)", VulcanUiScaling.Scaled(250f, 0f)) && manager.ActiveList != null)
            {
                config.GatheringPurchaseListId = manager.ActiveList.Id;
                GatherBuddy.Config.Save();
            }
        }

        ImGui.Spacing();
        DrawPurchaseListSelector(
            "制作收藏品购买清单",
            config.CraftingPurchaseListId,
            id => config.CraftingPurchaseListId = id,
            manager);
        if (selectedCraftingList != null)
            ImGui.TextColored(ImGuiColors.DalamudGrey3, GetPurchaseListSummary(manager, selectedCraftingList));

        using var disabledCrafting = ImRaii.Disabled(manager.ActiveList == null || manager.ActiveList.Id == config.CraftingPurchaseListId);
        if (ImGui.Button("使用当前商店清单 (制作)", VulcanUiScaling.Scaled(250f, 0f)) && manager.ActiveList != null)
        {
            config.CraftingPurchaseListId = manager.ActiveList.Id;
            GatherBuddy.Config.Save();
        }
    }

    private static void DrawPurchaseListSelector(string label, Guid selectedListId, Action<Guid> setter, VendorBuyListManager manager)
    {
        var selectedList = manager.Lists.FirstOrDefault(list => list.Id == selectedListId);
        var previewLabel = selectedList?.Name ?? "未选择清单";
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo(label, previewLabel))
            return;

        if (ImGui.Selectable("未选择清单", selectedListId == Guid.Empty))
        {
            setter(Guid.Empty);
            GatherBuddy.Config.Save();
        }

        foreach (var list in manager.Lists.OrderBy(list => list.CreatedAt))
        {
            var isSelected = list.Id == selectedListId;
            if (!ImGui.Selectable(list.Name, isSelected))
                continue;

            setter(list.Id);
            GatherBuddy.Config.Save();
        }

        ImGui.EndCombo();
    }

    private static string GetPurchaseListSummary(VendorBuyListManager manager, VendorBuyListDefinition selectedList)
    {
        var pendingCount = selectedList.Entries.Count(managerEntry => manager.GetRemainingQuantity(managerEntry) > 0);
        return $"{selectedList.Entries.Count} 个条目 · {pendingCount} 个待处理 (基于当前物品栏)";
    }

    private static void DrawStatus(
        CollectableManager manager,
        VendorBuyListDefinition? selectedGatheringList,
        VendorBuyListDefinition? selectedCraftingList)
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, "状态");
        var stateColor = manager.IsRunning ? ImGuiColors.ParsedGold : ImGuiColors.DalamudGrey3;
        DrawWrappedColoredText(stateColor, string.IsNullOrWhiteSpace(manager.StatusText) ? "空闲" : manager.StatusText);
        if (!CollectableTurnInRequirements.IsAvailable)
            DrawWrappedColoredText(ImGuiColors.DalamudYellow, CollectableTurnInRequirements.UnavailableHelpText);
        CollectableInventoryHelper.InitializeAsync();
        if (!CollectableInventoryHelper.IsTurnInItemMetadataReady)
        {
            var status = CollectableInventoryHelper.IsTurnInItemMetadataLoading
                ? "收藏品物品数据仍在加载中"
                : "收藏品物品数据不可用";
            DrawWrappedColoredText(ImGuiColors.DalamudGrey3, status);
        }
        else
        {
            var thresholdState = CollectableInventoryHelper.GetThresholdState(GatherBuddy.Config.CollectableConfig);
            ImGui.TextColored(ImGuiColors.DalamudGrey3,
                $"收藏品: {thresholdState.CollectableCount} · 物品栏: {thresholdState.UsedSlots}/{thresholdState.TotalSlots}");
        }
        if (!GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect)
            return;

        if (selectedGatheringList == null)
            DrawWrappedColoredText(ImGuiColors.DalamudYellow, "请为自动采集和手动采集缴纳选择采集购买清单");

        if (selectedCraftingList == null)
            DrawWrappedColoredText(ImGuiColors.DalamudYellow, "请为 Vulcan 和手动制作缴纳选择制作购买清单");
    }

    private static void DrawWrappedColoredText(Vector4 color, string text)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextColored(color, text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawWrappedText(string text)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    private static void DrawSetupGuideStep(string title, string description)
    {
        ImGui.TextColored(ImGuiColors.ParsedGold, title);
        DrawWrappedColoredText(ImGuiColors.DalamudGrey3, description);
        ImGui.Spacing();
    }
}
