using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using GatherBuddy.AutoGather.Extensions;
using GatherBuddy.AutoGather.Helpers;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.AutoGather.Movement;
using GatherBuddy.Automation;
using GatherBuddy.Classes;
using GatherBuddy.Crafting;
using GatherBuddy.CustomInfo;
using GatherBuddy.Data;
using GatherBuddy.Enums;
using GatherBuddy.Helpers;
using GatherBuddy.Plugin;
using GatherBuddy.SeFunctions;
using GatherBuddy.Time;
using GatherBuddy.Utilities;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Fish = GatherBuddy.Classes.Fish;
using GatheringType = GatherBuddy.Enums.GatheringType;
using NodeType = GatherBuddy.Enums.NodeType;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace GatherBuddy.AutoGather
{
    public partial class AutoGather : IDisposable
    {
        public AutoGather(GatherBuddy plugin)
        {
            // Initialize the task manager
            TaskManager = new(Dalamud.Framework)
            {
                ShowDebug = false
            };
            _plugin = plugin;
            _soundHelper = new SoundHelper();
            _advancedUnstuck = new();
            _activeItemList = new ActiveItemList(plugin.AutoGatherListsManager, this);
            _diadem = new Diadem();
            ArtisanExporter = new Reflection.ArtisanExporter(plugin.AutoGatherListsManager);
            Dalamud.Chat.CheckMessageHandled += OnMessageHandled;
            Dalamud.ToastGui.QuestToast += OnQuestToast;
            //Dalamud.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Gathering", OnGatheringFinalize);
            _plugin.FishRecorder.Parser.CaughtFish += OnFishCaught;
        }
        public Fish? LastCaughtFish { get; private set; }
        public Fish? PreviouslyCaughtFish { get; private set; }
        private void OnFishCaught(Fish arg1, ushort arg2, byte arg3, bool arg4, bool arg5)
        {
            PreviouslyCaughtFish = LastCaughtFish;
            LastCaughtFish = arg1;

            if (_consecutiveAmissCount > 0)
            {
                GatherBuddy.Log.Information($"[AutoGather] 成功钓上鱼, 脱钩计数已从 {_consecutiveAmissCount} 重置为 0");
                _consecutiveAmissCount = 0;
            }

            if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
            {
                if (_currentAutoHookTarget?.Fish?.ItemId == arg1.ItemId)
                {
                    var targetQuantity = _currentAutoHookTarget.Value.Quantity;
                    var currentCount = arg1.GetInventoryCount();

                    if (currentCount >= targetQuantity)
                    {
                        GatherBuddy.Log.Information($"[AutoGather] 已达到目标鱼数量 ({currentCount}/{targetQuantity}), 立即停止钓鱼");
                        AutoHook.SetPluginState?.Invoke(false);
                        AutoHook.SetAutoStartFishing?.Invoke(false);

                        TaskManager.Enqueue(() =>
                        {
                            if (IsFishing)
                            {
                                CleanupAutoHook();
                                QueueQuitFishingTasks();
                                _activeItemList.ForceRefresh();
                            }
                            return true;
                        });
                    }
                }
            }
        }

        // Track the current gather target for robust node handling
        private GatherTarget? _currentGatherTarget;
        private bool _waitingForFishingToFinishAfterTargetChange = false;
        private volatile bool _fishDetectedPlayer = false;
        private volatile bool _fishWaryDetected = false;
        private volatile bool _processingFishingToast = false;
        private int _consecutiveAmissCount = 0;
        private DateTime _stuckAtSpotStartTime = DateTime.MinValue;
        private DateTime _lastJiggleTime = DateTime.MinValue;
        private readonly Dictionary<GatherTarget, int> _jiggleAttempts = [];
        private readonly Dictionary<GatherTarget, DateTime> _fishingSpotArrivalTime = [];
        private GatherTarget? _fishingApproachTarget;
        private Vector3 _fishingApproachPosition;
        private readonly List<Vector3> _failedFishingApproachPositions = [];
        private DateTime _fishingApproachLastProgressTime = DateTime.MinValue;
        private DateTime _fishingApproachLastCheckTime = DateTime.MinValue;
        private float _fishingApproachBestDistance = float.MaxValue;
        private int _fishingApproachRetryCount;
        private bool _fishingApproachPathObserved;
        private bool _fishingApproachRecoveryPending;
        private const float MaxFishingAlternateDistance = 25f;
        private const uint FishWaryMessageId = 5517;
        private const uint FishAmissMessageId = 3516;
        private Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.LogMessage>? _cachedLogMessages;

        private void OnQuestToast(ref SeString message, ref QuestToastOptions options, ref bool isHandled)
        {
            try
            {
                var text = message.TextValue;

                if (string.IsNullOrEmpty(text) || text.Length < 10)
                {
                    return;
                }

                _cachedLogMessages ??= Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.LogMessage>();
                if (_cachedLogMessages == null)
                {
                    return;
                }

                var logMsg = default(LogMessage);
                foreach (var row in _cachedLogMessages)
                {
                    if (row.Text.ExtractText() == text)
                    {
                        logMsg = row;
                        break;
                    }
                }

                if (logMsg.RowId != 0)
                {
                    if (logMsg.RowId == FishWaryMessageId)
                    {
                        if (_processingFishingToast)
                        {
                            GatherBuddy.Log.Debug($"[AutoGather] 忽略重复的警惕提示, 正在处理前一个");
                            return;
                        }

                        GatherBuddy.Log.Warning($"[AutoGather] 鱼类警惕警告 (ID: {logMsg.RowId}): '{text}' - 简单重新定位");
                        _fishWaryDetected = true;
                    }
                    else if (logMsg.RowId == FishAmissMessageId)
                    {
                        if (_processingFishingToast)
                        {
                            GatherBuddy.Log.Debug($"[AutoGather] 忽略重复的脱钩提示, 正在处理前一个");
                            return;
                        }

                        _consecutiveAmissCount++;
                        GatherBuddy.Log.Warning($"[AutoGather] 检测到鱼类脱钩 (ID: {logMsg.RowId}, 次数: {_consecutiveAmissCount}): '{text}'");
                        _fishDetectedPlayer = true;
                    }
                }
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"[AutoGather] 处理任务提示失败: {e}");
            }
        }

        private void OnMessageHandled(IHandleableChatMessage chatMessage)
        {
            try
            {
                if (chatMessage.LogKind is (XivChatType)2243)
                {
                    var text = chatMessage.Message.TextValue;
                    var id = 0u;
                    foreach (var row in Dalamud.GameData.GetExcelSheet<LogMessage>())
                    {
                        if (row.Text.ToString() == text)
                        {
                            id = row.RowId;
                            break;
                        }
                    }

                    var lureFish = false;
                    foreach (var fish in GatherBuddy.GameData.Fishes.Values)
                    {
                        if (fish.FishData?.Unknown_70_1 == text)
                        {
                            lureFish = true;
                            break;
                        }
                    }

                    LureSuccess = lureFish;

                    if (LureSuccess)
                    {
                        return;
                    }

                    LureSuccess = id is 5565 or 5569;
                }
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"处理消息失败: {e}");
            }
        }

        private void ResetPendingFishingTargetChange()
            => _waitingForFishingToFinishAfterTargetChange = false;

        private bool FishingApproachOwnsRecovery
            => _fishingApproachTarget.HasValue;

        private void ResetFishingApproachState()
        {
            if (_fishingApproachRecoveryPending)
            {
                _advancedUnstuck.Cancel();
            }

            _fishingApproachTarget = null;
            _fishingApproachPosition = default;
            _failedFishingApproachPositions.Clear();
            _fishingApproachLastProgressTime = DateTime.MinValue;
            _fishingApproachLastCheckTime = DateTime.MinValue;
            _fishingApproachBestDistance = float.MaxValue;
            _fishingApproachRetryCount = 0;
            _fishingApproachPathObserved = false;
            _fishingApproachRecoveryPending = false;
            _fishingSpotDismountAttempts.Clear();
        }

        private void EnsureFishingApproachTarget(GatherTarget target)
        {
            if (_fishingApproachTarget is { } current
             && current.Item.ItemId == target.Item.ItemId
             && current.FishingSpot?.Id == target.FishingSpot?.Id)
            {
                return;
            }

            ResetFishingApproachState();
            _fishingApproachTarget = target;
        }

        private void SetFishingApproachPosition(GatherTarget target, Vector3 position)
        {
            EnsureFishingApproachTarget(target);
            _fishingApproachPosition = position;
            _fishingApproachBestDistance = position.DistanceToPlayer();
            _fishingApproachLastProgressTime = DateTime.Now;
            _fishingApproachLastCheckTime = DateTime.MinValue;
            _fishingApproachRetryCount = 0;
            _fishingApproachPathObserved = false;
        }

        private void FailFishingApproach(GatherTarget target, string reason, string failureStatus, bool retrySamePosition, bool runUnstuck)
        {
            var position = _fishingApproachPosition != default
                ? _fishingApproachPosition
                : FishingSpotData.GetValueOrDefault(target).Position;
            var routeState = $"destination={CurrentDestination}, pathing={IsPathing}, generating={IsPathGenerating}";

            if (retrySamePosition && _fishingApproachRetryCount == 0)
            {
                _fishingApproachRetryCount = 1;
                GatherBuddy.Log.Warning(
                    $"[AutoGather] Fishing approach stalled for {target.Fish!.Name[GatherBuddy.Language]} at {target.FishingSpot!.Name}: {reason}; retrying position={position}; {routeState}");
                StopNavigation();
                _fishingSpotArrivalTime.Remove(target);
                _fishingApproachBestDistance = position.DistanceToPlayer();
                _fishingApproachLastProgressTime = DateTime.Now;
                _fishingApproachLastCheckTime = DateTime.MinValue;
                _fishingApproachPathObserved = false;
                AutoStatus = "钓鱼寻路停滞，正在重试当前钓点...";
                return;
            }

            if (position != default && _failedFishingApproachPositions.TrueForAll(p => Vector3.DistanceSquared(p, position) >= 1f))
            {
                _failedFishingApproachPositions.Add(position);
            }

            GatherBuddy.Log.Warning(
                $"[AutoGather] Fishing approach failed for {target.Fish!.Name[GatherBuddy.Language]} at {target.FishingSpot!.Name}: {reason}; position={position}; {routeState}");

            StopNavigation();
            FishingSpotData.Remove(target);
            _fishingSpotDismountAttempts.Remove(position);
            _fishingSpotArrivalTime.Remove(target);
            _jiggleAttempts.Remove(target);
            _fishingApproachPosition = default;
            _fishingApproachBestDistance = float.MaxValue;
            _fishingApproachLastProgressTime = DateTime.MinValue;
            _fishingApproachLastCheckTime = DateTime.MinValue;
            _fishingApproachPathObserved = false;

            if (runUnstuck)
            {
                _advancedUnstuck.ForceFishing();
                if (!_advancedUnstuck.IsRunning)
                {
                    AbortAutoGather($"无法恢复前往钓场 {target.FishingSpot!.Name} 的路线");
                    return;
                }

                _fishingApproachRecoveryPending = true;
            }

            AutoStatus = failureStatus;
        }

        private bool CheckFishingApproachProgress(GatherTarget target, Vector3 destination, bool isPathing, bool isPathGenerating)
        {
            var distance = destination.DistanceToPlayer();
            if (distance < 1f)
            {
                return true;
            }

            var now = DateTime.Now;
            if (_fishingApproachLastCheckTime != DateTime.MinValue
             && (now - _fishingApproachLastCheckTime).TotalSeconds > 1)
            {
                _fishingApproachBestDistance = System.Math.Min(_fishingApproachBestDistance, distance);
                _fishingApproachLastProgressTime = now;
            }
            _fishingApproachLastCheckTime = now;

            if (!NavReady
             || isPathGenerating
             || TaskManager.IsBusy
             || _advancedUnstuck.IsRunning
             || Dalamud.Conditions[ConditionFlag.BetweenAreas]
             || Dalamud.Conditions[ConditionFlag.BetweenAreas51]
             || Dalamud.Conditions[ConditionFlag.Mounting]
             || Dalamud.Conditions[ConditionFlag.Mounting71]
             || Dalamud.Conditions[ConditionFlag.MountOrOrnamentTransition])
            {
                _fishingApproachBestDistance = System.Math.Min(_fishingApproachBestDistance, distance);
                _fishingApproachLastProgressTime = now;
                return true;
            }

            if (CurrentDestination != destination)
            {
                return true;
            }

            if (isPathing)
            {
                if (!_fishingApproachPathObserved)
                {
                    _fishingApproachPathObserved = true;
                    _fishingApproachBestDistance = distance;
                    _fishingApproachLastProgressTime = now;
                    return true;
                }

                if (_fishingApproachBestDistance - distance >= 1f)
                {
                    _fishingApproachBestDistance = distance;
                    _fishingApproachLastProgressTime = now;
                    return true;
                }

                if ((now - _fishingApproachLastProgressTime).TotalSeconds > GatherBuddy.Config.AutoGatherConfig.NavResetThreshold)
                {
                    FailFishingApproach(target, $"no destination progress for {(now - _fishingApproachLastProgressTime).TotalSeconds:F1}s", "Fishing route stalled twice, finding a nearby spot...", true, false);
                    return false;
                }

                return true;
            }

            if (!_fishingApproachPathObserved)
            {
                return true;
            }

            FailFishingApproach(target, $"path stopped {distance:F1}y from destination", "Fishing route stopped short twice, finding a nearby spot...", true, false);
            return false;
        }

        private readonly GatherBuddy _plugin;
        private readonly SoundHelper _soundHelper;
        private readonly AdvancedUnstuck _advancedUnstuck;
        private readonly ActiveItemList _activeItemList;
        private readonly Diadem _diadem;

        public Reflection.ArtisanExporter ArtisanExporter;
        public TaskManager TaskManager { get; }

        private bool _enabled { get; set; } = false;

        public bool Waiting
        {
            get;
            private set
            {
                field = value;
            }
        } = false;

        public unsafe bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                {
                    return;
                }

                if (!value)
                {
                    AutoStatus = "空闲中...";
                    TaskManager.Abort();
                    YesAlready.Unlock();

                    _activeItemList.Reset();
                    _currentRequestedTarget = null;
                    _currentGatherTarget = null;
                    Waiting = false;
                    ActionSequence = null;
                    CurrentCollectableRotation = null;

                    CleanupAutoHook();

                    StopNavigation();
                    _homeWorldWarning = false;
                    _diademQueuingInProgress = false;
                    FarNodesSeenSoFar.Clear();
                    VisitedNodes.Clear();
                    _lastAetherTarget = DateTime.MinValue;
                    _diademPathIndex = -1;
                    _fishDetectedPlayer = false;
                    _fishWaryDetected = false;
                    _processingFishingToast = false;
                    _consecutiveAmissCount = 0;
                    _stuckAtSpotStartTime = DateTime.MinValue;
                    _lastJiggleTime = DateTime.MinValue;
                    _jiggleAttempts.Clear();
                    _fishingSpotArrivalTime.Clear();
                    ResetFishingApproachState();
                    ResetPendingFishingTargetChange();
                    Dalamud.ToastGui.ErrorToast -= HandleNodeInteractionErrorToast;

                    M3Gamepad.EnableInputBlocking();

                    if (_autoRetainerMultiModeEnabled && AutoRetainer.IsEnabled)
                    {
                        try
                        {
                            AutoRetainer.DisableAllFunctions?.Invoke();
                            _autoRetainerMultiModeEnabled = false;
                            _originalCharacterNameWorld = null;
                            GatherBuddy.Log.Debug("禁用 AutoGather 时关闭了 AutoRetainer 多模式");
                        }
                        catch (Exception e)
                        {
                            GatherBuddy.Log.Error($"禁用 AutoRetainer 多模式失败: {e.Message}");
                        }
                    }

                    if (GatherBuddy.CollectableManager?.IsRunning == true)
                    {
                        GatherBuddy.Log.Debug("[AutoGather] 正在停止收藏品交付 (用户禁用了自动采集)");
                        GatherBuddy.CollectableManager?.Stop();
                    }

                    if (Crafting.CraftingGatherBridge.GetTemporaryGatherList() != null && !Crafting.CraftingGatherBridge.PreserveListOnDisable)
                    {
                        GatherBuddy.Log.Debug("[AutoGather] 正在清理临时 Vulcan 采集列表 (用户禁用了自动采集)");
                        Crafting.CraftingGatherBridge.DeleteTemporaryGatherList();
                    }
                }
                else
                {
                    if (!ValidateActiveItemsPerception())
                    {
                        return;
                    }

                    WentHome = true; //Prevents going home right after enabling auto-gather
                    if (AutoHook.Enabled)
                    {
                        AutoHook.SetPluginState(false);
                        AutoHook.SetAutoStartFishing?.Invoke(false);
                    }
                    YesAlready.Lock();
                    DisableQuickGathering();

                    M3Gamepad.EnableInputBlocking(movement: false, camera: false);
                }

                _enabled = value;
                _plugin.Ipc.AutoGatherEnabledChanged(value);
            }
        }

        public bool GoHome()
        {
            StopNavigation();

            if (WentHome)
            {
                return false;
            }

            WentHome = true;

            if (Dalamud.Conditions[ConditionFlag.BoundByDuty])
            {
                return false;
            }

            if (Lifestream.Enabled && !Lifestream.IsBusy())
            {
                var command = GatherBuddy.Config.AutoGatherConfig.LifestreamCommand;
                if (command.Contains("/li "))
                {
                    command = command.Replace("/li ", "");
                }

                Lifestream.ExecuteCommand(command);
                TaskManager.EnqueueImmediate(() => !Lifestream.IsBusy(), 120000, "等待 Lifestream 传送完成");
                return true;
            }
            else
            {
                GatherBuddy.Log.Warning("未安装或启用 Lifestream");
                return false;
            }
        }

        private unsafe void DisableQuickGathering()
        {
            try
            {
                var raptureAtkModule = RaptureAtkModule.Instance();
                if (raptureAtkModule == null)
                {
                    return;
                }

                raptureAtkModule->QuickGatheringEnabled = false;
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"禁用快速采集失败: {e.Message}");
            }
        }

        private class NoGatherableItemsInNodeException : Exception
        { }

        private class NoCollectableActionsException : Exception
        { }

        private bool _diademQueuingInProgress = false;
        private bool _homeWorldWarning = false;
        private bool _autoRetainerMultiModeEnabled = false;
        private string? _originalCharacterNameWorld = null;
        private bool _autoRetainerWasEnabledBeforeDiadem = false;

        public void DoAutoGather()
        {
            var currentTerritory = Dalamud.ClientState.TerritoryType;
            if (_lastTerritory != currentTerritory)
            {
                ResetFishingApproachState();
                _lastTerritory = currentTerritory;
                _diademPathIndex = -1;

                var isInDiademOrFirmament = currentTerritory == Diadem.Territory.Id;
                var wasInDiademOrFirmament = _lastTerritory == Diadem.Territory.Id;

                if (isInDiademOrFirmament && !wasInDiademOrFirmament)
                {
                    _autoRetainerWasEnabledBeforeDiadem = GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode;
                    if (_autoRetainerWasEnabledBeforeDiadem)
                    {
                        GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode = false;
                        GatherBuddy.Log.Information("在云冠群岛/天穹街时临时禁用了 AutoRetainer 集成");
                    }
                }
                else if (!isInDiademOrFirmament && wasInDiademOrFirmament)
                {
                    if (_autoRetainerWasEnabledBeforeDiadem)
                    {
                        GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode = true;
                        GatherBuddy.Log.Information("离开云冠群岛/天穹街后重新启用了 AutoRetainer 集成");
                        _autoRetainerWasEnabledBeforeDiadem = false;
                    }
                }
            }

            // Reset the flag before checking Enabled to get correct state even if auto-gather is disabled.
            // Integrity == 0 is checked to ensure we can use Luck if Revisit triggers.
            if (LuckUsed && (!IsGathering || (GatheringWindowReader?.IntegrityRemaining ?? 0) == 0))
            {
                LuckUsed = false;
            }

            if (!Enabled)
            {
                return;
            }

            // If we are not gathering and _currentGatherTarget is set, we just finished gathering or left the node
            if (!IsGathering && _currentGatherTarget != null)
            {
                var gatherTarget = _currentGatherTarget.Value;
                // Mark the node as visited if possible
                var targetNode = Dalamud.Targets.Target ?? Dalamud.Targets.PreviousTarget;
                if (targetNode != null && targetNode.ObjectKind is ObjectKind.GatheringPoint)
                {
                    _activeItemList.MarkVisited(targetNode);
                    var gatherable = gatherTarget.Gatherable;
                    var node = gatherTarget.Node;
                    var fishingSpot = gatherTarget.FishingSpot;

                    if (gatherable != null && (gatherable.NodeType == NodeType.Regular || gatherable.NodeType == NodeType.Ephemeral)
                        && LastVisitedNode != targetNode.BaseId
                        && node != null && node.WorldPositions.ContainsKey(targetNode.BaseId))
                    {
                        FarNodesSeenSoFar.Clear();

                        while (VisitedNodes.Count > (node.WorldPositions.Count <= 4 ? 2 : 4) - 1)
                        {
                            VisitedNodes.RemoveAt(0);
                        }

                        if (node.WorldPositions.Count > 2)
                        {
                            VisitedNodes.Add(targetNode.BaseId);
                        }
                    }
                    else if (gatherTarget.Fish?.IsSpearFish == true && fishingSpot != null
                        && LastVisitedNode != targetNode.BaseId
                        && fishingSpot.WorldPositions.ContainsKey(targetNode.BaseId))
                    {
                        FarNodesSeenSoFar.Clear();

                        while (VisitedNodes.Count > (fishingSpot.WorldPositions.Count <= 4 ? 2 : 4) - 1)
                        {
                            VisitedNodes.RemoveAt(0);
                        }

                        if (fishingSpot.WorldPositions.Count > 2)
                        {
                            VisitedNodes.Add(targetNode.BaseId);
                        }
                    }
                }
                _plugin.AutoGatherListsManager.RemoveCompletedItemsFromLists();
                // Unset the current gather target when leaving the node
                _currentGatherTarget = null;
                ResetPendingFishingTargetChange();
            }


            //try
            //{
            //    if (!NavReady)
            //    {
            //        AutoStatus = "Waiting for Navmesh...";
            //        return;
            //    }
            //}
            //catch (Exception)
            //{
            //    //GatherBuddy.Log.Error(e.Message);
            //    AutoStatus = "vnavmesh communication failed. Do you have it installed??";
            //    return;
            //}

            if (HandleFishingCollectable())
            {
                return;
            }

            HandlePathfinding(); // This should be done before checking TaskManager

            if (Dalamud.Conditions[ConditionFlag.Jumping61] && IsPathing) // Jumping Windmire
            {
                StopNavigation();
            }

            if (TaskManager.IsBusy)
            {
                //GatherBuddy.Log.Verbose("TaskManager has tasks, skipping DoAutoGather");
                return;
            }

            if (!_homeWorldWarning && !Functions.OnHomeWorld())
            {
                _homeWorldWarning = true;
                Communicator.PrintError("当前不在原始服务器, 部分物品无法采集");
            }

            if (DiscipleOfLand.NextTreasureMapAllowance == DateTime.MinValue)
            {
                //Wait for timer refresh
                AutoStatus = "刷新采集时钟中...";
                DiscipleOfLand.RefreshNextTreasureMapAllowance();
                return;
            }

            if (!CanAct && !_diademQueuingInProgress)
            {
                if (!Dalamud.Conditions[ConditionFlag.ExecutingGatheringAction])
                {
                    AutoStatus = "当前无法行动";
                }

                return;
            }


            if (FreeInventorySlots == 0)
            {
                if (GatherBuddy.CollectableManager?.IsRunning == true)
                {
                    AutoStatus = "正在交易收藏品...";
                    return;
                }

                if (HasReducibleItems())
                {
                    if (Player.Job == 18 /* FSH */ && GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
                    {
                        return;
                    }
                    else if (IsGathering)
                    {
                        CloseGatheringAddons();
                    }
                    else
                    {
                        ReduceItems(false);
                    }
                }
                else if (HasCollectables())
                {
                    GatherBuddy.Log.Information("[AutoGather] 背包已满收藏品 - 开始交付");
                    AutoStatus = "正在交易收藏品...";
                    if (IsGathering)
                    {
                        CloseGatheringAddons();
                    }
                    else
                    {
                        GatherBuddy.CollectableManager?.Start(Collectables.CollectableRunSource.AutoGather);
                    }
                }
                else
                {
                    AbortAutoGather("背包物品已满");
                }

                return;
            }

            if (Player.Job == 18 /* FSH */ && _currentGatherTarget != null && IsFishing)
            {
                var fish = _currentGatherTarget.GetValueOrDefault();
                if (FishingSpotData.TryGetValue(fish, out var fishingSpotData))
                {
                    if (GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes > 0 && fishingSpotData.Expiration < DateTime.Now)
                    {
                        GatherBuddy.Log.Information($"[AutoGather] 钓场计时器在主循环中过期 ({GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes} 分钟), 正在重新定位...");

                        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                        {
                            AutoHook.SetPluginState?.Invoke(false);
                            AutoHook.SetAutoStartFishing?.Invoke(false);
                        }
                        QueueQuitFishingTasks();
                        return;
                    }
                }
            }

            if (IsGathering)
            {
                // Set the current gather target when entering a node
                if (_currentGatherTarget == null)
                {
                    _currentGatherTarget = _activeItemList.CurrentOrDefault;
                }
                _currentRequestedTarget = _currentGatherTarget.Value.Item;

                if (!GatherBuddy.Config.AutoGatherConfig.DoGathering)
                {
                    return;
                }

                StopNavigation();

                if (Player.Job == 18 /* FSH */)
                {
                    AutoStatus = "钓鱼中...";
                    var fish = _currentGatherTarget.GetValueOrDefault();
                    var isSpearfishing = Dalamud.Targets.Target?.ObjectKind == ObjectKind.GatheringPoint;

                    if (isSpearfishing && fish.Fish != null)
                    {
                        _wasGatheringSpearfish = true;
                        var currentFishId = fish.Fish.ItemId;
                        var targetFishId = _currentAutoHookTarget?.Fish?.ItemId ?? 0;
                        var now = DateTime.Now;

                        if (!_currentAutoHookTarget.HasValue
                         || targetFishId != currentFishId
                         || _currentAutoHookTarget.Value.FishingSpot?.Id != fish.FishingSpot?.Id)
                        {
                            SetupAutoHookForFishing(fish);
                            _lastAutoHookSetupTime = now;
                            _autoHookSetupComplete = false;
                        }
                        else if (!_autoHookSetupComplete && (now - _lastAutoHookSetupTime).TotalSeconds >= 1.0)
                        {
                            SetupAutoHookForFishing(fish);
                            _lastAutoHookSetupTime = now;
                        }
                        return;
                    }

                    var nextTarget = _activeItemList.GetNextOrDefault();
                    if (!isSpearfishing && (nextTarget == default || nextTarget.Item != _currentGatherTarget?.Item))
                    {
                        if (IsFishing && AutoHook.Enabled)
                        {
                            AutoStatus = "正在等待当前抛竿完成, 再切换目标...";
                            if (!_waitingForFishingToFinishAfterTargetChange)
                            {
                                _waitingForFishingToFinishAfterTargetChange = true;
                                var nextTargetName = nextTarget == default ? "无活跃目标" : nextTarget.Item.Name[GatherBuddy.Language];
                                GatherBuddy.Log.Debug($"[AutoGather] 当前钓鱼目标 {fish.Item.Name[GatherBuddy.Language]} 已不可用, 等待抛竿完成后切换到 {nextTargetName}");
                                AutoHook.SetAutoStartFishing(false);
                            }
                        }
                        else
                        {
                            ResetPendingFishingTargetChange();
                            CleanupAutoHook();
                            QueueQuitFishingTasks();
                        }
                        return;
                    }
                    ResetPendingFishingTargetChange();

                    if (GatherBuddy.Config.AutoGatherConfig.UseNavigation)
                    {
                        DoFishMovement(fish);
                    }
                    DoFishingTasks(fish);
                    return;
                }

                AutoStatus = "采集中...";

                try
                {
                    DoActionTasks(_currentGatherTarget.Value);
                }
                catch (NoGatherableItemsInNodeException)
                {
                    CloseGatheringAddons();
                }
                catch (NoCollectableActionsException)
                {
                    Communicator.PrintError(
                        "当前无可用的收藏品价值上升技能, 请在配置中至少启用一个收藏品技能");
                    AbortAutoGather();
                }


                return;
            }

            if (AutoRetainer.IsEnabled && GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode)
            {
                if (ShouldWaitForAutoRetainer())
                {
                    Waiting = true;
                    _plugin.Ipc.AutoGatherWaiting();
                    return;
                }
            }

            if (_wasGatheringSpearfish)
            {
                GatherBuddy.Log.Debug("[AutoGather] 刺鱼完成, 正在刷新目标");
                _wasGatheringSpearfish = false;
                _activeItemList.ForceRefresh();
            }

            ActionSequence = null;
            CurrentCollectableRotation = null;

            //Cache IPC call results
            var isPathGenerating = IsPathGenerating;
            var isPathing = IsPathing;

            if (_advancedUnstuck.IsRunning || !FishingApproachOwnsRecovery && !_advancedUnstuck.Check(CurrentDestination, isPathing))
            {
                StopNavigation();
                AutoStatus = $"尝试进一步脱离卡死";
                return;
            }

            if (isPathGenerating)
            {
                AutoStatus = "正在生成路径...";
                return;
            }

            if (Player.Job is 17 /* BTN */ or 16 /* MIN */ or 18 /* FSH */
             && !isPathing
             && !Dalamud.Conditions[ConditionFlag.Mounted])
            {
                if (Player.Job == 18 /* FSH */ && TryUseFishingConsumables(GetFishingConsumablesPreset()))
                {
                    return;
                }

                if (SpiritbondMax > 0)
                {
                    if (Player.Job == 18 /* FSH */ && GatherBuddy.Config.AutoGatherConfig.DeferMateriaExtractionDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
                    {
                        return;
                    }
                    else
                    {
                        GatherBuddy.Log.Debug($"[Materia] 触发提取. 正在采集={IsGathering}, 精炼度满={SpiritbondMax}");
                        if (IsGathering)
                        {
                            QueueQuitFishingTasks();
                        }

                        DoMateriaExtraction();
                        return;
                    }
                }

                if (FreeInventorySlots < 20 && HasReducibleItems())
                {
                    if (Player.Job == 18 /* FSH */ && GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
                    {
                        return;
                    }
                    else
                    {
                        ReduceItems(GatherBuddy.Config.AutoGatherConfig.AlwaysReduceAllItems);
                        return;
                    }
                }
            }

            if (TryUseAetherCannon())
            {
                return;
            }

            var next = _activeItemList.GetNextOrDefault();
            _currentRequestedTarget = next != default ? next.Item : null;

            if (next.Fish == null || next.FishingSpot?.Spearfishing == true)
            {
                ResetFishingApproachState();
            }

            if (next.Fish != null)
            {
                if (!GatherBuddy.Config.AutoGatherConfig.FishDataCollection)
                {
                    GatherBuddy.Log.Warning("[AutoGather] 未启用钓鱼数据收集, 请在设置中启用该功能或从自动采集列表中移除鱼类");
                    Communicator.PrintError(
                        "自动采集列表中含有鱼类, 但尚未启用\"参与钓鱼数据收集\", 自动采集无法继续. 请在设置中启用\"参与钓鱼数据收集\", 或从自动采集列表中移除鱼类");
                    AbortAutoGather();
                    return;
                }

                if (!AutoHook.Enabled)
                {
                    Communicator.PrintError(
                        "[GatherBuddyReborn] 自动采集列表中含有鱼类, 但 AutoHook 未安装或未启用, 自动采集无法继续. 请安装并启用 AutoHook, 或从自动采集列表中移除鱼类");
                    AbortAutoGather();
                    return;
                }
            }

            if (Diadem.IsInside && GatherBuddy.Config.AutoGatherConfig.DiademFarmCloudedNodes && _activeItemList.IsCloudedNodeConsumed)
            {
                var currentWeather = EnhancedCurrentWeather.GetCurrentWeatherId();
                var hasUmbralTarget = false;
                foreach (var target in _activeItemList)
                {
                    if (target.Node?.NodeType == NodeType.Clouded && target.Node.UmbralWeather.Id == currentWeather)
                    {
                        hasUmbralTarget = true;
                        break;
                    }
                }

                if (hasUmbralTarget)
                {
                    GatherBuddy.Log.Information($"[Umbral] 已采集本影节点 - 正离开云冠群岛以重置会话");
                    StopNavigation();
                    LeaveTheDiadem();
                    return;
                }
            }

            if (next == default)
            {
                if (!_activeItemList.HasItemsToGather)
                {
                    AbortAutoGather();
                    return;
                }

                if (GatherBuddy.CollectableManager?.IsRunning == true)
                {
                    AutoStatus = "正在交易收藏品...";
                    return;
                }

                if (HasCollectables())
                {
                    AutoStatus = "正在交易收藏品...";
                    GatherBuddy.CollectableManager?.Start(Collectables.CollectableRunSource.AutoGather);
                    return;
                }

                var waitAtAetheryte = false;
                if (GatherBuddy.Config.AutoGatherConfig.TeleportToNextNode)
                {
                    var nextTimed = _activeItemList.PeekNextTimed();
                    waitAtAetheryte = nextTimed != default;
                    if (waitAtAetheryte && nextTimed.Location.Territory.Id != currentTerritory)
                    {
                        // Replace next target and fall through to teleport to its location.
                        next = nextTimed;
                    }
                }

                if (!waitAtAetheryte && GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle)
                {
                    if (GoHome())
                    {
                        return;
                    }
                }

                if (HasReducibleItems())
                {
                    if (Player.Job == 18 /* FSH */)
                    {
                        if (GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
                        {
                            return;
                        }
                        else
                        {
                            if (IsGathering)
                            {
                                QueueQuitFishingTasks();
                                return;
                            }

                            if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                            {
                                TaskManager.Enqueue(() =>
                                {
                                    AutoHook.SetPluginState?.Invoke(false);
                                    AutoHook.SetAutoStartFishing?.Invoke(false);
                                });
                            }

                            ReduceItems(true, () =>
                            {
                                if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                                {
                                    AutoHook.SetPluginState?.Invoke(true);
                                    AutoHook.SetAutoStartFishing?.Invoke(true);
                                }
                            });
                        }
                    }
                    else
                    {
                        ReduceItems(true);
                    }

                    return;
                }

                if (next == default)
                {
                    if (!Waiting)
                    {
                        Waiting = true;
                        _plugin.Ipc.AutoGatherWaiting();
                    }

                    AutoStatus = "无待采集物品";
                    return;
                }
            }

            _currentRequestedTarget = next.Item;
            Waiting = false;

            if (next.Item.ItemData.IsCollectable
                 && !CheckCollectablesUnlocked(next.Location.GatheringType.ToGroup()))
            {
                AbortAutoGather();
                return;
            }

            if (RepairIfNeeded())
            {
                return;
            }

            if (GatherBuddy.CollectableManager?.IsRunning == true)
            {
                AutoStatus = "正在交易收藏品...";
                return;
            }

            if (HasCollectables())
            {
                AutoStatus = "正在交易收藏品...";
                GatherBuddy.CollectableManager?.Start(Collectables.CollectableRunSource.AutoGather);
                return;
            }

            if (!GatherBuddy.Config.AutoGatherConfig.UseNavigation)
            {
                AutoStatus = "等待采集点出现... (无导航模式)";
                return;
            }

            var territoryId = currentTerritory;
            var targetTerritoryId = next.Location.Territory.Id;

            if ((territoryId == 129 && targetTerritoryId == 128
             || territoryId == 128 && targetTerritoryId == 129
             || territoryId == 132 && targetTerritoryId == 133
             || territoryId == 133 && targetTerritoryId == 132
             || territoryId == 130 && targetTerritoryId == 131
             || territoryId == 131 && targetTerritoryId == 130) && Lifestream.Enabled)
            {
                if (!Lifestream.IsBusy())
                {
                    if (Dalamud.Conditions[ConditionFlag.Gathering])
                    {
                        AutoStatus = "正在关闭采集窗口以准备传送...";
                        CloseGatheringAddons();
                        return;
                    }
                    AutoStatus = "正在使用城内以太之光...";
                    StopNavigation();
                    var name = string.Empty;
                    var territorySheet = Dalamud.GameData.GetExcelSheet<TerritoryType>();
                    var aetheryteSheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();

                    var targetTerritory = territorySheet.GetRow((uint)targetTerritoryId);
                    var aethernetShard = default(Lumina.Excel.Sheets.Aetheryte);
                    foreach (var a in aetheryteSheet)
                    {
                        if (a.Territory.RowId == targetTerritoryId && !a.IsAetheryte && a.AethernetName.RowId > 0)
                        {
                            aethernetShard = a;
                            break;
                        }
                    }

                    if (aethernetShard.RowId > 0)
                    {
                        name = aethernetShard.AethernetName.Value.Name.ToString();
                    }
                    else
                    {
                        name = targetTerritory.PlaceName.Value.Name.ToString();
                    }

                    TaskManager.Enqueue(() => Lifestream.AethernetTeleport(name));
                    TaskManager.DelayNext(1000);
                    TaskManager.Enqueue(() => GenericHelpers.IsScreenReady());
                }

                return;
            }

            var housingWardTerritories = new uint[] { 339, 340, 341, 649, 641 };
            var isTargetHousingWard = housingWardTerritories.Contains((uint)targetTerritoryId);

            if (isTargetHousingWard && Lifestream.Enabled)
            {
                var canAccessFromCurrentTerritory = territoryId == 129 && targetTerritoryId == 339  // Limsa -> Mist
                                                  || territoryId is 130 or 131 && targetTerritoryId == 341  // Ul'dah -> Goblet
                                                  || territoryId == 132 && targetTerritoryId == 340  // Gridania -> Lavender
                                                  || territoryId == 418 && targetTerritoryId == 649  // Foundation -> Empyreum
                                                  || territoryId == 628 && targetTerritoryId == 641; // Kugane -> Shirogane

                if (canAccessFromCurrentTerritory)
                {
                    if (!Lifestream.IsBusy())
                    {
                        if (IsFishing)
                        {
                            if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                            {
                                AutoHook.SetPluginState?.Invoke(false);
                                AutoHook.SetAutoStartFishing?.Invoke(false);
                            }
                            AutoStatus = "正在停止钓鱼以准备传送...";
                            QueueQuitFishingTasks();
                            return;
                        }

                        if (Dalamud.Conditions[ConditionFlag.Gathering])
                        {
                            AutoStatus = "正在关闭采集窗口以准备传送...";
                            CloseGatheringAddons();
                            return;
                        }

                        AutoStatus = "正在传送到房区...";
                        StopNavigation();

                        var wardCommand = targetTerritoryId switch
                        {
                            339 => "mist 1 1",
                            341 => "goblet 1 1",
                            340 => "lavender 1 1",
                            649 => "empyreum 1 1",
                            641 => "shirogane 1 1",
                            _ => ""
                        };

                        if (!string.IsNullOrEmpty(wardCommand))
                        {
                            TaskManager.Enqueue(() => Lifestream.ExecuteCommand(wardCommand));
                            TaskManager.DelayNext(1000);
                            TaskManager.Enqueue(() => GenericHelpers.IsScreenReady());
                        }
                    }

                    return;
                }
            }

            //Idyllshire to The Dravanian Hinterlands
            if (territoryId == 478 && next.Location.Territory.Id == 399)
            {
                IGameObject? aetheryte = null;
                var aetheryteDistance = float.MaxValue;
                foreach (var x in Dalamud.Objects)
                {
                    if (x.ObjectKind != ObjectKind.Aetheryte || !x.IsTargetable)
                    {
                        continue;
                    }

                    var distance = x.Position.DistanceToPlayer();
                    if (aetheryte == null || distance < aetheryteDistance)
                    {
                        aetheryte = x;
                        aetheryteDistance = distance;
                    }
                }
                if (aetheryte != null)
                {
                    if (aetheryte.Position.DistanceToPlayer() > 10)
                    {
                        AutoStatus = "正在移动到以太之光...";
                        if (!isPathing && !isPathGenerating)
                        {
                            Navigate(aetheryte.Position, false);
                        }
                    }
                    else if (!Lifestream.IsBusy())
                    {
                        if (Dalamud.Conditions[ConditionFlag.Gathering])
                        {
                            AutoStatus = "正在关闭采集窗口以准备传送...";
                            CloseGatheringAddons();
                            return;
                        }
                        AutoStatus = "传送中...";
                        StopNavigation();
                        var xCoord = next.Location.DefaultXCoord;
                        var exit = xCoord < 2000 ? 91u : 92u;
                        var name = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRow(exit).AethernetName.Value.Name.ToString();

                        TaskManager.Enqueue(() => Lifestream.AethernetTeleport(name));
                        TaskManager.DelayNext(1000);
                        TaskManager.Enqueue(() => GenericHelpers.IsScreenReady());
                    }

                    return;
                }
            }

            if (territoryId == 886 && next.Location.Territory.Id == Diadem.Territory.Id)
            {
                if (JobAsGatheringType == GatheringType.Unknown)
                {
                    var requiredGatheringType = next.Location.GatheringType.ToGroup();
                    if (ChangeGearSet(requiredGatheringType, 2400))
                    {
                        return;
                    }
                    else
                    {
                        AbortAutoGather();
                        return;
                    }
                }

                var dutyNpc = FindGameObject(o => o.BaseId == 1031694);
                var selectStringAddon = Dalamud.GameGui.GetAddonByName("SelectString");
                var talkAddon = Dalamud.GameGui.GetAddonByName("Talk");
                var selectYesNoAddon = Dalamud.GameGui.GetAddonByName("SelectYesno");
                var contentsFinderConfirmAddon = Dalamud.GameGui.GetAddonByName("ContentsFinderConfirm");
                GatherBuddy.Log.Verbose($"界面: {selectStringAddon}, {talkAddon}, {selectYesNoAddon}, {contentsFinderConfirmAddon}");
                if (dutyNpc != null && dutyNpc.Position.DistanceToPlayer() > 3)
                {
                    AutoStatus = "正在移动到云冠群岛 NPC...";
                    var point = VNavmesh.Query.Mesh.NearestPoint(dutyNpc.Position, 10, 10000).GetValueOrDefault(dutyNpc.Position);
                    if (CurrentDestination != point || !isPathing && !isPathGenerating)
                    {
                        Navigate(point, false);
                    }
                    return;
                }
                else if (dutyNpc != null)
                {
                    switch (Dalamud.Conditions[ConditionFlag.OccupiedInQuestEvent])
                    {
                        case false when contentsFinderConfirmAddon > 0:
                            {
                                var contents = new AddonMaster.ContentsFinderConfirm(contentsFinderConfirmAddon);
                                contents.Commence();
                                TaskManager.DelayNext(500);
                                TaskManager.Enqueue(() => _diademQueuingInProgress = false);
                                TaskManager.Enqueue(() => Dalamud.Conditions[ConditionFlag.BoundByDuty]);
                                return;
                            }
                        case false when contentsFinderConfirmAddon == nint.Zero
                         && selectStringAddon == nint.Zero
                         && selectYesNoAddon == nint.Zero:
                            unsafe
                            {
                                var targetSystem = TargetSystem.Instance();
                                if (targetSystem == null)
                                {
                                    return;
                                }

                                TaskManager.Enqueue(StopNavigation);
                                TaskManager.Enqueue(()
                                    => targetSystem->OpenObjectInteraction(
                                        (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)dutyNpc.Address));
                                TaskManager.Enqueue(() => Dalamud.Conditions[ConditionFlag.OccupiedInQuestEvent]);
                                TaskManager.Enqueue(() => _diademQueuingInProgress = true);
                                return;
                            }
                        case true when selectStringAddon > 0:
                            {
                                var select = new AddonMaster.SelectString(selectStringAddon);
                                TaskManager.Enqueue(() => select.Entries[0].Select());
                                return;
                            }
                        case true when selectYesNoAddon > 0:
                            {
                                var yesNo = new AddonMaster.SelectYesno(selectYesNoAddon);
                                TaskManager.Enqueue(yesNo.Yes);
                                TaskManager.DelayNext(5000);
                                return;
                            }
                        case true when talkAddon > 0:
                            {
                                var talk = new AddonMaster.Talk(talkAddon);
                                TaskManager.Enqueue(talk.Click);
                                return;
                            }
                    }
                }
            }

            if (territoryId != Diadem.Territory.Id && territoryId != 886 && next.Location.Territory == Diadem.Territory && Lifestream.Enabled)
            {
                if (!Lifestream.IsBusy())
                {
                    AutoStatus = "传送中...";
                    StopNavigation();
                    TaskManager.Enqueue(() => Lifestream.ExecuteCommand("firmament"));
                    TaskManager.Enqueue(() => !Lifestream.IsBusy(), 30000);
                }
                return;
            }

            var forcedAetheryte = Array.Find(ForcedAetherytes.ZonesWithoutAetherytes, z => z.ZoneId == next.Location.Territory.Id);
            if (forcedAetheryte.ZoneId != 0
             && GatherBuddy.GameData.Aetherytes[forcedAetheryte.AetheryteId].Territory.Id == territoryId)
            {
                var needsLifestream = territoryId == 478 || territoryId == 129 || territoryId == 128 || territoryId == 132 || territoryId == 133 || territoryId == 130 || territoryId == 131;
                if (needsLifestream && !Lifestream.Enabled)
                {
                    AutoStatus = $"安装 Lifestream 或手动传送到 {next.Location.Territory.Name}";
                }
                else
                {
                    AutoStatus = "需要手动传送";
                }

                return;
            }

            var housingWardTerritoriesCheck = new uint[] { 339, 340, 341, 649, 641 };
            if (housingWardTerritoriesCheck.Contains((uint)targetTerritoryId) && !Lifestream.Enabled)
            {
                AutoStatus = "安装 Lifestream 以进入房区";
                return;
            }

            // At this point, we are definitely going to gather something, so we may go home after that.
            if (Lifestream.Enabled)
            {
                Lifestream.Abort();
            }

            WentHome = false;

            var isInSameCityPair = territoryId is 128 or 129 && targetTerritoryId is 128 or 129
                                || territoryId is 132 or 133 && targetTerritoryId is 132 or 133
                                || territoryId is 130 or 131 && targetTerritoryId is 130 or 131;

            if (next.Location.Territory.Id != territoryId && !isInSameCityPair)
            {
                if (Dalamud.Conditions[ConditionFlag.BoundByDuty] && !Diadem.IsInside)
                {
                    AutoStatus = "处于任务状态中无法传送";
                    return;
                }
                else if (Diadem.IsInside)
                {
                    LeaveTheDiadem();
                    return;
                }

                if (Dalamud.Conditions[ConditionFlag.Gathering]
                 || Dalamud.Conditions[ConditionFlag.ExecutingGatheringAction]
                 || Dalamud.Conditions[ConditionFlag.Occupied]
                 || Dalamud.Conditions[ConditionFlag.Fishing]
                 || Dalamud.Conditions[ConditionFlag.Casting]
                 || Dalamud.Conditions[ConditionFlag.Mounting]
                 || Dalamud.Conditions[ConditionFlag.Mounting71])
                {
                    AutoStatus = "正在等待当前技能完成以准备传送...";
                    return;
                }

                if (TaskManager.IsBusy)
                {
                    AutoStatus = "正在等待当前任务完成以准备传送...";
                    return;
                }

                if (Environment.TickCount64 - _lastNodeInteractionTime < 5000)
                {
                    AutoStatus = "正在等待附近采集点交互后准备传送...";
                    return;
                }

                AutoStatus = "传送中...";
                StopNavigation();

                if (!MoveToTerritory(next.Location))
                {
                    AbortAutoGather();
                }

                return;
            }

            var targetGatheringType = next.Location.GatheringType.ToGroup();

            var config = next.Fish != null
                ? MatchConfigPreset(next.Fish)
                : MatchConfigPreset(next.Gatherable);

            if (DoUseConsumablesWithoutCastTime(config))
            {
                return;
            }

            if (JobAsGatheringType != targetGatheringType)
            {
                if (!ChangeGearSet(targetGatheringType, 2400))
                {
                    AbortAutoGather();
                }

                return;
            }

            if (next.Fish != null)
            {
                if (next.FishingSpot?.Spearfishing == true)
                {
                    DoNodeMovement(next, config);
                    return;
                }

                DoFishMovement(next);
                return;
            }


            if (next.Gatherable != null)
            {
                DoNodeMovement(next, config);
                return;
            }

            AutoStatus = "意外脱离循环控制, 请报告此错误";
            return;
        }

        public readonly Dictionary<GatherTarget, (Vector3 Position, Angle Rotation, DateTime Expiration)> FishingSpotData = [];
        private readonly Dictionary<Vector3, DateTime> _fishingSpotDismountAttempts = [];

        private KeyValuePair<GatherTarget, (Vector3 Position, Angle Rotation, DateTime Expiration)> FindFishingSpotEntry(GatherTarget target)
        {
            var spotId = target.FishingSpot?.Id;
            foreach (var entry in FishingSpotData)
            {
                if (entry.Key.FishingSpot?.Id == spotId)
                {
                    return entry;
                }
            }

            return default;
        }

        private void DoFishMovement(GatherTarget next)
        {
            Debug.Assert(next.Fish != null);
            Debug.Assert(next.FishingSpot != null);

            var fish = next;
            var territoryId = Dalamud.ClientState.TerritoryType;

            var isPathGenerating = IsPathGenerating;
            var isPathing = IsPathing;

            EnsureFishingApproachTarget(fish);
            if (_fishingApproachRecoveryPending)
            {
                if (_advancedUnstuck.IsRunning)
                {
                    AutoStatus = "正在恢复钓鱼寻路...";
                    return;
                }

                _fishingApproachRecoveryPending = false;
            }

            if (!FishingSpotData.TryGetValue(fish, out var fishingSpotData))
            {
                var existingEntryForSameSpot = FindFishingSpotEntry(fish);
                if (existingEntryForSameSpot.Key.Fish != null)
                {
                    GatherBuddy.Log.Information($"[AutoGather] 为相同钓场重复使用位置 (从 {existingEntryForSameSpot.Key.Fish.Name[GatherBuddy.Language]} 切换到 {fish.Fish!.Name[GatherBuddy.Language]})");
                    FishingSpotData.Add(fish, existingEntryForSameSpot.Value);
                    SetFishingApproachPosition(fish, existingEntryForSameSpot.Value.Position);

                    if (IsFishing)
                    {
                        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                        {
                            AutoHook.SetPluginState?.Invoke(false);
                            AutoHook.SetAutoStartFishing?.Invoke(false);
                        }
                        AutoStatus = "正在停止钓鱼以更换目标...";
                        QueueQuitFishingTasks();
                    }

                    return;
                }

                if (IsFishing)
                {
                    if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                    {
                        AutoHook.SetPluginState?.Invoke(false);
                        AutoHook.SetAutoStartFishing?.Invoke(false);
                    }
                    AutoStatus = "正在停止钓鱼以更换目标...";
                    QueueQuitFishingTasks();
                    return;
                }

                var selectingAlternate = _failedFishingApproachPositions.Count > 0;
                var positionData = selectingAlternate
                    ? _plugin.FishRecorder.GetPositionForFishingSpot(fish!.FishingSpot, _failedFishingApproachPositions, Player.Position)
                    : _plugin.FishRecorder.GetPositionForFishingSpot(fish!.FishingSpot);
                if (!positionData.HasValue)
                {
                    if (_failedFishingApproachPositions.Count > 0)
                    {
                        var failedPositions = FormatFailedFishingPositions();
                        GatherBuddy.Log.Error(
                            $"[AutoGather] No untried fishing positions remain for {fish.Fish!.Name[GatherBuddy.Language]} at {fish.FishingSpot.Name}; failed={failedPositions}; destination={CurrentDestination}; pathing={IsPathing}; generating={IsPathGenerating}");
                        AbortAutoGather($"钓场 {fish.FishingSpot.Name} 已无尚未尝试的钓点");
                        return;
                    }

                    Communicator.PrintError(
                        $"缺少钓场的位置信息: {fish.FishingSpot.Name}, 自动钓鱼无法继续. 至少在 \"{fish.FishingSpot.Name}\" 手动钓一次, 以便 GBR 获取位置");
                    AbortAutoGather();
                    return;
                }

                if (selectingAlternate)
                {
                    var alternateDistance = positionData.Value.Position.DistanceToPlayer();
                    if (alternateDistance > MaxFishingAlternateDistance)
                    {
                        var failedPositions = FormatFailedFishingPositions();
                        GatherBuddy.Log.Error(
                            $"[AutoGather] Nearest untried fishing position for {fish.Fish!.Name[GatherBuddy.Language]} at {fish.FishingSpot.Name} is {alternateDistance:F1}y away, exceeding the {MaxFishingAlternateDistance:F1}y recovery limit; failed={failedPositions}; destination={CurrentDestination}; pathing={IsPathing}; generating={IsPathGenerating}");
                        AbortAutoGather($"最近未尝试的钓点距离 {alternateDistance:F1}y，已停止自动钓鱼");
                        return;
                    }
                }

                FishingSpotData.Add(fish, (positionData.Value.Position, positionData.Value.Rotation, DateTime.MaxValue));
                SetFishingApproachPosition(fish, positionData.Value.Position);
                return;
            }

            if (_fishingApproachPosition == default)
            {
                SetFishingApproachPosition(fish, fishingSpotData.Position);
            }

            if (next.Fish.UmbralWeather.IsUmbral)
            {
                var currentWeather = EnhancedCurrentWeather.GetCurrentWeatherId();
                if (next.Fish.UmbralWeather.Id != currentWeather)
                {
                    if (IsGathering)
                    {
                        if (IsFishing && AutoHook.Enabled)
                        {
                            AutoHook.SetAutoStartFishing(false);
                        }
                        else
                        {
                            CleanupAutoHook();
                            QueueQuitFishingTasks();
                        }
                    }
                    else
                    {
                        AutoStatus = "正在等待正确的云冠群岛灵风天气";
                    }

                    return;
                }
            }

            if (_fishDetectedPlayer)
            {
                _fishDetectedPlayer = false;
                _processingFishingToast = true;

                if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                {
                    AutoHook.SetPluginState?.Invoke(false);
                    AutoHook.SetAutoStartFishing?.Invoke(false);
                }

                if (_consecutiveAmissCount == 1)
                {
                    GatherBuddy.Log.Warning($"[AutoGather] 首次脱钩检测 - 在钓点内重新定位...");
                    var oldPosition = fishingSpotData.Position;

                    const float MinRelocationDistance = 10.0f;
                    var positionData = _plugin.FishRecorder.GetPositionForFishingSpot(
                        fish!.FishingSpot,
                        oldPosition,
                        MinRelocationDistance,
                        _failedFishingApproachPositions);

                    if (!positionData.HasValue)
                    {
                        _processingFishingToast = false;
                        Communicator.PrintError(
                            $"缺少钓场 {fish.FishingSpot.Name} 的备用位置数据，无法继续自动钓鱼。");
                        AbortAutoGather();
                        return;
                    }

                    var newPos = positionData.Value.Position;
                    var newRot = positionData.Value.Rotation;
                    var dist = Vector3.Distance(newPos, oldPosition);

                    GatherBuddy.Log.Information($"[AutoGather] 在 '{fish.FishingSpot.Name}' 钓场内重新定位, " +
                              $"从 {oldPosition} 到 {newPos}, 距离={dist}y");

                    FishingSpotData[fish] = (newPos, newRot, DateTime.MaxValue);
                    SetFishingApproachPosition(fish, newPos);

                    AutoStatus = "鱼类警惕! 正在重新调整位置并等待 30 秒...";
                    QueueQuitFishingTasks();

                    TaskManager.DelayNext(30000);
                    TaskManager.Enqueue(() =>
                    {
                        _processingFishingToast = false;
                        GatherBuddy.Log.Information("[AutoGather] 等待完成, 恢复钓鱼...");
                        return true;
                    });
                }
                else
                {
                    GatherBuddy.Log.Warning($"[AutoGather] 持续脱钩 (次数: {_consecutiveAmissCount}) - 正在传送出地图以清除状态...");

                    AutoStatus = "鱼类持续警惕! 正在传送离开地图以重置状态...";
                    QueueQuitFishingTasks();

                    TaskManager.Enqueue(() =>
                    {
                        var wentHome = GoHome();
                        if (wentHome)
                        {
                            GatherBuddy.Log.Information("[AutoGather] 已传送回家. 等待后返回钓场...");
                        }
                        else
                        {
                            GatherBuddy.Log.Warning("[AutoGather] 无法传送回家 (Lifestream 不可用?). 在当前地点等待...");
                        }
                        return true;
                    });

                    TaskManager.DelayNext(10000);

                    TaskManager.Enqueue(() =>
                    {
                        _consecutiveAmissCount = 0;
                        _processingFishingToast = false;
                        WentHome = false;
                        GatherBuddy.Log.Information("[AutoGather] 通过地图传送清除了脱钩状态. 正在返回钓场...");
                        AutoStatus = "正在返回钓点...";
                        return true;
                    });
                }

                return;
            }

            if (IsFishing)
            {
                if (GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes > 0)
                {
                    GatherBuddy.Log.Verbose($"[AutoGather] IsFishing 区块已进入, 将检查计时器. MaxFishingSpotMinutes={GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes}, 过期时间={fishingSpotData.Expiration}");
                }
                if (_fishWaryDetected)
                {
                    _fishWaryDetected = false;
                    _processingFishingToast = true;
                    GatherBuddy.Log.Information($"[AutoGather] 鱼类警惕警告 - 执行简单重新定位 (不计入脱钩)...");

                    var oldPosition = fishingSpotData.Position;
                    const float MinRelocationDistance = 10.0f;
                    var positionData = _plugin.FishRecorder.GetPositionForFishingSpot(
                        fish!.FishingSpot,
                        oldPosition,
                        MinRelocationDistance,
                        _failedFishingApproachPositions);

                    if (positionData.HasValue)
                    {
                        var newPos = positionData.Value.Position;
                        var newRot = positionData.Value.Rotation;
                        var dist = Vector3.Distance(newPos, oldPosition);

                        GatherBuddy.Log.Information($"[AutoGather] 警惕重新定位: {oldPosition} → {newPos}, 距离={dist}y");
                        FishingSpotData[fish] = (newPos, newRot, DateTime.MaxValue);
                        SetFishingApproachPosition(fish, newPos);

                        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                        {
                            AutoHook.SetPluginState?.Invoke(false);
                            AutoHook.SetAutoStartFishing?.Invoke(false);
                        }

                        AutoStatus = "钓点警惕 - 正在重新调整位置...";
                        QueueQuitFishingTasks();

                        TaskManager.Enqueue(() =>
                        {
                            _processingFishingToast = false;
                            GatherBuddy.Log.Debug("[AutoGather] 警惕处理完成, 已准备好接收新提示");
                            return true;
                        });
                    }
                    else
                    {
                        _processingFishingToast = false;
                        GatherBuddy.Log.Warning("[AutoGather] 没有用于警惕重新定位的备用位置, 继续...");
                    }

                    return;
                }

                if (GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes > 0 && fishingSpotData.Expiration < DateTime.Now)
                {
                    GatherBuddy.Log.Information($"[AutoGather] 钓场计时器过期 ({GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes} 分钟), 正在重新定位...");
                    var oldPosition = fishingSpotData.Position;

                    const float MinRelocationDistance = 10.0f;
                    var positionData = _plugin.FishRecorder.GetPositionForFishingSpot(
                        fish!.FishingSpot,
                        oldPosition,
                        MinRelocationDistance,
                        _failedFishingApproachPositions);

                    if (!positionData.HasValue)
                    {
                        Communicator.PrintError(
                            $"缺少钓场 {fish.FishingSpot.Name} 的备用位置数据，无法继续自动钓鱼。");
                        AbortAutoGather();
                        return;
                    }

                    var newPos = positionData.Value.Position;
                    var newRot = positionData.Value.Rotation;
                    var dist = Vector3.Distance(newPos, oldPosition);

                    GatherBuddy.Log.Debug($"[AutoGather] 为 '{fish.FishingSpot.Name}' 重新定位钓场, " +
                                  $"从 {oldPosition} 到 {newPos}, 距离={dist}");

                    FishingSpotData[fish] = (newPos, newRot, DateTime.MaxValue);
                    SetFishingApproachPosition(fish, newPos);

                    if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                    {
                        AutoHook.SetPluginState?.Invoke(false);
                        AutoHook.SetAutoStartFishing?.Invoke(false);
                    }

                    AutoStatus = "正在停止钓鱼以前往新的钓点...";
                    QueueQuitFishingTasks();
                    return;
                }

                DoFishingTasks(next);
                return;
            }

            if (GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes > 0 && fishingSpotData.Expiration < DateTime.Now)
            {
                GatherBuddy.Log.Debug("[AutoGather] 是时候换新钓场了!");
                var oldPosition = fishingSpotData.Position;

                const float MinRelocationDistance = 10.0f;
                var positionData = _plugin.FishRecorder.GetPositionForFishingSpot(
                    fish!.FishingSpot,
                    oldPosition,
                    MinRelocationDistance,
                    _failedFishingApproachPositions);

                if (!positionData.HasValue)
                {
                    Communicator.PrintError(
                        $"缺少钓场 {fish.FishingSpot.Name} 的备用位置数据，无法继续自动钓鱼。");
                    AbortAutoGather();
                    return;
                }

                var newPos = positionData.Value.Position;
                var newRot = positionData.Value.Rotation;
                var dist = Vector3.Distance(newPos, oldPosition);

                GatherBuddy.Log.Debug($"[AutoGather] 为 '{fish.FishingSpot.Name}' 重新定位钓场, " +
                              $"从 {oldPosition} 到 {newPos}, 距离={dist}");

                FishingSpotData[fish] = (newPos, newRot, DateTime.MaxValue);
                SetFishingApproachPosition(fish, newPos);

                AutoStatus = "正在移动至新的钓场...";
                MoveToFishingSpot(newPos, newRot);
                return;
            }

            if (Vector3.Distance(fishingSpotData.Position, Player.Position) < 1)
            {
                if (IsPathing || IsPathGenerating)
                {
                    StopNavigation();
                }

                if (Dalamud.Conditions[ConditionFlag.Mounted])
                {
                    if (!_fishingSpotDismountAttempts.TryGetValue(fishingSpotData.Position, out var firstAttempt))
                    {
                        _fishingSpotDismountAttempts[fishingSpotData.Position] = DateTime.Now;
                    }
                    else if ((DateTime.Now - firstAttempt).TotalSeconds > 5)
                    {
                        FailFishingApproach(fish, "failed to dismount for more than 5s", "无法在此处降落，正在脱困并选择附近钓点...", false, true);
                        return;
                    }

                    EnqueueDismount(moveAfterDismount: false);
                    AutoStatus = "正在下坐骑...";
                    return;
                }

                if (_fishingSpotDismountAttempts.ContainsKey(fishingSpotData.Position))
                {
                    _fishingSpotDismountAttempts.Remove(fishingSpotData.Position);
                }

                var playerAngle = new Angle(Player.Rotation);
                if (!playerAngle.AlmostEqual(fishingSpotData.Rotation, 1.Degrees().Rad))
                {
                    TaskManager.Enqueue(() => SetRotation(fishingSpotData.Rotation));
                    _fishingSpotArrivalTime.Remove(fish);
                    AutoStatus = "正在调整角色面向...";
                    return;
                }

                if (TaskManager.IsBusy)
                {
                    AutoStatus = "正在等待角色面向...";
                    return;
                }

                if (!_fishingSpotArrivalTime.ContainsKey(fish))
                {
                    _fishingSpotArrivalTime[fish] = DateTime.Now;
                }

                var timeSinceArrival = (DateTime.Now - _fishingSpotArrivalTime[fish]).TotalSeconds;
                if (timeSinceArrival < 1.0)
                {
                    AutoStatus = $"等待进行抛竿检查 ({1.0 - timeSinceArrival:F1}s)...";
                    return;
                }

                if (fishingSpotData.Expiration == DateTime.MaxValue && GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes > 0)
                {
                    var newExpiration = DateTime.Now.AddMinutes(GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes);
                    FishingSpotData[fish] = (fishingSpotData.Position, fishingSpotData.Rotation, newExpiration);
                    GatherBuddy.Log.Information($"[AutoGather] 已启动钓场计时器: {GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes} 分钟");
                }
                else
                {
                    GatherBuddy.Log.Debug($"钓场有效时间为 {(fishingSpotData.Expiration - DateTime.Now).TotalSeconds:F0} 秒");
                }


                uint castStatus;
                unsafe
                {
                    castStatus = ActionManager.Instance()->GetActionStatus(ActionType.Action, 289);
                }

                if (castStatus != 0)
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 抛竿动作状态为 {castStatus}, 检查是否需要微调");

                    var attemptCount = _jiggleAttempts.GetValueOrDefault(fish, 0);
                    if (attemptCount >= 3)
                    {
                        FailFishingApproach(fish, $"Cast remained unavailable after {attemptCount} jiggle attempts", "Too many jiggle attempts, finding a nearby spot...", false, false);
                        return;
                    }

                    if ((DateTime.Now - _lastJiggleTime).TotalSeconds < 5)
                    {
                        GatherBuddy.Log.Debug($"[AutoGather] 微调冷却中, 剩余 {5 - (DateTime.Now - _lastJiggleTime).TotalSeconds:F0} 秒");
                        return;
                    }

                    GatherBuddy.Log.Information($"[AutoGather] 无法抛竿 (状态: {castStatus}), 尝试调整位置 (第 {attemptCount + 1}/3 次)");

                    Vector3 newPos;
                    var newRotation = fishingSpotData.Rotation;

                    var forwardDirection = new Vector3(
                        (float)System.Math.Sin(Player.Rotation),
                        0,
                        (float)System.Math.Cos(Player.Rotation)
                    );

                    var stepSize = 1.0f;
                    var testPos = Player.Position + forwardDirection * stepSize;
                    var meshPoint = VNavmesh.Query.Mesh.NearestPoint(testPos, 0.5f, 1.0f);
                    if (meshPoint.HasValue)
                    {
                        newPos = meshPoint.Value;
                        GatherBuddy.Log.Information($"[AutoGather] 向前方移动 {stepSize:F1}y");
                    }
                    else
                    {
                        GatherBuddy.Log.Warning("[AutoGather] 在导航网格上找不到有效的调整位置");
                        _jiggleAttempts[fish] = attemptCount + 1;
                        _lastJiggleTime = DateTime.Now;
                        return;
                    }

                    _jiggleAttempts[fish] = attemptCount + 1;
                    FishingSpotData[fish] = (newPos, newRotation, fishingSpotData.Expiration);
                    _lastJiggleTime = DateTime.Now;

                    AutoStatus = "正在为抛竿调整位置...";
                    MoveToFishingSpot(newPos, newRotation);
                    return;
                }
                else
                {
                    if (_jiggleAttempts.ContainsKey(fish))
                    {
                        GatherBuddy.Log.Information($"[AutoGather] 微调后抛竿已可用, 清除尝试计数器");
                        _jiggleAttempts.Remove(fish);
                    }
                }

                StopNavigation();
                AutoStatus = "钓鱼中...";
                DoFishingTasks(next);
                return;
            }

            AutoStatus = "正在移动到钓点";
            if (!CheckFishingApproachProgress(fish, fishingSpotData.Position, isPathing, isPathGenerating))
            {
                return;
            }

            if (CurrentDestination != fishingSpotData.Position)
            {
                StopNavigation();
                var autoHookArmed =
                    GatherBuddy.Config.AutoGatherConfig.UseAutoHook
                    && AutoHook.Enabled
                    && AutoHook.GetAutoStartFishing?.Invoke() == true;

                if (IsGathering || IsFishing || autoHookArmed)
                {
                    AutoStatus = "正在停止钓鱼以更换目标...";
                    QueueQuitFishingTasks();
                    return;
                }

                MoveToFishingSpot(fishingSpotData.Position, fishingSpotData.Rotation);
            }
        }

        private bool DoNodeMovementDiadem(GatherTarget next, ConfigPreset config)
        {
            Debug.Assert(next.Gatherable != null);
            Debug.Assert(next.Node != null);

            var player = Player.Position;

            // ActiveItemsList prioritizes umbral items with matching weather,
            // so we only need to check the first item in the list.
            // Let the normal navigation logic handle Skybuilders' Tools quest items and Umbral nodes.

            if (next.Node.NodeType == NodeType.Clouded)
            {
                var currentWeather = EnhancedCurrentWeather.GetCurrentWeatherId();
                if (next.Node.UmbralWeather.Id != currentWeather || _activeItemList.IsCloudedNodeConsumed)
                {
                    AutoStatus = "正在等待正确的云冠群岛灵风天气";
                    StopNavigation();
                    return true;
                }

                // Check if the node hasn't spawned due to a game bug.
                var flag = TimedNodePosition;
                var nodeId = FirstNodeId(next.Node.WorldPositions);
                if (flag.HasValue && Vector2.Distance(flag.Value, player.ToVector2()) < NodeVisibilityDistance
                    && FindGameObject(o => o.ObjectKind == ObjectKind.GatheringPoint && o.IsTargetable && nodeId == o.BaseId) == null)
                {
                    GatherBuddy.Log.Warning("看起来云冠群岛采集点因游戏 bug 未刷新. 尝试离开");

                    // Pick a random node far away and move there.
                    var pos = Vector3.Zero;
                    var farPositions = 0;
                    foreach (var diademNode in GatherBuddy.GameData.GatheringNodes.Values)
                    {
                        if (diademNode.Territory != Diadem.Territory)
                        {
                            continue;
                        }

                        foreach (var positions in diademNode.WorldPositions.Values)
                        {
                            foreach (var position in positions)
                            {
                                if (Vector2.DistanceSquared(position.ToVector2(), flag.Value) > 200f * 200f && Random.Shared.Next(++farPositions) == 0)
                                {
                                    pos = position;
                                }
                            }
                        }
                    }

                    Navigate(pos, true, direct: true);
                    TaskManager.Enqueue(() => !IsPathGenerating);
                    TaskManager.Enqueue(() => FindGameObject(o => o.ObjectKind == ObjectKind.GatheringPoint && nodeId == o.BaseId) == null || !IsPathing, 10000);
                    TaskManager.Enqueue(StopNavigation);

                    AutoStatus = "尝试重置错误的云冠群岛采集点";
                    return true;
                }
                return false;
            }

            if (next.Item is Gatherable oddlyDelicateCandidate && Diadem.OddlyDelicateItems.Contains(oddlyDelicateCandidate))
            {
                return false;
            }

            // For regular nodes, we go in a full circle along the pre-calculated optimal path.
            var path = Diadem.ShortestPaths[JobAsGatheringType];
            if (_diademPathIndex == -1)
            {
                // Find the closest node to start the path.
                var closestDist = float.PositiveInfinity;
                for (var i = 0; i < path.Length; i++)
                {
                    if (!_diadem.IsNodeAvailable(path[i]))
                    {
                        continue;
                    }

                    var positionSum = Vector3.Zero;
                    var positionCount = 0;
                    foreach (var position in WorldData.WorldLocationsByNodeId[path[i]])
                    {
                        if (!IsBlacklisted(position))
                        {
                            positionSum += position;
                            ++positionCount;
                        }
                    }

                    if (positionCount == 0)
                    {
                        continue;
                    }

                    var dist = Vector3.Distance(player, positionSum / positionCount);
                    // If there are several close nodes within 50y of each other, pick the one with the lowest index.
                    if (dist + 50f < closestDist)
                    {
                        closestDist = dist;
                        _diademPathIndex = i;
                    }
                }
            }
            else
            {
                var prevIndex = _diademPathIndex;
                while (!_diadem.IsNodeAvailable(path[_diademPathIndex]) || WorldData.WorldLocationsByNodeId[path[_diademPathIndex]].TrueForAll(IsBlacklisted))
                {
                    _diademPathIndex = (_diademPathIndex + 1) % path.Length;
                    if (prevIndex == _diademPathIndex)
                    {
                        AbortAutoGather("云冠群岛所有活跃采集点均已被屏蔽");
                    }
                }
            }

            var nextNodeId = path[_diademPathIndex];
            var nextNode = FindGameObject(o => o.ObjectKind == ObjectKind.GatheringPoint && o.BaseId == nextNodeId && o.IsTargetable);
            if (nextNode != null)
            {
                if (IsBlacklisted(nextNode.Position))
                {
                    _diademPathIndex = (_diademPathIndex + 1) % path.Length;
                }
                else
                {
                    AutoStatus = $"正在移动至云冠群岛采集点 ({Vector3.Distance(player, nextNode.Position):F0}y)...";
                    var pos = nextNode.Position;
                    if (TryWindmireJump(ref pos))
                    {
                        Navigate(pos, ShouldFly(pos), direct: true, nodeId: nextNode.BaseId);
                    }
                    else
                    {
                        MoveToCloseNode(nextNode, next.Gatherable!, config);
                    }
                }
            }
            else
            {
                var nodeId = path[_diademPathIndex];
                var nodePositions = WorldData.WorldLocationsByNodeId[nodeId];
                if (nodePositions.Count == 0)
                {
                    throw new InvalidOperationException($"Diadem node {nodeId} has no known positions.");
                }

                var pos = nodePositions[0];
                var posDistance = Vector3.DistanceSquared(pos, player);
                for (var i = 1; i < nodePositions.Count; ++i)
                {
                    var distance = Vector3.DistanceSquared(nodePositions[i], player);
                    if (distance < posDistance)
                    {
                        pos = nodePositions[i];
                        posDistance = distance;
                    }
                }

                AutoStatus = $"正在移动至云冠群岛采集点 ({Vector3.Distance(player, pos):F0}y)...";

                var jump = TryWindmireJump(ref pos);
                Navigate(pos, ShouldFly(pos), direct: jump, nodeId: nodeId);
            }
            return true;
        }

        private static bool TryWindmireJump(ref Vector3 destination)
        {
            if (!GatherBuddy.Config.AutoGatherConfig.DiademWindmireJumps)
            {
                return false;
            }

            var pos = destination;
            var player = Player.Position;

            var windmire = Vector3.Zero;
            var windmireDistance = float.MaxValue;
            foreach (var (jumpFrom, jumpTo) in Diadem.Windmires)
            {
                var distance = Vector3.Distance(player, jumpFrom) + Vector3.Distance(jumpTo, pos);
                if (distance < windmireDistance)
                {
                    windmire = jumpFrom;
                    windmireDistance = distance;
                }
            }

            var directDistance = Vector3.Distance(player, pos);

            // Use Windmire only if it provides a 2x advantage in distance.
            if (windmireDistance * 2f < directDistance)
            {
                destination = windmire;
                return true;
            }
            return false;
        }

        private void DoNodeMovement(GatherTarget next, ConfigPreset config)
        {
            if (Diadem.IsInside)
            {
                if (DoNodeMovementDiadem(next, config))
                {
                    return;
                }

                _diademPathIndex = -1;
            }

            var allPositions = new List<(uint Id, Vector3 Position)>();
            foreach (var (worldNodeId, worldPositions) in next.Location.WorldPositions)
            {
                if (VisitedNodes.Contains(worldNodeId))
                {
                    continue;
                }

                foreach (var worldPosition in worldPositions)
                {
                    if (!IsBlacklisted(worldPosition))
                    {
                        allPositions.Add((worldNodeId, worldPosition));
                    }
                }
            }

            var isSpearfishing = next.Fish?.IsSpearFish == true;
            var visibleNodes = new List<IGameObject>();
            if (isSpearfishing && next.FishingSpot is { } spearfishingSpot)
            {
                foreach (var gameObject in Dalamud.Objects)
                {
                    if (TryGetSpearfishingNodeState(gameObject, out var state)
                     && state.RemainingCount > 0
                     && MatchesSpearfishingSpot(spearfishingSpot, state)
                     && !IsVisitedSpearfishingNode(state))
                    {
                        visibleNodes.Add(gameObject);
                    }
                }
            }
            else
            {
                foreach (var gameObject in Dalamud.Objects)
                {
                    if (allPositions.Contains((gameObject.BaseId, gameObject.Position)))
                    {
                        visibleNodes.Add(gameObject);
                    }
                }
            }

            var closestTargetableNode = FindClosestObject(visibleNodes, true);

            if (!isSpearfishing)
            {
                var isTimedNode = next.Gatherable?.NodeType is NodeType.Unspoiled or NodeType.Legendary or NodeType.Clouded;
                if (ActivateGatheringBuffs(isTimedNode))
                {
                    return;
                }
            }

            if (closestTargetableNode != null)
            {
                AutoStatus = "正在移动至采集点...";

                if (next.Gatherable != null)
                {
                    MoveToCloseNode(closestTargetableNode, next.Gatherable, config);
                }
                else if (next.Fish != null)
                {
                    MoveToCloseSpearfishingNode(closestTargetableNode, next.Fish, next.FishingSpot!);
                }
                return;
            }

            if (isSpearfishing && FindClosestObject(visibleNodes, false) is { } visibleSpearfishingNode)
            {
                AutoStatus = visibleSpearfishingNode.IsTargetable ? "正在移动至采集点..." : "正在等待采集点变为可选中...";
                MoveToCloseSpearfishingNode(visibleSpearfishingNode, next.Fish!, next.FishingSpot!);
                return;
            }

            if (isSpearfishing && next.FishingSpot is { IsShadowNode: true } shadowSpot)
            {
                if (TryGetSwimmingShadowsMarker(shadowSpot, out var shadowMarker))
                {
                    AutoStatus = "正在移动至鱼影...";
                    if (CurrentDestination != default
                     && IsPathing
                     && Vector2.DistanceSquared(CurrentDestination.ToVector2(), shadowMarker.ToVector2()) <= 10 * 10)
                    {
                        return;
                    }

                    var destination = VNavmesh.Query.Mesh.NearestPoint(shadowMarker, 10, 10000).GetValueOrDefault(shadowMarker);
                    Navigate(destination, ShouldFly(destination));
                    return;
                }

                StopNavigation();
                _activeItemList.ForceRefresh();
                AutoStatus = "正在刷新鱼影状态...";
                return;
            }

            AutoStatus = "正在移动至较远采集点...";

            if (CurrentDestination != default && IsPathing)
            {
                var currentNode = visibleNodes.Find(o => o.Position == CurrentDestination);
                if (currentNode != null && !currentNode.IsTargetable)
                {
                    GatherBuddy.Log.Verbose($"远点不可选中，距离 {currentNode.Position.DistanceToPlayer()}");
                }

                //It takes some time (roundtrip to the server) before a node becomes targetable after it becomes visible,
                //so we need to delay excluding it. But instead of measuring time, we use distance, since character is traveling at a constant speed.
                //Value 50 was determined empirically.
                foreach (var node in allPositions)
                {
                    if (node.Position.DistanceToPlayer() < NodeVisibilityDistance)
                    {
                        FarNodesSeenSoFar.Add(node.Position);
                    }
                }

                if (CurrentDestination.DistanceToPlayer() < NodeVisibilityDistance)
                {
                    GatherBuddy.Log.Verbose("远点不可选中, 正在选择其他");
                }
                else
                {
                    return;
                }
            }

            (uint? Id, Vector3 Position) selectedFarNode;

            // Only Legendary, Unspoiled, and Clouded nodes show a map marker.
            var mapMarkerAvailable = next.Node?.NodeType is NodeType.Legendary or NodeType.Unspoiled or NodeType.Clouded;
            // Wait an additional 8 seconds because it takes a few seconds for the previous flag to disappear.
            var gracePeriod = next.Time == TimeInterval.Always ? 0 : next.Time.Start - GatherBuddy.Time.ServerTime.AddSeconds(-8);
            var mapMarker = mapMarkerAvailable && gracePeriod <= 0 ? TimedNodePosition : null;

            if (mapMarkerAvailable && ShouldUseFlag)
            {
                if (mapMarker == null)
                {
                    AutoStatus = "等待地图标记出现" + (gracePeriod > 0 ? $" (缓冲时间: {gracePeriod / RealTime.MillisecondsPerSecond} 秒)" : "");
                    return;
                }

                selectedFarNode = FindClosestPosition(allPositions, mapMarker.Value, null);

                if (selectedFarNode.Position == default || Vector2.DistanceSquared(mapMarker.Value, selectedFarNode.Position.ToVector2()) > 10 * 10)
                {
                    var point = new Vector3(mapMarker.Value.X, 0, mapMarker.Value.Y);
                    selectedFarNode = (null, VNavmesh.Query.Mesh.NearestPoint(point, 10, 10000).GetValueOrDefault(point));
                }
            }
            else
            {
                //Select the closest node
                selectedFarNode = FindClosestPosition(allPositions, mapMarker ?? Player.Position.ToVector2(), FarNodesSeenSoFar);

                if (selectedFarNode.Position == default)
                {
                    FarNodesSeenSoFar.Clear();
                    GatherBuddy.Log.Verbose($"选定采集点为空, 远距离采集点过滤已清除");
                    return;
                }
            }

            var jump = Diadem.IsInside && TryWindmireJump(ref selectedFarNode.Position);

            Navigate(selectedFarNode.Position, ShouldFly(selectedFarNode.Position), direct: jump, nodeId: jump ? null : selectedFarNode.Id);
        }

        private uint LastVisitedNode
            => VisitedNodes.Count > 0 ? VisitedNodes[^1] : 0;

        private string FormatFailedFishingPositions()
            => string.Join(", ", _failedFishingApproachPositions.ConvertAll(p => $"({p.X:F1}, {p.Y:F1}, {p.Z:F1})"));

        private static IGameObject? FindGameObject(Predicate<IGameObject> match)
        {
            foreach (var gameObject in Dalamud.Objects)
            {
                if (match(gameObject))
                {
                    return gameObject;
                }
            }

            return null;
        }

        private static IGameObject? FindClosestObject(List<IGameObject> objects, bool targetableOnly)
        {
            var playerPosition = Player.Position;
            IGameObject? closest = null;
            var closestDistance = float.MaxValue;
            foreach (var gameObject in objects)
            {
                if (targetableOnly && !gameObject.IsTargetable)
                {
                    continue;
                }

                var distance = Vector3.Distance(playerPosition, gameObject.Position);
                if (closest == null || distance < closestDistance)
                {
                    closest = gameObject;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private static (uint? Id, Vector3 Position) FindClosestPosition(List<(uint Id, Vector3 Position)> positions, Vector2 target,
            HashSet<Vector3>? exclude)
        {
            (uint? Id, Vector3 Position) closest = default;
            var closestDistance = float.MaxValue;
            var found = false;
            foreach (var (id, position) in positions)
            {
                if (exclude != null && exclude.Contains(position))
                {
                    continue;
                }

                var distance = Vector2.DistanceSquared(target, position.ToVector2());
                if (!found || distance < closestDistance)
                {
                    closest = (id, position);
                    closestDistance = distance;
                    found = true;
                }
            }

            return closest;
        }

        private static uint FirstNodeId(Dictionary<uint, List<Vector3>> worldPositions)
        {
            foreach (var nodeId in worldPositions.Keys)
            {
                return nodeId;
            }

            throw new InvalidOperationException("The node has no world positions.");
        }

        private unsafe void LeaveTheDiadem()
        {
            TaskManager.Enqueue(() =>
            {
                AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentsFinderMenu)->Show();
            });

            TaskManager.Enqueue(() =>
            {
                if (GenericHelpers.TryGetAddonByName("ContentsFinderMenu", out var addon) && addon->IsReady)
                {
                    var leaveCallback = stackalloc FFXIVClientStructs.FFXIV.Component.GUI.AtkValue[1];
                    addon->FireCallback(1, leaveCallback);
                    return true;
                }
                return false;
            }, "等待任务搜索器界面");

            TaskManager.DelayNext(500);

            TaskManager.Enqueue(() =>
            {
                if (GenericHelpers.TryGetAddonByName("SelectYesno", out var yesnoAddon) && yesnoAddon->IsReady)
                {
                    var yesNo = new AddonMaster.SelectYesno((nint)yesnoAddon);
                    yesNo.Yes();
                    return;
                }
            });

            TaskManager.DelayNext(500);
            TaskManager.Enqueue(() => !GenericHelpers.TryGetAddonByName("SelectYesno", out _), "等待确认对话框关闭");
            TaskManager.Enqueue(() => GenericHelpers.IsScreenReady());
        }

        private void AbortAutoGather(string? status = null)
        {
            ResetFishingApproachState();
            _currentRequestedTarget = null;

            if (Diadem.IsInside)
            {
                LeaveTheDiadem();
                return;
            }

            if (HasReducibleItems())
            {
                if (Player.Job == 18 && GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs && (IsFishing || HasActiveFishingBuff()))
                {
                    GatherBuddy.Log.Debug("[AutoGather] 因正在钓鱼或有增益效果, 中止时跳过精选");
                }
                else
                {
                    GatherBuddy.Log.Debug("[AutoGather] 在中止时发现可精选物品, 关闭前进行精选");

                    if (Player.Job == 18)
                    {
                        if (IsGathering)
                        {
                            QueueQuitFishingTasks();
                            return;
                        }

                        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHook && AutoHook.Enabled)
                        {
                            TaskManager.Enqueue(() =>
                            {
                                AutoHook.SetPluginState?.Invoke(false);
                                AutoHook.SetAutoStartFishing?.Invoke(false);
                            });
                        }

                        ReduceItems(true, () =>
                        {
                            AbortAutoGather(status);
                        });
                    }
                    else
                    {
                        ReduceItems(true, () =>
                        {
                            AbortAutoGather(status);
                        });
                    }

                    return;
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                AutoStatus = status;
            }

            if (GatherBuddy.Config.AutoGatherConfig.HonkMode)
            {
                Task.Run(() => _soundHelper.StartHonkSoundTask(3));
            }

            CloseGatheringAddons();
            if (GatherBuddy.Config.AutoGatherConfig.GoHomeWhenDone)
            {
                EnqueueActionWithDelay(() => { GoHome(); });
            }

            TaskManager.Enqueue(() =>
            {
                Enabled = false;
                AutoStatus = status ?? AutoStatus;
            });
        }

        private unsafe void CloseGatheringAddons(bool closeGathering = true)
        {
            var masterpieceOpen = MasterpieceAddon != null;
            var gatheringOpen = GatheringAddon != null;
            if (masterpieceOpen)
            {
                EnqueueActionWithDelay(() =>
                {
                    if (MasterpieceAddon is var addon and not null)
                    {
                        Callback.Fire(&addon->AtkUnitBase, true, -1);
                    }
                });
                TaskManager.Enqueue(() => MasterpieceAddon == null, "等待收藏品采集界面关闭");
                TaskManager.Enqueue(() => GatheringAddon is var addon and not null, "等待采集界面弹出");
                TaskManager.DelayNext(
                    300); //There is some delay after the moment the addon pops up (and is ready) before the callback can be used to close it. We wait some time and retry the callback.
            }

            if (closeGathering && (gatheringOpen || masterpieceOpen))
            {
                TaskManager.Enqueue(() =>
                {
                    if (GatheringAddon is var gathering and not null && gathering->IsReady)
                    {
                        Callback.Fire(&gathering->AtkUnitBase, true, -1);
                        TaskManager.DelayNextImmediate(100);
                        return false;
                    }

                    var addon = SelectYesnoAddon;
                    if (addon != null)
                    {
                        EnqueueActionWithDelay(() =>
                        {
                            if (SelectYesnoAddon is var addon and not null)
                            {
                                var master = new AddonMaster.SelectYesno(addon);
                                master.Yes();
                            }
                        }, true);
                        TaskManager.EnqueueImmediate(() => !IsGathering, "等待采集界面关闭");
                        return true;
                    }

                    return !IsGathering;
                }, "等待采集界面关闭或确认对话框弹出");
            }
        }

        private bool CheckCollectablesUnlocked(GatheringType gatheringType)
        {
            var level = gatheringType switch
            {
                GatheringType.Miner => DiscipleOfLand.MinerLevel,
                GatheringType.Botanist => DiscipleOfLand.BotanistLevel,
                GatheringType.Fisher => DiscipleOfLand.FisherLevel,
                GatheringType.Multiple => System.Math.Max(DiscipleOfLand.MinerLevel, DiscipleOfLand.BotanistLevel),
                _ => 0
            };
            if (level < Actions.Collect.MinLevel)
            {
                Communicator.PrintError("已将收藏品加入采集列表, 但等级不足以采集");
                return false;
            }

            var questId = gatheringType switch
            {
                GatheringType.Miner => Actions.Collect.QuestIds.Miner,
                GatheringType.Botanist => Actions.Collect.QuestIds.Botanist,
                _ => 0u
            };

            if (questId != 0 && !QuestManager.IsQuestComplete(questId))
            {
                Communicator.PrintError("已将收藏品加入采集列表, 但尚未解锁收藏品功能");
                var sheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Quest>()!;
                var row = sheet.GetRow(questId)!;
                var loc = row.IssuerLocation.Value!;
                var map = loc.Map.Value!;
                var pos = MapUtil.WorldToMap(new Vector2(loc.X, loc.Z), map);
                var mapPayload = new MapLinkPayload(loc.Territory.RowId, loc.Map.RowId, pos.X, pos.Y);
                var text = new SeStringBuilder();
                text.AddText("收藏品功能可通过任务 ")
                    .AddUiForeground(0x0225)
                    .AddUiGlow(0x0226)
                    .AddQuestLink(questId)
                    .AddUiForeground(500)
                    .AddUiGlow(501)
                    .AddText($"{(char)SeIconChar.LinkMarker}")
                    .AddUiGlowOff()
                    .AddUiForegroundOff()
                    .AddText(row.Name.ToString())
                    .Add(RawPayload.LinkTerminator)
                    .AddUiGlowOff()
                    .AddUiForegroundOff()
                    .AddText(" 解锁, 该任务可在 ")
                    .AddUiForeground(0x0225)
                    .AddUiGlow(0x0226)
                    .Add(mapPayload)
                    .AddUiForeground(500)
                    .AddUiGlow(501)
                    .AddText($"{(char)SeIconChar.LinkMarker}")
                    .AddUiGlowOff()
                    .AddUiForegroundOff()
                    .AddText($"{mapPayload.PlaceName} {mapPayload.CoordinateString}")
                    .Add(RawPayload.LinkTerminator)
                    .AddUiGlowOff()
                    .AddUiForegroundOff()
                    .AddText(" 开始");
                Communicator.Print(text.BuiltString);
                return false;
            }

            return true;
        }

        private unsafe bool ChangeGearSet(GatheringType job, int delay)
        {
            var (preferredName, classJobId) = job switch
            {
                GatheringType.Miner => (GatherBuddy.Config.MinerSetName, 16u),
                GatheringType.Botanist => (GatherBuddy.Config.BotanistSetName, 17u),
                GatheringType.Fisher => (GatherBuddy.Config.FisherSetName, 18u),
                _ => (null, 0u),
            };
            if (classJobId == 0)
            {
                Communicator.PrintError($"没有与 {job} 关联的职业。");
                return false;
            }

            var gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
            {
                Communicator.PrintError("无法读取已保存的套装。");
                return false;
            }

            if (!GearsetStatsReader.TryResolveExistingGearsetIndex(gearsetModule, classJobId, preferredName, out var gearsetIndex,
                    out var gearsetName))
            {
                Communicator.PrintError($"未找到 {job} 的已保存套装。");
                return false;
            }

            if (job is GatheringType.Miner or GatheringType.Botanist
                && Player.Job == 18 /* FSH */
                && GatherBuddy.Config.AutoGatherConfig.UseAutoHook
                && AutoHook.Enabled)
            {
                GatherBuddy.Log.Debug($"[AutoGather] 从捕鱼人切换到 {job}, 正在禁用 AutoHook");
                try
                {
                    AutoHook.SetPluginState(false);
                    AutoHook.SetAutoStartFishing?.Invoke(false);
                }
                catch (Exception e)
                {
                    GatherBuddy.Log.Error($"[AutoGather] 切换装备时禁用 AutoHook 失败: {e}");
                }

                CleanupAutoHook();
            }

            _diademPathIndex = -1; // Reset The Diadem path after changing job
            GatherBuddy.Log.Information($"[AutoGather] Switching to {job} with gearset {gearsetIndex} ({gearsetName}).");
            gearsetModule->EquipGearset(gearsetIndex);
            TaskManager.DelayNext(Random.Shared.Next(delay, delay + 500)); // Add a random delay to be less suspicious
            return true;
        }

        private void EnqueueEnsureAutoHookDisabled()
        {
            if (!GatherBuddy.Config.AutoGatherConfig.UseAutoHook || !AutoHook.Enabled)
            {
                return;
            }

            const int maxAttempts = 10;
            var attempt = 0;

            void TryDisableOnce()
            {
                attempt++;

                var pluginOn = AutoHook.GetPluginState?.Invoke() == true;
                var autoStartOn = AutoHook.GetAutoStartFishing?.Invoke() == true;
                var stillEnabled = pluginOn || autoStartOn;

                if (!stillEnabled)
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 经过 {attempt} 次尝试后 AutoHook 已完全禁用");
                    return;
                }

                GatherBuddy.Log.Debug($"[AutoGather] AutoHook 仍处于启用状态 (插件={pluginOn}, 自动启动={autoStartOn})," +
                              $"第 {attempt}/{maxAttempts} 次尝试 - 发送 IPC 禁用");

                AutoHook.SetAutoStartFishing?.Invoke(false);
                AutoHook.SetPluginState?.Invoke(false);

                if (attempt >= maxAttempts)
                {
                    GatherBuddy.Log.Warning("[AutoGather] 超过最大尝试次数后未能完全禁用 AutoHook");
                    return;
                }

                TaskManager.Enqueue(TryDisableOnce, "确保 AutoHook 已禁用");
            }

            TaskManager.Enqueue(TryDisableOnce, "确保 AutoHook 已禁用");
        }


        internal void DebugClearVisited()
        {
            _activeItemList.DebugClearVisited();
        }

        internal void DebugMarkVisited(GatherTarget target)
        {
            _activeItemList.DebugMarkVisited(target);
        }

        private bool ValidateActiveItemsPerception()
        {
            try
            {
                var currentJob = Dalamud.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
                var isMiner = currentJob == 16;
                var isBotanist = currentJob == 17;

                if (!isMiner && !isBotanist)
                {
                    GatherBuddy.Log.Debug($"[AutoGather] 启用时跳过鉴别力验证 - 玩家职业非采矿工或园艺工 (当前职业: {currentJob})");
                    return true;
                }

                var playerPerception = DiscipleOfLand.Perception;
                var insufficientPerception = new List<(string Name, int Required)>();

                if (_activeItemList.GetType()
                    .GetField("_listsManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.GetValue(_activeItemList) is not AutoGatherListsManager listsManager)
                {
                    return true;
                }

                foreach (var (item, _) in listsManager.ActiveItems)
                {
                    if (item is not Gatherable gatherable)
                    {
                        continue;
                    }

                    var requiredPerception = (int)gatherable.GatheringData.PerceptionReq;
                    if (requiredPerception == 0)
                    {
                        continue;
                    }

                    var gatheringType = gatherable.GatheringType.ToGroup();
                    if (isMiner && gatheringType != GatheringType.Miner || isBotanist && gatheringType != GatheringType.Botanist)
                    {
                        continue;
                    }

                    GatherBuddy.Log.Debug($"[AutoGather] 验证 {gatherable.Name[GatherBuddy.Language]}: 需要 {requiredPerception} 鉴别力 (当前: {playerPerception})");

                    if (playerPerception < requiredPerception)
                    {
                        insufficientPerception.Add((gatherable.Name[GatherBuddy.Language], requiredPerception));
                    }
                }

                if (insufficientPerception.Count > 0)
                {
                    var itemDetails = string.Join(", ", insufficientPerception.ConvertAll(x => $"{x.Name} (需要 {x.Required})"));
                    Communicator.PrintError($"[AutoGather] 无法启用自动采集: 鉴别力不足 (当前: {playerPerception}): {itemDetails}");
                    GatherBuddy.Log.Error($"[AutoGather] 自动采集未启用: 鉴别力不足 {playerPerception}");
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                GatherBuddy.Log.Error($"[AutoGather] 验证活跃物品鉴别力时出错: {ex.Message}\n{ex.StackTrace}");
                return true;
            }
        }

        private bool ShouldWaitForAutoRetainer()
        {
            try
            {
                if (GatherBuddy.Config.AutoGatherConfig.AutoRetainerDelayForTimedNodes)
                {
                    if (_currentGatherTarget != null)
                    {
                        var target = _currentGatherTarget.Value;
                        if (target.Node?.NodeType is NodeType.Legendary or NodeType.Unspoiled)
                        {
                            return false;
                        }
                    }

                    var nextItem = _activeItemList.GetNextOrDefault();
                    if (nextItem != default)
                    {
                        if (nextItem.Node?.NodeType is NodeType.Legendary or NodeType.Unspoiled)
                        {
                            if (nextItem.Time.InRange(AdjustedServerTime) &&
                                !_activeItemList.DebugVisitedTimedLocations.ContainsKey(nextItem.Node))
                            {
                                return false;
                            }
                        }
                    }
                }

                if (AutoRetainer.GetEnabledRetainers == null || AutoRetainer.GetOfflineCharacterData == null)
                {
                    return false;
                }

                var enabledRetainers = AutoRetainer.GetEnabledRetainers();

                if (enabledRetainers == null || enabledRetainers.Count == 0)
                {
                    if (_autoRetainerMultiModeEnabled)
                    {
                        AutoRetainer.AbortAllTasks?.Invoke();
                        AutoRetainer.DisableAllFunctions?.Invoke();
                        _autoRetainerMultiModeEnabled = false;
                    }
                    return false;
                }

                var threshold = GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiModeThreshold;
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var hasRetainersReady = false;
                long? closestTime = null;

                foreach (var (cid, retainerNames) in enabledRetainers)
                {
                    if (retainerNames.Count == 0)
                    {
                        continue;
                    }

                    var charData = AutoRetainer.GetOfflineCharacterData(cid);
                    if (charData == null || charData.RetainerData == null)
                    {
                        continue;
                    }

                    if (!charData.Enabled)
                    {
                        continue;
                    }

                    foreach (var retainer in charData.RetainerData)
                    {
                        if (!retainerNames.Contains(retainer.Name))
                        {
                            continue;
                        }

                        if (!retainer.HasVenture)
                        {
                            continue;
                        }

                        var secondsRemaining = (long)retainer.VentureEndsAt - currentTime;

                        if (secondsRemaining <= 0 || secondsRemaining <= threshold)
                        {
                            hasRetainersReady = true;
                            var effectiveTime = secondsRemaining <= 0 ? 0 : secondsRemaining;
                            if (!closestTime.HasValue || effectiveTime < closestTime.Value)
                            {
                                closestTime = effectiveTime;
                            }
                        }
                    }
                }

                if (hasRetainersReady && closestTime.HasValue)
                {
                    if (!_autoRetainerMultiModeEnabled)
                    {
                        var player = Dalamud.Objects.LocalPlayer;
                        if (player != null)
                        {
                            _originalCharacterNameWorld = $"{player.Name}@{player.HomeWorld.Value.Name}";
                        }

                        AutoRetainer.EnableMultiMode?.Invoke();
                        _autoRetainerMultiModeEnabled = true;
                    }
                    AutoStatus = $"正在等待处理雇员, 剩余: ({closestTime.Value} 秒)...";
                    return true;
                }
                else
                {
                    if (_autoRetainerMultiModeEnabled)
                    {
                        AutoRetainer.AbortAllTasks?.Invoke();
                        AutoRetainer.DisableAllFunctions?.Invoke();
                        _autoRetainerMultiModeEnabled = false;
                    }

                    if (!string.IsNullOrEmpty(_originalCharacterNameWorld))
                    {
                        var currentPlayer = Dalamud.Objects.LocalPlayer;
                        if (currentPlayer != null)
                        {
                            var currentCharacter = $"{currentPlayer.Name}@{currentPlayer.HomeWorld.Value.Name}";
                            if (currentCharacter != _originalCharacterNameWorld)
                            {
                                if (Lifestream.IsBusy != null && Lifestream.IsBusy())
                                {
                                    AutoStatus = $"正在等待角色切换完成...";
                                    return true;
                                }

                                if (Lifestream.Enabled && Lifestream.ChangeCharacter != null)
                                {
                                    var parts = _originalCharacterNameWorld.Split('@');
                                    if (parts.Length == 2)
                                    {
                                        var charName = parts[0];
                                        var worldName = parts[1];

                                        AutoStatus = $"重新登录: {charName}@{worldName}...";

                                        var errorCode = Lifestream.ChangeCharacter(charName, worldName);
                                        if (errorCode == 0)
                                        {
                                            return true;
                                        }
                                        else
                                        {
                                            GatherBuddy.Log.Warning($"无法重新登录到 {_originalCharacterNameWorld}。错误代码: {errorCode}");
                                            _originalCharacterNameWorld = null;
                                        }
                                    }
                                    else
                                    {
                                        GatherBuddy.Log.Warning($"无效的角色名称格式: {_originalCharacterNameWorld}");
                                        _originalCharacterNameWorld = null;
                                    }
                                }
                                else
                                {
                                    GatherBuddy.Log.Warning("无法重新登录 - Lifestream 不可用");
                                    _originalCharacterNameWorld = null;
                                }
                            }
                            else
                            {
                                if (!Player.Available || !Player.Interactable)
                                {
                                    AutoStatus = "等待玩家准备就绪...";
                                    return true;
                                }

                                _originalCharacterNameWorld = null;
                            }
                        }

                        return true;
                    }

                    return false;
                }
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"检查 AutoRetainer 探险计时失败: {e.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            ResetFishingApproachState();
            _currentRequestedTarget = null;
            _advancedUnstuck.Dispose();
            _activeItemList.Dispose();
            _diadem?.Dispose();
            Dalamud.Chat.CheckMessageHandled -= OnMessageHandled;
            Dalamud.ToastGui.QuestToast -= OnQuestToast;
            //Dalamud.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "Gathering", OnGatheringFinalize);
        }
    }
}
