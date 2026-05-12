using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using GatherBuddy.Automation;
using GatherBuddy.Helpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using GatherBuddy.AutoGather.Collectables.Data;
using GatherBuddy.Config;
using GatherBuddy.Plugin;
using GatherBuddy.SeFunctions;
using Lumina.Excel.Sheets;

namespace GatherBuddy.AutoGather.Collectables;

enum CollectableState
{
    Idle,
    CheckingInventory,
    MovingToShop,
    WaitingForArrival,
    WaitingForShopWindow,
    SelectingJob,
    SelectingItem,
    SubmittingItem,
    CheckingOvercapDialog,
    WaitingForSubmit,
    CheckingForMore,
    MovingToScripShop,
    WaitingForScripShopArrival,
    WaitingForScripShopWindow,
    SelectingScripShopPage,
    SelectingScripShopSubPage,
    SelectingScripShopItem,
    PurchasingScripShopItem,
    WaitingForPurchaseComplete,
    CheckingForMorePurchases,
    Completed,
    Error
}

public class CollectableManager : IDisposable
{
    private readonly Configuration _config;
    private readonly IFramework _framework;
    private readonly ICondition _condition;
    private readonly CollectableWindowHandler _windowHandler;
    private readonly ScripShopWindowHandler _scripShopWindowHandler;
    private readonly TaskManager _taskManager;
    
    public event System.Action? OnFinishCollecting;
    public event System.Action<string>? OnError;
    
    public bool IsRunning { get; private set; }
    
    private CollectableState _state = CollectableState.Idle;
    private Queue<(uint itemId, string name, int count, int jobId)> _turnInQueue = new();
    private uint _currentItemId = 0;
    private string? _currentItemName;
    private int _currentJobId = -1;
    private DateTime _lastAction = DateTime.MinValue;
    private TimeSpan _actionDelay = TimeSpan.FromMilliseconds(500);
    private int _phaseCounter = 0;
    private DateTime _movementStartTime = DateTime.MinValue;
    private bool _teleportAttempted = false;
    private bool _lifestreamAttempted = false;
    private Queue<(uint itemId, string name, int remaining, int cost, int page, int subPage)> _purchaseQueue = new();
    private int _currentPurchaseAmount = 0;
    private bool _overcapInterrupted = false;
    private DateTime _overcapCheckStartTime = DateTime.MinValue;
    
    public CollectableManager(IFramework framework, ICondition condition, Configuration config)
    {
        _config = config;
        _framework = framework;
        _condition = condition;
        _windowHandler = new CollectableWindowHandler();
        _scripShopWindowHandler = new ScripShopWindowHandler();
        _taskManager = new TaskManager(framework);
        _taskManager.ShowDebug = false;
    }
    
    public void Start()
    {
        if (IsRunning)
        {
            GatherBuddy.Log.Debug("[收藏品管理器] 已在运行中");
            return;
        }
        
        if (!HasCollectables())
        {
            GatherBuddy.Log.Debug("[收藏品管理器] 背包中未找到收藏品");
            return;
        }
        
        GatherBuddy.Log.Information("[收藏品管理器] 开始交付收藏品");
        IsRunning = true;
        _state = CollectableState.CheckingInventory;
        _framework.Update += OnUpdate;
    }
    
    public void Stop()
    {
        if (!IsRunning) return;
        
        GatherBuddy.Log.Information("[收藏品管理器] 停止交付收藏品");
        _framework.Update -= OnUpdate;
        _taskManager.Abort();
        IsRunning = false;
        _state = CollectableState.Idle;
        _turnInQueue.Clear();
        _purchaseQueue.Clear();
        _windowHandler.CloseWindow();
        _scripShopWindowHandler.CloseShop();
    }
    
    private void OnUpdate(IFramework framework)
    {
        try
        {
            if (!IsRunning) return;
            
            switch (_state)
            {
                case CollectableState.CheckingInventory:
                    CheckInventory();
                    break;
                    
                case CollectableState.MovingToShop:
                    MoveToShop();
                    break;
                    
                case CollectableState.WaitingForArrival:
                    WaitForArrival();
                    break;
                    
                case CollectableState.WaitingForShopWindow:
                    WaitForShopWindow();
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
                    
                case CollectableState.MovingToScripShop:
                    MoveToScripShop();
                    break;
                    
                case CollectableState.WaitingForScripShopArrival:
                    WaitForScripShopArrival();
                    break;
                    
                case CollectableState.WaitingForScripShopWindow:
                    WaitForScripShopWindow();
                    break;
                    
                case CollectableState.SelectingScripShopPage:
                    SelectScripShopPage();
                    break;
                    
                case CollectableState.SelectingScripShopSubPage:
                    SelectScripShopSubPage();
                    break;
                    
                case CollectableState.SelectingScripShopItem:
                    SelectScripShopItem();
                    break;
                    
                case CollectableState.PurchasingScripShopItem:
                    PurchaseScripShopItem();
                    break;
                    
                case CollectableState.WaitingForPurchaseComplete:
                    WaitForPurchaseComplete();
                    break;
                    
                case CollectableState.CheckingForMorePurchases:
                    CheckForMorePurchases();
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
            GatherBuddy.Log.Error($"[收藏品管理器] 状态机出错: {ex}");
            _state = CollectableState.Error;
        }
    }
    
    private void CheckInventory()
    {
        var shopSubSheet = Dalamud.GameData.GetSubrowExcelSheet<CollectablesShopItem>();
        var shopItemIds = shopSubSheet == null
            ? new HashSet<uint>()
            : shopSubSheet.SelectMany(s => s).Select(r => r.Item.RowId).ToHashSet();

        var fishSheet = Dalamud.GameData.GetExcelSheet<FishParameter>();
        var fishItemIds = fishSheet == null
            ? new HashSet<uint>()
            : fishSheet.Select(f => f.Item.RowId).ToHashSet();

        var collectables = ItemHelper.GetCurrentInventoryItems()
            .Where(i => i.IsCollectable && shopItemIds.Contains(i.BaseItemId))
            .GroupBy(i => i.BaseItemId)
            .ToList();
        
        _turnInQueue.Clear();
        
        foreach (var group in collectables)
        {
            var itemId = group.Key;
            var count = group.Count();
            
            var item = Dalamud.GameData.GetExcelSheet<Item>().GetRow(itemId);
            if (item.RowId == 0)
                continue;

            var isFish = fishItemIds.Contains(itemId);
            if (isFish && item.AetherialReduce > 0)
                continue;
                
            var itemName = item.Name.ToString();
            var jobId = ItemJobResolver.GetJobIdForItem(itemName, Dalamud.GameData);
            
            if (jobId != -1)
            {
                _turnInQueue.Enqueue((itemId, itemName, count, jobId));
            }
        }
        
        if (_turnInQueue.Count == 0)
        {
            GatherBuddy.Log.Information("[收藏品管理器] 没有可以交付的有效收藏品");
            _state = CollectableState.Completed;
            return;
        }
        
        GatherBuddy.Log.Information($"[收藏品管理器] 找到 {_turnInQueue.Count} 种可交付的收藏品");
        _teleportAttempted = false;
        _lifestreamAttempted = false;
        _state = CollectableState.MovingToShop;
    }
    
    private void MoveToShop()
    {
        if (Dalamud.Conditions[ConditionFlag.BetweenAreas] || Dalamud.Conditions[ConditionFlag.BetweenAreas51])
        {
            return;
        }
        
        var shop = _config.CollectableConfig.PreferredCollectableShop;
        var playerPos = Player.Position;
        
        if (playerPos == Vector3.Zero || shop.Location == Vector3.Zero)
        {
            return;
        }
        
        var distance = Vector3.Distance(playerPos, shop.Location);
        
        if (distance <= 2f && Dalamud.ClientState.TerritoryType == shop.TerritoryId)
        {
            _state = CollectableState.WaitingForShopWindow;
            return;
        }
        
        if (Dalamud.ClientState.TerritoryType != shop.TerritoryId && !_teleportAttempted)
        {
            GatherBuddy.Log.Information($"[收藏品管理器] 正传送到 {shop.Name}（以太之光 {shop.AetheryteId}）");
            if (Teleporter.Teleport(shop.AetheryteId))
            {
                _teleportAttempted = true;
                _lastAction = DateTime.UtcNow;
            }
            else
            {
                GatherBuddy.Log.Error($"[收藏品管理器] 传送到 {shop.Name} 失败");
                _state = CollectableState.Error;
            }
            return;
        }
        
        if (_teleportAttempted && Lifestream.Enabled && Lifestream.IsBusy())
        {
            return;
        }
        
        if (_teleportAttempted && (DateTime.UtcNow - _lastAction) < TimeSpan.FromSeconds(3))
        {
            return;
        }
        
        if (shop.IsLifestreamRequired && !_lifestreamAttempted && Lifestream.Enabled)
        {
            if (distance > 40f)
            {
                GatherBuddy.Log.Information($"[收藏品管理器] 使用 Lifestream: {shop.LifestreamCommand}");
                Lifestream.ExecuteCommand(shop.LifestreamCommand);
                _lifestreamAttempted = true;
                _lastAction = DateTime.UtcNow;
                return;
            }
        }
        
        if (_lifestreamAttempted && Lifestream.IsBusy())
        {
            return;
        }
        
        if (Dalamud.Conditions[ConditionFlag.BetweenAreas] || Dalamud.Conditions[ConditionFlag.BetweenAreas51])
        {
            return;
        }
        
        if (playerPos == Vector3.Zero)
        {
            return;
        }
        
        if (Lifestream.Enabled && Lifestream.IsBusy())
        {
            GatherBuddy.Log.Debug($"[收藏品管理器] Lifestream 正忙，等待中...");
            return;
        }
        
        if (Dalamud.ClientState.TerritoryType == shop.TerritoryId)
        {
            var timeSinceLastAction = (DateTime.UtcNow - _lastAction).TotalSeconds;
            if (timeSinceLastAction <= 5)
            {
                GatherBuddy.Log.Debug($"[收藏品管理器] 等待 {5 - timeSinceLastAction:F1} 秒后开始寻路...");
                return;
            }
            
            GatherBuddy.Log.Information($"[收藏品管理器] 开始导航到 {shop.Name}");
            try
            {
                VNavmesh.SimpleMove.PathfindAndMoveTo(shop.Location, false);
                _movementStartTime = DateTime.UtcNow;
                _state = CollectableState.WaitingForArrival;
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"[收藏品管理器] 导航错误: {ex.Message}");
            }
        }
    }
    
    private void WaitForArrival()
    {
        var shop = _config.CollectableConfig.PreferredCollectableShop;
        var playerPos = Player.Position;
        
        if (Dalamud.ClientState.TerritoryType != shop.TerritoryId)
        {
            _state = CollectableState.MovingToShop;
            return;
        }
        
        if (playerPos == Vector3.Zero)
        {
            return;
        }
        
        var distance = Vector3.Distance(playerPos, shop.Location);
        
        if (distance <= 2f)
        {
            VNavmesh.Path.Stop();
            _state = CollectableState.WaitingForShopWindow;
            return;
        }
        
        if ((DateTime.UtcNow - _movementStartTime) > TimeSpan.FromSeconds(60))
        {
            GatherBuddy.Log.Error($"[收藏品管理器] 导航超时，距离目标仍有 {distance:F1}m");
            VNavmesh.Path.Stop();
            _state = CollectableState.Error;
            return;
        }
        
        if ((DateTime.UtcNow - _lastAction) > TimeSpan.FromMilliseconds(500))
        {
            try
            {
                if (!VNavmesh.Path.IsRunning())
                {
                    VNavmesh.SimpleMove.PathfindAndMoveTo(shop.Location, false);
                }
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"[收藏品管理器] 导航错误: {ex.Message}");
            }
            _lastAction = DateTime.UtcNow;
        }
    }
    
    private unsafe void WaitForShopWindow()
    {
        if (!_windowHandler.IsReady)
        {
            if (_taskManager.IsBusy)
                return;
                
            var shop = _config.CollectableConfig.PreferredCollectableShop;
            var gameObj = Dalamud.Objects.FirstOrDefault(a => a.BaseId == shop.NpcId);
            
            if (gameObj == null)
            {
                return;
            }
            
            EnqueueNpcInteraction(gameObj);
            return;
        }
        _state = CollectableState.SelectingJob;
        _lastAction = DateTime.MinValue;
    }
    
    private unsafe void EnqueueNpcInteraction(global::Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject)
    {
        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
            return;
            
        _taskManager.Enqueue(() =>
        {
            targetSystem->OpenObjectInteraction((GameObject*)gameObject.Address);
        });
        _taskManager.Enqueue(() => _condition[ConditionFlag.OccupiedInQuestEvent] || _windowHandler.IsReady, 500);
        
        _taskManager.Enqueue(() =>
        {
            if (!_condition[ConditionFlag.OccupiedInQuestEvent] && !_windowHandler.IsReady)
            {
                targetSystem->OpenObjectInteraction((GameObject*)gameObject.Address);
            }
        });
        _taskManager.Enqueue(() => _condition[ConditionFlag.OccupiedInQuestEvent] || _windowHandler.IsReady, 500);
    }
    
    private void SelectJob()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        if (_turnInQueue.Count == 0)
        {
            _state = CollectableState.Completed;
            return;
        }
        
        var next = _turnInQueue.Peek();
        
        if (_currentJobId != next.jobId)
        {
            _windowHandler.SelectJob((uint)next.jobId);
            _currentJobId = next.jobId;
            _lastAction = DateTime.UtcNow;
            _phaseCounter = 0;
        }
        
        _state = CollectableState.SelectingItem;
    }
    
    private void SelectItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        var next = _turnInQueue.Peek();
        
        if (_currentItemId != next.itemId)
        {
            _windowHandler.SelectItemById(next.itemId);
            _currentItemId = next.itemId;
            _currentItemName = next.name;
            _lastAction = DateTime.UtcNow;
            return;
        }
        
        _state = CollectableState.SubmittingItem;
    }
    
    private void SubmitItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        _windowHandler.SubmitItem();
        _lastAction = DateTime.UtcNow;
        _overcapCheckStartTime = DateTime.UtcNow;
        _state = CollectableState.CheckingOvercapDialog;
    }
    
    private unsafe void CheckOvercapDialog()
    {
        if (Automation.GenericHelpers.TryGetAddonByName<FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno>("SelectYesno", out var addon) &&
            Automation.GenericHelpers.IsAddonReady((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon))
        {
            GatherBuddy.Log.Information("[收藏品管理器] 检测到工票溢出，拒绝交付并前往工票商店");
            Automation.Callback.Fire((FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)addon, true, 1);
            _lastAction = DateTime.UtcNow;
            _overcapInterrupted = true;
            
            _windowHandler.CloseWindow();
            
            if (_config.CollectableConfig.BuyAfterEachCollect && _config.CollectableConfig.ScripShopItems.Count > 0)
            {
                GatherBuddy.Log.Information("[收藏品管理器] 前往工票商店以消费工票");
                PreparePurchaseQueue();
                
                if (_purchaseQueue.Count == 0)
                {
                    GatherBuddy.Log.Error("[收藏品管理器] 检测到工票溢出但没有可购买的物品。正在禁用自动交付");
                    _config.CollectableConfig.AutoTurnInCollectables = false;
                    Communicator.PrintError("��⵽��Ʊ���, ��û����Ҫ����Ĺ�Ʊ�̵���Ʒ����ͣ���Զ������ղ�Ʒ, �����ù�Ʊ�̵깺���б����ֶ����ѹ�Ʊ��");
                    _state = CollectableState.Completed;
                    return;
                }
                
                _state = CollectableState.MovingToScripShop;
            }
            else
            {
                GatherBuddy.Log.Error("[收藏品管理器] 工票溢出但没有工票商店配置。正在禁用自动交付");
                _config.CollectableConfig.AutoTurnInCollectables = false;
                Communicator.PrintError("��⵽��Ʊ���, ���Զ���Ʊ���﹦����δ���á���ͣ���Զ������ղ�Ʒ, ������\"���׺��Զ�����Ʊ�̵���Ʒ\"ѡ��, ���ڹ�Ʊ�̵깺���б�������Ҫ�������Ʒ��");
                _state = CollectableState.Completed;
            }
            return;
        }
        
        if ((DateTime.UtcNow - _overcapCheckStartTime) > TimeSpan.FromMilliseconds(500))
        {
            _state = CollectableState.WaitingForSubmit;
        }
    }
    
    private void WaitForSubmit()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        var current = _turnInQueue.Peek();
        var newCount = current.count - 1;
        
        _turnInQueue.Dequeue();
        
        if (newCount > 0)
        {
            _turnInQueue = new Queue<(uint itemId, string name, int count, int jobId)>(
                new[] { (current.itemId, current.name, newCount, current.jobId) }.Concat(_turnInQueue)
            );
        }
        else
        {
            _currentItemId = 0;
            _currentItemName = null;
        }
        
        _state = CollectableState.CheckingForMore;
        _lastAction = DateTime.UtcNow;
    }
    
    private void CheckForMore()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        if (_turnInQueue.Count > 0)
        {
            _state = CollectableState.SelectingJob;
        }
        else
        {
            if (_config.CollectableConfig.BuyAfterEachCollect && _config.CollectableConfig.ScripShopItems.Count > 0)
            {
                GatherBuddy.Log.Information("[收藏品管理器] 交付完成，前往工票商店购买");
                _windowHandler.CloseWindow();
                PreparePurchaseQueue();
                _state = CollectableState.MovingToScripShop;
            }
            else
            {
                _state = CollectableState.Completed;
            }
        }
    }
    
    private void Complete()
    {
        GatherBuddy.Log.Information("[收藏品管理器] 已完成所有交付");
        _windowHandler.CloseWindow();
        _framework.Update -= OnUpdate;
        IsRunning = false;
        _state = CollectableState.Idle;
        OnFinishCollecting?.Invoke();
    }
    
    private void HandleError()
    {
        GatherBuddy.Log.Error("[收藏品管理器] 达到错误状态");
        OnError?.Invoke("收藏品交付过程中发生错误");
        Stop();
    }
    
    private bool HasCollectables()
    {
        var items = ItemHelper.GetCurrentInventoryItems();
        return items.Any(i => i.IsCollectable);
    }
    
    private void PreparePurchaseQueue()
    {
        _purchaseQueue.Clear();
        
        var inventoryItems = ItemHelper.GetCurrentInventoryItems();
        
        foreach (var item in _config.CollectableConfig.ScripShopItems)
        {
            if (item.Item == null) continue;
            
            if (item.Item.Page < 3)
            {
                GatherBuddy.Log.Warning($"[收藏品管理器] 跳过工匠工票物品: {item.Name}（页={item.Item.Page}）");
                continue;
            }
            
            var currentCount = inventoryItems
                .Where(x => x.BaseItemId == item.Item.ItemId)
                .Sum(x => (int)x.Quantity);
            
            var remaining = item.Quantity - currentCount;
            
            if (remaining > 0)
            {
                GatherBuddy.Log.Debug($"[收藏品管理器] {item.Name}: 目标={item.Quantity}，当前={currentCount}，剩余={remaining}");
                _purchaseQueue.Enqueue((item.Item.ItemId, item.Name, remaining, (int)item.Item.ItemCost, item.Item.Page, item.Item.SubPage));
            }
        }
    }
    
    private void MoveToScripShop()
    {
        if (_purchaseQueue.Count == 0)
        {
            _state = CollectableState.Completed;
            return;
        }
        
        var shop = _config.CollectableConfig.PreferredCollectableShop;
        var playerPos = Player.Position;
        
        if (playerPos == Vector3.Zero || shop.ScripShopLocation == Vector3.Zero)
        {
            return;
        }
        
        var distance = Vector3.Distance(playerPos, shop.ScripShopLocation);
        
        if (distance <= 0.4f)
        {
            VNavmesh.Path.Stop();
            _state = CollectableState.WaitingForScripShopWindow;
            return;
        }
        
        if ((DateTime.UtcNow - _lastAction) > TimeSpan.FromMilliseconds(200))
        {
            try
            {
                if (!VNavmesh.Path.IsRunning())
                {
                    VNavmesh.SimpleMove.PathfindAndMoveTo(shop.ScripShopLocation, false);
                    _movementStartTime = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"[收藏品管理器] 导航错误: {ex.Message}");
            }
            
            _lastAction = DateTime.UtcNow;
            _state = CollectableState.WaitingForScripShopArrival;
        }
    }
    
    private void WaitForScripShopArrival()
    {
        var shop = _config.CollectableConfig.PreferredCollectableShop;
        var playerPos = Player.Position;
        
        if (playerPos == Vector3.Zero)
        {
            return;
        }
        
        var distance = Vector3.Distance(playerPos, shop.ScripShopLocation);
        
        if (distance <= 0.4f)
        {
            VNavmesh.Path.Stop();
            _state = CollectableState.WaitingForScripShopWindow;
            return;
        }
        
        if ((DateTime.UtcNow - _movementStartTime) > TimeSpan.FromSeconds(30))
        {
            GatherBuddy.Log.Error($"[收藏品管理器] 工票商店导航超时");
            _state = CollectableState.Error;
            return;
        }
        
        if ((DateTime.UtcNow - _lastAction) > TimeSpan.FromMilliseconds(200))
        {
            try
            {
                if (!VNavmesh.Path.IsRunning())
                {
                    VNavmesh.SimpleMove.PathfindAndMoveTo(shop.ScripShopLocation, false);
                }
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"[收藏品管理器] 导航错误: {ex.Message}");
            }
            _lastAction = DateTime.UtcNow;
        }
    }
    
    private unsafe void WaitForScripShopWindow()
    {
        if (!_scripShopWindowHandler.IsReady)
        {
            if (_taskManager.IsBusy)
                return;
            
            var shop = _config.CollectableConfig.PreferredCollectableShop;
            var gameObj = Dalamud.Objects.FirstOrDefault(a => a.BaseId == shop.ScripShopNpcId);
            
            if (gameObj == null)
            {
                GatherBuddy.Log.Debug($"[收藏品管理器] 在对象表中找不到工票 NPC {shop.ScripShopNpcId}");
                return;
            }
            
            GatherBuddy.Log.Debug($"[收藏品管理器] 找到工票 NPC: {gameObj.Name.TextValue}，位置 {gameObj.Position}");
            EnqueueScripShopNpcInteraction(gameObj);
            return;
        }
        _state = CollectableState.SelectingScripShopPage;
        _lastAction = DateTime.MinValue;
    }
    
    private unsafe void EnqueueScripShopNpcInteraction(global::Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject)
    {
        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
            return;
            
        _taskManager.Enqueue(() =>
        {
            VNavmesh.Path.Stop();
            targetSystem->Target = (GameObject*)gameObject.Address;
            targetSystem->OpenObjectInteraction((GameObject*)gameObject.Address);
        });
        _taskManager.DelayNext(1000);
        _taskManager.Enqueue(() =>
        {
            _scripShopWindowHandler.OpenShop();
        });
        _taskManager.DelayNext(2000);
        _taskManager.Enqueue(() => _scripShopWindowHandler.IsReady, 10000);
    }
    
    private void SelectScripShopPage()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        if (_purchaseQueue.Count == 0)
        {
            _state = CollectableState.Completed;
            return;
        }
        
        var next = _purchaseQueue.Peek();
        _scripShopWindowHandler.SelectPage(next.page);
        _lastAction = DateTime.UtcNow;
        _state = CollectableState.SelectingScripShopSubPage;
    }
    
    private void SelectScripShopSubPage()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        var next = _purchaseQueue.Peek();
        _scripShopWindowHandler.SelectSubPage(next.subPage);
        _lastAction = DateTime.UtcNow;
        _state = CollectableState.SelectingScripShopItem;
    }
    
    private void SelectScripShopItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        var next = _purchaseQueue.Peek();
        var scrips = _scripShopWindowHandler.GetScripCount();
        var reserveAmount = _config.CollectableConfig.ReserveScripAmount;
        var availableScrips = Math.Max(0, scrips - reserveAmount);
        var maxByScrip = next.cost > 0 ? (availableScrips / next.cost) : next.remaining;
        var amount = Math.Min(next.remaining, Math.Min(maxByScrip, 99));
        
        if (amount <= 0)
        {
            if (reserveAmount > 0 && scrips < reserveAmount + next.cost)
            {
                GatherBuddy.Log.Information($"[收藏品管理器] 跳过购买 {next.name} - 会超出工票储备（当前: {scrips}，储备: {reserveAmount}，费用: {next.cost}）");
            }
            _purchaseQueue.Dequeue();
            _state = CollectableState.CheckingForMorePurchases;
            return;
        }
        if (!_scripShopWindowHandler.SelectItem(next.itemId, amount))
        {
            GatherBuddy.Log.Error($"[收藏品管理器] 选择物品 {next.name} 失败");
            _purchaseQueue.Dequeue();
            _state = CollectableState.CheckingForMorePurchases;
            return;
        }
        
        _currentPurchaseAmount = amount;
        _lastAction = DateTime.UtcNow;
        _state = CollectableState.PurchasingScripShopItem;
    }
    
    private void PurchaseScripShopItem()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        _scripShopWindowHandler.PurchaseItem();
        _lastAction = DateTime.UtcNow;
        _state = CollectableState.WaitingForPurchaseComplete;
    }
    
    private void WaitForPurchaseComplete()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        var next = _purchaseQueue.Dequeue();
        var newRemaining = next.remaining - _currentPurchaseAmount;
        
        GatherBuddy.Log.Debug($"[收藏品管理器] 已购买 {_currentPurchaseAmount}x {next.name}，剩余: {newRemaining}");
        
        if (newRemaining > 0)
        {
            _purchaseQueue.Enqueue((next.itemId, next.name, newRemaining, next.cost, next.page, next.subPage));
        }
        
        _currentPurchaseAmount = 0;
        _lastAction = DateTime.UtcNow;
        _state = CollectableState.CheckingForMorePurchases;
    }
    
    private void CheckForMorePurchases()
    {
        if ((DateTime.UtcNow - _lastAction) < _actionDelay) return;
        
        if (_purchaseQueue.Count > 0)
        {
            _state = CollectableState.SelectingScripShopPage;
        }
        else
        {
            GatherBuddy.Log.Information("[收藏品管理器] 已完成所有购买");
            _scripShopWindowHandler.CloseShop();
            
            if (_overcapInterrupted && HasCollectables())
            {
                GatherBuddy.Log.Information("[收藏品管理器] 工票购买后仍有收藏品，恢复交付");
                _overcapInterrupted = false;
                _teleportAttempted = false;
                _lifestreamAttempted = false;
                _lastAction = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                _state = CollectableState.CheckingInventory;
            }
            else
            {
                _state = CollectableState.Completed;
            }
        }
    }
    
    public void Dispose()
    {
        Stop();
    }
}
