using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using ElliLib;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private void DrawFilterPanel()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##recipeSearch", "搜索...", ref _recipeSearchText, 256))
        {
            _filtersDirty = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "制作职业");
        ImGui.Spacing();

        var columns = 4;
        var buttonPad = 4f;
        var framePad = ImGui.GetStyle().FramePadding;
        var regionWidth = ImGui.GetContentRegionAvail().X;
        var btnSide = (regionWidth - (columns - 1) * buttonPad) / columns;
        var iconSide = btnSide - framePad.X * 2;
        if (iconSide < 16) iconSide = 16;
        if (iconSide > 26) { iconSide = 26; btnSide = iconSide + framePad.X * 2; }
        var iconSize = new Vector2(iconSide, iconSide);
        var selectedColor = new Vector4(0.25f, 0.50f, 0.85f, 1.00f);

        var isAllSelected = _selectedJobFilters.Count == 0;
        if (isAllSelected)
            ImGui.PushStyleColor(ImGuiCol.Button, selectedColor);
        
        ImGui.PushID("jobAll");
        if (ImGui.Button("全部", new Vector2(btnSide, btnSide)))
        {
            _selectedJobFilters.Clear();
            _filtersDirty = true;
        }
        ImGui.PopID();
        
        if (isAllSelected)
            ImGui.PopStyleColor();
        
        ImGui.SameLine(0, buttonPad);

        for (var i = 0; i < JobNames.Length; i++)
        {
            var classJobId = CraftTypeToClassJobId[i];
            var jobId = classJobId;
            var isSelected = _selectedJobFilters.Contains(jobId);
            var jobIconId = 62100 + classJobId;

            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Button, selectedColor);

            var wrap = Icons.DefaultStorage.TextureProvider
                .GetFromGameIcon(new GameIconLookup(jobIconId))
                .GetWrapOrDefault();

            var clicked = false;
            ImGui.PushID($"job{i}");
            if (wrap != null)
                clicked = ImGui.ImageButton(wrap.Handle, iconSize);
            else
                clicked = ImGui.Button(JobNames[i], new Vector2(iconSize.X + 8, iconSize.Y + 8));
            ImGui.PopID();

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text(JobNames[i]);
                ImGui.EndTooltip();
            }

            if (clicked)
            {
                if (_selectedJobFilters.Contains(jobId))
                    _selectedJobFilters.Remove(jobId);
                else
                    _selectedJobFilters.Add(jobId);
                _filtersDirty = true;
            }

            if (isSelected)
                ImGui.PopStyleColor();

            if ((i + 2) % columns != 0 && i < JobNames.Length - 1)
                ImGui.SameLine(0, buttonPad);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "等级范围");
        ImGui.Spacing();

        if (ImGui.Checkbox("物品装等", ref _filterByEquipLevel))
            _filtersDirty = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("按物品装备等级筛选和排序, 而不是按制作等级");
        ImGui.Spacing();

        var sliderWidth = 150f;
        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderInt("##minLevel", ref _minLevel, 1, 100, "最低: %d", ImGuiSliderFlags.AlwaysClamp))
        {
            _minLevel = Math.Clamp(_minLevel, 1, _maxLevel);
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("按住 Ctrl 并点击可直接输入数值");
        }

        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderInt("##maxLevel", ref _maxLevel, 1, 100, "最高: %d", ImGuiSliderFlags.AlwaysClamp))
        {
            _maxLevel = Math.Clamp(_maxLevel, _minLevel, 100);
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("按住 Ctrl 并点击可直接输入数值");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "筛选");
        ImGui.Spacing();

        if (ImGui.Checkbox("仅练级", ref _filterBrowserLevelingOnly))
        {
            if (_filterBrowserLevelingOnly)
            {
                _filterBrowserMasterRecipes = false;
                _filterBrowserHousingRecipes = false;
                _filterBrowserDyeRecipes = false;
                _filterBrowserCollectables = false;
                _filterBrowserExpertRecipes = false;
                _filterBrowserQuestRecipes = false;
            }
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只显示生产笔记中按等级分类的配方");
        
        if (ImGui.Checkbox("隐藏已制作", ref _hideCrafted))
        {
            _filtersDirty = true;
        }
        if (ImGui.Checkbox("房屋", ref _filterBrowserHousingRecipes))
        {
            if (_filterBrowserHousingRecipes)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只显示成品属于房屋类物品分类的配方");

        if (ImGui.Checkbox("染剂", ref _filterBrowserDyeRecipes))
        {
            if (_filterBrowserDyeRecipes)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只显示成品为染剂的配方");

        if (ImGui.Checkbox("收藏品", ref _filterBrowserCollectables))
        {
            if (_filterBrowserCollectables)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("秘籍", ref _filterBrowserMasterRecipes))
        {
            if (_filterBrowserMasterRecipes)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("高难度", ref _filterBrowserExpertRecipes))
        {
            if (_filterBrowserExpertRecipes)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("任务", ref _filterBrowserQuestRecipes))
        {
            if (_filterBrowserQuestRecipes)
                _filterBrowserLevelingOnly = false;
            _filtersDirty = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var count = _filteredRecipes?.Count ?? 0;
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"{count} 个配方");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var hasLists = GatherBuddy.CraftingListManager.Lists.Count > 0;
        using (ImRaii.Disabled(_filteredUncraftedRecipeCount == 0 || !hasLists))
        {
            if (ImGui.Button($"批量加入 {_filteredUncraftedRecipeCount} 个配方...", new Vector2(-1, 0)))
            {
                _bulkAddFilteredListSearch = string.Empty;
                ImGui.OpenPopup("BulkAddFilteredRecipesPopup");
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            const string description = "将当前筛选结果中所有尚未制作的配方一次性加入所选制作清单";
            if (!hasLists)
            {
                ImGui.SetTooltip($"{description}\n\n请先创建一个制作清单");
            }
            else if (_filteredUncraftedRecipeCount == 0)
            {
                ImGui.SetTooltip($"{description}\n\n当前筛选条件下没有尚未制作的配方");
            }
            else
            {
                ImGui.SetTooltip($"{description}");
            }
        }

        ImGui.SetNextWindowSize(new Vector2(320f, 0f), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("BulkAddFilteredRecipesPopup"))
        {
            ImGui.TextWrapped($"将当前筛选结果中 {_filteredUncraftedRecipeCount} 个尚未制作的配方批量加入:");
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##BulkAddFilteredListSearch", "搜索清单...", ref _bulkAddFilteredListSearch, 128);

            var filteredLists = string.IsNullOrWhiteSpace(_bulkAddFilteredListSearch)
                ? GatherBuddy.CraftingListManager.Lists.OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : GatherBuddy.CraftingListManager.Lists
                    .Where(list => list.Name.Contains(_bulkAddFilteredListSearch, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var rowH = ImGui.GetTextLineHeightWithSpacing();
            var popupHeight = filteredLists.Count > 0 ? Math.Min(filteredLists.Count * rowH, 180f) : rowH;
            ImGui.BeginChild("##BulkAddFilteredListScroll", new Vector2(0, popupHeight), true);
            if (filteredLists.Count == 0)
            {
                ImGui.TextDisabled("没有匹配项");
            }
            else
            {
                foreach (var list in filteredLists)
                {
                    if (ImGui.Selectable(list.Name))
                    {
                        AddFilteredRecipesToList(list);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            ImGui.EndChild();
            ImGui.EndPopup();
        }
    }

    private void AddFilteredRecipesToList(CraftingListDefinition list)
    {
        var addedCount = 0;
        if (_filteredRecipes != null)
        {
            foreach (var recipe in _filteredRecipes)
            {
                if (recipe.IsCrafted)
                    continue;

                list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, 1));
                addedCount++;
            }
        }

        if (addedCount == 0)
            return;

        GatherBuddy.CraftingListManager.SaveList(list);
        RefreshOpenCraftingList(list.ID);
        GatherBuddy.Log.Information($"[VulcanWindow] Added {addedCount} filtered uncrafted recipes to list '{list.Name}'");
        Communicator.Print($"已将 {addedCount} 个筛选出的未制作配方加入 '{list.Name}'");
    }

    private void DrawResultsList()
    {
        if (_filteredRecipes == null || _filteredRecipes.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "没有符合筛选条件的配方");
            return;
        }

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"  {_filteredRecipes.Count} 个配方");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 180);
        
        var sortLabel = _sortColumn switch
        {
            SortColumn.Level => _filterByEquipLevel ? "装备等级" : "等级",
            SortColumn.Crafted => "制作状态",
            _ => "排序"
        };
        var sortIcon = _sortDirection == SortDirection.Ascending ? FontAwesomeIcon.ArrowUp : FontAwesomeIcon.ArrowDown;
        
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "排序:");
        ImGui.SameLine();
        
        if (ImGui.Button($"{sortLabel}##sortBtn", new Vector2(90, 0)))
        {
            ImGui.OpenPopup("##sortMenu");
        }
        
        ImGui.SameLine(0, 4);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(sortIcon.ToIconString());
        }
        
        if (ImGui.BeginPopup("##sortMenu"))
        {
            if (ImGui.MenuItem("等级", "", _sortColumn == SortColumn.Level))
            {
                if (_sortColumn == SortColumn.Level)
                    _sortDirection = _sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
                else
                    _sortColumn = SortColumn.Level;
                _filtersDirty = true;
            }
            if (ImGui.MenuItem("制作状态", "", _sortColumn == SortColumn.Crafted))
            {
                if (_sortColumn == SortColumn.Crafted)
                    _sortDirection = _sortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
                else
                    _sortColumn = SortColumn.Crafted;
                _filtersDirty = true;
            }
            ImGui.EndPopup();
        }
        
        ImGui.Separator();

        var iconSm = new Vector2(28, 28);
        var jobIconSm = new Vector2(20, 20);
        const float rightGroupWidth = 70f;
        var contentMaxX = ImGui.GetContentRegionMax().X;
        var itemHeight = iconSm.Y + ImGui.GetStyle().ItemSpacing.Y;

        if (_pendingRecipeScrollId.HasValue)
        {
            var targetIndex = _filteredRecipes.FindIndex(r => r.Recipe.RowId == _pendingRecipeScrollId.Value);
            if (targetIndex >= 0)
            {
                var viewportHeight = ImGui.GetContentRegionAvail().Y;
                var targetScroll = Math.Max(0f, targetIndex * itemHeight - Math.Max(0f, (viewportHeight - itemHeight) * 0.5f));
                ImGui.SetScrollY(targetScroll);
            }
            else
            {
                _pendingRecipeScrollId = null;
            }
        }

        ElliLib.ImGuiClip.ClippedDraw(_filteredRecipes, recipe =>
        {
            var isSelected = _selectedRecipe?.Recipe.RowId == recipe.Recipe.RowId;
            var rowStartY = ImGui.GetCursorPosY();

            if (recipe.Icon.TryGetWrap(out var wrap, out _))
                ImGui.Image(wrap.Handle, iconSm);
            else
                ImGui.Dummy(iconSm);
            
            ImGui.SameLine(0, 4);

            var cursorY = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(cursorY + (iconSm.Y - ImGui.GetTextLineHeight()) / 2);

            var hasSettings = GatherBuddy.RecipeBrowserSettings.Has(recipe.Recipe.RowId);
            if (hasSettings)
            {
                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1), FontAwesomeIcon.Cog.ToIconString());
                ImGui.SameLine();
            }

            var label = $"{recipe.Name}##browse{recipe.Recipe.RowId}";
            if (ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.None, new Vector2(contentMaxX - ImGui.GetCursorPosX() - rightGroupWidth, 0)))
            {
                _selectedRecipe = recipe;
            }

            if (_pendingRecipeScrollId == recipe.Recipe.RowId)
            {
                ImGui.SetScrollHereY(0.5f);
                _pendingRecipeScrollId = null;
            }

            var isPopupOpen = GatherBuddy.ControllerSupport != null
                ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"RecipeContextMenu##{recipe.Recipe.RowId}", Dalamud.GamepadState)
                : ImGui.BeginPopupContextItem($"RecipeContextMenu##{recipe.Recipe.RowId}");
            
            if (isPopupOpen)
            {
                if (ImGui.MenuItem("显示配方属性 (调试)"))
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
                
                ImGui.Separator();

                var lists = GatherBuddy.CraftingListManager.Lists;

                if (ImGui.IsWindowAppearing())
                {
                    _contextMenuListSearch      = string.Empty;
                    _contextMenuAddQuantity     = 1;
                    _contextMenuNewListName     = string.Empty;
                    _contextMenuNewListEphemeral = false;
                }

                ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "创建新清单:");
                ImGui.SetNextItemWidth(-1);
                var createEnter = ImGui.InputTextWithHint("##NewListName", "清单名称...", ref _contextMenuNewListName, 128, ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.Checkbox("临时##ctxNewListEphemeral", ref _contextMenuNewListEphemeral);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("制作完成后自动删除此清单\n可稍后在清单编辑器中关闭");
                if ((ImGui.Button("创建并添加", new Vector2(-1, 0)) || createEnter) && !string.IsNullOrWhiteSpace(_contextMenuNewListName))
                {
                    var newList = GatherBuddy.CraftingListManager.CreateNewList(_contextMenuNewListName.Trim(), _contextMenuNewListEphemeral);
                    newList.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
                    GatherBuddy.CraftingListManager.SaveList(newList);
                    RefreshOpenCraftingList(newList.ID);
                    GatherBuddy.Log.Information($"[VulcanWindow] Created list '{newList.Name}' and added {recipe.Name} x{_contextMenuAddQuantity}");
                    Communicator.Print($"已创建 '{newList.Name}' 并添加 {recipe.Name} x{_contextMenuAddQuantity}");
                    ImGui.CloseCurrentPopup();
                }

                if (lists.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.Separator();

                    var filteredLists = string.IsNullOrWhiteSpace(_contextMenuListSearch)
                        ? lists
                        : lists.Where(l => l.Name.Contains(_contextMenuListSearch, StringComparison.OrdinalIgnoreCase)).ToList();

                    var rowH = ImGui.GetTextLineHeightWithSpacing();

                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.6f, 1.0f), $"将 {recipe.Name} 加入清单:");
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("数量:");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(100);
                    ImGui.InputInt("##ContextQty", ref _contextMenuAddQuantity, 1);
                    if (_contextMenuAddQuantity < 1) _contextMenuAddQuantity = 1;
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##ContextListSearch", "搜索清单...", ref _contextMenuListSearch, 128);

                    var singleH = filteredLists.Count > 0 ? Math.Min(filteredLists.Count * rowH, 150f) : rowH;
                    ImGui.BeginChild("##SingleAddScroll", new Vector2(0, singleH), true);
                    if (filteredLists.Count == 0)
                        ImGui.TextDisabled("无匹配");
                    foreach (var list in filteredLists)
                    {
                        if (ImGui.MenuItem(list.Name))
                        {
                            list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
                            GatherBuddy.CraftingListManager.SaveList(list);
                            RefreshOpenCraftingList(list.ID);
                            GatherBuddy.Log.Information($"Added {recipe.Name} x{_contextMenuAddQuantity} to crafting list '{list.Name}'");
                            Communicator.Print($"已将 {recipe.Name} x{_contextMenuAddQuantity} 加入 '{list.Name}'");
                            _contextMenuLastAddedList = list.Name;
                            _contextMenuLastAddedAt   = DateTime.Now;
                        }
                    }
                    ImGui.EndChild();

                    if (_contextMenuLastAddedList != null)
                    {
                        var elapsed = (DateTime.Now - _contextMenuLastAddedAt).TotalSeconds;
                        if (elapsed < 1.5)
                        {
                            var alpha = (float)(1.0 - elapsed / 1.5);
                            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, alpha), $"已加入 '{_contextMenuLastAddedList}'");
                        }
                        else
                        {
                            _contextMenuLastAddedList = null;
                        }
                    }

                }
                else
                {
                    ImGui.TextDisabled("没有可用制作清单");
                }

                ImGui.EndPopup();
            }

            ImGui.SetCursorPosX(contentMaxX - rightGroupWidth);
            ImGui.SetCursorPosY(rowStartY + (iconSm.Y - ImGui.GetTextLineHeight()) / 2);
            
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (recipe.IsCrafted)
                {
                    ImGui.TextColored(new Vector4(0.0f, 0.5f, 0.0f, 1), FontAwesomeIcon.Check.ToIconString());
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.0f, 0.0f, 1), FontAwesomeIcon.Times.ToIconString());
                }
            }
            
            ImGui.SameLine(0, 4);
            ImGui.SetCursorPosY(rowStartY + (iconSm.Y - jobIconSm.Y) / 2);
            
            var jobIconId = 62100 + CraftTypeToClassJobId[recipe.Recipe.CraftType.RowId];
            var jobWrap = Icons.DefaultStorage.TextureProvider
                .GetFromGameIcon(new GameIconLookup(jobIconId))
                .GetWrapOrDefault();
            if (jobWrap != null)
                ImGui.Image(jobWrap.Handle, jobIconSm);
            
            ImGui.SameLine(0, 2);
            ImGui.SetCursorPosY(rowStartY + (iconSm.Y - ImGui.GetTextLineHeight()) / 2);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), _filterByEquipLevel ? $"{recipe.ItemEquipLevel}" : $"{recipe.Level}");
            ImGui.SetCursorPosY(rowStartY + itemHeight);
        }, itemHeight);
    }

    private void DrawDetailsPanel()
    {
        if (_selectedRecipe == null)
        {
            var center = ImGui.GetContentRegionAvail();
            ImGui.SetCursorPos(new Vector2(12, center.Y / 2 - 20));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "选择配方以查看详情");
            ImGui.SetCursorPosX(12);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "并开始制作");
            return;
        }

        var recipe = _selectedRecipe;

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(new Vector4(0.65f, 0.65f, 0.65f, 1.0f), $"配方 ID: {recipe.Recipe.RowId}");
        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        if (recipe.Icon.TryGetWrap(out var wrap, out _))
            ImGui.Image(wrap.Handle, new Vector2(48, 48));
        else
            ImGui.Dummy(new Vector2(48, 48));
        
        ImGui.SameLine(0, 12);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (48 - ImGui.GetTextLineHeight()) / 2);
        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.6f, 1.0f), recipe.Name);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        
        var r = recipe.Recipe;
        if (r.SecretRecipeBook.RowId > 0)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 1.0f, 1.0f), "[秘籍]");
            ImGui.SameLine();
        }
        if (r.ItemResult.Value.AlwaysCollectable)
        {
            ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.9f, 1.0f), "[收藏品]");
            ImGui.SameLine();
        }
        if (r.IsExpert)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "[专家]");
            ImGui.SameLine();
        }
        if (r.ItemResult.Value.ItemSearchCategory.RowId == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.9f, 1.0f, 1.0f), "[任务]");
            ImGui.SameLine();
        }
        if (recipe.IsCrafted)
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f), "[已制作]");
            ImGui.SameLine();
        }
        ImGui.NewLine();

        ImGui.Spacing();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var classLineY = ImGui.GetCursorPosY();
        var classMidY  = classLineY + (24 - ImGui.GetTextLineHeight()) / 2;
        var jobIconId  = 62100 + CraftTypeToClassJobId[r.CraftType.RowId];
        var jobWrap    = Icons.DefaultStorage.TextureProvider
            .GetFromGameIcon(new GameIconLookup(jobIconId))
            .GetWrapOrDefault();
        ImGui.SetCursorPosY(classMidY);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "职业:");
        ImGui.SameLine();
        ImGui.SetCursorPosY(classLineY);
        if (jobWrap != null)
            ImGui.Image(jobWrap.Handle, new Vector2(24, 24));
        ImGui.SameLine(0, 2);
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), recipe.JobAbbreviation);
        ImGui.SameLine(0, 16);
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "等级:");
        ImGui.SameLine();
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), recipe.Level.ToString());
        ImGui.SameLine(0, 16);
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "产量:");
        ImGui.SameLine();
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), r.AmountResult.ToString());

        ImGui.Spacing();
        var lt = r.RecipeLevelTable.Value;
        var difficulty = (int)(lt.Difficulty * r.DifficultyFactor / 100);
        var qualityMax  = (int)(lt.Quality    * r.QualityFactor    / 100);
        var durability  = (int)(lt.Durability  * r.DurabilityFactor  / 100);
        var statLabelColor = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
        var statValueColor = new Vector4(0.8f, 0.9f, 1.0f, 1.0f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(statLabelColor, "难度:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(statValueColor, $"{difficulty}"); ImGui.SameLine(0, 16);
        ImGui.TextColored(statLabelColor, "耐久:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(statValueColor, $"{durability}"); ImGui.SameLine(0, 16);
        ImGui.TextColored(statLabelColor, "最高品质:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(statValueColor, $"{qualityMax}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var directIngredients = RecipeManager.GetIngredients(r);
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        var showRetainer = AllaganTools.Enabled;

        DrawIngredientSectionHeader("材料", showRetainer);

        var craftable = directIngredients.Count > 0 ? int.MaxValue : 0;
        foreach (var (ingId, ingAmt) in directIngredients)
        {
            if (ingAmt <= 0) continue;
            var (ingNq, ingHq) = GetInventoryCountSplit(ingId);
            craftable = Math.Min(craftable, (ingNq + ingHq) / ingAmt);
        }
        if (craftable == int.MaxValue) craftable = 0;

        foreach (var (itemId, needed) in directIngredients)
        {
            if (itemSheet == null || !itemSheet.TryGetRow(itemId, out var item))
                continue;
            DrawIngredientRow(itemId, needed, item, showRetainer);
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var (resultNq, resultHq) = GetInventoryCountSplit(r.ItemResult.RowId);
        var bagTotal = resultNq + resultHq;
        ImGui.TextColored(statLabelColor, "可制作:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(craftable > 0 ? statValueColor : new Vector4(1f, 0.4f, 0.4f, 1f), $"{craftable}");
        ImGui.SameLine(0, 16);
        ImGui.TextColored(statLabelColor, "背包中:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(bagTotal > 0 ? statValueColor : new Vector4(0.5f, 0.5f, 0.5f, 1f),
            resultHq > 0 ? $"{resultNq}+{resultHq} HQ" : $"{resultNq}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawIngredientSectionHeader("全部材料 (含半成品)", showRetainer);

        var resolvedIngredients = RecipeManager.GetResolvedIngredients(r);
        foreach (var (itemId, needed) in resolvedIngredients.OrderBy(x => x.Key))
        {
            if (itemSheet == null || !itemSheet.TryGetRow(itemId, out var item))
                continue;
            DrawIngredientRow(itemId, needed, item, showRetainer);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        var settings = GatherBuddy.RecipeBrowserSettings.Get(recipe.Recipe.RowId);
        if (settings != null && settings.HasAnySettings())
        {
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1.0f), "已配置设置:");
            ImGui.Spacing();
            
            if (itemSheet != null)
            {
                if (settings.FoodItemId.HasValue && itemSheet.TryGetRow(settings.FoodItemId.Value, out var food))
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 24);
                    ImGui.Text($"食物: {food.Name.ExtractText()}");
                }
                if (settings.MedicineItemId.HasValue && itemSheet.TryGetRow(settings.MedicineItemId.Value, out var medicine))
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 24);
                    ImGui.Text($"爆发药: {medicine.Name.ExtractText()}");
                }
                if (settings.ManualItemId.HasValue && itemSheet.TryGetRow(settings.ManualItemId.Value, out var manual))
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 24);
                    ImGui.Text($"指南: {manual.Name.ExtractText()}");
                }
            }
            ImGui.Spacing();
        }

        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, avail.Y - 96));

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "数量:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("##browserQty", ref _browserCraftQuantity, 1);
        if (_browserCraftQuantity < 1) _browserCraftQuantity = 1;

        ImGui.SameLine();
        var allaganEnabled = AllaganTools.Enabled;
        if (!allaganEnabled)
            _browserRetainerRestock = false;
        using (ImRaii.Disabled(!allaganEnabled))
            ImGui.Checkbox("从雇员补货##browserRestock", ref _browserRetainerRestock);
        if (ImGui.IsItemHovered(allaganEnabled ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(allaganEnabled
                ? "制作前自动从雇员处取出缺少的材料"
                : "需要安装 AllaganTools (InventoryTools) 插件");

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var topRowButtonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        var artisanLoaded = IPCSubscriber.IsReady("Artisan");
        if (artisanLoaded)
        {
            ImGuiUtil.DrawDisabledButton("检测到 Artisan", new Vector2(topRowButtonWidth, 22),
                "Artisan 插件已加载, 请卸载 Artisan 后使用 Vulcan 制作系统", true);
        }
        else if (ImGui.Button("开始制作"))
        {
            StartBrowserCraft(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }
        ImGui.SameLine();
        if (ImGui.Button("设置"))
            _craftSettingsPopup.Open(recipe.Recipe.RowId, recipe.Name);

        var canQuickSynth = recipe.Recipe.CanQuickSynth;
        var qsTooltip = artisanLoaded
            ? "Artisan 插件已加载, 请卸载 Artisan 后使用 Vulcan 制作系统"
            : canQuickSynth
                ? $"简易制作 {recipe.Name} x{_browserCraftQuantity}"
                : "此配方无法简易制作";
        ImGui.SameLine();
        if (ImGuiUtil.DrawDisabledButton("简易制作", new Vector2(-1, ImGui.GetFrameHeight()), qsTooltip, !canQuickSynth || artisanLoaded))
        {
            StartBrowserQuickSynth(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }
    }

    private static void DrawIngredientSectionHeader(string title, bool showRetainer)
    {
        const float colWidth = 40f;
        var currentX    = ImGui.GetCursorPosX();
        var headerY     = ImGui.GetCursorPosY();
        var contentMaxX = ImGui.GetContentRegionMax().X;
        var valueAreaStart = GetIngredientValueAreaStart(currentX, contentMaxX, showRetainer);
        var nqColStart  = valueAreaStart;
        var hqColStart  = valueAreaStart + colWidth;

        var titleStartX   = currentX + 12f;
        var titleMaxWidth = valueAreaStart - titleStartX - 8f;
        title = TruncateTextToWidth(title, titleMaxWidth);
        ImGui.SetCursorPosX(titleStartX);
        if (title.Length > 0)
            DrawClippedText(title, titleMaxWidth, new Vector4(0.7f, 0.9f, 1.0f, 1.0f));
        else
            ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight()));

        var colHeaderColor = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);

        var nqW = ImGui.CalcTextSize("NQ").X;
        ImGui.SetCursorPosX(nqColStart + (colWidth - nqW) / 2);
        ImGui.SetCursorPosY(headerY);
        ImGui.TextColored(colHeaderColor, "NQ");

        var hqW = ImGui.CalcTextSize("HQ").X;
        ImGui.SetCursorPosX(hqColStart + (colWidth - hqW) / 2);
        ImGui.SetCursorPosY(headerY);
        ImGui.TextColored(colHeaderColor, "HQ");

        if (showRetainer)
        {
            var retColStart = valueAreaStart + colWidth * 2;
            var retW = ImGui.CalcTextSize("雇").X;
            ImGui.SetCursorPosX(retColStart + (colWidth - retW) / 2);
            ImGui.SetCursorPosY(headerY);
            ImGui.TextColored(colHeaderColor, "雇");
        }

        ImGui.Spacing();
    }

    private static void DrawIngredientRow(uint itemId, int needed, Item item, bool showRetainer)
    {
        const float colWidth    = 40f;
        const float xnWidth     = 32f;
        const float iconSize    = 24f;
        const float xnIconGap   = 4f;
        const float iconNameGap = 6f;
        var currentX    = ImGui.GetCursorPosX();
        var rowStartX   = currentX + 12;
        var rowY        = ImGui.GetCursorPosY();
        var textY       = rowY + (iconSize - ImGui.GetTextLineHeight()) / 2;
        var contentMaxX = ImGui.GetContentRegionMax().X;
        var valueAreaStart = GetIngredientValueAreaStart(currentX, contentMaxX, showRetainer);
        var nqColStart  = valueAreaStart;
        var hqColStart  = valueAreaStart + colWidth;

        var xnText  = $"\u00d7{needed}";
        var xnTextW = ImGui.CalcTextSize(xnText).X;
        ImGui.SetCursorPosX(rowStartX + (xnWidth - xnTextW) / 2);
        ImGui.SetCursorPosY(textY);
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), xnText);

        var iconX = rowStartX + xnWidth + xnIconGap;
        ImGui.SetCursorPosX(iconX);
        ImGui.SetCursorPosY(rowY);
        var icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
        if (icon.TryGetWrap(out var ingWrap, out _))
            ImGui.Image(ingWrap.Handle, new Vector2(iconSize, iconSize));
        else
            ImGui.Dummy(new Vector2(iconSize, iconSize));

        var nameStartX   = iconX + iconSize + iconNameGap;
        var nameMaxWidth = valueAreaStart - nameStartX - 6f;
        ImGui.SetCursorPosX(nameStartX);
        ImGui.SetCursorPosY(textY);
        var name = TruncateTextToWidth(item.Name.ExtractText(), nameMaxWidth);
        if (name.Length > 0)
            DrawClippedText(name, nameMaxWidth, new Vector4(0.85f, 0.85f, 0.85f, 1.0f));

        var (nq, hq) = GetInventoryCountSplit(itemId);
        var total     = nq + hq;
        var haveColor = total >= needed ? new Vector4(0.3f, 1.0f, 0.3f, 1.0f) : new Vector4(1.0f, 0.5f, 0.5f, 1.0f);

        var nqStr = nq > 9999 ? "9999+" : $"{nq}";
        ImGui.SetCursorPosX(nqColStart + (colWidth - ImGui.CalcTextSize(nqStr).X) / 2);
        ImGui.SetCursorPosY(textY);
        ImGui.TextColored(haveColor, nqStr);

        var hqStr = hq > 9999 ? "9999+" : $"{hq}";
        ImGui.SetCursorPosX(hqColStart + (colWidth - ImGui.CalcTextSize(hqStr).X) / 2);
        ImGui.SetCursorPosY(textY);
        ImGui.TextColored(new Vector4(0.6f, 0.85f, 1.0f, 1.0f), hqStr);

        if (showRetainer)
        {
            var retColStart = valueAreaStart + colWidth * 2;
            var retCount    = GetRetainerItemCount(itemId);
            var retStr      = retCount > 9999 ? "9999+" : $"{retCount}";
            ImGui.SetCursorPosX(retColStart + (colWidth - ImGui.CalcTextSize(retStr).X) / 2);
            ImGui.SetCursorPosY(textY);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), retStr);
        }

        ImGui.SetCursorPosY(rowY + iconSize + ImGui.GetStyle().ItemSpacing.Y);
    }

    private static string TruncateTextToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return string.Empty;

        var ellipsis = "...";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        if (maxWidth <= ellipsisWidth)
            return string.Empty;

        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        while (text.Length > 0 && ImGui.CalcTextSize(text + ellipsis).X > maxWidth)
            text = text[..^1];

        return text.Length == 0 ? string.Empty : text + ellipsis;
    }

    private static void DrawClippedText(string text, float maxWidth, Vector4 color)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return;

        var clipMin = ImGui.GetCursorScreenPos();
        var clipMax = new Vector2(clipMin.X + maxWidth, clipMin.Y + ImGui.GetTextLineHeight());
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(clipMin, clipMax, true);
        ImGui.TextColored(color, text);
        drawList.PopClipRect();
    }

    private static float GetIngredientValueAreaStart(float currentX, float contentMaxX, bool showRetainer)
    {
        const float leftIndent = 12f;
        const float colWidth = 40f;
        const float xnWidth = 32f;
        const float xnIconGap = 4f;
        const float iconSize = 24f;
        const float iconNameGap = 6f;
        const float minGapBeforeValues = 6f;

        var valueColumnCount = showRetainer ? 3 : 2;
        var desiredStart = contentMaxX - colWidth * valueColumnCount;
        var minimumStart = currentX + leftIndent + xnWidth + xnIconGap + iconSize + iconNameGap + minGapBeforeValues;
        return Math.Max(desiredStart, minimumStart);
    }

    private static unsafe (int nq, int hq) GetInventoryCountSplit(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null) return (0, 0);
            var nq = (int)inventory->GetInventoryItemCount(itemId, false, false, false);
            var hq = (int)inventory->GetInventoryItemCount(itemId, true,  false, false);
            return (nq, hq);
        }
        catch { return (0, 0); }
    }

    private static int GetRetainerItemCount(uint itemId)
    {
        if (!AllaganTools.Enabled)
            return 0;

        var now = DateTime.Now;
        if (RetainerIngredientRefreshTimes.TryGetValue(itemId, out var lastRefresh)
         && (now - lastRefresh).TotalSeconds < RetainerIngredientRefreshIntervalSeconds)
            return CachedRetainerIngredientCounts.GetValueOrDefault(itemId, 0);

        var count = RetainerItemQuery.GetTotalCount(itemId);
        CachedRetainerIngredientCounts[itemId] = count;
        RetainerIngredientRefreshTimes[itemId] = now;
        return count;
    }

}
