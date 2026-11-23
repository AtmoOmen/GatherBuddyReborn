using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatherBuddy.Automation;
using GatherBuddy.Classes;
using GatherBuddy.CustomInfo;
using GatherBuddy.Data;
using GatherBuddy.Helpers;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.SeFunctions;
using GatherBuddy.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Aetheryte = GatherBuddy.Classes.Aetheryte;

namespace GatherBuddy.AutoGather
{
    public partial class AutoGather
    {
        private void EnqueueDismount(bool moveAfterDismount = true)
        {
            TaskManager.Enqueue(StopNavigation);

            TaskManager.Enqueue(() => { TryDismount(moveAfterDismount); }, "解除飞行");

            TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.InFlight] && CanAct, 1000, "等待飞行状态取消");
            TaskManager.Enqueue(() =>
            {
                if (!Dalamud.Conditions[ConditionFlag.InFlight])
                    TryDismount(moveAfterDismount);
            }, "下坐骑");
            TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.Mounted] && !Dalamud.Conditions[ConditionFlag.InFlight] && CanAct,
                1000, "等待坐骑状态取消");
            if (moveAfterDismount)
            {
                EnqueueMovementAfterDismount();
            }
            else
            {
                // Fishing must stay at its selected cast position.
                TaskManager.Enqueue(() => { if (!Dalamud.Conditions[ConditionFlag.Mounted]) { TaskManager.DelayNextImmediate(500); } });
            }
        }

        private void EnqueueMovementAfterDismount()
        {
            // A short movement pulse clears the suspended state after landing on the Chinese client.
            var moved = false;
            TaskManager.Enqueue(() =>
            {
                if (Dalamud.Conditions[ConditionFlag.Mounted] || Dalamud.Conditions[ConditionFlag.InFlight])
                    return;

                Chat.ExecuteCommand("/automove on");
                moved = true;
                TaskManager.DelayNextImmediate(100);
            }, "落地后短暂移动");
            TaskManager.Enqueue(() =>
            {
                if (!moved)
                    return;

                Chat.ExecuteCommand("/automove off");
                TaskManager.DelayNextImmediate(400);
            }, "停止自动移动");
        }

        private void EnqueueMountUp()
        {
            const uint MountRouletteGeneralActionId = 9;
            var mount = GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId;
            var type = ActionType.Mount;
            if (mount == 0 || !IsMountUnlocked(mount) || !CanUseTravelAction(type, mount))
            {
                type = ActionType.GeneralAction;
                mount = MountRouletteGeneralActionId;
                if (!CanUseTravelAction(type, mount))
                {
                    return;
                }
            }

            EnqueueActionWithDelay(() =>
            {
                if (!Dalamud.Conditions[ConditionFlag.Mounted] && !Dalamud.Conditions[ConditionFlag.InFlight])
                {
                    UseTravelAction(type, mount);
                }
            });
            TaskManager.Enqueue(() => Dalamud.Conditions[ConditionFlag.Mounted], 2000);
        }

        private unsafe bool CanMount()
        {
            var am = ActionManager.Instance();
            return am->GetActionStatus(ActionType.Mount, 0) == 0;
        }

        private unsafe bool IsMountUnlocked(uint mount)
        {
            var instance = PlayerState.Instance();
            if (instance == null)
            {
                return false;
            }

            return instance->IsMountUnlocked(mount);
        }

        private static bool WithinNodeInteractionRange(Vector3 position, Vector3 node)
            => WithinNodeHorizontalRange(position, node)
            && Math.Abs(position.Y - node.Y) < 3f;

        private static bool WithinNodeHorizontalRange(Vector3 position, Vector3 node)
            => Vector2.DistanceSquared(position.ToVector2(), node.ToVector2()) < 3.5f * 3.5f;

        private static bool AboveNode(Vector3 position, Vector3 node)
            => position.Y >= node.Y && WithinNodeHorizontalRange(position, node);

        private void MoveToCloseNode(IGameObject gameObject, Gatherable targetItem, ConfigPreset config)
        {
            if (!Player.Available)
            {
                return;
            }

            if (Dalamud.Conditions[ConditionFlag.InFlight] && !Dalamud.Conditions[ConditionFlag.Diving]
                && (AboveNode(Player.Position, gameObject.Position) || WithinNodeInteractionRange(Player.Position, gameObject.Position)))
            {
                // A route may pass above the node before its last turn. Finish it first.
                if (CurrentDestination == gameObject.Position && (IsPathing || IsPathGenerating))
                    return;

                Navigate(gameObject.Position, true, nodeId: gameObject.BaseId);
                return;
            }

            if (WithinNodeInteractionRange(Player.Position, gameObject.Position))
            {
                var waitGP = targetItem.ItemData.IsCollectable && Player.Object.CurrentGp < config.CollectableMinGP;
                waitGP |= !targetItem.ItemData.IsCollectable && Player.Object.CurrentGp < config.GatherableMinGP;

                if (Dalamud.Conditions[ConditionFlag.Mounted])
                {
                    if (IsWaitingToRetryNavigation(gameObject.Position))
                    {
                        AutoStatus = "等待重试下坐骑...";
                        return;
                    }

                    EnqueueDismount();
                    TaskManager.Enqueue(() =>
                    {
                        if (Dalamud.Conditions[ConditionFlag.Mounted] && Dalamud.Conditions[ConditionFlag.InFlight] && !Dalamud.Conditions[ConditionFlag.Diving])
                        {
                            ForceLandAndDismount();
                        }
                    });
                }
                else if (waitGP)
                {
                    StopNavigation();
                    AutoStatus = "等待采集 GP 恢复...";
                }
                else
                {
                    // Use consumables with cast time just before gathering a node when player is surely not mounted
                    if (GetConsumablesWithCastTime(config) is var consumable and > 0)
                    {
                        if (IsPathing)
                        {
                            StopNavigation();
                        }
                        else
                        {
                            EnqueueActionWithDelay(() => UseItem(consumable));
                        }
                    }
                    else
                    {
                        // Check perception requirement before interacting with node
                        if (DiscipleOfLand.Perception < targetItem.GatheringData.PerceptionReq)
                        {
                            Communicator.PrintError($"鉴别力不足无法采集该物品. 需要: {targetItem.GatheringData.PerceptionReq}, 当前: {DiscipleOfLand.Perception}");
                            AbortAutoGather();
                            return;
                        }

                        if (!Dalamud.Conditions[ConditionFlag.InFlight] && gameObject.IsTargetable)
                        {
                            StopNavigation();
                            EnqueueNodeInteraction(gameObject, targetItem);
                        }
                    }
                }
            }
            else
            {
                Navigate(gameObject.Position, ShouldFly(gameObject.Position), nodeId: gameObject.BaseId);
            }
        }

        private void ForceLandAndDismount()
        {
            var floor = VNavmesh.Query.Mesh.NearestPoint(Player.Position, 5, 5);
            if (floor != null)
            {
                Navigate(floor.Value, true, direct: true);
                TaskManager.Enqueue(() => !IsPathGenerating);
                TaskManager.DelayNext(50);
                TaskManager.Enqueue(() => !IsPathing, 1000);
                EnqueueDismount();
            }
            // If even that fails, do advanced unstuck
            TaskManager.Enqueue(() => { if (Dalamud.Conditions[ConditionFlag.Mounted]) { _advancedUnstuck.Force(); } });
        }

        private void MoveToCloseSpearfishingNode(IGameObject gameObject, Classes.Fish targetFish, FishingSpot expectedSpot)
        {
            if (WithinNodeInteractionRange(Player.Position, gameObject.Position))
            {
                if (Dalamud.Conditions[ConditionFlag.Mounted])
                {
                    EnqueueDismount();
                }
                else
                {
                    EnqueueSpearfishingNodeInteraction(gameObject, targetFish, expectedSpot);
                }

                if (!Dalamud.Conditions[ConditionFlag.Diving])
                {
                    TaskManager.Enqueue(() => { if (!Dalamud.Conditions[ConditionFlag.Gathering]) { Navigate(gameObject.Position, false, nodeId: gameObject.BaseId, underwaterNode: true); } });
                }
            }
            else if (Vector2.Distance(gameObject.Position.ToVector2(), Player.Position.ToVector2()) < Math.Max(GatherBuddy.Config.AutoGatherConfig.MountUpDistance, 5)
                && !Dalamud.Conditions[ConditionFlag.InFlight] && !Dalamud.Conditions[ConditionFlag.Diving])
            {
                Navigate(gameObject.Position, false, nodeId: gameObject.BaseId, underwaterNode: true);
            }
            else
            {
                Navigate(gameObject.Position, ShouldFly(gameObject.Position), nodeId: gameObject.BaseId, underwaterNode: true);
            }
        }

        private static bool MountTransition
            => Dalamud.Conditions[ConditionFlag.Mounting] || Dalamud.Conditions[ConditionFlag.Mounting71]
            || Dalamud.Conditions[ConditionFlag.MountOrOrnamentTransition];

        private static bool NavigationMovementAllowed => !MountTransition && VNavmesh.Path.GetMovementAllowed();

        private void ResetNavigationProgress()
        {
            _advancedUnstuck.ResetTracking();
            _fishingApproachProgress.Reset();
            _waypointProgress.Reset();
        }

        private bool CheckActiveWaypoint(bool isPathing, bool isPathGenerating)
        {
            if (CurrentDestination == default || !isPathing
                || (isPathGenerating && _navState.stage != PathfindingStage.RetryCombinedPathfinding)
                || !NavigationMovementAllowed)
            {
                _waypointProgress.Reset();
                return false;
            }

            var now = Environment.TickCount64;
            if (!_waypointProgress.ShouldSample(now))
                return false;

            var remaining = VNavmesh.Path.ListWaypoints();
            if (remaining.Count == 0)
            {
                _waypointProgress.Reset();
                return false;
            }

            var player = Player.Position;
            var waypoint = remaining[0];
            var distance = _navState.flying && !_navState.mountingUp
                ? Vector3.Distance(player, waypoint)
                : Vector2.Distance(player.ToVector2(), waypoint.ToVector2());
            var timeout = Math.Max(8f, 3 * GatherBuddy.Config.AutoGatherConfig.NavResetThreshold);
            var stalled = _waypointProgress.IsStalled(waypoint, distance, now, timeout);
            if (!stalled && Vector3.DistanceSquared(remaining[^1], _navState.activePathEnd) < 1f)
                return false;

            var reason = stalled ? "未接近当前路径点" : "执行中的路径终点不匹配";
            GatherBuddy.Log.Warning($"{reason}，角色={player}，下一路径点={waypoint}，剩余点数={remaining.Count}，实际终点={remaining[^1]}，已接受终点={_navState.activePathEnd}");
            if (_fishingApproachTarget is { } fish && CurrentDestination == _fishingApproachPosition)
                FailFishingApproach(fish, reason, "正在寻找可抵达的钓点...", true, false);
            else
            {
                StopNavigation();
                AutoStatus = "正在从当前位置重新寻路...";
            }

            return true;
        }

        private void StopNavigation()
        {
            _fishingApproachPathObserved = false;
            StopPathfinding();

            _navState = default;
            if (VNavmesh.Enabled)
            {
                VNavmesh.Path.Stop();
            }
        }

        private void StopPathfinding()
        {
            ResetNavigationProgress();
            if (_navState.cts != null && _navState.task != null)
            {
                var cts = _navState.cts;
                cts.Cancel();
                _navState.task.ContinueWith(_ => cts.Dispose());
                _navState.task = null;
                _navState.cts = null;
            }
        }

        private unsafe void SetRotation(Angle angle)
        {
            if (!Player.Available)
            {
                return;
            }

            var playerObject = (GameObject*)Player.Object.Address;
            GatherBuddy.Log.Debug($"设置朝向角度为 {angle.Rad}");
            playerObject->SetRotation(angle.Rad);
        }

        private bool IsWaitingToRetryNavigation(Vector3 destination)
            => _failedPath is { } failure && failure.Destination == destination
            && Environment.TickCount64 < failure.RetryAt && Vector3.DistanceSquared(failure.From, Player.Position) < 9f;

        private void Navigate(Vector3 destination, bool shouldFly, bool direct = false, uint? nodeId = null, bool underwaterNode = false)
        {
            if (!NavReady)
            {
                AutoStatus = "等待 vnavmesh 导航网格就绪...";
                return;
            }

            if (IsWaitingToRetryNavigation(destination))
            {
                AutoStatus = "未找到可用路径，等待重试...";
                return;
            }
            _failedPath = null;

            var canMount = Vector2.Distance(destination.ToVector2(), Player.Position.ToVector2()) >= GatherBuddy.Config.AutoGatherConfig.MountUpDistance && CanMount();
            if (!Dalamud.Conditions[ConditionFlag.Mounted] && canMount)
            {
                EnqueueMountUp();
                if (!GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting)
                {
                    StopNavigation();
                    return;
                }
            }

            var landingDistance = GatherBuddy.Config.AutoGatherConfig.LandingDistance;

            if (_navState.destination == destination && _navState.stage == PathfindingStage.RetryCombinedPathfinding
                && _navState.task == null && Environment.TickCount64 - _navState.lastTry > 1000)
            {
                _navState.lastTry = Environment.TickCount64;
                _navState.cts = new CancellationTokenSource();
                _navState.retryStart = Player.Position;
                _navState.task = FindCombinedPath(_navState.retryStart, destination, landingDistance, Dalamud.Conditions[ConditionFlag.InFlight], _navState.cts.Token, _navState.interactionNode, _navState.landAtNode);
                GatherBuddy.Log.Debug($"重新尝试组合寻路到 {destination}");
                return;
            }

            if (_navState.destination == destination && (IsPathing || _navState.task != null))
            {
                return;
            }

            StopNavigation();

            shouldFly &= canMount || Dalamud.Conditions[ConditionFlag.Mounted];
            shouldFly |= Dalamud.Conditions[ConditionFlag.Diving];

            var offsettedDestination = GetCorrectedDestination(destination, Player.Position, nodeId);
            _navState.destination = destination;
            _navState.pathDestination = offsettedDestination;
            _navState.interactionNode = nodeId.HasValue ? destination : null;
            _navState.landAtNode = nodeId.HasValue && !underwaterNode && !Dalamud.Conditions[ConditionFlag.Diving];
            if (_navState.landAtNode && WithinNodeHorizontalRange(Player.Position, destination))
            {
                if (!Dalamud.Conditions[ConditionFlag.InFlight])
                    shouldFly = false;
                else if (AboveNode(Player.Position, destination) || WithinNodeInteractionRange(Player.Position, destination))
                {
                    BeginNodeLanding(null);
                    return;
                }
            }
            _navState.flying = shouldFly;
            _navState.mountingUp = shouldFly && !Dalamud.Conditions[ConditionFlag.Mounted] && !Dalamud.Conditions[ConditionFlag.Diving];
            _navState.direct = direct || !shouldFly || landingDistance == 0 || destination != offsettedDestination || Dalamud.Conditions[ConditionFlag.Diving];
            _navState.offset = destination != offsettedDestination;
            StartPathfinding(landingDistance);
        }

        private void StartPathfinding(float landingDistance)
        {
            _navState.cts = new CancellationTokenSource();
            _navState.stage = PathfindingStage.InitialCombinedPathfinding;
            _navState.retriedDirect = false;
            if (_navState.direct)
            {
                _navState.task = FindDirectPath(Player.Position, _navState.pathDestination, _navState.flying, _navState.cts.Token, _navState.interactionNode, _navState.landAtNode);
            }
            else
            {
                _navState.lastTry = Environment.TickCount64;
                _navState.task = FindCombinedPath(Player.Position, _navState.pathDestination, landingDistance, Dalamud.Conditions[ConditionFlag.InFlight], _navState.cts.Token, _navState.interactionNode, _navState.landAtNode);
            }
        }

        private void HandlePathfinding()
        {
            if (_navState.destination == default)
            {
                return;
            }

            if (!NavReady)
            {
                StopNavigation();
                AutoStatus = "等待 vnavmesh 导航网格就绪...";
                return;
            }

            var player = Player.Position;
            if (_navState.landing is { } landing)
            {
                UpdateNodeLanding(landing);
                return;
            }
            if (_navState.landAtNode && _navState.flying && !_navState.mountingUp && _navState.groundWaypointCount == 0
                && _navState.task == null && Dalamud.Conditions[ConditionFlag.InFlight] && !IsPathing
                && (AboveNode(player, CurrentDestination) || WithinNodeInteractionRange(player, CurrentDestination)))
            {
                BeginNodeLanding(null);
                return;
            }
            if (_navState.mountingUp && Dalamud.Conditions[ConditionFlag.Mounted])
            {
                _navState.mountingUp = false;
                if (_navState.task == null || IsPathing)
                {
                    // Ground movement ignores height and may already have consumed an ascent waypoint.
                    VNavmesh.Path.Stop();
                    StopPathfinding();
                    StartPathfinding(GatherBuddy.Config.AutoGatherConfig.LandingDistance);
                    return;
                }
            }

            if (_navState.flying && !_navState.mountingUp && _navState.stage == PathfindingStage.Done && _navState.groundWaypointCount > 0
                && !Dalamud.Conditions[ConditionFlag.Diving]
                && VNavmesh.Path.NumWaypoints() <= _navState.groundWaypointCount)
            {
                List<Vector3> remaining = [.. VNavmesh.Path.ListWaypoints()];
                _navState.flying = false;
                _waypointProgress.Reset();
                if (remaining.Count > 0)
                {
                    if (Vector3.DistanceSquared(remaining[^1], _navState.activePathEnd) >= 1f)
                    {
                        StopNavigation();
                        return;
                    }

                    const float landingTolerance = 3f;
                    if (_navState.landAtNode && WithinNodeHorizontalRange(player, _navState.landingPoint))
                    {
                        BeginNodeLanding(remaining);
                        return;
                    }
                    if (Dalamud.Conditions[ConditionFlag.InFlight]
                        && Vector3.DistanceSquared(player, _navState.landingPoint) > landingTolerance * landingTolerance)
                    {
                        GatherBuddy.Log.Warning($"飞行路径点已耗尽但角色尚未到达落点，重新寻路。角色={player}，计划落点={_navState.landingPoint}");
                        StopNavigation();
                        return;
                    }

                    VNavmesh.Path.Stop();
                    VNavmesh.Path.MoveTo(remaining, false);
                    TryDismount();
                }
                return;
            }

            if (_navState.task == null || _navState.cts == null || !_navState.task.IsCompleted)
            {
                return;
            }

            NavigationPath? route;
            try
            {
                route = _navState.task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                StopNavigation();
                return;
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Error($"寻路任务抛出异常: {ex.Message}");
                route = null;
            }
            _navState.cts.Dispose();
            _navState.task = null;
            _navState.cts = null;

            if (route != null && _navState.offset && !WithinNodeInteractionRange(route.Waypoints[^1], _navState.destination)
                && !(_navState.landAtNode && _navState.flying && AboveNode(route.Waypoints[^1], _navState.destination)))
            {
                GatherBuddy.Log.Warning($"路径终点 {route.Waypoints[^1]} 超出采集点 {_navState.destination} 的交互范围，偏移目标={_navState.pathDestination}");
                route = null;
            }

            if (route == null)
            {
                if (_navState.direct || _navState.stage == PathfindingStage.FallbackDirectPathfinding)
                {
                    if (!_navState.retriedDirect)
                    {
                        _navState.retriedDirect = true;
                        _navState.cts = new CancellationTokenSource();
                        var token = _navState.cts.Token;
                        _navState.task = _navState.flying && _navState.interactionNode is { } node && !Dalamud.Conditions[ConditionFlag.Diving]
                            ? RetryNodeFlight(player, node, Dalamud.Conditions[ConditionFlag.InFlight], token, _navState.landAtNode)
                            : FindDirectPath(player, _navState.pathDestination, _navState.flying, token, _navState.interactionNode, _navState.landAtNode);
                        GatherBuddy.Log.Debug($"直接寻路失败，从当前位置重新计算到 {_navState.pathDestination} 的路径");
                        return;
                    }

                    if (_navState.flying && _navState.interactionNode.HasValue
                        && !Dalamud.Conditions[ConditionFlag.InFlight] && !Dalamud.Conditions[ConditionFlag.Diving])
                    {
                        _navState.flying = _navState.mountingUp = false;
                        _navState.direct = true;
                        _navState.stage = PathfindingStage.InitialCombinedPathfinding;
                        _navState.pathDestination = _navState.destination;
                        _navState.offset = false;
                        _navState.cts = new CancellationTokenSource();
                        _navState.task = FindDirectPath(player, _navState.destination, false, _navState.cts.Token, _navState.interactionNode, _navState.landAtNode);
                        GatherBuddy.Log.Debug("飞行寻路失败，角色仍在地面，尝试地面路线");
                        return;
                    }

                    GatherBuddy.Log.Warning($"未找到可执行路径，角色={player}，请求终点={_navState.pathDestination}，目标={CurrentDestination}，飞行={_navState.flying}");
                    if (_fishingApproachTarget is { } fish && CurrentDestination == _fishingApproachPosition)
                    {
                        FailFishingApproach(fish, "未找到通往钓位的路径", "正在寻找可抵达的钓点...", false, false);
                        return;
                    }
                    _failedPath = (CurrentDestination, player, Environment.TickCount64 + 5000);
                    StopNavigation();
                    AutoStatus = "未找到可用路径，等待重试...";
                }
                else if (_navState.stage == PathfindingStage.InitialCombinedPathfinding)
                {
                    GatherBuddy.Log.Debug($"VNavmesh 未能找到组合路径，回退到直接路径");
                    _navState.stage++;
                    _navState.cts = new CancellationTokenSource();
                    _navState.task = FindDirectPath(player, _navState.destination, _navState.flying, _navState.cts.Token, _navState.interactionNode, _navState.landAtNode);
                }
                else if (_navState.stage == PathfindingStage.RetryCombinedPathfinding)
                {
                    if (!IsPathing)
                        StopNavigation();
                }
                else
                {
                    GatherBuddy.Log.Error($"BUG: 在非预期阶段 {_navState.stage} 寻路失败");
                    AbortAutoGather();
                }
            }
            else
            {
                var path = route.Waypoints;
                if (_navState.stage == PathfindingStage.RetryCombinedPathfinding
                    && !TryJoinFlightLeg(path, route.GroundWaypointCount, _navState.retryStart, player))
                {
                    // Keep the current fallback only while it is still running.
                    if (!IsPathing)
                        StopNavigation();
                    return;
                }

                if (route.GroundWaypointCount > 0)
                {
                    _navState.stage = PathfindingStage.Done;
                    _navState.groundWaypointCount = route.GroundWaypointCount;
                    _navState.landingPoint = path[^(route.GroundWaypointCount + 1)];
                }
                else if (_navState.landAtNode && _navState.flying && AboveNode(path[^1], CurrentDestination)
                    && !WithinNodeInteractionRange(path[^1], CurrentDestination))
                {
                    _navState.stage = PathfindingStage.Done;
                }
                else if (_navState.stage == PathfindingStage.FallbackDirectPathfinding)
                {
                    _navState.stage = PathfindingStage.RetryCombinedPathfinding;
                }

                VNavmesh.Path.Stop();
                _navState.activePathEnd = path[^1];
                VNavmesh.Path.MoveTo(path, _navState.flying && !_navState.mountingUp);
                GatherBuddy.Log.Debug($"开始导航，目标={_navState.destination}，寻路目标={_navState.pathDestination}，路径终点={path[^1]}，路径点={path.Count}");
            }
        }

        private static bool TryJoinFlightLeg(List<Vector3> path, int groundCount, Vector3 start, Vector3 player)
        {
            // Only skip points when the player is actually on a segment of the replacement route.
            for (var i = 0; i < path.Count - groundCount; ++i)
            {
                var end = path[i];
                var delta = end - start;
                var lengthSquared = delta.LengthSquared();
                var t = lengthSquared == 0 ? 0 : Math.Clamp(Vector3.Dot(player - start, delta) / lengthSquared, 0f, 1f);
                if (Vector3.DistanceSquared(player, start + t * delta) <= 1f)
                {
                    path.RemoveRange(0, i);
                    return true;
                }

                start = end;
            }

            return false;
        }

        private void BeginNodeLanding(List<Vector3>? groundPath)
        {
            StopPathfinding();
            VNavmesh.Path.Stop();
            var player = Player.Position;
            _navState.landing = new NodeLanding(player.Y, groundPath);
            AutoStatus = "正在降落，等待着地...";
            if (!Dalamud.Conditions[ConditionFlag.InFlight])
                return;

            // Descend to the first landable surface under the mount, including on multi-level maps.
            var floor = VNavmesh.Query.Mesh.PointOnFloor(player, false, 1f);
            if (floor is { } point && point.Y < player.Y && Vector3.DistanceSquared(point, player) > 0.25f)
            {
                VNavmesh.Path.MoveTo([point], true);
            }
        }

        private void UpdateNodeLanding(NodeLanding landing)
        {
            if (Dalamud.Conditions[ConditionFlag.Diving])
            {
                StopNavigation();
                return;
            }

            var inFlight = Dalamud.Conditions[ConditionFlag.InFlight];
            var player = Player.Position;
            var now = Environment.TickCount64;
            AutoStatus = inFlight ? "正在降落，等待着地..." : "等待下坐骑...";
            if (!inFlight && (landing.Stage is NodeLandingStage.Descending or NodeLandingStage.LandingRequested))
            {
                // The first request in the air can land the mount without dismounting.
                VNavmesh.Path.Stop();
                landing.Stage = NodeLandingStage.Landed;
                landing.LastProgress = now;
                return;
            }

            if (!NavigationMovementAllowed || (inFlight && landing.LastHeight - player.Y >= 0.5f))
            {
                landing.LastHeight = player.Y;
                landing.LastProgress = now;
                return;
            }

            if (!inFlight && !Dalamud.Conditions[ConditionFlag.Mounted])
            {
                if (CanAct)
                    FinishNodeLanding(landing, player);
                return;
            }

            if (inFlight && landing.Stage == NodeLandingStage.Descending
                && (!IsPathing || now - landing.LastProgress >= 3000) && TryDismount())
            {
                VNavmesh.Path.Stop();
                landing.Stage = NodeLandingStage.LandingRequested;
                landing.LastProgress = now;
            }
            else if (!inFlight && landing.Stage == NodeLandingStage.Landed && TryDismount())
            {
                landing.Stage = NodeLandingStage.DismountRequested;
                landing.LastProgress = now;
            }

            if (now - landing.LastProgress < 8000)
                return;

            var failedAction = inFlight ? "降落" : "下坐骑";
            GatherBuddy.Log.Warning($"未能{failedAction}，稍后重试。角色={player}，采集点={CurrentDestination}");
            _failedPath = (CurrentDestination, player, now + 5000);
            StopNavigation();
            AutoStatus = $"未能{failedAction}，等待重试...";
        }

        private void FinishNodeLanding(NodeLanding landing, Vector3 player)
        {
            // Reuse the ground leg only if we landed on its planned surface.
            if (landing.GroundPath is { } ground && Vector3.DistanceSquared(player, _navState.landingPoint) <= 9f)
            {
                _navState.landing = null;
                _navState.flying = false;
                VNavmesh.Path.MoveTo(ground, false);
                return;
            }

            StopNavigation();
            if (landing.Stage == NodeLandingStage.DismountRequested)
                EnqueueMovementAfterDismount();
        }

        private bool TryDismount(bool moveAfterDismount = true)
            => Dalamud.Conditions[ConditionFlag.Mounted] && CanAct
            && UseTravelAction(moveAfterDismount ? ActionType.GeneralAction : ActionType.Mount, moveAfterDismount ? 23u : 0u);

        private static async Task<List<Vector3>> FindWaypoints(Vector3 start, Vector3 target, bool fly, CancellationToken token, Vector3? interactionNode = null, Vector3? overheadTarget = null)
        {
            var path = await VNavmesh.Nav.PathfindCancelable(start, target, fly, token);
            if (path.Count == 0)
                return path;

            var end = path[^1];
            var inRange = interactionNode is { } node ? WithinNodeInteractionRange(end, node) : Vector3.DistanceSquared(end, target) < 1f;
            if ((!inRange && !(overheadTarget is { } landing && AboveNode(end, landing)))
                || !path.TrueForAll(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)))
            {
                GatherBuddy.Log.Warning($"拒绝无效寻路结果，起点={start}，请求终点={target}，采集点={interactionNode}，实际终点={end}，飞行={fly}");
                return [];
            }

            return path;
        }

        private static async Task<NavigationPath?> RetryNodeFlight(Vector3 start, Vector3 node, bool inFlight, CancellationToken token, bool landAtNode)
        {
            // A node can lie inside scenery. Retry the flight to a reachable surface near it.
            var target = node;
            var floor = inFlight ? VNavmesh.Query.Mesh.PointOnFloor(start, false, 5f) : start;
            if (floor.HasValue)
            {
                var ground = await FindWaypoints(floor.Value, node, false, token, node);
                if (ground.Count > 0)
                    target = ground[^1];
            }
            return await FindDirectPath(start, target, true, token, node, landAtNode);
        }

        private static async Task<NavigationPath?> FindDirectPath(Vector3 start, Vector3 target, bool fly, CancellationToken token, Vector3? interactionNode = null, bool landAtNode = false)
        {
            var path = await FindWaypoints(start, target, fly, token, interactionNode, fly && landAtNode ? interactionNode : null);
            return path.Count == 0 ? null : new NavigationPath(path);
        }

        private static async Task<NavigationPath?> FindCombinedPath(Vector3 player, Vector3 target, float landingDistance, bool flying, CancellationToken token, Vector3? interactionNode = null, bool landAtNode = false)
        {
            var floor = flying ? VNavmesh.Query.Mesh.PointOnFloor(player, false, 5f) : player;
            if (floor == null)
                return null;

            var ground = await FindWaypoints(floor.Value, target, false, token, interactionNode);
            if (ground.Count < 2)
                return null;

            var landing = FindLandingPoint(ground, target, landingDistance);
            var meshPoint = VNavmesh.Query.Mesh.NearestPoint(landing, landingDistance, 10f);
            if (meshPoint == null || Math.Abs(target.Y - meshPoint.Value.Y) > 10f)
                return null;

            var flight = await FindWaypoints(player, meshPoint.Value, true, token, overheadTarget: landAtNode ? interactionNode : null);
            if (flight.Count == 0)
                return null;

            if (landAtNode && interactionNode is { } node && AboveNode(flight[^1], node)
                && !WithinNodeInteractionRange(flight[^1], node))
                return new NavigationPath(flight);

            // Snapping can put the landing point across a barrier from the original ground segment.
            // Ask for a connected ground leg starting at the actual end of the flight.
            var approachTarget = interactionNode.HasValue ? ground[^1] : target;
            var approach = await FindWaypoints(flight[^1], approachTarget, false, token, interactionNode);
            if (approach.Count == 0)
                return null;

            flight.AddRange(approach);
            return new NavigationPath(flight, approach.Count);
        }

        private static Vector3 FindLandingPoint(List<Vector3> ground, Vector3 target, float radius)
        {
            var radiusSquared = radius * radius;
            for (var i = ground.Count - 2; i >= 0; --i)
            {
                var start = ground[i];
                var end = ground[i + 1];
                if (Vector3.DistanceSquared(start, target) <= radiusSquared)
                    continue;

                // Select the point on this segment entering the landing sphere.
                var delta = end - start;
                var lengthSquared = delta.LengthSquared();
                if (lengthSquared == 0 || Vector3.DistanceSquared(end, target) > radiusSquared)
                    return end;

                var fromCenter = start - target;
                var b = Vector3.Dot(fromCenter, delta);
                var c = fromCenter.LengthSquared() - radiusSquared;
                var t = (-b - MathF.Sqrt(MathF.Max(0, b * b - lengthSquared * c))) / lengthSquared;
                return Vector3.Lerp(start, end, Math.Clamp(t, 0f, 1f));
            }

            return ground[0];
        }

        private static Vector3 GetCorrectedDestination(in Vector3 destination, in Vector3 player, uint? nodeId)
        {
            const float MaxHorizontalSeparation = 3.0f;
            const float MaxVerticalSeparation = 2.5f;

            if (!GatherBuddy.Config.AutoGatherConfig.DisableRandomLandingPositions
                && nodeId.HasValue
                && AutoOffsets.TryGetRandomOffset(nodeId.Value, destination, player, out var offset))
            {
                GatherBuddy.Log.Debug($"为采集点 {nodeId.Value} 使用自动偏移: {offset}。到采集点距离: {Vector2.Distance(offset.ToVector2(), destination.ToVector2()):F2}y，角度: {Math.Acos(Vector2.Dot(Vector2.Normalize((player - destination).ToVector2()), Vector2.Normalize((offset - destination).ToVector2()))) * 180.0 / Math.PI:F1}度");
                return offset;
            }

            try
            {
                float separation;
                if (WorldData.NodeOffsets.TryGetValue(destination, out offset))
                {
                    offset = VNavmesh.Query.Mesh.NearestPoint(offset, MaxHorizontalSeparation, MaxVerticalSeparation).GetValueOrDefault(offset);
                    if ((separation = Vector2.Distance(offset.ToVector2(), destination.ToVector2())) > MaxHorizontalSeparation)
                    {
                        GatherBuddy.Log.Warning($"偏移被忽略，校正网格后水平间距 {separation} 过大。允许的最大值为 {MaxHorizontalSeparation}");
                    }
                    else if ((separation = Math.Abs(offset.Y - destination.Y)) > MaxVerticalSeparation)
                    {
                        GatherBuddy.Log.Warning($"偏移被忽略，校正网格后垂直间距 {separation} 过大。允许的最大值为 {MaxVerticalSeparation}");
                    }
                    else
                    {
                        return offset;
                    }
                }

                // There was code that corrected the destination to the nearest point on the mesh, but testing showed that
                // navigating directly to the node yields better results, and points within landing distance are on the mesh anyway.
            }
            catch (Exception) { }

            return destination;
        }

        private unsafe void MoveToFishingSpot(Vector3 position, Angle angle)
        {
            Navigate(position, ShouldFly(position));
        }

        public static Aetheryte? FindClosestAetheryte(ILocation location)
        {
            var aetheryte = location.ClosestAetheryte;

            var territory = location.Territory;
            if (ForcedAetherytes.ZonesWithoutAetherytes.FirstOrDefault(x => x.ZoneId == territory.Id).AetheryteId is var alt && alt > 0)
            {
                territory = GatherBuddy.GameData.Aetherytes[alt].Territory;
            }

            if (aetheryte == null || !Teleporter.IsAttuned(aetheryte.Id) || aetheryte.Territory != territory)
            {
                aetheryte = territory.Aetherytes
                    .Where(a => Teleporter.IsAttuned(a.Id))
                    .OrderBy(a => a.WorldDistance(territory.Id, location.IntegralXCoord, location.IntegralYCoord))
                    .FirstOrDefault();
            }

            return aetheryte;
        }

        private bool MoveToTerritory(ILocation location)
        {
            var aetheryte = FindClosestAetheryte(location);
            if (aetheryte == null)
            {
                Communicator.PrintError("无法找到可用的以太之光来进行传送。");
                return false;
            }

            GatherBuddy.Log.Debug($"[MoveToTerritory] 传送到 {aetheryte.Name}");
            TaskManager.Enqueue(() => Teleporter.Teleport(aetheryte.Id) ? true : (bool?)null, $"传送至 {aetheryte.Name}");
            // A transfer can finish between frames. The aetheryte may also be in a gateway zone.
            TaskManager.Enqueue(() => Dalamud.ClientState.TerritoryType == aetheryte.Territory.Id
                                   && CanAct && GenericHelpers.IsScreenReady(),
                30000, abortOnTimeout: true, name: $"等待抵达 {aetheryte.Name}");
            TaskManager.DelayNext((int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay);

            return true;
        }
    }
}
