using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan.Vendors;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private static readonly uint[] DebugJobIds = [8, 9, 10, 11, 12, 13, 14, 15];

    private static readonly M3Segment[] VendorLocationSegments =
    [
        new("优先使用 DataShare", FontAwesomeIcon.Database, "优先使用 Allagan Tools 的 DataShare 坐标，缺失时从 Level 表、ENpcPlace 补充数据和 planevent.lgb 中查找。"),
        new("优先使用 LGB", FontAwesomeIcon.Map, "优先使用 planevent.lgb 坐标，缺失时从 Level 表、ENpcPlace 补充数据和 Allagan Tools 的 DataShare 中查找。"),
    ];

    private void DrawDebugPage()
    {
        using var child = ImRaii.Child("##vulcanDebug", Vector2.Zero);
        if (!child)
        {
            return;
        }

        using (M3Card.Begin("##debugMenus", "右键菜单", FontAwesomeIcon.Bars, M3.AccentFor(0)))
        {
            var maxRecentLists = GatherBuddy.Config.MaxRecentCraftingListsInContextMenu;
            if (M3Widgets.RowNumber("菜单中的最近清单", ref maxRecentLists, 1, 1, 50, supporting: "右键菜单中显示的最近制作清单数量。"))
            {
                GatherBuddy.Config.MaxRecentCraftingListsInContextMenu = Math.Clamp(maxRecentLists, 1, 50);
                GatherBuddy.Config.Save();
            }
        }

        DrawVendorNpcLocationDebug();

        using (M3Card.Begin("##debugRepair", "修理", FontAwesomeIcon.Wrench, M3.AccentFor(2)))
        {
            M3Widgets.RowValue("已穿戴装备的最低耐久度", $"{Crafting.RepairManager.GetMinEquippedPercent()}%");
            M3Widgets.RowValue("可以自行修理", Crafting.RepairManager.CanRepairAny() ? "是" : "否");
            var npcNearby = Crafting.RepairManager.RepairNPCNearby(out _);
            M3Widgets.RowValue("附近修理工", npcNearby ? "是" : "否");
            if (npcNearby)
            {
                M3Widgets.RowValue("修理工报价", $"{Crafting.RepairManager.GetNPCRepairPrice():N0} 金币");
            }
        }

        using (M3Card.Begin("##debugMateria", "精制魔晶石", FontAwesomeIcon.Gem, M3.AccentFor(3)))
        {
            M3Widgets.RowValue("已解锁魔晶石精制", Crafting.MateriaManager.IsExtractionUnlocked() ? "是" : "否");
            M3Widgets.RowValue("可精制的装备", Crafting.MateriaManager.ReadySpiritbondItemCount().ToString());
            M3Widgets.RowValue("背包空位", Crafting.MateriaManager.HasFreeInventorySlots() ? "是" : "否");
        }

        using (M3Card.Begin("##debugGearset", "套装能力值", FontAwesomeIcon.UserCog, M3.AccentFor(4)))
        {
            var jobIndex = Math.Max(0, Array.IndexOf(DebugJobIds, _debugSelectedJobId));
            if (M3Widgets.RowCombo("职业", ref jobIndex, DebugJobIds.Length, i => GetDebugJobName(DebugJobIds[i])))
            {
                _debugSelectedJobId = DebugJobIds[jobIndex];
                _debugLastTestResult = null;
            }

            if (M3Widgets.Button("##readStats", "读取能力值", M3ButtonStyle.Tonal, FontAwesomeIcon.Search))
            {
                var stats = GearsetStatsReader.ReadGearsetStatsForJob(_debugSelectedJobId);
                _debugLastTestResult = stats != null
                    ? $"读取成功，作业精度{stats.Craftsmanship}，加工精度{stats.Control}，制作力{stats.CP}，掌握已解锁{stats.Manipulation}"
                    : "无法读取该职业的套装能力值";
            }

            ImGui.SameLine(0, M3.Space2);
            if (M3Widgets.Button("##updateGearset", "更新已保存的套装", M3ButtonStyle.Text, FontAwesomeIcon.Save,
                    tooltip: "使用当前装备更新所选职业的已保存装备套装, 需要切换至该职业"))
            {
                var refreshResult = GearsetStatsReader.RefreshGearsetFromCurrentEquipped(_debugSelectedJobId);
                var stats = GearsetStatsReader.ReadGearsetStatsForJob(_debugSelectedJobId);
                _debugLastTestResult = stats != null
                    ? $"{refreshResult} 当前读取结果: 作业精度={stats.Craftsmanship}, 加工精度={stats.Control}，制作力{stats.CP}, 掌握={stats.Manipulation}"
                    : refreshResult;
            }

            if (_debugLastTestResult != null)
            {
                M3Text.Draw(_debugLastTestResult, M3TextStyle.Muted, true);
            }
        }

        DrawGamepadInputTest();
    }

    private static void DrawVendorNpcLocationDebug()
    {
        using var card = M3Card.Begin("##debugVendorLocations", "商人坐标", FontAwesomeIcon.MapMarkerAlt, M3.AccentFor(1));
        var dataShareFirst = GatherBuddy.Config.VendorNpcLocationsDataShareFirst;
        var index = dataShareFirst ? 0 : 1;
        if (M3Widgets.RowSegmented("数据来源偏好", ref index, VendorLocationSegments))
        {
            SetVendorNpcLocationSourcePreference(index == 0);
        }

        M3Widgets.RowValue("查找顺序", dataShareFirst ? "DataShare、Level 表、补充数据、LGB" : "LGB、Level 表、补充数据、DataShare");
        M3Widgets.RowValue("缓存", GetVendorNpcLocationCacheStatus());
    }

    private static string PressedButtons(params (GamepadButtons Button, string Name)[] buttons)
    {
        var gamepad = Dalamud.GamepadState;
        var pressed = new List<string>(buttons.Length);
        foreach (var (button, name) in buttons)
        {
            if (gamepad.Pressed(button) > 0)
            {
                pressed.Add(name);
            }
        }

        return pressed.Count > 0 ? string.Join(", ", pressed) : "无";
    }

    private static void DrawGamepadInputTest()
    {
        using var card = M3Card.Begin("##debugGamepad", "手柄输入", FontAwesomeIcon.Gamepad, M3.AccentFor(5));
        var gamepad = Dalamud.GamepadState;
        M3Widgets.RowValue("左摇杆", $"X {gamepad.LeftStick.X:F3}, Y {gamepad.LeftStick.Y:F3}");
        M3Widgets.RowValue("右摇杆", $"X {gamepad.RightStick.X:F3}, Y {gamepad.RightStick.Y:F3}");
        M3Widgets.RowValue("方向键", PressedButtons((GamepadButtons.DpadUp, "上"), (GamepadButtons.DpadDown, "下"), (GamepadButtons.DpadLeft, "左"),
            (GamepadButtons.DpadRight, "右")));
        M3Widgets.RowValue("面板按键", PressedButtons((GamepadButtons.South, "A/叉"), (GamepadButtons.East, "B/圈"),
            (GamepadButtons.West, "X／方块键"), (GamepadButtons.North, "Y／三角键")));
        M3Widgets.RowValue("肩键", PressedButtons((GamepadButtons.L1, "L1"), (GamepadButtons.R1, "R1"), (GamepadButtons.L2, "L2"),
            (GamepadButtons.R2, "R2")));

        var io = ImGui.GetIO();
        M3Widgets.RowValue("导航已激活", io.NavActive ? "是" : "否");
        M3Widgets.RowValue("导航可见", io.NavVisible ? "是" : "否");
        M3Widgets.RowValue("配置标志", io.ConfigFlags.ToString());

        var navKeyboardEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        var navGamepadEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0;
        if (M3Widgets.Button("##gamepadNav", navGamepadEnabled ? "关闭手柄导航" : "开启手柄导航", M3ButtonStyle.Tonal,
                FontAwesomeIcon.Gamepad))
        {
            io = ImGui.GetIO();
            io.ConfigFlags ^= ImGuiConfigFlags.NavEnableGamepad;
            GatherBuddy.Log.Information($"[VulcanWindow] {(navGamepadEnabled ? "Disabled" : "Enabled")} ImGui gamepad navigation");
        }

        ImGui.SameLine(0, M3.Space2);
        if (M3Widgets.Button("##keyboardNav", navKeyboardEnabled ? "关闭键盘导航" : "开启键盘导航", M3ButtonStyle.Tonal,
                FontAwesomeIcon.Keyboard))
        {
            io = ImGui.GetIO();
            io.ConfigFlags ^= ImGuiConfigFlags.NavEnableKeyboard;
            GatherBuddy.Log.Information($"[VulcanWindow] {(navKeyboardEnabled ? "Disabled" : "Enabled")} ImGui keyboard navigation");
        }

        M3Text.Draw("按 Tab 键或手柄方向键进入导航。", M3TextStyle.Muted);
    }
    private static string GetDebugJobName(uint jobId) => jobId switch
    {
        8 => "刻木匠",
        9 => "锻铁匠",
        10 => "铸甲匠",
        11 => "雕金匠",
        12 => "制革匠",
        13 => "裁衣匠",
        14 => "炼金术士",
        15 => "烹调师",
        _ => "未知"
    };

    private static string GetTerritoryName(uint territoryId)
    {
        var territorySheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        if (territorySheet?.TryGetRow(territoryId, out var territory) == true)
        {
            return territory.PlaceName.ValueNullable?.Name.ExtractText() ?? "未知";
        }
        return "未知";
    }

    private static void SetVendorNpcLocationSourcePreference(bool dataShareFirst)
    {
        if (GatherBuddy.Config.VendorNpcLocationsDataShareFirst == dataShareFirst)
        {
            return;
        }

        GatherBuddy.Config.VendorNpcLocationsDataShareFirst = dataShareFirst;
        GatherBuddy.Config.Save();
        GatherBuddy.Log.Debug($"[VulcanWindow] Vendor NPC location source order set to {(dataShareFirst ? "DataShare -> Level -> Supplemental -> LGB" : "LGB -> Level -> Supplemental -> DataShare")}");
        VendorNpcLocationCache.ReloadAsync();
    }

    private static string GetVendorNpcLocationCacheStatus()
    {
        if (VendorNpcLocationCache.IsInitializing)
        {
            return $"重建中 ({VendorNpcLocationCache.ResolvedNpcCount}/{VendorNpcLocationCache.RequestedNpcCount})";
        }

        if (VendorNpcLocationCache.IsInitialized)
        {
            return $"就绪 ({VendorNpcLocationCache.ResolvedNpcCount}/{VendorNpcLocationCache.RequestedNpcCount})";
        }

        if (VendorShopResolver.IsInitializing)
        {
            return "等待商店数据";
        }

        if (!VendorShopResolver.IsInitialized)
        {
            return "商店数据未初始化";
        }

        return "位置缓存未初始化";
    }
}
