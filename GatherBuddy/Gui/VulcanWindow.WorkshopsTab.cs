using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using GatherBuddy.Vulcan.Workshops;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

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

    private void DrawWorkshopsPage()
        => DrawWorkshopsTabContent();

    private void DrawWorkshopsTabContent()
    {
        if (_openWorkshopAddToListPopup)
        {
            M3Dialog.Open(WorkshopAddToListPopupId);
            _openWorkshopAddToListPopup = false;
        }

        var projects = WorkshopDataService.GetProjects();
        EnsureWorkshopSelection(projects);

        var avail = ImGui.GetContentRegionAvail();
        var leftWidth = VulcanUiScaling.Scaled(240f);
        var remainingWidth = Math.Max(0f, avail.X - leftWidth - M3.Space2 * 2f);
        var middleWidth = remainingWidth * 0.38f;

        using (var panel = M3Panel.Begin("##WorkshopProjectsPanel", new Vector2(leftWidth, avail.Y), false, VulcanUiStyle.PanelBackground))
        {
            if (panel)
            {
                DrawWorkshopProjectsPanel(projects);
            }
        }

        ImGui.SameLine(0, M3.Space2);
        using (var panel = M3Panel.Begin("##WorkshopScopesPanel", new Vector2(middleWidth, avail.Y), true, VulcanUiStyle.PanelBackground))
        {
            if (panel)
            {
                DrawWorkshopScopesPanel();
            }
        }

        ImGui.SameLine(0, M3.Space2);
        using (var panel = M3Panel.Begin("##WorkshopDetailsPanel", new Vector2(0, avail.Y), false))
        {
            if (panel)
            {
                DrawWorkshopDetailsPanel();
            }
        }

        DrawWorkshopAddToListPopup();
    }
    private void EnsureWorkshopSelection(IReadOnlyList<WorkshopProjectNode> projects)
    {
        if (_selectedWorkshopProject != null && !ContainsWorkshopProject(projects, _selectedWorkshopProject.SequenceId))
        {
            _selectedWorkshopProject = null;
        }

        _selectedWorkshopProject ??= projects.Count > 0 ? projects[0] : null;

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
        {
            _workshopLoopCount = 1;
        }
    }

    private static bool ScopeExists(WorkshopProjectNode project, WorkshopScopeNode scope)
        => scope switch
        {
            WorkshopProjectNode projectScope => projectScope.SequenceId == project.SequenceId,
            WorkshopPartNode partScope => project.Parts.Exists(part => part.PartId == partScope.PartId),
            WorkshopPhaseNode phaseScope => project.Parts.Exists(part => part.Phases.Exists(phase => phase.PhaseId == phaseScope.PhaseId)),
            _ => false,
        };

    private static bool ContainsWorkshopProject(IReadOnlyList<WorkshopProjectNode> projects, uint sequenceId)
    {
        for (var i = 0; i < projects.Count; i++)
        {
            if (projects[i].SequenceId == sequenceId)
            {
                return true;
            }
        }

        return false;
    }

    private void DrawWorkshopProjectsPanel(IReadOnlyList<WorkshopProjectNode> projects)
    {
        DrawSearchInputWithInlineClear("##workshopProjectSearch", "搜索工程", ref _workshopProjectSearch, 256);
        var filteredProjects = projects;
        if (!string.IsNullOrWhiteSpace(_workshopProjectSearch))
        {
            var matches = new List<WorkshopProjectNode>();
            foreach (var candidate in projects)
            {
                if (candidate.ProjectName.Contains(_workshopProjectSearch, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(candidate);
                }
            }

            filteredProjects = matches;
        }

        M3Text.Draw(filteredProjects.Count == 1 ? "1 个工程" : $"{filteredProjects.Count} 个工程", M3TextStyle.Faint);
        using var list = ImRaii.Child("##WorkshopProjectsList", new Vector2(-1, -1), false);
        if (!list)
        {
            return;
        }

        if (filteredProjects.Count == 0)
        {
            M3Text.Draw("没有匹配搜索条件的工房工程。", M3TextStyle.Faint, true);
            return;
        }

        var iconSize = VulcanUiScaling.Scaled(28f);
        foreach (var project in filteredProjects)
        {
            if (Ui.IconListRow($"##workshopProject_{project.SequenceId}", project.ProjectIconId, project.ProjectName,
                    _selectedWorkshopProject?.SequenceId == project.SequenceId, iconSize))
            {
                _selectedWorkshopProject = project;
                _selectedWorkshopScope = project;
            }

            M3Tooltip.Hovered($"{project.ProjectName}\n{project.Parts.Count} 个部件， {project.PhaseCount} 个阶段");
        }
    }

    private void DrawWorkshopScopesPanel()
    {
        if (_selectedWorkshopProject == null)
        {
            M3Text.Draw("选择一个工房工程查看各部件。", M3TextStyle.Faint, true);
            return;
        }

        M3Widgets.SectionLabel("工程范围");
        M3Text.Draw(_selectedWorkshopProject.ProjectName, M3TextStyle.Muted, true);
        ImGui.Dummy(new Vector2(0, M3.Space1));

        var overviewSelected = _selectedWorkshopScope is WorkshopProjectNode selectedProject
            && selectedProject.SequenceId == _selectedWorkshopProject.SequenceId;
        using (var overview = M3Tree.Node($"##workshopOverview_{_selectedWorkshopProject.SequenceId}", "整个工程", overviewSelected, true,
                   FontAwesomeIcon.Industry, tooltip: "列出整个部队工房工程所需的全部材料，可用于创建涵盖整个工程的制作清单。"))
        {
            if (overview.Clicked)
            {
                _selectedWorkshopScope = _selectedWorkshopProject;
            }
        }

        foreach (var part in _selectedWorkshopProject.Parts)
        {
            var isSelected = _selectedWorkshopScope is WorkshopPartNode selectedPart && selectedPart.PartId == part.PartId;
            using var node = M3Tree.Node($"##workshopPart_{part.PartId}", part.DisplayName, isSelected, part.Phases.Count == 0, FontAwesomeIcon.Cube,
                defaultOpen: true, tooltip: $"{part.Phases.Count} 个阶段， {part.CraftableRequirementCount} 种有配方的材料",
                trailing: part.Phases.Count > 0 ? part.Phases.Count.ToString() : null);
            if (node.Clicked || node.DoubleClicked)
            {
                _selectedWorkshopScope = part;
            }

            if (!node.Open)
            {
                continue;
            }

            foreach (var phase in part.Phases)
            {
                var phaseSelected = _selectedWorkshopScope is WorkshopPhaseNode selectedPhase && selectedPhase.PhaseId == phase.PhaseId;
                using var phaseNode = M3Tree.Node($"##workshopPhase_{phase.PhaseId}", $"阶段 {phase.PhaseIndex}", phaseSelected, true,
                    tooltip: $"{phase.CraftableRequirementCount} 种有配方的材料");
                if (phaseNode.Clicked)
                {
                    _selectedWorkshopScope = phase;
                }
            }
        }
    }

    private static string DescribeUncraftableRequirements(WorkshopScopeNode scope)
    {
        var builder = new StringBuilder("缺少配方的材料 ");
        var first = true;
        foreach (var requirement in scope.Requirements)
        {
            if (requirement.IsCraftable)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append(requirement.ItemName).Append(" ×").Append(requirement.RequiredQuantity);
            first = false;
        }

        return builder.ToString();
    }

    private void DrawWorkshopDetailsPanel()
    {
        if (_selectedWorkshopScope == null)
        {
            M3Widgets.EmptyState("##noWorkshopScope", "未选择工程范围", "选择工程、部件或阶段查看需求。制作清单使用 Vulcan 的常规规划器。",
                FontAwesomeIcon.Industry);
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

        using var scroll = ImRaii.Child("##workshopDetailScroll", new Vector2(-1, -1), false);
        if (!scroll)
        {
            return;
        }

        var start = ImGui.GetCursorScreenPos();
        var headerIcon = VulcanUiScaling.Scaled(48f);
        M3Widgets.GameIcon(scope.ProjectIconId, headerIcon);
        ImGui.SameLine(0, M3.Space3);
        using (ImRaii.Group())
        {
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y + (headerIcon - ImGui.GetTextLineHeightWithSpacing() * 2f) * 0.5f));
            using (ImRaii.PushFont(M3.TitleMedium))
            {
                ImGui.TextUnformatted(scope.DisplayName);
            }

            M3Text.Draw(scope.Kind == WorkshopScopeKind.Project ? scopeLabel : $"{scope.ProjectName} · {scopeLabel}", M3TextStyle.Muted);
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        using (M3Card.Begin("##workshopSummary", "摘要", FontAwesomeIcon.ClipboardList, M3.AccentFor(0)))
        {
            M3Widgets.RowValue("有配方的材料", scope.CraftableRequirementCount.ToString(), color: scope.CraftableRequirementCount > 0 ? M3.Scheme.Info : null);
            M3Widgets.RowValue("因无配方跳过", scope.UncraftableRequirementCount.ToString(),
                scope.UncraftableRequirementCount > 0 ? DescribeUncraftableRequirements(scope) : null,
                scope.UncraftableRequirementCount > 0 ? M3.Scheme.Warning : null);
            M3Widgets.RowValue("所需材料数", scope.TotalRequiredItemCount.ToString());
            M3Widgets.RowValue("制作次数", scope.TotalCraftsNeeded.ToString(), color: scope.TotalCraftsNeeded > 0 ? M3.Scheme.Success : null);
        }

        using (M3Card.Begin("##workshopMakeList", "创建制作清单", FontAwesomeIcon.ListUl, M3.AccentFor(1)))
        {
            if (M3Widgets.RowNumber("制作轮数", ref _workshopLoopCount, 1, 1, 999, supporting: "按选中的范围重复制作的次数。"))
            {
                _workshopLoopCount = Math.Max(1, _workshopLoopCount);
            }

            M3Widgets.RowSwitch("临时###Ephemeral##workshopEphemeral", ref _workshopEphemeral,
                "制作完成后删除清单。之后可在清单编辑器中修改此设置。");

            if (_workshopListNameScope != scope || _workshopListNameLoopCount != _workshopLoopCount)
            {
                _workshopListName = WorkshopDataService.GetDefaultListName(scope, _workshopLoopCount);
                _workshopListNameScope = scope;
                _workshopListNameLoopCount = _workshopLoopCount;
            }

            M3Widgets.TextField("##workshopListName", "清单名称", ref _workshopListName, ImGui.GetContentRegionAvail().X - M3Card.RightInset, 256);
            ImGui.Dummy(new Vector2(0, M3.Space1));
            if (M3Widgets.Button("##createWorkshopList", "创建清单", M3ButtonStyle.Filled, FontAwesomeIcon.Plus,
                    enabled: scope.CraftableRequirementCount > 0,
                    tooltip: scope.CraftableRequirementCount > 0 ? null : "当前选择没有可加入 Vulcan 制作清单的有配方工房补给品"))
            {
                CreateWorkshopCraftingList(scope, _workshopListName);
            }

            ImGui.SameLine(0, M3.Space2);
            var canAddToExistingList = scope.CraftableRequirementCount > 0 && GatherBuddy.CraftingListManager.Lists.Count > 0;
            if (M3Widgets.Button("##addWorkshopToExistingList", "添加到已有清单", M3ButtonStyle.Tonal, FontAwesomeIcon.FolderPlus,
                    enabled: canAddToExistingList,
                    tooltip: canAddToExistingList
                        ? "将这些配方加入一份已有的制作清单。"
                        : scope.CraftableRequirementCount <= 0
                            ? "当前选择没有可加入的有配方工房补给品"
                            : "先创建制作清单，再将选中的工房配方添加进去。"))
            {
                QueueWorkshopAddToListPopup();
            }
        }

        var showRetainer = AllaganTools.Enabled;
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        DrawIngredientSectionHeader("部队工房所需材料", showRetainer);
        if (scope.Requirements.Count == 0)
        {
            M3Text.Draw("未找到当前选择的工房需求。", M3TextStyle.Faint);
        }
        else
        {
            foreach (var requirement in scope.Requirements)
            {
                if (itemSheet != null && itemSheet.TryGetRow(requirement.ItemId, out var item))
                {
                    DrawIngredientRow(requirement.ItemId, requirement.RequiredQuantity, item, showRetainer);
                }
            }
        }

        DrawWorkshopRecipeSummary(scope);
    }

    private void DrawWorkshopRecipeSummary(WorkshopScopeNode scope)
    {
        var sortedRequirements = new List<(WorkshopSupplyRequirement Requirement, int Index)>();
        foreach (var requirement in scope.Requirements)
        {
            if (requirement.IsCraftable)
            {
                sortedRequirements.Add((requirement, sortedRequirements.Count));
            }
        }

        sortedRequirements.Sort(static (lhs, rhs) =>
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(lhs.Requirement.ItemName, rhs.Requirement.ItemName);
            return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
        });
        var craftableRequirements = sortedRequirements.ConvertAll(static entry => entry.Requirement);
        if (craftableRequirements.Count == 0)
        {
            return;
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        M3Widgets.SectionLabel("将加入清单的配方");
        var iconSize = VulcanUiScaling.Scaled(22f);
        foreach (var requirement in craftableRequirements)
        {
            var craftCount = requirement.CraftsNeeded * Math.Max(1, _workshopLoopCount);
            M3Widgets.GameIcon(requirement.IconId, iconSize);
            ImGui.SameLine(0, M3.Space2);
            Ui.RowText(requirement.ItemName, iconSize);
            ImGui.SameLine(0, M3.Space2);
            Ui.RowText(craftCount == 1 ? "制作 1 次" : $"{craftCount} 次制作", iconSize, M3TextStyle.Muted);
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
        {
            list.AddRecipe(recipeId, quantity);
        }

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
        var scope = _selectedWorkshopScope;
        using var dialog = M3Dialog.Begin(WorkshopAddToListPopupId, "添加到制作清单",
            scope == null ? "请先选择部队工房工程。" : $"{scope.DisplayName}, {scope.Kind.ToString().ToLowerInvariant()}, ×{Math.Max(1, _workshopLoopCount)}",
            FontAwesomeIcon.FolderPlus, 460f);
        if (!dialog.IsOpen)
        {
            return;
        }

        if (scope != null)
        {
            M3Widgets.SearchField("##WorkshopExistingListSearch", "搜索清单", ref _workshopExistingListSearch, ImGui.GetContentRegionAvail().X);
            var filteredLists = new List<CraftingListDefinition>();
            foreach (var candidate in GatherBuddy.CraftingListManager.Lists)
            {
                if (string.IsNullOrWhiteSpace(_workshopExistingListSearch)
                 || candidate.Name.Contains(_workshopExistingListSearch, StringComparison.OrdinalIgnoreCase)
                 || CraftingListManager.FormatFolderPath(candidate.FolderPath).Contains(_workshopExistingListSearch, StringComparison.OrdinalIgnoreCase))
                {
                    filteredLists.Add(candidate);
                }
            }

            filteredLists.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));

            using var lists = ImRaii.Child("##WorkshopExistingListScroll", new Vector2(-1, VulcanUiScaling.Scaled(240f)), false);
            if (lists)
            {
                if (filteredLists.Count == 0)
                {
                    M3Text.Draw("没有符合搜索条件的清单。", M3TextStyle.Faint);
                }

                foreach (var list in filteredLists)
                {
                    var folder = string.IsNullOrEmpty(list.FolderPath) ? null : CraftingListManager.FormatFolderPath(list.FolderPath);
                    if (M3Widgets.MenuItem($"##workshopExistingList_{list.ID}", list.Name, false, FontAwesomeIcon.ClipboardList, shortcut: folder))
                    {
                        AddWorkshopScopeToExistingList(scope, list);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        M3Dialog.Actions([new M3DialogAction("取消")]);
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
        {
            list.AddRecipe(recipeId, quantity);
        }

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
