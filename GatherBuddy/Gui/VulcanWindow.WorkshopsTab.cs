using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using ElliLib.Raii;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan.Workshops;
using Lumina.Excel.Sheets;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private const string WorkshopAddToListPopupId = "WorkshopAddToExistingListPopup";
    private string _workshopProjectSearch = string.Empty;
    private WorkshopProjectNode? _selectedWorkshopProject;
    private WorkshopScopeNode? _selectedWorkshopScope;
    private int _workshopLoopCount = 1;
    private bool _workshopEphemeral = false;
    private bool _openWorkshopAddToListPopup = false;
    private string _workshopExistingListSearch = string.Empty;
    private string _workshopListName = string.Empty;
    private WorkshopScopeNode? _workshopListNameScope = null;
    private int _workshopListNameLoopCount = 0;

    private void DrawWorkshopsTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("工房##workshopsTab", 2, 9);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("工房##workshopsTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }

        using (tabItem)
        {
            if (!tabOpen)
                return;

            DrawWorkshopsTabContent();
        }
    }

    private void DrawWorkshopsTabContent()
    {
        if (_openWorkshopAddToListPopup)
        {
            ImGui.OpenPopup(WorkshopAddToListPopupId);
            _openWorkshopAddToListPopup = false;
        }
        var projects = WorkshopDataService.GetProjects();
        EnsureWorkshopSelection(projects);

        ImGui.Spacing();
        var avail = ImGui.GetContentRegionAvail();
        const float leftWidth = 220f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var remainingWidth = Math.Max(0f, avail.X - leftWidth - spacing * 2f);
        var middleWidth = remainingWidth * 0.40f;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##WorkshopProjectsPanel", new Vector2(leftWidth, avail.Y), true);
            DrawWorkshopProjectsPanel(projects);
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##WorkshopScopesPanel", new Vector2(middleWidth, avail.Y), true);
            DrawWorkshopScopesPanel();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##WorkshopDetailsPanel", new Vector2(0, avail.Y), true);
            DrawWorkshopDetailsPanel();
            ImGui.EndChild();
        }

        DrawWorkshopAddToListPopup();
    }

    private void EnsureWorkshopSelection(IReadOnlyList<WorkshopProjectNode> projects)
    {
        if (_selectedWorkshopProject != null && !projects.Any(project => project.SequenceId == _selectedWorkshopProject.SequenceId))
            _selectedWorkshopProject = null;

        _selectedWorkshopProject ??= projects.FirstOrDefault();

        if (_selectedWorkshopProject == null)
        {
            _selectedWorkshopScope = null;
            return;
        }

        if (_selectedWorkshopScope == null
         || _selectedWorkshopScope.ProjectId != _selectedWorkshopProject.ProjectId
         || !ScopeExists(_selectedWorkshopProject, _selectedWorkshopScope))
        {
            _selectedWorkshopScope = _selectedWorkshopProject;
        }

        if (_workshopLoopCount < 1)
            _workshopLoopCount = 1;
    }

    private static bool ScopeExists(WorkshopProjectNode project, WorkshopScopeNode scope)
        => scope switch
        {
            WorkshopProjectNode projectScope => projectScope.SequenceId == project.SequenceId,
            WorkshopPartNode partScope => project.Parts.Any(part => part.PartId == partScope.PartId),
            WorkshopPhaseNode phaseScope => project.Parts.SelectMany(part => part.Phases).Any(phase => phase.PhaseId == phaseScope.PhaseId),
            _ => false,
        };

    private void DrawWorkshopProjectsPanel(IReadOnlyList<WorkshopProjectNode> projects)
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##workshopProjectSearch", "搜索工程...", ref _workshopProjectSearch, 256);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var filteredProjects = string.IsNullOrWhiteSpace(_workshopProjectSearch)
            ? projects
            : projects
                .Where(project => project.ProjectName.Contains(_workshopProjectSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"{filteredProjects.Count} 个工程");
        ImGui.Spacing();

        ImGui.BeginChild("##WorkshopProjectsList", new Vector2(-1, 0), false);
        if (filteredProjects.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "没有匹配搜索的工房工程");
            ImGui.EndChild();
            return;
        }

        var iconSize = new Vector2(28f, 28f);
        var maxX = ImGui.GetContentRegionMax().X;
        foreach (var project in filteredProjects)
        {
            var isSelected = _selectedWorkshopProject?.SequenceId == project.SequenceId;
            var rowY = ImGui.GetCursorPosY();
            var icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(project.ProjectIconId));
            if (icon.TryGetWrap(out var wrap, out _))
                ImGui.Image(wrap.Handle, iconSize);
            else
                ImGui.Dummy(iconSize);

            ImGui.SameLine(0, 6f);
            ImGui.SetCursorPosY(rowY + (iconSize.Y - ImGui.GetTextLineHeight()) / 2f);
            if (ImGui.Selectable(
                    $"{project.ProjectName}##workshopProject_{project.SequenceId}",
                    isSelected,
                    ImGuiSelectableFlags.None,
                    new Vector2(maxX - ImGui.GetCursorPosX(), 0)))
            {
                _selectedWorkshopProject = project;
                _selectedWorkshopScope = project;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(project.ProjectName);
                ImGui.TextColored(ImGuiColors.DalamudGrey3, $"{project.Parts.Count} 个部件 · {project.PhaseCount} 个阶段");
                ImGui.EndTooltip();
            }
        }
        ImGui.EndChild();
    }

    private void DrawWorkshopScopesPanel()
    {
        if (_selectedWorkshopProject == null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "选择工房工程以浏览范围");
            return;
        }

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, "工程范围");
        ImGui.TextColored(ImGuiColors.DalamudGrey3, _selectedWorkshopProject.ProjectName);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var overviewSelected = _selectedWorkshopScope is WorkshopProjectNode selectedProject
            && selectedProject.SequenceId == _selectedWorkshopProject.SequenceId;
        if (ImGui.Selectable($"总览##workshopOverview_{_selectedWorkshopProject.SequenceId}", overviewSelected))
            _selectedWorkshopScope = _selectedWorkshopProject;

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("显示完整工程需求, 并为整个工房工程生成清单");

        ImGui.Spacing();

        foreach (var part in _selectedWorkshopProject.Parts)
        {
            var isSelected = _selectedWorkshopScope is WorkshopPartNode selectedPart && selectedPart.PartId == part.PartId;
            var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen;
            if (isSelected)
                flags |= ImGuiTreeNodeFlags.Selected;
            if (part.Phases.Count == 0)
                flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

            var open = ImGui.TreeNodeEx($"{part.DisplayName}##workshopPart_{part.PartId}", flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                _selectedWorkshopScope = part;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{part.Phases.Count} 个阶段 · {part.CraftableRequirementCount} 个有配方的补给品");

            if (!open || part.Phases.Count == 0)
                continue;

            foreach (var phase in part.Phases)
            {
                var phaseSelected = _selectedWorkshopScope is WorkshopPhaseNode selectedPhase && selectedPhase.PhaseId == phase.PhaseId;
                if (ImGui.Selectable($"阶段 {phase.PhaseIndex}##workshopPhase_{phase.PhaseId}", phaseSelected))
                    _selectedWorkshopScope = phase;

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"{phase.CraftableRequirementCount} 个有配方的补给品");
            }

            ImGui.TreePop();
        }
    }

    private void DrawWorkshopDetailsPanel()
    {
        if (_selectedWorkshopScope == null)
        {
            var center = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPos(new Vector2(12, center.Y / 2f - 20f));
            ImGui.TextColored(ImGuiColors.DalamudGrey, "选择工房范围以查看需求");
            ImGui.SetCursorPosX(12);
            ImGui.TextColored(ImGuiColors.DalamudGrey, "生成的清单将使用 Vulcan 常规规划器");
            return;
        }

        var scope = _selectedWorkshopScope;
        var scopeLabel = scope.Kind switch
        {
            WorkshopScopeKind.Project => "工房工程",
            WorkshopScopeKind.Part => "部件",
            WorkshopScopeKind.Phase => "阶段",
            _ => "范围",
        };

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var headerIcon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(scope.ProjectIconId));
        if (headerIcon.TryGetWrap(out var wrap, out _))
            ImGui.Image(wrap.Handle, new Vector2(48f, 48f));
        else
            ImGui.Dummy(new Vector2(48f, 48f));

        ImGui.SameLine(0, 12f);
        var headerStartY = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(headerStartY + 4f);
        ImGui.TextColored(ImGuiColors.ParsedGold, scope.DisplayName);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, scopeLabel);
        if (scope.Kind != WorkshopScopeKind.Project)
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"工程: {scope.ProjectName}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawWorkshopStatRow("有配方:", scope.CraftableRequirementCount.ToString(), scope.CraftableRequirementCount > 0 ? new Vector4(0.65f, 0.85f, 1.0f, 1.0f) : ImGuiColors.DalamudGrey3);
        DrawWorkshopStatRow("已跳过:", scope.UncraftableRequirementCount.ToString(), scope.UncraftableRequirementCount > 0 ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey3);
        if (scope.UncraftableRequirementCount > 0 && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextColored(ImGuiColors.DalamudOrange, "未找到配方:");
            ImGui.Spacing();
            foreach (var req in scope.Requirements.Where(r => !r.IsCraftable))
                ImGui.TextUnformatted($"  {req.ItemName}  x{req.RequiredQuantity}");
            ImGui.EndTooltip();
        }
        DrawWorkshopStatRow("需求:", scope.TotalRequiredItemCount.ToString(), new Vector4(0.85f, 0.85f, 0.85f, 1.0f));
        DrawWorkshopStatRow("制作:", scope.TotalCraftsNeeded.ToString(), scope.TotalCraftsNeeded > 0 ? new Vector4(0.75f, 1.0f, 0.75f, 1.0f) : ImGuiColors.DalamudGrey3);

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, "次数:");
        ImGui.SameLine();
        ImGui.InputInt("##workshopLoopCount", ref _workshopLoopCount, 1);
        _workshopLoopCount = Math.Max(1, _workshopLoopCount);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.Checkbox("临时##workshopEphemeral", ref _workshopEphemeral);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("制作完成后自动删除此清单, 可稍后在清单编辑器中修改");

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);

        if (_workshopListNameScope != scope || _workshopListNameLoopCount != _workshopLoopCount)
        {
            _workshopListName = WorkshopDataService.GetDefaultListName(scope, _workshopLoopCount);
            _workshopListNameScope = scope;
            _workshopListNameLoopCount = _workshopLoopCount;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##workshopListName", ref _workshopListName, 256);

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        using (ImRaii.Disabled(scope.CraftableRequirementCount == 0))
        {
            if (ImGui.Button("创建制作清单##createWorkshopList"))
                CreateWorkshopCraftingList(scope, _workshopListName);
        }

        if (scope.CraftableRequirementCount == 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("当前选择没有可加入 Vulcan 制作清单的有配方工房补给品");

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var canAddToExistingList = scope.CraftableRequirementCount > 0 && GatherBuddy.CraftingListManager.Lists.Count > 0;
        using (ImRaii.Disabled(!canAddToExistingList))
        {
            if (ImGui.Button("加入已有清单##addWorkshopToExistingList"))
                QueueWorkshopAddToListPopup();
        }

        if (!canAddToExistingList && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var tooltip = scope.CraftableRequirementCount <= 0
                ? "当前选择没有可加入的有配方工房补给品"
                : "先创建制作清单, 然后即可追加工房选择";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var detailHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##workshopDetailScroll", new Vector2(-1, detailHeight));

        var showRetainer = AllaganTools.Enabled;
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        DrawIngredientSectionHeader("工房需求", showRetainer);

        if (scope.Requirements.Count == 0)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
            ImGui.TextColored(ImGuiColors.DalamudGrey, "当前选择没有工房需求");
        }
        else
        {
            foreach (var requirement in scope.Requirements)
            {
                if (itemSheet == null || !itemSheet.TryGetRow(requirement.ItemId, out var item))
                    continue;

                DrawIngredientRow(requirement.ItemId, requirement.RequiredQuantity, item, showRetainer);
            }
        }

        DrawWorkshopRecipeSummary(scope);
        ImGui.EndChild();
    }

    private static void DrawWorkshopStatRow(string label, string value, Vector4 valueColor)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, label);
        ImGui.SameLine(0, 4);
        ImGui.TextColored(valueColor, value);
    }

    private void DrawWorkshopRecipeSummary(WorkshopScopeNode scope)
    {
        var craftableRequirements = scope.Requirements
            .Where(requirement => requirement.IsCraftable)
            .OrderBy(requirement => requirement.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (craftableRequirements.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "已加入清单的配方");

        const float iconSize = 22f;
        var textColor = new Vector4(0.85f, 0.85f, 0.85f, 1.0f);
        foreach (var requirement in craftableRequirements)
        {
            var craftCount = requirement.CraftsNeeded * Math.Max(1, _workshopLoopCount);
            var rowY = ImGui.GetCursorPosY();
            var rowStartX = ImGui.GetCursorPosX() + 12;
            var textY = rowY + (iconSize - ImGui.GetTextLineHeight()) / 2f;
            var icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(requirement.IconId));
            ImGui.SetCursorPosX(rowStartX);
            if (icon.TryGetWrap(out var wrap, out _))
                ImGui.Image(wrap.Handle, new Vector2(iconSize, iconSize));
            else
                ImGui.Dummy(new Vector2(iconSize, iconSize));

            ImGui.SameLine(0, 6f);
            ImGui.SetCursorPosY(textY);
            ImGui.TextColored(textColor, requirement.ItemName);
            ImGui.SameLine(0, 6f);
            ImGui.SetCursorPosY(textY);
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"x{craftCount} 次制作");
        }
    }

    private void CreateWorkshopCraftingList(WorkshopScopeNode scope, string listName)
    {
        var draft = WorkshopDataService.CreateListDraft(scope, _workshopLoopCount);
        if (draft == null)
        {
            GatherBuddy.Log.Warning($"[WorkshopsTab] Failed to create workshop draft for {scope.Kind} '{scope.DisplayName}'");
            return;
        }

        var resolvedName = string.IsNullOrWhiteSpace(listName) ? draft.Name : listName.Trim();
        var list = GatherBuddy.CraftingListManager.CreateNewList(resolvedName, _workshopEphemeral);
        list.Description = draft.Description;
        list.SkipIfEnough = true;

        foreach (var (recipeId, quantity) in draft.Recipes)
            list.AddRecipe(recipeId, quantity);

        if (!GatherBuddy.CraftingListManager.SaveList(list))
        {
            GatherBuddy.Log.Warning($"[WorkshopsTab] Failed to save workshop list '{list.Name}'");
            return;
        }

        GatherBuddy.Log.Information(
            $"[WorkshopsTab] Created workshop list '{list.Name}' from {scope.Kind} '{scope.DisplayName}' with {draft.Recipes.Count} recipe(s)");
        Communicator.Print($"已创建工房清单 '{list.Name}', 包含 {draft.Recipes.Count} 个配方");
        OpenCraftingList(list);
        _craftingListsRequestFocus = true;
        _previewList = list;
        _previewFolderPath = null;
    }

    private void QueueWorkshopAddToListPopup()
    {
        _workshopExistingListSearch = string.Empty;
        _openWorkshopAddToListPopup = true;
    }

    private void DrawWorkshopAddToListPopup()
    {
        var popupSize = new Vector2(520f, 380f);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(popupSize, ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(popupSize, popupSize);
        if (!ImGui.BeginPopup(WorkshopAddToListPopupId, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            return;

        var scope = _selectedWorkshopScope;
        if (scope == null)
        {
            ImGui.TextColored(ImGuiColors.ParsedGold, "加入已有清单");
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "未选择工房范围");
            ImGui.Spacing();
            if (ImGui.Button("关闭", new Vector2(100f, 0f)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }
        ImGui.TextColored(ImGuiColors.ParsedGold, "加入已有清单");
        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"{scope.DisplayName} · {scope.Kind} · x{Math.Max(1, _workshopLoopCount)}");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##WorkshopExistingListSearch", "搜索清单...", ref _workshopExistingListSearch, 256);
        ImGui.Spacing();

        var filteredLists = GatherBuddy.CraftingListManager.Lists
            .Where(list =>
                string.IsNullOrWhiteSpace(_workshopExistingListSearch)
             || list.Name.Contains(_workshopExistingListSearch, StringComparison.OrdinalIgnoreCase)
             || CraftingListManager.FormatFolderPath(list.FolderPath).Contains(_workshopExistingListSearch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 3f + 6f;
        var listHeight = Math.Max(120f, ImGui.GetContentRegionAvail().Y - footerHeight);
        ImGui.BeginChild("##WorkshopExistingListScroll", new Vector2(-1, listHeight), true);
        if (filteredLists.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "没有匹配搜索的清单");
        }
        else
        {
            foreach (var list in filteredLists)
            {
                if (ImGui.Selectable($"{list.Name}##workshopExistingList_{list.ID}"))
                {
                    AddWorkshopScopeToExistingList(scope, list);
                    ImGui.CloseCurrentPopup();
                }

                if (!string.IsNullOrEmpty(list.FolderPath))
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20f);
                    ImGui.TextColored(ImGuiColors.DalamudGrey3, CraftingListManager.FormatFolderPath(list.FolderPath));
                }
            }
        }
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("取消", new Vector2(100f, 0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private void AddWorkshopScopeToExistingList(WorkshopScopeNode scope, CraftingListDefinition list)
    {
        var draft = WorkshopDataService.CreateListDraft(scope, _workshopLoopCount);
        if (draft == null)
        {
            GatherBuddy.Log.Warning($"[WorkshopsTab] Failed to create workshop draft for existing list append from {scope.Kind} '{scope.DisplayName}'");
            return;
        }

        foreach (var (recipeId, quantity) in draft.Recipes)
            list.AddRecipe(recipeId, quantity);

        list.Description = string.IsNullOrEmpty(list.Description)
            ? draft.Description
            : list.Description + "\n" + draft.Description;

        if (!GatherBuddy.CraftingListManager.SaveList(list))
        {
            GatherBuddy.Log.Warning($"[WorkshopsTab] Failed to append workshop recipes to existing list '{list.Name}'");
            return;
        }

        RefreshOpenCraftingList(list.ID);
        _previewList = list;
        _previewFolderPath = null;
        GatherBuddy.Log.Information(
            $"[WorkshopsTab] Added {draft.Recipes.Count} workshop recipe(s) from {scope.Kind} '{scope.DisplayName}' to existing list '{list.Name}'");
        Communicator.Print($"已将来自 '{scope.DisplayName}' 的 {draft.Recipes.Count} 个配方加入 '{list.Name}'");
    }
}
