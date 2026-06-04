using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using GatherBuddy.Automation;
using GatherBuddy.AutoGather.Collectables.Data;
using GatherBuddy.Config;
using GatherBuddy.Helpers;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan.Vendors;

namespace GatherBuddy.AutoGather.Collectables;

enum CollectableState
{
    Idle,
    CheckingInventory,
    NavigatingToTurnInNpc,
    OpeningTurnInWindow,
    SelectingJob,
    SelectingItem,
    SubmittingItem,
    CheckingOvercapDialog,
    WaitingForSubmit,
    CheckingForMore,
    ClosingTurnInWindow,
    StartingPurchaseList,
    WaitingForPurchaseList,
    ReturningHome,
    Completed,
    Error,
}

public enum CollectableRunSource
{
    Manual,
    AutoGather,
    VulcanQueue,
}

public unsafe class CollectableManager : IDisposable
{
    private readonly Configuration _config;
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly CollectableWindowHandler _windowHandler;

    public event Action? OnFinishCollecting;
    public event Action<string>? OnError;

    public bool IsRunning { get; private set; }
    public string StatusText { get; private set; } = "空闲";
    public CollectableRunSource CurrentRunSource { get; private set; } = CollectableRunSource.Manual;

    private CollectableState _state = CollectableState.Idle;
    private Queue<CollectableTurnInItem> _turnInQueue = new();
    private readonly Queue<Guid> _pendingPurchaseListIds = new();
    private VendorNpc? _turnInVendor;
    private VendorNpcLocation? _turnInLocation;
    private Guid? _activePurchaseListId;
    private uint _currentItemId;
    private int _currentJobId = -1;
    private DateTime _lastAction = DateTime.MinValue;
    private DateTime _stateStartTime = DateTime.MinValue;
    private readonly TimeSpan _actionDelay = TimeSpan.FromMilliseconds(400);
    private bool _overcapInterrupted;
    private bool _purchaseAttemptedForOvercap;
    private bool _lastOvercapPurchaseHitScripReserveLimit;
    private bool _returnHomeAfterCompletion;
    private bool _homeReturnStarted;
    private bool _runContainsGatheringCollectables;
    private bool _runContainsCraftingCollectables;
    private string? _lastErrorText;
    private string? _completionMessage;
    private CollectableState _nextStateAfterWindowClose = CollectableState.Idle;
    private string? _statusAfterWindowClose;

    public CollectableManager(IFramework framework, ICondition condition, Configuration config)
    {
        _config = config;
        _framework = framework;
        _condition = condition;
        _windowHandler = new CollectableWindowHandler();
    }

    public bool Start(CollectableRunSource source = CollectableRunSource.Manual, bool returnHomeAfterCompletion = false)
    {
        if (IsRunning)
        {
            GatherBuddy.Log.Debug("[收藏品管理器] 收藏品交付已在运行中");
            return false;
        }

        if (!CollectableTurnInRequirements.IsAvailable)
        {
            StatusText = CollectableTurnInRequirements.UnavailableStatusText;
            GatherBuddy.Log.Debug("[收藏品管理器] 无法开始收藏品交付，因为 AllaganTools 和 AllaganItemSearch 均未加载");
            return false;
        }

        CollectableInventoryHelper.InitializeAsync();
        if (!CollectableInventoryHelper.IsTurnInItemMetadataReady)
        {
            StatusText = CollectableInventoryHelper.IsTurnInItemMetadataLoading
                ? "收藏品物品数据仍在加载中。"
                : "收藏品物品数据不可用。";
            return false;
        }

        var availableItems = CollectableInventoryHelper.GetTurnInItems();
        if (availableItems.Count == 0)
        {
            StatusText = "没有可交付的收藏品。";
            return false;
        }

        var route = CollectableTurnInRouteResolver.ResolvePreferredRoute(_config.CollectableConfig.PreferredTurnInRoute);
        if (route == null)
        {
            StatusText = CollectableTurnInRouteResolver.HasLookupData
                ? "收藏品路线位置仍在加载中。"
                : "收藏品路线数据不可用。";
            return false;
        }

        _turnInVendor = route.Vendor;
        _turnInLocation = route.Location;
        _config.CollectableConfig.PreferredTurnInRoute = CollectableTurnInRouteResolver.ToPreference(route);
        _config.Save();

        _turnInQueue = new Queue<CollectableTurnInItem>(availableItems);
        UpdateRunCollectableTypes(availableItems);
        _pendingPurchaseListIds.Clear();
        _activePurchaseListId = null;
        _currentItemId = 0;
        _currentJobId = -1;
        _lastAction = DateTime.MinValue;
        _stateStartTime = DateTime.UtcNow;
        _overcapInterrupted = false;
        _purchaseAttemptedForOvercap = false;
        _lastOvercapPurchaseHitScripReserveLimit = false;
        _returnHomeAfterCompletion = returnHomeAfterCompletion;
        _homeReturnStarted = false;
        _lastErrorText = null;
        _completionMessage = null;
        CurrentRunSource = source;
        IsRunning = true;
        _state = CollectableState.CheckingInventory;
        StatusText = $"开始收藏品交付（{DescribeSource(source)}），地点：{route.DisplayName}。";

        GatherBuddy.Log.Information($"[收藏品管理器] 开始收藏品交付（{DescribeSource(source)}），使用 {route.DisplayName}");
        _framework.Update += OnUpdate;
        return true;
    }

    public void Stop()
    {
        if (!IsRunning && _state == CollectableState.Idle)
            return;

        GatherBuddy.Log.Information("[收藏品管理器] 正在停止收藏品交付");
        CleanupCurrentRun(stopActivePurchaseList: true);
        StatusText = "收藏品交付已停止。";
    }

    public void ClearStatus()
    {
        if (!IsRunning)
            StatusText = "空闲";
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            if (!IsRunning)
                return;

            switch (_state)
            {
                case CollectableState.CheckingInventory:
                    CheckInventory();
                    break;
                case CollectableState.NavigatingToTurnInNpc:
                    UpdateTurnInNavigation();
                    break;
                case CollectableState.OpeningTurnInWindow:
                    OpenTurnInWindow();
                    break;
                case CollectableState.SelectingJob:
                    SelectJob();
                    break;
                case CollectableState.SelectingItem:
                    SelectItem();
                    break;
                case CollectableState.SubmittingItem:
                    SubmitItem();
                    break;
                case CollectableState.CheckingOvercapDialog:
                    CheckOvercapDialog();
                    break;
                case CollectableState.WaitingForSubmit:
                    WaitForSubmit();
                    break;
                case CollectableState.CheckingForMore:
                    CheckForMore();
                    break;
                case CollectableState.ClosingTurnInWindow:
                    CloseTurnInWindow();
                    break;
                case CollectableState.StartingPurchaseList:
                    StartPurchaseList();
                    break;
                case CollectableState.WaitingForPurchaseList:
                    WaitForPurchaseList();
                    break;
                case CollectableState.ReturningHome:
                    ReturnHome();
                    break;
                case CollectableState.Completed:
                    Complete();
                    break;
                case CollectableState.Error:
                    HandleError();
                    break;
            }
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"[收藏品管理器] 收藏品交付出错: {ex}");
            Fail("收藏品自动交付过程中发生意外错误。");
        }
    }

    private void CheckInventory()
    {
        CollectableInventoryHelper.InitializeAsync();
        if (!CollectableInventoryHelper.IsTurnInItemMetadataReady)
        {
            StatusText = CollectableInventoryHelper.IsTurnInItemMetadataLoading
                ? "收藏品物品数据仍在加载中。"
                : "收藏品物品数据不可用。";
            return;
        }
        var route = CollectableTurnInRouteResolver.ResolvePreferredRoute(_config.CollectableConfig.PreferredTurnInRoute);
        if (route == null)
        {
            StatusText = CollectableTurnInRouteResolver.HasLookupData
                ? "收藏品路线位置仍在加载中。"
                : "收藏品路线数据不可用。";
            return;
        }

        _turnInVendor = route.Vendor;
        _turnInLocation = route.Location;
        _config.CollectableConfig.PreferredTurnInRoute = CollectableTurnInRouteResolver.ToPreference(route);
        _config.Save();

        var items = CollectableInventoryHelper.GetTurnInItems();
        _turnInQueue = new Queue<CollectableTurnInItem>(items);
        UpdateRunCollectableTypes(items);
        _currentItemId = 0;
        _currentJobId = -1;

        if (_turnInQueue.Count == 0)
        {
            _completionMessage = "没有可用于交付的收藏品。";
            _state = CollectableState.Completed;
            return;
        }

        VendorInteractionHelper.ResetShopSelectionState(_turnInVendor);
        GatherBuddy.VendorNavigator.StartNavigation(_turnInLocation);
        _stateStartTime = DateTime.UtcNow;
        StatusText = $"正在导航到 {_turnInVendor.Name} 进行收藏品交付。";
        _state = CollectableState.NavigatingToTurnInNpc;
    }

    private void UpdateTurnInNavigation()
    {
        if (_turnInVendor == null || _turnInLocation == null)
        {
            Fail("未配置收藏品交付路线。");
            return;
        }

        if (GatherBuddy.VendorNavigator.IsFailed)
        {
            Fail($"导航到 {_turnInVendor.Name} 进行收藏品交付失败。");
            return;
        }

        if (!GatherBuddy.VendorNavigator.IsReadyToPurchase)
            return;

        _stateStartTime = DateTime.UtcNow;
        _lastAction = DateTime.MinValue;
        StatusText = $"正在打开 {_turnInVendor.Name} 的收藏品菜单。";
        _state = CollectableState.OpeningTurnInWindow;
    }

    private void OpenTurnInWindow()
    {
        if (_turnInVendor == null || _turnInLocation == null)
        {
            Fail("未配置收藏品交付路线。");
            return;
        }

        if (_windowHandler.IsReady)
        {
            _stateStartTime = DateTime.UtcNow;
            _lastAction = DateTime.MinValue;
            StatusText = "正在选择收藏品交付职业。";
            _state = CollectableState.SelectingJob;
            return;
        }

        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        if (VendorInteractionHelper.TryClickTalk())
        {
            _lastAction = DateTime.UtcNow;
            return;
        }

        if (VendorInteractionHelper.TrySelectShopOption(_turnInVendor, out var selectionError))
        {
            _lastAction = DateTime.UtcNow;
            return;
        }

        if (selectionError != null)
        {
            Fail(selectionError);
            return;
        }

        if (VendorInteractionHelper.TryInteractWithTarget(_turnInLocation))
        {
            _lastAction = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _stateStartTime) > TimeSpan.FromSeconds(15))
            Fail($"打开 {_turnInVendor.Name} 的收藏品菜单超时。");
    }

    private void SelectJob()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        if (_turnInQueue.Count == 0)
        {
            BeginCompletionFlow();
            return;
        }

        var next = _turnInQueue.Peek();
        if (_currentJobId != next.JobId)
        {
            _windowHandler.SelectJob((uint)next.JobId);
            _currentJobId = next.JobId;
            _currentItemId = 0;
            _lastAction = DateTime.UtcNow;
            return;
        }

        _state = CollectableState.SelectingItem;
    }

    private void SelectItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        var next = _turnInQueue.Peek();
        if (_currentItemId != next.ItemId)
        {
            _windowHandler.SelectItemById(next.ItemId);
            _currentItemId = next.ItemId;
            _lastAction = DateTime.UtcNow;
            StatusText = $"正在选择 {next.ItemName} 进行交付。";
            return;
        }

        _state = CollectableState.SubmittingItem;
    }

    private void SubmitItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        var next = _turnInQueue.Peek();
        StatusText = $"正在交付 {next.ItemName}。";
        _windowHandler.SubmitItem();
        _lastAction = DateTime.UtcNow;
        _stateStartTime = DateTime.UtcNow;
        _state = CollectableState.CheckingOvercapDialog;
    }

    private void CheckOvercapDialog()
    {
        if (GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno>("SelectYesno", out var addon)
         && GenericHelpers.IsAddonReady((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon))
        {
            Callback.Fire((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon, true, 1);
            _lastAction = DateTime.UtcNow;
            _currentItemId = 0;
            _overcapInterrupted = true;
            GatherBuddy.Log.Warning("[收藏品管理器] 收藏品交付期间检测到工票上限");

            if (_purchaseAttemptedForOvercap)
            {
                DisableAutoTurnInAndFail(_lastOvercapPurchaseHitScripReserveLimit
                    ? "运行购买列表后收藏品仍被工票上限阻止。配置的工票储备阻止了足够的工票消费以继续。"
                    : "运行购买列表后收藏品仍被工票上限阻止。");
                return;
            }

            if (!HasConfiguredPurchaseList())
            {
                DisableAutoTurnInAndFail("收藏品交付时达到工票上限，但未配置收藏品购买列表。");
                return;
            }

            TransitionAfterClosingTurnInWindow(CollectableState.StartingPurchaseList, "已达到工票上限，正在运行收藏品购买列表。");
            return;
        }

        if ((DateTime.UtcNow - _stateStartTime) > TimeSpan.FromMilliseconds(500))
            _state = CollectableState.WaitingForSubmit;
    }

    private void WaitForSubmit()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        if (_turnInQueue.Count == 0)
        {
            BeginCompletionFlow();
            return;
        }

        var current = _turnInQueue.Dequeue();
        var remainingCount = current.Count - 1;
        if (remainingCount > 0)
            _turnInQueue = new Queue<CollectableTurnInItem>(new[] { current with { Count = remainingCount } }.Concat(_turnInQueue));

        _purchaseAttemptedForOvercap = false;
        _lastOvercapPurchaseHitScripReserveLimit = false;
        _currentItemId = 0;
        _lastAction = DateTime.UtcNow;
        StatusText = $"已交付 {current.ItemName}。";
        _state = CollectableState.CheckingForMore;
    }

    private void CheckForMore()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay)
            return;

        if (_turnInQueue.Count > 0)
        {
            _state = CollectableState.SelectingJob;
            return;
        }

        if (ShouldRunPurchaseList())
        {
            TransitionAfterClosingTurnInWindow(CollectableState.StartingPurchaseList, "正在运行收藏品购买列表。");
            return;
        }

        BeginCompletionFlow();
    }

    private VendorPurchaseConstraints? GetPurchaseConstraints()
    {
        var reserveScripAmount = Math.Clamp(_config.CollectableConfig.ReserveScripAmount, 0, 4000);
        return reserveScripAmount > 0
            ? new VendorPurchaseConstraints((uint)reserveScripAmount)
            : null;
    }

    private void CloseTurnInWindow()
    {
        if (!_windowHandler.IsReady)
        {
            var nextState = _nextStateAfterWindowClose;
            var nextStatus = _statusAfterWindowClose;
            _nextStateAfterWindowClose = CollectableState.Idle;
            _statusAfterWindowClose = null;
            _stateStartTime = DateTime.UtcNow;
            _lastAction = DateTime.MinValue;
            if (!string.IsNullOrWhiteSpace(nextStatus))
                StatusText = nextStatus;
            _state = nextState;
            return;
        }

        if ((DateTime.UtcNow - _lastAction) >= _actionDelay)
        {
            _windowHandler.CloseWindow();
            _lastAction = DateTime.UtcNow;
        }

        if ((DateTime.UtcNow - _stateStartTime) > TimeSpan.FromSeconds(10))
            Fail("关闭收藏品交付窗口超时。");
    }

    private void StartPurchaseList()
    {
        if (_activePurchaseListId == null)
        {
            QueuePendingPurchaseLists();
            if (_pendingPurchaseListIds.Count == 0)
            {
                if (_overcapInterrupted)
                    DisableAutoTurnInAndFail("收藏品交付时达到工票上限，但未配置收藏品购买列表。");
                else
                    BeginCompletionFlow();
                return;
            }

            _activePurchaseListId = _pendingPurchaseListIds.Dequeue();
        }

        var purchaseListId = _activePurchaseListId.Value;
        if (purchaseListId == Guid.Empty)
        {
            if (_overcapInterrupted)
                DisableAutoTurnInAndFail("收藏品交付时达到工票上限，但未配置收藏品购买列表。");
            else
                BeginCompletionFlow();
            return;
        }

        var startResult = GatherBuddy.VendorBuyListManager.Start(purchaseListId, GetPurchaseConstraints());
        if (!string.IsNullOrWhiteSpace(GatherBuddy.VendorBuyListManager.StatusText))
            StatusText = GatherBuddy.VendorBuyListManager.StatusText;

        switch (startResult)
        {
            case VendorBuyListManager.StartResult.Started:
            case VendorBuyListManager.StartResult.AlreadyRunning:
            case VendorBuyListManager.StartResult.WaitingForPreviousInteraction:
                if (_overcapInterrupted)
                    _purchaseAttemptedForOvercap = true;

                if (GatherBuddy.VendorBuyListManager.IsBusy)
                {
                    _stateStartTime = DateTime.UtcNow;
                    _state = CollectableState.WaitingForPurchaseList;
                    return;
                }

                HandlePurchaseListCompletion();
                return;
            case VendorBuyListManager.StartResult.VendorDataLoading:
            case VendorBuyListManager.StartResult.LocationDataLoading:
            case VendorBuyListManager.StartResult.AnotherPurchaseRunning:
                return;
            case VendorBuyListManager.StartResult.AutomationUnavailable:
                if (_overcapInterrupted)
                {
                    DisableAutoTurnInAndFail("收藏品交付时达到工票上限，但供应商自动化不可用，因为 Allagan Tools 和 Allagan Item Search 均未安装或启用。");
                    return;
                }
                _activePurchaseListId = null;
                AdvancePurchaseListsOrComplete();
                return;
            case VendorBuyListManager.StartResult.Empty:
            case VendorBuyListManager.StartResult.NoPendingEntries:
                if (_overcapInterrupted)
                {
                    DisableAutoTurnInAndFail($"收藏品交付时达到工票上限，但购买列表 '{GetPurchaseListName(purchaseListId)}' 没有待处理的物品。");
                    return;
                }
                _activePurchaseListId = null;
                AdvancePurchaseListsOrComplete();
                return;
            case VendorBuyListManager.StartResult.NoList:
                if (_overcapInterrupted)
                    DisableAutoTurnInAndFail("配置的收藏品购买列表不可用。");
                else
                {
                    _activePurchaseListId = null;
                    AdvancePurchaseListsOrComplete();
                }
                return;
        }
    }

    private void WaitForPurchaseList()
    {
        if (!string.IsNullOrWhiteSpace(GatherBuddy.VendorBuyListManager.StatusText))
            StatusText = GatherBuddy.VendorBuyListManager.StatusText;

        if (GatherBuddy.VendorBuyListManager.IsBusy)
            return;

        HandlePurchaseListCompletion();
    }

    private void HandlePurchaseListCompletion()
    {
        _activePurchaseListId = null;
        if (_overcapInterrupted)
        {
            _lastOvercapPurchaseHitScripReserveLimit = GatherBuddy.VendorBuyListManager.LastRunHitScripReserveLimit;
            _overcapInterrupted = false;
            _currentItemId = 0;
            _currentJobId = -1;
            _pendingPurchaseListIds.Clear();
            StatusText = "购买列表完成后恢复收藏品交付。";
            _state = CollectableState.CheckingInventory;
            return;
        }
        AdvancePurchaseListsOrComplete();
    }

    private void ReturnHome()
    {
        if (!_homeReturnStarted)
        {
            if (Lifestream.Enabled && Lifestream.IsBusy())
                return;

            if (!HomeNavigationHelper.TryStartReturnHome(out var error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                    GatherBuddy.Log.Warning($"[收藏品管理器] {error}");

                _completionMessage = string.IsNullOrWhiteSpace(error)
                    ? "收藏品交付完成。"
                    : $"收藏品交付完成（未返回家园: {error}）";
                _state = CollectableState.Completed;
                return;
            }

            _homeReturnStarted = true;
            _lastAction = DateTime.UtcNow;
            StatusText = "收藏品交付后正在返回家园。";
            return;
        }

        if (!HomeNavigationHelper.IsReturnComplete())
            return;

        _completionMessage = "收藏品交付完成。";
        _state = CollectableState.Completed;
    }

    private void BeginCompletionFlow()
    {
        if (_returnHomeAfterCompletion && HomeNavigationHelper.ShouldReturnHomeAfterCollectables())
        {
            _homeReturnStarted = false;
            TransitionAfterClosingTurnInWindow(CollectableState.ReturningHome, "收藏品交付后正在返回家园。");
            return;
        }

        _completionMessage = "收藏品交付完成。";
        TransitionAfterClosingTurnInWindow(CollectableState.Completed, _completionMessage);
    }

    private void Complete()
    {
        _completionMessage ??= "收藏品交付完成。";
        GatherBuddy.Log.Information($"[收藏品管理器] {_completionMessage}");
        CleanupCurrentRun(stopActivePurchaseList: false);
        StatusText = _completionMessage;
        OnFinishCollecting?.Invoke();
    }

    private void HandleError()
    {
        var errorMessage = _lastErrorText ?? "收藏品自动交付过程中发生错误。";
        GatherBuddy.Log.Error($"[收藏品管理器] {errorMessage}");
        CleanupCurrentRun(stopActivePurchaseList: true);
        StatusText = errorMessage;
        OnError?.Invoke(errorMessage);
    }

    private void Fail(string message)
    {
        _lastErrorText = message;
        StatusText = message;
        _state = CollectableState.Error;
    }

    private void DisableAutoTurnInAndFail(string message)
    {
        var hardFailMessage = $"{message} 已禁用自动交付收藏品以防止重复失败。请调整收藏品储备或购买列表，然后重新启用自动交付。";
        var shouldSave = false;
        if (_config.CollectableConfig.AutoTurnInCollectables)
        {
            _config.CollectableConfig.AutoTurnInCollectables = false;
            shouldSave = true;
        }

        if (!string.Equals(_config.CollectableConfig.AutoTurnInHardFailReason, hardFailMessage, StringComparison.Ordinal))
        {
            _config.CollectableConfig.AutoTurnInHardFailReason = hardFailMessage;
            shouldSave = true;
        }

        if (shouldSave)
            _config.Save();

        Communicator.PrintError($"[GatherBuddyReborn] {hardFailMessage}");
        Fail(hardFailMessage);
    }

    private void TransitionAfterClosingTurnInWindow(CollectableState nextState, string nextStatus)
    {
        if (!_windowHandler.IsReady)
        {
            _stateStartTime = DateTime.UtcNow;
            _lastAction = DateTime.MinValue;
            StatusText = nextStatus;
            _state = nextState;
            return;
        }

        _nextStateAfterWindowClose = nextState;
        _statusAfterWindowClose = nextStatus;
        _stateStartTime = DateTime.UtcNow;
        _lastAction = DateTime.MinValue;
        StatusText = "正在关闭收藏品交付窗口。";
        _state = CollectableState.ClosingTurnInWindow;
    }

    private void CleanupCurrentRun(bool stopActivePurchaseList)
    {
        _framework.Update -= OnUpdate;
        if (_turnInVendor != null)
            VendorInteractionHelper.ResetShopSelectionState(_turnInVendor);

        if (_windowHandler.IsReady)
            _windowHandler.CloseWindow();

        if (stopActivePurchaseList
         && _activePurchaseListId.HasValue
         && GatherBuddy.VendorBuyListManager.IsBusy
         && GatherBuddy.VendorBuyListManager.RunningListId == _activePurchaseListId.Value)
        {
            GatherBuddy.VendorBuyListManager.Stop();
        }
        else if (_activePurchaseListId == null && (GatherBuddy.VendorNavigator.IsActive || GatherBuddy.VendorNavigator.IsReadyToPurchase))
        {
            GatherBuddy.VendorNavigator.Stop();
        }

        IsRunning = false;
        _state = CollectableState.Idle;
        _turnInQueue.Clear();
        _pendingPurchaseListIds.Clear();
        _turnInVendor = null;
        _turnInLocation = null;
        _activePurchaseListId = null;
        _currentItemId = 0;
        _currentJobId = -1;
        _lastAction = DateTime.MinValue;
        _stateStartTime = DateTime.MinValue;
        _overcapInterrupted = false;
        _purchaseAttemptedForOvercap = false;
        _lastOvercapPurchaseHitScripReserveLimit = false;
        _returnHomeAfterCompletion = false;
        _homeReturnStarted = false;
        _runContainsGatheringCollectables = false;
        _runContainsCraftingCollectables = false;
        _completionMessage = null;
        _lastErrorText = null;
        _nextStateAfterWindowClose = CollectableState.Idle;
        _statusAfterWindowClose = null;
    }

    private bool HasConfiguredPurchaseList()
        => GetConfiguredPurchaseListIds(_overcapInterrupted).Count > 0;

    private bool ShouldRunPurchaseList()
        => _config.CollectableConfig.BuyAfterEachCollect && HasConfiguredPurchaseList();

    private void UpdateRunCollectableTypes(IReadOnlyCollection<CollectableTurnInItem> items)
    {
        _runContainsGatheringCollectables = false;
        _runContainsCraftingCollectables = false;

        foreach (var item in items)
        {
            if (IsGatheringCollectable(item))
                _runContainsGatheringCollectables = true;
            else
                _runContainsCraftingCollectables = true;
        }
    }

    private void QueuePendingPurchaseLists()
    {
        _pendingPurchaseListIds.Clear();
        foreach (var purchaseListId in GetConfiguredPurchaseListIds(_overcapInterrupted))
            _pendingPurchaseListIds.Enqueue(purchaseListId);
    }

    private List<Guid> GetConfiguredPurchaseListIds(bool prioritizeCurrentTurnInType)
    {
        var purchaseListIds = new List<Guid>();

        switch (CurrentRunSource)
        {
            case CollectableRunSource.AutoGather:
                AddPurchaseListIdIfConfigured(purchaseListIds, _config.CollectableConfig.GatheringPurchaseListId);
                break;
            case CollectableRunSource.VulcanQueue:
                AddPurchaseListIdIfConfigured(purchaseListIds, _config.CollectableConfig.CraftingPurchaseListId);
                break;
            default:
                if (prioritizeCurrentTurnInType && TryGetCurrentTurnInPurchaseListId(out var currentPurchaseListId))
                {
                    AddPurchaseListIdIfConfigured(purchaseListIds, currentPurchaseListId);
                    break;
                }

                if (_runContainsGatheringCollectables)
                    AddPurchaseListIdIfConfigured(purchaseListIds, _config.CollectableConfig.GatheringPurchaseListId);

                if (_runContainsCraftingCollectables)
                    AddPurchaseListIdIfConfigured(purchaseListIds, _config.CollectableConfig.CraftingPurchaseListId);
                break;
        }

        return purchaseListIds;
    }

    private void AdvancePurchaseListsOrComplete()
    {
        if (_pendingPurchaseListIds.Count > 0)
        {
            var nextPurchaseListId = _pendingPurchaseListIds.Peek();
            StatusText = $"正在运行收藏品购买列表 '{GetPurchaseListName(nextPurchaseListId)}'。";
            _stateStartTime = DateTime.UtcNow;
            _state = CollectableState.StartingPurchaseList;
            return;
        }

        BeginCompletionFlow();
    }

    private bool TryGetCurrentTurnInPurchaseListId(out Guid purchaseListId)
    {
        purchaseListId = Guid.Empty;
        if (_turnInQueue.Count == 0)
            return false;

        var currentTurnInItem = _turnInQueue.Peek();
        purchaseListId = IsGatheringCollectable(currentTurnInItem)
            ? _config.CollectableConfig.GatheringPurchaseListId
            : _config.CollectableConfig.CraftingPurchaseListId;
        return purchaseListId != Guid.Empty;
    }

    private static void AddPurchaseListIdIfConfigured(ICollection<Guid> purchaseListIds, Guid purchaseListId)
    {
        if (purchaseListId != Guid.Empty && !purchaseListIds.Contains(purchaseListId))
            purchaseListIds.Add(purchaseListId);
    }

    private static bool IsGatheringCollectable(CollectableTurnInItem item)
        => item.JobId >= 8;

    private string GetPurchaseListName(Guid purchaseListId)
        => GatherBuddy.VendorBuyListManager.Lists.FirstOrDefault(list => list.Id == purchaseListId)?.Name ?? purchaseListId.ToString();

    private static string DescribeSource(CollectableRunSource source)
        => source switch
        {
            CollectableRunSource.AutoGather => "自动采集",
            CollectableRunSource.VulcanQueue => "Vulcan 队列",
            _ => "手动模式",
        };

    public void Dispose()
        => Stop();
}
