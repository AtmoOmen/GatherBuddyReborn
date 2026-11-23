using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using GatherBuddy.Helpers;
using GatherBuddy.Plugin;
using System;
using System.Numerics;

namespace GatherBuddy.AutoGather.Movement
{
    public sealed class AdvancedUnstuck : IDisposable
    {
        private const double UnstuckDuration = 1.0;

        private readonly OverrideMovement _movementController = new();
        private readonly NavigationProgress _progress = new();
        private DateTime _unstuckStart;
        private DateTime _lastJumpAttempt;

        public bool IsRunning => _movementController.Enabled;

        internal void ResetTracking() => _progress.Reset();

        public bool Check(Vector3 destination, bool isPathing)
        {
            if (IsRunning)
            {
                return false;
            }

            var now = DateTime.Now;

            if (destination == default || !isPathing || (now - _unstuckStart).TotalSeconds < GatherBuddy.Config.AutoGatherConfig.NavResetCooldown)
            {
                ResetTracking();
                return true;
            }

            var stationarySeconds = _progress.Observe(Player.Position, Environment.TickCount64);
            if (stationarySeconds > GatherBuddy.Config.AutoGatherConfig.NavResetThreshold)
            {
                GatherBuddy.Log.Warning($"高级脱困: 角色连续 {stationarySeconds:F1} 秒停留在 {Player.Position}");
                ResetTracking();
                if ((now - _lastJumpAttempt).TotalSeconds > GatherBuddy.Config.AutoGatherConfig.NavResetCooldown * 2.0 && IsJumpPossible())
                {
                    _lastJumpAttempt = now;
                    Jump();
                }
                else
                {
                    Start();
                }
            }

            return !IsRunning;
        }

        public void Force()
        {
            if (!IsRunning)
            {
                GatherBuddy.Log.Warning("高级脱困: 强制启动");
                Start();
            }
        }

        public void ForceFishing()
        {
            if (!IsRunning)
            {
                GatherBuddy.Log.Warning("高级脱困: 为钓鱼强制启动（寻找可降落位置）");
                StartFishing();
            }
        }

        private unsafe bool IsJumpPossible()
        {
            if (Dalamud.Conditions[ConditionFlag.InFlight] || Dalamud.Conditions[ConditionFlag.Diving] || Dalamud.Conditions[ConditionFlag.Jumping])
            {
                return false;
            }

            var amInstance = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (amInstance == null)
            {
                return false;
            }

            var actionStatus = amInstance->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 2);
            return actionStatus == 0;
        }

        private unsafe void Jump()
        {
            GatherBuddy.Log.Debug($"高级脱困: 尝试跳跃");

            var amInstance = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();

            amInstance->UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 2);
        }

        private void Start()
        {
            var rng = new Random();
            float rnd() => (rng.Next(2) == 0 ? -1 : 1) * rng.NextSingle();
            var newPosition = Player.Position + Vector3.Normalize(new Vector3(rnd(), rnd(), rnd())) * 25f;
            _movementController.DesiredPosition = newPosition;
            _movementController.Enabled = true;
            _unstuckStart = DateTime.Now;
            Dalamud.Framework.Update += RunningUpdate;
        }

        private void StartFishing()
        {
            if (!VNavmesh.Enabled || VNavmesh.Query.Mesh.PointOnFloor == null)
            {
                GatherBuddy.Log.Error("vNavmesh 网格查询不可用，无法安全为钓鱼脱困");
                return;
            }

            var rng = new Random();
            Vector3 landablePosition = default;

            for (var distance = 15; distance <= 60; distance += 5)
            {
                for (var angleStep = 0; angleStep < 16; angleStep++)
                {
                    var angle = (float)(angleStep * Math.PI * 2 / 16);
                    var offset = new Vector3(
                        (float)Math.Cos(angle) * distance,
                        0,
                        (float)Math.Sin(angle) * distance
                    );

                    var testPosition = Player.Position + offset;

                    try
                    {
                        var floorPosition = VNavmesh.Query.Mesh.PointOnFloor.Invoke(testPosition, true, 50f);

                        if (floorPosition.HasValue && floorPosition.Value != default)
                        {
                            var distanceToFloor = Vector3.Distance(floorPosition.Value, Player.Position);
                            var heightDiff = Math.Abs(floorPosition.Value.Y - testPosition.Y);

                            if (distanceToFloor > 10f && heightDiff < 30f)
                            {
                                landablePosition = floorPosition.Value;
                                GatherBuddy.Log.Information($"[钓鱼脱困] 找到可降落位置，距离 {distanceToFloor:F1}y（高度差: {heightDiff:F1}y）");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GatherBuddy.Log.Debug($"查询网格时出错，距离 {distance}，角度 {angleStep}: {ex.Message}");
                    }
                }

                if (landablePosition != default)
                {
                    break;
                }
            }

            if (landablePosition == default)
            {
                GatherBuddy.Log.Error("[钓鱼脱困] 严重: 在 60y 范围内找不到任何可降落位置。需要手动干预");
                return;
            }

            _movementController.DesiredPosition = landablePosition;
            _movementController.Enabled = true;
            _unstuckStart = DateTime.Now;
            Dalamud.Framework.Update += RunningUpdate;
        }

        private void RunningUpdate(IFramework framework)
        {
            if (DateTime.Now.Subtract(_unstuckStart).TotalSeconds > UnstuckDuration)
            {
                Stop();
            }
        }

        public void Cancel()
            => Stop();

        private void Stop()
        {
            _movementController.Enabled = false;
            ResetTracking();
            Dalamud.Framework.Update -= RunningUpdate;
        }

        public void Dispose()
        {
            Stop();
            _movementController.Dispose();
        }
    }
}
