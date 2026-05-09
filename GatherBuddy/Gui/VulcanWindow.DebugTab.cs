using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan.Vendors;
using Lumina.Excel.Sheets;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private void DrawDebugTab()
    {
        IDisposable tabItem;
        bool tabOpen;
        
        if (GatherBuddy.ControllerSupport != null)
        {
        var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("调试##debugTab", 7, 9);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("调试##debugTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }
        
        using (tabItem)
        {
            if (!tabOpen)
                return;

        ImGui.BeginGroup();
        ImGui.Text("右键菜单设置");
        ImGui.Spacing();

        ImGui.Text("  最近清单上限:");
        ImGui.SameLine();
        var maxRecentLists = GatherBuddy.Config.MaxRecentCraftingListsInContextMenu;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("###MaxRecentLists", ref maxRecentLists, 1, 1))
        {
            GatherBuddy.Config.MaxRecentCraftingListsInContextMenu = Math.Max(1, Math.Min(50, maxRecentLists));
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("右键菜单中显示的最近制作清单数量上限 (1-50)");

        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawVendorNpcLocationDebug();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("修理状态");
        ImGui.Spacing();
        ImGui.Text($"  最低装备耐久: {Crafting.RepairManager.GetMinEquippedPercent()}%");
        ImGui.Text($"  可自行修理: {Crafting.RepairManager.CanRepairAny()}");
        ImGui.Text($"  附近有修理 NPC: {Crafting.RepairManager.RepairNPCNearby(out _)}");
        if (Crafting.RepairManager.RepairNPCNearby(out _))
        {
            ImGui.Text($"  NPC 修理价格: {Crafting.RepairManager.GetNPCRepairPrice()} 金币");
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("魔晶石精制状态");
        ImGui.Spacing();
        ImGui.Text($"  已解锁精制: {Crafting.MateriaManager.IsExtractionUnlocked()}");
        ImGui.Text($"  可精制装备数: {Crafting.MateriaManager.ReadySpiritbondItemCount()}");
        ImGui.Text($"  空余栏位: {Crafting.MateriaManager.HasFreeInventorySlots()}");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("装备套装属性测试");
        ImGui.Text("  选择职业:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("###JobSelector", GetDebugJobName(_debugSelectedJobId)))
        {
            var jobs = new[] { (8u, "刻木匠"), (9u, "锻铁匠"), (10u, "铸甲匠"), (11u, "雕金匠"), (12u, "制革匠"), (13u, "裁衣匠"), (14u, "炼金术士"), (15u, "烹调师") };
            foreach (var (jobId, jobName) in jobs)
            {
                if (ImGui.Selectable(jobName, _debugSelectedJobId == jobId))
                {
                    _debugSelectedJobId = jobId;
                    _debugLastTestResult = null;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        if (ImGui.Button("测试属性读取", new Vector2(150, 0)))
        {
            var stats = GearsetStatsReader.ReadGearsetStatsForJob(_debugSelectedJobId);
            if (stats != null)
            {
                _debugLastTestResult = $"成功: 作业精度={stats.Craftsmanship}, 加工精度={stats.Control}, CP={stats.CP}, 掌握={stats.Manipulation}";
            }
            else
            {
                _debugLastTestResult = "失败: 无法读取该职业的装备套装属性";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新装备套装", new Vector2(150, 0)))
        {
            GearsetStatsReader.RefreshGearsetFromCurrentEquipped(_debugSelectedJobId);
            _debugLastTestResult = "已根据当前装备刷新装备套装";
        }

        if (_debugLastTestResult != null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_debugLastTestResult);
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawGamepadInputTest();
        }
    }

    private static void DrawVendorNpcLocationDebug()
    {
        ImGui.BeginGroup();
        ImGui.Text("商店 NPC 位置来源");
        ImGui.Spacing();

        var dataShareFirst = GatherBuddy.Config.VendorNpcLocationsDataShareFirst;
        if (ImGui.RadioButton("优先 DataShare###vendorNpcLocationDataShareFirst", dataShareFirst))
            SetVendorNpcLocationSourcePreference(true);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("优先使用 AllaganTools DataShare 位置\n其余缺失位置再由 Level 表、ENpcPlace 补充数据和 planevent.lgb 补齐");

        ImGui.SameLine();

        if (ImGui.RadioButton("优先 LGB###vendorNpcLocationLgbFirst", !dataShareFirst))
            SetVendorNpcLocationSourcePreference(false);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("优先使用 planevent.lgb 位置\n其余缺失位置再由 Level 表、ENpcPlace 补充数据和 AllaganTools DataShare 补齐");

        ImGui.Spacing();
        ImGui.Text($"  来源顺序: {(dataShareFirst ? "DataShare -> Level -> Supplemental -> LGB" : "LGB -> Level -> Supplemental -> DataShare")}");
        ImGui.Text($"  缓存状态: {GetVendorNpcLocationCacheStatus()}");
        ImGui.EndGroup();
    }
    
    private void DrawGamepadInputTest()
    {
        ImGui.BeginGroup();
        ImGui.Text("手柄输入测试");
        ImGui.Separator();
        ImGui.Spacing();
        
        var gamepad = Dalamud.GamepadState;
        
        ImGui.Text("左摇杆:");
        ImGui.SameLine();
        ImGui.Text($"X: {gamepad.LeftStick.X:F3}, Y: {gamepad.LeftStick.Y:F3}");
        
        ImGui.Text("右摇杆:");
        ImGui.SameLine();
        ImGui.Text($"X: {gamepad.RightStick.X:F3}, Y: {gamepad.RightStick.Y:F3}");
        
        ImGui.Spacing();
        ImGui.Text("方向键:");
        ImGui.SameLine();
        var dpad = "无";
        if (gamepad.Pressed(GamepadButtons.DpadUp) > 0) dpad = "上";
        if (gamepad.Pressed(GamepadButtons.DpadDown) > 0) dpad = "下";
        if (gamepad.Pressed(GamepadButtons.DpadLeft) > 0) dpad = "左";
        if (gamepad.Pressed(GamepadButtons.DpadRight) > 0) dpad = "右";
        ImGui.Text(dpad);
        
        ImGui.Spacing();
        ImGui.Text("面键:");
        var faceButtons = new List<string>();
        if (gamepad.Pressed(GamepadButtons.South) > 0) faceButtons.Add("A/叉");
        if (gamepad.Pressed(GamepadButtons.East) > 0) faceButtons.Add("B/圈");
        if (gamepad.Pressed(GamepadButtons.West) > 0) faceButtons.Add("X/方");
        if (gamepad.Pressed(GamepadButtons.North) > 0) faceButtons.Add("Y/三角");
        ImGui.SameLine();
        ImGui.Text(faceButtons.Count > 0 ? string.Join(", ", faceButtons) : "无");
        
        ImGui.Spacing();
        ImGui.Text("肩键:");
        var shoulderButtons = new List<string>();
        if (gamepad.Pressed(GamepadButtons.L1) > 0) shoulderButtons.Add("L1");
        if (gamepad.Pressed(GamepadButtons.R1) > 0) shoulderButtons.Add("R1");
        if (gamepad.Pressed(GamepadButtons.L2) > 0) shoulderButtons.Add("L2");
        if (gamepad.Pressed(GamepadButtons.R2) > 0) shoulderButtons.Add("R2");
        ImGui.SameLine();
        ImGui.Text(shoulderButtons.Count > 0 ? string.Join(", ", shoulderButtons) : "无");
        
        ImGui.Spacing();
        ImGui.Text("ImGui 导航状态:");
        var io = ImGui.GetIO();
        ImGui.Text($"  NavActive: {io.NavActive}");
        ImGui.Text($"  NavVisible: {io.NavVisible}");
        ImGui.Text($"  ConfigFlags: {io.ConfigFlags}");
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        var navKeyboardEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        var navGamepadEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0;
        
        if (ImGui.Button(navGamepadEnabled ? "禁用手柄导航" : "启用手柄导航", new Vector2(200, 0)))
        {
            io = ImGui.GetIO();
            if (navGamepadEnabled)
            {
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableGamepad;
                GatherBuddy.Log.Information("[VulcanWindow] Disabled ImGui gamepad navigation");
            }
            else
            {
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
                GatherBuddy.Log.Information("[VulcanWindow] Enabled ImGui gamepad navigation");
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button(navKeyboardEnabled ? "禁用键盘导航" : "启用键盘导航", new Vector2(200, 0)))
        {
            io = ImGui.GetIO();
            if (navKeyboardEnabled)
            {
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
                GatherBuddy.Log.Information("[VulcanWindow] Disabled ImGui keyboard navigation");
            }
            else
            {
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                GatherBuddy.Log.Information("[VulcanWindow] Enabled ImGui keyboard navigation");
            }
        }
        
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "提示: 按 Tab 或使用方向键开始导航");
        
        ImGui.EndGroup();
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
            return;

        GatherBuddy.Config.VendorNpcLocationsDataShareFirst = dataShareFirst;
        GatherBuddy.Config.Save();
        GatherBuddy.Log.Debug($"[VulcanWindow] Vendor NPC location source order set to {(dataShareFirst ? "DataShare -> Level -> Supplemental -> LGB" : "LGB -> Level -> Supplemental -> DataShare")}");
        VendorNpcLocationCache.ReloadAsync();
    }

    private static string GetVendorNpcLocationCacheStatus()
    {
        if (VendorNpcLocationCache.IsInitializing)
            return $"重建中 ({VendorNpcLocationCache.ResolvedNpcCount}/{VendorNpcLocationCache.RequestedNpcCount})";
        if (VendorNpcLocationCache.IsInitialized)
            return $"就绪 ({VendorNpcLocationCache.ResolvedNpcCount}/{VendorNpcLocationCache.RequestedNpcCount})";
        if (VendorShopResolver.IsInitializing)
            return "等待商店数据";
        if (!VendorShopResolver.IsInitialized)
            return "商店数据未初始化";
        return "位置缓存未初始化";
    }
}
