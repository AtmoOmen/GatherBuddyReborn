using GatherBuddy.Plugin;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using Dalamud.Game.ClientState.Conditions;
using GatherBuddy.Automation;
using GatherBuddy.Helpers;

namespace GatherBuddy.AutoGather;

public unsafe partial class AutoGather
{
    private Item? EquipmentNeedingRepair()
    {
        const int defaultThreshold = 5;
        var threshold = GatherBuddy.Config.AutoGatherConfig.DoRepair ? GatherBuddy.Config.AutoGatherConfig.RepairThreshold : defaultThreshold;

        var equippedItems = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        for (var i = 0; i < equippedItems->Size; i++)
        {
            var equippedItem = equippedItems->GetInventorySlot(i);
            if (equippedItem != null && equippedItem->ItemId > 0)
            {
                if (equippedItem->Condition / 300 <= threshold)
                {
                    return Dalamud.GameData.Excel.GetSheet<Item>().GetRow(equippedItem->ItemId);
                }
            }
        }

        return null;
    }

    private bool HasRepairJob(Item itemToRepair)
    {
        if (itemToRepair.ClassJobRepair.RowId > 0)
        {
            var repairJobLevel =
                PlayerState.Instance()->ClassJobLevels[
                    Dalamud.GameData.GetExcelSheet<ClassJob>()?.GetRow(itemToRepair.ClassJobRepair.RowId).ExpArrayIndex ?? 0];
            if (Math.Max(1, itemToRepair.LevelEquip - 10) <= repairJobLevel)
                return true;
        }

        return false;
    }

    private bool HasDarkMatter(Item itemToRepair)
    {
        var darkMatters = Dalamud.GameData.Excel.GetSheet<ItemRepairResource>();
        foreach (var darkMatter in darkMatters)
        {
            if (darkMatter.Item.RowId < itemToRepair.ItemRepair.Value.Item.RowId)
                continue;

            if (GetInventoryItemCount(darkMatter.Item.RowId) > 0)
                return true;
        }

        return false;
    }

    private bool RepairIfNeeded()
    {
        if (Dalamud.Conditions[ConditionFlag.Mounted] || Player.Job is not 16 /* MIN */ and not 17 /* BTN */ and not 18 /* FSH */)
            return false;

        var itemToRepair = EquipmentNeedingRepair();

        if (itemToRepair == null)
            return false;

        if (!GatherBuddy.Config.AutoGatherConfig.DoRepair)
        {
            Communicator.PrintError("装备耐久度过低, 请先启用自动修理功能。");
            AbortAutoGather("需要修理装备。");
            return true;
        }

        if (!HasRepairJob((Item)itemToRepair))
        {
            AbortAutoGather("需要修理装备, 但未找到可修理的职业。");
            return true;
        }
        if (!HasDarkMatter((Item)itemToRepair))
        {
            AbortAutoGather("需要修理装备, 但未找到暗物质。");
            return true;
        }

        AutoStatus = "正在修理装备...";
        StopNavigation();

        var delay = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
        if (RepairAddon == null)
            ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6);

        TaskManager.Enqueue(() => RepairAddon != null, 1000, true, "等待修理界面就绪");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (RepairAddon is var addon && addon != null) { GatherBuddy.Log.Debug("[Repair] 点击全部修理按钮"); new AddonMaster.Repair(addon).RepairAll(); } }, 1000, "修理全部装备");
        TaskManager.Enqueue(() => SelectYesnoAddon != null, 1000, true, "等待确认对话框就绪");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (SelectYesnoAddon is var addon && addon != null) Callback.Fire(&addon->AtkUnitBase, true, 0); }, 1000, "确认修理");
        TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.Occupied39], 5000, "等待修理完成");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (RepairAddon is var addon and not null) Callback.Fire(&addon->AtkUnitBase, true, -1); }, 1000, true, "关闭修理界面");
        TaskManager.DelayNext(1000);
        TaskManager.Enqueue(() => {
            var repairAutoAddon = GetAddon<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RepairAuto");
            if (repairAutoAddon == null || !repairAutoAddon->IsVisible)
                return true;
            GatherBuddy.Log.Debug("[Repair] 修理界面仍可见，正在关闭");
            repairAutoAddon->Close(true);
            return true;
        }, 3000, "等待修理界面关闭或强制关闭");
        TaskManager.DelayNext(delay);

        return true;
    }

    private DateTime _lastRepairTime = DateTime.MinValue;
    
    private bool RepairIfNeededForFishing()
    {
        if (Dalamud.Conditions[ConditionFlag.Mounted] || Player.Job is not 18 /* FSH */)
            return false;

        var itemToRepair = EquipmentNeedingRepair();

        if (itemToRepair == null)
        {
            _lastRepairTime = DateTime.MinValue;
            return false;
        }
        
        if (GatherBuddy.Config.AutoGatherConfig.DeferRepairDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
            return false;
        
        if ((DateTime.Now - _lastRepairTime).TotalSeconds < 5)
            return false;

        if (!GatherBuddy.Config.AutoGatherConfig.DoRepair)
        {
            Communicator.PrintError("装备耐久度过低, 请先启用自动修理功能。");
            AbortAutoGather("需要修理装备。");
            return true;
        }

        if (!HasRepairJob((Item)itemToRepair))
        {
            AbortAutoGather("需要修理装备, 但未找到可修理的职业。");
            return true;
        }
        if (!HasDarkMatter((Item)itemToRepair))
        {
            AbortAutoGather("需要修理装备, 但未找到暗物质。");
            return true;
        }

        AutoStatus = "正在修理装备...";
        _lastRepairTime = DateTime.Now;
        var delay = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
        
        TaskManager.Enqueue(StopNavigation);
        
        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
        {
            TaskManager.Enqueue(() =>
            {
                AutoHook.SetPluginState?.Invoke(false);
                AutoHook.SetAutoStartFishing?.Invoke(false);
            });
        }
        
        if (IsGathering || IsFishing)
        {
            QueueQuitFishingTasks();
            TaskManager.Enqueue(() => !IsFishing, 5000, "等待钓鱼停止");
        }
        
        if (RepairAddon == null)
        {
            EnqueueActionWithDelay(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 6));
        }

        TaskManager.Enqueue(() => RepairAddon != null, 1000, true, "等待修理界面就绪");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (RepairAddon is var addon && addon != null) new AddonMaster.Repair(addon).RepairAll(); }, 1000, "修理全部装备");
        TaskManager.Enqueue(() => SelectYesnoAddon != null, 1000, true, "等待确认对话框就绪");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (SelectYesnoAddon is var addon && addon != null) new AddonMaster.SelectYesno(addon).Yes(); }, 1000, "确认修理");
        TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.Occupied39], 5000, "等待修理完成");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() => { if (RepairAddon is var addon and not null) Callback.Fire(&addon->AtkUnitBase, true, -1); }, 1000, true, "关闭修理界面");
        TaskManager.DelayNext(1000);
        TaskManager.Enqueue(() => {
            var repairAutoAddon = GetAddon<FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase>("RepairAuto");
            if (repairAutoAddon == null || !repairAutoAddon->IsVisible)
                return true;
            GatherBuddy.Log.Debug("[Repair] 修理界面仍可见，正在关闭");
            repairAutoAddon->Close(true);
            return true;
        }, 3000, "等待修理界面关闭或强制关闭");
        TaskManager.DelayNext(delay);
        TaskManager.Enqueue(() =>
        {
            if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
            {
                AutoHook.SetPluginState?.Invoke(true);
                AutoHook.SetAutoStartFishing?.Invoke(true);
            }
        });

        return true;
    }
}
