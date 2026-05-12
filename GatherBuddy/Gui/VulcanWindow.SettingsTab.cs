using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ElliLib.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private void DrawSettingsTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null)
        {
        var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("设置##settingsTab", 6, 9);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("设置##settingsTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }

        using (tabItem)
        {
            if (!tabOpen)
                return;

            var coordinator = GatherBuddy.RaphaelSolveCoordinator;
            var raphaelConfig = GatherBuddy.Config.RaphaelSolverConfig;

            var currentMode = raphaelConfig.SolverMode;
            var modeNames = new[] { "纯 Raphael", "标准求解器", "仅推进度" };
            var safeModeIndex = Math.Clamp((int)currentMode, 0, modeNames.Length - 1);
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("求解器模式###SolverMode", modeNames[safeModeIndex]))
            {
                if (ImGui.Selectable("纯 Raphael", currentMode == RaphaelSolverMode.PureRaphael))
                {
                    raphaelConfig.SolverMode = RaphaelSolverMode.PureRaphael;
                    GatherBuddy.Config.Save();
                    CraftingGameInterop.ReloadSolvers();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("纯 Raphael: 使用 Raphael 求解器生成固定循环");
                    ImGui.TextUnformatted("结果稳定且较优");
                    ImGui.EndTooltip();
                }

                if (ImGui.Selectable("标准求解器", currentMode == RaphaelSolverMode.StandardSolver))
                {
                    raphaelConfig.SolverMode = RaphaelSolverMode.StandardSolver;
                    GatherBuddy.Config.Save();
                    CraftingGameInterop.ReloadSolvers();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("标准求解器: 基于 Artisan 改造的动态求解器");
                    ImGui.TextUnformatted("会响应状态, 更灵活");
                    ImGui.EndTooltip();
                }

                if (ImGui.Selectable("仅推进度", currentMode == RaphaelSolverMode.ProgressOnly))
                {
                    raphaelConfig.SolverMode = RaphaelSolverMode.ProgressOnly;
                    GatherBuddy.Config.Save();
                    CraftingGameInterop.ReloadSolvers();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("仅推进度: 不使用品质技能完成制作");
                    ImGui.TextUnformatted("执行最快, 不提升品质");
                    ImGui.EndTooltip();
                }
                ImGui.EndCombo();
            }

            var delay = GatherBuddy.Config.VulcanExecutionDelayMs;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("操作延迟 (ms)", ref delay, 0, 1000))
            {
                GatherBuddy.Config.VulcanExecutionDelayMs = Math.Clamp(delay, 0, 1000);
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每个制作操作之间的延迟毫秒数 (0 = 立即, 最大 1000 ms)");

            var ctxMenuEntries = GatherBuddy.Config.VulcanContextMenuEntries;
            if (ImGui.Checkbox("启用右键菜单入口", ref ctxMenuEntries))
            {
                GatherBuddy.Config.VulcanContextMenuEntries = ctxMenuEntries;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("在游戏内右键菜单中显示 Vulcan 相关入口\n包括“在 Vulcan 中打开”“加入制作清单”“加入商店购买清单”");

            DrawVulcanRepairConfig();

            DrawVulcanMateriaConfig();

            DrawVulcanRetainerBellConfig();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Raphael 求解器");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginGroup();
            ImGui.Text("  最大并发: ");
            ImGui.SameLine();
            var maxConcurrent = raphaelConfig.MaxConcurrentRaphaelProcesses;
            if (ImGui.InputInt("###MaxConcurrent", ref maxConcurrent, 1, 1))
            {
                raphaelConfig.MaxConcurrentRaphaelProcesses = Math.Max(1, maxConcurrent);
                GatherBuddy.Config.Save();
            }

            ImGui.Text("  求解超时 (分钟): ");
            ImGui.SameLine();
            var timeoutMinutes = raphaelConfig.RaphaelTimeoutMinutes;
            if (ImGui.InputInt("###RaphaelTimeout", ref timeoutMinutes, 1, 1))
            {
                raphaelConfig.RaphaelTimeoutMinutes = Math.Max(1, Math.Min(60, timeoutMinutes));
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每次 Raphael 求解的超时时间, 超时后方案会标记为失败并跳过制作");

            ImGui.Text("  缓存最长保留 (天): ");
            ImGui.SameLine();
            var maxAgeDays = raphaelConfig.SolutionCacheMaxAgeDays;
            if (ImGui.InputInt("###CacheMaxAge", ref maxAgeDays, 1, 10))
            {
                raphaelConfig.SolutionCacheMaxAgeDays = Math.Max(1, Math.Min(365, maxAgeDays));
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("插件加载时丢弃超过指定天数的方案");

            ImGui.Spacing();
            var backloadProgress = raphaelConfig.RaphaelBackloadProgress;
            if (ImGui.Checkbox("  末段补进度###RaphaelBackloadProgress", ref backloadProgress))
            {
                raphaelConfig.RaphaelBackloadProgress = backloadProgress;
                GatherBuddy.Config.Save();
                coordinator.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("仅在循环末段使用推进度技能\n可能降低可达品质, 追求最高品质时关闭\n修改此项会清空方案缓存");

            var allowSpecialist = raphaelConfig.RaphaelAllowSpecialistActions;
            if (ImGui.Checkbox("  允许专家技能###RaphaelAllowSpecialist", ref allowSpecialist))
            {
                raphaelConfig.RaphaelAllowSpecialistActions = allowSpecialist;
                GatherBuddy.Config.Save();
                coordinator.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("关闭时, 即使当前为专家, Raphael 也会生成非专家循环\n仅在需要 Raphael 使用专家技能时启用\n修改此项会清空方案缓存");

            var activeColor = coordinator.ActiveSolves > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            ImGui.TextColored(activeColor, $"  活动求解: {coordinator.ActiveSolves}/{raphaelConfig.MaxConcurrentRaphaelProcesses}");

            var pendingColor = coordinator.PendingSolves > 0 ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey;
            ImGui.TextColored(pendingColor, $"  等待中: {coordinator.PendingSolves}");

            var cachedColor = coordinator.CachedSolutionCount > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            ImGui.TextColored(cachedColor, $"  已缓存方案: {coordinator.CachedSolutionCount}");

            if (ImGui.Button("清空缓存", new Vector2(150, 0)))
            {
                coordinator.Clear();
            }
            ImGui.EndGroup();
        }
    }

    private void DrawVulcanRepairConfig()
    {
        var config = GatherBuddy.Config.VulcanRepairConfig;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用修理", ref enabled))
        {
            config.Enabled = enabled;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("需要时在制作间自动修理装备");

        var threshold = config.RepairThreshold;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("修理阈值 (%)", ref threshold, 0, 99))
        {
            config.RepairThreshold = threshold;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("最低装备耐久低于此百分比时修理");

        var prioritizeNPC = config.PrioritizeNPCRepair;
        if (ImGui.Checkbox("优先 NPC 修理", ref prioritizeNPC))
        {
            config.PrioritizeNPCRepair = prioritizeNPC;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("有可用 NPC 且金币足够时使用 NPC 修理, 否则自行修理");
        
        if (config.PrioritizeNPCRepair)
        {
            ImGui.Spacing();
            ImGui.Text("偏好修理 NPC:");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("选择需要修理时前往的修理 NPC");
            
            ImGui.SetNextItemWidth(300);
            var currentNPC = config.PreferredRepairNPC;
            var displayText = currentNPC != null 
                ? $"{currentNPC.Name} ({GetTerritoryName(currentNPC.TerritoryType)})"
                : "当前区域 NPC";
            
            if (ImGui.BeginCombo("##PreferredRepairNPC", displayText))
            {
                ImGui.SetNextItemWidth(280);
                ImGui.InputTextWithHint("##RepairNPCSearch", "搜索 NPC...", ref _repairNPCSearchInput, 256);
                ImGui.Separator();
                
                if (ImGui.Selectable("当前区域 NPC", currentNPC == null))
                {
                    config.PreferredRepairNPC = null;
                    config.PreferredRepairNPCDataId = 0;
                    GatherBuddy.Config.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("使用当前区域任意修理 NPC");
                
                var repairNPCs = Crafting.RepairNPCHelper.RepairNPCs;
                if (repairNPCs.Count == 0)
                {
                    ImGui.TextDisabled("尚未加载修理 NPC...");
                }
                else
                {
                    var searchLower = _repairNPCSearchInput.ToLowerInvariant();
                    var filteredNPCs = string.IsNullOrWhiteSpace(_repairNPCSearchInput)
                        ? repairNPCs
                        : repairNPCs.Where(npc => 
                            npc.Name.ToLowerInvariant().Contains(searchLower) ||
                            GetTerritoryName(npc.TerritoryType).ToLowerInvariant().Contains(searchLower)).ToList();
                    
                    if (filteredNPCs.Count == 0)
                    {
                        ImGui.TextDisabled("没有匹配搜索的 NPC...");
                    }
                    else
                    {
                        foreach (var npc in filteredNPCs)
                        {
                            var territoryName = GetTerritoryName(npc.TerritoryType);
                            var npcLabel = $"{npc.Name} - {territoryName}";
                            
                            if (ImGui.Selectable(npcLabel, currentNPC?.DataId == npc.DataId))
                            {
                                config.PreferredRepairNPC = npc;
                                config.PreferredRepairNPCDataId = npc.DataId;
                                GatherBuddy.Config.Save();
                            }
                        }
                    }
                }
                
                ImGui.EndCombo();
            }
        }
    }
    
    private void DrawVulcanMateriaConfig()
    {
        var config = GatherBuddy.Config.VulcanMateriaConfig;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用魔晶石精制", ref enabled))
        {
            config.Enabled = enabled;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("在制作间从精炼度满的装备中自动精制魔晶石");
    }

    private void DrawVulcanRetainerBellConfig()
    {
        var config = GatherBuddy.Config.VulcanRetainerBellConfig;

        var autoNav = config.AutoNavigateToRetainerBell;
        if (ImGui.Checkbox("自动前往传唤铃", ref autoNav))
        {
            config.AutoNavigateToRetainerBell = autoNav;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("开始雇员补货时自动前往最近的传唤铃");
    }
}
