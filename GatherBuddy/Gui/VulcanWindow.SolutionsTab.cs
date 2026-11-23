using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Crafting;
using GatherBuddy.RebornUI;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private string _solutionsSearch = string.Empty;
    private string? _selectedSolutionKey = null;
    private List<CachedRaphaelSolution> _solutionsList = [];
    private DateTime _solutionsLastRefresh = DateTime.MinValue;
    private readonly Dictionary<uint, (string Name, uint IconId)> _solutionItemCache = [];

    private void DrawSolutionsPage()
    {
        if ((DateTime.UtcNow - _solutionsLastRefresh).TotalSeconds > 0.5)
        {
            _solutionsList = GatherBuddy.RaphaelSolveCoordinator.GetAllCachedSolutions();
            _solutionsList.Sort(static (a, b) =>
            {
                var order = b.GeneratedAt.CompareTo(a.GeneratedAt);
                return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
            });
            _solutionsLastRefresh = DateTime.UtcNow;
        }

        var coordinator = GatherBuddy.RaphaelSolveCoordinator;
        var raphaelConfig = GatherBuddy.Config.RaphaelSolverConfig;

        M3Widgets.SearchField("##solutionsSearch", "搜索物品", ref _solutionsSearch, VulcanUiScaling.Scaled(240f));
        var rowTop = ImGui.GetItemRectMin().Y;
        var rowHeight = ImGui.GetItemRectSize().Y;

        void SameRow(float height, float gap)
        {
            ImGui.SameLine(0, gap);
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop + (rowHeight - height) * 0.5f));
        }

        SameRow(M3Widgets.ButtonHeight, M3.Space2);
        if (M3Widgets.Button("##stopQueue", "停止队列", M3ButtonStyle.Tonal, FontAwesomeIcon.Stop, enabled: coordinator.PendingSolves > 0,
                tooltip: "取消等待中及进行中的 Raphael 求解，保留已完成的缓存方案。"))
        {
            coordinator.CancelAllPendingSolves();
        }

        SameRow(M3Widgets.ButtonHeight, M3.Space2);
        if (M3Widgets.Button("##clearAll", "全部清空", M3ButtonStyle.Text, FontAwesomeIcon.Trash, enabled: _solutionsList.Count > 0,
                tooltip: "清除所有缓存方案。"))
        {
            coordinator.Clear();
            _selectedSolutionKey = null;
            _solutionsList.Clear();
        }

        SameRow(Ui.LabeledCheckboxHeight, M3.Space2);
        var autoClear = raphaelConfig.AutoClearSolutionCache;
        if (M3Widgets.LabeledCheckbox("队列开始时清除###Clear when a queue starts", ref autoClear,
                tooltip: "每次开始新制作队列时清空求解缓存。\n关闭后，能力值相同时可跨队列复用方案。"))
        {
            raphaelConfig.AutoClearSolutionCache = autoClear;
            GatherBuddy.Config.Save();
        }

        SameRow(ImGui.GetFrameHeight(), M3.Space2);
        M3Badge.Draw($"正在求解 {coordinator.ActiveSolves}", coordinator.ActiveSolves > 0 ? M3.Scheme.Success : M3.Scheme.Outline, "正在进行的求解任务");
        SameRow(ImGui.GetFrameHeight(), M3.Space1);
        M3Badge.Draw($"等待求解 {coordinator.PendingSolves}", coordinator.PendingSolves > 0 ? M3.Scheme.Warning : M3.Scheme.Outline, "排队等待的求解任务");

        ImGui.Dummy(new Vector2(0, M3.Space2));

        var filtered = _solutionsList;
        if (!string.IsNullOrWhiteSpace(_solutionsSearch))
        {
            filtered = [];
            foreach (var solution in _solutionsList)
            {
                if (GetSolutionItemName(solution.Request.RecipeId).Contains(_solutionsSearch, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(solution);
                }
            }
        }

        if (filtered.Count == 0)
        {
            M3Widgets.EmptyState("##noSolutions", _solutionsList.Count == 0 ? "没有缓存方案" : "没有匹配结果",
                _solutionsList.Count == 0
                    ? "使用 Raphael 求解器启动制作队列以生成方案。"
                    : "没有符合搜索条件的方案。", FontAwesomeIcon.Lightbulb);
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        var leftWidth = VulcanUiScaling.Scaled(300f);
        using (var left = M3Panel.Begin("##solLeftPanel", new Vector2(leftWidth, avail.Y), fill: VulcanUiStyle.PanelBackground))
        {
            if (left)
            {
                DrawSolutionsList(filtered);
            }
        }

        ImGui.SameLine(0, M3.Space2);
        using var right = M3Panel.Begin("##solRightPanel", new Vector2(0f, avail.Y));
        if (right)
        {
            DrawSolutionDetail();
        }
    }

    private void DrawSolutionsList(List<CachedRaphaelSolution> solutions)
    {
        var iconSize = VulcanUiScaling.Scaled(28f);
        Ui.ClippedDraw(solutions, solution =>
        {
            var (name, iconId) = GetSolutionItemInfo(solution.Request.RecipeId);
            var req = solution.Request;
            if (Ui.IconListRow($"##sol_{solution.Key}", iconId, name, _selectedSolutionKey == solution.Key, iconSize))
            {
                _selectedSolutionKey = solution.Key;
            }

            M3Tooltip.Hovered($"{name}\n等级 {req.Level} · {solution.ActionIds.Count} 工次\n作业精度 {req.Craftsmanship} · 加工精度 {req.Control} · 制作力 {req.CP}");
        }, iconSize + M3.Space2 + ImGui.GetStyle().ItemSpacing.Y);
    }
    private void DrawSolutionDetail()
    {
        if (_selectedSolutionKey == null)
        {
            M3Widgets.EmptyState("##noSolution", "未选择方案", "在左侧选择方案查看制作步骤。", FontAwesomeIcon.Lightbulb);
            return;
        }

        var solution = _solutionsList.Find(s => s.Key == _selectedSolutionKey);
        if (solution == null)
        {
            _selectedSolutionKey = null;
            return;
        }

        var req = solution.Request;
        var (name, iconId) = GetSolutionItemInfo(req.RecipeId);
        var largeIconSize = VulcanUiScaling.Scaled(48f);
        var start = ImGui.GetCursorScreenPos();
        if (iconId > 0)
        {
            M3Widgets.GameIcon(iconId, largeIconSize);
            ImGui.SameLine(0, M3.Space3);
        }

        using (ImRaii.Group())
        {
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y + (largeIconSize - ImGui.GetTextLineHeightWithSpacing() * 2f) * 0.5f));
            using (ImRaii.PushFont(M3.TitleMedium))
            {
                ImGui.TextUnformatted(name);
            }

            M3Text.Draw($"配方 ID {req.RecipeId} · 求解时间 {solution.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm}", M3TextStyle.Muted);
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        using (M3Card.Begin("##solveParameters", "求解参数", FontAwesomeIcon.SlidersH, M3.AccentFor(0)))
        {
            M3Widgets.RowValue("等级", req.Level.ToString());
            M3Widgets.RowValue("作业精度", req.Craftsmanship.ToString());
            M3Widgets.RowValue("加工精度", req.Control.ToString());
            M3Widgets.RowValue("制作力", req.CP.ToString());
            M3Widgets.RowValue("掌握", req.Manipulation ? "是" : "否");
            M3Widgets.RowValue("专家", req.Specialist ? "是" : "否");
            M3Widgets.RowValue("初期品质", req.InitialQuality.ToString());
        }

        M3Widgets.SectionLabel($"技能 ({solution.ActionIds.Count})");
        var actionIconSize = VulcanUiScaling.Scaled(24f);
        using var actions = ImRaii.Child("##solActions", new Vector2(-1, ImGui.GetContentRegionAvail().Y), false);
        for (var i = 0; i < solution.ActionIds.Count; i++)
        {
            var actionId = solution.ActionIds[i];
            var skillIconId = GetSkillIconId(actionId);
            if (skillIconId > 0)
            {
                M3Widgets.GameIcon(skillIconId, actionIconSize);
            }
            else
            {
                ImGui.Dummy(new Vector2(actionIconSize));
            }

            ImGui.SameLine(0, M3.Space2);
            Ui.RowText($"{i + 1}.", actionIconSize, M3TextStyle.Faint);
            ImGui.SameLine(0, M3.Space1);
            Ui.RowText(CraftSkillDisplayName((VulcanSkill)actionId), actionIconSize);
        }
    }
    private (string Name, uint IconId) GetSolutionItemInfo(uint recipeId)
    {
        if (_solutionItemCache.TryGetValue(recipeId, out var cached))
        {
            return cached;
        }

        try
        {
            var recipe = RecipeManager.GetRecipe(recipeId);
            if (recipe.HasValue)
            {
                var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
                if (itemSheet != null && itemSheet.TryGetRow(recipe.Value.ItemResult.RowId, out var item))
                {
                    var info = (item.Name.ExtractText(), (uint)item.Icon);
                    _solutionItemCache[recipeId] = info;
                    return info;
                }
            }
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Debug($"[SolutionsTab] Failed to get item info for recipe {recipeId}: {ex.Message}");
        }

        var fallback = ($"配方 {recipeId}", 0u);
        _solutionItemCache[recipeId] = fallback;
        return fallback;
    }

    private string GetSolutionItemName(uint recipeId)
        => GetSolutionItemInfo(recipeId).Name;
}
