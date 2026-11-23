using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using GatherBuddy.AutoGather;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.Classes;
using GatherBuddy.CustomInfo;
using GatherBuddy.Enums;
using GatherBuddy.FishTimer;
using GatherBuddy.Levenshtein;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using GatherBuddy.SeFunctions;
using GatherBuddy.Structs;
using GatherBuddy.Time;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Action = System.Action;
using Aetheryte = GatherBuddy.Classes.Aetheryte;
using Effects = GatherBuddy.Models.Effects;
using FishingSpot = GatherBuddy.Classes.FishingSpot;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    [GeneratedRegex(@"(?<Name>.*) \((?<Id>\d{5})\)$", RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking)]
    private static partial Regex CosmicMissionRegex();

    private static uint _startId = 10031;
    private static uint _endId = 10096;

    private static void DrawDebugAetheryte(Aetheryte a)
    {
        DebugCell(a.Id.ToString());
        DebugCell(a.Name);
        DebugCell(a.Territory.Name);
        DebugCell($"{a.XCoord}-{a.YCoord}");
        DebugCell($"{a.XStream}-{a.YStream}-{a.Plane}");
    }

    private static void DrawDebugTerritory(Territory t)
    {
        DebugCell(t.Id.ToString());
        DebugCell(t.Name);
        DebugCell(t.SizeFactor.ToString(CultureInfo.InvariantCulture));
        DebugCell(t.WeatherRates.Rates.Length.ToString());
        DebugCell(JoinCell(", ", t.WeatherRates.Rates, static r => $"{r.Weather.Name} ({r.Weather.Id})"));
    }

    private static void DrawDebugBait(Bait b)
    {
        DebugCell(b.Id.ToString());
        DebugCell(b.Name);
    }

    private static void DrawGatherableDebug(Gatherable g)
    {
        DebugCell(g.ItemId.ToString());
        DebugCell(g.GatheringId.ToString());
        DebugCell(g.Name.English);
        DebugCell(g.LevelString());
        DebugCell(g.NodeList.Count.ToString());
    }

    private static void DrawGatheringNodeDebug(GatheringNode n)
    {
        DebugCell(n.Id.ToString());
        DebugCell(n.Name);
        DebugCell(EnumLocalization.Get(n.GatheringType));
        DebugCell(n.Level.ToString());
        DebugCell(EnumLocalization.Get(n.NodeType));
        DebugCell($"{n.Territory.Name} ({n.Territory.Id})");
        DebugCell($"{n.IntegralXCoord}-{n.IntegralYCoord}");
        DebugCell(n.ClosestAetheryte?.Name ?? "未知");
        DebugCell(n.Folklore);
        DebugCell(n.Times.PrintHours(true));
        DebugCell(n.PrintItems());
        DebugCell(ToCoordString(n.WorldPositions));
    }

    private static string ToCoordString(Dictionary<uint, List<Vector3>> worldCoords)
    {
        var result = string.Empty;
        foreach (var (key, value) in worldCoords)
        {
            result += $"{key}: {string.Join('|', value)} ";
        }

        return result;
    }

    private static void DrawFishDebug(Fish f)
    {
        DebugCell(f.ItemId.ToString());
        DebugCell($"{f.FishId}{(f.IsSpearFish ? " (sf)" : "")}");
        DebugCell(f.Name.English);
        DebugCell(f.FishRestrictions.ToString());
        DebugCell(f.Folklore);
        DebugCell(f.InLog.ToString());
        DebugCell(f.IsBigFish.ToString());
        DebugCell(JoinCell("|", f.FishingSpots, static s => s.Name));
    }

    private Dictionary<ushort, int>? _fishingSpotDataCounts;

    private static bool ArePositionsIdentical(Vector3 pos1, Vector3 pos2)
    {
        const float epsilon = 1.0f;
        return Math.Abs(pos1.X - pos2.X) < epsilon
            && Math.Abs(pos1.Y - pos2.Y) < epsilon
            && Math.Abs(pos1.Z - pos2.Z) < epsilon;
    }

    private class Vector3Comparer : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y)
            => ArePositionsIdentical(x, y);

        public int GetHashCode(Vector3 obj)
        {
            var x = (int)obj.X;
            var y = (int)obj.Y;
            var z = (int)obj.Z;
            return HashCode.Combine(x, y, z);
        }
    }

    private static Dictionary<ushort, int> CountDistinctSpotPositions(List<FishRecord> records)
    {
        var positionsBySpot = new Dictionary<ushort, HashSet<Vector3>>();
        var comparer = new Vector3Comparer();
        foreach (var fishRecord in records)
        {
            if (!fishRecord.PositionDataValid)
            {
                continue;
            }

            if (!positionsBySpot.TryGetValue(fishRecord.SpotId, out var positions))
            {
                positions = new HashSet<Vector3>(comparer);
                positionsBySpot.Add(fishRecord.SpotId, positions);
            }

            positions.Add(fishRecord.Position);
        }

        var counts = new Dictionary<ushort, int>(positionsBySpot.Count);
        foreach (var (spotId, positions) in positionsBySpot)
        {
            counts.Add(spotId, positions.Count);
        }

        return counts;
    }

    private static int CountItems<T>(IEnumerable<T> source)
    {
        if (source is ICollection<T> collection)
        {
            return collection.Count;
        }

        var count = 0;
        foreach (var _ in source)
        {
            count++;
        }

        return count;
    }

    private void DrawFishingSpotDebug(FishingSpot s)
    {
        _fishingSpotDataCounts ??= CountDistinctSpotPositions(_plugin.FishRecorder.RemoteRecords);

        DebugCell($"{s.Id}{(s.Spearfishing ? " (sf)" : "")}");
        DebugCell(s.Name);
        var dataCount = _fishingSpotDataCounts.GetValueOrDefault((ushort)s.Id, 0);
        DebugCell(dataCount.ToString());
        DebugCell($"{s.Territory.Name} ({s.Territory.Id})");
        DebugCell(s.ClosestAetheryte?.Name ?? "未知");
        DebugCell($"{s.IntegralXCoord / 100f:00.00}-{s.IntegralYCoord / 100f:00.00}");
        DebugCell($"{s.SpearfishingSpotData?.IsShadowNode ?? false}");
        DebugCell(JoinCell("|", s.Items, static fish => fish.Name.ToString()));
    }

    private static void PrintNode<T>(PatriciaTrie<T>.Node node)
    {
        var name = node.TotalWord.ToString();
        if (name.Length == 0)
        {
            name = "根目录";
        }

        if (node.Children.Count == 0)
        {
            ImGui.Text(name);
        }
        else
        {
            if (!ImGui.TreeNodeEx(name))
            {
                return;
            }

            foreach (var child in node.Children)
            {
                PrintNode(child);
            }

            ImGui.TreePop();
        }
    }

    private unsafe void DrawDebugButtons()
    {
        if (ImGui.CollapsingHeader("调试###Debug"))
        {
            if (ImGui.Button("标记天气脏数据###Set Weather Dirty"))
            {
                _weatherTable.SetDirty();
            }

            if (ImGui.Button("标记位置脏数据###Set Locations Dirty"))
            {
                GatherBuddy.UptimeManager.ResetLocations();
            }

            if (ImGui.Button("填充云冠群岛列表###Populate Diadem List"))
            {
                var list = new AutoGatherList()
                {
                    Name = "云冠群岛调试",
                    Description = "云冠群岛可采集物品调试列表",
                };
                foreach (var item in GatherBuddy.GameData.Gatherables.Values)
                {
                    if (!item.Name[ClientLanguage.English].Contains("Grade 4 Skybuilder", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    list.Add(item);
                    list.SetQuantity(item, 1000);
                }
                _plugin.AutoGatherListsManager.AddList(list);
            }


            if (ImGui.Button("测试增强天气###Test Enhanced Weather"))
            {
                var enhancedWeather = EnhancedCurrentWeather.GetCurrentWeatherWithDebug();
                var originalWeather = WeatherManager.Instance()->GetCurrentWeather();

                GatherBuddy.Log.Information($"[Weather Test] Enhanced: {enhancedWeather}, Original: {originalWeather}");

                if (GatherBuddy.GameData.Weathers.TryGetValue(enhancedWeather, out var eWeather))
                {
                    GatherBuddy.Log.Information($"[Weather Test] Enhanced Weather Name: {eWeather.Name}");
                }

                if (GatherBuddy.GameData.Weathers.TryGetValue(originalWeather, out var oWeather))
                {
                    GatherBuddy.Log.Information($"[Weather Test] Original Weather Name: {oWeather.Name}");
                }
            }

            if (ImGui.Button("解锁所有鱼类图鉴###Set All Fish Unlocked"))
            {
                GatherBuddy.FishLog.SetAllUnlocked();
            }

            if (FishTimerWindow.CollectableIcon.TryGetWrap(out var wrapCollectable, out _))
            {
                ImGui.Image(wrapCollectable.Handle, wrapCollectable.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.DoubleHookIcon.TryGetWrap(out var wrapDoubleHook, out _))
            {
                ImGui.Image(wrapDoubleHook.Handle, wrapDoubleHook.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.TripleHookIcon.TryGetWrap(out var wrapTripleHook, out _))
            {
                ImGui.Image(wrapTripleHook.Handle, wrapTripleHook.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.QuadHookIcon.TryGetWrap(out var wrapQuadHook, out _))
            {
                ImGui.Image(wrapQuadHook.Handle, wrapQuadHook.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.OctopusIcon.TryGetWrap(out var wrapOctopus, out _))
            {
                ImGui.Image(wrapOctopus.Handle, wrapOctopus.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.SharkIcon.TryGetWrap(out var wrapShark, out _))
            {
                ImGui.Image(wrapShark.Handle, wrapShark.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.JellyfishIcon.TryGetWrap(out var wrapJellyfish, out _))
            {
                ImGui.Image(wrapJellyfish.Handle, wrapJellyfish.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.SeadragonIcon.TryGetWrap(out var wrapSeadragon, out _))
            {
                ImGui.Image(wrapSeadragon.Handle, wrapSeadragon.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.FuguIcon.TryGetWrap(out var wrapFugu, out _))
            {
                ImGui.Image(wrapFugu.Handle, wrapFugu.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.CrabIcon.TryGetWrap(out var wrapCrab, out _))
            {
                ImGui.Image(wrapCrab.Handle, wrapCrab.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.MantaIcon.TryGetWrap(out var wrapManta, out _))
            {
                ImGui.Image(wrapManta.Handle, wrapManta.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.ShellfishIcon.TryGetWrap(out var wrapShellfish, out _))
            {
                ImGui.Image(wrapShellfish.Handle, wrapShellfish.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.SquidIcon.TryGetWrap(out var wrapSquid, out _))
            {
                ImGui.Image(wrapSquid.Handle, wrapSquid.Size);
            }

            ImGui.SameLine();
            if (FishTimerWindow.ShrimpIcon.TryGetWrap(out var wrapShrimp, out _))
            {
                ImGui.Image(wrapShrimp.Handle, wrapShrimp.Size);
            }
        }
    }

    private static unsafe void DrawDebugTime()
    {
        if (!ImGui.CollapsingHeader("时间###Time"))
        {
            return;
        }

        using var table = ImRaii.Table("##Times", 2);
        if (!table)
        {
            return;
        }

        var fw = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
        DebugCell("客户端框架时间戳");
        DebugCell(fw == null ? "NULL" : fw->UtcTime.Timestamp.ToString());
        DebugCell("框架的艾欧泽亚时间");
        DebugCell(fw == null ? "NULL" : fw->ClientTime.EorzeaTime.ToString());
        DebugCell("框架函数返回值");
        DebugCell(FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.GetServerTime().ToString());
        DebugCell("DateTimeOffset");
        DebugCell(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        DebugCell("GBR 时间戳");
        DebugCell(GatherBuddy.Time.ServerTime.Time.ToString());
        DebugCell("GBR 艾欧泽亚时间");
        DebugCell(GatherBuddy.Time.EorzeaTime.Time.ToString());
        DebugCell("当前计算天气");
        DebugCell(Dalamud.ClientState.TerritoryType != 0
            ? GatherBuddy.WeatherManager.FindLastCurrentNextWeather(Dalamud.ClientState.TerritoryType).Current.Name
            : "无");
        DebugCell("当前实际天气");
        DebugCell(Dalamud.ClientState.TerritoryType != 0
         && GatherBuddy.GameData.Weathers.TryGetValue(WeatherManager.Instance()->GetCurrentWeather(), out var w)
                ? w.Name
                : "无");
        DebugCell("客户端天气（ClientStructs）");
        var enhancedId = EnhancedCurrentWeather.GetCurrentWeatherId();
        DebugCell(Dalamud.ClientState.TerritoryType != 0
         && GatherBuddy.GameData.Weathers.TryGetValue(enhancedId, out var ew)
                ? $"{ew.Name} ({enhancedId})"
                : $"无 ({enhancedId})");
    }

    private static unsafe void DrawDebugFishingState()
    {
        if (!ImGui.CollapsingHeader("钓鱼状态###Fishing State"))
        {
            return;
        }

        ImGui.Text($"远程任务状态（上传）：{_plugin.FishRecorder.RemoteRecordsUploadTask.Status}");
        if (ImGui.Button("强制取消###Force Cancellation"))
        {
            _plugin.FishRecorder.StopRemoteRecordsRequests();
        }

        using var table = ImRaii.Table("##Framework", 2);
        if (!table)
        {
            return;
        }

        DebugCell("当前待保存更改");
        DebugCell(_plugin.FishRecorder.Changes.ToString());
        DebugCell("下次定时保存");
        DebugCell(_plugin.FishRecorder.SaveTime == TimeStamp.MaxValue
            ? "永不"
            : TimeInterval.DurationString(_plugin.FishRecorder.SaveTime, TimeStamp.UtcNow, false));
        DebugCell("UiState 地址");
        DebugCell($"{(IntPtr)FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance():X}");
        DebugCell("钓鱼事件处理器地址");
        DebugCell($"0x{(nint)GatherBuddy.EventFramework.FishingEventHandler:X}");
        DebugCell("钓鱼状态");
        DebugCell(GatherBuddy.EventFramework.FishingState.ToString());
        DebugCell("泳饵数量");
        DebugCell(GatherBuddy.EventFramework.NumSwimBait.ToString());
        DebugCell("选中的泳饵");
        DebugCell(GatherBuddy.EventFramework.CurrentSwimBait?.ToString() ?? "NULL");
        DebugCell("泳饵 1");
        DebugCell(GatherBuddy.EventFramework.SwimBait(0)?.ToString() ?? "NULL");
        DebugCell("泳饵 2");
        DebugCell(GatherBuddy.EventFramework.SwimBait(1)?.ToString() ?? "NULL");
        DebugCell("泳饵 3");
        DebugCell(GatherBuddy.EventFramework.SwimBait(2)?.ToString() ?? "NULL");
        DebugCell("咬钩类型地址");
        DebugCell(GatherBuddy.TugType.Address.ToString("X"));
        DebugCell("咬钩类型");
        DebugCell(EnumLocalization.Get(GatherBuddy.TugType.Bite));

        var record = _plugin.FishRecorder.Record;
        DebugCell("上次钓鱼状态");
        DebugCell(_plugin.FishRecorder.LastState.ToString());
        DebugCell("当前步骤");
        DebugCell(_plugin.FishRecorder.Step.ToString());
        DebugCell("获得力");
        DebugCell(record.Gathering.ToString());
        DebugCell("鉴别力");
        DebugCell(record.Perception.ToString());
        DebugCell("开始时间");
        DebugCell((record.TimeStamp / 1000).ToString());
        DebugCell("当前钓场");
        DebugCell($"{record.FishingSpot?.Name ?? "未知"} ({record.FishingSpot?.Id ?? 0})");
        if (CosmicMissionRegex().Match(record.FishingSpot?.Name ?? string.Empty).Groups["Id"] is { Success: true, Value: { } mission })
        {
            var id = uint.Parse(mission);
            if (Dalamud.GameData.GetExcelSheet<WKSMissionUnit>().TryGetRow(id, out var row))
            {
                DebugCell("当前任务");
                DebugCell($"{row.Name.ExtractText()} ({id})");
            }
        }

        DebugCell("选中的鱼饵");
        var baitId = GatherBuddy.CurrentBait.Current;
        DebugCell($"{GatherBuddy.GameData.Bait.GetValueOrDefault(baitId, Bait.Unknown).Name} ({baitId})");
        DebugCell("当前鱼饵");
        DebugCell($"{record.Bait.Name} ({record.Bait.Id})");
        DebugCell("持续时间");
        DebugCell(_plugin.FishRecorder.Timer.ElapsedMilliseconds.ToString());
        DebugCell("咬钩类型");
        DebugCell(record.Tug.ToString());
        DebugCell("提钩方式");
        DebugCell(record.Hook.ToString());
        DebugCell("上次捕获");
        DebugCell(
            $"{_plugin.FishRecorder.LastCatch?.Name[GatherBuddy.Language] ?? "无"} ({_plugin.FishRecorder.LastCatch?.ItemId ?? 0} - {_plugin.FishRecorder.LastCatch?.FishId ?? 0})");
        DebugCell("当前捕获");
        DebugCell(
            $"{record.Catch?.Name[GatherBuddy.Language] ?? "无"} ({record.Catch?.ItemId ?? 0} - {record.Catch?.FishId ?? 0}）· 鱼长 {record.Size / 10f}，捕获 {record.Amount} 次");
        foreach (var flag in Enum.GetValues<Effects>())
        {
            DebugCell(flag.ToString());
            DebugCell(record.Flags.HasFlag(flag).ToString());
        }
    }

    private static unsafe void DrawDebugFishingTimes()
    {
        if (!ImGui.CollapsingHeader("钓鱼时间###Fishing Times"))
        {
            return;
        }

        using var table = ImRaii.Table("##Fishing Times", 6);
        if (!table)
        {
            return;
        }

        foreach (var (fishId, data) in _plugin.FishRecorder.Times)
        {
            DebugCell(GatherBuddy.GameData.Fishes[fishId].Name[GatherBuddy.Language]);
            DebugCell("总计");
            DebugCell(data.All.Min.ToString());
            DebugCell(data.All.Max.ToString());
            DebugCell(data.All.MinChum.ToString());
            DebugCell(data.All.MaxChum.ToString());
            foreach (var (baitId, times) in data.Data)
            {
                var bait = GatherBuddy.GameData.Bait.TryGetValue(baitId, out var b)
                    ? b
                    : new Bait(GatherBuddy.GameData.Fishes[fishId].ItemData);
                ImGui.TableNextColumn();
                DebugCell(bait.Name);
                DebugCell(times.Min.ToString());
                DebugCell(times.Max.ToString());
                DebugCell(times.MinChum.ToString());
                DebugCell(times.MaxChum.ToString());
            }
        }
    }

    private static void DrawUptimeManagerTable()
    {
        if (!ImGui.CollapsingHeader($"出现时间段 ({GatherBuddy.GameData.TimedGatherables})###Uptimes ({GatherBuddy.GameData.TimedGatherables})"))
        {
            return;
        }

        using var table = ImRaii.Table("##Uptimes", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
        {
            return;
        }

        foreach (var item in GatherBuddy.GameData.Gatherables.Values)
        {
            if (item.InternalLocationId == 0)
            {
                continue;
            }

            DebugCell(Math.Abs(item.InternalLocationId).ToString("0000"));
            DebugCell(item.Name[GatherBuddy.Language]);
            DebugCell(item.NodeList.Count.ToString());
            var (loc, time) = GatherBuddy.UptimeManager.BestLocation(item);
            DebugCell(loc.Name);
            if (item.InternalLocationId > 0)
            {
                if (time == TimeInterval.Invalid)
                {
                    DebugCell("无效");
                    DebugCell(string.Empty);
                }
                else if (time == TimeInterval.Never)
                {
                    DebugCell("永不");
                    DebugCell(string.Empty);
                }
                else
                {
                    DebugCell(time.Start.ToString());
                    DebugCell(time.End.ToString());
                }
            }
            else
            {
                DebugCell("始终");
                ImGui.TableNextColumn();
            }
        }

        foreach (var fish in GatherBuddy.GameData.Fishes.Values)
        {
            if (fish.InternalLocationId == 0)
            {
                continue;
            }

            DebugCell(Math.Abs(fish.InternalLocationId).ToString("0000"));
            DebugCell(fish.Name[GatherBuddy.Language]);
            DebugCell(fish.FishingSpots.Count.ToString());
            var (loc, time) = GatherBuddy.UptimeManager.BestLocation(fish);
            DebugCell(loc.Name);
            if (fish.InternalLocationId > 0)
            {
                if (time == TimeInterval.Invalid)
                {
                    DebugCell("无效");
                    DebugCell(string.Empty);
                }
                else if (time == TimeInterval.Never)
                {
                    DebugCell("永不");
                    DebugCell(string.Empty);
                }
                else
                {
                    DebugCell(time.Start.ToString());
                    DebugCell(time.End.ToString());
                }
            }
            else
            {
                DebugCell("始终");
                ImGui.TableNextColumn();
            }
        }
    }

    private static void DrawAlarmDebug()
    {
        if (!ImGui.CollapsingHeader("闹钟###Alarms##AlarmDebug"))
        {
            return;
        }

        using var table = ImRaii.Table("##Alarms", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
        {
            return;
        }

        var nextAlarm = _plugin.AlarmManager.ActiveAlarms.Count > 0 ? _plugin.AlarmManager.ActiveAlarms[0].Item2 : TimeStamp.Epoch;
        var (abs, rel) = nextAlarm != TimeStamp.Epoch
            ? (nextAlarm.LocalTime.ToString(CultureInfo.InvariantCulture),
                TimeInterval.DurationString(nextAlarm, GatherBuddy.Time.ServerTime, false))
            : ("永不", "永不");

        DebugCell("已启用");
        DebugCell(GatherBuddy.Config.AlarmsEnabled.ToString());
        DebugCell("有待保存的更改");
        DebugCell(_plugin.AlarmManager.Dirty.ToString());
        DebugCell("下次变化的时间");
        DebugCell(abs);
        DebugCell("距下次变化");
        DebugCell(rel);
        DebugCell("#Alarm Groups");
        DebugCell(_plugin.AlarmManager.Alarms.Count.ToString());
        DebugCell("#Enabled Alarms");
        DebugCell(_plugin.AlarmManager.ActiveAlarms.Count.ToString());
        foreach (var (alarm, state) in _plugin.AlarmManager.ActiveAlarms)
        {
            DebugCell(alarm.Name.Length != 0 ? alarm.Name : alarm.Item.Name[GatherBuddy.Language]);
            DebugCell($"{state} ({TimeInterval.DurationString(state, GatherBuddy.Time.ServerTime, false)})");
        }
    }

    private string _identifyTest = string.Empty;
    private uint _lastItemIdentified = 0;
    private bool _subscribeToAutoGatherWaiting = false;
    private bool _subscribeToAutoGatherEnabledChanged = false;
    private long _lastAutoGatherWaitingTime = 0;
    private long _lastAutoGatherEnabledChangedTime = 0;
    private bool _lastAutoGatherEnabledValue = false;
    private Action? _autoGatherWaitingHandler = null;
    private Action<bool>? _autoGatherEnabledChangedHandler = null;

    private static void DrawWaymarkTab()
    {
        if (!ImGui.CollapsingHeader("场地标记###Waymarks##WaymarkDebug"))
        {
            return;
        }

        ImGui.TextUnformatted($"标记管理器：0x{GatherBuddy.WaymarkManager.Address:X}");
        var baseAddr = System.Diagnostics.Process.GetCurrentProcess().MainModule?.BaseAddress ?? IntPtr.Zero;
        ImGui.TextUnformatted(
            $"标记管理器偏移量: +0x{(ulong)GatherBuddy.WaymarkManager.Address - (ulong)baseAddr:X}");
        using var table = ImRaii.Table("##Waymarks", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
        {
            return;
        }

        for (var i = 0; i < GatherBuddy.WaymarkManager.Count; ++i)
        {
            using var id = ImRaii.PushId(i);
            var waymark = GatherBuddy.WaymarkManager[i];
            ImGui.TableNextColumn();
            if (ImGui.Button("清除###Clear"))
            {
                GatherBuddy.WaymarkManager.ClearWaymark(i);
            }

            ImGui.TableNextColumn();
            if (ImGui.Button("设置###Set"))
            {
                GatherBuddy.WaymarkManager.SetWaymark(i);
            }

            DebugCell(waymark.Active.ToString());
            DebugCell(waymark.Position.X.ToString());
            DebugCell(waymark.Position.Y.ToString());
            DebugCell(waymark.Position.Z.ToString());
            DebugCell(waymark.X.ToString());
            DebugCell(waymark.Y.ToString());
            DebugCell(waymark.Z.ToString());
        }
    }

    private static void DrawOceanTab()
    {
        if (!ImGui.CollapsingHeader("海钓航路###Ocean Routes##OceanDebug"))
        {
            return;
        }

        using (var table = ImRaii.Table("##Ocean", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter))
        {
            if (table)
            {
                ImGui.TableSetupColumn("航线"u8);
                ImGui.TableSetupColumn("时间"u8);
                ImGui.TableSetupColumn("区域"u8);
                ImGui.TableSetupColumn("钓场 1"u8);
                ImGui.TableSetupColumn("钓场 1 幻海流"u8);
                ImGui.TableSetupColumn("钓场 2"u8);
                ImGui.TableSetupColumn("钓场 2 幻海流"u8);
                ImGui.TableSetupColumn("钓场 3"u8);
                ImGui.TableSetupColumn("钓场 3 幻海流"u8);
                ImGui.TableHeadersRow();
                foreach (var route in GatherBuddy.GameData.OceanRoutes)
                {
                    DebugCell(route.ToString());
                    DebugCell(route.StartTime.ToString());
                    DebugCell(route.Area.ToString());
                    DebugCell(route.GetSpots(0).Normal.Name);
                    DebugCell(route.GetSpots(0).Spectral.Name);
                    DebugCell(route.GetSpots(1).Normal.Name);
                    DebugCell(route.GetSpots(1).Spectral.Name);
                    DebugCell(route.GetSpots(2).Normal.Name);
                    DebugCell(route.GetSpots(2).Spectral.Name);
                }
            }
        }

        using (var table = ImRaii.Table("##OceanTimeline", 9,
                   ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter))
        {
            if (table)
            {
                ImGui.TableSetupColumn("#"u8);
                ImGui.TableSetupColumn("阿尔迪纳德"u8);
                ImGui.TableSetupColumn("钓场 1###A"u8);
                ImGui.TableSetupColumn("钓场 2###A"u8);
                ImGui.TableSetupColumn("钓场 3###A"u8);
                ImGui.TableSetupColumn("奥萨德"u8);
                ImGui.TableSetupColumn("钓场 1###O"u8);
                ImGui.TableSetupColumn("钓场 2###O"u8);
                ImGui.TableSetupColumn("钓场 3###O"u8);
                ImGui.TableHeadersRow();
                for (var idx = 0; idx < GatherBuddy.GameData.OceanTimeline.Count; ++idx)
                {
                    var routeAldenard = GatherBuddy.GameData.OceanTimeline[OceanArea.Aldenard][idx];
                    var routeOthard = GatherBuddy.GameData.OceanTimeline[OceanArea.Othard][idx];
                    DebugCell(idx.ToString());
                    DebugCell(routeAldenard.ToString());
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                    DebugCell(routeAldenard.GetSpots(0).Normal.Name);
                    DebugCell(routeAldenard.GetSpots(1).Normal.Name);
                    DebugCell(routeAldenard.GetSpots(2).Normal.Name);
                    DebugCell(routeOthard.ToString());
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                    DebugCell(routeOthard.GetSpots(0).Normal.Name);
                    DebugCell(routeOthard.GetSpots(1).Normal.Name);
                    DebugCell(routeOthard.GetSpots(2).Normal.Name);
                }
            }
        }
    }

    private static void DrawCosmicTab()
    {
        if (!ImGui.CollapsingHeader("宇宙探索钓鱼任务###Cosmic Exploration Fishing Missions##CosmicDebug"))
        {
            return;
        }

        if (_debugCosmicMissions == null)
        {
            _debugCosmicMissions = [.. GatherBuddy.GameData.CosmicFishingMissions.Values];
            Array.Sort(_debugCosmicMissions, static (a, b) => a.Id.CompareTo(b.Id));
        }

        using var table = ImRaii.Table("##Cosmic", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (table)
        {
            foreach (var mission in _debugCosmicMissions)
            {
                DebugCell($"{mission.Id}");
                DebugCell(mission.Name);
            }
        }
    }

    private sealed record FishBaitId(string Name, uint Id, bool Mooch);

    private static CosmicMission[]? _debugCosmicMissions;
    private List<Territory>? _debugTerritories;
    private List<string>? _debugWeathers;
    private List<FishBaitId>? _debugFishBait;
    private int _debugTerritoryIdx = -1;
    private int _debugWeatherIdx = -1;
    private int _debugFishBaitIdx = -1;

    private static void DebugCell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }

    private static string JoinCell<T>(string separator, IReadOnlyList<T> items, Func<T, string> selector)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(selector(items[0]));
        for (var i = 1; i < items.Count; ++i)
        {
            builder.Append(separator).Append(selector(items[i]));
        }

        return builder.ToString();
    }

    private static void DrawTabbedTable<T>(string label, IEnumerable<T> data, Action<T> drawRow, ImGuiTableFlags flags = ImGuiTableFlags.None,
        params string[] columnTitles)
    {
        if (columnTitles.Length == 0 || !ImGui.CollapsingHeader(label))
        {
            return;
        }

        using var table = ImRaii.Table($"{label}##Table", columnTitles.Length, flags);
        if (!table)
        {
            return;
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        foreach (var title in columnTitles)
        {
            ImGui.TableNextColumn();
            M3Table.Header(title, center: false);
        }

        foreach (var datum in data)
        {
            ImGui.TableNextRow();
            drawRow(datum);
        }
    }

    private void DrawDebugFishHelper()
    {
        if (_debugTerritories == null)
        {
            _debugTerritories = [.. GatherBuddy.GameData.Territories.Values];
            _debugTerritories.Sort(static (a, b) =>
            {
                var order = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                return order != 0 ? order : a.Id.CompareTo(b.Id);
            });
        }

        if (_debugWeathers == null)
        {
            var weatherNames = new HashSet<string>();
            foreach (var weatherData in GatherBuddy.GameData.Weathers.Values)
            {
                weatherNames.Add(weatherData.Name);
            }

            _debugWeathers = [.. weatherNames];
            _debugWeathers.Sort(StringComparer.CurrentCulture);
        }

        if (_debugFishBait == null)
        {
            _debugFishBait = new List<FishBaitId>(GatherBuddy.GameData.Fishes.Count + GatherBuddy.GameData.Bait.Count);
            foreach (var fishData in GatherBuddy.GameData.Fishes.Values)
            {
                _debugFishBait.Add(new FishBaitId(fishData.Name.English, fishData.ItemId, true));
            }

            foreach (var baitData in GatherBuddy.GameData.Bait.Values)
            {
                _debugFishBait.Add(new FishBaitId(baitData.Name, baitData.Id, false));
            }
        }

        var territories = _debugTerritories;
        M3Widgets.Combo("##Territory", ref _debugTerritoryIdx, territories.Count, i => $"{territories[i].Name} ({territories[i].Id})", 300 * M3.Scale,
            "选择区域", true);
        ImGui.SameLine(0, M3.Space2);
        M3Widgets.Combo("##Weather", ref _debugWeatherIdx, _debugWeathers, 180 * M3.Scale, "选择天气", true);
        if (_debugTerritoryIdx >= 0 && _debugWeatherIdx >= 0)
        {
            var territory = territories[_debugTerritoryIdx];
            var weather = _debugWeathers[_debugWeatherIdx];
            var weathers = new List<uint>();
            foreach (var (rateWeather, _) in territory.WeatherRates.Rates)
            {
                if (rateWeather.Name == weather)
                {
                    weathers.Add(rateWeather.Id);
                }
            }

            ImGui.SameLine(0, M3.Space2);
            if (weathers.Count > 0)
            {
                var weatherIds = string.Join(", ", weathers);
                M3Widgets.CopyButton("##weatherIds", weatherIds, weatherIds, "已复制", M3ButtonStyle.Text, tooltip: "复制天气 ID");
            }
            else
            {
                Ui.RowText("该区域没有这种天气。", M3Widgets.ComboHeight, M3TextStyle.Muted);
            }
        }

        var fishBait = _debugFishBait;
        M3Widgets.Combo("##fish", ref _debugFishBaitIdx, fishBait.Count, i => fishBait[i].Name, 300 * M3.Scale, "选择鱼类或鱼饵", true);
        if (_debugFishBaitIdx >= 0)
        {
            var fish = fishBait[_debugFishBaitIdx];
            ImGui.SameLine(0, M3.Space2);
            M3Widgets.CopyButton("##fishId", fish.Id.ToString(), fish.Mooch ? $"{fish.Id} （以小钓大）" : fish.Id.ToString(), "已复制", M3ButtonStyle.Text,
                tooltip: "复制物品 ID");
        }
    }

    private void DrawDebugPage()
    {
        using var id = ImRaii.PushId("Debug");
        var s = M3.Scheme;
        using var headers = ImRaii.PushColor(ImGuiCol.Header, s.SurfaceContainerHigh)
            .Push(ImGuiCol.HeaderHovered, M3.StateLayer(s.SurfaceContainerHigh, s.OnSurface, true, false))
            .Push(ImGuiCol.HeaderActive, M3.StateLayer(s.SurfaceContainerHigh, s.OnSurface, true, true));
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, M3.ShapeSmall);
        using var tables = M3Table.PushStyle();
        DrawDebugFishHelper();
        ImGui.Dummy(new Vector2(0, M3.Space1));

        using var child = ImRaii.Child(string.Empty);
        if (!child)
        {
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;

        DrawDebugButtons();
        DrawDebugTime();
        DrawDebugFishingState();
        DrawDebugFishingTimes();
        DrawAlarmDebug();
        DrawAutoGatherDebug();
        DrawReflectionDebug();
        DrawTabbedTable($"以太之光（{GatherBuddy.GameData.Aetherytes.Count})", GatherBuddy.GameData.Aetherytes.Values,
            DrawDebugAetheryte, flags, "Id", "名称", "区域", "坐标", "以太流");
        DrawTabbedTable($"区域（{GatherBuddy.GameData.WeatherTerritories.Length})", GatherBuddy.GameData.WeatherTerritories,
            DrawDebugTerritory, flags, "Id", "名称", "尺寸系数", "天气数", "天气");
        DrawTabbedTable($"鱼饵（{GatherBuddy.GameData.Bait.Count})", GatherBuddy.GameData.Bait.Values,
            DrawDebugBait, flags, "Id", "名称");
        var sortedGatherables = new List<Gatherable>(GatherBuddy.GameData.Gatherables.Values);
        sortedGatherables.Sort(static (lhs, rhs) => lhs.ItemId.CompareTo(rhs.ItemId));
        DrawTabbedTable($"采集品（{GatherBuddy.GameData.Gatherables.Count})",
            sortedGatherables,
            DrawGatherableDebug, flags, "物品Id", "采集Id", "名称", "等级", "采集点数");
        DrawTabbedTable($"采集点（{GatherBuddy.GameData.GatheringNodes.Count})", GatherBuddy.GameData.GatheringNodes.Values,
            DrawGatheringNodeDebug, flags, "Id", "名称", "职业", "等级", "类型", "区域", "坐标", "以太之光", "传承录", "时间",
            "物品", "世界坐标");
        DrawTabbedTable($"鱼类（{GatherBuddy.GameData.Fishes.Count})", GatherBuddy.GameData.Fishes.Values,
            DrawFishDebug, flags, "物品Id", "鱼Id", "名称", "限制", "传承录", "已记录", "大鱼", "渔场");
        DrawTabbedTable($"钓场（{GatherBuddy.GameData.FishingSpots.Count})", GatherBuddy.GameData.FishingSpots.Values,
            DrawFishingSpotDebug, flags, "Id", "名称", "数据", "区域", "以太之光", "坐标", "阴影", "鱼");
        DrawUptimeManagerTable();
        DrawOceanTab();
        DrawCosmicTab();
        DrawWaymarkTab();
        if (ImGui.CollapsingHeader("采集前缀树###GatheringTree"))
        {
            id.Push("GatheringTree");
            PrintNode(GatherBuddy.GameData.GatherablesTrie.Root);
            id.Pop();
        }

        if (ImGui.CollapsingHeader("钓鱼前缀树###FishingTree"))
        {
            id.Push("FishingTree");
            PrintNode(GatherBuddy.GameData.FishTrie.Root);
            id.Pop();
        }

        if (ImGui.CollapsingHeader("IPC"))
        {
            var autoGatherEnabled = Dalamud.PluginInterface.GetIpcSubscriber<bool>($"{GatherBuddy.InternalName}.IsAutoGatherEnabled").InvokeFunc();
            var autoGatherWaiting = Dalamud.PluginInterface.GetIpcSubscriber<bool>($"{GatherBuddy.InternalName}.IsAutoGatherWaiting").InvokeFunc();
            var autoGatherStatusText = Dalamud.PluginInterface.GetIpcSubscriber<string>($"{GatherBuddy.InternalName}.GetAutoGatherStatusText").InvokeFunc();
            var versionText = Dalamud.PluginInterface.GetIpcSubscriber<int>($"{GatherBuddy.InternalName}.Version").InvokeFunc().ToString();

            // Create a table for IPC methods and values
            using var table = ImRaii.Table("##IPCTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
            if (table)
            {
                // Version
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"int {GatherBuddy.InternalName}.Version()");
                ImGui.TableNextColumn();
                ImGui.Text(versionText);

                // Identify
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"uint {GatherBuddy.InternalName}.Identify(string)");
                ImGui.SameLine();
                ImGui.TableNextColumn();
                ImGui.Text($"{_lastItemIdentified,-6}");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputTextWithHint("##IPCIdentifyTest", "识别...", ref _identifyTest, 64))
                {
                    _lastItemIdentified = Dalamud.PluginInterface.GetIpcSubscriber<string, uint>($"{GatherBuddy.InternalName}.Identify")
                        .InvokeFunc(_identifyTest);
                }

                // IsAutoGatherEnabled
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"bool {GatherBuddy.InternalName}.IsAutoGatherEnabled()");
                ImGui.TableNextColumn();
                ImGui.Text(autoGatherEnabled.ToString());

                // IsAutoGatherWaiting
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"bool {GatherBuddy.InternalName}.IsAutoGatherWaiting()");
                ImGui.TableNextColumn();
                ImGui.Text(autoGatherWaiting.ToString());

                // GetAutoGatherStatusText
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"string {GatherBuddy.InternalName}.GetAutoGatherStatusText()");
                ImGui.TableNextColumn();
                ImGui.Text(autoGatherStatusText);

                // SetAutoGatherEnabled
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"void {GatherBuddy.InternalName}.SetAutoGatherEnabled(bool)");
                ImGui.TableNextColumn();
                if (ImGui.Button("切换###Toggle"))
                {
                    Dalamud.PluginInterface.GetIpcSubscriber<bool, object>($"{GatherBuddy.InternalName}.SetAutoGatherEnabled")
                        .InvokeAction(!autoGatherEnabled);
                }
                // AutoGatherWaiting event
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Checkbox($"订阅 {GatherBuddy.InternalName}.AutoGatherWaiting###Subscribe to {GatherBuddy.InternalName}.AutoGatherWaiting", ref _subscribeToAutoGatherWaiting))
                {
                    if (_subscribeToAutoGatherWaiting)
                    {
                        _autoGatherWaitingHandler = () => _lastAutoGatherWaitingTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        Dalamud.PluginInterface.GetIpcSubscriber<Action>($"{GatherBuddy.InternalName}.AutoGatherWaiting")
                            .Subscribe(_autoGatherWaitingHandler);
                    }
                    else if (_autoGatherWaitingHandler != null)
                    {
                        Dalamud.PluginInterface.GetIpcSubscriber<Action>($"{GatherBuddy.InternalName}.AutoGatherWaiting")
                            .Unsubscribe(_autoGatherWaitingHandler);
                        _autoGatherWaitingHandler = null;
                        _lastAutoGatherWaitingTime = default;
                    }
                }
                ImGui.TableNextColumn();
                var secondsSinceWaiting = _lastAutoGatherWaitingTime > 0
                    ? (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastAutoGatherWaitingTime)
                    : -1;
                ImGui.Text($"{(secondsSinceWaiting >= 0 ? $"{secondsSinceWaiting}秒前" : "永不")}");

                // AutoGatherEnabledChanged event
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Checkbox($"订阅 {GatherBuddy.InternalName}.AutoGatherEnabledChanged(bool)###Subscribe to {GatherBuddy.InternalName}.AutoGatherEnabledChanged(bool)", ref _subscribeToAutoGatherEnabledChanged))
                {
                    if (_subscribeToAutoGatherEnabledChanged)
                    {
                        _autoGatherEnabledChangedHandler = value =>
                        {
                            _lastAutoGatherEnabledChangedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            _lastAutoGatherEnabledValue = value;
                        };
                        Dalamud.PluginInterface.GetIpcSubscriber<bool, object>($"{GatherBuddy.InternalName}.AutoGatherEnabledChanged")
                            .Subscribe(_autoGatherEnabledChangedHandler);
                    }
                    else if (_autoGatherEnabledChangedHandler != null)
                    {
                        Dalamud.PluginInterface.GetIpcSubscriber<bool, object>($"{GatherBuddy.InternalName}.AutoGatherEnabledChanged")
                            .Unsubscribe(_autoGatherEnabledChangedHandler);
                        _autoGatherEnabledChangedHandler = null;
                        _lastAutoGatherEnabledValue = default;
                        _lastAutoGatherEnabledChangedTime = default;
                    }
                }
                ImGui.TableNextColumn();
                var secondsSinceEnabled = _lastAutoGatherEnabledChangedTime > 0
                    ? (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastAutoGatherEnabledChangedTime)
                    : -1;
                ImGui.Text($"{(secondsSinceEnabled >= 0 ? $"{secondsSinceEnabled}秒前（值 {_lastAutoGatherEnabledValue})" : "永不")}");
            }
        }

        if (ImGui.CollapsingHeader("世界对象###World Objects"))
        {
            using var group = ImRaii.Group();
            ImGui.Text("200 星码内的采集点");
            foreach (var obj in Dalamud.Objects)
            {
                if (obj.ObjectKind != ObjectKind.GatheringPoint)
                {
                    continue;
                }

                ImGui.PushID(obj.GameObjectId.ToString());
                var node = GatherBuddy.GameData.GatheringNodes.TryGetValue((uint)obj.GameObjectId, out var n) ? n : null;
                ImGui.Text($"{obj.GameObjectId}: {obj.Name ?? "未知"} · 基础 ID {obj.BaseId}");
                ImGui.SameLine();
                if (ImGui.SmallButton("导航至###NavTo"))
                {
                    VNavmesh.SimpleMove.PathfindAndMoveTo(obj.Position, true);
                }
                ImGui.SameLine();
                ImGui.PopID();
            }
        }

        if (ImGui.CollapsingHeader("已保存的世界对象###Saved World Objects"))
        {
            using var group = ImRaii.Group();
            ImGui.Text("已保存的采集点");
            foreach (var kvp in WorldData.WorldLocationsByNodeId)
            {
                ImGui.PushID(kvp.Key.ToString());
                ImGui.Text($"{kvp.Key}");
                foreach (var loc in kvp.Value)
                {
                    ImGui.Indent();
                    ImGui.Text($"{loc}");
                    ImGui.Unindent();
                }
                ImGui.PopID();
            }
        }
    }

    private void DrawAutoGatherDebug()
    {
        if (!ImGui.CollapsingHeader("自动采集###AutoGather"))
        {
            return;
        }

        if (ImGui.Button("清除限时采集点记忆###Clear Timed Node Memory"))
        {
            GatherBuddy.Log.Information("已手动清除限时采集点记忆!");
            GatherBuddy.AutoGather.DebugClearVisited();
        }

        ImGui.Text($"已启用：{GatherBuddy.AutoGather.Enabled}");
        ImGui.Text($"状态：{GatherBuddy.AutoGather.AutoStatus}");
        ImGui.Text($"当前目的地：{GatherBuddy.AutoGather.CurrentDestination}");
        ImGui.Text($"正在采集：{AutoGather.AutoGather.IsGathering}");
        ImGui.Text($"正在寻路：{AutoGather.AutoGather.IsPathing}");
        ImGui.Text($"正在生成路径：{GatherBuddy.AutoGather.IsPathGenerating}");
        ImGui.Text($"导航已就绪：{AutoGather.AutoGather.NavReady}");
        ImGui.Text($"可行动：{AutoGather.AutoGather.CanAct}");
        ImGui.Text($"黑名单采集点：{GatherBuddy.Config.AutoGatherConfig.BlacklistedNodesByTerritoryId.Count}");
        ImGui.Text($"区域待采集物品：{CountItems(GatherBuddy.AutoGather.ItemsToGatherInZone)}");
        ImGui.Text($"待采集物品：{CountItems(GatherBuddy.AutoGather.ItemsToGather)}");
        ImGui.Text($"使用采集标记：{AutoGather.AutoGather.ShouldUseFlag}");
        ImGui.Text($"上一次的耐久：{GatherBuddy.AutoGather.LastIntegrity}");
        ImGui.Text($"上一次的收藏价值：{GatherBuddy.AutoGather.LastCollectability}");
        ImGui.Text($"强心剂冷却中：{GatherBuddy.AutoGather.IsCordialOnCooldown}");
        //ImGui.Text($"IsFoodBuffUp: {GatherBuddy.AutoGather.GetIsFoodBuffUp()}");
        //ImGui.Text($"IsPotionBuffUp: {GatherBuddy.AutoGather.GetIsPotionBuffUp()}");
        ImGui.Text($"指导书效果中：{AutoGather.AutoGather.IsManualBuffUp}");
        //ImGui.Text($"IsSquadronManualBuffUp: {GatherBuddy.AutoGather.GetIsSquadronManualBuffUp()}");
        //ImGui.Text($"IsSquadronPassBuffUp: {GatherBuddy.AutoGather.GetIsSquadronPassBuffUp()}");
        ImGui.Text($"排序方式：{GatherBuddy.Config.AutoGatherConfig.SortingMethod.ToString()}");

        unsafe
        {
            var addon = (AddonGatheringMasterpiece*)(nint)Dalamud.GameGui.GetAddonByName("GatheringMasterpiece");
            if (addon != null && addon->IsFullyLoaded() && addon->IsReady)
            {
                ImGui.Text($"最低收藏价值: {addon->GetComponentByNodeId(13)->GetTextNodeById(3)->GetAsAtkTextNode()->NodeText} {addon->AtkUnitBase.GetNodeById(13)->IsVisible()}");
                ImGui.Text($"中等收藏价值: {addon->GetComponentByNodeId(14)->GetTextNodeById(3)->GetAsAtkTextNode()->NodeText} {addon->AtkUnitBase.GetNodeById(14)->IsVisible()}");
                ImGui.Text($"最高收藏价值: {addon->GetComponentByNodeId(15)->GetTextNodeById(3)->GetAsAtkTextNode()->NodeText} {addon->AtkUnitBase.GetNodeById(15)->IsVisible()}");
            }
        }

        if (ImGui.CollapsingHeader("限时采集点记忆###Timed Node Memory"))
        {
            foreach (var (location, time) in GatherBuddy.AutoGather.DebugVisitedTimedLocations)
            {
                ImGui.Text($"{location.Name} {time.End.ConvertToEorzea().DateTime.ToString("HH:mm", CultureInfo.InvariantCulture)} ET");
            }
        }

        if (ImGui.CollapsingHeader("已访问采集点###Visited Nodes"))
        {
            foreach (var pos in GatherBuddy.AutoGather.VisitedNodes)
            {
                ImGui.Text($"{pos}");
            }
        }

        if (ImGui.CollapsingHeader("已见到的远距离采集点###Far Nodes Seen So Far"))
        {
            foreach (var pos in GatherBuddy.AutoGather.FarNodesSeenSoFar)
            {
                ImGui.Text($"{pos}");
            }
        }

        if (ImGui.CollapsingHeader("待采集物品###Items to Gather"))
        {
            foreach (var x in GatherBuddy.AutoGather.ItemsToGather)
            {
                ImGui.Text($"物品 {x.Item.Name}，地点 {x.Location.Name}，有效时间 {(x.Time == TimeInterval.Always ? "始终" : x.Time.End.ConvertToEorzea().DateTime.ToString("HH:mm", CultureInfo.InvariantCulture))} 艾欧泽亚时间，数量 {x.Quantity}");
                if (x.Time == TimeInterval.Always || x.Node == null || x.Node.NodeType is not NodeType.Unspoiled and not NodeType.Legendary and not NodeType.Clouded)
                {
                    continue;
                }

                ImGui.SameLine();
                if (ImGui.Button($"标记已访问###Mark Visited##{x.Item.ItemId}"))
                {
                    GatherBuddy.AutoGather.DebugMarkVisited(x);
                }
            }
        }

        var tr = GatherBuddy.AutoGather.GatheringWindowReader;
        if (ImGui.CollapsingHeader("采集窗口读取器###Gather Window Reader"))
        {
            var text = new StringBuilder();
            if (tr != null)
            {
                text.AppendLine($"已触摸: {tr.Touched}");
                text.AppendLine($"有未隐藏项: {tr.HasUnhidden}");
                text.AppendLine($"采集次数／耐久 {tr.IntegrityRemaining}/{tr.IntegrityMax}");
                text.Append($"快速采集{(tr.QuickGatheringAllowed ? "可用" : "不可用")}");
                if (tr.QuickGatheringAllowed)
                {
                    text.Append($"，{(tr.QuickGatheringEnabled ? "已勾选" : "未勾选")}");
                }

                if (tr.QuickGatheringInProgress)
                {
                    text.Append("，进行中");
                }

                text.AppendLine();
                for (var i = 0; i < 8; i++)
                {
                    var n = tr.ItemSlots[i];
                    text.Append($"槽位 {i}:");
                    if (n.IsEmpty)
                    {
                        text.AppendLine(" 空的;");
                        continue;
                    }
                    text.Append($" {(!n.IsEmpty ? n.Item.Name[GatherBuddy.Language] : "无")};");
                    //if (!n.Enabled) text.Append(" disabled;");
                    text.Append($" 等级: {n.ItemLevel}; 产量: {n.Yield}{(n.HasGivingLandBuff ? "+?" : "")}; 概率: {n.GatherChance}; 加成: {n.BoonChance};");
                    if (n.IsHidden)
                    {
                        text.Append(" 隐藏，");
                    }

                    if (n.IsRare)
                    {
                        text.Append(" 珍稀，");
                    }

                    if (n.HasBonus)
                    {
                        text.Append(" 有加成，");
                    }

                    if (n.IsCollectable)
                    {
                        text.Append($" 收藏品，");
                    }

                    text.AppendLine();
                }
            }
            else
            {
                text.AppendLine("未就绪");
            }

            ImGui.TextWrapped(text.ToString());
        }

        AutoGatherUI.DrawDebugTables();
    }

    private static void DrawReflectionDebug()
    {

        if (!ImGui.CollapsingHeader("反射###Reflection"))
        {
            return;
        }

        var exporter = GatherBuddy.AutoGather.ArtisanExporter;
        ImGui.Text($"Artisan 程序集已启用 {AutoGather.Helpers.Reflection.ArtisanExporter.ArtisanAssemblyEnabled}");
        if (exporter.TouchArtisanAssembly)
        {
            ImGui.Text($"Artisan 实例: {exporter.ArtisanAssemblyInstance}");
        }

        DrawCosmicFishDataButton();
    }

    private static void DrawCosmicFishDataButton()
    {
        ImGui.PushItemWidth(100);
        var startId = (int)_startId;
        if (ImGui.InputInt($"起始 ID: {GatherBuddy.GameData.FishingSpots.GetValueOrDefault(_startId)?.Name}###startid", ref startId))
        {
            _startId = (uint)Math.Max(0, startId);
        }

        var endId = (int)_endId;
        if (ImGui.InputInt($"终止 ID: {GatherBuddy.GameData.FishingSpots.GetValueOrDefault(_endId)?.Name}###endid", ref endId))
        {
            _endId = (uint)Math.Max(0, endId);
        }
        ImGui.PopItemWidth();

        if (!M3Widgets.Button("##copyUnknown", "复制最近的未知鱼类数据", M3ButtonStyle.Tonal, FontAwesomeIcon.Copy))
        {
            return;
        }

        var patch = $"{nameof(Patch)}.{Enum.GetValues<Patch>()[^1]}";
        var text = "";
        foreach (var spot in GatherBuddy.GameData.FishingSpots.Values)
        {
            if (spot.Id < _startId || spot.Id > _endId)
            {
                continue;
            }

            if (spot.Items.Length is 0)
            {
                continue;
            }

            var match = CosmicMissionRegex().Match(spot.Name);
            uint missionId = 0;
            var name = spot.Name;
            if (match.Success)
            {
                var spotName = match.Groups[1].Value;
                missionId = uint.Parse(match.Groups[2].Value);
                name = spotName
                  + " "
                  + (Dalamud.GameData.GetExcelSheet<WKSMissionUnit>().GetRowOrDefault(missionId)?.Name.ExtractText() ?? "未知");
            }

            text += $"\n        // {name}\n";
            foreach (var fish in spot.Items)
            {
                text += $"        data.Apply({fish.ItemId}, {patch}) // {fish.Name}\n";
                text += "            .Bait(data)\n";
                if (missionId is not 0)
                {
                    text += $"            .Mission(data, {missionId})\n";
                }

                text += "            .Bite(data, HookSet.Unknown, BiteType.Unknown);\n";
            }
        }

        ImGui.SetClipboardText(text);
    }
}
