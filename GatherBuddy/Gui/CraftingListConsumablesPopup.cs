using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public class CraftingListConsumablesPopup
{
    private CraftingListDefinition? _list;
    private CraftingListConsumableSettings _workingDefaults = new();
    private string? _workingDefaultPrecraftMacroId;
    private string? _workingDefaultFinalMacroId;
    private SolverOverrideMode _workingDefaultPrecraftSolverOverride = SolverOverrideMode.Default;
    private SolverOverrideMode _workingDefaultFinalSolverOverride = SolverOverrideMode.Default;
    private bool _workingUseAllHQ;
    private List<(uint ItemId, string Name, bool IsHQ)> _foodItems = [];
    private List<(uint ItemId, string Name, bool IsHQ)> _medicineItems = [];
    private List<(uint ItemId, string Name)> _manualItems = [];
    private List<(uint ItemId, string Name)> _squadronManualItems = [];
    public System.Action? OnSaved { get; set; }
    private static float SelectorLabelWidth => VulcanUiScaling.Scaled(150f);
    private static float SelectorComboWidth => VulcanUiScaling.Scaled(260f);

    private static readonly (string Label, SolverOverrideMode Mode, string? Tooltip)[] SolverChoices =
    [
        ("默认求解器", SolverOverrideMode.Default, null),
        ("标准求解器", SolverOverrideMode.StandardSolver, null),
        ("Raphael 求解器", SolverOverrideMode.RaphaelSolver, null),
        ("仅完成制作", SolverOverrideMode.ProgressOnlySolver, "跳过提升品质的技能，尽快完成制作。"),
    ];

    internal static RebornUI.HostedPanelWindow CreateWindow()
        => new("GatherBuddyCraftingListDefaults", "清单默认设置", new Vector2(500f, 540f), new Vector2(460f, 300f), bodyScrolls: false);

    public void OpenListDefaults(CraftingListDefinition list)
    {
        _list = list;
        _workingDefaults = list.Consumables.Clone();
        _workingDefaultPrecraftMacroId = list.DefaultPrecraftMacroId;
        _workingDefaultFinalMacroId = list.DefaultFinalMacroId;
        _workingDefaultPrecraftSolverOverride = list.DefaultPrecraftSolverOverride;
        _workingDefaultFinalSolverOverride = list.DefaultFinalSolverOverride;
        _workingUseAllHQ = list.UseAllHQ;
        EnsureConsumablesLoaded();
        GatherBuddy.CraftingListDefaultsWindow?.Show(this, DrawContents, () => list.Name);
    }

    public void Close()
    {
        GatherBuddy.CraftingListDefaultsWindow?.Hide(this);
    }

    private void DrawContents()
    {
        var footer = M3Widgets.ButtonHeight + M3.Space1 + ImGui.GetStyle().ItemSpacing.Y * 2f;
        using (var body = ImRaii.Child("##listDefaultsBody", new Vector2(-1, -footer), false))
        {
            if (body)
            {
                DrawListDefaults();
            }
        }

        ImGui.Dummy(new Vector2(0, M3.Space1));
        if (M3Widgets.Button("##saveDefaults", "保存默认设置", M3ButtonStyle.Filled, FontAwesomeIcon.Save))
        {
            Save();
            if (_list != null)
            {
                M3Snackbar.Show($"已保存清单「{_list.Name}」的默认设置。");
            }
        }

        ImGui.SameLine(0, M3.Space2);
        if (M3Widgets.Button("##cancelDefaults", "取消", M3ButtonStyle.Text))
        {
            Close();
        }
    }

    private void DrawListDefaults()
    {
        var allMacros = CraftingGameInterop.UserMacroLibrary.GetAllMacros();
        using (M3Card.Begin("##listMacros", "制作宏或求解器", FontAwesomeIcon.Scroll, M3.AccentFor(0), subtitle: "成品和半成品可以分别设置自己的制作方式。"))
        {
            DrawMacroSelector("半成品", "##PrecraftMacro", ref _workingDefaultPrecraftMacroId, ref _workingDefaultPrecraftSolverOverride, allMacros);
            DrawMacroSelector("成品", "##FinalMacro", ref _workingDefaultFinalMacroId, ref _workingDefaultFinalSolverOverride, allMacros);
        }

        using (M3Card.Begin("##listConsumables", "消耗品", FontAwesomeIcon.Utensils, M3.AccentFor(1)))
        {
            M3Widgets.LabeledCheckbox("默认使用 HQ 材料##ListUseAllHQ", ref _workingUseAllHQ,
                tooltip: "制定制作计划和提前进行 Raphael 求解时，默认将所有支持 HQ 的材料按 HQ 计算。\n成品和半成品的个别设置可覆盖此选项。");

            var foodItemId = _workingDefaults.FoodItemId;
            var foodHq = _workingDefaults.FoodHQ;
            var medicineItemId = _workingDefaults.MedicineItemId;
            var medicineHq = _workingDefaults.MedicineHQ;
            var manualItemId = _workingDefaults.ManualItemId;
            var squadronManualItemId = _workingDefaults.SquadronManualItemId;

            DrawConsumableSelector("食物", "##ListFood", _foodItems, ref foodItemId, ref foodHq);
            DrawConsumableSelector("药品", "##ListMedicine", _medicineItems, ref medicineItemId, ref medicineHq);
            DrawManualSelector("工程学指南", "##ListManual", _manualItems, ref manualItemId);
            DrawManualSelector("军用及民用指南", "##ListSquadron", _squadronManualItems, ref squadronManualItemId);

            _workingDefaults.FoodItemId = foodItemId;
            _workingDefaults.FoodHQ = foodHq;
            _workingDefaults.MedicineItemId = medicineItemId;
            _workingDefaults.MedicineHQ = medicineHq;
            _workingDefaults.ManualItemId = manualItemId;
            _workingDefaults.SquadronManualItemId = squadronManualItemId;
        }
    }

    private static void SelectorLabel(string label)
    {
        var start = ImGui.GetCursorPosY();
        ImGui.SetCursorPosY(start + (M3Widgets.ComboHeight - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.TextUnformatted(label);
        ImGui.SameLine(SelectorLabelWidth);
        ImGui.SetCursorPosY(start);
    }

    private static void DrawMacroSelector(string label, string comboId, ref string? selectedMacroId, ref SolverOverrideMode solverOverride, List<UserMacro> allMacros)
    {
        SelectorLabel(label);
        int index;
        var macroId = selectedMacroId;
        if (!string.IsNullOrEmpty(macroId) && solverOverride == SolverOverrideMode.Default)
        {
            var macroIndex = allMacros.FindIndex(m => m.Id == macroId);
            index = macroIndex < 0 ? -1 : SolverChoices.Length + macroIndex;
        }
        else
        {
            var mode = solverOverride;
            index = Array.FindIndex(SolverChoices, c => c.Mode == mode);
        }

        string Label(int i)
        {
            if (i < SolverChoices.Length)
            {
                return SolverChoices[i].Label;
            }

            var macro = allMacros[i - SolverChoices.Length];
            return macro.MinCraftsmanship > 0 || macro.MinControl > 0 || macro.MinCP > 0
                ? $"{macro.Name} ({macro.MinCraftsmanship}/{macro.MinControl}/{macro.MinCP})"
                : macro.Name;
        }

        if (!M3Widgets.Combo(comboId, ref index, SolverChoices.Length + allMacros.Count, Label, SelectorComboWidth,
                GetMacroSelectionName(selectedMacroId, solverOverride, allMacros), allMacros.Count > 6,
                itemTooltip: i => i < SolverChoices.Length ? SolverChoices[i].Tooltip : null))
        {
            return;
        }

        if (index < SolverChoices.Length)
        {
            selectedMacroId = null;
            solverOverride = SolverChoices[index].Mode;
        }
        else
        {
            selectedMacroId = allMacros[index - SolverChoices.Length].Id;
            solverOverride = SolverOverrideMode.Default;
        }
    }

    private static string GetMacroSelectionName(string? macroId, SolverOverrideMode solverOverride, List<UserMacro> allMacros)
        => solverOverride switch
        {
            SolverOverrideMode.StandardSolver => "标准求解器",
            SolverOverrideMode.RaphaelSolver => "Raphael 求解器",
            SolverOverrideMode.ProgressOnlySolver => "仅完成制作",
            _ when !string.IsNullOrEmpty(macroId) => allMacros.Find(m => m.Id == macroId)?.Name ?? "（找不到宏）",
            _ => "默认求解器",
        };

    private static string ItemLabel(string name, bool hq)
        => hq ? $"{name} {(char)SeIconChar.HighQuality}" : name;

    private static void DrawConsumableSelector(string label, string id, List<(uint ItemId, string Name, bool IsHQ)> items, ref uint? itemId, ref bool hq)
    {
        SelectorLabel(label);
        var current = itemId;
        var currentHq = hq;
        var index = current.HasValue ? items.FindIndex(i => i.ItemId == current.Value && i.IsHQ == currentHq) + 1 : 0;
        if (!M3Widgets.Combo(id, ref index, items.Count + 1, i => i == 0 ? "无" : ItemLabel(items[i - 1].Name, items[i - 1].IsHQ), SelectorComboWidth, "无",
                items.Count > 8))
        {
            return;
        }

        if (index <= 0)
        {
            itemId = null;
            hq = false;
        }
        else
        {
            itemId = items[index - 1].ItemId;
            hq = items[index - 1].IsHQ;
        }
    }

    private static void DrawManualSelector(string label, string id, List<(uint ItemId, string Name)> items, ref uint? itemId)
    {
        SelectorLabel(label);
        var current = itemId ?? 0;
        var index = current == 0 ? 0 : items.FindIndex(i => i.ItemId == current) + 1;
        if (M3Widgets.Combo(id, ref index, items.Count + 1, i => i == 0 ? "无" : items[i - 1].Name, SelectorComboWidth, "无"))
        {
            itemId = index <= 0 ? null : items[index - 1].ItemId;
        }
    }

    private void EnsureConsumablesLoaded()
    {
        _foodItems.Clear();
        _medicineItems.Clear();
        _manualItems.Clear();
        _squadronManualItems.Clear();
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        if (itemSheet == null)
        {
            return;
        }

        foreach (var item in itemSheet)
        {
            if (IsCraftersFood(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _foodItems.Add((item.RowId, name, false));
                    if (item.CanBeHq)
                    {
                        _foodItems.Add((item.RowId, name, true));
                    }
                }
            }
            else if (IsCraftersPot(item))
            {
                var name = item.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _medicineItems.Add((item.RowId, name, false));
                    if (item.CanBeHq)
                    {
                        _medicineItems.Add((item.RowId, name, true));
                    }
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

        _foodItems = SortConsumables(_foodItems);
        _medicineItems = SortConsumables(_medicineItems);
        _manualItems = SortConsumables(_manualItems);
        _squadronManualItems = SortConsumables(_squadronManualItems);
    }

    private static List<(uint ItemId, string Name, bool IsHQ)> SortConsumables(List<(uint ItemId, string Name, bool IsHQ)> items)
    {
        var keyedItems = new List<((uint ItemId, string Name, bool IsHQ) Item, bool Missing, int Index)>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            keyedItems.Add((items[i], !HasItemInInventory(items[i].ItemId, items[i].IsHQ), i));
        }

        keyedItems.Sort(static (lhs, rhs) =>
        {
            var order = lhs.Missing.CompareTo(rhs.Missing);
            if (order != 0)
            {
                return order;
            }

            order = string.Compare(lhs.Item.Name, rhs.Item.Name, StringComparison.CurrentCulture);
            if (order != 0)
            {
                return order;
            }

            order = (lhs.Item.IsHQ ? 0 : 1).CompareTo(rhs.Item.IsHQ ? 0 : 1);
            return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
        });
        return keyedItems.ConvertAll(static keyed => keyed.Item);
    }

    private static List<(uint ItemId, string Name)> SortConsumables(List<(uint ItemId, string Name)> items)
    {
        var keyedItems = new List<((uint ItemId, string Name) Item, bool Missing, int Index)>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            keyedItems.Add((items[i], !HasItemInInventory(items[i].ItemId, false), i));
        }

        keyedItems.Sort(static (lhs, rhs) =>
        {
            var order = lhs.Missing.CompareTo(rhs.Missing);
            if (order != 0)
            {
                return order;
            }

            order = string.Compare(lhs.Item.Name, rhs.Item.Name, StringComparison.CurrentCulture);
            return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
        });
        return keyedItems.ConvertAll(static keyed => keyed.Item);
    }

    private static bool HasParam(ItemFood? consumable, Predicate<uint> match)
    {
        if (consumable == null)
        {
            return false;
        }

        foreach (var param in consumable.Value.Params)
        {
            if (match(param.BaseParam.RowId))
            {
                return true;
            }
        }

        return false;
    }

    private static ItemFood? GetItemConsumableProperties(Item item, bool hq)
    {
        if (!item.ItemAction.IsValid)
        {
            return null;
        }

        var action = item.ItemAction.Value;
        var actionParams = hq ? action.DataHQ : action.Data;
        if (actionParams[0] is not 48 and not 49)
        {
            return null;
        }

        return Dalamud.GameData.GetExcelSheet<ItemFood>()?.GetRow(actionParams[1]);
    }

    private static bool IsCraftersFood(Item item)
    {
        if (item.ItemUICategory.RowId != 46)
        {
            return false;
        }

        return HasParam(GetItemConsumableProperties(item, false), static id => id is 11 or 70 or 71);
    }

    private static bool IsCraftersPot(Item item)
    {
        if (item.ItemUICategory.RowId != 44)
        {
            return false;
        }

        return HasParam(GetItemConsumableProperties(item, false), static id => id is 11 or 70 or 71 or 69 or 68);
    }

    private static bool IsCraftersManual(Item item)
    {
        if (item.ItemUICategory.RowId != 63)
        {
            return false;
        }

        if (!item.ItemAction.IsValid)
        {
            return false;
        }

        var action = item.ItemAction.Value;
        return action.Action.RowId == 816 && action.Data[0] is 300 or 301 or 1751 or 5329;
    }

    private static bool IsSquadronManual(Item item)
    {
        if (item.ItemUICategory.RowId != 63)
        {
            return false;
        }

        if (!item.ItemAction.IsValid)
        {
            return false;
        }

        var action = item.ItemAction.Value;
        return action.Action.RowId == 816 && action.Data[0] is 2291 or 2292 or 2293 or 2294;
    }

    private static string GetDisplayName(uint? itemId, bool hq)
        => itemId.HasValue ? GetItemName(itemId.Value) + (hq ? $" {(char)SeIconChar.HighQuality}" : "") : "无";

    private static string GetItemName(uint itemId)
    {
        var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
        if (itemSheet != null && itemSheet.TryGetRow(itemId, out var item))
        {
            return item.Name.ExtractText();
        }

        return "未知";
    }

    private static unsafe bool HasItemInInventory(uint itemId, bool hq)
    {
        try
        {
            var inventoryManager = InventoryManager.Instance();
            return inventoryManager != null
                && inventoryManager->GetInventoryItemCount(itemId, hq, false, false) > 0;
        }
        catch
        {
            return false;
        }
    }

    private void Save()
    {
        if (_list == null)
        {
            return;
        }

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
