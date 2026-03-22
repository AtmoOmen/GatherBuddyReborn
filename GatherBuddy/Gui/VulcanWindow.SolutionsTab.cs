using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using ElliLib.Raii;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private string _solutionsSearch = string.Empty;
    private string? _selectedSolutionKey = null;
    private List<CachedRaphaelSolution> _solutionsList = new();
    private DateTime _solutionsLastRefresh = DateTime.MinValue;
    private readonly Dictionary<uint, (string Name, uint IconId)> _solutionItemCache = new();

    private void DrawSolutionsTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("解法缓存##solutionsTab", 4, 7); // Solutions | 解法缓存?
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("解法缓存##solutionsTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }

        using (tabItem)
        {
            if (!tabOpen)
                return;

            if ((DateTime.UtcNow - _solutionsLastRefresh).TotalSeconds > 0.5)
            {
                _solutionsList = GatherBuddy.RaphaelSolveCoordinator
                    .GetAllCachedSolutions()
                    .OrderByDescending(s => s.GeneratedAt)
                    .ToList();
                _solutionsLastRefresh = DateTime.UtcNow;
            }

            var coordinator = GatherBuddy.RaphaelSolveCoordinator;
            var raphaelConfig = GatherBuddy.Config.RaphaelSolverConfig;

            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##solutionsSearch", "搜索物品...", ref _solutionsSearch, 128);
            ImGui.SameLine();
            if (ImGui.Button("全部清空", new Vector2(90, 0)))
            {
                coordinator.Clear();
                _selectedSolutionKey = null;
                _solutionsList.Clear();
            }
            ImGui.SameLine();
            var autoClear = raphaelConfig.AutoClearSolutionCache;
            if (ImGui.Checkbox("队列开始时自动清空", ref autoClear))
            {
                raphaelConfig.AutoClearSolutionCache = autoClear;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每次在制作队列开始时, 清空 Raphael 求解器缓存的资源\n关闭此选项可在属性未变化时跨队列复用解法宏。");

            var activeColor = coordinator.ActiveSolves > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            var pendingColor = coordinator.PendingSolves > 0 ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey;
            ImGui.SameLine();
            ImGui.TextColored(activeColor, $"进行中: {coordinator.ActiveSolves}");
            ImGui.SameLine();
            ImGui.TextColored(pendingColor, $"等待中: {coordinator.PendingSolves}");

            ImGui.Separator();
            ImGui.Spacing();

            var filtered = string.IsNullOrWhiteSpace(_solutionsSearch)
                ? _solutionsList
                : _solutionsList
                    .Where(s => GetSolutionItemName(s.Request.RecipeId)
                        .Contains(_solutionsSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (filtered.Count == 0)
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey, _solutionsList.Count == 0
                    ? "没有缓存的解法宏, 使用 Raphael 求解器启动队列以生成解法宏。"
                    : "没有匹配搜索条件的解法宏。");
                return;
            }

            var avail = ImGui.GetContentRegionAvail();
            var leftWidth = 290f;
            var rightWidth = avail.X - leftWidth - ImGui.GetStyle().ItemSpacing.X;

            using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
            {
                ImGui.BeginChild("##solLeftPanel", new Vector2(leftWidth, avail.Y), true);
                DrawSolutionsList(filtered);
                ImGui.EndChild();
            }

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
            {
                ImGui.BeginChild("##solRightPanel", new Vector2(rightWidth, avail.Y), true);
                DrawSolutionDetail();
                ImGui.EndChild();
            }
        }
    }

    private void DrawSolutionsList(List<CachedRaphaelSolution> solutions)
    {
        var iconSize = new Vector2(28f, 28f);
        var itemHeight = iconSize.Y + ImGui.GetStyle().ItemSpacing.Y;
        var contentMaxX = ImGui.GetContentRegionMax().X;

        ElliLib.ImGuiClip.ClippedDraw(solutions, solution =>
        {
            var (name, iconId) = GetSolutionItemInfo(solution.Request.RecipeId);
            var isSelected = _selectedSolutionKey == solution.Key;
            var req = solution.Request;
            var statsLine = $"等级 {req.Level}  {req.Craftsmanship}/{req.Control}/{req.CP}  共 {solution.ActionIds.Count} 步";

            if (iconId > 0)
            {
                var wrap = Icons.DefaultStorage.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(iconId))
                    .GetWrapOrDefault();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, iconSize);
                else
                    ImGui.Dummy(iconSize);
            }
            else
                ImGui.Dummy(iconSize);

            ImGui.SameLine(0, 6);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (iconSize.Y - ImGui.GetTextLineHeight()) / 2f);

            if (ImGui.Selectable($"{name}##sol_{solution.Key}", isSelected, ImGuiSelectableFlags.None,
                    new Vector2(contentMaxX - ImGui.GetCursorPosX(), 0)))
                _selectedSolutionKey = solution.Key;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(statsLine);
        }, itemHeight);
    }

    private void DrawSolutionDetail()
    {
        if (_selectedSolutionKey == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "选择一个解法宏查看详情。");
            return;
        }

        var solution = _solutionsList.FirstOrDefault(s => s.Key == _selectedSolutionKey);
        if (solution == null)
        {
            _selectedSolutionKey = null;
            return;
        }

        var req = solution.Request;
        var (name, iconId) = GetSolutionItemInfo(req.RecipeId);
        var largeIconSize = new Vector2(48f, 48f);

        if (iconId > 0)
        {
            var wrap = Icons.DefaultStorage.TextureProvider
                .GetFromGameIcon(new GameIconLookup(iconId))
                .GetWrapOrDefault();
            if (wrap != null)
            {
                ImGui.Image(wrap.Handle, largeIconSize);
                ImGui.SameLine(0, 10);
            }
        }

        var lineH = ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (largeIconSize.Y - lineH * 2f) / 2f);
        ImGui.TextColored(ImGuiColors.ParsedGold, name);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"配方 ID {req.RecipeId}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.DalamudYellow, "解法参数");
        ImGui.Spacing();

        ImGui.Text($"等级:          {req.Level}");
        ImGui.Text($"作业精度:  {req.Craftsmanship}");
        ImGui.SameLine(180);
        ImGui.Text($"加工精度:  {req.Control}");
        ImGui.SameLine(340);
        ImGui.Text($"制作力:  {req.CP}");
        ImGui.Text($"掌握:   {(req.Manipulation ? "是" : "否")}");
        ImGui.SameLine(180);
        ImGui.Text($"专家:  {(req.Specialist ? "是" : "否")}");
        ImGui.Text($"初期品质: {req.InitialQuality}");
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudGrey, $"生成时间: {solution.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, $"技能 ({solution.ActionIds.Count})");
        ImGui.Spacing();

        var actionIconSize = new Vector2(24f, 24f);
        var remainH = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##solActions", new Vector2(-1, remainH), false);

        for (var i = 0; i < solution.ActionIds.Count; i++)
        {
            var actionId = solution.ActionIds[i];
            var skillName = ((VulcanSkill)actionId).ToString();

            // 使用字典映射中文技能名
            var skillEnum = (VulcanSkill)actionId;
            var skillNameZh = VulcanSkillNamesZh.TryGetValue(skillEnum, out var zhName)
                ? zhName
                : skillEnum.ToString();

            var skillIconId = GetSkillIconId(actionId);

            if (skillIconId > 0)
            {
                var wrap = Icons.DefaultStorage.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(skillIconId))
                    .GetWrapOrDefault();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, actionIconSize);
                else
                    ImGui.Dummy(actionIconSize);
            }
            else
            {
                ImGui.Dummy(actionIconSize);
            }

            ImGui.SameLine(0, 6);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (actionIconSize.Y - ImGui.GetTextLineHeight()) / 2f);
            ImGui.Text($"{i + 1}. {skillNameZh}");
        }

        ImGui.EndChild();
    }

    private (string Name, uint IconId) GetSolutionItemInfo(uint recipeId)
    {
        if (_solutionItemCache.TryGetValue(recipeId, out var cached))
            return cached;

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

        var fallback = ($"配方 ID {recipeId}", 0u);
        _solutionItemCache[recipeId] = fallback;
        return fallback;
    }

    private string GetSolutionItemName(uint recipeId)
        => GetSolutionItemInfo(recipeId).Name;
}
