using GatherBuddy.Automation;
using Lumina.Excel.Sheets;
using GatherBuddy.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace GatherBuddy.AutoGather.Collectables;

public unsafe class CollectableWindowHandler
{
    public unsafe bool IsReady => Automation.GenericHelpers.TryGetAddonByName<AtkUnitBase>("CollectablesShop", out var addon) &&
                                  Automation.GenericHelpers.IsAddonReady(addon);

    public unsafe void SelectJob(uint id)
    {
        if (Automation.GenericHelpers.TryGetAddonByName<AtkUnitBase>("CollectablesShop", out var addon) &&
            Automation.GenericHelpers.IsAddonReady(addon))
        {
            var selectJob = stackalloc AtkValue[]
            {
                new() {Type = ValueType.Int, Int = 14},
                new(){Type = ValueType.UInt, UInt = id }
            };
            addon->FireCallback(2, selectJob); 
        }
    }

    public unsafe void SelectItem(string itemName)
    {
        if (Automation.GenericHelpers.TryGetAddonByName<AtkUnitBase>("CollectablesShop", out var addon) &&
            Automation.GenericHelpers.IsAddonReady(addon))
        {
            var turnIn = new TurninWindow(addon);
            var index = turnIn.GetItemIndexOf(itemName);
            if (index == -1)
            {
                GatherBuddy.Log.Error($"[收藏品窗口] 在当前收藏品标签页找不到物品 '{itemName}'");
                return;
            }
            var selectItem = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 12 },
                new(){Type = ValueType.UInt, UInt = (uint)index}
            };
            addon->FireCallback(2, selectItem);
        }
    }

    public unsafe void SelectItemById(uint itemId)
    {
        var item = Dalamud.GameData.GetExcelSheet<Item>().GetRow(itemId);
        var itemName = item.Name.ToString();
        SelectItem(itemName);
    }
    
    public unsafe void SubmitItem()
    {
        if (Automation.GenericHelpers.TryGetAddonByName<AtkUnitBase>("CollectablesShop", out var addon) &&
            Automation.GenericHelpers.IsAddonReady(addon))
        {
            var submitItem = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 15 },
                new(){Type = ValueType.UInt, UInt = 0}
            };
            addon->FireCallback(2, submitItem, true);
        }
    }
    
    public unsafe void CloseWindow()
    {
        if (Automation.GenericHelpers.TryGetAddonByName<AtkUnitBase>("CollectablesShop", out var addon) &&
            Automation.GenericHelpers.IsAddonReady(addon))
        {
            Callback.Fire(addon, true, -1);
            addon->Close(true);
        }
    }
}
