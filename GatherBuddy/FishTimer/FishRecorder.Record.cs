using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using GatherBuddy.Classes;
using GatherBuddy.Helpers;
using GatherBuddy.Utilities;
using GatherBuddy.Enums;
using GatherBuddy.FishTimer.Parser;
using GatherBuddy.Models;
using GatherBuddy.SeFunctions;
using GatherBuddy.Structs;
using GatherBuddy.Time;
using Lumina.Excel.Sheets;
using FishingSpot = GatherBuddy.Classes.FishingSpot;

namespace GatherBuddy.FishTimer;

public partial class FishRecorder
{
    public const int DeadLureTiming    = 5000;
    public const int InvalidLureTiming = DeadLureTiming + 500;

    [Flags]
    internal enum CatchSteps
    {
        None           = 0x00,
        BeganFishing   = 0x01,
        IdentifiedSpot = 0x02,
        FishBit        = 0x04,
        FishCaught     = 0x08,
        Mooch          = 0x10,
        FishReeled     = 0x20,
        NoMoreHook     = 0x40,
    }

    public readonly   FishingParser Parser;
    internal          CatchSteps    Step      = 0;
    internal          FishingState  LastState = FishingState.None;
    internal readonly Stopwatch     Timer     = new();
    internal readonly Stopwatch     LureTimer = new();
    private           byte          _currentLureStack;
    public event System.Action      UsedLure;

    public Fish? LastCatch;

    public FishRecord Record = new FishRecord();

    private static Bait GetCurrentBait()
    {
        if (GatherBuddy.EventFramework.CurrentSwimBait is { } fishId)
        {
            if (GatherBuddy.GameData.Fishes.TryGetValue(fishId, out var fish))
                return new Bait(fish.ItemData);

            GatherBuddy.Log.Error($"ID 为 {fishId} 的物品不是可用作钓饵的已知鱼类");
            return Bait.Unknown;
        }

        var baitId = GatherBuddy.CurrentBait.Current;
        if (GatherBuddy.GameData.Bait.TryGetValue(baitId, out var bait))
            return bait;

        GatherBuddy.Log.Error($"ID 为 {baitId} 的物品不是已知类型的钓饵");
        return Bait.Unknown;
    }

    private void CheckBuffs()
    {
        if (Dalamud.Objects.LocalPlayer?.StatusList is not {} statusList)
            return;

        foreach (var buff in statusList)
        {
            Record.Flags |= buff.StatusId switch
            {
                761  => Effects.Snagging,
                763  => Effects.Chum,
                568  => Effects.Intuition,
                762  => Effects.FishEyes,
                1804 => Effects.IdenticalCast,
                1803 => Effects.SurfaceSlap,
                2780 => Effects.PrizeCatch,
                3907 => Effects.BigGameFishing,
                850  => Effects.Patience,
                765  => Effects.Patience2,
                _    => Effects.None,
            };
        }

        if (Record.Flags.HasFlag(Effects.Patience)
         && Record.Flags.HasFlag(Effects.Patience2))
            Record.Flags &= ~Effects.Patience;
    }

    private void UpdateLure()
    {
        if (Dalamud.Objects.LocalPlayer?.StatusList is not { } statusList)
            return;

        foreach (var buff in statusList)
        {
            Record.Flags |= buff.StatusId switch
            {
                3972 when buff.Param == 1 => Effects.AmbitiousLure1,
                3972 when buff.Param == 2 => Effects.AmbitiousLure2,
                3972 when buff.Param == 3 => Effects.AmbitiousLure1 | Effects.AmbitiousLure2,
                3973 when buff.Param == 1 => Effects.ModestLure1,
                3973 when buff.Param == 2 => Effects.ModestLure2,
                3973 when buff.Param == 3 => Effects.ModestLure1 | Effects.ModestLure2,
                _                         => Effects.None,
            };
        }

        if (Record.Flags.HasLure() && LureTimer.ElapsedMilliseconds >= InvalidLureTiming)
            Record.Flags |= Effects.ValidLure;
        LureTimer.Stop();
    }

    private static readonly uint GatheringIdx =
        Dalamud.GameData.GetExcelSheet<BaseParam>((ClientLanguage)4).Cast<BaseParam?>()
            .FirstOrDefault(r => r!.Value.Name == "获得力")?.RowId // 修改为中文
     ?? 72;

    private static readonly uint PerceptionIdx =
        Dalamud.GameData.GetExcelSheet<BaseParam>((ClientLanguage)4).Cast<BaseParam?>()
            .FirstOrDefault(r => r!.Value.Name == "鉴别力")?.RowId // 修改为中文
     ?? 73;

    private static int GetContentHash(ulong id)
    {
        var lower = ((id << 5) - id) & 0xFFFFFFFF;
        var upper = ((id << 4) + id) >> 32;
        return (int)((lower & 0x45551555) | ((upper ^ lower) & 0x2AAA8AAA) | 0x10002000);
    }

    private unsafe void CheckStats()
    {
        var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (uiState == null)
            return;

        Record.Gathering     = (ushort)uiState->PlayerState.Attributes[(int)GatheringIdx];
        Record.Perception    = (ushort)uiState->PlayerState.Attributes[(int)PerceptionIdx];
    }


    private void Reset()
    {
        LastCatch = Record.Catch ?? LastCatch;
        Record = new FishRecord()
        {
            Flags = Effects.Valid,
        };
        Step             = CatchSteps.None;
        Record.TimeStamp = TimeStamp.Epoch;
        Timer.Reset();
        LureTimer.Reset();
        _currentLureStack = 0;
    }

    private void SubscribeToParser()
    {
        Parser.BeganFishing   += OnBeganFishing;
        Parser.BeganMooching  += OnMooch;
        Parser.CaughtFish     += OnCatch;
        Parser.IdentifiedSpot += OnIdentification;
        Parser.HookedIn       += OnHooking;
    }

    private void OnBeganFishing(FishingSpot? spot)
    {
        Reset();
        Record.TimeStamp = GatherBuddy.Time.ServerTime;
        Timer.Start();
        Step = CatchSteps.BeganFishing;
        CheckBuffs();
        CheckStats();
        Record.Bait          = GetCurrentBait();
        Record.FishingSpot   = spot;
        Record.Position      = Player.Position;
        Record.RotationAngle = new Angle(Player.Rotation);
        var worldName = Player.CurrentWorld;
        if (worldName != null)
        {
            var world = Dalamud.GameData.GetExcelSheet<World>().FirstOrDefault(w => w.Name.ToString().Equals(worldName, StringComparison.OrdinalIgnoreCase));
            Record.World = world;
        }
        if (Record.HasSpot)
            Step |= CatchSteps.IdentifiedSpot;

        GatherBuddy.Log.Verbose($"开始在 {spot?.Name ?? "未知钓场"} 钓鱼，使用 {Record.Bait.Name}");
    }

    private void OnBite()
    {
        Timer.Stop();
        UpdateLure();
        Record.SetTugHook(GatherBuddy.TugType.Bite, Record.Hook);
        Step                 |= CatchSteps.FishBit;
        if (LureTimer.ElapsedMilliseconds > 0)
            GatherBuddy.Log.Verbose(
                $"鱼在 {Timer.ElapsedMilliseconds} ms 后咬钩，咬钩类型 {Record.Tug}。距离上次撒饵: {LureTimer.ElapsedMilliseconds} ms");
        else
            GatherBuddy.Log.Verbose($"鱼在 {Timer.ElapsedMilliseconds} ms 后咬钩，咬钩类型 {Record.Tug}");
    }

    private void OnIdentification(FishingSpot spot)
    {
        Record.FishingSpot =  spot;
        Step               |= CatchSteps.IdentifiedSpot;
        GatherBuddy.Log.Verbose($"识别出之前未知的钓场为 {spot.Name}");
    }

    private void OnHooking(HookSet hook)
    {
        if (!Step.HasFlag(CatchSteps.FishReeled) && !Step.HasFlag(CatchSteps.NoMoreHook))
        {
            Record.SetTugHook(Record.Tug, hook);
            GatherBuddy.Log.Verbose($"使用 {hook} 提钩，咬钩类型 {Record.Tug}");
        }
    }

    private void OnCatch(Fish fish, ushort size, byte amount, bool large, bool collectible)
    {
        Step            |= CatchSteps.FishCaught;
        Record.Catch    =  fish;
        Record.Size     =  size;
        Record.Amount   =  amount;
        if (large)
            Record.Flags |= Effects.Large;
        if (collectible)
            Record.Flags |= Effects.Collectible;
        GatherBuddy.Log.Verbose(
            $"钓到了 {amount} 条 {(large ? "大型 " : string.Empty)}{(collectible ? "收藏品 " : string.Empty)}{Record.Catch.Name[(ClientLanguage)4]}，大小 {size / 10f:F1}");
    }

    private void OnMooch()
    {
        var spot = Record.FishingSpot;
        Reset();
        Record.TimeStamp = GatherBuddy.Time.ServerTime;
        Timer.Start();
        Step = CatchSteps.BeganFishing | CatchSteps.Mooch;
        CheckBuffs();
        CheckStats();
        Record.Bait        = LastCatch != null ? new Bait(LastCatch.ItemData) : GetCurrentBait();
        Record.FishingSpot = spot;
        if (Record.HasSpot)
            Step |= CatchSteps.IdentifiedSpot;
        GatherBuddy.Log.Verbose($"使用 {Record.Bait.Name} 以小钓大，位置 {spot?.Name ?? "未知钓场"}");
    }

    private void OnFishingStop()
    {
        if (Timer.IsRunning)
        {
            Timer.Stop();
            return;
        }

        if (!Step.HasFlag(CatchSteps.BeganFishing))
            return;

        Record.Bite = (ushort)Math.Clamp(Timer.ElapsedMilliseconds, 0, ushort.MaxValue);
        UpdateLure();
        if (!Record.VerifyValidity())
            Record.Flags &= ~Effects.Valid;

        Step = CatchSteps.None;
        if (GatherBuddy.Config.StoreFishRecords)
            Add(Record);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (GatherBuddy.Config.AutoGatherConfig.FishDataCollection && UploadTaskReady && NextRemoteRecordsUpdate < DateTime.Now)
        {
            var token = RemoteRecordsCancellationTokenSource.Token;
            RemoteRecordsUploadTask = Task.Run(() => UploadLocalRecords(token), token);
        }
        TimedSave();
        UpdateLureStatus();
        var state = GatherBuddy.EventFramework.FishingState;
        if (LastState == state)
            return;

        LastState = state;
        switch (state)
        {
            case FishingState.Bite:    OnBite(); break;
            case FishingState.Reeling: Step |= CatchSteps.FishReeled; break;
            case FishingState.PoleReady:
            case FishingState.Quit:
                OnFishingStop();
                break;
            case FishingState.PullPoleIn:
                Step |= CatchSteps.NoMoreHook;
                break;
        }
    }

    private void UpdateLureStatus()
    {
        if (Dalamud.Objects.LocalPlayer?.StatusList.FirstOrDefault(s => s.StatusId is 3972 or 3973) is { } currentStatus
         && currentStatus.Param != _currentLureStack)
        {
            _currentLureStack = (byte)currentStatus.Param;
            UsedLure.Invoke();
        }
    }
}
