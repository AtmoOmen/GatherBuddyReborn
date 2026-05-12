using System;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GatherBuddy.AutoGather.Collectables;
using GatherBuddy.Automation;
using GatherBuddy.Plugin;

namespace GatherBuddy.Vulcan.Vendors;

public sealed class VendorPurchaseManager : IDisposable
{
    private enum State
    {
        Idle,
        WaitingForNavigation,
        OpeningShop,
        PurchasingItem,
        ConfirmingPurchase,
        WaitingForPurchaseComplete,
    }

    public enum CompletionState
    {
        Completed,
        PartiallyCompleted,
        Failed,
        Cancelled,
    }

    private sealed record VendorPurchaseRequest(
        uint              ItemId,
        string            ItemName,
        uint              Cost,
        uint              CurrencyItemId,
        string            CurrencyName,
        VendorCurrencyGroup CurrencyGroup,
        uint              Quantity,
        VendorNpc         Vendor,
        VendorNpcLocation Location,
        VendorShopType    ShopType
    );

    public sealed record PurchaseResult(
        CompletionState State,
        uint            ItemId,
        string          ItemName,
        uint            RequestedQuantity,
        uint            CompletedQuantity,
        VendorNpc       Vendor,
        string          Message
    );

    private static readonly TimeSpan ActionThrottle       = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan InteractionCooldown  = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShopOpenTimeout      = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ConfirmTimeout       = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PurchaseTimeout      = TimeSpan.FromSeconds(10);
    private const           int      MaxInteractionTries  = 5;
    private const           uint     MaxPurchaseBatchSize = 99;

    private State                  _state = State.Idle;
    private VendorPurchaseRequest? _request;
    private DateTime               _stateStartTime = DateTime.MinValue;
    private DateTime               _lastActionTime = DateTime.MinValue;
    private int                    _interactionAttempts;
    private int                    _ownedCountBeforePurchase;
    private uint                   _completedQuantity;
    private uint                   _currentBatchQuantity;
    private string                 _statusText = string.Empty;
    private bool                   _inclusionPageSelected;
    private bool                   _inclusionSubPageSelected;
    private bool                   _gcRankSelected;
    private bool                   _gcCategorySelected;
    private bool                   _grandCompanyQuantityPrepared;

    public event Action<PurchaseResult>? PurchaseFinished;

    public bool IsRunning
        => _state != State.Idle && _request != null;

    public string StatusText
        => _statusText;

    public bool IsRunningFor(VendorShopEntry entry, VendorNpc npc)
        => _request != null
        && _request.ItemId == entry.ItemId
        && _request.ShopType == entry.ShopType
        && VendorPreferenceHelper.MatchesVendor(_request.Vendor, npc);

    private static bool IsDirectSpecialShopPurchaseSupported(VendorShopEntry entry, VendorNpc vendor)
        => entry.Group is VendorCurrencyGroup.Tomestones or VendorCurrencyGroup.BicolorGemstones or VendorCurrencyGroup.Scrips
        && vendor.MenuShopType == VendorMenuShopType.SpecialShop
        && vendor.ShopItemIndex >= 0
        && vendor.SourceShopId != 0;

    private static bool IsGrandCompanyPurchaseSupported(VendorNpc vendor)
        => vendor.MenuShopType == VendorMenuShopType.GrandCompanyShop
        && vendor.GcRankIndex >= 0
        && vendor.GcCategoryIndex >= 0;

    public static bool IsPurchaseSupported(VendorShopEntry entry, VendorNpc vendor)
        => entry.ShopType switch
        {
            VendorShopType.GilShop => vendor.MenuShopType == VendorMenuShopType.GilShop,
            VendorShopType.SpecialCurrency =>
                (vendor.MenuShopType == VendorMenuShopType.InclusionShop
                && vendor.InclusionPageIndex >= 0
                && vendor.ShopItemIndex >= 0
                && vendor.SourceShopId != 0
                || IsDirectSpecialShopPurchaseSupported(entry, vendor)),
            VendorShopType.GrandCompanySeals => IsGrandCompanyPurchaseSupported(vendor),
            _ => false,
        };

    public void StartPurchase(VendorShopEntry entry, VendorNpc vendor, VendorNpcLocation location, uint quantity, bool continueCurrentVendorInteraction = false)
    {
        if (VendorDevExclusions.IsExcluded(vendor))
        {
            GatherBuddy.Log.Warning($"[VendorPurchaseManager] 忽略对开发排除列表中的商人的采购请求 {vendor.Name} ({vendor.NpcId}/{vendor.MenuShopType}/{vendor.ShopId}/{vendor.SourceShopId}:{vendor.ShopItemIndex}, gc={vendor.GcRankIndex}/{vendor.GcCategoryIndex})");
            return;
        }
        if (!IsPurchaseSupported(entry, vendor))
        {
            GatherBuddy.Log.Warning($"[VendorPurchaseManager] 不支持的采购请求 {entry.ItemName}: shopType={entry.ShopType}, menuShopType={vendor.MenuShopType}, shopId={vendor.ShopId}, sourceShopId={vendor.SourceShopId}, shopItemIndex={vendor.ShopItemIndex}, gcRankIndex={vendor.GcRankIndex}, gcCategoryIndex={vendor.GcCategoryIndex}");
            return;
        }

        Stop();

        var requestedQuantity = quantity == 0 ? 1u : quantity;
        VendorInteractionHelper.ResetShopSelectionState(vendor);
        _request = new VendorPurchaseRequest(entry.ItemId, entry.ItemName, entry.Cost, entry.CurrencyItemId, entry.CurrencyName, entry.Group, requestedQuantity, vendor,
            location, entry.ShopType);
        _interactionAttempts = 0;
        _ownedCountBeforePurchase = CountItemOnCharacter(entry.ItemId);
        _completedQuantity = 0;
        _currentBatchQuantity = 0;
        _inclusionPageSelected = false;
        _inclusionSubPageSelected = false;
        _gcRankSelected = false;
        _gcCategorySelected = false;
        _grandCompanyQuantityPrepared = false;
        _lastActionTime = DateTime.MinValue;
        _stateStartTime = DateTime.UtcNow;
        _statusText = continueCurrentVendorInteraction
            ? $"继续从 {vendor.Name} 购买 {requestedQuantity:N0}x {entry.ItemName}"
            : $"正在导航到 {vendor.Name} 以购买 {requestedQuantity:N0}x {entry.ItemName}";
        _state = continueCurrentVendorInteraction
            ? State.OpeningShop
            : State.WaitingForNavigation;

        YesAlready.Lock();
        if (continueCurrentVendorInteraction)
            return;
        else
            GatherBuddy.Log.Information($"[VendorPurchaseManager] 开始 {entry.ShopType} 采购 {requestedQuantity:N0}x {entry.ItemName}(来自 {vendor.Name}, menu={vendor.MenuShopType}, shop={vendor.ShopId}, source={vendor.SourceShopId}, itemIndex={vendor.ShopItemIndex}, gc={vendor.GcRankIndex}/{vendor.GcCategoryIndex})");
        GatherBuddy.VendorNavigator.StartNavigation(location, continueCurrentVendorInteraction);
    }

    public void Update()
    {
        if (_request == null || _state == State.Idle)
            return;

        try
        {
            switch (_state)
            {
                case State.WaitingForNavigation:       UpdateWaitingForNavigation();       break;
                case State.OpeningShop:                UpdateOpeningShop();                break;
                case State.PurchasingItem:             UpdatePurchasingItem();             break;
                case State.ConfirmingPurchase:         UpdateConfirmingPurchase();         break;
                case State.WaitingForPurchaseComplete: UpdateWaitingForPurchaseComplete(); break;
            }
        }
        catch (Exception ex)
        {
            Fail($"为 {_request.ItemName} 进行的商人采购失败: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (_request == null)
        {
            ResetState();
            return;
        }

        var result = new PurchaseResult(
            CompletionState.Cancelled,
            _request.ItemId,
            _request.ItemName,
            _request.Quantity,
            _completedQuantity,
            _request.Vendor,
            $"已取消从 {_request.Vendor.Name} 购买 {_request.ItemName}");
        ResetState();
        PurchaseFinished?.Invoke(result);
    }

    public void Dispose()
        => Stop();

    private uint GetRemainingQuantity()
        => _request == null || _request.Quantity <= _completedQuantity
            ? 0
            : _request.Quantity - _completedQuantity;

    private static bool RequiresSinglePurchaseBatch(uint itemId)
        => VendorShopResolver.HousingItemIds.Contains(itemId);

    private uint GetDesiredBatchQuantity(uint remainingQuantity)
    {
        if (_request == null || remainingQuantity == 0)
            return 0;

        var requiresSinglePurchaseBatch = RequiresSinglePurchaseBatch(_request.ItemId);
        return Math.Min(remainingQuantity, requiresSinglePurchaseBatch ? 1u : MaxPurchaseBatchSize);
    }

    private bool TryPrepareBatchQuantity(uint remainingQuantity)
    {
        if (_request == null)
            return false;

        _currentBatchQuantity = 0;
        var desiredBatchQuantity = GetDesiredBatchQuantity(remainingQuantity);
        if (desiredBatchQuantity == 0)
            return false;

        if (_request.Cost == 0)
        {
            _currentBatchQuantity = desiredBatchQuantity;
            return true;
        }

        var availability = VendorCurrencyAvailabilityResolver.Resolve(_request.CurrencyGroup, _request.CurrencyItemId, _request.CurrencyName);
        var affordableBatchQuantity = Math.Min(desiredBatchQuantity, availability.AvailableAmount / _request.Cost);
        if (affordableBatchQuantity == 0)
        {
            HandleCurrencyExhaustion(availability);
            return false;
        }

        if (affordableBatchQuantity < desiredBatchQuantity)
            GatherBuddy.Log.Warning(
                $"[VendorPurchaseManager] 只有 {availability.AvailableAmount:N0} {availability.CurrencyName} 可用于 {_request.ItemName}; 将当前批次从 {desiredBatchQuantity:N0} 限制到 {affordableBatchQuantity:N0}(单价 {_request.Cost:N0}, 来源={availability.Source})");

        _currentBatchQuantity = affordableBatchQuantity;
        return true;
    }

    private void HandleCurrencyExhaustion(VendorCurrencyAvailability availability)
    {
        if (_request == null)
            return;

        var remainingQuantity = GetRemainingQuantity();
        var message = _completedQuantity > 0
            ? $"已从 {_request.Vendor.Name} 购买 {_completedQuantity:N0}/{_request.Quantity:N0}x {_request.ItemName}, 剩余 {remainingQuantity:N0}x 无法负担, 仅有 {availability.AvailableAmount:N0} {availability.CurrencyName}(单价 {_request.Cost:N0})"
            : $"没有足够的 {availability.CurrencyName} 从 {_request.Vendor.Name} 购买 {_request.ItemName}, 1x 需要 {_request.Cost:N0}, 但只有 {availability.AvailableAmount:N0} 可用";

        if (_completedQuantity > 0)
        {
            CompletePartially(message);
            return;
        }

        Fail(message);
    }

    private void BeginNextBatch()
    {
        if (_request == null)
            return;

        _currentBatchQuantity = 0;
        _interactionAttempts = 0;
        _inclusionPageSelected = false;
        _inclusionSubPageSelected = false;
        _gcRankSelected = false;
        _gcCategorySelected = false;
        _grandCompanyQuantityPrepared = false;
        _state = State.OpeningShop;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.MinValue;
        _statusText = $"准备下一批 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
    }

    private bool TryAdvancePurchaseProgress()
    {
        if (_request == null || _currentBatchQuantity == 0)
            return false;

        var currentCount = CountItemOnCharacter(_request.ItemId);
        var batchIncrease = currentCount - _ownedCountBeforePurchase;
        if (batchIncrease <= 0)
            return false;

        var purchasedThisBatch = (uint)batchIncrease;
        purchasedThisBatch = Math.Min(purchasedThisBatch, Math.Min(_currentBatchQuantity, GetRemainingQuantity()));
        if (purchasedThisBatch == 0)
            return false;

        _completedQuantity += purchasedThisBatch;

        if (_completedQuantity >= _request.Quantity)
        {
            Complete($"已从 {_request.Vendor.Name} 购买 {_completedQuantity:N0}x {_request.ItemName}");
            return true;
        }

        BeginNextBatch();
        return true;
    }

    private void UpdateWaitingForNavigation()
    {
        if (_request == null)
            return;

        var navigator = GatherBuddy.VendorNavigator;
        if (navigator.IsFailed)
        {
            Fail($"无法导航到 {_request.Vendor.Name} 以购买 {_request.ItemName}");
            return;
        }

        if (!navigator.IsReadyToPurchase)
            return;

        _state = State.OpeningShop;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.MinValue;
        _interactionAttempts = 0;
        _statusText = $"正在打开 {_request.Vendor.Name} 的商店";
    }

    private unsafe void UpdateOpeningShop()
    {
        if (_request == null || !GenericHelpers.IsScreenReady())
            return;

        if (_request.ShopType == VendorShopType.GilShop
         && GenericHelpers.TryGetAddonByName("Shop", out AtkUnitBase* gilShop)
         && gilShop->IsVisible)
        {
            _state = State.PurchasingItem;
            _stateStartTime = DateTime.UtcNow;
            _lastActionTime = DateTime.MinValue;
            _statusText = $"正在金币商店中选择 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
            return;
        }

        if (_request.ShopType == VendorShopType.SpecialCurrency
         && GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* inclusionShop)
         && inclusionShop->IsVisible)
        {
            _state = State.PurchasingItem;
            _stateStartTime = DateTime.UtcNow;
            _lastActionTime = DateTime.MinValue;
            _statusText = $"正在综合商店中选择 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
            return;
        }

        if (_request.ShopType == VendorShopType.GrandCompanySeals
         && GenericHelpers.TryGetAddonByName("GrandCompanyExchange", out AtkUnitBase* grandCompanyExchange)
         && grandCompanyExchange->IsVisible)
        {
            _state = State.PurchasingItem;
            _stateStartTime = DateTime.UtcNow;
            _lastActionTime = DateTime.MinValue;
            _statusText = $"正在国防联军商店中选择 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
            return;
        }

        if (_request.ShopType == VendorShopType.SpecialCurrency
         && _request.Vendor.MenuShopType == VendorMenuShopType.SpecialShop)
        {
            if (GenericHelpers.TryGetAddonByName("ShopExchangeItem", out AtkUnitBase* itemExchange) && itemExchange->IsVisible)
            {
                _state = State.PurchasingItem;
                _stateStartTime = DateTime.UtcNow;
                _lastActionTime = DateTime.MinValue;
                _statusText = $"正在直接特殊商店中选择 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }

            if (GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* currencyExchange) && currencyExchange->IsVisible)
            {
                _state = State.PurchasingItem;
                _stateStartTime = DateTime.UtcNow;
                _lastActionTime = DateTime.MinValue;
                _statusText = $"正在直接特殊商店中选择 {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }
        }

        if ((DateTime.UtcNow - _lastActionTime) >= ActionThrottle)
        {
            if (VendorInteractionHelper.TryClickTalk())
            {
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"推进与 {_request.Vendor.Name} 的对话";
                return;
            }

            if (VendorInteractionHelper.TrySelectShopOption(_request.Vendor, out var selectionError))
            {
                _lastActionTime = DateTime.UtcNow;
                _stateStartTime = DateTime.UtcNow;
                _statusText = $"正在选择 {_request.ItemName} 的商人路线";
                return;
            }

            if (selectionError != null)
            {
                Fail(selectionError);
                return;
            }
        }

        if (_interactionAttempts == 0 || (DateTime.UtcNow - _lastActionTime) >= InteractionCooldown)
        {
            if (AttemptVendorInteraction("打开商店"))
                return;
        }

        if ((DateTime.UtcNow - _stateStartTime) > ShopOpenTimeout)
        {
            if (_interactionAttempts >= MaxInteractionTries)
            {
                Fail($"打开 {_request.Vendor.Name} 的 {DescribeShopLabel(_request)} 超时(购买 {_request.ItemName})");
                return;
            }

            AttemptVendorInteraction("重试商店交互");
            _stateStartTime = DateTime.UtcNow;
        }
    }

    private unsafe void UpdatePurchasingItem()
    {
        if (_request == null)
            return;

        var remainingQuantity = GetRemainingQuantity();
        if (remainingQuantity == 0)
        {
            Complete($"已从 {_request.Vendor.Name} 购买 {_request.Quantity:N0}x {_request.ItemName}");
            return;
        }

        if (_request.ShopType == VendorShopType.SpecialCurrency)
        {
            UpdatePurchasingSpecialCurrencyItem();
            return;
        }

        if (_request.ShopType == VendorShopType.GrandCompanySeals)
        {
            UpdatePurchasingGrandCompanyItem();
            return;
        }

        if (!GenericHelpers.TryGetAddonByName("Shop", out AtkUnitBase* shop))
        {
            if ((DateTime.UtcNow - _stateStartTime) > ShopOpenTimeout)
                Fail($"金币商店在选择 {_request.ItemName} 之前已关闭");
            return;
        }

        if ((DateTime.UtcNow - _lastActionTime) < ActionThrottle)
            return;

        var reader = new VendorGilShopReader(shop);
        var targetItem = reader.Items.FirstOrDefault(item => item.ItemId == _request.ItemId);
        if (targetItem == null)
        {
            Fail($"在 {_request.Vendor.Name} 的金币商店中找不到 {_request.ItemName}");
            return;
        }
        if (!TryPrepareBatchQuantity(remainingQuantity))
            return;
        _ownedCountBeforePurchase = CountItemOnCharacter(_request.ItemId);
        Callback.Fire(shop, true, 0, targetItem.Index, _currentBatchQuantity, 0);

        _state = State.ConfirmingPurchase;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.UtcNow;
        _statusText = $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";

        _state = State.ConfirmingPurchase;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.UtcNow;
        _statusText = $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
    }

    private unsafe void UpdatePurchasingDirectSpecialShopItem()
    {
        if (_request == null)
            return;

        if (_request.Vendor.ShopItemIndex < 0)
        {
            Fail($"没有可用于 {_request.ItemName} 的特殊商店物品索引");
            return;
        }

        var activeAddonName = string.Empty;
        if (GenericHelpers.TryGetAddonByName("ShopExchangeItem", out AtkUnitBase* itemShop) && itemShop->IsVisible)
            activeAddonName = "ShopExchangeItem";
        else if (GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* currencyShop) && currencyShop->IsVisible)
            activeAddonName = "ShopExchangeCurrency";

        if (activeAddonName.Length == 0)
        {
            if ((DateTime.UtcNow - _stateStartTime) > ShopOpenTimeout)
                Fail($"直接特殊商店在选择 {_request.ItemName} 之前已关闭");
            return;
        }

        if ((DateTime.UtcNow - _lastActionTime) < ActionThrottle)
            return;

        var remainingQuantity = GetRemainingQuantity();
        if (!TryPrepareBatchQuantity(remainingQuantity))
            return;
        _ownedCountBeforePurchase = CountItemOnCharacter(_request.ItemId);
        if (!VendorInteractionHelper.TrySelectSpecialShopItem(_request.Vendor.ShopItemIndex, _request.ItemId, _currentBatchQuantity, out var itemError))
        {
            if (itemError != null)
                Fail(itemError);
            return;
        }

        _state = State.ConfirmingPurchase;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.UtcNow;
        _statusText = $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
    }

    private unsafe void UpdatePurchasingSpecialCurrencyItem()
    {
        if (_request == null)
            return;
        if (_request.Vendor.MenuShopType == VendorMenuShopType.SpecialShop)
        {
            UpdatePurchasingDirectSpecialShopItem();
            return;
        }

        if (_request.Vendor.ShopItemIndex < 0)
        {
            Fail($"没有可用于 {_request.ItemName} 的综合商店物品索引");
            return;
        }

        if (!GenericHelpers.TryGetAddonByName("InclusionShop", out AtkUnitBase* shop) || !shop->IsVisible)
        {
            if ((DateTime.UtcNow - _stateStartTime) > ShopOpenTimeout)
                Fail($"综合商店在选择 {_request.ItemName} 之前已关闭");
            return;
        }

        if ((DateTime.UtcNow - _lastActionTime) < ActionThrottle)
            return;

        if (!_inclusionPageSelected && _request.Vendor.InclusionPageIndex >= 0)
        {
            if (VendorInteractionHelper.TrySelectInclusionPage(_request.Vendor.InclusionPageIndex, out var pageError))
            {
                _inclusionPageSelected = true;
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"正在选择 {_request.ItemName} 的综合商店页面 ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }

            if (pageError != null)
            {
                Fail(pageError);
                return;
            }
        }

        if (!_inclusionSubPageSelected && _request.Vendor.InclusionSubPageIndex > 0)
        {
            if (VendorInteractionHelper.TrySelectInclusionSubPage(_request.Vendor.InclusionSubPageIndex, out var subPageError))
            {
                _inclusionSubPageSelected = true;
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"正在选择 {_request.ItemName} 的综合商店子页面 ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }

            if (subPageError != null)
            {
                Fail(subPageError);
                return;
            }
        }

        var remainingQuantity = GetRemainingQuantity();
        if (!TryPrepareBatchQuantity(remainingQuantity))
            return;
        _ownedCountBeforePurchase = CountItemOnCharacter(_request.ItemId);
        if (!VendorInteractionHelper.TrySelectInclusionShopItem(_request.Vendor.ShopItemIndex, _request.ItemId, _currentBatchQuantity, out var itemError))
        {
            if (itemError != null)
                Fail(itemError);
            return;
        }

        _state = State.ConfirmingPurchase;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.UtcNow;
        _statusText = $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
    }

    private unsafe void UpdatePurchasingGrandCompanyItem()
    {
        if (_request == null)
            return;

        if (!GenericHelpers.TryGetAddonByName("GrandCompanyExchange", out AtkUnitBase* shop) || !shop->IsVisible)
        {
            if ((DateTime.UtcNow - _stateStartTime) > ShopOpenTimeout)
                Fail($"国防联军商店在选择 {_request.ItemName} 之前已关闭");
            return;
        }

        if ((DateTime.UtcNow - _lastActionTime) < ActionThrottle)
            return;

        if (!_gcRankSelected)
        {
            if (VendorInteractionHelper.IsGrandCompanyRankTabSelected(_request.Vendor.GcRankIndex, out var rankError))
            {
                _gcRankSelected = true;
            }
            else if (rankError != null)
            {
                Fail(rankError);
                return;
            }
            else if (VendorInteractionHelper.TrySelectGrandCompanyRankTab(_request.Vendor.GcRankIndex, out rankError))
            {
                _gcRankSelected = true;
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"正在选择 {_request.ItemName} 的国防联军军衔选项卡 ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }
            else if (rankError != null)
            {
                Fail(rankError);
                return;
            }
            else
            {
                return;
            }
        }

        if (!_gcCategorySelected)
        {
            if (VendorInteractionHelper.IsGrandCompanyCategoryTabSelected(_request.Vendor.GcCategoryIndex, out var categoryError))
            {
                _gcCategorySelected = true;
            }
            else if (categoryError != null)
            {
                Fail(categoryError);
                return;
            }
            else if (VendorInteractionHelper.TrySelectGrandCompanyCategoryTab(_request.Vendor.GcCategoryIndex, out categoryError))
            {
                _gcCategorySelected = true;
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"正在选择 {_request.ItemName} 的国防联军分类选项卡 ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }
            else if (categoryError != null)
            {
                Fail(categoryError);
                return;
            }
            else
            {
                return;
            }
        }

        var currentGrandCompanyId = GetCurrentGrandCompanyId();
        if (currentGrandCompanyId == 0)
        {
            Fail("无法确定请求商店对应的当前国防联军");
            return;
        }

        var currentSealCurrencyItemId = GetGrandCompanySealCurrencyItemId(currentGrandCompanyId);
        if (_request.CurrencyItemId != 0 && currentSealCurrencyItemId != 0 && _request.CurrencyItemId != currentSealCurrencyItemId)
        {
            Fail($"{_request.ItemName} 不由当前国防联军出售");
            return;
        }
        var remainingQuantity = GetRemainingQuantity();
        if (!TryPrepareBatchQuantity(remainingQuantity))
            return;

        _ownedCountBeforePurchase = CountItemOnCharacter(_request.ItemId);
        if (!VendorInteractionHelper.TrySelectGrandCompanyItem(_request.ItemId, _currentBatchQuantity, GetCurrentGrandCompanyRank(currentGrandCompanyId),
                out var selectedQuantity, out var opensCurrencyExchange, out var itemError))
        {
            if (itemError != null)
                Fail(itemError);
            return;
        }

        _currentBatchQuantity = selectedQuantity;
        _grandCompanyQuantityPrepared = !opensCurrencyExchange;
        _state = State.ConfirmingPurchase;
        _stateStartTime = DateTime.UtcNow;
        _lastActionTime = DateTime.UtcNow;
        _statusText = opensCurrencyExchange
            ? $"设置 {_currentBatchQuantity:N0}x {_request.ItemName} 的数量 ({_completedQuantity:N0}/{_request.Quantity:N0})"
            : $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
    }

    private void UpdateConfirmingPurchase()
    {
        if (_request == null)
            return;

        if (_request.ShopType == VendorShopType.GrandCompanySeals && !_grandCompanyQuantityPrepared)
        {
            if ((DateTime.UtcNow - _lastActionTime) < ActionThrottle)
                return;

            if (VendorInteractionHelper.TrySetGrandCompanyExchangeQuantity(_currentBatchQuantity, out var quantityError))
            {
                _grandCompanyQuantityPrepared = true;
                _lastActionTime = DateTime.UtcNow;
                _statusText = $"确认购买 {_currentBatchQuantity:N0}x {_request.ItemName} ({_completedQuantity:N0}/{_request.Quantity:N0})";
                return;
            }

            if (quantityError != null)
            {
                Fail(quantityError);
                return;
            }
        }

        if ((DateTime.UtcNow - _lastActionTime) >= ActionThrottle && VendorInteractionHelper.TryConfirmPurchase())
        {
            _state = State.WaitingForPurchaseComplete;
            _stateStartTime = DateTime.UtcNow;
            _lastActionTime = DateTime.UtcNow;
            _statusText = $"等待 {_currentBatchQuantity:N0}x {_request.ItemName} 到达背包或兵装库 ({_completedQuantity:N0}/{_request.Quantity:N0})";
            return;
        }

        if (TryAdvancePurchaseProgress())
            return;

        if ((DateTime.UtcNow - _stateStartTime) > ConfirmTimeout)
            Fail($"等待 {_request.ItemName} 购买确认超时");
    }

    private void UpdateWaitingForPurchaseComplete()
    {
        if (_request == null)
            return;

        if (TryAdvancePurchaseProgress())
            return;

        var shouldAttemptReconfirm = (DateTime.UtcNow - _lastActionTime) >= ActionThrottle;
        var reConfirmed = shouldAttemptReconfirm
            && (_request.ShopType == VendorShopType.GrandCompanySeals
                ? VendorInteractionHelper.TryConfirmYesNo()
                : VendorInteractionHelper.TryConfirmPurchase());
        if (reConfirmed)
        {
            _lastActionTime = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _stateStartTime) > PurchaseTimeout)
            Fail($"等待 {_request.ItemName} 添加到背包或兵装库超时");
    }

    private bool AttemptVendorInteraction(string reason)
    {
        if (_request == null)
            return false;

        _interactionAttempts++;
        _lastActionTime = DateTime.UtcNow;

        if (!VendorInteractionHelper.TryInteractWithTarget(_request.Location))
            return false;

        _statusText = $"{reason}, 与 {_request.Vendor.Name} 交互中";
        return true;
    }

    private void Complete(string message)
    {
        if (_request == null)
            return;

        GatherBuddy.Log.Information($"[VendorPurchaseManager] {message}");
        Communicator.Print($"[GatherBuddyReborn] {message}");
        var result = new PurchaseResult(
            CompletionState.Completed,
            _request.ItemId,
            _request.ItemName,
            _request.Quantity,
            _completedQuantity,
            _request.Vendor,
            message);
        ResetState();
        PurchaseFinished?.Invoke(result);
    }

    private void CompletePartially(string message)
    {
        if (_request == null)
            return;

        GatherBuddy.Log.Error($"[VendorPurchaseManager] {message}");
        Communicator.PrintError($"[GatherBuddyReborn] {message}");
        var result = new PurchaseResult(
            CompletionState.PartiallyCompleted,
            _request.ItemId,
            _request.ItemName,
            _request.Quantity,
            _completedQuantity,
            _request.Vendor,
            message);
        ResetState();
        PurchaseFinished?.Invoke(result);
    }

    private void Fail(string message)
    {
        if (_request == null)
            return;

        GatherBuddy.Log.Error($"[VendorPurchaseManager] {message}");
        Communicator.PrintError($"[GatherBuddyReborn] {message}");
        var result = new PurchaseResult(
            CompletionState.Failed,
            _request.ItemId,
            _request.ItemName,
            _request.Quantity,
            _completedQuantity,
            _request.Vendor,
            message);
        ResetState();
        PurchaseFinished?.Invoke(result);
    }

    private void ResetState()
    {
        if (_request != null)
            VendorInteractionHelper.ResetShopSelectionState(_request.Vendor);
        GatherBuddy.VendorNavigator.Stop();
        YesAlready.Unlock();

        _state = State.Idle;
        _request = null;
        _stateStartTime = DateTime.MinValue;
        _lastActionTime = DateTime.MinValue;
        _interactionAttempts = 0;
        _ownedCountBeforePurchase = 0;
        _completedQuantity = 0;
        _currentBatchQuantity = 0;
        _inclusionPageSelected = false;
        _inclusionSubPageSelected = false;
        _gcRankSelected = false;
        _gcCategorySelected = false;
        _grandCompanyQuantityPrepared = false;
        _statusText = string.Empty;
    }

    private static string DescribeShopLabel(VendorPurchaseRequest request)
        => request.ShopType switch
        {
            VendorShopType.GilShop         => "金币商店",
            VendorShopType.GrandCompanySeals => "国防联军商店",
            VendorShopType.SpecialCurrency => request.Vendor.MenuShopType switch
            {
                VendorMenuShopType.InclusionShop => "综合商店",
                VendorMenuShopType.SpecialShop   => "特殊商店",
                _                                => "特殊货币商店",
            },
            _                              => "商店",
        };

    private static unsafe byte GetCurrentGrandCompanyId()
    {
        var playerState = PlayerState.Instance();
        return playerState == null ? (byte)0 : playerState->GrandCompany;
    }

    private static unsafe uint GetCurrentGrandCompanyRank(byte grandCompanyId)
    {
        if (grandCompanyId == 0)
            return 0;

        var playerState = PlayerState.Instance();
        return playerState == null || playerState->GrandCompany != grandCompanyId
            ? 0u
            : playerState->GetGrandCompanyRank();
    }

    private static uint GetGrandCompanySealCurrencyItemId(uint grandCompanyId)
        => grandCompanyId switch
        {
            1 => 20u,
            2 => 21u,
            3 => 22u,
            _ => 0u,
        };

    private static int CountItemOnCharacter(uint itemId)
        => ItemHelper.GetInventoryAndArmoryItemCount(itemId);
}
