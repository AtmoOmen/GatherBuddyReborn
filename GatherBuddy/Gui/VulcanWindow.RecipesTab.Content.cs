using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private const string BulkAddRecipesDialogId = "##bulkAddRecipes";
    private static readonly M3PluginRequirement[] RecipeAllaganToolsPlugin = [new("InventoryTools", "Allagan Tools")];

    private void DrawFilterPanel()
    {
        if (DrawSearchInputWithInlineClear("##recipeSearch", "搜索配方", ref _recipeSearchText, 256))
        {
            _filtersDirty = true;
        }

        M3Widgets.SectionLabel("制作职业");
        var iconSize = VulcanUiScaling.Scaled(30f);
        var flow = new M3Flow(M3.Space1);
        flow.Next(M3Widgets.ChipWidth("全部"));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + MathF.Max(0f, (iconSize - M3Widgets.ChipHeight) * 0.5f));
        if (M3Widgets.Chip("##jobAll", "全部", _selectedJobFilters.Count == 0, tooltip: "显示所有职业的配方"))
        {
            _selectedJobFilters.Clear();
            _filtersDirty = true;
        }

        for (var i = 0; i < JobNames.Length; i++)
        {
            var jobId = CraftTypeToClassJobId[i];
            var isSelected = _selectedJobFilters.Contains(jobId);
            flow.Next(iconSize);
            if (M3Widgets.GameIconButton($"##job{i}", 62100 + jobId, iconSize, isSelected || _selectedJobFilters.Count == 0 ? 1f : 0.55f, JobNames[i],
                    selected: isSelected))
            {
                if (!_selectedJobFilters.Remove(jobId))
                {
                    _selectedJobFilters.Add(jobId);
                }

                _filtersDirty = true;
            }
        }

        M3Widgets.SectionLabel("等级范围");
        if (M3Widgets.LabeledCheckbox("使用物品装备等级###Use item equip level", ref _filterByEquipLevel, tooltip: "按物品装备等级筛选和排序，而不是按制作等级。"))
        {
            _filtersDirty = true;
        }

        var low = _minLevel;
        var high = _maxLevel;
        if (M3Widgets.RangeSliderInt("##levels", ref low, ref high, 1, 100, ImGui.GetContentRegionAvail().X, displayValue: $"{low} 至 {high}"))
        {
            _minLevel = Math.Clamp(low, 1, 100);
            _maxLevel = Math.Clamp(high, _minLevel, 100);
            _filtersDirty = true;
        }

        M3Widgets.SectionLabel("显示");
        DrawRecipeFilter("仅显示升级配方", ref _filterBrowserLevelingOnly, "只显示制作手册等级列表中的配方。", true);
        if (M3Widgets.LabeledCheckbox("隐藏已制作###Hide crafted", ref _hideCrafted))
        {
            _filtersDirty = true;
        }

        DrawRecipeFilter("家具", ref _filterBrowserHousingRecipes, "只显示产出住宅物品的配方。");
        DrawRecipeFilter("染剂", ref _filterBrowserDyeRecipes, "只显示产出染剂的配方。");
        DrawRecipeFilter("收藏品", ref _filterBrowserCollectables);
        DrawRecipeFilter("秘籍配方", ref _filterBrowserMasterRecipes);
        DrawRecipeFilter("高难度配方", ref _filterBrowserExpertRecipes);
        DrawRecipeFilter("任务配方", ref _filterBrowserQuestRecipes);

        ImGui.Dummy(new Vector2(0, M3.Space2));
        var hasLists = GatherBuddy.CraftingListManager.Lists.Count > 0;
        const string description = "将筛选结果中尚未制作的所有配方加入制作清单。";
        if (M3Widgets.Button("##bulkAdd", _filteredUncraftedRecipeCount == 1 ? "将 1 个配方加入清单" : $"将 {_filteredUncraftedRecipeCount} 个配方加入清单",
                M3ButtonStyle.Tonal, FontAwesomeIcon.FolderPlus, ImGui.GetContentRegionAvail().X, _filteredUncraftedRecipeCount > 0 && hasLists,
                !hasLists
                    ? $"{description}\n\n请先创建制作清单。"
                    : _filteredUncraftedRecipeCount == 0
                        ? $"{description}\n\n没有符合筛选条件的未制作配方。"
                        : description))
        {
            _bulkAddFilteredListSearch = string.Empty;
            M3Dialog.Open(BulkAddRecipesDialogId);
        }

        DrawBulkAddDialog();
    }

    private static void DrawRecipeFilter(string label, ref bool value, string? tooltip = null, bool leveling = false)
    {
        if (!M3Widgets.LabeledCheckbox(label, ref value, tooltip: tooltip))
        {
            return;
        }

        if (value && leveling)
        {
            _filterBrowserMasterRecipes = false;
            _filterBrowserHousingRecipes = false;
            _filterBrowserDyeRecipes = false;
            _filterBrowserCollectables = false;
            _filterBrowserExpertRecipes = false;
            _filterBrowserQuestRecipes = false;
        }
        else if (value)
        {
            _filterBrowserLevelingOnly = false;
        }

        _filtersDirty = true;
    }

    private void DrawBulkAddDialog()
    {
        using var dialog = M3Dialog.Begin(BulkAddRecipesDialogId, "将配方加入清单",
            $"将筛选出的 {_filteredUncraftedRecipeCount} 个未制作配方加入选定的清单。", FontAwesomeIcon.FolderPlus, 420f);
        if (!dialog.IsOpen)
        {
            return;
        }

        M3Widgets.SearchField("##BulkAddFilteredListSearch", "搜索清单", ref _bulkAddFilteredListSearch, ImGui.GetContentRegionAvail().X);
        var filteredLists = FilterListsByName(GatherBuddy.CraftingListManager.Lists, _bulkAddFilteredListSearch);
        filteredLists.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        using (var lists = ImRaii.Child("##BulkAddFilteredListScroll", new Vector2(-1, VulcanUiScaling.Scaled(220f)), false))
        {
            if (lists)
            {
                if (filteredLists.Count == 0)
                {
                    M3Text.Draw("没有符合搜索条件的清单。", M3TextStyle.Faint);
                }

                foreach (var list in filteredLists)
                {
                    if (M3Widgets.MenuItem($"##bulkList{list.ID}", list.Name, false, FontAwesomeIcon.ClipboardList))
                    {
                        AddFilteredRecipesToList(list);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
        }

        M3Dialog.Actions([new M3DialogAction("取消")]);
    }

    private static List<CraftingListDefinition> FilterListsByName(IReadOnlyList<CraftingListDefinition> lists, string search)
    {
        var filtered = new List<CraftingListDefinition>(lists.Count);
        foreach (var list in lists)
        {
            if (string.IsNullOrWhiteSpace(search) || list.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(list);
            }
        }

        return filtered;
    }

    private void AddFilteredRecipesToList(CraftingListDefinition list)
    {
        var addedCount = 0;
        if (_filteredRecipes != null)
        {
            foreach (var recipe in _filteredRecipes)
            {
                if (recipe.IsCrafted)
                {
                    continue;
                }

                list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, 1));
                addedCount++;
            }
        }

        if (addedCount == 0)
        {
            return;
        }

        GatherBuddy.CraftingListManager.SaveList(list);
        RefreshOpenCraftingList(list.ID);
        GatherBuddy.Log.Information($"[VulcanWindow] Added {addedCount} filtered uncrafted recipes to list '{list.Name}'");
        Communicator.Print($"已将筛选结果中 {addedCount} 个未制作配方加入「{list.Name}」。");
    }

    private void DrawResultsList()
    {
        if (_filteredRecipes == null || _filteredRecipes.Count == 0)
        {
            M3Widgets.EmptyState("##noRecipes", "没有配方", "没有符合筛选条件的配方。", FontAwesomeIcon.BookOpen);
            return;
        }

        var sortLabel = _sortColumn switch
        {
            SortColumn.Level => _filterByEquipLevel ? "装备等级" : "等级",
            SortColumn.Crafted => "已制作",
            _ => "排序",
        };
        var rowTop = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (M3Widgets.ButtonHeight - ImGui.GetTextLineHeight()) * 0.5f);
        M3Text.Draw($"{_filteredRecipes.Count} 个配方", M3TextStyle.Muted);
        var sortWidth = M3Widgets.ButtonWidth(FontAwesomeIcon.Sort, sortLabel);
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - sortWidth - M3Widgets.IconButtonSize - M3.Space1));
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop));
        if (M3Widgets.Button("##sortBtn", sortLabel, M3ButtonStyle.Text, FontAwesomeIcon.Sort, tooltip: "排序依据"))
        {
            M3Menu.Open("##sortMenu");
        }

        ImGui.SameLine(0, M3.Space1);
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop + (M3Widgets.ButtonHeight - M3Widgets.IconButtonSize) * 0.5f));
        var ascending = _sortDirection == SortDirection.Ascending;
        if (M3Widgets.IconButton("##sortDirection", ascending ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown,
                ascending ? "升序，点击切换为降序" : "降序，点击切换为升序"))
        {
            _sortDirection = ascending ? SortDirection.Descending : SortDirection.Ascending;
            _filtersDirty = true;
        }

        using (var menu = M3Menu.Begin("##sortMenu"))
        {
            if (menu.IsOpen)
            {
                if (M3Menu.Item("##sortLevel", _filterByEquipLevel ? "装备等级" : "等级", FontAwesomeIcon.SortNumericDown, selected: _sortColumn == SortColumn.Level))
                {
                    _sortColumn = SortColumn.Level;
                    _filtersDirty = true;
                }

                if (M3Menu.Item("##sortCrafted", "已制作", FontAwesomeIcon.Check, selected: _sortColumn == SortColumn.Crafted))
                {
                    _sortColumn = SortColumn.Crafted;
                    _filtersDirty = true;
                }
            }
        }

        var iconSize = VulcanUiScaling.Scaled(28f);
        var jobIconSize = VulcanUiScaling.Scaled(20f);
        var rowHeight = iconSize + M3.Space2;
        var itemHeight = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        using var rows = ImRaii.Child("##recipeRows", new Vector2(-1, -1));
        if (!rows)
        {
            return;
        }

        if (_pendingRecipeScrollId.HasValue)
        {
            var targetIndex = _filteredRecipes.FindIndex(r => r.Recipe.RowId == _pendingRecipeScrollId.Value);
            if (targetIndex >= 0)
            {
                var viewportHeight = ImGui.GetContentRegionAvail().Y;
                ImGui.SetScrollY(Math.Max(0f, targetIndex * itemHeight - Math.Max(0f, (viewportHeight - itemHeight) * 0.5f)));
            }
            else
            {
                _pendingRecipeScrollId = null;
            }
        }

        Ui.ClippedDraw(_filteredRecipes, recipe =>
        {
            DrawRecipeRow(recipe, iconSize, jobIconSize, rowHeight);
            if (GatherBuddy.Config.ShowRecipeBrowserTooltips && ImGui.IsItemHovered()
             && TryGetRecipesTooltipAnchor(out var tooltipAnchorMin, out var tooltipAnchorMax, out var tooltipExpandRight))
            {
                GatherBuddy.NativeItemTooltipBridge?.RequestItemTooltip(recipe.Recipe.ItemResult.RowId, tooltipAnchorMin, tooltipAnchorMax, tooltipExpandRight);
            }

            if (_pendingRecipeScrollId == recipe.Recipe.RowId)
            {
                ImGui.SetScrollHereY(0.5f);
                _pendingRecipeScrollId = null;
            }

            DrawRecipeContextMenu(recipe);
        }, itemHeight);
    }

    private static void DrawRecipeRow(ExtendedRecipe recipe, float iconSize, float jobIconSize, float rowHeight)
    {
        var s = M3.Scheme;
        var isSelected = _selectedRecipe?.Recipe.RowId == recipe.Recipe.RowId;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var min = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton($"##browse{recipe.Recipe.RowId}", new Vector2(width, rowHeight)))
        {
            _selectedRecipe = recipe;
        }

        var hovered = ImGui.IsItemHovered();
        var max = min + new Vector2(width, rowHeight);
        var drawList = ImGui.GetWindowDrawList();
        if (isSelected)
        {
            drawList.AddRectFilled(min, max, M3.U32(s.SecondaryContainer), M3.ShapeSmall);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(min, max, M3.U32(s.OnSurface, M3.StateHover), M3.ShapeSmall);
        }

        var iconMin = min + new Vector2(M3.Space1, (rowHeight - iconSize) * 0.5f);
        if (recipe.Icon.TryGetWrap(out var wrap, out _))
        {
            M3ActionIcon.Image(drawList, wrap, iconMin, iconMin + new Vector2(iconSize), M3.ShapeExtraSmall);
        }

        var textY = min.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        var level = _filterByEquipLevel ? $"{recipe.ItemEquipLevel}" : $"{recipe.Level}";
        var right = max.X - M3.Space2;
        drawList.AddText(new Vector2(right - ImGui.CalcTextSize(level).X, textY), M3.U32(s.OnSurfaceVariant), level);
        right -= ImGui.CalcTextSize("100").X + M3.Space1 + jobIconSize;
        var jobIconId = 62100 + CraftTypeToClassJobId[recipe.Recipe.CraftType.RowId];
        var jobMin = new Vector2(right, min.Y + (rowHeight - jobIconSize) * 0.5f);
        M3ActionIcon.Image(drawList, M3ActionIcon.GameIcon(jobIconId), jobMin, jobMin + new Vector2(jobIconSize), M3.ShapeExtraSmall);
        var crafted = recipe.IsCrafted ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;
        var craftedSize = M3Draw.MeasureIcon(crafted);
        right -= M3.Space2 + craftedSize.X;
        M3Draw.Icon(drawList, crafted, new Vector2(right, min.Y + (rowHeight - craftedSize.Y) * 0.5f),
            recipe.IsCrafted ? s.Success : M3Text.ColorOf(M3TextStyle.Faint));

        var textX = iconMin.X + iconSize + M3.Space2;
        if (GatherBuddy.RecipeBrowserSettings.Has(recipe.Recipe.RowId))
        {
            var cogSize = M3Draw.MeasureIcon(FontAwesomeIcon.Cog);
            M3Draw.Icon(drawList, FontAwesomeIcon.Cog, new Vector2(textX, min.Y + (rowHeight - cogSize.Y) * 0.5f), s.Tertiary);
            textX += cogSize.X + M3.Space1;
        }

        var name = M3Navigation.Truncate(recipe.Name, MathF.Max(8f, right - M3.Space2 - textX));
        drawList.AddText(new Vector2(textX, textY), M3.U32(isSelected ? s.OnSecondaryContainer : s.OnSurface), name);
        if (hovered && !GatherBuddy.Config.ShowRecipeBrowserTooltips && name != recipe.Name)
        {
            M3Tooltip.Show(recipe.Name);
        }
    }

    private void DrawRecipeContextMenu(ExtendedRecipe recipe)
    {
        using var menu = M3Menu.BeginContext($"RecipeContextMenu##{recipe.Recipe.RowId}");
        if (!menu.IsOpen)
        {
            return;
        }

        if (ImGui.IsWindowAppearing())
        {
            _contextMenuListSearch = string.Empty;
            _contextMenuAddQuantity = 1;
            _contextMenuNewListName = string.Empty;
            _contextMenuNewListEphemeral = false;
        }

        M3Menu.Heading(recipe.Name);
        if (M3Menu.Item("##settings", "制作设置", FontAwesomeIcon.Cog))
        {
            _craftSettingsPopup.Open(recipe.Recipe.RowId, recipe.Name);
        }

        if (M3Menu.Item("##debug", "记录配方属性", FontAwesomeIcon.Bug, tooltip: "将配方数据写入 Dalamud 日志，以便排查问题。"))
        {
            LogRecipeProperties(recipe);
        }

        M3Menu.Divider();
        var width = VulcanUiScaling.Scaled(260f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + M3.Space2);
        using var group = ImRaii.Group();
        M3Widgets.SectionLabel("数量");
        M3Widgets.NumberField("##ContextQty", ref _contextMenuAddQuantity, width, 1, 1, 9999);
        M3Widgets.SectionLabel("新建清单");
        M3TextField.Draw("##NewListName", "清单名称", ref _contextMenuNewListName, width, 128);
        var createEnter = ImGui.IsItemDeactivatedAfterEdit() && ImGui.IsKeyPressed(ImGuiKey.Enter);
        M3Widgets.LabeledCheckbox("临时清单###Ephemeral##ctxNewListEphemeral", ref _contextMenuNewListEphemeral,
            tooltip: "制作完成后删除清单。稍后可在清单编辑器中修改此设置。");
        var canCreate = !string.IsNullOrWhiteSpace(_contextMenuNewListName);
        if ((M3Widgets.Button("##createAndAdd", "创建并添加", M3ButtonStyle.Tonal, FontAwesomeIcon.Plus, width, canCreate) || createEnter) && canCreate)
        {
            var newList = GatherBuddy.CraftingListManager.CreateNewList(_contextMenuNewListName.Trim(), _contextMenuNewListEphemeral);
            newList.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
            GatherBuddy.CraftingListManager.SaveList(newList);
            RaphaelAssessmentService.QueueWarmupForAddedListRecipe(recipe.Recipe.RowId, newList);
            RefreshOpenCraftingList(newList.ID);
            GatherBuddy.Log.Information($"[VulcanWindow] 已创建清单 '{newList.Name}' 并添加 {recipe.Name} x{_contextMenuAddQuantity}");
            M3Snackbar.Show($"已创建 {newList.Name} 并添加 {recipe.Name} ×{_contextMenuAddQuantity}。");
            M3Menu.Close();
        }

        var lists = GatherBuddy.CraftingListManager.Lists;
        if (lists.Count == 0)
        {
            M3Text.Draw("尚无制作清单。", M3TextStyle.Faint);
            return;
        }

        M3Widgets.SectionLabel("加入清单");
        M3Widgets.SearchField("##ContextListSearch", "搜索清单", ref _contextMenuListSearch, width);
        var filteredLists = string.IsNullOrWhiteSpace(_contextMenuListSearch)
            ? lists
            : FilterListsByName(lists, _contextMenuListSearch);
        var height = Math.Min(Math.Max(1, filteredLists.Count) * M3Widgets.MenuItemHeight, VulcanUiScaling.Scaled(170f));
        using var scroll = ImRaii.Child("##SingleAddScroll", new Vector2(width, height), false);
        if (!scroll)
        {
            return;
        }

        if (filteredLists.Count == 0)
        {
            M3Text.Draw("没有符合搜索条件的清单。", M3TextStyle.Faint);
        }

        foreach (var list in filteredLists)
        {
            if (!M3Widgets.MenuItem($"##ctxList{list.ID}", list.Name, false, FontAwesomeIcon.ClipboardList))
            {
                continue;
            }

            list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
            GatherBuddy.CraftingListManager.SaveList(list);
            RaphaelAssessmentService.QueueWarmupForAddedListRecipe(recipe.Recipe.RowId, list);
            RefreshOpenCraftingList(list.ID);
            GatherBuddy.Log.Information($"已将 {recipe.Name} x{_contextMenuAddQuantity} 添加到制作清单 '{list.Name}'");
            M3Snackbar.Show($"已将 {recipe.Name} ×{_contextMenuAddQuantity} 加入 {list.Name}。");
        }
    }

    private static void LogRecipeProperties(ExtendedRecipe recipe)
    {
        GatherBuddy.Log.Information($"=== Recipe Properties for {recipe.Name} ===");
        GatherBuddy.Log.Information($"Recipe.RowId: {recipe.Recipe.RowId}");
        GatherBuddy.Log.Information($"Recipe.Quest.RowId: {recipe.Recipe.Quest.RowId}");
        GatherBuddy.Log.Information($"Recipe.IsSecondary: {recipe.Recipe.IsSecondary}");
        GatherBuddy.Log.Information($"Recipe.IsExpert: {recipe.Recipe.IsExpert}");
        GatherBuddy.Log.Information($"Recipe.SecretRecipeBook.RowId: {recipe.Recipe.SecretRecipeBook.RowId}");
        GatherBuddy.Log.Information($"Recipe.CanQuickSynth: {recipe.Recipe.CanQuickSynth}");
        GatherBuddy.Log.Information($"Recipe.CanHq: {recipe.Recipe.CanHq}");
        GatherBuddy.Log.Information($"Recipe.IsSpecializationRequired: {recipe.Recipe.IsSpecializationRequired}");
        GatherBuddy.Log.Information($"Recipe.DifficultyFactor: {recipe.Recipe.DifficultyFactor}");
        GatherBuddy.Log.Information($"Recipe.QualityFactor: {recipe.Recipe.QualityFactor}");
        GatherBuddy.Log.Information($"Recipe.RecipeLevelTable.RowId: {recipe.Recipe.RecipeLevelTable.RowId}");
        GatherBuddy.Log.Information($"Recipe.RecipeNotebookList.RowId: {recipe.Recipe.RecipeNotebookList.RowId}");
        var resultItem = recipe.Recipe.ItemResult.Value;
        GatherBuddy.Log.Information($"Item.RowId: {resultItem.RowId}");
        GatherBuddy.Log.Information($"Item.AlwaysCollectable: {resultItem.AlwaysCollectable}");
        GatherBuddy.Log.Information($"Item.IsUnique: {resultItem.IsUnique}");
        GatherBuddy.Log.Information($"Item.IsUntradable: {resultItem.IsUntradable}");
        GatherBuddy.Log.Information($"Item.ItemSearchCategory.RowId: {resultItem.ItemSearchCategory.RowId}");
        GatherBuddy.Log.Information($"Item.ItemUICategory.RowId: {resultItem.ItemUICategory.RowId}");
        GatherBuddy.Log.Information($"Item.Rarity: {resultItem.Rarity}");
        LogRecipeNotebookDivisionInfo(recipe.Recipe);
    }

    private static bool UsesRaphaelSolverForRecipeBrowser(Recipe recipe, RecipeCraftSettings? settings)
    {
        var item = new CraftingListItem(recipe.RowId, 1)
        {
            IsOriginalRecipe = true,
            CraftSettings = settings?.Clone(),
        };
        var executionContext = CraftingContextResolver.ResolveExecutionContext(item, recipe, null);
        return CraftingContextResolver.UsesRaphaelSolver(executionContext);
    }

    private void DrawDetailsPanel()
    {
        if (_selectedRecipe == null)
        {
            M3Widgets.EmptyState("##noRecipe", "未选择配方", "选择配方以查看所需材料并开始制作。", FontAwesomeIcon.BookOpen);
            return;
        }

        var recipe = _selectedRecipe;
        var r = recipe.Recipe;
        using var scroll = ImRaii.Child("##recipeDetailScroll", new Vector2(-1, -1));
        if (!scroll)
        {
            return;
        }

        var headerIconSize = VulcanUiScaling.Scaled(48f);
        var start = ImGui.GetCursorScreenPos();
        if (recipe.Icon.TryGetWrap(out var wrap, out _))
        {
            M3Draw.Texture(ImGui.GetWindowDrawList(), wrap, start, headerIconSize);
        }

        ImGui.Dummy(new Vector2(headerIconSize));
        ImGui.SameLine(0, M3.Space3);
        using (ImRaii.Group())
        {
            using (ImRaii.PushFont(M3.TitleMedium))
            {
                ImGui.TextUnformatted(recipe.Name);
            }

            M3Text.Draw($"{recipe.JobAbbreviation} · {recipe.Level} 级 · 产量 {r.AmountResult} · 配方 {r.RowId}", M3TextStyle.Muted);
            var flow = new M3Flow(M3.Space1);
            DrawRecipeTag(ref flow, r.SecretRecipeBook.RowId > 0, "秘籍", M3.Scheme.Tertiary);
            DrawRecipeTag(ref flow, r.ItemResult.Value.AlwaysCollectable, "收藏品", M3.Scheme.Info);
            DrawRecipeTag(ref flow, r.IsExpert, "高难度", M3.Scheme.Error);
            DrawRecipeTag(ref flow, r.ItemResult.Value.ItemSearchCategory.RowId == 0, "任务", M3.Scheme.Secondary);
            DrawRecipeTag(ref flow, recipe.IsCrafted, "已制作", M3.Scheme.Success);
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        var settings = GatherBuddy.RecipeBrowserSettings.Get(r.RowId);
        DrawRecipeActions(recipe, settings);

        var directIngredients = RecipeManager.GetIngredients(r);
        var craftable = directIngredients.Count > 0 ? int.MaxValue : 0;
        foreach (var (ingId, ingAmt) in directIngredients)
        {
            if (ingAmt > 0)
            {
                var (ingNq, ingHq) = GetInventoryCountSplit(ingId);
                craftable = Math.Min(craftable, (ingNq + ingHq) / ingAmt);
            }
        }

        if (craftable == int.MaxValue)
        {
            craftable = 0;
        }

        var lt = r.RecipeLevelTable.Value;
        var (resultNq, resultHq) = GetInventoryCountSplit(r.ItemResult.RowId);
        using (M3Card.Begin("##recipeOverview", "概览", FontAwesomeIcon.InfoCircle, M3.AccentFor(1)))
        {
            M3Widgets.RowValue("作业量", $"{(int)(lt.Difficulty * r.DifficultyFactor / 100)}");
            M3Widgets.RowValue("耐久", $"{(int)(lt.Durability * r.DurabilityFactor / 100)}");
            M3Widgets.RowValue("最高品质", $"{(int)(lt.Quality * r.QualityFactor / 100)}");
            M3Widgets.RowValue("可制作数量", craftable.ToString(), color: craftable > 0 ? M3.Scheme.Success : M3.Scheme.Error);
            M3Widgets.RowValue("背包库存", resultHq > 0 ? $"{resultNq} + {resultHq} HQ" : $"{resultNq}");
        }

        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        var showRetainer = AllaganTools.Enabled;
        DrawIngredientSectionHeader("材料", showRetainer);
        foreach (var (itemId, needed) in directIngredients)
        {
            if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
            {
                DrawIngredientRow(itemId, needed, item, showRetainer);
            }
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        DrawIngredientSectionHeader("全部材料（含中间品）", showRetainer);
        foreach (var (itemId, needed) in GetSortedResolvedIngredients(r))
        {
            if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
            {
                DrawIngredientRow(itemId, needed, item, showRetainer);
            }
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        if (settings != null && settings.HasAnySettings() && itemSheet != null)
        {
            using var card = M3Card.Begin("##recipeSettings", "制作设置", FontAwesomeIcon.Cog, M3.AccentFor(2));
            if (settings.FoodItemId.HasValue && itemSheet.TryGetRow(settings.FoodItemId.Value, out var food))
            {
                M3Widgets.RowValue("食物", food.Name.ExtractText());
            }

            if (settings.MedicineItemId.HasValue && itemSheet.TryGetRow(settings.MedicineItemId.Value, out var medicine))
            {
                M3Widgets.RowValue("药品", medicine.Name.ExtractText());
            }

            if (settings.ManualItemId.HasValue && itemSheet.TryGetRow(settings.ManualItemId.Value, out var manual))
            {
                M3Widgets.RowValue("指南", manual.Name.ExtractText());
            }
        }

        if (!UsesRaphaelSolverForRecipeBrowser(r, settings))
        {
            return;
        }

        if (!RaphaelAssessmentService.TryAssessRecipe(r.RowId, settings, out var raphaelAssessment))
        {
            raphaelAssessment = new RaphaelAssessment(
                RaphaelAssessmentState.Unavailable,
                RaphaelAssessmentOutcome.None,
                "无法使用 Raphael 验证。",
                "此配方没有可用的属性数据。");
        }

        var tone = GetRaphaelAssessmentColor(raphaelAssessment);
        using (M3Card.Begin("##recipeRaphael", "Raphael 验证", FontAwesomeIcon.Lightbulb, tone))
        {
            M3Badge.Draw(raphaelAssessment.Summary, tone, raphaelAssessment.Details);
            M3Text.Draw(raphaelAssessment.Details, M3TextStyle.Muted, true);
            if (raphaelAssessment.State == RaphaelAssessmentState.NotGenerated
             && M3Widgets.Button("##queueRaphael", "立即验证", M3ButtonStyle.Tonal, FontAwesomeIcon.Play))
            {
                RaphaelAssessmentService.TryQueueWarmupForRecipe(r.RowId, settings);
            }
        }
    }

    private uint _resolvedIngredientsRecipeId = uint.MaxValue;
    private readonly List<(uint ItemId, int Needed)> _resolvedIngredients = [];

    private List<(uint ItemId, int Needed)> GetSortedResolvedIngredients(Recipe recipe)
    {
        if (_resolvedIngredientsRecipeId == recipe.RowId)
        {
            return _resolvedIngredients;
        }

        _resolvedIngredients.Clear();
        foreach (var (itemId, needed) in RecipeManager.GetResolvedIngredients(recipe))
        {
            _resolvedIngredients.Add((itemId, needed));
        }

        _resolvedIngredients.Sort(static (a, b) => a.ItemId.CompareTo(b.ItemId));
        _resolvedIngredientsRecipeId = recipe.RowId;
        return _resolvedIngredients;
    }

    private static void DrawRecipeTag(ref M3Flow flow, bool show, string label, Vector4 accent)
    {
        if (!show)
        {
            return;
        }

        flow.Next(M3Badge.Size(label).X);
        M3Badge.Draw(label, accent);
    }

    private void DrawRecipeActions(ExtendedRecipe recipe, RecipeCraftSettings? settings)
    {
        using var card = M3Card.Begin("##recipeActions", "制作", FontAwesomeIcon.Hammer, M3.AccentFor(0));
        M3Widgets.RowNumber("数量###Quantity##browserQty", ref _browserCraftQuantity, 1, 1, 9999);
        if (!M3Plugins.AllLoaded(RecipeAllaganToolsPlugin))
        {
            _browserRetainerRestock = false;
        }

        M3Widgets.RowPluginSwitch("从雇员补充材料###Restock from retainers##browserRestock", ref _browserRetainerRestock, RecipeAllaganToolsPlugin,
            "制作前从雇员处取回缺少的材料。");

        var artisanLoaded = IPCSubscriber.IsReady("Artisan");
        const string artisanTooltip = "Artisan 已加载。请卸载 Artisan 后使用 Vulcan 制作。";
        if (M3Widgets.Button("##startCraft", "开始制作", M3ButtonStyle.Filled, FontAwesomeIcon.Play, enabled: !artisanLoaded,
                tooltip: artisanLoaded ? artisanTooltip : $"制作 {recipe.Name} ×{_browserCraftQuantity}"))
        {
            StartBrowserCraft(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }

        ImGui.SameLine(0, M3.Space2);
        var canQuickSynth = recipe.Recipe.CanQuickSynth;
        if (M3Widgets.Button("##quickSynth", "简易制作", M3ButtonStyle.Tonal, FontAwesomeIcon.Forward, enabled: canQuickSynth && !artisanLoaded,
                tooltip: artisanLoaded
                    ? artisanTooltip
                    : canQuickSynth
                        ? $"简易制作 {recipe.Name} ×{_browserCraftQuantity}"
                        : "此配方无法简易制作。"))
        {
            StartBrowserQuickSynth(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }

        ImGui.SameLine(0, M3.Space2);
        if (M3Widgets.Button("##recipeSettingsBtn", "设置", M3ButtonStyle.Text, FontAwesomeIcon.Cog,
                tooltip: settings != null && settings.HasAnySettings() ? "编辑此配方的制作设置" : "为此配方设置食物、药品等"))
        {
            _craftSettingsPopup.Open(recipe.Recipe.RowId, recipe.Name);
        }
    }

    private static Vector4 GetRaphaelAssessmentColor(RaphaelAssessment assessment)
        => assessment.State switch
        {
            RaphaelAssessmentState.Ready when assessment.Outcome is RaphaelAssessmentOutcome.FullQuality
                or RaphaelAssessmentOutcome.CollectibleTier3
                or RaphaelAssessmentOutcome.MinimumQualityMet
                or RaphaelAssessmentOutcome.NoQualityRequired
                => M3.Scheme.Success,
            RaphaelAssessmentState.Ready => M3.Scheme.Warning,
            RaphaelAssessmentState.Generating => M3.Scheme.Info,
            RaphaelAssessmentState.Failed => M3.Scheme.Error,
            RaphaelAssessmentState.Unavailable => M3.Scheme.Warning,
            _ => M3.Scheme.Outline,
        };

    private static void DrawIngredientSectionHeader(string title, bool showRetainer)
    {
        var colWidth = VulcanUiScaling.Scaled(44f);
        var currentX = ImGui.GetCursorPosX();
        var valueAreaStart = GetIngredientValueAreaStart(currentX, ImGui.GetContentRegionMax().X, showRetainer);
        using (ImRaii.PushFont(M3.TitleMedium))
        {
            ImGui.TextUnformatted(M3Navigation.Truncate(title, MathF.Max(8f, valueAreaStart - currentX - M3.Space2)));
        }

        var bottom = ImGui.GetCursorPosY();
        using (ImRaii.PushFont(M3.LabelSmall))
        {
            void Column(string label, int index)
            {
                var width = ImGui.CalcTextSize(label).X;
                ImGui.SetCursorPos(new Vector2(valueAreaStart + colWidth * index + (colWidth - width) * 0.5f,
                    bottom - ImGui.GetTextLineHeight() - ImGui.GetStyle().ItemSpacing.Y));
                Ui.Text(label, M3.Scheme.OnSurfaceVariant);
            }

            Column("NQ", 0);
            Column("HQ", 1);
            if (showRetainer)
            {
                Column("Ret", 2);
            }
        }

        ImGui.SetCursorPos(new Vector2(currentX, bottom + M3.Space1));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void DrawIngredientRow(uint itemId, int needed, Item item, bool showRetainer)
    {
        var s = M3.Scheme;
        var colWidth = VulcanUiScaling.Scaled(44f);
        var countWidth = VulcanUiScaling.Scaled(36f);
        var iconSize = VulcanUiScaling.Scaled(24f);
        var currentX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();
        var textY = rowY + (iconSize - ImGui.GetTextLineHeight()) * 0.5f;
        var valueAreaStart = GetIngredientValueAreaStart(currentX, ImGui.GetContentRegionMax().X, showRetainer);

        var countText = $"×{needed}";
        ImGui.SetCursorPos(new Vector2(currentX + countWidth - ImGui.CalcTextSize(countText).X, textY));
        Ui.Text(countText, s.OnSurfaceVariant);

        var iconX = currentX + countWidth + M3.Space2;
        ImGui.SetCursorPos(new Vector2(iconX, rowY));
        M3Widgets.GameIcon(item.Icon, iconSize);

        var nameX = iconX + iconSize + M3.Space2;
        ImGui.SetCursorPos(new Vector2(nameX, textY));
        var name = item.Name.ExtractText();
        var shown = M3Navigation.Truncate(name, MathF.Max(8f, valueAreaStart - nameX - M3.Space1));
        Ui.Text(shown, s.OnSurface);
        if (shown != name)
        {
            M3Tooltip.Hovered(name);
        }

        var (nq, hq) = GetInventoryCountSplit(itemId);
        DrawIngredientValue(nq, valueAreaStart, colWidth, 0, textY, nq + hq >= needed ? s.Success : s.Error);
        DrawIngredientValue(hq, valueAreaStart, colWidth, 1, textY, s.Info);
        if (showRetainer)
        {
            DrawIngredientValue(GetRetainerItemCount(itemId), valueAreaStart, colWidth, 2, textY, s.Tertiary);
        }

        ImGui.SetCursorPos(new Vector2(currentX, rowY + iconSize + ImGui.GetStyle().ItemSpacing.Y));
        ImGui.Dummy(Vector2.Zero);
    }

    private static void DrawIngredientValue(int value, float valueAreaStart, float colWidth, int index, float textY, Vector4 color)
    {
        var text = value > 9999 ? "9999+" : value.ToString();
        ImGui.SetCursorPos(new Vector2(valueAreaStart + colWidth * index + (colWidth - ImGui.CalcTextSize(text).X) * 0.5f, textY));
        Ui.Text(text, value == 0 ? M3Text.ColorOf(M3TextStyle.Faint) : color);
    }

    private static float GetIngredientValueAreaStart(float currentX, float contentMaxX, bool showRetainer)
    {
        var colWidth = VulcanUiScaling.Scaled(44f);
        var countWidth = VulcanUiScaling.Scaled(36f);
        var iconSize = VulcanUiScaling.Scaled(24f);
        var desiredStart = contentMaxX - colWidth * (showRetainer ? 3 : 2);
        var minimumStart = currentX + countWidth + iconSize + M3.Space2 * 3;
        return Math.Max(desiredStart, minimumStart);
    }

    private static unsafe (int nq, int hq) GetInventoryCountSplit(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                return (0, 0);
            }

            var nq = (int)inventory->GetInventoryItemCount(itemId, false, false, false);
            var hq = (int)inventory->GetInventoryItemCount(itemId, true, false, false);
            return (nq, hq);
        }
        catch { return (0, 0); }
    }

    private static int GetRetainerItemCount(uint itemId)
    {
        if (!AllaganTools.Enabled)
        {
            return 0;
        }

        var now = DateTime.Now;
        if (RetainerIngredientRefreshTimes.TryGetValue(itemId, out var lastRefresh)
         && (now - lastRefresh).TotalSeconds < RetainerIngredientRefreshIntervalSeconds)
        {
            return CachedRetainerIngredientCounts.GetValueOrDefault(itemId, 0);
        }

        var count = RetainerItemQuery.GetTotalCount(itemId);
        CachedRetainerIngredientCounts[itemId] = count;
        RetainerIngredientRefreshTimes[itemId] = now;
        return count;
    }
}
