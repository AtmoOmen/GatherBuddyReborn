using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using ElliLib;
using ElliLib.Table;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private static Vector2 IconSize => ImGuiHelpers.ScaledVector2(40, 40);
    private static Vector2 LineIconSize => new(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight());
    private static Vector2 ItemSpacing => ImGui.GetStyle().ItemSpacing;
    private static float Scale => ImGuiHelpers.GlobalScale;
    
    private static float TextWidth(string text)
        => ImGui.CalcTextSize(text).X + ItemSpacing.X;

    private static unsafe void SwitchJobIfNeeded(uint requiredJobId)
    {
        var currentJob = Dalamud.ClientState.LocalPlayer?.ClassJob.RowId ?? 0;
        if (currentJob == requiredJobId)
            return;

        try
        {
            var gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                GatherBuddy.Log.Error("[VulcanWindow] Failed to get gearset module");
                return;
            }

            for (int i = 0; i < 100; i++)
            {
                if (gearsetModule->Entries[i].ClassJob == requiredJobId)
                {
                    gearsetModule->EquipGearset(i);
                    GatherBuddy.Log.Information($"[VulcanWindow] Switched to gearset {i} for job {requiredJobId}");
                    return;
                }
            }

            GatherBuddy.Log.Warning($"[VulcanWindow] No gearset found for job {requiredJobId}");
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"[VulcanWindow] Failed to switch job: {ex.Message}");
        }
    }

    private static void StartCraftWithRaphael(Recipe recipe)
    {
        var requiredJob = (uint)(recipe.CraftType.RowId + 8);
        var currentJob = Dalamud.ClientState.LocalPlayer?.ClassJob.RowId ?? 0;
        
        if (currentJob != requiredJob)
        {
            GatherBuddy.Log.Information($"[VulcanWindow] Job switch needed: {currentJob} -> {requiredJob}");
            SwitchJobIfNeeded(requiredJob);
            
            var tm = GatherBuddy.AutoGather?.TaskManager;
            if (tm != null)
            {
                tm.DelayNext(3000);
                tm.Enqueue(() =>
                {
                    StartCraftWithRaphaelAfterJobSwitch(recipe);
                    return true;
                }, "StartCraftAfterJobSwitch");
            }
            return;
        }
        
        StartCraftWithRaphaelAfterJobSwitch(recipe);
    }
    
    private static void StartCraftWithRaphaelAfterJobSwitch(Recipe recipe)
    {
        var settings = GatherBuddy.RecipeBrowserSettings.Get(recipe.RowId);
        if (settings != null && settings.HasAnySettings())
        {
            GatherBuddy.Log.Debug($"[VulcanWindow] Applying recipe browser settings for {recipe.RowId}");
            if (settings.FoodItemId.HasValue)
                GatherBuddy.Log.Debug($"  Food: {settings.FoodItemId.Value}");
            if (settings.MedicineItemId.HasValue)
                GatherBuddy.Log.Debug($"  Medicine: {settings.MedicineItemId.Value}");
            if (settings.ManualItemId.HasValue)
                GatherBuddy.Log.Debug($"  Manual: {settings.ManualItemId.Value}");
            if (settings.SquadronManualItemId.HasValue)
                GatherBuddy.Log.Debug($"  Squadron Manual: {settings.SquadronManualItemId.Value}");
            GatherBuddy.Log.Debug($"  Ingredient prefs: {settings.IngredientPreferences.Count} items, UseAllNQ={settings.UseAllNQ}");
            CraftingGameInterop.SetIngredientPreferences(
                settings.IngredientPreferences.Count > 0 || settings.UseAllNQ ? settings.IngredientPreferences : null,
                settings.UseAllNQ);
            
            var allApplied = ConsumableChecker.ApplyConsumables(settings);
            if (!allApplied)
            {
                GatherBuddy.Log.Debug($"[VulcanWindow] Consumables applied, waiting 3 seconds before starting craft");
                var taskMgr = GatherBuddy.AutoGather?.TaskManager;
                if (taskMgr != null)
                {
                    taskMgr.DelayNext(3000);
                }
            }
        }
        else
        {
            CraftingGameInterop.SetIngredientPreferences(null);
        }
        
        var solverMode = GatherBuddy.Config.RaphaelSolverConfig.SolverMode;
        if (solverMode != RaphaelSolverMode.PureRaphael)
        {
            CraftingGameInterop.StartCraft(recipe, 1);
            return;
        }

        var requiredJob = (uint)(recipe.CraftType.RowId + 8);
        var baseStats = GearsetStatsReader.ReadGearsetStatsForJob(requiredJob);
        if (baseStats == null)
        {
            GatherBuddy.Log.Warning($"[VulcanWindow] Could not read gearset stats for job {requiredJob}, crafting without Raphael");
            CraftingGameInterop.StartCraft(recipe, 1);
            return;
        }
        
        var gearsetStats = baseStats;
        if (settings != null && (settings.FoodItemId.HasValue || settings.MedicineItemId.HasValue))
        {
            gearsetStats = GearsetStatsReader.ApplyConsumablesToStats(baseStats, settings);
            GatherBuddy.Log.Debug($"[VulcanWindow] Stats with consumables: Craftsmanship={gearsetStats.Craftsmanship}, Control={gearsetStats.Control}, CP={gearsetStats.CP}");
        }

        var request = new RaphaelSolveRequest(
            RecipeId: recipe.RowId,
            Level: gearsetStats.Level,
            Craftsmanship: gearsetStats.Craftsmanship,
            Control: gearsetStats.Control,
            CP: gearsetStats.CP,
            Manipulation: gearsetStats.Manipulation,
            Specialist: gearsetStats.Specialist,
            InitialQuality: 0
        );

        if (GatherBuddy.RaphaelSolveCoordinator.TryGetSolution(request, out var solution) && solution != null && !solution.IsFailed)
        {
            GatherBuddy.Log.Debug($"[VulcanWindow] Raphael solution already cached for recipe {recipe.RowId}, starting craft");
            CraftingGameInterop.StartCraft(recipe, 1);
            return;
        }

        GatherBuddy.Log.Information($"[VulcanWindow] Enqueuing Raphael solve for recipe {recipe.RowId}");
        var queueItem = new CraftingListItem(recipe.RowId, 1);
        var recipeStats = new List<(uint RecipeId, int Craftsmanship, int Control, int CP, int Level, bool Manipulation, bool Specialist)>
        {
            (recipe.RowId, gearsetStats.Craftsmanship, gearsetStats.Control, gearsetStats.CP, gearsetStats.Level, gearsetStats.Manipulation, gearsetStats.Specialist)
        };
        GatherBuddy.RaphaelSolveCoordinator.EnqueueSolvesFromCraftStates(new[] { queueItem }, recipeStats);

        var tm = GatherBuddy.AutoGather?.TaskManager;
        if (tm == null)
        {
            GatherBuddy.Log.Error($"[VulcanWindow] TaskManager unavailable, cannot wait for Raphael solve");
            return;
        }

        tm.Enqueue(() => WaitForRaphaelSolution(request), 60000, "WaitForRaphaelSolution");
        tm.Enqueue(() =>
        {
            GatherBuddy.Log.Information($"[VulcanWindow] Raphael solution ready, starting craft for recipe {recipe.RowId}");
            CraftingGameInterop.StartCraft(recipe, 1);
            return true;
        }, "StartCraftAfterRaphael");
    }

    private static bool WaitForRaphaelSolution(RaphaelSolveRequest request)
    {
        if (GatherBuddy.RaphaelSolveCoordinator.TryGetSolution(request, out var solution))
        {
            if (solution != null && !solution.IsFailed)
            {
                GatherBuddy.Log.Debug($"[VulcanWindow] Raphael solution ready for recipe {request.RecipeId}");
                return true;
            }
            else if (solution != null && solution.IsFailed)
            {
                GatherBuddy.Log.Warning($"[VulcanWindow] Raphael solution failed for recipe {request.RecipeId}: {solution.FailureReason}");
                return true;
            }
        }

        return false;
    }

    private static void StartBrowserQuickSynth(Recipe recipe, int quantity)
    {
        var expandedQueue = new List<CraftingListItem>(quantity);
        for (int i = 0; i < quantity; i++)
        {
            var item = new CraftingListItem(recipe.RowId, 1) { IsOriginalRecipe = true };
            item.Options.NQOnly = true;
            expandedQueue.Add(item);
        }
        GatherBuddy.Log.Information($"[VulcanWindow] Browser quick synth: {recipe.ItemResult.Value.Name.ExtractText()} x{quantity}");
        CraftingGatherBridge.StartQueueCraftAndGather(expandedQueue, new Dictionary<uint, int>());
    }

    private static void StartBrowserCraft(Recipe recipe, int quantity)
    {
        var settings = GatherBuddy.RecipeBrowserSettings.Get(recipe.RowId);
        RecipeCraftSettings? craftSettings = null;
        if (settings != null && settings.HasAnySettings())
        {
            craftSettings = new RecipeCraftSettings
            {
                FoodMode = settings.FoodMode,
                FoodItemId = settings.FoodItemId,
                FoodHQ = settings.FoodHQ,
                MedicineMode = settings.MedicineMode,
                MedicineItemId = settings.MedicineItemId,
                MedicineHQ = settings.MedicineHQ,
                ManualMode = settings.ManualMode,
                ManualItemId = settings.ManualItemId,
                SquadronManualMode = settings.SquadronManualMode,
                SquadronManualItemId = settings.SquadronManualItemId,
                UseAllNQ = settings.UseAllNQ,
                SelectedMacroId = settings.SelectedMacroId,
                MacroMode = settings.MacroMode,
                SolverOverride = settings.SolverOverride,
                IngredientPreferences = new Dictionary<uint, int>(settings.IngredientPreferences),
            };
        }

        var expandedQueue = new List<CraftingListItem>(quantity);
        for (int i = 0; i < quantity; i++)
        {
            var item = new CraftingListItem(recipe.RowId, 1) { IsOriginalRecipe = true };
            if (craftSettings != null)
            {
                item.CraftSettings = craftSettings;
                if (craftSettings.IngredientPreferences.Count > 0 || craftSettings.UseAllNQ)
                    item.IngredientPreferences = new Dictionary<uint, int>(craftSettings.IngredientPreferences);
            }
            expandedQueue.Add(item);
        }

        GatherBuddy.Log.Information($"[VulcanWindow] Browser craft: {recipe.ItemResult.Value.Name.ExtractText()} x{quantity}");
        CraftingGatherBridge.StartQueueCraftAndGather(expandedQueue, new Dictionary<uint, int>());
    }

    private enum SortColumn { Name, Job, Level, Crafted }
    private enum SortDirection { Ascending, Descending }
    
    private static List<ExtendedRecipe>? _extendedRecipeList;
    private static List<ExtendedRecipe>? _filteredRecipes;
    private static bool _filtersDirty = true;
    private static ExtendedRecipe? _selectedRecipe;
    private static SortColumn _sortColumn = SortColumn.Level;
    private static SortDirection _sortDirection = SortDirection.Ascending;
    private static bool _hideCrafted = false;
    private static bool _filterByEquipLevel = false;
    private static RecipeTable? _recipeTable;
    private static string _recipeSearchText = "";
    private static HashSet<uint> _selectedJobFilters = new();
    private static int _minLevel = 1;
    private static int _maxLevel = 100;
    private static bool _filterBrowserMasterRecipes = false;
    private static bool _filterBrowserCollectables = false;
    private static bool _filterBrowserExpertRecipes = false;
    private static bool _filterBrowserQuestRecipes = false;
    private static bool _filterBrowserRegularOnly = false;
    private static bool _isInitialized = false;
    private static bool _craftedStatusDirty = false;
    private static int _browserCraftQuantity = 1;
    private static RecipeCraftSettingsPopup _craftSettingsPopup = new();
    private static string _contextMenuListSearch   = string.Empty;
    private static int    _contextMenuAddQuantity   = 1;
    private static string _contextMenuNewListName   = string.Empty;
    private static bool   _contextMenuNewListEphemeral = false;
    private static readonly uint[] CraftTypeToClassJobId = { 8, 9, 10, 11, 12, 13, 14, 15 };
    private static readonly string[] JobNames = { "刻木匠", "锻铁匠", "铸甲匠", "雕金匠", "制革匠", "裁衣匠", "炼金术士", "烹调师" };

    private static void InitializeRecipeList()
    {
        if (_isInitialized)
            return;

        var tempList = new List<ExtendedRecipe>();
        var recipeSheet = Dalamud.GameData.GetExcelSheet<Recipe>();
        if (recipeSheet != null)
        {
            foreach (var recipe in recipeSheet)
            {
                if (recipe.ItemResult.RowId > 0)
                {
                    tempList.Add(new ExtendedRecipe(recipe, lazyLoad: false));
                }
            }
        }
        _extendedRecipeList = tempList;

        _isInitialized = true;
        _filtersDirty = true;
    }

    private static void UpdateFilteredList()
    {
        if (!_filtersDirty || _extendedRecipeList == null)
            return;

        var filtered = _extendedRecipeList.Where(PassesFilters).ToList();
        
        filtered = _sortColumn switch
        {
            SortColumn.Name => _sortDirection == SortDirection.Ascending 
                ? filtered.OrderBy(r => r.Name).ToList()
                : filtered.OrderByDescending(r => r.Name).ToList(),
            SortColumn.Job => _sortDirection == SortDirection.Ascending
                ? filtered.OrderBy(r => r.JobId).ToList()
                : filtered.OrderByDescending(r => r.JobId).ToList(),
            SortColumn.Level => _sortDirection == SortDirection.Ascending 
                ? filtered.OrderBy(r => _filterByEquipLevel ? r.ItemEquipLevel : r.Level).ToList()
                : filtered.OrderByDescending(r => _filterByEquipLevel ? r.ItemEquipLevel : r.Level).ToList(),
            SortColumn.Crafted => _sortDirection == SortDirection.Ascending
                ? filtered.OrderBy(r => r.IsCrafted).ToList()
                : filtered.OrderByDescending(r => r.IsCrafted).ToList(),
            _ => filtered
        };
        
        _filteredRecipes = filtered;
        _filtersDirty = false;
        GatherBuddy.Log.Debug($"[VulcanWindow] Filtered to {_filteredRecipes.Count} recipes");
    }

    private static bool PassesFilters(ExtendedRecipe item)
    {
        if (!string.IsNullOrWhiteSpace(_recipeSearchText))
        {
            if (!item.Name.Contains(_recipeSearchText, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (_selectedJobFilters.Count > 0)
        {
            if (!_selectedJobFilters.Contains(item.JobId))
                return false;
        }

        var levelValue = _filterByEquipLevel ? item.ItemEquipLevel : item.Level;
        if (levelValue < _minLevel || levelValue > _maxLevel)
            return false;
        
        if (_hideCrafted && item.IsCrafted)
            return false;
        
        if (_filterBrowserRegularOnly)
        {
            if (item.Recipe.SecretRecipeBook.RowId > 0 ||
                item.Recipe.ItemResult.Value.AlwaysCollectable ||
                item.Recipe.IsExpert ||
                item.Recipe.ItemResult.Value.ItemSearchCategory.RowId == 0)
                return false;
        }
        else
        {
            if (_filterBrowserMasterRecipes && item.Recipe.SecretRecipeBook.RowId == 0)
                return false;
            
            if (_filterBrowserCollectables && !item.Recipe.ItemResult.Value.AlwaysCollectable)
                return false;
            
            if (_filterBrowserExpertRecipes && !item.Recipe.IsExpert)
                return false;
            
            if (_filterBrowserQuestRecipes && item.Recipe.ItemResult.Value.ItemSearchCategory.RowId != 0)
                return false;
        }

        return true;
    }

    private void DrawCraftingTab()
    {
        IDisposable tabItem;
        bool tabOpen;
        
        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("配方##recipesTab", 1, 8);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("配方##recipesTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }
        
        using (tabItem)
        {
            if (!tabOpen)
                return;

        if (!_isInitialized)
        {
            InitializeRecipeList();
        }

        if (_craftedStatusDirty && _extendedRecipeList != null)
        {
            foreach (var recipe in _extendedRecipeList)
            {
                recipe.UpdateCraftedStatus();
            }
            _craftedStatusDirty = false;
        }

        UpdateFilteredList();

        ImGui.Spacing();
        var avail = ImGui.GetContentRegionAvail();

        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##FilterPanel", new Vector2(180, avail.Y), true);
            DrawFilterPanel();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##ResultsList", new Vector2(avail.X * 0.40f, avail.Y), true);
            DrawResultsList();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##DetailsPanel", new Vector2(0, avail.Y), true);
            DrawDetailsPanel();
            ImGui.EndChild();
        }
        }
    }

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
        if (ImGui.Button("全", new Vector2(btnSide, btnSide)))
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

        if (ImGui.Checkbox("物品装备品级", ref _filterByEquipLevel))
            _filtersDirty = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("按物品的装备品级而不是制作等级进行筛选和排序");
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
            ImGui.SetTooltip("Ctrl + 点击以输入数值");
        }

        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderInt("##maxLevel", ref _maxLevel, 1, 100, "最高: %d", ImGuiSliderFlags.AlwaysClamp))
        {
            _maxLevel = Math.Clamp(_maxLevel, _minLevel, 100);
            _filtersDirty = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Ctrl + 点击以输入数值");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "筛选条件");
        ImGui.Spacing();

        if (ImGui.Checkbox("仅普通配方", ref _filterBrowserRegularOnly)) // Regular Only
        {
            if (_filterBrowserRegularOnly)
            {
                _filterBrowserMasterRecipes = false;
                _filterBrowserCollectables = false;
                _filterBrowserExpertRecipes = false;
                _filterBrowserQuestRecipes = false;
            }
            _filtersDirty = true;
        }
        
        if (ImGui.Checkbox("隐藏已制作", ref _hideCrafted))
        {
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("收藏品", ref _filterBrowserCollectables))
        {
            if (_filterBrowserCollectables)
                _filterBrowserRegularOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("秘籍配方", ref _filterBrowserMasterRecipes))
        {
            if (_filterBrowserMasterRecipes)
                _filterBrowserRegularOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("高难度配方", ref _filterBrowserExpertRecipes))
        {
            if (_filterBrowserExpertRecipes)
                _filterBrowserRegularOnly = false;
            _filtersDirty = true;
        }

        if (ImGui.Checkbox("任务配方", ref _filterBrowserQuestRecipes))
        {
            if (_filterBrowserQuestRecipes)
                _filterBrowserRegularOnly = false;
            _filtersDirty = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var count = _filteredRecipes?.Count ?? 0;
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"{count} 个配方");
    }

    private void DrawResultsList()
    {
        if (_filteredRecipes == null || _filteredRecipes.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "没有符合筛选条件的配方。");
            return;
        }

        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), $"  {_filteredRecipes.Count} 个配方");
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 180);
        
        var sortLabel = _sortColumn switch
        {
            SortColumn.Level => _filterByEquipLevel ? "装备品级" : "等级",
            SortColumn.Crafted => "已制作",
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
            if (ImGui.MenuItem("已制作", "", _sortColumn == SortColumn.Crafted))
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

            var isPopupOpen = GatherBuddy.ControllerSupport != null
                ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"RecipeContextMenu##{recipe.Recipe.RowId}", Dalamud.GamepadState)
                : ImGui.BeginPopupContextItem($"RecipeContextMenu##{recipe.Recipe.RowId}");
            
            if (isPopupOpen)
            {
                if (ImGui.MenuItem("显示配方属性 (调试)")) // Show Recipe Properties (Debug)
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
                    var resultItem = recipe.Recipe.ItemResult.Value;
                    GatherBuddy.Log.Information($"Item.RowId: {resultItem.RowId}");
                    GatherBuddy.Log.Information($"Item.AlwaysCollectable: {resultItem.AlwaysCollectable}");
                    GatherBuddy.Log.Information($"Item.IsUnique: {resultItem.IsUnique}");
                    GatherBuddy.Log.Information($"Item.IsUntradable: {resultItem.IsUntradable}");
                    GatherBuddy.Log.Information($"Item.ItemSearchCategory.RowId: {resultItem.ItemSearchCategory.RowId}");
                    GatherBuddy.Log.Information($"Item.ItemUICategory.RowId: {resultItem.ItemUICategory.RowId}");
                    GatherBuddy.Log.Information($"Item.Rarity: {resultItem.Rarity}");
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
                ImGui.Checkbox("临时清单##ctxNewListEphemeral", ref _contextMenuNewListEphemeral);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("在制作完成后自动删除该清单。\n可以在清单编辑器中关闭此选项。");
                if ((ImGui.Button("创建 & 添加", new Vector2(-1, 0)) || createEnter) && !string.IsNullOrWhiteSpace(_contextMenuNewListName))
                {
                    var newList = GatherBuddy.CraftingListManager.CreateNewList(_contextMenuNewListName.Trim(), _contextMenuNewListEphemeral);
                    newList.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
                    GatherBuddy.CraftingListManager.SaveList(newList);
                    GatherBuddy.Log.Information($"[VulcanWindow] Created list '{newList.Name}' and added {recipe.Name} x{_contextMenuAddQuantity}");
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

                    ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.6f, 1.0f), $"添加 {recipe.Name} 到清单:");
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
                        ImGui.TextDisabled("没有匹配项");
                    foreach (var list in filteredLists)
                    {
                        if (ImGui.MenuItem(list.Name))
                        {
                            list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, _contextMenuAddQuantity));
                            GatherBuddy.CraftingListManager.SaveList(list);
                            GatherBuddy.Log.Information($"Added {recipe.Name} x{_contextMenuAddQuantity} to crafting list '{list.Name}'");
                        }
                    }
                    ImGui.EndChild();

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    ImGui.TextColored(new Vector4(0.6f, 0.9f, 1.0f, 1.0f), "将所有未制作(已筛选)添加到:"); // Add all uncrafted (filtered) to

                    var bulkH = filteredLists.Count > 0 ? Math.Min(filteredLists.Count * rowH, 150f) : rowH;
                    ImGui.BeginChild("##BulkAddScroll", new Vector2(0, bulkH), true);
                    if (filteredLists.Count == 0)
                        ImGui.TextDisabled("没有匹配项");
                    foreach (var list in filteredLists)
                    {
                        if (ImGui.MenuItem($"{list.Name} (批量)##bulk_{list.ID}"))
                        {
                            var uncraftedCount = 0;
                            if (_filteredRecipes != null)
                            {
                                foreach (var r in _filteredRecipes)
                                {
                                    if (!r.IsCrafted)
                                    {
                                        list.Recipes.Add(new CraftingListItem(r.Recipe.RowId, 1));
                                        uncraftedCount++;
                                    }
                                }
                            }
                            GatherBuddy.CraftingListManager.SaveList(list);
                            GatherBuddy.Log.Information($"Added {uncraftedCount} uncrafted recipes to list '{list.Name}'");
                        }
                    }
                    ImGui.EndChild();
                }
                else
                {
                    ImGui.TextDisabled("没有可用的制作清单");
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
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "选择一个配方查看详情");
            ImGui.SetCursorPosX(12);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "并开始制作。");
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
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "[高难度]");
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
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), recipe.JobName); // 默认 JobAbbreviation , 改为 JobName 显示中文全称
        ImGui.SameLine(0, 16);
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "等级:");
        ImGui.SameLine();
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), recipe.Level.ToString());
        ImGui.SameLine(0, 16);
        ImGui.SetCursorPosY(classMidY);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1), "产出:");
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
        ImGui.TextColored(statLabelColor, "最大品质:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(statValueColor, $"{qualityMax}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var directIngredients = RecipeManager.GetIngredients(r);
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        var showRetainer = AllaganTools.Enabled;

        DrawIngredientSectionHeader("所需材料", showRetainer);

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
        ImGui.TextColored(statLabelColor, "可制作次数:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(craftable > 0 ? statValueColor : new Vector4(1f, 0.4f, 0.4f, 1f), $"{craftable}");
        ImGui.SameLine(0, 16);
        ImGui.TextColored(statLabelColor, "持有数量:"); ImGui.SameLine(0, 4);
        ImGui.TextColored(bagTotal > 0 ? statValueColor : new Vector4(0.5f, 0.5f, 0.5f, 1f),
            resultHq > 0 ? $"{resultNq}+{resultHq}hq" : $"{resultNq}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawIngredientSectionHeader("全部材料(含半成品制作)", showRetainer);

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
                    ImGui.Text($"药水: {medicine.Name.ExtractText()}");
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

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var topRowButtonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        var artisanLoaded = IPCSubscriber.IsReady("Artisan");
        if (artisanLoaded)
        {
            ImGuiUtil.DrawDisabledButton("检测到 Artisan", new Vector2(topRowButtonWidth, 22),
                "Artisan 已加载, 请卸载 Artisan 以使用 Vulcan 的制作系统。", true);
        }
        else if (ImGui.Button("开始制作", new Vector2(topRowButtonWidth, 22)))
        {
            StartBrowserCraft(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }
        ImGui.SameLine();
        if (ImGui.Button("设置", new Vector2(topRowButtonWidth, 22)))
            _craftSettingsPopup.Open(recipe.Recipe.RowId, recipe.Name);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 12);
        var canQuickSynth = recipe.Recipe.CanQuickSynth;
        var qsTooltip = artisanLoaded
            ? "Artisan 已加载, 请卸载 Artisan 以使用 Vulcan 的制作系统。"
            : canQuickSynth
                ? $"简易制作 {recipe.Name} x{_browserCraftQuantity}"
                : "该配方无法简易制作。";
        if (ImGuiUtil.DrawDisabledButton("简易制作", new Vector2(-1, 22), qsTooltip, !canQuickSynth || artisanLoaded))
        {
            StartBrowserQuickSynth(recipe.Recipe, _browserCraftQuantity);
            MinimizeWindow();
        }
    }

    private static void DrawIngredientSectionHeader(string title, bool showRetainer)
    {
        const float colWidth = 40f;
        var headerY     = ImGui.GetCursorPosY();
        var contentMaxX = ImGui.GetContentRegionMax().X;
        var nqColStart  = showRetainer ? contentMaxX - colWidth * 3 : contentMaxX - colWidth * 2;
        var hqColStart  = showRetainer ? contentMaxX - colWidth * 2 : contentMaxX - colWidth;

        var titleStartX   = ImGui.GetCursorPosX() + 12;
        var titleMaxWidth = nqColStart - titleStartX - 8f;
        if (titleMaxWidth > 0 && ImGui.CalcTextSize(title).X > titleMaxWidth)
        {
            while (title.Length > 0 && ImGui.CalcTextSize(title + "...").X > titleMaxWidth)
                title = title[..^1];
            title += "...";
        }
        ImGui.SetCursorPosX(titleStartX);
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), title);

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
            var retColStart = contentMaxX - colWidth;
            var retW = ImGui.CalcTextSize("雇员").X;
            ImGui.SetCursorPosX(retColStart + (colWidth - retW) / 2);
            ImGui.SetCursorPosY(headerY);
            ImGui.TextColored(colHeaderColor, "雇员");
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

        var rowStartX   = ImGui.GetCursorPosX() + 12;
        var rowY        = ImGui.GetCursorPosY();
        var textY       = rowY + (iconSize - ImGui.GetTextLineHeight()) / 2;
        var contentMaxX = ImGui.GetContentRegionMax().X;
        var nqColStart  = showRetainer ? contentMaxX - colWidth * 3 : contentMaxX - colWidth * 2;
        var hqColStart  = showRetainer ? contentMaxX - colWidth * 2 : contentMaxX - colWidth;

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
        var nameMaxWidth = nqColStart - nameStartX - 6f;
        ImGui.SetCursorPosX(nameStartX);
        ImGui.SetCursorPosY(textY);
        var name = item.Name.ExtractText();
        if (nameMaxWidth > 0 && ImGui.CalcTextSize(name).X > nameMaxWidth)
        {
            while (name.Length > 0 && ImGui.CalcTextSize(name + "...").X > nameMaxWidth)
                name = name[..^1];
            name += "...";
        }
        ImGui.Text(name);

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
            var retColStart = contentMaxX - colWidth;
            var retCount    = GetRetainerItemCount(itemId);
            var retStr      = retCount > 9999 ? "9999+" : $"{retCount}";
            ImGui.SetCursorPosX(retColStart + (colWidth - ImGui.CalcTextSize(retStr).X) / 2);
            ImGui.SetCursorPosY(textY);
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), retStr);
        }

        ImGui.SetCursorPosY(rowY + iconSize + ImGui.GetStyle().ItemSpacing.Y);
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
        return (int)RetainerCache.GetRetainerItemCount(itemId);
    }

    public class ExtendedRecipe
    {
        public Recipe Recipe;
        public ISharedImmediateTexture Icon = null!;
        public string Name = string.Empty;
        public string JobAbbreviation = string.Empty;
        public string JobName = string.Empty; // 新增 JobName
        public uint JobId;
        public uint Level;
        public uint ItemEquipLevel;
        public bool IsCrafted;
        internal bool _isFullyLoaded = false;

        public ExtendedRecipe(Recipe recipe, bool lazyLoad = false)
        {
            Recipe = recipe;
            if (lazyLoad)
                UpdateBasicInfo();
            else
                Update();
        }

        private void UpdateBasicInfo()
        {
            var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
            if (itemSheet != null && Recipe.ItemResult.RowId > 0)
            {
                var item = itemSheet.GetRow(Recipe.ItemResult.RowId);
                if (item.RowId > 0)
                {
                    Name = item.Name.ExtractText();
                    Icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                    ItemEquipLevel = (uint)item.LevelEquip;
                }
            }

            JobId = Recipe.CraftType.RowId + 8;
            Level = Recipe.RecipeLevelTable.Value.ClassJobLevel;

            var classJobSheet = Dalamud.GameData.GetExcelSheet<ClassJob>();
            if (classJobSheet != null)
            {
                var job = classJobSheet.GetRow(JobId);
                if (job.RowId > 0)
                {
                    JobAbbreviation = job.Abbreviation.ExtractText();
                    JobName = job.Name.ExtractText();
                }
            }

            _isFullyLoaded = false;
            
            UpdateCraftedStatus();
        }

        public unsafe void UpdateCraftedStatus()
        {
            try
            {
                IsCrafted = FFXIVClientStructs.FFXIV.Client.Game.QuestManager.IsRecipeComplete(Recipe.RowId);
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Debug($"[VulcanWindow] Failed to check crafted status for recipe {Recipe.RowId}: {ex.Message}");
                IsCrafted = false;
            }
        }

        public unsafe void Update()
        {
            var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
            if (itemSheet != null && Recipe.ItemResult.RowId > 0)
            {
                var item = itemSheet.GetRow(Recipe.ItemResult.RowId);
                if (item.RowId > 0)
                {
                    Name = item.Name.ExtractText();
                    Icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                    ItemEquipLevel = (uint)item.LevelEquip;
                }
            }

            JobId = Recipe.CraftType.RowId + 8;
            Level = Recipe.RecipeLevelTable.Value.ClassJobLevel;

            var classJobSheet = Dalamud.GameData.GetExcelSheet<ClassJob>();
            if (classJobSheet != null)
            {
                var job = classJobSheet.GetRow(JobId);
                if (job.RowId > 0)
                {
                    JobAbbreviation = job.Abbreviation.ExtractText();
                    JobName = job.Name.ExtractText();
                }
            }

            _isFullyLoaded = true;
            
            UpdateCraftedStatus();
        }

        public unsafe void SetTooltip(Vector2 iconSize)
        {
            using var tooltip = ImRaii.Tooltip();
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing * new Vector2(1f, 1.5f));

            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), Name);
            ImGui.Text($"配方 ID: {Recipe.RowId}");
            ImGui.Text($"等级: {Level} {JobName}"); // 默认 JobAbbreviation , 此处改为 JobName 显示中文
            ImGui.Text($"产出: {Recipe.AmountResult}x");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var directIngredients = RecipeManager.GetIngredients(Recipe);
            if (directIngredients.Count > 0)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.6f, 1.0f), "所需材料:");
                ImGui.Spacing();

                var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
                
                foreach (var (itemId, needed) in directIngredients)
                {
                    if (itemSheet == null || !itemSheet.TryGetRow(itemId, out var item))
                        continue;

                    var have = GetInventoryCount(itemId);
                    var icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                    
                    using var itemStyle = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing / 2);
                    
                    if (icon.TryGetWrap(out var wrap, out _))
                        ImGui.Image(wrap.Handle, iconSize * 0.75f);
                    else
                        ImGui.Dummy(iconSize * 0.75f);

                    ImGui.SameLine();

                    var color = have >= needed
                        ? new Vector4(0.3f, 1.0f, 0.3f, 1.0f)
                        : new Vector4(1.0f, 0.3f, 0.3f, 1.0f);

                    var itemName = item.Name.ExtractText();
                    ImGui.TextColored(color, $"{itemName}: {have} / {needed}");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "无需材料");
            }
            
            var settings = GatherBuddy.RecipeBrowserSettings.Get(Recipe.RowId);
            if (settings != null && settings.HasAnySettings())
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1.0f), "已配置设置:");
                ImGui.Spacing();
                
                var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
                if (itemSheet != null)
                {
                    if (settings.FoodItemId.HasValue && itemSheet.TryGetRow(settings.FoodItemId.Value, out var food))
                        ImGui.Text($"食物: {food.Name.ExtractText()}");
                    if (settings.MedicineItemId.HasValue && itemSheet.TryGetRow(settings.MedicineItemId.Value, out var medicine))
                        ImGui.Text($"药水: {medicine.Name.ExtractText()}");
                    if (settings.ManualItemId.HasValue && itemSheet.TryGetRow(settings.ManualItemId.Value, out var manual))
                        ImGui.Text($"指南: {manual.Name.ExtractText()}");
                    if (settings.SquadronManualItemId.HasValue && itemSheet.TryGetRow(settings.SquadronManualItemId.Value, out var sqManual))
                        ImGui.Text($"军用指南: {sqManual.Name.ExtractText()}");
                }
            }
        }

        private static unsafe int GetInventoryCount(uint itemId)
        {
            try
            {
                var inventory = InventoryManager.Instance();
                if (inventory == null)
                    return 0;

                var baseItemId = itemId >= 1_000_000 ? itemId - 1_000_000 : itemId;
                var hqItemId = baseItemId + 1_000_000;
                var total = 0;
                var inventories = new InventoryType[]
                {
                    InventoryType.Inventory1, InventoryType.Inventory2,
                    InventoryType.Inventory3, InventoryType.Inventory4,
                    InventoryType.Crystals
                };

                foreach (var invType in inventories)
                {
                    var container = inventory->GetInventoryContainer(invType);
                    if (container == null)
                        continue;

                    for (var i = 0; i < container->Size; i++)
                    {
                        var item = container->GetInventorySlot(i);
                        if (item == null || item->ItemId == 0)
                            continue;

                        if (item->ItemId == baseItemId || item->ItemId == hqItemId)
                            total += (int)item->Quantity;
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }
    }

    private sealed class RecipeTable : Table<ExtendedRecipe>
    {
        private static float _nameColumnWidth;
        private static float _jobColumnWidth;
        private static float _levelColumnWidth;
        private static float _craftedColumnWidth;
        private static float _globalScale;

        private static readonly NameColumn _nameColumn = new() { Label = "配方名称..." };
        private static readonly JobColumn _jobColumn = new() { Label = "职业" };
        private static readonly LevelColumn _levelColumn = new() { Label = "等级" };
        private static readonly CraftedColumn _craftedColumn = new() { Label = "已制作" };

        protected override void PreDraw()
        {
            if (_globalScale != ImGuiHelpers.GlobalScale)
            {
                _globalScale = ImGuiHelpers.GlobalScale;
                _nameColumnWidth = Math.Max(300f, Items.Any() ? Items.Max(i => TextWidth(i.Name)) + ItemSpacing.X + LineIconSize.X : 300f) / Scale;
                _jobColumnWidth = TextWidth("刻木匠") / Scale + Table.ArrowWidth;
                _levelColumnWidth = TextWidth("100") / Scale + Table.ArrowWidth;
                _craftedColumnWidth = TextWidth("已制作") / Scale + Table.ArrowWidth;
            }
        }

        public RecipeTable()
            : base("##RecipeTable", _extendedRecipeList ?? new List<ExtendedRecipe>(), _nameColumn, _jobColumn, _levelColumn, _craftedColumn)
        {
            Sortable = true;
            Flags |= ImGuiTableFlags.Hideable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
            Flags &= ~ImGuiTableFlags.NoBordersInBodyUntilResize;
        }

        public void UpdateFilter()
        {
            FilterDirty = true;
        }

        private sealed class NameColumn : ColumnString<ExtendedRecipe>
        {
            public NameColumn()
                => Flags |= ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoReorder;

            public override string ToName(ExtendedRecipe item)
                => item.Name;

            public override float Width
                => _nameColumnWidth * ImGuiHelpers.GlobalScale;

            public override bool DrawFilter()
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Label);
                return false;
            }

            public override void DrawColumn(ExtendedRecipe item, int id)
            {
                using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ItemSpacing / 2);
                
                if (item.Icon.TryGetWrap(out var wrap, out _))
                    ImGui.Image(wrap.Handle, LineIconSize);
                else
                    ImGui.Dummy(LineIconSize);
                
                ImGui.SameLine();
                
                var hasSettings = GatherBuddy.RecipeBrowserSettings.Has(item.Recipe.RowId);
                if (hasSettings)
                {
                    using var font = ImRaii.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.9f, 1), FontAwesomeIcon.Cog.ToIconString());
                    ImGui.SameLine();
                }
                
                var selected = ImGui.Selectable(item.Name);
                var hovered = ImGui.IsItemHovered();

                var isPopupOpen = GatherBuddy.ControllerSupport != null
                    ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"RecipeContextMenu##{item.Recipe.RowId}", Dalamud.GamepadState)
                    : ImGui.BeginPopupContextItem($"RecipeContextMenu##{item.Recipe.RowId}");
                
                if (isPopupOpen)
                {
                    if (ImGui.MenuItem("配置制作设置"))
                    {
                        _craftSettingsPopup.Open(item.Recipe.RowId, item.Name);
                    }
                    
                    if (ImGui.MenuItem("显示配方属性 (调试)"))
                    {
                        GatherBuddy.Log.Information($"=== Recipe Properties for {item.Name} ===");
                        GatherBuddy.Log.Information($"Recipe.RowId: {item.Recipe.RowId}");
                        GatherBuddy.Log.Information($"Recipe.Quest.RowId: {item.Recipe.Quest.RowId}");
                        GatherBuddy.Log.Information($"Recipe.IsSecondary: {item.Recipe.IsSecondary}");
                        GatherBuddy.Log.Information($"Recipe.IsExpert: {item.Recipe.IsExpert}");
                        GatherBuddy.Log.Information($"Recipe.SecretRecipeBook.RowId: {item.Recipe.SecretRecipeBook.RowId}");
                        GatherBuddy.Log.Information($"Recipe.CanQuickSynth: {item.Recipe.CanQuickSynth}");
                        GatherBuddy.Log.Information($"Recipe.CanHq: {item.Recipe.CanHq}");
                        GatherBuddy.Log.Information($"Recipe.IsSpecializationRequired: {item.Recipe.IsSpecializationRequired}");
                        GatherBuddy.Log.Information($"Recipe.DifficultyFactor: {item.Recipe.DifficultyFactor}");
                        GatherBuddy.Log.Information($"Recipe.QualityFactor: {item.Recipe.QualityFactor}");
                        GatherBuddy.Log.Information($"Recipe.RecipeLevelTable.RowId: {item.Recipe.RecipeLevelTable.RowId}");
                        var resultItem = item.Recipe.ItemResult.Value;
                        GatherBuddy.Log.Information($"Item.RowId: {resultItem.RowId}");
                        GatherBuddy.Log.Information($"Item.AlwaysCollectable: {resultItem.AlwaysCollectable}");
                        GatherBuddy.Log.Information($"Item.IsUnique: {resultItem.IsUnique}");
                        GatherBuddy.Log.Information($"Item.IsUntradable: {resultItem.IsUntradable}");
                        GatherBuddy.Log.Information($"Item.ItemSearchCategory.RowId: {resultItem.ItemSearchCategory.RowId}");
                        GatherBuddy.Log.Information($"Item.ItemUICategory.RowId: {resultItem.ItemUICategory.RowId}");
                        GatherBuddy.Log.Information($"Item.Rarity: {resultItem.Rarity}");
                    }
                    
                    ImGui.Separator();
                    
                    var lists = GatherBuddy.CraftingListManager.Lists;
                    
                    if (lists.Count > 0)
                    {
                        ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.6f, 1.0f), $"添加 {item.Name} 到清单:");
                        ImGui.Separator();
                        
                        foreach (var list in lists)
                        {
                            if (ImGui.MenuItem(list.Name))
                            {
                                list.Recipes.Add(new CraftingListItem(item.Recipe.RowId, 1));
                                GatherBuddy.CraftingListManager.SaveList(list);
                                GatherBuddy.Log.Information($"Added {item.Name} to crafting list '{list.Name}'");
                            }
                        }
                        
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();
                        
                        ImGui.TextColored(new Vector4(0.6f, 0.9f, 1.0f, 1.0f), "将所有未制作(已筛选)添加到:");
                        ImGui.Separator();
                        
                        foreach (var list in lists)
                        {
                            if (ImGui.MenuItem($"{list.Name} (批量)##bulk_{list.ID}"))
                            {
                                var uncraftedCount = 0;
                                if (_recipeTable != null)
                                {
                                    foreach (var (recipe, _) in _recipeTable.GetFilteredItems())
                                    {
                                        if (!recipe.IsCrafted)
                                        {
                                            list.Recipes.Add(new CraftingListItem(recipe.Recipe.RowId, 1));
                                            uncraftedCount++;
                                        }
                                    }
                                }
                                GatherBuddy.CraftingListManager.SaveList(list);
                                GatherBuddy.Log.Information($"Added {uncraftedCount} uncrafted recipes to list '{list.Name}'");
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("没有可用的制作清单");
                        ImGui.TextDisabled("请在制作清单标签页中创建一个");
                    }
                    
                    ImGui.EndPopup();
                }

                if (selected)
                {
                    SwitchJobIfNeeded(item.JobId);
                    StartCraftWithRaphael(item.Recipe);
                }

                if (hovered)
                {
                    item.SetTooltip(IconSize);
                }
            }

            public override bool FilterFunc(ExtendedRecipe item)
            {
                if (!string.IsNullOrWhiteSpace(_recipeSearchText))
                {
                    if (!item.Name.Contains(_recipeSearchText, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (_selectedJobFilters.Count > 0)
                {
                    if (!_selectedJobFilters.Contains(item.JobId))
                        return false;
                }

                if (item.Level < _minLevel || item.Level > _maxLevel)
                    return false;
                
                if (_filterBrowserRegularOnly)
                {
                    if (item.Recipe.SecretRecipeBook.RowId > 0 ||
                        item.Recipe.ItemResult.Value.AlwaysCollectable ||
                        item.Recipe.IsExpert ||
                        item.Recipe.ItemResult.Value.ItemSearchCategory.RowId == 0)
                        return false;
                }
                else
                {
                    if (_filterBrowserMasterRecipes && item.Recipe.SecretRecipeBook.RowId == 0)
                        return false;
                    
                    if (_filterBrowserCollectables && !item.Recipe.ItemResult.Value.AlwaysCollectable)
                        return false;
                    
                    if (_filterBrowserExpertRecipes && !item.Recipe.IsExpert)
                        return false;
                    
                    if (_filterBrowserQuestRecipes && item.Recipe.ItemResult.Value.ItemSearchCategory.RowId != 0)
                        return false;
                }

                return true;
            }
        }

        private sealed class JobColumn : ColumnString<ExtendedRecipe>
        {
            public override string ToName(ExtendedRecipe item)
                => item.JobName; // 默认 JobAbbreviation , 此处改为 JobName 显示中文

            public override float Width
                => _jobColumnWidth * ImGuiHelpers.GlobalScale;

            public override bool DrawFilter()
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Label);
                return false;
            }

            public override void DrawColumn(ExtendedRecipe item, int _)
            {
                ImGui.Text(item.JobName); // 默认 JobAbbreviation , 此处改为 JobName 显示中文
            }

            public override int Compare(ExtendedRecipe lhs, ExtendedRecipe rhs)
                => lhs.JobId.CompareTo(rhs.JobId);
        }

        private sealed class LevelColumn : Column<ExtendedRecipe>
        {
            public override float Width
                => _levelColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ExtendedRecipe item, int _)
            {
                ImGui.Text(item.Level.ToString());
            }

            public override int Compare(ExtendedRecipe lhs, ExtendedRecipe rhs)
                => lhs.Level.CompareTo(rhs.Level);
        }

        private sealed class CraftedColumn : Column<ExtendedRecipe>
        {
            public override float Width
                => _craftedColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ExtendedRecipe item, int _)
            {
                using var font = ImRaii.PushFont(UiBuilder.IconFont);
                if (item.IsCrafted)
                {
                    using var color = ImRaii.PushColor(ImGuiCol.Text, 0xFF008000);
                    ImGuiUtil.Center(FontAwesomeIcon.Check.ToIconString());
                }
                else
                {
                    using var color = ImRaii.PushColor(ImGuiCol.Text, 0xFF000080);
                    ImGuiUtil.Center(FontAwesomeIcon.Times.ToIconString());
                }
            }

            public override int Compare(ExtendedRecipe lhs, ExtendedRecipe rhs)
            {
                if (lhs.IsCrafted != rhs.IsCrafted)
                    return rhs.IsCrafted.CompareTo(lhs.IsCrafted);
                return 0;
            }
        }


    }
}
