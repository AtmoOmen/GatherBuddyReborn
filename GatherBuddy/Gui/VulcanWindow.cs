using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using ElliLib.Raii;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow : Window, IDisposable
{
    // Shared state
    private CraftingListDefinition? _editingList = null;
    private CraftingListEditor? _listEditor = null;
    private bool _deferEditorDraw = false;

    private bool _isMinimized = false;
    private bool _wasFocusedLastFrame = false;
    
    // TeamCraft import state
    private bool _showTeamCraftImport = false;
    private string _teamCraftListName = string.Empty;
    private string _teamCraftPrecrafts = string.Empty;
    private string _teamCraftFinalItems = string.Empty;
    
    // Debug tab state
    private uint _debugSelectedJobId = 8;
    private string? _debugLastTestResult;
    private string _repairNPCSearchInput = "";

    public CraftingListDefinition? CurrentCraftingList
        => _editingList;

    public VulcanWindow() : base("Vulcan - 制作###VulcanWindow")
    {
        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        
        CraftingGameInterop.CraftFinished += OnCraftFinished;
    }
    
    private void OnCraftFinished(Recipe? recipe, bool cancelled)
    {
        if (!cancelled && recipe != null)
        {
            _craftedStatusDirty = true;
        }
    }
    
    private void MinimizeWindow()
    {
        _isMinimized = true;
        IsOpen = false;
    }
    
    public void RestoreWindow()
    {
        _isMinimized = false;
        IsOpen = true;
    }

    public void OpenToList(string argument)
    {
        CraftingListDefinition? list;
        if (int.TryParse(argument, out var listId))
            list = GatherBuddy.CraftingListManager.GetListByID(listId);
        else
            list = GatherBuddy.CraftingListManager.GetListByName(argument);

        if (list == null)
        {
            GatherBuddy.Log.Warning($"[VulcanWindow] OpenToList: No list found matching '{argument}'");
            _isMinimized = false;
            IsOpen = true;
            return;
        }

        _isMinimized = false;
        IsOpen = true;
        _editingList = list;
        _listEditor = new CraftingListEditor(list);
        _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
        GatherBuddy.CraftingMaterialsWindow?.SetEditor(_listEditor);
        _deferEditorDraw = true;
    }

    public override void PreDraw()
    {
        if (!IsOpen)
            return;
    }

    public override void Draw()
    {
        GatherBuddy.ControllerSupport?.TabNavigation.Update(Dalamud.GamepadState, 7);
        
        // Track window focus for controller input blocking
        var isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (isFocused)
        {
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow("Vulcan - Crafting###VulcanWindow");
            _wasFocusedLastFrame = true;
        }
        else if (_wasFocusedLastFrame)
        {
            // We just lost focus, clear it
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow(null);
            _wasFocusedLastFrame = false;
        }
        
        ImGui.Text("制作系统");
        ImGui.Separator();

        using (var tab = ImRaii.TabBar("VulcanTabs###VulcanTabs", ImGuiTabBarFlags.None))
        {
            if (tab)
            {
                DrawCraftingListsTab();
                DrawCraftingTab();
                DrawMacrosTab();
                DrawStandardSolverConfigTab();
                DrawSolutionsTab();
                DrawSettingsTab();
                DrawDebugTab();
            }
        }
        
        _craftSettingsPopup.Draw();
        
        GatherBuddy.ControllerSupport?.UpdateEndOfFrame();
    }

    private void DrawCraftingListsTab()
    {
        if (GatherBuddy.ControllerSupport != null)
        {
            using var tabItem = GatherBuddy.ControllerSupport.TabNavigation.TabItem("制作清单##craftingListsTab", 0, 7);
            if (!tabItem)
                return;
            DrawCraftingListsTabContent();
        }
        else
        {
            using var tabItem = ImRaii.TabItem("制作清单##craftingListsTab");
            if (!tabItem)
                return;
            DrawCraftingListsTabContent();
        }
    }
    
    private void DrawCraftingListsTabContent()
    {

        if (_editingList != null && _listEditor != null)
        {
            if (_deferEditorDraw)
            {
                _deferEditorDraw = false;
                ImGui.Text("加载中...");
                return;
            }
            
            var refreshedList = GatherBuddy.CraftingListManager.GetListByID(_editingList.ID);
            if (refreshedList == null)
            {
                _editingList = null;
                GatherBuddy.CraftingMaterialsWindow?.SetEditor(null);
                _listEditor = null;
                DrawListManager();
                return;
            }

            _editingList = refreshedList;
            
            ImGui.Text($"正在编辑: {_editingList.Name}");
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("返回到清单管理", new Vector2(200, 0)))
            {
                _editingList = null;
                _listEditor = null;
            }

            ImGui.Spacing();
            if (_listEditor != null)
            {
                _listEditor.Draw();
            }
        }
        else
        {
            DrawListManager();
        }
    }

    private void DrawListManager()
    {
        ImGui.Text("制作清单");
        ImGui.Spacing();

        if (ImGui.Button("创建新清单", new Vector2(130, 0)))
        {
            // Will show input dialog
            ImGui.OpenPopup("CreateListPopup");
        }
        
        ImGui.SameLine();
        if (ImGui.Button("TeamCraft 导入", new Vector2(130, 0)))
        {
            _showTeamCraftImport = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("导入清单", new Vector2(130, 0)))
        {
            _importListText  = string.Empty;
            _importListError = null;
            ImGui.OpenPopup("ImportListPopup");
        }

        ImGui.Spacing();

        if (GatherBuddy.CraftingListManager.Lists.Count > 0)
        {
            ImGui.Text("已保存清单:");
            ImGui.Spacing();

            using var indent = ImRaii.PushIndent();
            var lists = GatherBuddy.CraftingListManager.Lists.ToList();
            foreach (var list in lists)
            {
            var selectableLabel = $"{list.Name} ({list.Recipes.Count} 个配方)";
                var selectableWidth = ImGui.CalcTextSize(selectableLabel).X + ImGui.GetStyle().ItemSpacing.X;
                if (ImGui.Selectable($"{selectableLabel}##list_{list.ID}", false, ImGuiSelectableFlags.None, new Vector2(selectableWidth, 0)))
                {
                    _editingList = list;
                    _listEditor = new CraftingListEditor(list);
                    _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
                    GatherBuddy.CraftingMaterialsWindow?.SetEditor(_listEditor);
                    _deferEditorDraw = true;
                }
                
                var isPopupOpen = GatherBuddy.ControllerSupport != null
                    ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"ListContextMenu_{list.ID}", Dalamud.GamepadState)
                    : ImGui.BeginPopupContextItem($"ListContextMenu_{list.ID}");
                
                if (isPopupOpen)
                {
                    if (ImGui.Selectable("编辑"))
                    {
                        _editingList = list;
                        _listEditor = new CraftingListEditor(list);
                        _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
                        GatherBuddy.CraftingMaterialsWindow?.SetEditor(_listEditor);
                        _deferEditorDraw = true;
                    }

                    if (ImGui.Selectable("开始"))
                    {
                        StartCraftingList(list);
                    }

                    if (ImGui.Selectable("导入到剪贴板"))
                    {
                        var exported = GatherBuddy.CraftingListManager.ExportList(list.ID);
                        if (exported != null)
                        {
                            ImGui.SetClipboardText(exported);
                            GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to clipboard");
                        }
                    }

                    ImGui.Separator();

                    if (ImGui.Selectable("删除"))
                    {
                        GatherBuddy.CraftingListManager.DeleteList(list.ID);
                    }

                    ImGui.EndPopup();
                }
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "尚未创建任何制作清单, 请点击\"创建新清单\"按钮以开始创建。");
        }

        DrawCreateListPopup();
        DrawImportListPopup();
        DrawTeamCraftImportWindow();
    }

    private string _newListName    = string.Empty;
    private string _importListText  = string.Empty;
    private string? _importListError = null;

    private void DrawImportListPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(540, 260), ImGuiCond.FirstUseEver);
        if (!ImGui.BeginPopupModal("ImportListPopup", ImGuiWindowFlags.None))
            return;

        ImGui.TextWrapped("在下方粘贴导出的制作清单字符串, 然后点击导入。");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##importListText", ref _importListText, 65536, new Vector2(-1, 120));

        if (_importListError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudRed, _importListError);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (ElliLib.Raii.ImRaii.Disabled(string.IsNullOrWhiteSpace(_importListText)))
        {
            if (ImGui.Button("导入", new Vector2(120, 0)))
            {
                var (imported, error) = GatherBuddy.CraftingListManager.ImportList(_importListText);
                if (imported != null)
                {
                    _editingList = imported;
                    _listEditor  = new CraftingListEditor(imported);
                    _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
                    GatherBuddy.CraftingMaterialsWindow?.SetEditor(_listEditor);
                    _deferEditorDraw = true;
                    _importListText  = string.Empty;
                    _importListError = null;
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _importListError = error;
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("取消", new Vector2(100, 0)))
        {
            _importListText  = string.Empty;
            _importListError = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawCreateListPopup()
    {
        if (ImGui.BeginPopupModal("CreateListPopup", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("输入清单名称:");
            ImGui.InputText("##newListName", ref _newListName, 256);

            if (!string.IsNullOrWhiteSpace(_newListName) && !GatherBuddy.CraftingListManager.IsNameUnique(_newListName))
            {
                ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "已存在同名的制作清单。");
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "插件将会自动重命名。");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("创建", new Vector2(100, 0)) && !string.IsNullOrWhiteSpace(_newListName))
            {
                var newList = GatherBuddy.CraftingListManager.CreateNewList(_newListName);
                _editingList = newList;
                _listEditor = new CraftingListEditor(newList);
                _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
                GatherBuddy.CraftingMaterialsWindow?.SetEditor(_listEditor);
                _deferEditorDraw = true;
                _newListName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消", new Vector2(100, 0)))
            {
                _newListName = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void StartCraftingList(CraftingListDefinition list)
    {
        if (list.Recipes.Count == 0)
        {
            GatherBuddy.Log.Warning("[VulcanWindow] Cannot start empty list");
            return;
        }

        var craftingQueue = new CraftingListQueue();
        foreach (var item in list.Recipes)
        {
            if (!item.Options.Skipping)
            {
                craftingQueue.AddRecipeWithPrecrafts(item.RecipeId, item.Quantity, list.SkipIfEnough);
            }
        }

        craftingQueue.BuildExpandedList();
        var sortedRecipes = GetRecipesInDependencyOrder(craftingQueue.Recipes, craftingQueue.OriginalRecipes);
        
        var expandedQueue = new List<CraftingListItem>();
        foreach (var recipeItem in sortedRecipes)
        {
            var originalItem = list.Recipes.FirstOrDefault(r => r.RecipeId == recipeItem.RecipeId);
            var recipeOptions = list.GetRecipeOptions(recipeItem.RecipeId);
            var isOriginal = originalItem != null;
            
            for (int i = 0; i < recipeItem.Quantity; i++)
            {
                var queueItem = new CraftingListItem(recipeItem.RecipeId, 1);
                
                queueItem.Options.NQOnly = recipeOptions.NQOnly;
                if (list.QuickSynthAll)
                {
                    var recipeData = RecipeManager.GetRecipe(recipeItem.RecipeId);
                    if (recipeData?.CanQuickSynth == true)
                        queueItem.Options.NQOnly = true;
                }
                queueItem.Options.Skipping = recipeOptions.Skipping;
                queueItem.IsOriginalRecipe = isOriginal;
                
                if (originalItem != null)
                {
                    queueItem.ConsumableOverrides = originalItem.ConsumableOverrides.Clone();
                }
                var craftSettings = originalItem?.CraftSettings ?? list.PrecraftCraftSettings.GetValueOrDefault(recipeItem.RecipeId);
                var effectiveMacroId = ResolveEffectiveMacroId(craftSettings, !isOriginal, list);
                if (craftSettings != null)
                {
                    queueItem.CraftSettings = new RecipeCraftSettings
                    {
                        FoodMode = craftSettings.FoodMode,
                        FoodItemId = craftSettings.FoodItemId,
                        FoodHQ = craftSettings.FoodHQ,
                        MedicineMode = craftSettings.MedicineMode,
                        MedicineItemId = craftSettings.MedicineItemId,
                        MedicineHQ = craftSettings.MedicineHQ,
                        ManualMode = craftSettings.ManualMode,
                        ManualItemId = craftSettings.ManualItemId,
                        SquadronManualMode = craftSettings.SquadronManualMode,
                        SquadronManualItemId = craftSettings.SquadronManualItemId,
                        IngredientPreferences = new Dictionary<uint, int>(craftSettings.IngredientPreferences),
                        UseAllNQ = craftSettings.UseAllNQ,
                        SelectedMacroId = effectiveMacroId,
                        SolverOverride = craftSettings.SolverOverride,
                    };
                }
                else if (effectiveMacroId != null)
                {
                    queueItem.CraftSettings = new RecipeCraftSettings { SelectedMacroId = effectiveMacroId };
                }
                if (originalItem != null)
                {
                    var topLevelPrefs = originalItem.IngredientPreferences;
                    var craftSettingsPrefs = originalItem.CraftSettings?.IngredientPreferences;
                    var effectivePrefs = topLevelPrefs.Count > 0 ? topLevelPrefs : craftSettingsPrefs;
                    if (effectivePrefs != null && effectivePrefs.Count > 0)
                        queueItem.IngredientPreferences = new Dictionary<uint, int>(effectivePrefs);
                }
                expandedQueue.Add(queueItem);
            }
        }
        
        var materials = list.ListMaterials();
        
        GatherBuddy.Log.Information($"[VulcanWindow] Starting crafting list '{list.Name}' with {expandedQueue.Count} crafts from {sortedRecipes.Count} recipes");
        CraftingGatherBridge.StartQueueCraftAndGather(expandedQueue, materials, list.Consumables, list.SkipIfEnough);
    }

    private List<CraftingListItem> GetRecipesInDependencyOrder(List<CraftingListItem> recipes, List<CraftingListItem> originalRecipesList)
    {
        var originalRecipes = new HashSet<uint>();
        
        foreach (var item in originalRecipesList)
        {
            originalRecipes.Add(item.RecipeId);
        }
        
        var precrafts = new List<CraftingListItem>();
        var finalProducts = new List<CraftingListItem>();
        
        foreach (var recipe in recipes)
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
        
        var result = new List<CraftingListItem>();
        var processed = new HashSet<uint>();
        
        var precraftsByJob = precrafts
            .GroupBy(r => RecipeManager.GetRecipe(r.RecipeId)?.CraftType.RowId ?? uint.MaxValue)
            .OrderBy(g => g.Key);
        
        foreach (var jobGroup in precraftsByJob)
        {
            var jobRecipes = jobGroup.ToList();
            foreach (var recipeItem in jobRecipes)
            {
                ProcessRecipeWithDependencies(recipeItem, recipes, processed, result);
            }
        }
        
        var sortedFinalProducts = finalProducts
            .GroupBy(r => RecipeManager.GetRecipe(r.RecipeId)?.CraftType.RowId ?? uint.MaxValue)
            .OrderBy(g => g.Key)
            .SelectMany(g => g)
            .ToList();
        
        foreach (var recipeItem in sortedFinalProducts)
        {
            if (!processed.Contains(recipeItem.RecipeId))
            {
                processed.Add(recipeItem.RecipeId);
                result.Add(recipeItem);
            }
        }
        
        return result;
    }

    private static string? ResolveEffectiveMacroId(RecipeCraftSettings? settings, bool isPrecraft, CraftingListDefinition list)
    {
        var isSpecific = settings?.MacroMode == MacroOverrideMode.Specific
            || (settings?.MacroMode == MacroOverrideMode.Inherit && !string.IsNullOrEmpty(settings?.SelectedMacroId));
        if (isSpecific)
            return settings?.SelectedMacroId;
        return isPrecraft ? list.DefaultPrecraftMacroId : list.DefaultFinalMacroId;
    }

    private void ProcessRecipeWithDependencies(CraftingListItem recipeItem, List<CraftingListItem> allRecipes, HashSet<uint> processed, List<CraftingListItem> result)
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
                    ProcessRecipeWithDependencies(depItem, allRecipes, processed, result);
                }
            }
        }
        
        processed.Add(recipeItem.RecipeId);
        result.Add(recipeItem);
    }

    private string GetCraftingJobName(uint craftTypeId)
    {
        var classJobSheet = Dalamud.GameData.GetExcelSheet<ClassJob>();
        if (classJobSheet != null)
        {
            var classJobId = craftTypeId + 8;
            var classJob = classJobSheet.GetRow(classJobId);
            if (classJob.RowId > 0)
                return classJob.Abbreviation.ExtractText();
        }
        return "Unknown";
    }

    private unsafe int GetInventoryCount(uint itemId)
    {
        try
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
                return 0;
            var total = 0;
            var baseItemId = itemId >= 1_000_000 ? itemId - 1_000_000 : itemId;
            var hqItemId = baseItemId + 1_000_000;
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

    private void DrawStandardSolverConfigTab()
    {
        IDisposable tabItem;
        bool tabOpen;
        
        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("标准求解器##standardSolverTab", 3, 7);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("标准求解器##standardSolverTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }
        
        using (tabItem)
        {
            if (!tabOpen)
                return;

        var config = GatherBuddy.Config.StandardSolverConfig;

        ImGui.Text("标准求解器配置");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "配置动态的标准求解器行为");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("秘诀设置");
        ImGui.Spacing();
        
        var useTricksGood = config.UseTricksGood;
        if (ImGui.Checkbox("在 高品质 时使用 秘诀 技能", ref useTricksGood))
        {
            config.UseTricksGood = useTricksGood;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("状态为 高品质 时使用 秘诀 技能");

        var useTricksExcellent = config.UseTricksExcellent;
        if (ImGui.Checkbox("在 最高品质 时使用 秘诀 技能", ref useTricksExcellent))
        {
            config.UseTricksExcellent = useTricksExcellent;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("状态为 最高品质 时使用 秘诀 技能");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("品质设置");
        ImGui.Spacing();

        var maxPercentage = config.MaxPercentage;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("目标 HQ %##maxPercentage", ref maxPercentage, 0, 100))
        {
            config.MaxPercentage = maxPercentage;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("普通制作的目标 HQ 百分比(0-100)");

        var useQualityStarter = config.UseQualityStarter;
        if (ImGui.Checkbox("使用品质起手(闲静)", ref useQualityStarter))
        {
            config.UseQualityStarter = useQualityStarter;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("在开局使用 闲静 技能提升品质, 而不是 坚信 技能提升进展");

        var maxIQPrepTouch = config.MaxIQPrepTouch;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("使用坯料加工的最大内静层数##maxIQPrepTouch", ref maxIQPrepTouch, 0, 10))
        {
            config.MaxIQPrepTouch = maxIQPrepTouch;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("使用坯料加工前所需的最大内静层数");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("收藏品设置");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(200);
        var collectibleModes = new[] { "1档 (最低)", "2档 (中等)", "3档 (最高)" };
        var collectibleMode = Math.Clamp(config.SolverCollectibleMode - 1, 0, collectibleModes.Length - 1);
        if (ImGui.Combo("目标收藏品质##collectibleMode", ref collectibleMode, collectibleModes, collectibleModes.Length))
        {
            config.SolverCollectibleMode = collectibleMode + 1;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("选择目标收藏品质 (1档 = 最低, 3档 = 最高)");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("专家技能设置");
        ImGui.Spacing();

        var useSpecialist = config.UseSpecialist;
        if (ImGui.Checkbox("使用专家技能", ref useSpecialist))
        {
            config.UseSpecialist = useSpecialist;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("在可用时使用 设计变动, 专心致志 技能");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("奇迹之材设置");
        ImGui.Spacing();

        var useMaterialMiracle = config.UseMaterialMiracle;
        if (ImGui.Checkbox("使用奇迹之材", ref useMaterialMiracle))
        {
            config.UseMaterialMiracle = useMaterialMiracle;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("在制作中使用 奇迹之材 技能");

        if (config.UseMaterialMiracle)
        {
            var minSteps = config.MinimumStepsBeforeMiracle;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("使用奇迹之材的最少工次##minSteps", ref minSteps, 1, 10))
            {
                config.MinimumStepsBeforeMiracle = minSteps;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("使用奇迹之材所需的最少制作工次");

            var materialMiracleMulti = config.MaterialMiracleMulti;
            if (ImGui.Checkbox("允许多次使用奇迹之材", ref materialMiracleMulti))
            {
                config.MaterialMiracleMulti = materialMiracleMulti;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("允许在一次制作中多次使用奇迹之材");
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("重置为默认设置", new Vector2(200, 0)))
        {
            GatherBuddy.Config.StandardSolverConfig = new Vulcan.StandardSolverConfig();
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("将所有标准求解器设置重置为默认值");
        }
    }

    private void DrawSettingsTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("设置##settingsTab", 5, 7);
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
            var modeNames = new[] { "纯 Raphael 求解器", "标准求解器" };
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("求解模式###SolverMode", modeNames[(int)currentMode]))
            {
                if (ImGui.Selectable("纯 Raphael 求解器", currentMode == RaphaelSolverMode.PureRaphael))
                {
                    raphaelConfig.SolverMode = RaphaelSolverMode.PureRaphael;
                    GatherBuddy.Config.Save();
                    CraftingGameInterop.ReloadSolvers();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("纯 Raphael 求解器: 由 Raphael 求解器生成的静态宏");
                    ImGui.TextUnformatted("结果稳定且最优");
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
                    ImGui.TextUnformatted("标准求解器: 基于 Artisan 的动态求解器");
                    ImGui.TextUnformatted("根据制作状态反应结果, 更加灵活");
                    ImGui.EndTooltip();
                }
                ImGui.EndCombo();
            }

            var delay = GatherBuddy.Config.VulcanExecutionDelayMs;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt("技能延迟 (ms)", ref delay, 0, 1000))
            {
                GatherBuddy.Config.VulcanExecutionDelayMs = Math.Clamp(delay, 0, 1000);
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每个制作技能之间的延迟时间 (0 = 立即执行, 最大 1000 ms)");

            DrawVulcanRepairConfig();

            DrawVulcanMateriaConfig();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Raphael 求解器");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.BeginGroup();
            ImGui.Text("  最大线程数: ");
            ImGui.SameLine();
            var maxConcurrent = raphaelConfig.MaxConcurrentRaphaelProcesses;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("###MaxConcurrent", ref maxConcurrent, 1, 1))
            {
                raphaelConfig.MaxConcurrentRaphaelProcesses = Math.Max(1, maxConcurrent);
                GatherBuddy.Config.Save();
            }

            ImGui.Text("  求解超时 (分钟): ");
            ImGui.SameLine();
            var timeoutMinutes = raphaelConfig.RaphaelTimeoutMinutes;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("###RaphaelTimeout", ref timeoutMinutes, 1, 1))
            {
                raphaelConfig.RaphaelTimeoutMinutes = Math.Max(1, Math.Min(60, timeoutMinutes));
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每次 Raphael 求解的超时时间(分钟), 超过此时间则判定失败并跳过制作。");

            ImGui.Text("  缓存最大时效 (天): ");
            ImGui.SameLine();
            var maxAgeDays = raphaelConfig.SolutionCacheMaxAgeDays;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("###CacheMaxAge", ref maxAgeDays, 1, 10))
            {
                raphaelConfig.SolutionCacheMaxAgeDays = Math.Max(1, Math.Min(365, maxAgeDays));
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("超过设定天数的 Raphael 缓存 将在插件加载时被清理。");

            ImGui.Spacing();
            var backloadProgress = raphaelConfig.RaphaelBackloadProgress;
            if (ImGui.Checkbox("  后置进展###RaphaelBackloadProgress", ref backloadProgress))
            {
                raphaelConfig.RaphaelBackloadProgress = backloadProgress;
                GatherBuddy.Config.Save();
                coordinator.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("仅在求解末尾使用提升进展的技能。\n可能降低求解品质, 禁用此选项可获得最大求解品质。\n修改此选项会清空 Raphael 缓存。");

            var allowSpecialist = raphaelConfig.RaphaelAllowSpecialistActions;
            if (ImGui.Checkbox("  允许专家技能###RaphaelAllowSpecialist", ref allowSpecialist))
            {
                raphaelConfig.RaphaelAllowSpecialistActions = allowSpecialist;
                GatherBuddy.Config.Save();
                coordinator.Clear();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("禁用时(默认), Raphael 求解器会生成非专家结果, 即使您是专家职业。\n仅在需要使用专家技能时启用。\n修改此选项会清空 Raphael 缓存。");

            var activeColor = coordinator.ActiveSolves > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            ImGui.TextColored(activeColor, $"  正在求解: {coordinator.ActiveSolves}/{raphaelConfig.MaxConcurrentRaphaelProcesses}");

            var pendingColor = coordinator.PendingSolves > 0 ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey;
            ImGui.TextColored(pendingColor, $"  等待中: {coordinator.PendingSolves}");

            var cachedColor = coordinator.CachedSolutionCount > 0 ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey;
            ImGui.TextColored(cachedColor, $"  已缓存: {coordinator.CachedSolutionCount}");

            if (ImGui.Button("清空缓存", new Vector2(150, 0)))
            {
                coordinator.Clear();
            }
            ImGui.EndGroup();
        }
    }

    private void DrawDebugTab()
    {
        IDisposable tabItem;
        bool tabOpen;
        
        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("Debug##debugTab", 6, 7);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("Debug##debugTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }
        
        using (tabItem)
        {
            if (!tabOpen)
                return;

        ImGui.BeginGroup();
        ImGui.Text("Context Menu 设置");
        ImGui.Spacing();

        ImGui.Text("  最近制作清单数量上限:");
        ImGui.SameLine();
        var maxRecentLists = GatherBuddy.Config.MaxRecentCraftingListsInContextMenu;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("###MaxRecentLists", ref maxRecentLists, 1, 1))
        {
            GatherBuddy.Config.MaxRecentCraftingListsInContextMenu = Math.Max(1, Math.Min(50, maxRecentLists));
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("在 Context Menu 中显示的最近制作清单数量上限 (1-50)Maximum number of recent crafting lists to show in context menus (1-50)");

        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("修理状态");
        ImGui.Spacing();
        ImGui.Text($"  最低装备耐久: {Crafting.RepairManager.GetMinEquippedPercent()}%");
        ImGui.Text($"  可自己修理: {Crafting.RepairManager.CanRepairAny()}");
        ImGui.Text($"  附近 NPC 修理: {Crafting.RepairManager.RepairNPCNearby(out _)}");
        if (Crafting.RepairManager.RepairNPCNearby(out _))
        {
            ImGui.Text($"  NPC 修理价格: {Crafting.RepairManager.GetNPCRepairPrice()} 金币");
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("精制魔晶石状态");
        ImGui.Spacing();
        ImGui.Text($"  已解锁精制魔晶石: {Crafting.MateriaManager.IsExtractionUnlocked()}");
        ImGui.Text($"  可精制物品数: {Crafting.MateriaManager.ReadySpiritbondItemCount()}");
        ImGui.Text($"  物品栏空位: {Crafting.MateriaManager.HasFreeInventorySlots()}");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("套装属性测试");
        ImGui.Text("  选择职业:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("###JobSelector", GetDebugJobName(_debugSelectedJobId)))
        {
            var jobs = new[] { (8u, "刻木匠 (CRP)"), (9u, "锻铁匠 (BSM)"), (10u, "铸甲匠 (ARM)"), (11u, "雕金匠 (GSM)"), (12u, "制革匠 (LTW)"), (13u, "裁衣匠 (WVR)"), (14u, "炼金术士 (ALC)"), (15u, "烹调师 (CUL)") };
            foreach (var (jobId, jobName) in jobs)
            {
                if (ImGui.Selectable(jobName, _debugSelectedJobId == jobId))
                {
                    _debugSelectedJobId = jobId;
                    _debugLastTestResult = null;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        if (ImGui.Button("测试属性读取", new Vector2(150, 0)))
        {
            var stats = GearsetStatsReader.ReadGearsetStatsForJob(_debugSelectedJobId);
            if (stats != null)
            {
                _debugLastTestResult = $"成功: 作业精度 = {stats.Craftsmanship}, 加工精度 = {stats.Control}, 制作力 = {stats.CP}, 掌握 = {stats.Manipulation}";
            }
            else
            {
                _debugLastTestResult = "失败: 无法读取该职业的套装属性";
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("刷新套装", new Vector2(150, 0)))
        {
            GearsetStatsReader.RefreshGearsetFromCurrentEquipped(_debugSelectedJobId);
            _debugLastTestResult = "已从当前装备刷新套装"; // Gearset refreshed from currently equipped items
            }

        if (_debugLastTestResult != null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped(_debugLastTestResult);
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawGamepadInputTest();
        }
    }
    
    private void DrawGamepadInputTest()
    {
        ImGui.BeginGroup();
        ImGui.Text("手柄输入测试");
        ImGui.Separator();
        ImGui.Spacing();
        
        var gamepad = Dalamud.GamepadState;
        
        ImGui.Text("左摇杆:");
        ImGui.SameLine();
        ImGui.Text($"X: {gamepad.LeftStick.X:F3}, Y: {gamepad.LeftStick.Y:F3}");
        
        ImGui.Text("右摇杆:");
        ImGui.SameLine();
        ImGui.Text($"X: {gamepad.RightStick.X:F3}, Y: {gamepad.RightStick.Y:F3}");
        
        ImGui.Spacing();
        ImGui.Text("方向键:");
        ImGui.SameLine();
        var dpad = "None";
        if (gamepad.Pressed(GamepadButtons.DpadUp) > 0) dpad = "上";
        if (gamepad.Pressed(GamepadButtons.DpadDown) > 0) dpad = "下";
        if (gamepad.Pressed(GamepadButtons.DpadLeft) > 0) dpad = "左";
        if (gamepad.Pressed(GamepadButtons.DpadRight) > 0) dpad = "右";
        ImGui.Text(dpad);
        
        ImGui.Spacing();
        ImGui.Text("正面按键:"); // Face Buttons
        var faceButtons = new List<string>();
        if (gamepad.Pressed(GamepadButtons.South) > 0) faceButtons.Add("A/×");
        if (gamepad.Pressed(GamepadButtons.East) > 0) faceButtons.Add("B/○");
        if (gamepad.Pressed(GamepadButtons.West) > 0) faceButtons.Add("X/□");
        if (gamepad.Pressed(GamepadButtons.North) > 0) faceButtons.Add("Y/△");
        ImGui.SameLine();
        ImGui.Text(faceButtons.Count > 0 ? string.Join(", ", faceButtons) : "无");
        
        ImGui.Spacing();
        ImGui.Text("肩键:");
        var shoulderButtons = new List<string>();
        if (gamepad.Pressed(GamepadButtons.L1) > 0) shoulderButtons.Add("L1");
        if (gamepad.Pressed(GamepadButtons.R1) > 0) shoulderButtons.Add("R1");
        if (gamepad.Pressed(GamepadButtons.L2) > 0) shoulderButtons.Add("L2");
        if (gamepad.Pressed(GamepadButtons.R2) > 0) shoulderButtons.Add("R2");
        ImGui.SameLine();
        ImGui.Text(shoulderButtons.Count > 0 ? string.Join(", ", shoulderButtons) : "无");
        
        ImGui.Spacing();
        ImGui.Text("ImGui 导航状态:");
        var io = ImGui.GetIO();
        ImGui.Text($"  导航激活: {io.NavActive}");
        ImGui.Text($"  导航可见: {io.NavVisible}");
        ImGui.Text($"  配置标志: {io.ConfigFlags}");
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        var navKeyboardEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        var navGamepadEnabled = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0;
        
        if (ImGui.Button(navGamepadEnabled ? "禁用手柄导航" : "启用手柄导航", new Vector2(200, 0)))
        {
            io = ImGui.GetIO();
            if (navGamepadEnabled)
            {
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableGamepad;
                GatherBuddy.Log.Information("[VulcanWindow] Disabled ImGui gamepad navigation");
            }
            else
            {
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
                GatherBuddy.Log.Information("[VulcanWindow] Enabled ImGui gamepad navigation");
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button(navKeyboardEnabled ? "禁用键盘导航" : "启用键盘导航", new Vector2(200, 0)))
        {
            io = ImGui.GetIO();
            if (navKeyboardEnabled)
            {
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
                GatherBuddy.Log.Information("[VulcanWindow] Disabled ImGui keyboard navigation");
            }
            else
            {
                io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
                GatherBuddy.Log.Information("[VulcanWindow] Enabled ImGui keyboard navigation");
            }
        }
        
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "提示: 按 Tab 或使用手柄方向键开始导航");
        
        ImGui.EndGroup();
    }


    private static string GetDebugJobName(uint jobId) => jobId switch
    {
        8 => "刻木匠 (CRP)",
        9 => "锻铁匠 (BSM)",
        10 => "铸甲匠 (ARM)",
        11 => "雕金匠 (GSM)",
        12 => "制革匠 (LTW)",
        13 => "裁衣匠 (WVR)",
        14 => "炼金术士 (ALC)",
        15 => "烹调师 (CUL)",
        _ => "未知"
    };
    
    private static string GetTerritoryName(uint territoryId)
    {
        var territorySheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        if (territorySheet?.TryGetRow(territoryId, out var territory) == true)
        {
            return territory.PlaceName.ValueNullable?.Name.ExtractText() ?? "未知";
        }
        return "未知";
    }

    private void DrawVulcanRepairConfig()
    {
        var config = GatherBuddy.Config.VulcanRepairConfig;

        var enabled = config.Enabled;
        if (ImGui.Checkbox("启用 修理", ref enabled))
        {
            config.Enabled = enabled;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("需要修理装备时, 自动在制作之间进行修理");

        var threshold = config.RepairThreshold;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderInt("修理阈值 (%)", ref threshold, 0, 99))
        {
            config.RepairThreshold = threshold;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("装备耐久度低于此百分比时进行修理");

        var prioritizeNPC = config.PrioritizeNPCRepair;
        if (ImGui.Checkbox("优先使用 NPC 修理", ref prioritizeNPC))
        {
            config.PrioritizeNPCRepair = prioritizeNPC;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("有足够金币时优先使用 NPC 修理, 否则自己进行修理");
        
        if (config.PrioritizeNPCRepair)
        {
            ImGui.Spacing();
            ImGui.Text("首选修理 NPC:");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("选择一个修理 NPC, 在需要修理时前往");
            
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
                    ImGui.SetTooltip("使用当前区域内的任意修理 NPC");
                
                var repairNPCs = Crafting.RepairNPCHelper.RepairNPCs;
                if (repairNPCs.Count == 0)
                {
                    ImGui.TextDisabled("修理 NPC 还未加载完毕...");
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
                        ImGui.TextDisabled("没有匹配的 NPC...");
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
        if (ImGui.Checkbox("启用 精制魔晶石", ref enabled))
        {
            config.Enabled = enabled;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("自动在制作之间对满精炼度的装备精制魔晶石");
    }

    private void DrawTeamCraftImportWindow()
    {
        if (!_showTeamCraftImport)
            return;
        
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.Appearing);
        if (ImGui.Begin("TeamCraft 导入###TCImport", ref _showTeamCraftImport, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped("从 FFXIV TeamCraft 导入制作清单:");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "重要提示:");
            ImGui.TextWrapped("只有当您的游戏客户端语言与 TeamCraft 使用的语言一致时, 此导入才会生效。");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.TextWrapped("步骤 1: 在 TeamCraft 打开您的清单");
            ImGui.TextWrapped("步骤 2: 找到\"半成品\"标签并点击\"复制为文本\"");
            ImGui.TextWrapped("步骤 3: 将内容粘贴到下方的半成品制作物品框中");
            ImGui.TextWrapped("步骤 4: 对\"成品\"部分重复以上操作");
            ImGui.TextWrapped("步骤 5: 给您的清单命名并点击导入");
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text("清单名称:");
            ImGui.SetNextItemWidth(550);
            ImGui.InputText("##ImportListName", ref _teamCraftListName, 256);
            
            ImGui.Spacing();
            ImGui.Text("半成品制作物品:");
            ImGui.InputTextMultiline("##PrecraftItems", ref _teamCraftPrecrafts, 500000, new Vector2(550, 150));
            
            ImGui.Spacing();
            ImGui.Text("成品制作物品:");
            ImGui.InputTextMultiline("##FinalItems", ref _teamCraftFinalItems, 500000, new Vector2(550, 150));
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (ImGui.Button("导入", new Vector2(100, 0)))
            {
                var importedList = ParseTeamCraftImport();
                if (importedList != null)
                {
                    _editingList = importedList;
                    _listEditor = new CraftingListEditor(importedList);
                    _listEditor.OnStartCrafting = (l) => { StartCraftingList(l); MinimizeWindow(); };
                    _listEditor.RefreshInventoryCounts();
                    
                    _teamCraftListName = string.Empty;
                    _teamCraftPrecrafts = string.Empty;
                    _teamCraftFinalItems = string.Empty;
                    _showTeamCraftImport = false;
                    
                    GatherBuddy.Log.Information($"[VulcanWindow] Successfully imported TeamCraft list: {importedList.Name}");
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button("取消", new Vector2(100, 0)))
            {
                _teamCraftListName = string.Empty;
                _teamCraftPrecrafts = string.Empty;
                _teamCraftFinalItems = string.Empty;
                _showTeamCraftImport = false;
            }
            
            ImGui.End();
        }
    }
    
    private CraftingListDefinition? ParseTeamCraftImport()
    {
        if (string.IsNullOrWhiteSpace(_teamCraftListName) && 
            string.IsNullOrWhiteSpace(_teamCraftPrecrafts) && 
            string.IsNullOrWhiteSpace(_teamCraftFinalItems))
        {
            GatherBuddy.Log.Warning("[VulcanWindow] TeamCraft import: All fields are empty");
            return null;
        }
        
        var recipesToAdd = new List<(uint recipeId, int quantity)>();
        
        ParseTeamCraftSection(_teamCraftPrecrafts, recipesToAdd);
        ParseTeamCraftSection(_teamCraftFinalItems, recipesToAdd);
        
        if (recipesToAdd.Count == 0)
        {
            GatherBuddy.Log.Warning("[VulcanWindow] TeamCraft import: No valid recipes found");
            return null;
        }
        
        var listName = string.IsNullOrWhiteSpace(_teamCraftListName) 
            ? "从 TeamCraft 导入"
            : _teamCraftListName;
        
        var newList = GatherBuddy.CraftingListManager.CreateNewList(listName);
        
        foreach (var (recipeId, quantity) in recipesToAdd)
        {
            var existingItem = newList.Recipes.FirstOrDefault(r => r.RecipeId == recipeId);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                newList.Recipes.Add(new CraftingListItem(recipeId, quantity));
            }
        }
        
        GatherBuddy.CraftingListManager.SaveList(newList);
        GatherBuddy.Log.Debug($"[VulcanWindow] TeamCraft import: Created list '{listName}' with {newList.Recipes.Count} unique recipes");
        
        return newList;
    }
    
    private void ParseTeamCraftSection(string text, List<(uint recipeId, int quantity)> output)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        
        using var reader = new System.IO.StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            
            if (parts[0].EndsWith('x'))
            {
                if (!int.TryParse(parts[0].Substring(0, parts[0].Length - 1), out int numberOfItems))
                    continue;
                
                var itemName = string.Join(" ", parts.Skip(1)).Trim();
                
                GatherBuddy.Log.Debug($"[VulcanWindow] TeamCraft import: Parsing {numberOfItems}x {itemName}");
                
                var recipeSheet = Dalamud.GameData.GetExcelSheet<Recipe>();
                if (recipeSheet == null)
                    continue;
                
                Recipe? foundRecipe = null;
                foreach (var recipe in recipeSheet)
                {
                    if (recipe.ItemResult.RowId > 0)
                    {
                        var item = recipe.ItemResult.Value;
                        if (item.Name.ExtractText() == itemName)
                        {
                            foundRecipe = recipe;
                            break;
                        }
                    }
                }
                
                if (foundRecipe != null)
                {
                    int craftsNeeded = (int)Math.Ceiling(numberOfItems / (double)foundRecipe.Value.AmountResult);
                    output.Add((foundRecipe.Value.RowId, craftsNeeded));
                    GatherBuddy.Log.Debug($"[VulcanWindow] TeamCraft import: Found recipe {foundRecipe.Value.RowId}, need {craftsNeeded} crafts for {numberOfItems} items");
                }
                else
                {
                    GatherBuddy.Log.Warning($"[VulcanWindow] TeamCraft import: Could not find recipe for item '{itemName}'");
                }
            }
        }
    }

    public void Dispose()
    {
        CraftingGameInterop.CraftFinished -= OnCraftFinished;
    }
}
