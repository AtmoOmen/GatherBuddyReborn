using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GatherBuddy.Gui;

public class CraftingListConsumablesPopup
{
    private bool _isOpen;
    private string _title = "";
    private CraftingListDefinition? _list;
    private CraftingListConsumableSettings _workingDefaults = new();
    private string? _workingDefaultPrecraftMacroId;
    private string? _workingDefaultFinalMacroId;
    private SolverOverrideMode _workingDefaultPrecraftSolverOverride = SolverOverrideMode.Default;
    private SolverOverrideMode _workingDefaultFinalSolverOverride = SolverOverrideMode.Default;
    private bool _workingUseAllHQ;
    private string _foodSearch = string.Empty;
    private string _medicineSearch = string.Empty;
    private string _precraftMacroSearch = string.Empty;
    private string _finalMacroSearch = string.Empty;
    private List<(uint ItemId, string Name, bool IsHQ)> _foodItems = new();
    private List<(uint ItemId, string Name, bool IsHQ)> _medicineItems = new();
    private List<(uint ItemId, string Name)> _manualItems = new();
    private List<(uint ItemId, string Name)> _squadronManualItems = new();
    public System.Action? OnSaved { get; set; }
    private static float SelectorLabelWidth => VulcanUiScaling.Scaled(160f);
    private static float SelectorComboWidth => VulcanUiScaling.Scaled(240f);
    private static Vector2 SaveButtonSize => VulcanUiScaling.Scaled(140f, 0f);
    private static Vector2 CancelButtonSize => VulcanUiScaling.Scaled(100f, 0f);

    public void OpenListDefaults(CraftingListDefinition list)
    {
        _list = list;
        _title = $"制作清单 消耗品/宏 - {list.Name}";
        _workingDefaults = list.Consumables.Clone();
        _workingDefaultPrecraftMacroId = list.DefaultPrecraftMacroId;
        _workingDefaultFinalMacroId = list.DefaultFinalMacroId;
        _workingDefaultPrecraftSolverOverride = list.DefaultPrecraftSolverOverride;
        _workingDefaultFinalSolverOverride = list.DefaultFinalSolverOverride;
        _workingUseAllHQ = list.UseAllHQ;
        _foodSearch = string.Empty;
        _medicineSearch = string.Empty;
        _precraftMacroSearch = string.Empty;
        _finalMacroSearch = string.Empty;
        EnsureConsumablesLoaded();
        _isOpen = true;
    }

    public void Draw()
    {
        if (!_isOpen)
            return;

        if (ImGui.Begin(_title, ref _isOpen, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            DrawListDefaults();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("保存为默认", SaveButtonSize))
            {
                Save();
            }

            ImGui.SameLine();
            if (ImGui.Button("取消", CancelButtonSize))
            {
                _isOpen = false;
            }

            ImGui.End();
        }
    }

    private void DrawListDefaults()
    {
        ImGui.Text("配置此清单的默认消耗品");
        ImGui.Spacing();
        DrawMacroSection();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Checkbox("Assume All HQ Materials##ListUseAllHQ", ref _workingUseAllHQ);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Use all HQ-capable ingredients as the list default for planning and Raphael warmup.\nItem and precraft settings can still override this.");

        ImGui.Spacing();

        var foodItemId = _workingDefaults.FoodItemId;
        var foodHq = _workingDefaults.FoodHQ;
        var medicineItemId = _workingDefaults.MedicineItemId;
        var medicineHq = _workingDefaults.MedicineHQ;
        var manualItemId = _workingDefaults.ManualItemId;
        var squadronManualItemId = _workingDefaults.SquadronManualItemId;

        DrawFoodSelector(ref foodItemId, ref foodHq, "##ListFood", ref _foodSearch);
        DrawMedicineSelector(ref medicineItemId, ref medicineHq, "##ListMedicine", ref _medicineSearch);
        DrawManualSelector(ref manualItemId, "##ListManual");
        DrawSquadronManualSelector(ref squadronManualItemId, "##ListSquadron");

        _workingDefaults.FoodItemId = foodItemId;
        _workingDefaults.FoodHQ = foodHq;
        _workingDefaults.MedicineItemId = medicineItemId;
        _workingDefaults.MedicineHQ = medicineHq;
        _workingDefaults.ManualItemId = manualItemId;
        _workingDefaults.SquadronManualItemId = squadronManualItemId;
    }

    private void DrawMacroSection()
    {
        var allMacros = CraftingGameInterop.UserMacroLibrary.GetAllMacros();
        DrawMacroSelector(
            "半成品制作宏:",
            "##PrecraftMacro",
            "##PrecraftMacroSearch",
            ref _workingDefaultPrecraftMacroId,
            ref _workingDefaultPrecraftSolverOverride,
            ref _precraftMacroSearch,
            allMacros);

        DrawMacroSelector(
            "成品制作宏:",
            "##FinalMacro",
            "##FinalMacroSearch",
            ref _workingDefaultFinalMacroId,
            ref _workingDefaultFinalSolverOverride,
            ref _finalMacroSearch,
            allMacros);
    }

    private void DrawMacroSelector(
        string label,
        string comboId,
        string searchId,
        ref string? selectedMacroId,
        ref SolverOverrideMode solverOverride,
        ref string macroSearch,
        List<UserMacro> allMacros)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.SameLine(SelectorLabelWidth);

        var currentName = GetMacroSelectionName(selectedMacroId, solverOverride, allMacros);
        ImGui.SetNextItemWidth(SelectorComboWidth);
        if (!ImGui.BeginCombo(comboId, currentName))
            return;

        var isDefault = solverOverride == SolverOverrideMode.Default && string.IsNullOrEmpty(selectedMacroId);
        if (ImGui.Selectable("默认 (使用求解器)", isDefault))
        {
            selectedMacroId = null;
            solverOverride = SolverOverrideMode.Default;
        }
        if (ImGui.Selectable("标准求解器", solverOverride == SolverOverrideMode.StandardSolver))
        {
            selectedMacroId = null;
            solverOverride = SolverOverrideMode.StandardSolver;
        }
        if (ImGui.Selectable("Raphael 求解器", solverOverride == SolverOverrideMode.RaphaelSolver))
        {
            selectedMacroId = null;
            solverOverride = SolverOverrideMode.RaphaelSolver;
        }
        if (ImGui.Selectable("只推进度", solverOverride == SolverOverrideMode.ProgressOnlySolver))
        {
            selectedMacroId = null;
            solverOverride = SolverOverrideMode.ProgressOnlySolver;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只使用推进度的技能, 不使用品质技能, 适合快速 NQ 制作");

        if (allMacros.Count > 0)
        {
            ImGui.Separator();
            ImGui.InputTextWithHint(searchId, "搜索宏...", ref macroSearch, 128);
            var searchValue = macroSearch;
            var filteredMacros = string.IsNullOrWhiteSpace(searchValue)
                ? allMacros
                : allMacros.Where(m => m.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var macro in filteredMacros)
            {
                var isSelected = selectedMacroId == macro.Id;
                var displayName = macro.MinCraftsmanship > 0 || macro.MinControl > 0 || macro.MinCP > 0
                    ? $"{macro.Name} ({macro.MinCraftsmanship}/{macro.MinControl}/{macro.MinCP})"
                    : macro.Name;
                if (ImGui.Selectable(displayName, isSelected))
                {
                    selectedMacroId = macro.Id;
                    solverOverride = SolverOverrideMode.Default;
                }
            }
        }

        ImGui.EndCombo();
    }

    private static string GetMacroSelectionName(string? macroId, SolverOverrideMode solverOverride, List<UserMacro> allMacros)
    {
        return solverOverride switch
        {
            SolverOverrideMode.StandardSolver     => "标准求解器",
            SolverOverrideMode.RaphaelSolver      => "Raphael 求解器",
            SolverOverrideMode.ProgressOnlySolver => "只推进度",
            _ when !string.IsNullOrEmpty(macroId) => allMacros.FirstOrDefault(m => m.Id == macroId)?.Name ?? "(未找到宏)",
            _                                     => "默认 (使用求解器)",
        };
    }

    private void DrawFoodSelector(ref uint? itemId, ref bool hq, string idSuffix, ref string search)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("食物:");
        ImGui.SameLine(SelectorLabelWidth);

        var current = GetDisplayName(itemId, hq);
        ImGui.SetNextItemWidth(SelectorComboWidth);
        if (ImGui.BeginCombo($"##Food{idSuffix}", current))
        {
            if (ImGui.Selectable("无", !itemId.HasValue))
            {
                itemId = null;
                hq = false;
            }

            ImGui.Separator();
            ImGui.InputTextWithHint($"##FoodSearch{idSuffix}", "搜索食物...", ref search, 128);

            var searchValue = search;
            var filtered = string.IsNullOrWhiteSpace(searchValue)
                ? _foodItems
                : _foodItems.Where(f => f.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var entry in filtered)
            {
                var displayName = entry.Name + (entry.IsHQ ? $" {(char)SeIconChar.HighQuality}" : "");
                var isSelected = itemId == entry.ItemId && hq == entry.IsHQ;
                if (ImGui.Selectable(displayName, isSelected))
                {
                    itemId = entry.ItemId;
                    hq = entry.IsHQ;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawMedicineSelector(ref uint? itemId, ref bool hq, string idSuffix, ref string search)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("药水:");
        ImGui.SameLine(SelectorLabelWidth);

        var current = GetDisplayName(itemId, hq);
        ImGui.SetNextItemWidth(SelectorComboWidth);
        if (ImGui.BeginCombo($"##Medicine{idSuffix}", current))
        {
            if (ImGui.Selectable("无", !itemId.HasValue))
            {
                itemId = null;
                hq = false;
            }

            ImGui.Separator();
            ImGui.InputTextWithHint($"##MedicineSearch{idSuffix}", "搜索药水...", ref search, 128);

            var searchValue = search;
            var filtered = string.IsNullOrWhiteSpace(searchValue)
                ? _medicineItems
                : _medicineItems.Where(m => m.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var entry in filtered)
            {
                var displayName = entry.Name + (entry.IsHQ ? $" {(char)SeIconChar.HighQuality}" : "");
                var isSelected = itemId == entry.ItemId && hq == entry.IsHQ;
                if (ImGui.Selectable(displayName, isSelected))
                {
                    itemId = entry.ItemId;
                    hq = entry.IsHQ;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawManualSelector(ref uint? itemId, string idSuffix)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("指南:");
        ImGui.SameLine(SelectorLabelWidth);

        var manualId = itemId ?? 0;
        var current = manualId == 0 ? "无" : GetItemName(manualId);
        ImGui.SetNextItemWidth(SelectorComboWidth);
        if (ImGui.BeginCombo($"##Manual{idSuffix}", current))
        {
            if (ImGui.Selectable("无", manualId == 0))
                itemId = null;

            foreach (var (id, name) in _manualItems)
            {
                var isSelected = id == manualId;
                if (ImGui.Selectable(name, isSelected))
                    itemId = id;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawSquadronManualSelector(ref uint? itemId, string idSuffix)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text("冒险者分队指南:");
        ImGui.SameLine(SelectorLabelWidth);

        var squadronId = itemId ?? 0;
        var current = squadronId == 0 ? "无" : GetItemName(squadronId);
        ImGui.SetNextItemWidth(SelectorComboWidth);
        if (ImGui.BeginCombo($"##Squadron{idSuffix}", current))
        {
            if (ImGui.Selectable("无", squadronId == 0))
                itemId = null;

            foreach (var (id, name) in _squadronManualItems)
            {
                var isSelected = id == squadronId;
                if (ImGui.Selectable(name, isSelected))
                    itemId = id;
            }

            ImGui.EndCombo();
        }
    }

    private void EnsureConsumablesLoaded()
    {
        if (_foodItems.Count > 0 || _medicineItems.Count > 0)
            return;

        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        if (itemSheet == null)
            return;

        foreach (var item in itemSheet)
        {
            if (IsCraftersFood(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _foodItems.Add((item.RowId, name, false));
                    if (item.CanBeHq)
                        _foodItems.Add((item.RowId, name, true));
                }
            }
            else if (IsCraftersPot(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _medicineItems.Add((item.RowId, name, false));
                    if (item.CanBeHq)
                        _medicineItems.Add((item.RowId, name, true));
                }
            }
            else if (IsCraftersManual(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _manualItems.Add((item.RowId, name));
                }
            }
            else if (IsSquadronManual(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _squadronManualItems.Add((item.RowId, name));
                }
            }
        }

        _foodItems = _foodItems
            .OrderBy(f => !HasItemInInventory(f.ItemId, f.IsHQ))
            .ThenBy(f => f.Name)
            .ThenBy(f => f.IsHQ ? 0 : 1)
            .ToList();
        _medicineItems = _medicineItems
            .OrderBy(m => !HasItemInInventory(m.ItemId, m.IsHQ))
            .ThenBy(m => m.Name)
            .ThenBy(m => m.IsHQ ? 0 : 1)
            .ToList();
        _manualItems = _manualItems
            .OrderBy(m => !HasItemInInventory(m.ItemId, false))
            .ThenBy(m => m.Name)
            .ToList();
        _squadronManualItems = _squadronManualItems
            .OrderBy(m => !HasItemInInventory(m.ItemId, false))
            .ThenBy(m => m.Name)
            .ToList();
    }

    private static ItemFood? GetItemConsumableProperties(Item item, bool hq)
    {
        if (!item.ItemAction.IsValid)
            return null;
        var action = item.ItemAction.Value;
        var actionParams = hq ? action.DataHQ : action.Data;
        if (actionParams[0] is not 48 and not 49)
            return null;
        return Dalamud.GameData.GetExcelSheet<ItemFood>()?.GetRow(actionParams[1]);
    }

    private static bool IsCraftersFood(Item item)
    {
        if (item.ItemUICategory.RowId != 46)
            return false;
        var consumable = GetItemConsumableProperties(item, false);
        return consumable != null && consumable.Value.Params.Any(p => p.BaseParam.RowId is 11 or 70 or 71);
    }

    private static bool IsCraftersPot(Item item)
    {
        if (item.ItemUICategory.RowId != 44)
            return false;
        var consumable = GetItemConsumableProperties(item, false);
        return consumable != null && consumable.Value.Params.Any(p => p.BaseParam.RowId is 11 or 70 or 71 or 69 or 68);
    }

    private static bool IsCraftersManual(Item item)
    {
        if (item.ItemUICategory.RowId != 63)
            return false;
        if (!item.ItemAction.IsValid)
            return false;
        var action = item.ItemAction.Value;
        return action.Action.RowId == 816 && action.Data[0] is 300 or 301 or 1751 or 5329;
    }

    private static bool IsSquadronManual(Item item)
    {
        if (item.ItemUICategory.RowId != 63)
            return false;
        if (!item.ItemAction.IsValid)
            return false;
        var action = item.ItemAction.Value;
        return action.Action.RowId == 816 && action.Data[0] is 2291 or 2292 or 2293 or 2294;
    }

    private static string GetDisplayName(uint? itemId, bool hq)
        => itemId.HasValue ? GetItemName(itemId.Value) + (hq ? $" {(char)SeIconChar.HighQuality}" : "") : "无";

    private static string GetItemName(uint itemId)
    {
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
            return item.Name.ExtractText();
        return "未知";
    }

    private static unsafe bool HasItemInInventory(uint itemId, bool hq)
    {
        try
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return false;
            
            var inventories = new InventoryType[]
            {
                InventoryType.Inventory1, InventoryType.Inventory2,
                InventoryType.Inventory3, InventoryType.Inventory4
            };
            
            foreach (var invType in inventories)
            {
                var container = inventoryManager->GetInventoryContainer(invType);
                if (container == null) continue;
                
                for (int i = 0; i < container->Size; i++)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemId == 0) continue;
                    
                    if (item->ItemId == itemId)
                    {
                        bool itemIsHQ = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                        if (hq == itemIsHQ && item->Quantity > 0)
                            return true;
                    }
                }
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void Save()
    {
        if (_list == null)
            return;

        _list.Consumables = _workingDefaults;
        _list.DefaultPrecraftMacroId = _workingDefaultPrecraftMacroId;
        _list.DefaultFinalMacroId = _workingDefaultFinalMacroId;
        _list.DefaultPrecraftSolverOverride = _workingDefaultPrecraftSolverOverride;
        _list.DefaultFinalSolverOverride = _workingDefaultFinalSolverOverride;
        _list.UseAllHQ = _workingUseAllHQ;
        GatherBuddy.CraftingListManager.SaveList(_list);
        RaphaelAssessmentService.QueueWarmupForList(_list);
        MacroValidator.InvalidateAll();
        OnSaved?.Invoke();
    }
}
