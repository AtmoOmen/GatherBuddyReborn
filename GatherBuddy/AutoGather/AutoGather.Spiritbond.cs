using System;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using GatherBuddy.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Plugin;
using static GatherBuddy.Automation.AddonMaster;

namespace GatherBuddy.AutoGather;

public partial class AutoGather
{

    unsafe int SpiritbondMax
    {
        get
        {
            if (!GatherBuddy.Config.AutoGatherConfig.DoMaterialize) return 0;

            var inventory = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
            var result    = 0;
            for (var slot = 0; slot < inventory->Size; slot++)
            {
                var inventoryItem = inventory->GetInventorySlot(slot);
                if (inventoryItem == null || inventoryItem->ItemId <= 0)
                    continue;

                //GatherBuddy.Log.Debug("Slot " + slot + " has " + inventoryItem->Spiritbond + " Spiritbond");
                if (inventoryItem->SpiritbondOrCollectability == 10000)
                {
                    result++;
                }
            }

            return result;
        }
    }

    unsafe void DoMateriaExtraction()
    {
        if (!QuestManager.IsQuestComplete(66174))
        {
            GatherBuddy.Config.AutoGatherConfig.DoMaterialize = false;
            Communicator.PrintError("[GatherBuddy Reborn] 无法自动精制魔晶石, 精制任务尚未完成。此功能已停用。");
            return;
        }
        if (MaterializeAddon == null)
        {
            StopNavigation();
            EnqueueActionWithDelay(() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 14));
            TaskManager.Enqueue(() => MaterializeAddon != null, "精魔晶石界面已打开");
            return;
        }

        EnqueueActionWithDelay(() => { if (MaterializeAddon is var addon and not null) Callback.Fire(&addon->AtkUnitBase, true, 2, 0); });
        TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.Occupied39], "等待占用状态解除");
        EnqueueActionWithDelay(() => { });

        if (SpiritbondMax == 1) 
        {
            EnqueueActionWithDelay(() => { if (MaterializeAddon is var addon and not null) Callback.Fire(&addon->AtkUnitBase, true, -1); });
        }
    }
}
