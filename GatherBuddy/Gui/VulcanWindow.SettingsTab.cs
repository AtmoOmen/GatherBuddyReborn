using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Crafting;
using GatherBuddy.RebornUI.Settings;
using RebornMaterial;
using System;
using System.Numerics;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private static readonly string[] SolverModeNames = ["仅使用 Raphael", "标准求解器", "仅完成制作"];

    private static readonly string[] SolverModeTooltips =
    [
        "执行 Raphael 预先生成的固定制作流程。\n相同条件下按同一顺序使用技能。",
        "使用改编自 Artisan 的动态求解器。\n根据每一工次的制作状态选择技能。",
        "以尽快完成制作为目标，跳过提升品质的技能。",
    ];

    private static readonly VulcanSolverMode[] SolverModes = [VulcanSolverMode.PureRaphael, VulcanSolverMode.StandardSolver, VulcanSolverMode.ProgressOnly];

    private void DrawSettingsPage()
    {
        using (M3Card.Begin("vulcan_settings_crafting", "制作", FontAwesomeIcon.Hammer, M3.AccentFor(0)))
        {
            DrawSolverMode();
            SettingRows.Slider("技能执行间隔", "两次制作技能之间的等待时间，单位为毫秒。设为 0 时立即执行，最大为 1000。",
                GatherBuddy.Config.VulcanExecutionDelayMs, 0, 1000, v => GatherBuddy.Config.VulcanExecutionDelayMs = v);
            SettingRows.Switch("右键菜单选项",
                "在游戏的右键菜单中显示「在 Vulcan 中打开」「添加到制作清单」和「添加到商店购买清单」。",
                GatherBuddy.Config.VulcanContextMenuEntries, v => GatherBuddy.Config.VulcanContextMenuEntries = v);
            SettingRows.Switch("显示配方产物的物品说明", "在配方页将鼠标悬停于配方产物上时，显示游戏内的物品说明。",
                GatherBuddy.Config.ShowRecipeBrowserTooltips, v => GatherBuddy.Config.ShowRecipeBrowserTooltips = v);
            SettingRows.Hotkey("打开配方页的快捷键", "打开 Vulcan 并直接进入配方页。",
                GatherBuddy.Config.VulcanRecipesTabHotkey, v => GatherBuddy.Config.VulcanRecipesTabHotkey = v);
        }

        using (M3Card.Begin("vulcan_settings_repair", "修理", FontAwesomeIcon.Wrench, M3.AccentFor(1)))
        {
            DrawVulcanRepairConfig();
        }

        using (M3Card.Begin("vulcan_settings_between", "制作间歇", FontAwesomeIcon.Gem, M3.AccentFor(2)))
        {
            var materia = GatherBuddy.Config.VulcanMateriaConfig;
            SettingRows.Switch("启用魔晶石精制", "在两次制作之间，对精炼度达到 100% 的装备精制魔晶石。",
                materia.Enabled, v => materia.Enabled = v);

            var retainerBell = GatherBuddy.Config.VulcanRetainerBellConfig;
            SettingRows.Switch("自动前往传唤铃", "开始从雇员取回物品时，自动前往最近的传唤铃。",
                retainerBell.AutoNavigateToRetainerBell, v => retainerBell.AutoNavigateToRetainerBell = v);
        }

        using (M3Card.Begin("vulcan_settings_raphael", "Raphael 求解器", FontAwesomeIcon.Brain, M3.AccentFor(3)))
        {
            DrawRaphaelConfig();
        }
    }

    private static void DrawSolverMode()
    {
        var raphaelConfig = GatherBuddy.Config.RaphaelSolverConfig;
        var index = Math.Max(0, Array.IndexOf(SolverModes, raphaelConfig.SolverMode));
        var width = MathF.Max(M3Widgets.ComboWidthFor(SolverModeNames[1]), 160f * M3.Scale);
        var row = M3SettingRow.Begin("求解模式", "Vulcan 选择制作技能的方式。", new Vector2(width, M3Widgets.ComboHeight));
        ImGui.SetCursorScreenPos(row.ControlPosition);
        var changed = M3Widgets.Combo("##vulcan_solver_mode", ref index, SolverModeNames.Length, i => SolverModeNames[i], width,
            itemTooltip: i => SolverModeTooltips[i]);
        M3SettingRow.End(row);

        if (!changed || index < 0 || index >= SolverModes.Length || SolverModes[index] == raphaelConfig.SolverMode)
        {
            return;
        }

        raphaelConfig.SolverMode = SolverModes[index];
        GatherBuddy.Config.Save();
        CraftingGameInterop.ReloadSolvers();
    }

    private static void DrawVulcanRepairConfig()
    {
        var config = GatherBuddy.Config.VulcanRepairConfig;
        SettingRows.Switch("启用修理", "在两次制作之间按需修理装备。", config.Enabled, v => config.Enabled = v);
        SettingRows.Slider("修理阈值", "任一装备的耐久度低于此百分比时进行修理。",
            config.RepairThreshold, 0, 99, v => config.RepairThreshold = v);
        SettingRows.Switch("优先找修理工修理", "有可用的修理工且金币足够时，优先委托修理工，否则自行修理。",
            config.PrioritizeNPCRepair, v => config.PrioritizeNPCRepair = v);

        if (!config.PrioritizeNPCRepair)
        {
            return;
        }

        using var sub = M3SubGroup.Begin();
        var npcs = RepairNPCHelper.RepairNPCs;
        var current = config.PreferredRepairNPC;
        var index = 0;
        if (current != null)
        {
            var found = npcs.FindIndex(n => n.DataId == current.DataId);
            index = found >= 0 ? found + 1 : 0;
        }

        var supporting = npcs.Count == 0
            ? "正在加载修理工列表。"
            : "选择需要修理时前往的修理工。选「当前区域的修理工」时，可使用附近任意修理工。";
        if (!M3Widgets.RowCombo("首选修理工", ref index, npcs.Count + 1,
                i => i == 0 ? "当前区域的修理工" : $"{npcs[i - 1].Name} - {GetTerritoryName(npcs[i - 1].TerritoryType)}",
                supporting, search: true))
        {
            return;
        }

        if (index <= 0 || index > npcs.Count)
        {
            config.PreferredRepairNPC = null;
            config.PreferredRepairNPCDataId = 0;
        }
        else
        {
            var npc = npcs[index - 1];
            config.PreferredRepairNPC = npc;
            config.PreferredRepairNPCDataId = npc.DataId;
        }

        GatherBuddy.Config.Save();
    }

    private static void DrawRaphaelConfig()
    {
        var coordinator = GatherBuddy.RaphaelSolveCoordinator;
        var raphaelConfig = GatherBuddy.Config.RaphaelSolverConfig;

        SettingRows.Number("最大并行求解数", "允许同时运行的 Raphael 求解任务数。",
            raphaelConfig.MaxConcurrentRaphaelProcesses, 1, 1000, v => raphaelConfig.MaxConcurrentRaphaelProcesses = v);
        SettingRows.Number("求解超时",
            "每项 Raphael 求解最多允许的分钟数。超时会记为求解失败，并跳过这项制作。",
            raphaelConfig.RaphaelTimeoutMinutes, 1, 60, v => raphaelConfig.RaphaelTimeoutMinutes = v, 1, "分钟");
        SettingRows.Number("缓存保留天数", "插件加载时清理超过保留天数的求解方案。",
            raphaelConfig.SolutionCacheMaxAgeDays, 1, 365, v => raphaelConfig.SolutionCacheMaxAgeDays = v, 1, "天");

        if (SettingRows.Switch("最后推进进展",
                "仅在制作流程末尾使用推进进展的技能。若希望尽量提高可达品质，可关闭此项。 "
              + "更改此项会清空求解缓存。",
                raphaelConfig.RaphaelBackloadProgress, v => raphaelConfig.RaphaelBackloadProgress = v))
        {
            coordinator.Clear();
        }

        if (SettingRows.Switch("允许使用专家技能",
                "默认关闭。关闭时，即使当前职业已设为专家，Raphael 也只生成无需专家技能的制作流程。 "
              + "更改此项会清空求解缓存。",
                raphaelConfig.RaphaelAllowSpecialistActions, v => raphaelConfig.RaphaelAllowSpecialistActions = v))
        {
            coordinator.Clear();
        }

        M3Widgets.SectionLabel("状态");
        var muted = M3.Scheme.OnSurfaceVariant;
        M3Widgets.RowValue("正在求解", $"{coordinator.ActiveSolves} / {raphaelConfig.MaxConcurrentRaphaelProcesses}", null,
            coordinator.ActiveSolves > 0 ? M3.Severity(M3Severity.Success) : muted);
        M3Widgets.RowValue("等待求解", coordinator.PendingSolves.ToString(), null,
            coordinator.PendingSolves > 0 ? M3.Severity(M3Severity.Warning) : muted);
        M3Widgets.RowValue("已缓存方案", coordinator.CachedSolutionCount.ToString(), null,
            coordinator.CachedSolutionCount > 0 ? M3.Severity(M3Severity.Success) : muted);

        if (M3Widgets.RowButton("清理求解缓存", "清空缓存", "删除所有已保存的 Raphael 求解方案。", M3ButtonStyle.Outlined,
                FontAwesomeIcon.Trash))
        {
            coordinator.Clear();
            M3Snackbar.Show("已清空 Raphael 求解缓存。");
        }
    }
}
