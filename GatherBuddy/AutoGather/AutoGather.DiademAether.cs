using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GatherBuddy.AutoGather.Helpers;
using GatherBuddy.Helpers;
using System;
using System.Linq;
using System.Numerics;

namespace GatherBuddy.AutoGather
{
    public partial class AutoGather
    {
        private const int AetherGaugeOffset = 0x268;
        private const int AetherGaugeReadyThreshold = 200;
        private const float AetherTargetScanRadius = 25f;
        private const uint AethercannonActionId = 19700;
        private DateTime _lastAetherTarget = DateTime.MinValue;
        private readonly TimeSpan _aetherDebounce = TimeSpan.FromSeconds(2);
        
        private unsafe bool IsDiademAetherGaugeReady()
        {
            var addonPtr = Dalamud.GameGui.GetAddonByName("HWDAetherGauge");
            if (addonPtr == nint.Zero)
                return false;

            var addon = (AtkUnitBase*)(nint)addonPtr;
            if (addon == null || !addon->IsVisible)
                return false;

            int currentGauge = *(int*)((nint)addonPtr + AetherGaugeOffset);
            return currentGauge >= AetherGaugeReadyThreshold;
        }
        
        private IGameObject? FindNearbyEnemyForAether()
        {
            var player = Dalamud.Objects.LocalPlayer;
            if (player == null) 
                return null;

            Vector3 pPos = player.Position;
            IGameObject? best = null;
            float bestDistSq = AetherTargetScanRadius * AetherTargetScanRadius;

            foreach (var obj in Dalamud.Objects)
            {
                if (obj is not IBattleNpc bnpc)
                    continue;

                if (!IsValidDiademEnemy(bnpc))
                    continue;

                float distSq = Vector3.DistanceSquared(pPos, bnpc.Position);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = bnpc;
                }
            }

            return best;
        }
        
        private bool IsValidDiademEnemy(IBattleNpc bnpc)
        {
            if (bnpc.IsDead)
                return false;

            if (!bnpc.IsTargetable)
                return false;

            if (bnpc.SubKind is 2 or 9)
                return false;

            return true;
        }
        
        private unsafe void TargetByGameObject(IGameObject gameObject)
        {
            var targetSystem = TargetSystem.Instance();
            if (targetSystem == null)
                return;
                
            targetSystem->Target = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gameObject.Address;
        }
        
        private unsafe bool TryUseAetherCannon()
        {
            if (!GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon)
                return false;
            if (!Diadem.IsInside)
                return false;
            if (Dalamud.Conditions[ConditionFlag.Mounted])
                return false;
            if (IsPathing)
                return false;
            if (DateTime.UtcNow - _lastAetherTarget < _aetherDebounce)
                return false;
            if (!IsDiademAetherGaugeReady())
                return false;

            var enemy = FindNearbyEnemyForAether();
            if (enemy == null)
                return false;

            var enemyId = enemy.GameObjectId;
            TargetByGameObject(enemy);
            _lastAetherTarget = DateTime.UtcNow;
            GatherBuddy.Log.Debug($"[云冠群岛] 瞄准敌人 {enemy.Name}（ID: {enemyId}），位置 {enemy.Position}");

            TaskManager.DelayNext(100);

            TaskManager.Enqueue(() =>
            {
                var currentTarget = Dalamud.Targets.Target;
                if (currentTarget == null || currentTarget.GameObjectId != enemyId)
                {
                    GatherBuddy.Log.Debug($"[云冠群岛] 目标未正确设置。当前: {currentTarget?.Name ?? "null"}");
                    return true;
                }

                GatherBuddy.Log.Debug($"[云冠群岛] 目标已确认: {currentTarget.Name}，距离: {Vector3.Distance(Player.Position, currentTarget.Position):F1}y");
                return true;
            });

            EnqueueActionWithDelay(() =>
            {
                var currentTarget = Dalamud.Targets.Target;
                if (currentTarget == null)
                {
                    GatherBuddy.Log.Debug($"[云冠群岛] 尝试开火时无目标");
                    return;
                }

                var amInstance = ActionManager.Instance();
                if (amInstance == null)
                {
                    GatherBuddy.Log.Debug($"[云冠群岛] ActionManager.Instance() 为空");
                    return;
                }

                var targetId = currentTarget.GameObjectId;
                var actionStatus = amInstance->GetActionStatus(ActionType.Action, AethercannonActionId);
                GatherBuddy.Log.Debug($"[云冠群岛] 向目标 ID {targetId} 开火，动作状态: {actionStatus}");

                if (actionStatus == 0)
                {
                    var result = amInstance->UseAction(ActionType.Action, AethercannonActionId, targetId);
                    GatherBuddy.Log.Debug($"[云冠群岛] UseAction 返回: {result}");
                }
                else
                {
                    GatherBuddy.Log.Debug($"[云冠群岛] 无法使用动作，状态代码: {actionStatus}");
                }
            });

            TaskManager.Enqueue(() => Dalamud.Conditions[ConditionFlag.Casting], 1000, "等待以太炮发射开始");
            TaskManager.Enqueue(() => !Dalamud.Conditions[ConditionFlag.Casting], 5000, "等待以太炮发射完成");
            TaskManager.DelayNext(500);
            return true;
        }
    }
}
