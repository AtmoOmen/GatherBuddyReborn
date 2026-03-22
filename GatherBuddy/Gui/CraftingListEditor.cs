using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Lumina.Excel.Sheets;
using ElliLib;
using ElliLib.Raii;
using ElliLib.Widgets;
using ImRaii = ElliLib.Raii.ImRaii;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan;

namespace GatherBuddy.Gui;

public class CraftingListEditor
{
    private CraftingListDefinition _list;
    private int _searchQuantity = 1;
    private Recipe? _selectedRecipe = null;
    private Dictionary<uint, string> _recipeLabels = new();
    private bool _showMaterials = true;
    private ClippedSelectableCombo<Recipe>? _recipeCombo = null;
    private List<Recipe> _allRecipes = new();
    private List<Recipe> _keywordFilteredRecipes = new();
    private string _lastComboFilter = string.Empty;
    
    private List<CraftingListItem>? _cachedSortedQueue = null;
    private int _cachedRecipeCount = -1;
    private bool _cachedQueueValid = false;
    private string _cachedListHash = string.Empty;
    private int _selectedQueueIndex = -1;
    private bool _showPrecrafts = true;
    
    private Dictionary<uint, int>? _cachedMaterials = null;
    private string _cachedMaterialsHash = string.Empty;
    private bool _cachedMaterialsValid = false;
    
    private Task? _queueGenerationTask = null;
    private CancellationTokenSource? _queueCancellationSource = null;
    private bool _isGeneratingQueue = false;
    
    private Task? _materialsGenerationTask = null;
    private CancellationTokenSource? _materialsCancellationSource = null;
    private bool _isGeneratingMaterials = false;
    
    private Dictionary<uint, int> _cachedInventoryCounts = new();
    private Dictionary<uint, DateTime> _inventoryRefreshTimes = new();
    private Dictionary<uint, int> _cachedRetainerCounts = new();
    private Dictionary<uint, DateTime> _retainerRefreshTimes = new();
    private const double InventoryRefreshIntervalSeconds = 0.5;
    
    private RecipeCraftSettingsPopup _craftSettingsPopup = new();
    private CraftingListConsumablesPopup _consumablesPopup = new();
    
    private int _editingQuantityIndex = -1;
    private int _tempQuantityInput = 0;
    private Dictionary<uint, int>? _cachedPrecraftMaterials = null;
    private string _cachedPrecraftMaterialsHash = string.Empty;

    private string _editingName        = string.Empty;
    private string _editingDescription = string.Empty;
    private bool   _nameConflict       = false;
    private bool   _editingDescActive  = false;
    private bool   _focusDescNext      = false;
    
    internal bool HasCachedMaterials    => _cachedMaterials != null;
    internal bool IsGeneratingMaterials => _isGeneratingMaterials;
    internal string ListName            => _list.Name;
    
    public Action<CraftingListDefinition>? OnStartCrafting { get; set; }

    public CraftingListEditor(CraftingListDefinition list)
    {
        _list               = list;
        _editingName        = list.Name;
        _editingDescription = list.Description;
        RefreshInventoryCounts();
        TriggerQueueRegeneration();
    }
    
    public void Dispose()
    {
        _queueCancellationSource?.Cancel();
        _queueCancellationSource?.Dispose();
        _materialsCancellationSource?.Cancel();
        _materialsCancellationSource?.Dispose();
    }
    
    public void RefreshInventoryCounts()
    {
        _cachedInventoryCounts.Clear();
        _inventoryRefreshTimes.Clear();
        _cachedRetainerCounts.Clear();
        _retainerRefreshTimes.Clear();
    }
    public void Draw()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        
        var leftPaneWidth = availableWidth * 0.4f;
        var rightPaneWidth = availableWidth - leftPaneWidth - 8;
        
        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("LeftPane", new Vector2(leftPaneWidth, availableHeight), true);
            DrawQueuePane();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("RightPane", new Vector2(rightPaneWidth, availableHeight), true);
            DrawDetailsPane();
            ImGui.EndChild();
        }
        
        _craftSettingsPopup.Draw();
        _consumablesPopup.Draw();
    }

    private void DrawQueuePane()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow, "制作队列");
        ImGui.Separator();
        ImGui.Spacing();

        if (_list.Recipes.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "队列中无配方。");
            ImGui.Spacing();
            ImGui.TextWrapped("请使用右侧面板添加配方。");
            return;
        }

        var sortedQueue  = GetSortedQueue();
        var displayQueue = _showPrecrafts
            ? sortedQueue
            : _list.Recipes.Select(r => new CraftingListItem(r.RecipeId, r.Quantity)).ToList();

        var lineH   = ImGui.GetTextLineHeightWithSpacing();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var bottomH = lineH * 3 + spacing * 3    // 3 checkboxes
                    + 22f * 2  + spacing * 2     // Start + gather/materials row
                    + spacing * 2 + 6f;          // separator + padding
        var queueH  = Math.Max(ImGui.GetContentRegionAvail().Y - bottomH, lineH * 3);

        ImGui.BeginChild("QueueList", new Vector2(-1, queueH), false);

        if (_isGeneratingQueue)
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "正在计算制作队列...");
        }
        else if (displayQueue.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "队列为空");
        }
        else
        {
            var originalRecipes = new HashSet<uint>(_list.Recipes.Select(r => r.RecipeId));

            void DrawQueueItem(int idx)
            {
                var queueItem  = displayQueue[idx];
                var recipeData = RecipeManager.GetRecipe(queueItem.RecipeId);
                if (recipeData == null) return;

                var itemName = recipeData.Value.ItemResult.Value.Name.ExtractText();
                var jobName  = GetCraftingJobName(recipeData.Value.CraftType.RowId);

                var isOriginalRecipe = originalRecipes.Contains(queueItem.RecipeId);
                var willBeSkipped    = _list.SkipIfEnough && WillBeSkippedDueToInventory(recipeData.Value, queueItem.Quantity);
                var recipeOptions    = _list.GetRecipeOptions(queueItem.RecipeId);
                var quickSynthPrefix = recipeOptions.NQOnly ? "[简易制作] " : "";

                Vector4 textColor;
                if (willBeSkipped)
                    textColor = new Vector4(1, 0.3f, 0.3f, 1);
                else if (recipeOptions.NQOnly)
                    textColor = new Vector4(0.3f, 0.9f, 0.9f, 1);
                else if (isOriginalRecipe)
                    textColor = new Vector4(1, 1, 1, 1);
                else
                    textColor = new Vector4(0.7f, 0.7f, 0.7f, 1);

                var queueItemCraftSettings = isOriginalRecipe
                    ? _list.Recipes.FirstOrDefault(r => r.RecipeId == queueItem.RecipeId)?.CraftSettings
                    : _list.PrecraftCraftSettings.GetValueOrDefault(queueItem.RecipeId);
                var queueItemValidation = MacroValidator.GetOrCompute(queueItem.RecipeId, ResolveEffectiveMacroId(queueItemCraftSettings, !isOriginalRecipe), queueItemCraftSettings, _list.Consumables);
                if (queueItemValidation != null)
                {
                    var dotColor = queueItemValidation.IsValid
                        ? new Vector4(0.30f, 0.70f, 0.30f, 1f)
                        : (queueItemValidation.Failure is MacroValidationFailure.InsufficientProgress or MacroValidationFailure.ActionUnusable
                            ? new Vector4(0.78f, 0.62f, 0.15f, 1f)
                            : new Vector4(0.78f, 0.25f, 0.25f, 1f));
                    ImGui.TextColored(dotColor, "\u25cf");
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(queueItemValidation.IsValid
                            ? $"宏: 通过\n进展: {queueItemValidation.FinalProgress}/{queueItemValidation.RequiredProgress}\n品质: {queueItemValidation.FinalQuality}\n耐久: {queueItemValidation.FinalDurability}"
                            : $"宏: {queueItemValidation.Failure} 于步骤 {queueItemValidation.FailedAtStep}\n进展: {queueItemValidation.FinalProgress}/{queueItemValidation.RequiredProgress}");
                    ImGui.SameLine();
                }

                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                var isSelected = _selectedQueueIndex == idx;
                var label      = $"{quickSynthPrefix}{idx + 1}. {itemName} x{queueItem.Quantity} ({jobName})";
                if (ImGui.Selectable(label, isSelected))
                    _selectedQueueIndex = idx;
                ImGui.PopStyleColor();

                var isPopupOpen = GatherBuddy.ControllerSupport != null
                    ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"queue_ctx_{idx}", Dalamud.GamepadState)
                    : ImGui.BeginPopupContextItem($"queue_ctx_{idx}");

                if (isPopupOpen)
                {
                    if (ImGui.MenuItem("制作设置..."))
                    {
                        if (isOriginalRecipe)
                        {
                            var listItem = _list.Recipes.FirstOrDefault(r => r.RecipeId == queueItem.RecipeId);
                            if (listItem != null)
                                _craftSettingsPopup.OpenForListItem(listItem, _list, itemName);
                        }
                        else
                        {
                            _craftSettingsPopup.OpenForPrecraft(queueItem.RecipeId, itemName, _list);
                        }
                    }

                    ImGui.Separator();

                    if (recipeData.Value.CanQuickSynth)
                    {
                        var useQuickSynth = recipeOptions.NQOnly;
                        if (ImGui.MenuItem("简易制作", "", useQuickSynth))
                        {
                            _list.SetRecipeQuickSynth(queueItem.RecipeId, !useQuickSynth);
                            GatherBuddy.CraftingListManager.SaveList(_list);
                            _cachedQueueValid    = false;
                            _cachedMaterialsValid = false;
                            TriggerQueueRegeneration();
                        }
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("为此配方使用简易制作 (仅普通品质)");
                    }
                    else
                    {
                        ImGui.TextDisabled("简易制作不可用");
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("配方必须解锁并至少制作过一次才能使用简易制作");
                    }

                    ImGui.EndPopup();
                }
            }

            for (int i = 0; i < displayQueue.Count; i++)
                DrawQueueItem(i);
        }

        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("显示半成品制作##sp", ref _showPrecrafts);

        var skipIfEnough = _list.SkipIfEnough;
        if (ImGui.Checkbox("跳过已经足够的物品##sie", ref skipIfEnough))
        {
            _list.SkipIfEnough    = skipIfEnough;
            _cachedQueueValid     = false;
            _cachedMaterialsValid = false;
            GatherBuddy.CraftingListManager.SaveList(_list);
            TriggerQueueRegeneration();
            RefreshInventoryCounts();
        }

        var quickSynthAll = _list.QuickSynthAll;
        if (ImGui.Checkbox("简易制作全部##qsa", ref quickSynthAll))
        {
            _list.QuickSynthAll = quickSynthAll;
            GatherBuddy.CraftingListManager.SaveList(_list);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("强制清单中所有可用物品使用简易制作, 覆盖每个物品的求解器设置。");

        var allaganEnabled = AllaganTools.Enabled;
        using (ImRaii.Disabled(!allaganEnabled))
        {
            var retainerRestock = _list.RetainerRestock;
            if (ImGui.Checkbox("从雇员补货##rrr", ref retainerRestock))
            {
                _list.RetainerRestock = retainerRestock;
                GatherBuddy.CraftingListManager.SaveList(_list);
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(allaganEnabled
                ? "在生成自动采集列表前, 从雇员处取出需要的材料, 遵循 HQ/NQ 偏好设置。"
                : "需要安装并启用 Allagan Tools 插件。");

        ImGui.Spacing();

        if (IPCSubscriber.IsReady("Artisan"))
        {
            ImGuiUtil.DrawDisabledButton("检测到 Artisan", new Vector2(-1, 22),
                "Artisan 插件已加载, 请卸载 Artisan 以使用 Vulcan 的制作系统。", true);
        }
        else
        {
            var (hardFails, warnings) = CountValidationIssues();
            if (hardFails > 0)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.50f, 0.15f, 0.15f, 1f));
            else if (warnings > 0)
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.40f, 0.05f, 1f));

            if (ImGui.Button("开始 采集/制作", new Vector2(-1, 22)))
            {
                if (hardFails > 0)
                    ImGui.OpenPopup("ConfirmFailedMacros##startCraft");
                else
                    OnStartCrafting?.Invoke(_list);
            }

            if (hardFails > 0 || warnings > 0)
            {
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(hardFails > 0
                        ? $"{hardFails} 个宏预计会在本次制作中失败, 点击确认仍然开始。"
                        : $"{warnings} 个宏存在警告。");
            }

            if (ImGui.BeginPopupModal("ConfirmFailedMacros##startCraft", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextColored(new Vector4(0.78f, 0.25f, 0.25f, 1f), $"{hardFails} 个宏预计会制作失败。");
                ImGui.TextWrapped("这些物品可能无法完成, 是否仍要开始制作?");
                ImGui.Spacing();
                if (ImGui.Button("仍然开始", new Vector2(120, 0)))
                {
                    OnStartCrafting?.Invoke(_list);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("取消", new Vector2(80, 0)))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }

        var halfW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (ImGui.Button("生成自动采集列表##gatherList", new Vector2(halfW, 22)))
        {
            var materials = _list.ListMaterials();
            CraftingGatherBridge.CreatePersistentGatherList($"{_list.Name}...Auto-Generated", materials);
        }
        ImGui.SameLine();
        var matsBtnLabel = GatherBuddy.CraftingMaterialsWindow?.IsOpen == true ? "隐藏材料表" : "查看材料表";
        if (ImGui.Button($"{matsBtnLabel}##viewMats", new Vector2(-1, 22)) && GatherBuddy.CraftingMaterialsWindow != null)
            GatherBuddy.CraftingMaterialsWindow.IsOpen = !GatherBuddy.CraftingMaterialsWindow.IsOpen;
    }
    
    private void DrawDetailsPane()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow, "制作清单详情");
        ImGui.Separator();
        ImGui.Spacing();
        DrawListInfoSection();

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, "制作清单消耗品");
        ImGui.Separator();
        ImGui.Spacing();
        DrawListConsumablesSection();

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, "添加配方");
        ImGui.Separator();
        ImGui.Spacing();
        DrawAddRecipeSection();

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, "配方清单");
        ImGui.Separator();
        ImGui.Spacing();
        DrawRecipeListSection();
        
    }

    private void DrawListInfoSection()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##listName", ref _editingName, 128))
            _nameConflict = false;

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            var trimmed = _editingName.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _editingName = _list.Name;
            }
            else if (GatherBuddy.CraftingListManager.IsNameUnique(trimmed, _list.ID))
            {
                _list.Name   = trimmed;
                _editingName = trimmed;
                GatherBuddy.CraftingListManager.SaveList(_list);
                GatherBuddy.Log.Debug($"[CraftingListEditor] Renamed list to '{trimmed}'");
            }
            else
            {
                _nameConflict = true;
            }
        }

        if (_nameConflict)
            ImGui.TextColored(ImGuiColors.DalamudRed, "已存在同名的制作清单。");

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudGrey3, "备注"); // Notes

        if (_editingDescActive)
        {
            if (_focusDescNext)
            {
                ImGui.SetKeyboardFocusHere();
                _focusDescNext = false;
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextMultiline("##listDesc", ref _editingDescription, 512, new Vector2(-1, 60));
            if (ImGui.IsItemDeactivated())
            {
                _list.Description = _editingDescription;
                GatherBuddy.CraftingListManager.SaveList(_list);
                _editingDescActive = false;
                GatherBuddy.Log.Debug($"[CraftingListEditor] Updated description for list '{_list.Name}'");
            }
        }
        else
        {
            using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.18f, 1f)))
            {
                ImGui.BeginChild("##notesDisplay", new Vector2(-1, 60f), true,
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

                if (string.IsNullOrEmpty(_editingDescription))
                    ImGui.TextColored(ImGuiColors.DalamudGrey, "点击添加备注...");
                else
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3))
                        ImGui.TextWrapped(_editingDescription);
                }

                if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _editingDescActive = true;
                    _focusDescNext     = true;
                }

                ImGui.EndChild();
            }
        }
    }

    private void DrawListConsumablesSection()
    {
        var labelColor = new Vector4(0.80f, 0.80f, 0.80f, 1f);
        var valueX     = 80f;
        var hasAny     = false;

        if (_list.Consumables.FoodItemId.HasValue)
        {
            ImGui.TextColored(labelColor, "食物:");
            ImGui.SameLine(valueX);
            ImGui.TextColored(labelColor, GetItemLabel(_list.Consumables.FoodItemId.Value, _list.Consumables.FoodHQ));
            hasAny = true;
        }
        if (_list.Consumables.MedicineItemId.HasValue)
        {
            ImGui.TextColored(labelColor, "药水:");
            ImGui.SameLine(valueX);
            ImGui.TextColored(labelColor, GetItemLabel(_list.Consumables.MedicineItemId.Value, _list.Consumables.MedicineHQ));
            hasAny = true;
        }
        if (_list.Consumables.ManualItemId.HasValue)
        {
            ImGui.TextColored(labelColor, "指南:");
            ImGui.SameLine(valueX);
            ImGui.TextColored(labelColor, GetItemLabel(_list.Consumables.ManualItemId.Value, false));
            hasAny = true;
        }
        if (_list.Consumables.SquadronManualItemId.HasValue)
        {
            ImGui.TextColored(labelColor, "军用指南:");
            ImGui.SameLine(valueX);
            ImGui.TextColored(labelColor, GetItemLabel(_list.Consumables.SquadronManualItemId.Value, false));
            hasAny = true;
        }
        if (!hasAny)
            ImGui.TextColored(ImGuiColors.DalamudGrey, "无设置");

        ImGui.Spacing();
        if (ImGui.Button("编辑 消耗品 & 宏##editConsumables", new Vector2(0, 0)))
            _consumablesPopup.OpenListDefaults(_list);
    }
    
    private void DrawAddRecipeSection()
    {
        if (_recipeCombo == null)
            InitializeRecipeCombo();

        DrawRecipeComboWithKeywordFilter();

        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("##quantity", ref _searchQuantity, 1);
        if (_searchQuantity < 1)
            _searchQuantity = 1;
        ImGui.SameLine();

        using (ImRaii.Disabled(_selectedRecipe == null))
        {
            var clicked = ImGui.Button("添加到清单##addRecipeBtn", new Vector2(0, 0));
            if (!clicked && ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                clicked = true;
            if (clicked && _selectedRecipe != null)
            {
                _list.AddRecipe(_selectedRecipe.Value.RowId, _searchQuantity);
                GatherBuddy.CraftingListManager.SaveList(_list);
                _cachedQueueValid     = false;
                _cachedMaterialsValid = false;
                TriggerQueueRegeneration();
                _selectedRecipe = null;
                _searchQuantity = 1;
            }
        }

        if (ImGui.IsItemHovered() && _selectedRecipe != null)
            ImGui.SetTooltip($"添加 {_recipeLabels[_selectedRecipe.Value.RowId]} x{_searchQuantity} 到清单");
    }

    private void DrawRecipeComboWithKeywordFilter()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##recipeComboCustom", _selectedRecipe.HasValue ? _recipeLabels.GetValueOrDefault(_selectedRecipe.Value.RowId, "选择配方") : "选择配方"))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filterRecipes", "输入以筛选...", ref _lastComboFilter, 256);

            var filterKeywords = _lastComboFilter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.ToLowerInvariant())
                .ToArray();

            var displayRecipes = _allRecipes;
            if (filterKeywords.Length > 0)
            {
                displayRecipes = _allRecipes.Where(r =>
                {
                    var label = _recipeLabels[r.RowId].ToLowerInvariant();
                    return filterKeywords.All(keyword => label.Contains(keyword));
                }).ToList();
            }

            var height = ImGui.GetTextLineHeightWithSpacing();
            void DrawRecipeItem(Recipe recipe)
            {
                if (ImGui.Selectable(_recipeLabels[recipe.RowId], _selectedRecipe?.RowId == recipe.RowId))
                {
                    _selectedRecipe = recipe;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGuiClip.ClippedDraw(displayRecipes, DrawRecipeItem, height);

            ImGui.EndCombo();
        }
    }

    private void InitializeRecipeCombo()
    {
        var recipeSheet = Dalamud.GameData.GetExcelSheet<Recipe>();
        if (recipeSheet == null)
            return;

        _allRecipes.Clear();
        foreach (var recipe in recipeSheet)
        {
            try
            {
                if (recipe.ItemResult.RowId == 0 || recipe.Number == 0)
                    continue;

                var recipeNameOriginal = recipe.ItemResult.Value.Name.ExtractText();
                if (!_recipeLabels.ContainsKey(recipe.RowId))
                {
                    var jobName = GetCraftingJobName(recipe.CraftType.RowId);
                    _recipeLabels[recipe.RowId] = $"{recipeNameOriginal} ({jobName} {recipe.RecipeLevelTable.Value.ClassJobLevel})";
                }

                _allRecipes.Add(recipe);
            }
            catch
            {
            }
        }

        _allRecipes.Sort((a, b) =>
        {
            var levelCmp = b.RecipeLevelTable.Value.ClassJobLevel.CompareTo(a.RecipeLevelTable.Value.ClassJobLevel);
            if (levelCmp != 0) return levelCmp;
            return a.ItemResult.Value.Name.ExtractText().CompareTo(b.ItemResult.Value.Name.ExtractText());
        });

        _recipeCombo = new ClippedSelectableCombo<Recipe>("RecipeCombo", "Recipe", 300, _allRecipes, r => _recipeLabels[r.RowId]);
    }

    private void DrawRecipeListSection()
    {
        if (_list.Recipes.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "尚未添加任何配方。");
            return;
        }

        int indexToRemove = -1;
        
        for (int i = 0; i < _list.Recipes.Count; i++)
        {
            var item = _list.Recipes[i];
            var recipe = RecipeManager.GetRecipe(item.RecipeId);
            if (recipe == null)
                continue;

            var itemName = recipe.Value.ItemResult.Value.Name.ExtractText();
            var jobName = GetCraftingJobName(recipe.Value.CraftType.RowId);
            
            var skipIndicator = item.Options.Skipping ? "[跳过] " : "";
            var hqIndicator = (item.IngredientPreferences.Count > 0 || item.CraftSettings?.IngredientPreferences.Count > 0) ? "[HQ] " : "";
            var quickSynthIndicator = item.Options.NQOnly ? "[简易制作] " : "";
            var craftSettingsIndicator = item.CraftSettings?.HasAnySettings() == true ? "[设置] " : "";
            var textColor = item.Options.Skipping ? new Vector4(0.7f, 0.7f, 0.7f, 1) : new Vector4(1, 1, 1, 1);

            var validation = MacroValidator.GetOrCompute(item.RecipeId, ResolveEffectiveMacroId(item.CraftSettings, false), item.CraftSettings, _list.Consumables);
            if (validation != null)
            {
                var dotColor = validation.IsValid
                    ? new Vector4(0.30f, 0.70f, 0.30f, 1f)
                    : (validation.Failure is MacroValidationFailure.InsufficientProgress or MacroValidationFailure.ActionUnusable
                        ? new Vector4(0.78f, 0.62f, 0.15f, 1f)
                        : new Vector4(0.78f, 0.25f, 0.25f, 1f));
                ImGui.TextColored(dotColor, "\u25cf");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(validation.IsValid
                        ? $"宏: 通过\n进展: {validation.FinalProgress}/{validation.RequiredProgress}\n品质: {validation.FinalQuality}\n耐久: {validation.FinalDurability}"
                        : $"宏: {validation.Failure} 于步骤 {validation.FailedAtStep}\n进展: {validation.FinalProgress}/{validation.RequiredProgress}");
                ImGui.SameLine();
            }

            ImGui.PushStyleColor(ImGuiCol.Text, textColor);
            ImGui.Selectable($"{quickSynthIndicator}{craftSettingsIndicator}{hqIndicator}{skipIndicator}{itemName} x{item.Quantity} ({jobName})##recipe_{i}", false);
            ImGui.PopStyleColor();
            
            var isPopupOpen = GatherBuddy.ControllerSupport != null
                ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"context_{i}", Dalamud.GamepadState)
                : ImGui.BeginPopupContextItem($"context_{i}");
            
            if (isPopupOpen)
            {
                if (ImGui.MenuItem("制作设置..."))
                {
                    _craftSettingsPopup.OpenForListItem(item, _list, itemName);
                }
                
                ImGui.Separator();
                
                ImGui.Text("数量:");
                ImGui.SetNextItemWidth(100);
                if (_editingQuantityIndex != i)
                {
                    _tempQuantityInput = item.Quantity;
                    _editingQuantityIndex = i;
                }
                
                if (ImGui.InputInt($"##qty_{i}", ref _tempQuantityInput, 1))
                {
                    if (_tempQuantityInput < 1)
                        _tempQuantityInput = 1;
                }
                
                if (ImGui.IsItemDeactivatedAfterEdit() && _tempQuantityInput != item.Quantity)
                {
                    _list.UpdateRecipeQuantity(item.RecipeId, _tempQuantityInput);
                    GatherBuddy.CraftingListManager.SaveList(_list);
                    _cachedQueueValid = false;
                    _cachedMaterialsValid = false;
                    TriggerQueueRegeneration();
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem(item.Options.Skipping ? "启用" : "跳过"))
                {
                    item.Options.Skipping = !item.Options.Skipping;
                    GatherBuddy.CraftingListManager.SaveList(_list);
                    _cachedQueueValid = false;
                    _cachedMaterialsValid = false;
                    TriggerQueueRegeneration();
                }
                
                if (ImGui.MenuItem("移除"))
                {
                    indexToRemove = i;
                }
                
                ImGui.EndPopup();
            }
            else if (_editingQuantityIndex == i)
            {
                _editingQuantityIndex = -1;
            }
        }

        if (indexToRemove >= 0)
        {
            _list.Recipes.RemoveAt(indexToRemove);
            GatherBuddy.CraftingListManager.SaveList(_list);
            _cachedQueueValid = false;
            _cachedMaterialsValid = false;
            TriggerQueueRegeneration();
        }
    }

    private string ComputeListHash()
    {
        var hashParts = new List<string>();
        hashParts.Add($"SkipIfEnough:{_list.SkipIfEnough}");
        foreach (var item in _list.Recipes)
        {
            hashParts.Add($"{item.RecipeId}:{item.Quantity}:{item.Options.Skipping}");
        }
        return string.Join("|", hashParts);
    }
    
    private void TriggerQueueRegeneration()
    {
        var currentHash = ComputeListHash();
        if (_cachedQueueValid && _cachedSortedQueue != null && currentHash == _cachedListHash)
        {
            return;
        }
        
        _queueCancellationSource?.Cancel();
        _queueCancellationSource?.Dispose();
        _queueCancellationSource = new CancellationTokenSource();
        
        _isGeneratingQueue = true;
        var token = _queueCancellationSource.Token;
        var hash = currentHash;
        
        _queueGenerationTask = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                
                var queue = GenerateSortedQueueSync();
                
                if (!token.IsCancellationRequested)
                {
                    _cachedSortedQueue = queue;
                    _cachedListHash = hash;
                    _cachedQueueValid = true;
                }
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"Error generating queue: {ex.Message}");
            }
            finally
            {
                _isGeneratingQueue = false;
            }
        }, token);
    }
    
    internal void TriggerMaterialsRegeneration()
    {
        var currentHash = ComputeListHash();
        if (_cachedMaterialsValid && _cachedMaterials != null && currentHash == _cachedMaterialsHash)
        {
            return;
        }
        
        _materialsCancellationSource?.Cancel();
        _materialsCancellationSource?.Dispose();
        _materialsCancellationSource = new CancellationTokenSource();
        
        _isGeneratingMaterials = true;
        var token = _materialsCancellationSource.Token;
        var hash = currentHash;
        
        _materialsGenerationTask = Task.Run(() =>
        {
            try
            {
                if (token.IsCancellationRequested) return;
                
                var materials = _list.ListMaterials();
                
                if (!token.IsCancellationRequested)
                {
                    _cachedMaterials = materials;
                    _cachedMaterialsHash = hash;
                    _cachedMaterialsValid = true;
                }
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"Error generating materials: {ex.Message}");
            }
            finally
            {
                _isGeneratingMaterials = false;
            }
        }, token);
    }
    
    private List<CraftingListItem> GetSortedQueue()
    {
        if (_cachedSortedQueue != null && _cachedQueueValid)
        {
            return _cachedSortedQueue;
        }
        return new List<CraftingListItem>();
    }
    
    private List<CraftingListItem> GenerateSortedQueueSync()
    {
        var queue = new CraftingListQueue();
        foreach (var item in _list.Recipes)
        {
            if (!item.Options.Skipping)
            {
                queue.AddRecipeWithPrecrafts(item.RecipeId, item.Quantity, _list.SkipIfEnough);
            }
        }
        
        var originalRecipes = new HashSet<uint>();
        foreach (var item in _list.Recipes)
        {
            originalRecipes.Add(item.RecipeId);
        }
        
        var precrafts = new List<CraftingListItem>();
        var finalProducts = new List<CraftingListItem>();
        
        foreach (var recipe in queue.Recipes)
        {
            if (originalRecipes.Contains(recipe.RecipeId))
            {
                finalProducts.Add(recipe);
            }
            else
            {
                precrafts.Add(recipe);
            }
        }
        
        var precraftsByJob = precrafts
            .GroupBy(r => RecipeManager.GetRecipe(r.RecipeId)?.CraftType.RowId ?? uint.MaxValue)
            .OrderBy(g => g.Key);
        
        var sortedPrecrafts = new List<CraftingListItem>();
        var processedPrecrafts = new HashSet<uint>();
        
        foreach (var jobGroup in precraftsByJob)
        {
            var jobRecipes = jobGroup.ToList();
            foreach (var recipeItem in jobRecipes)
            {
                ProcessPrecraftWithDependencies(recipeItem, queue.Recipes, processedPrecrafts, sortedPrecrafts);
            }
        }
        
        var sortedFinalProducts = finalProducts
            .GroupBy(r => RecipeManager.GetRecipe(r.RecipeId)?.CraftType.RowId ?? uint.MaxValue)
            .OrderBy(g => g.Key)
            .SelectMany(g => g)
            .ToList();
        
        var result = new List<CraftingListItem>();
        result.AddRange(sortedPrecrafts);
        result.AddRange(sortedFinalProducts);
        
        return result;
    }
    
    internal Dictionary<uint, int> GetCachedMaterials()
    {
        var currentHash = ComputeListHash();
        if (_cachedMaterialsValid && _cachedMaterials != null && currentHash == _cachedMaterialsHash)
        {
            return _cachedMaterials;
        }
        
        _cachedMaterials = _list.ListMaterials();
        _cachedMaterialsHash = currentHash;
        _cachedMaterialsValid = true;
        
        return _cachedMaterials;
    }

    internal Dictionary<uint, int> GetCachedPrecraftMaterials()
    {
        var currentHash = ComputeListHash();
        if (_cachedPrecraftMaterials != null && currentHash == _cachedPrecraftMaterialsHash)
            return _cachedPrecraftMaterials;

        _cachedPrecraftMaterials = _list.ListPrecrafts();
        _cachedPrecraftMaterialsHash = currentHash;
        return _cachedPrecraftMaterials;
    }

    private static string GetConsumableSummary(CraftingListConsumableSettings settings)
    {
        var parts = new List<string>();

        if (settings.FoodItemId.HasValue)
            parts.Add($"Food: {GetItemLabel(settings.FoodItemId.Value, settings.FoodHQ)}");
        if (settings.MedicineItemId.HasValue)
            parts.Add($"Medicine: {GetItemLabel(settings.MedicineItemId.Value, settings.MedicineHQ)}");
        if (settings.ManualItemId.HasValue)
            parts.Add($"Manual: {GetItemLabel(settings.ManualItemId.Value, false)}");
        if (settings.SquadronManualItemId.HasValue)
            parts.Add($"Squadron: {GetItemLabel(settings.SquadronManualItemId.Value, false)}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "None";
    }

    private static string GetItemLabel(uint itemId, bool hq)
    {
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
            return item.Name.ExtractText() + (hq ? " HQ" : "");
        return itemId.ToString();
    }
    
    internal unsafe int GetInventoryCount(uint itemId)
    {
        var now = DateTime.Now;
        
        if (_inventoryRefreshTimes.TryGetValue(itemId, out var lastRefresh))
        {
            if ((now - lastRefresh).TotalSeconds < InventoryRefreshIntervalSeconds)
            {
                return _cachedInventoryCounts.GetValueOrDefault(itemId, 0);
            }
        }
        
        try
        {
            var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (inventory == null)
                return 0;
            
            var count = inventory->GetInventoryItemCount(itemId, false, false, false)
                      + inventory->GetInventoryItemCount(itemId, true, false, false);
            _cachedInventoryCounts[itemId] = count;
            _inventoryRefreshTimes[itemId] = now;
            return count;
        }
        catch
        {
            return 0;
        }
    }

    internal int GetRetainerCount(uint itemId)   => (int)RetainerCache.GetRetainerItemCount(itemId);
    internal int GetRetainerCountNQ(uint itemId) => (int)RetainerCache.GetRetainerItemCountNQ(itemId);
    internal int GetRetainerCountHQ(uint itemId) => (int)RetainerCache.GetRetainerItemCountHQ(itemId);
    
    private unsafe bool WillBeSkippedDueToInventory(Recipe recipe, int quantityToCraft)
    {
        try
        {
            var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (inventory == null)
                return false;
            
            var resultItemId = recipe.ItemResult.RowId;
            var amountPerCraft = recipe.AmountResult;
            var totalNeeded = quantityToCraft * amountPerCraft;
            
            var nqCount = inventory->GetInventoryItemCount(resultItemId, false, false, false);
            var hqCount = inventory->GetInventoryItemCount(resultItemId, true, false, false);
            var totalCount = nqCount + hqCount;
            
            return totalCount >= totalNeeded;
        }
        catch
        {
            return false;
        }
    }

    private void ProcessPrecraftWithDependencies(CraftingListItem recipeItem, List<CraftingListItem> allRecipes, HashSet<uint> processed, List<CraftingListItem> result)
    {
        if (processed.Contains(recipeItem.RecipeId))
            return;
        
        var recipe = RecipeManager.GetRecipe(recipeItem.RecipeId);
        if (recipe == null)
            return;
        
        var ingredients = RecipeManager.GetIngredients(recipe.Value);
        foreach (var (itemId, _) in ingredients)
        {
            var depRecipe = RecipeManager.GetRecipeForItem(itemId);
            if (depRecipe.HasValue)
            {
                var depItem = allRecipes.FirstOrDefault(r => r.RecipeId == depRecipe.Value.RowId);
                if (depItem != null)
                {
                    ProcessPrecraftWithDependencies(depItem, allRecipes, processed, result);
                }
            }
        }
        
        processed.Add(recipeItem.RecipeId);
        result.Add(recipeItem);
    }
    
    private string? ResolveEffectiveMacroId(RecipeCraftSettings? settings, bool isPrecraft)
    {
        var isSpecific = settings?.MacroMode == MacroOverrideMode.Specific
            || (settings?.MacroMode == MacroOverrideMode.Inherit && !string.IsNullOrEmpty(settings?.SelectedMacroId));
        if (isSpecific)
            return settings?.SelectedMacroId;
        return isPrecraft ? _list.DefaultPrecraftMacroId : _list.DefaultFinalMacroId;
    }

    private (int hardFails, int warnings) CountValidationIssues()
    {
        var hardFails = 0;
        var warnings  = 0;

        foreach (var item in _list.Recipes)
        {
            var macroId = ResolveEffectiveMacroId(item.CraftSettings, false);
            if (string.IsNullOrEmpty(macroId))
                continue;
            var result = MacroValidator.GetOrCompute(item.RecipeId, macroId, item.CraftSettings, _list.Consumables);
            if (result == null || result.Failure == MacroValidationFailure.NoStats)
                continue;
            if (!result.IsValid)
            {
                if (result.Failure is MacroValidationFailure.CPExhausted or MacroValidationFailure.DurabilityFailed)
                    hardFails++;
                else
                    warnings++;
            }
        }

        var originalRecipeIds = new HashSet<uint>(_list.Recipes.Select(r => r.RecipeId));
        foreach (var queueItem in GetSortedQueue())
        {
            if (originalRecipeIds.Contains(queueItem.RecipeId))
                continue;
            var craftSettings = _list.PrecraftCraftSettings.GetValueOrDefault(queueItem.RecipeId);
            var macroId = ResolveEffectiveMacroId(craftSettings, true);
            if (string.IsNullOrEmpty(macroId))
                continue;
            var result = MacroValidator.GetOrCompute(queueItem.RecipeId, macroId, craftSettings, _list.Consumables);
            if (result == null || result.Failure == MacroValidationFailure.NoStats)
                continue;
            if (!result.IsValid)
            {
                if (result.Failure is MacroValidationFailure.CPExhausted or MacroValidationFailure.DurabilityFailed)
                    hardFails++;
                else
                    warnings++;
            }
        }

        return (hardFails, warnings);
    }

    private string GetCraftingJobName(uint craftTypeId)
    {
        var classJobSheet = Dalamud.GameData.GetExcelSheet<ClassJob>();
        if (classJobSheet != null)
        {
            var classJobId = craftTypeId + 8;
            var classJob = classJobSheet.GetRow(classJobId);
            if (classJob.RowId > 0)
                return classJob.Name.ExtractText(); // 默认 Abbreviation，修改以显示全称
        }
        return "未知";
    }
}
