using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using GatherBuddy.Automation;
using GatherBuddy.Plugin;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using DalamudObjectKind = global::Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace GatherBuddy.Vulcan.Vendors;

public static class VendorInteractionHelper
{
    private static readonly Type[] CustomTalkScriptTypes =
    [
        typeof(FateShop),
        typeof(GilShop),
        typeof(SpecialShop),
        typeof(GCShop),
        typeof(FccShop),
        typeof(InclusionShop),
        typeof(CollectablesShop),
        typeof(TopicSelect),
        typeof(PreHandler),
        typeof(CustomTalk),
    ];
    private readonly record struct ShopMenuSelectionKey(uint NpcId, VendorMenuShopType ShopType, uint ShopId);
    private sealed class ShopMenuSelectionState(IReadOnlyList<int> menuIndices)
    {
        public IReadOnlyList<int> MenuIndices { get; } = menuIndices;
        public int NextStepIndex { get; set; }
    }
    private static readonly Dictionary<ShopMenuSelectionKey, ShopMenuSelectionState> ShopMenuSelectionStates = new();

    public static bool IsReadyToLeaveVendor()
        => GetVendorExitBlocker() == null;

    public static unsafe string? GetVendorExitBlocker()
    {
        if (IsAddonVisible("Shop"))
            return "Shop 界面仍处于打开状态";
        if (IsAddonVisible("InclusionShop"))
            return "InclusionShop 界面仍处于打开状态";
        if (IsAddonVisible("GrandCompanyExchange"))
            return "GrandCompanyExchange 界面仍处于打开状态";

        if (IsAddonVisible("ShopExchangeItemDialog"))
            return "ShopExchangeItemDialog 仍处于打开状态";

        if (IsAddonVisible("ShopExchangeCurrencyDialog"))
            return "ShopExchangeCurrencyDialog 仍处于打开状态";

        if (IsAddonVisible("ShopExchangeCurrency"))
            return "ShopExchangeCurrency 仍处于打开状态";

        if (IsAddonVisible("ShopExchangeItem"))
            return "ShopExchangeItem 仍处于打开状态";

        if (IsAddonVisible("SelectString"))
            return "SelectString 仍处于打开状态";

        if (IsAddonVisible("SelectIconString"))
            return "SelectIconString 仍处于打开状态";

        if (IsAddonVisible("Talk"))
            return "Talk 对话框仍处于打开状态";

        if (IsAddonVisible("SelectYesno"))
            return "SelectYesno 仍处于打开状态";

        if (!GenericHelpers.IsScreenReady())
            return "屏幕未就绪";

        if (global::GatherBuddy.Helpers.Player.IsAnimationLocked)
            return "玩家动画已锁定";

        if (Dalamud.Conditions[ConditionFlag.Occupied])
            return "Occupied";

        if (Dalamud.Conditions[ConditionFlag.Occupied39])
            return "Occupied39";

        if (Dalamud.Conditions[ConditionFlag.OccupiedInEvent])
            return "OccupiedInEvent";

        if (Dalamud.Conditions[ConditionFlag.OccupiedInQuestEvent])
            return "OccupiedInQuestEvent";

        if (Dalamud.Conditions[ConditionFlag.OccupiedSummoningBell])
            return "OccupiedSummoningBell";

        if (Dalamud.Conditions[ConditionFlag.BeingMoved])
            return "BeingMoved";

        if (Dalamud.Conditions[ConditionFlag.Casting])
            return "Casting";

        if (Dalamud.Conditions[ConditionFlag.Casting87])
            return "Casting87";

        if (Dalamud.Conditions[ConditionFlag.Jumping])
            return "Jumping";

        if (Dalamud.Conditions[ConditionFlag.Jumping61])
            return "Jumping61";

        if (Dalamud.Conditions[ConditionFlag.LoggingOut])
            return "LoggingOut";

        if (Dalamud.Conditions[ConditionFlag.Unconscious])
            return "Unconscious";

        if (Dalamud.Conditions[ConditionFlag.MountOrOrnamentTransition])
            return "MountOrOrnamentTransition";

        return null;
    }

    public static unsafe bool TryCloseGilShop()
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>("Shop", out var shop) || !shop->IsVisible)
            return false;
        shop->FireCallbackInt(-1);
        return true;
    }

    public static unsafe bool TryCloseInclusionShop()
    {
        if (!GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        addon->Close(true);
        return true;
    }

    public static unsafe bool TryCloseGrandCompanyExchange()
    {
        if (!GenericHelpers.TryGetAddonByName("GrandCompanyExchange", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        Callback.Fire(addon, true, -1);
        return true;
    }

    public static unsafe bool TryCloseShopExchangeItem()
    {
        if (!GenericHelpers.TryGetAddonByName("ShopExchangeItem", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        addon->Close(true);
        return true;
    }

    public static unsafe bool TryCloseShopExchangeCurrency()
    {
        if (!GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        addon->Close(true);
        return true;
    }

    public static unsafe bool TryCloseShopExchangeItemDialog()
    {
        if (!GenericHelpers.TryGetAddonByName("ShopExchangeItemDialog", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        addon->Close(true);
        return true;
    }

    public static unsafe bool TryCloseShopExchangeCurrencyDialog()
    {
        if (!GenericHelpers.TryGetAddonByName("ShopExchangeCurrencyDialog", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        addon->Close(true);
        return true;
    }
    public static unsafe bool TryInteractWithTarget(VendorNpcLocation target)
    {
        var npc = FindLiveNpcObject(target);
        if (npc == null)
            return false;

        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
        {
            GatherBuddy.Log.Warning("[VendorInteractionHelper] TargetSystem 不可用");
            return false;
        }

        targetSystem->Target = (GameObject*)npc.Address;
        targetSystem->OpenObjectInteraction((GameObject*)npc.Address);
        return true;
    }

    public static unsafe bool TryClickTalk()
    {
        if (!GenericHelpers.TryGetAddonByName<AddonTalk>("Talk", out var talk) || !talk->AtkUnitBase.IsVisible)
            return false;

        new AddonMaster.Talk((nint)talk).Click();
        return true;
    }


    public static unsafe bool TrySelectShopOption(VendorNpc vendor, out string? error)
        => TrySelectShopOption(vendor.NpcId, vendor.MenuShopType, vendor.ShopId, out error);

    public static unsafe bool TrySelectShopOption(uint npcId, VendorMenuShopType shopType, uint shopId, out string? error)
    {
        error = null;
        var iconMenuVisible = GenericHelpers.TryGetAddonByName<AddonSelectIconString>("SelectIconString", out var iconMenu) && iconMenu->AtkUnitBase.IsVisible;
        var stringMenuVisible = GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectString", out var stringMenu) && stringMenu->AtkUnitBase.IsVisible;
        if (!iconMenuVisible && !stringMenuVisible)
            return false;
        if (!TryGetOrCreateShopMenuSelectionState(npcId, shopType, shopId, out var selectionState, out error))
            return false;

        if (selectionState.NextStepIndex >= selectionState.MenuIndices.Count)
        {
            ResetShopSelectionState(npcId, shopType, shopId);
            error = $"{DescribeMenuShopType(shopType)} 菜单路径(商人 {npcId}, 商店 {shopId})在商店打开前已耗尽";
            return false;
        }

        var menuIndex = selectionState.MenuIndices[selectionState.NextStepIndex];

        if (iconMenuVisible)
        {
            if (menuIndex >= iconMenu->PopupMenu.PopupMenu.EntryCount)
            {
                ResetShopSelectionState(npcId, shopType, shopId);
                error = $"{DescribeMenuShopType(shopType)} 菜单选项 {menuIndex} 目前在 SelectIconString 中不可用(商人 {npcId}, 商店 {shopId})";
                return false;
            }

            Callback.Fire(&iconMenu->AtkUnitBase, true, menuIndex);
            AdvanceShopMenuSelectionState(npcId, shopType, shopId, selectionState);
            return true;
        }

        if (stringMenuVisible)
        {
            var master = new AddonMaster.SelectString((nint)stringMenu);
            if (menuIndex >= master.EntryCount)
            {
                ResetShopSelectionState(npcId, shopType, shopId);
                error = $"{DescribeMenuShopType(shopType)} 选项 {menuIndex} 目前在 SelectString 中不可用(商人 {npcId}, 商店 {shopId})";
                return false;
            }

            master.Entries[menuIndex].Select();
            AdvanceShopMenuSelectionState(npcId, shopType, shopId, selectionState);
            return true;
        }

        return false;
    }

    private static unsafe bool TryConfirmShopExchangeDialog(string addonName)
    {
        if (!GenericHelpers.TryGetAddonByName(addonName, out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        if (addonName == "ShopExchangeCurrencyDialog")
        {
            var exchangeButton = addon->GetComponentButtonById(17);
            if (exchangeButton != null && TryClickComponentButton(addon, exchangeButton))
                return true;
        }

        Callback.Fire(addon, true, 0);
        addon->Close(true);
        return true;
    }


    public static void ResetShopSelectionState(VendorNpc vendor)
        => ResetShopSelectionState(vendor.NpcId, vendor.MenuShopType, vendor.ShopId);

    public static void ResetShopSelectionState(uint npcId, VendorMenuShopType shopType, uint shopId)
        => ShopMenuSelectionStates.Remove(new ShopMenuSelectionKey(npcId, shopType, shopId));

    public static unsafe bool TrySelectInclusionPage(int pageIndex, out string? error)
    {
        error = null;
        if (pageIndex < 0)
        {
            error = $"无效的 InclusionShop 页面索引 {pageIndex}";
            return false;
        }

        if (!GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        var selectPage = stackalloc AtkValue[]
        {
            new() { Type = ValueType.Int, Int = 12 },
            new() { Type = ValueType.UInt, UInt = (uint)pageIndex },
        };

        for (var index = 0; index < addon->UldManager.NodeListCount; index++)
        {
            var node = addon->UldManager.NodeList[index];
            if (node == null || node->Type != (NodeType)1015 || node->NodeId != 7)
                continue;

            var componentNode = node->GetAsAtkComponentNode();
            if (componentNode == null || componentNode->Component == null)
                continue;

            var dropdown = componentNode->GetAsAtkComponentDropdownList();
            dropdown->SelectItem(pageIndex);
            break;
        }

        addon->FireCallback(2, selectPage);
        return true;
    }

    public static unsafe bool TrySelectInclusionSubPage(int subPageIndex, out string? error)
    {
        error = null;
        if (subPageIndex <= 0)
        {
            error = $"无效的 InclusionShop 子页面索引 {subPageIndex}";
            return false;
        }

        if (!GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        var selectSubPage = stackalloc AtkValue[]
        {
            new() { Type = ValueType.Int, Int = 13 },
            new() { Type = ValueType.UInt, UInt = (uint)subPageIndex },
        };
        addon->FireCallback(2, selectSubPage);
        return true;
    }

    public static unsafe bool TrySelectInclusionShopItem(int itemIndex, uint requestedItemId, uint quantity, out string? error)
    {
        error = null;
        if (itemIndex < 0)
        {
            error = $"无效的 InclusionShop 物品索引 {itemIndex}";
            return false;
        }

        if (!GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        var selectItem = stackalloc AtkValue[]
        {
            new() { Type = ValueType.Int, Int = 14 },
            new() { Type = ValueType.UInt, UInt = (uint)itemIndex },
            new() { Type = ValueType.UInt, UInt = Math.Max(1u, quantity) },
        };
        var liveItemIndex = FindInclusionShopItemIndex(addon, requestedItemId);
        if (liveItemIndex < 0)
        {
            error = $"在实时 InclusionShop 行中找不到请求的物品 {requestedItemId}";
            return false;
        }

        if (liveItemIndex != itemIndex)
            GatherBuddy.Log.Debug($"[VendorInteractionHelper] 将 InclusionShop 物品索引从存储索引 {itemIndex} 映射到实时索引 {liveItemIndex}(请求物品 {requestedItemId})");

        selectItem[1].UInt = (uint)liveItemIndex;
        addon->FireCallback(3, selectItem);
        return true;
    }

    public static unsafe bool TrySelectSpecialShopItem(int itemIndex, uint requestedItemId, uint quantity, out string? error)
    {
        error = null;
        if (itemIndex < 0)
        {
            error = $"无效的 SpecialShop 物品索引 {itemIndex}";
            return false;
        }

        var batchQuantity = (int)Math.Max(1u, quantity);
        if (GenericHelpers.TryGetAddonByName("ShopExchangeItem", out AtkUnitBase* itemShop) && itemShop->IsVisible)
        {
            Callback.Fire(itemShop, true, 0, itemIndex, batchQuantity, 0);
            return true;
        }

        if (GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* currencyShop) && currencyShop->IsVisible)
        {
            var liveItemIndex = FindShopExchangeCurrencyItemIndex(currencyShop, requestedItemId);
            if (liveItemIndex < 0)
            {
                error = $"在实时 ShopExchangeCurrency 行中找不到请求的物品 {requestedItemId}";
                return false;
            }

            if (liveItemIndex != itemIndex)
                GatherBuddy.Log.Debug($"[VendorInteractionHelper] 将 ShopExchangeCurrency 行从存储索引 {itemIndex} 映射到实时索引 {liveItemIndex}(请求物品 {requestedItemId})");

            Callback.Fire(currencyShop, true, 0, liveItemIndex, batchQuantity, 0);
            return true;
        }

        return false;
    }

    public static unsafe bool IsGrandCompanyRankTabSelected(int rankIndex, out string? error)
        => IsGrandCompanyTabSelected(37u, rankIndex, "rank", out error);

    public static unsafe bool IsGrandCompanyCategoryTabSelected(int categoryIndex, out string? error)
        => IsGrandCompanyTabSelected(44u, categoryIndex, "category", out error);

    public static unsafe bool TrySelectGrandCompanyRankTab(int rankIndex, out string? error)
        => TrySelectGrandCompanyTab(37u, rankIndex, "rank", out error);

    public static unsafe bool TrySelectGrandCompanyCategoryTab(int categoryIndex, out string? error)
        => TrySelectGrandCompanyTab(44u, categoryIndex, "category", out error);

    public static unsafe bool TrySelectGrandCompanyItem(uint requestedItemId, uint quantity, uint currentGrandCompanyRank,
        out uint selectedQuantity, out bool opensCurrencyExchange, out string? error)
    {
        selectedQuantity = 0;
        opensCurrencyExchange = false;
        error = null;
        if (!GenericHelpers.TryGetAddonByName("GrandCompanyExchange", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        var reader = new VendorGrandCompanyExchangeReader(addon);
        var targetItem = reader.Items.FirstOrDefault(item => item.ItemId == requestedItemId);
        if (targetItem == null)
        {
            error = $"在实时 GrandCompanyExchange 行中找不到请求的物品 {requestedItemId}";
            return false;
        }

        if (currentGrandCompanyRank < targetItem.RequiredRank)
        {
            error = $"当前国防联军军衔太低，无法购买 {targetItem.Name}";
            return false;
        }

        opensCurrencyExchange = targetItem.OpensCurrencyExchange;
        selectedQuantity = opensCurrencyExchange
            ? Math.Max(1u, quantity)
            : targetItem.Stackable
                ? Math.Max(1u, quantity)
                : 1u;

        if (opensCurrencyExchange)
        {
            Callback.Fire(addon, true, 0, targetItem.Index, 1, 0, true, true, targetItem.ItemId, targetItem.IconId, targetItem.SealCost);
        }
        else
        {
            Callback.Fire(addon, true, 0, targetItem.Index, (int)selectedQuantity, 0, true, false, 0, 0, 0);
        }
        return true;
    }

    public static unsafe bool TrySetGrandCompanyExchangeQuantity(uint quantity, out string? error)
    {
        error = null;
        if (quantity == 0)
        {
            error = "国防联军兑换数量必须大于零";
            return false;
        }

        if (!GenericHelpers.TryGetAddonByName("ShopExchangeCurrencyDialog", out AtkUnitBase* addon) || !addon->IsVisible)
            return false;

        if (addon->UldManager.NodeListCount <= 8 || addon->UldManager.NodeList[8] == null)
        {
            error = "无法找到 ShopExchangeCurrencyDialog 中的数量输入框";
            return false;
        }

        var numericInput = addon->UldManager.NodeList[8]->GetAsAtkComponentNumericInput();
        if (numericInput == null)
        {
            error = "ShopExchangeCurrencyDialog 数量输入框不可用";
            return false;
        }

        numericInput->SetValue((int)quantity);
        return true;
    }

    public static unsafe bool TryConfirmPurchase()
    {
        if (TryConfirmShopExchangeDialog("ShopExchangeItemDialog"))
            return true;

        if (TryConfirmShopExchangeDialog("ShopExchangeCurrencyDialog"))
            return true;

        return TryConfirmYesNo();
    }

    public static unsafe bool TryConfirmYesNo()
    {
        if (!GenericHelpers.TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var addon)
         || !addon->AtkUnitBase.IsVisible
         || addon->YesButton == null)
            return false;

        if (TryCheckSelectYesnoConfirmationBox(addon))
            return true;

        new AddonMaster.SelectYesno((nint)addon).Yes();
        return true;
    }

    public static unsafe bool TryExitVendorInteraction()
    {
        if (TryCloseShopExchangeItemDialog())
            return true;
        if (TryCloseShopExchangeCurrencyDialog())
            return true;

        if (TryCloseShopExchangeItem())
            return true;

        if (TryCloseShopExchangeCurrency())
            return true;

        if (TryCloseGrandCompanyExchange())
            return true;

        if (TryCloseInclusionShop())
            return true;

        if (TryCloseGilShop())
            return true;

        if (GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectString", out var stringMenu) && stringMenu->AtkUnitBase.IsVisible)
        {
            Callback.Fire(&stringMenu->AtkUnitBase, true, -1);
            return true;
        }

        if (GenericHelpers.TryGetAddonByName<AddonSelectIconString>("SelectIconString", out var iconMenu) && iconMenu->AtkUnitBase.IsVisible)
        {
            Callback.Fire(&iconMenu->AtkUnitBase, true, -1);
            return true;
        }

        if (GenericHelpers.TryGetAddonByName<AddonTalk>("Talk", out var talk) && talk->AtkUnitBase.IsVisible)
        {
            new AddonMaster.Talk((nint)talk).Click();
            return true;
        }

        return false;
    }


    private static bool TryGetOrCreateShopMenuSelectionState(uint npcId, VendorMenuShopType shopType, uint shopId, out ShopMenuSelectionState selectionState, out string? error)
    {
        var selectionKey = new ShopMenuSelectionKey(npcId, shopType, shopId);
        if (ShopMenuSelectionStates.TryGetValue(selectionKey, out selectionState!))
        {
            error = null;
            return true;
        }

        if (!TryBuildShopMenuSelectionPath(npcId, shopType, shopId, out var menuIndices))
        {
            error = $"无法确定 {DescribeMenuShopType(shopType)} 菜单路径(商人 {npcId}, 商店 {shopId})";
            selectionState = null!;
            return false;
        }

        selectionState = new ShopMenuSelectionState(menuIndices);
        ShopMenuSelectionStates[selectionKey] = selectionState;
        error = null;
        return true;
    }

    private static void AdvanceShopMenuSelectionState(uint npcId, VendorMenuShopType shopType, uint shopId, ShopMenuSelectionState selectionState)
    {
        selectionState.NextStepIndex++;
        if (selectionState.NextStepIndex >= selectionState.MenuIndices.Count)
            ShopMenuSelectionStates.Remove(new ShopMenuSelectionKey(npcId, shopType, shopId));
    }

    private static bool TryBuildShopMenuSelectionPath(uint npcId, VendorMenuShopType shopType, uint shopId, out List<int> menuIndices)
    {
        menuIndices = [];
        var npcSheet = Dalamud.GameData.GetExcelSheet<ENpcBase>();
        if (npcSheet == null || !npcSheet.TryGetRow(npcId, out var npc))
            return false;

        return TryBuildShopMenuSelectionPath(npcId, npc.ENpcData.ToList(), shopType, shopId, out menuIndices);
    }

    private static bool TryBuildShopMenuSelectionPath(uint npcId, IReadOnlyList<RowRef> menuEntries, VendorMenuShopType shopType, uint shopId, out List<int> menuIndices)
    {
        TryGetVisibleMenuRowIds(npcId, menuEntries, out var visibleMenuRowIds);
        for (var index = 0; index < menuEntries.Count; index++)
        {
            if (!TryBuildShopMenuSelectionPath(npcId, menuEntries[index], shopType, shopId, out var childMenuIndices))
                continue;

            var visibleIndex = GetVisibleMenuSelectionIndex(npcId, menuEntries, index, visibleMenuRowIds);
            menuIndices = new List<int>(childMenuIndices.Count + 1) { visibleIndex };
            menuIndices.AddRange(childMenuIndices);
            return true;
        }

        menuIndices = [];
        return false;
    }

    private static bool TryBuildShopMenuSelectionPath(uint npcId, RowRef menuEntry, VendorMenuShopType shopType, uint shopId, out List<int> menuIndices)
    {
        menuIndices = [];
        if (menuEntry.RowId == 0)
            return false;

        if (MatchesMenuTarget(npcId, menuEntry, shopType, shopId))
            return true;

        var preHandlerSheet = Dalamud.GameData.GetExcelSheet<PreHandler>();
        if (menuEntry.Is<PreHandler>() && preHandlerSheet != null && preHandlerSheet.TryGetRow(menuEntry.RowId, out var preHandler))
            return TryBuildShopMenuSelectionPath(npcId, preHandler.Target, shopType, shopId, out menuIndices);

        var topicSelectSheet = Dalamud.GameData.GetExcelSheet<TopicSelect>();
        if (menuEntry.Is<TopicSelect>() && topicSelectSheet != null && topicSelectSheet.TryGetRow(menuEntry.RowId, out var topicSelect))
            return TryBuildShopMenuSelectionPath(npcId, topicSelect.Shop.ToList(), shopType, shopId, out menuIndices);

        var customTalkSheet = Dalamud.GameData.GetExcelSheet<CustomTalk>();
        if (menuEntry.Is<CustomTalk>() && customTalkSheet != null && customTalkSheet.TryGetRow(menuEntry.RowId, out var customTalk))
        {
            if (TryBuildShopMenuSelectionPath(npcId, customTalk.SpecialLinks, shopType, shopId, out menuIndices))
                return true;

            foreach (var scriptEntry in customTalk.Script)
            {
                if (!TryResolveCustomTalkScriptArg(scriptEntry.ScriptArg, out var scriptTarget))
                    continue;

                if (!TryBuildShopMenuSelectionPath(npcId, scriptTarget, shopType, shopId, out menuIndices))
                    continue;
                return true;
            }
        }

        return false;
    }

    private static bool MatchesMenuTarget(uint npcId, RowRef menuEntry, VendorMenuShopType shopType, uint shopId)
        => shopType switch
        {
            VendorMenuShopType.GilShop               => menuEntry.RowId == shopId && menuEntry.Is<GilShop>(),
            VendorMenuShopType.SpecialShop           => MatchesSpecialShopMenuTarget(npcId, menuEntry, shopId),
            VendorMenuShopType.InclusionShop         => menuEntry.RowId == shopId && menuEntry.Is<InclusionShop>(),
            VendorMenuShopType.CollectablesShop      => menuEntry.RowId == shopId && menuEntry.Is<CollectablesShop>(),
            VendorMenuShopType.GrandCompanyShop      => menuEntry.RowId == shopId && menuEntry.Is<GCShop>(),
            VendorMenuShopType.FreeCompanyCreditShop => menuEntry.RowId == shopId && menuEntry.Is<FccShop>(),
            _                                        => false,
        };

    private static bool MatchesSpecialShopMenuTarget(uint npcId, RowRef menuEntry, uint shopId)
    {
        if (menuEntry.RowId == shopId && menuEntry.Is<SpecialShop>())
            return true;

        var fateShopSheet = Dalamud.GameData.GetExcelSheet<FateShop>();
        if (menuEntry.Is<FateShop>())
            return fateShopSheet != null
                && fateShopSheet.TryGetRow(menuEntry.RowId, out var fateShop)
                && fateShop.SpecialShop.Any(specialShop => specialShop.RowId == shopId);
        if (menuEntry.RowId != npcId || fateShopSheet == null || !fateShopSheet.TryGetRow(menuEntry.RowId, out var npcFateShop))
            return false;

        var matched = npcFateShop.SpecialShop.Any(specialShop => specialShop.RowId == shopId);
        if (matched)
            GatherBuddy.Log.Debug($"[VendorInteractionHelper] 通过未类型化的 FateShop 行 {menuEntry.RowId} 为商人 {npcId} 匹配到特殊商店 {shopId}");
        return matched;
    }

    private static string DescribeMenuShopType(VendorMenuShopType shopType)
        => shopType switch
        {
            VendorMenuShopType.GilShop               => "金币商店",
            VendorMenuShopType.SpecialShop           => "特殊商店",
            VendorMenuShopType.InclusionShop         => "综合商店",
            VendorMenuShopType.CollectablesShop      => "收藏品商店",
            VendorMenuShopType.GrandCompanyShop      => "国防联军商店",
            VendorMenuShopType.FreeCompanyCreditShop => "部队商店",
            _                                        => "商人",
        };

    private static unsafe bool TryGetVisibleMenuRowIds(uint npcId, IReadOnlyList<RowRef> menuEntries, out List<uint> visibleMenuRowIds)
    {
        visibleMenuRowIds = [];
        var candidateRowIds = menuEntries
            .Where(entry => IsSelectableMenuEntry(npcId, entry))
            .Select(entry => entry.RowId)
            .ToHashSet();
        if (candidateRowIds.Count == 0)
            return false;

        var eventFramework = EventFramework.Instance();
        if (eventFramework == null)
            return false;

        var relatedRowIds = new HashSet<uint>();
        foreach (var eventHandler in eventFramework->EventHandlerModule.EventHandlerMap)
        {
            if (eventHandler.Item2.Value == null || !candidateRowIds.Contains(eventHandler.Item1))
                continue;

            if (!IsEventHandlerRelatedToNpc(eventHandler.Item2.Value, npcId))
                continue;

            relatedRowIds.Add(eventHandler.Item1);
        }

        if (relatedRowIds.Count == 0)
            return false;

        foreach (var menuEntry in menuEntries)
        {
            if (IsSelectableMenuEntry(npcId, menuEntry) && relatedRowIds.Contains(menuEntry.RowId))
                visibleMenuRowIds.Add(menuEntry.RowId);
        }

        return visibleMenuRowIds.Count > 0;
    }

    private static unsafe bool IsEventHandlerRelatedToNpc(FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler* eventHandler, uint npcId)
    {
        foreach (var eventObject in eventHandler->EventObjects)
        {
            if (eventObject.Value != null && eventObject.Value->BaseId == npcId)
                return true;
        }

        return false;
    }

    private static int GetVisibleMenuSelectionIndex(uint npcId, IReadOnlyList<RowRef> menuEntries, int rawIndex, IReadOnlyList<uint>? visibleMenuRowIds)
    {
        var visibleFallbackIndex = CountSelectableMenuEntriesBefore(npcId, menuEntries, rawIndex);
        if (visibleMenuRowIds == null || visibleMenuRowIds.Count == 0 || rawIndex < 0 || rawIndex >= menuEntries.Count)
            return visibleFallbackIndex;

        var rowId = menuEntries[rawIndex].RowId;
        for (var index = 0; index < visibleMenuRowIds.Count; index++)
        {
            if (visibleMenuRowIds[index] != rowId)
                continue;

            if (index != visibleFallbackIndex)
                GatherBuddy.Log.Debug($"[VendorInteractionHelper] 将菜单行 {rowId} 从槽位索引 {visibleFallbackIndex} 映射到可见索引 {index}");
            return index;
        }

        return visibleFallbackIndex;
    }

    private static int CountSelectableMenuEntriesBefore(uint npcId, IReadOnlyList<RowRef> menuEntries, int exclusiveUpperBound)
    {
        var count = 0;
        for (var index = 0; index < exclusiveUpperBound; index++)
        {
            if (IsSelectableMenuEntry(npcId, menuEntries[index]))
                count++;
        }

        return count;
    }

    private static bool IsSelectableMenuEntry(uint npcId, RowRef menuEntry)
        => menuEntry.RowId != 0
        && (IsNpcScopedFateShopMenuEntry(npcId, menuEntry)
         || menuEntry.Is<GilShop>()
         || menuEntry.Is<SpecialShop>()
         || menuEntry.Is<GCShop>()
         || menuEntry.Is<FccShop>()
         || menuEntry.Is<InclusionShop>()
         || menuEntry.Is<CollectablesShop>()
         || menuEntry.Is<CustomTalk>()
         || menuEntry.Is<PreHandler>()
         || menuEntry.Is<TopicSelect>()
         || menuEntry.Is<DisposalShop>()
         || menuEntry.Is<LotteryExchangeShop>());

    private static bool IsNpcScopedFateShopMenuEntry(uint npcId, RowRef menuEntry)
    {
        if (menuEntry.RowId == 0)
            return false;
        if (menuEntry.Is<FateShop>())
            return true;
        if (menuEntry.RowId != npcId)
            return false;

        var fateShopSheet = Dalamud.GameData.GetExcelSheet<FateShop>();
        return fateShopSheet != null && fateShopSheet.TryGetRow(menuEntry.RowId, out _);
    }

    private static bool TryResolveCustomTalkScriptArg(uint scriptArg, out RowRef scriptTarget)
    {
        scriptTarget = default;
        if (scriptArg == 0)
            return false;

        var typeHash = RowRef.CreateTypeHash(CustomTalkScriptTypes);
        scriptTarget = RowRef.GetFirstValidRowOrUntyped(
            Dalamud.GameData.Excel,
            scriptArg,
            CustomTalkScriptTypes,
            typeHash,
            Dalamud.GameData.GameData.Options.DefaultExcelLanguage);
        return scriptTarget.RowId != 0;
    }

    private static IGameObject? FindLiveNpcObject(VendorNpcLocation target)
    {
        IGameObject? bestObject = null;
        var bestDistance = float.MaxValue;
        foreach (var obj in Dalamud.Objects)
        {
            if (obj.ObjectKind != DalamudObjectKind.EventNpc || obj.BaseId != target.NpcId || !obj.IsTargetable)
                continue;

            var distance = Vector3.DistanceSquared(obj.Position, target.Position);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestObject = obj;
        }

        return bestObject;
    }

    private static unsafe bool IsAddonVisible(string addonName)
    {
        var addon = (AtkUnitBase*)(nint)Dalamud.GameGui.GetAddonByName(addonName);
        return addon != null && addon->IsVisible;
    }


    private static unsafe uint ReadAtkUInt(AtkUnitBase* addon, int index)
    {
        if (addon == null || index < 0 || index >= addon->AtkValuesCount)
            return 0;

        var value = addon->AtkValues[index];
        return value.Type switch
        {
            ValueType.UInt => value.UInt,
            ValueType.Int  => (uint)Math.Max(0, value.Int),
            _              => 0,
        };
    }

    private static unsafe bool TryCheckSelectYesnoConfirmationBox(AddonSelectYesno* addon)
    {
        var checkbox = addon->ConfirmCheckBox;
        if (checkbox == null || checkbox->AtkResNode == null || !checkbox->AtkResNode->IsVisible() || checkbox->IsChecked)
            return false;
        if (!checkbox->IsEnabled)
            return false;

        var checkboxNode = checkbox->OwnerNode;
        if (checkboxNode == null)
            return false;

        var eventPointer = checkboxNode->AtkResNode.AtkEventManager.Event;
        if (eventPointer == null)
            return false;

        var atkEvent = (AtkEvent*)eventPointer;
        var data = stackalloc AtkEventData[1];
        data[0] = default;
        addon->AtkUnitBase.ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, atkEvent, data);
        checkbox->SetChecked(true);
        return true;
    }

    private static unsafe bool IsGrandCompanyTabSelected(uint baseNodeId, int tabIndex, string tabType, out string? error)
    {
        error = null;
        if (!TryGetGrandCompanyTabButton(baseNodeId, tabIndex, tabType, out _, out var button, out error))
            return false;

        return button->IsSelected;
    }

    private static unsafe bool TrySelectGrandCompanyTab(uint baseNodeId, int tabIndex, string tabType, out string? error)
    {
        error = null;
        if (!TryGetGrandCompanyTabButton(baseNodeId, tabIndex, tabType, out var addon, out var button, out error))
            return false;

        if (button->IsSelected)
            return true;

        if (!TryClickGrandCompanyTab(addon, button))
        {
            error = $"GrandCompanyExchange {tabType} 选项卡 {tabIndex + 1} 当前不可点击";
            return false;
        }
        return true;
    }

    private static unsafe bool TryGetGrandCompanyTabButton(uint baseNodeId, int tabIndex, string tabType,
        out AtkUnitBase* addon, out AtkComponentRadioButton* button, out string? error)
    {
        addon = null;
        button = null;
        error = null;
        if (tabIndex < 0)
        {
            error = $"无效的 GrandCompanyExchange {tabType} 选项卡索引 {tabIndex}";
            return false;
        }

        if (!GenericHelpers.TryGetAddonByName("GrandCompanyExchange", out addon) || !addon->IsVisible)
            return false;

        var tabNode = addon->GetNodeById(baseNodeId + (uint)tabIndex);
        if (tabNode == null || !tabNode->IsVisible())
        {
            error = $"找不到 GrandCompanyExchange {tabType} 选项卡 {tabIndex + 1}";
            return false;
        }

        button = tabNode->GetAsAtkComponentRadioButton();
        if (button != null)
            return true;

        error = $"找不到 GrandCompanyExchange {tabType} 选项卡 {tabIndex + 1}";
        return false;
    }

    private static unsafe bool TryClickGrandCompanyTab(AtkUnitBase* addon, AtkComponentRadioButton* button)
    {
        if (addon == null || button == null || !button->AtkComponentButton.IsEnabled)
            return false;

        var ownerNode = button->AtkComponentButton.AtkComponentBase.OwnerNode;
        if (ownerNode == null || !ownerNode->AtkResNode.IsVisible())
            return false;

        var eventPointer = ownerNode->AtkResNode.AtkEventManager.Event;
        if (eventPointer == null)
            return false;

        var atkEvent = (AtkEvent*)eventPointer;
        addon->ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, eventPointer);
        return true;
    }

    private static unsafe bool TryClickComponentButton(AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (addon == null || button == null || !button->IsEnabled)
            return false;

        var ownerNode = button->AtkComponentBase.OwnerNode;
        if (ownerNode == null || !ownerNode->AtkResNode.IsVisible())
            return false;

        var eventPointer = ownerNode->AtkResNode.AtkEventManager.Event;
        if (eventPointer == null)
            return false;

        var atkEvent = (AtkEvent*)eventPointer;
        addon->ReceiveEvent(atkEvent->State.EventType, (int)atkEvent->Param, eventPointer);
        return true;
    }

    private static unsafe int FindShopExchangeCurrencyItemIndex(AtkUnitBase* addon, uint requestedItemId)
    {
        var agentShopItemIndex = FindAgentShopItemIndex(requestedItemId);
        if (agentShopItemIndex >= 0)
        {
            var atkValueItemIndex = FindShopExchangeCurrencyItemIndexFromAtkValues(addon, requestedItemId);
            if (atkValueItemIndex >= 0 && atkValueItemIndex != agentShopItemIndex)
                GatherBuddy.Log.Debug($@"[VendorInteractionHelper] AgentShop 将 ShopExchangeCurrency 行从 AtkValues 索引 {atkValueItemIndex} 映射到实时 agent 索引 {agentShopItemIndex}(请求物品 {requestedItemId})");
            return agentShopItemIndex;
        }

        var fallbackItemIndex = FindShopExchangeCurrencyItemIndexFromAtkValues(addon, requestedItemId);
        if (fallbackItemIndex >= 0)
            return fallbackItemIndex;

        GatherBuddy.Log.Debug($@"[VendorInteractionHelper] 在 ShopExchangeCurrency 中找不到请求的物品 {requestedItemId}, AgentShop 行: {DescribeAgentShopReceiveItems()}");
        return -1;
    }

    private static unsafe int FindShopExchangeCurrencyItemIndexFromAtkValues(AtkUnitBase* addon, uint requestedItemId)
    {
        var numEntries = (int)ReadAtkUInt(addon, 4);
        for (var index = 0; index < numEntries; index++)
        {
            if (ReadAtkUInt(addon, 1064 + index) == requestedItemId)
                return (int)ReadAtkUInt(addon, 1308 + index);
        }

        return -1;
    }

    private static unsafe int FindAgentShopItemIndex(uint requestedItemId)
    {
        if (!TryGetAgentShop(out var agentShop))
            return -1;

        var receiveItems = agentShop->ItemReceiveSpan;
        for (var index = 0; index < receiveItems.Length; index++)
        {
            if (receiveItems[index].ItemId == requestedItemId)
                return index;
        }

        return -1;
    }

    private static unsafe bool TryGetAgentShop(out AgentShop* agentShop)
    {
        agentShop = null;
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
            return false;

        agentShop = (AgentShop*)agentModule->GetAgentByInternalId(AgentId.Shop);
        return agentShop != null && agentShop->ItemReceive != null && agentShop->ItemReceiveCount > 0;
    }

    private static unsafe string DescribeAgentShopReceiveItems(int maxItems = 12)
    {
        if (!TryGetAgentShop(out var agentShop))
            return "不可用";

        var receiveItems = agentShop->ItemReceiveSpan;
        if (receiveItems.Length == 0)
            return "空";

        var count = Math.Min(maxItems, receiveItems.Length);
        var descriptions = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var item = receiveItems[index];
            descriptions.Add($@"{index}:{item.ItemId}:{item.ItemName}");
        }

        if (receiveItems.Length > count)
            descriptions.Add($@"+{receiveItems.Length - count} 更多");

        return string.Join(", ", descriptions);
    }

    private static unsafe int FindInclusionShopItemIndex(AtkUnitBase* addon, uint requestedItemId)
    {
        var numEntries = (int)ReadAtkUInt(addon, 298);
        for (var index = 0; index < numEntries; index++)
        {
            if (ReadAtkUInt(addon, 300 + (index * 18)) == requestedItemId)
                return index;
        }

        return -1;
    }

}
