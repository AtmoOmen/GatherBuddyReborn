using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using GatherBuddy.Config;
using GatherBuddy.Enums;
using GatherBuddy.FishTimer;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using GatherBuddy.RebornUI.Tables;
using GatherBuddy.Time;
using GatherBuddy.Weather;
using Newtonsoft.Json;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Effects = GatherBuddy.Models.Effects;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private sealed class RecordTable : Table<FishRecord>
    {
        public const string FileNamePopup = "FileNamePopup";

        public RecordTable()
            : base("Fish Records", _plugin.FishRecorder.Records, _catchHeader, _baitHeader, _durationHeader, _castStartHeader,
                _biteTypeHeader, _hookHeader, _amountHeader, _spotHeader, _contentIdHeader, _gatheringHeader, _perceptionHeader, _sizeHeader,
                _flagHeader)
            => Flags |= ImGuiTableFlags.Resizable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable;

        private int _lastCount;
        private static int _deleteIdx = -1;

        protected override void PreDraw()
        {
            if (_deleteIdx > -1)
            {
                _plugin.FishRecorder.Remove(_deleteIdx);
                _deleteIdx = -1;
            }

            if (_lastCount != Items.Count)
            {
                FilterDirty = true;
                _lastCount = Items.Count;
            }
        }

        private static readonly ContentIdHeader _contentIdHeader = new() { Label = "Content ID", LocalizedLabel = "角色 ID" };
        private static readonly BaitHeader _baitHeader = new() { Label = "Bait", LocalizedLabel = "钓饵" };
        private static readonly SpotHeader _spotHeader = new() { Label = "Fishing Spot", LocalizedLabel = "渔场" };
        private static readonly CatchHeader _catchHeader = new() { Label = "Caught Fish", LocalizedLabel = "已捕获" };
        private static readonly CastStartHeader _castStartHeader = new() { Label = "TimeStamp", LocalizedLabel = "时间戳" };
        private static readonly BiteTypeHeader _biteTypeHeader = new() { Label = "Tug", LocalizedLabel = "咬钩强度" };
        private static readonly HookHeader _hookHeader = new() { Label = "Hookset", LocalizedLabel = "提钩" };
        private static readonly DurationHeader _durationHeader = new() { Label = "Bite", LocalizedLabel = "持续时间" };
        private static readonly GatheringHeader _gatheringHeader = new() { Label = "Gath.", LocalizedLabel = "获得力" };
        private static readonly PerceptionHeader _perceptionHeader = new() { Label = "Perc.", LocalizedLabel = "鉴别力" };
        private static readonly AmountHeader _amountHeader = new() { Label = "Amt", LocalizedLabel = "数量" };
        private static readonly SizeHeader _sizeHeader = new() { Label = "Ilm", LocalizedLabel = "尺寸" };
        private static readonly FlagHeader _flagHeader = new() { Label = "Flags", LocalizedLabel = "标志" };

        private sealed class GatheringHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
                => record.Gathering.ToString();

            public override float Width
                => 50 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Gathering.CompareTo(rhs.Gathering);

            public override void DrawColumn(FishRecord record, int _)
                => Ui.RightAligned(ToName(record));
        }

        private sealed class PerceptionHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
                => record.Perception.ToString();

            public override float Width
                => 50 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Perception.CompareTo(rhs.Perception);

            public override void DrawColumn(FishRecord record, int _)
                => Ui.RightAligned(ToName(record));
        }

        private sealed class AmountHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
                => record.Amount.ToString();

            public override float Width
                => 35 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Amount.CompareTo(rhs.Amount);

            public override void DrawColumn(FishRecord record, int _)
            {
                Ui.RightAligned(ToName(record));
            }
        }

        private sealed class SizeHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
                => $"{record.Size / 10f:F1}";

            public override float Width
                => 50 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Size.CompareTo(rhs.Size);

            public override void DrawColumn(FishRecord record, int _)
            {
                var tt = string.Empty;
                if (record.Flags.HasFlag(Effects.Large))
                {
                    tt = "大尺寸！";
                }

                if (record.Flags.HasFlag(Effects.Collectible))
                {
                    tt += tt.Length > 0 ? "\n收藏品！" : "收藏品！";
                }

                using var color = ImRaii.PushColor(ImGuiCol.Text, ColorId.DisabledText.Value(), tt.Length == 0);
                Ui.RightAligned(ToName(record));
                M3Tooltip.Hovered(tt);
            }
        }


        private sealed class ContentIdHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord item)
                => item.Flags.HasFlag(Effects.Legacy) ? "旧版记录" : item.ContentIdHash.ToString("X8");

            public override float Width
                => 75 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.ContentIdHash.CompareTo(rhs.ContentIdHash);
        }

        private sealed class BaitHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord item)
                => item.Bait.Name;

            public override float Width
                => 150 * ImGuiHelpers.GlobalScale;
        }

        private sealed class SpotHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord item)
                => item.FishingSpot?.Name ?? "未知";

            public override float Width
                => 200 * ImGuiHelpers.GlobalScale;
        }

        private sealed class CatchHeader : ColumnString<FishRecord>
        {
            public CatchHeader()
            {
                Flags |= ImGuiTableColumnFlags.NoHide;
                Flags |= ImGuiTableColumnFlags.NoReorder;
            }

            public override string ToName(FishRecord record)
                => record.Catch?.Name[GatherBuddy.Language] ?? "无";

            public override float Width
                => 200 * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(FishRecord record, int idx)
            {
                base.DrawColumn(record, idx);
                if (ImGui.GetIO().KeyCtrl && ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _deleteIdx = idx;
                }

                M3Tooltip.Hovered("按住 Ctrl 并右键点击以删除…");
            }
        }

        private sealed class CastStartHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
            {
                if (!GatherBuddy.Config.UseUnixTimeFishRecords)
                {
                    return (record.TimeStamp.Time / 1000).ToString();
                }

                var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(record.TimeStamp.Time).ToLocalTime();
                return dateTime.ToString("g");
            }

            public override float Width
                => 80 * ImGuiHelpers.GlobalScale;

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.TimeStamp.CompareTo(rhs.TimeStamp);

            public override void DrawColumn(FishRecord record, int _)
            {
                base.DrawColumn(record, _);
                if (ImGui.IsItemHovered())
                {
                    using var tt = ImRaii.Tooltip();
                    ImGui.TextUnformatted($"{record.TimeStamp}");
                    var et = record.TimeStamp.ConvertToEorzea().RoundToSecond();
                    ImGui.TextUnformatted($"{et.CurrentHour:D2}:{et.CurrentMinute:D2}:{et.CurrentSecond:D2} ET");
                }
            }
        }

        [Flags]
        private enum TugTypeFilter : byte
        {
            Weak = 0x01,
            Strong = 0x02,
            Legendary = 0x04,
            Unknown = 0x08,
            None = 0x10,
        }

        private sealed class BiteTypeHeader : ColumnFlags<TugTypeFilter, FishRecord>
        {
            private static readonly string[] FlagNames =
                ["轻竿###Weak", "普通竿###Strong", "鱼王竿###Legendary", "未知###Unknown", "无###None"];

            protected override string[] Names
                => FlagNames;

            public BiteTypeHeader()
            {
                AllFlags = TugTypeFilter.Weak | TugTypeFilter.Strong | TugTypeFilter.Legendary | TugTypeFilter.Unknown | TugTypeFilter.None;
                _filter = AllFlags;
            }

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Tug.CompareTo(rhs.Tug);

            public override void DrawColumn(FishRecord item, int idx)
                => ImGui.Text(item.Tug switch
                {
                    BiteType.Weak => "轻竿",
                    BiteType.Strong => "普通竿",
                    BiteType.Legendary => "鱼王竿",
                    BiteType.None => "无",
                    _ => "未知",
                });

            private TugTypeFilter _filter;

            protected override void SetValue(TugTypeFilter value, bool enable)
            {
                if (enable)
                {
                    _filter |= value;
                }
                else
                {
                    _filter &= ~value;
                }
            }

            public override TugTypeFilter FilterValue
                => _filter;

            public override bool FilterFunc(FishRecord item)
                => item.Tug switch
                {
                    BiteType.Weak => _filter.HasFlag(TugTypeFilter.Weak),
                    BiteType.Strong => _filter.HasFlag(TugTypeFilter.Strong),
                    BiteType.Legendary => _filter.HasFlag(TugTypeFilter.Legendary),
                    BiteType.None => _filter.HasFlag(TugTypeFilter.None),
                    _ => _filter.HasFlag(TugTypeFilter.Unknown),
                };

            public override float Width
                => 60 * ImGuiHelpers.GlobalScale;
        }

        [Flags]
        private enum HookSetFilter : byte
        {
            Regular = 0x01,
            Precise = 0x02,
            Powerful = 0x04,
            Double = 0x08,
            Triple = 0x10,
            Unknown = 0x20,
            Stellar = 0x40,
            None = 0x80,
        }

        private sealed class HookHeader : ColumnFlags<HookSetFilter, FishRecord>
        {
            private static readonly string[] FlagNames =
                ["提钩###Regular", "精准提钩###Precise", "强力提钩###Powerful", "双重提钩###Double", "三重提钩###Triple",
                 "未知###Unknown", "星际提钩###Stellar", "无###None"];

            protected override string[] Names
                => FlagNames;

            public HookHeader()
            {
                AllFlags = HookSetFilter.Precise
                  | HookSetFilter.Powerful
                  | HookSetFilter.Regular
                  | HookSetFilter.Double
                  | HookSetFilter.Triple
                  | HookSetFilter.Stellar
                  | HookSetFilter.Unknown
                  | HookSetFilter.None;
                _filter = AllFlags;
            }

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Hook.CompareTo(rhs.Hook);

            public override void DrawColumn(FishRecord item, int idx)
                => ImGui.Text(item.Hook switch
                {
                    HookSet.Hook => "提钩",
                    HookSet.Precise => "精准提钩",
                    HookSet.Powerful => "强力提钩",
                    HookSet.DoubleHook => "双重提钩",
                    HookSet.TripleHook => "三重提钩",
                    HookSet.Stellar => "星际提钩",
                    HookSet.None => "无",
                    _ => "未知",
                });

            private HookSetFilter _filter;

            protected override void SetValue(HookSetFilter value, bool enable)
            {
                if (enable)
                {
                    _filter |= value;
                }
                else
                {
                    _filter &= ~value;
                }
            }

            public override HookSetFilter FilterValue
                => _filter;

            public override bool FilterFunc(FishRecord item)
                => item.Hook switch
                {
                    HookSet.Precise => _filter.HasFlag(HookSetFilter.Precise),
                    HookSet.Powerful => _filter.HasFlag(HookSetFilter.Powerful),
                    HookSet.Hook => _filter.HasFlag(HookSetFilter.Regular),
                    HookSet.DoubleHook => _filter.HasFlag(HookSetFilter.Double),
                    HookSet.TripleHook => _filter.HasFlag(HookSetFilter.Triple),
                    HookSet.Stellar => _filter.HasFlag(HookSetFilter.Stellar),
                    HookSet.None => _filter.HasFlag(HookSetFilter.None),
                    _ => _filter.HasFlag(HookSetFilter.Unknown),
                };

            public override float Width
                => 75 * ImGuiHelpers.GlobalScale;
        }

        private sealed class DurationHeader : ColumnString<FishRecord>
        {
            public override string ToName(FishRecord record)
                => $"{record.Bite / 1000}.{record.Bite % 1000:D3}";

            public override float Width
                => 50 * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(FishRecord record, int _)
                => Ui.RightAligned(ToName(record));

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Bite.CompareTo(rhs.Bite);
        }

        private class FlagHeader : TriStateColumnFlags<FlagHeader.ColumnEffects, FishRecord>
        {
            private float _iconScale;
            private readonly (ISharedImmediateTexture, Effects)[] _effects;

            [Flags]
            public enum ColumnEffects : ulong
            {
                LargeCatch = Effects.Large,
                AverageCatch = (ulong)Effects.Large << 32,
                CollectibleOn = Effects.Collectible,
                CollectibleOff = (ulong)Effects.Collectible << 32,
                PatienceOn = Effects.Patience,
                PatienceOff = (ulong)Effects.Patience << 32,
                Patience2On = Effects.Patience2,
                Patience2Off = (ulong)Effects.Patience2 << 32,
                IntuitionOn = Effects.Intuition,
                IntuitionOff = (ulong)Effects.Intuition << 32,
                SnaggingOn = Effects.Snagging,
                SnaggingOff = (ulong)Effects.Snagging << 32,
                FishEyesOn = Effects.FishEyes,
                FishEyesOff = (ulong)Effects.FishEyes << 32,
                ChumOn = Effects.Chum,
                ChumOff = (ulong)Effects.Chum << 32,
                PrizeCatchOn = Effects.PrizeCatch,
                PrizeCatchOff = (ulong)Effects.PrizeCatch << 32,
                IdenticalCastOn = Effects.IdenticalCast,
                IdenticalCastOff = (ulong)Effects.IdenticalCast << 32,
                SurfaceSlapOn = Effects.SurfaceSlap,
                SurfaceSlapOff = (ulong)Effects.SurfaceSlap << 32,
                BigGameFishingOn = Effects.BigGameFishing,
                BigGameFishingOff = (ulong)Effects.BigGameFishing << 32,
                AmbitiousLureOn = Effects.AmbitiousLure1 | Effects.AmbitiousLure2,
                AmbitiousLureOff = (ulong)(Effects.AmbitiousLure1 | Effects.AmbitiousLure2) << 32,
                ModestLureOn = Effects.ModestLure1 | Effects.ModestLure2,
                ModestLureOff = (ulong)(Effects.ModestLure1 | Effects.ModestLure2) << 32,
            }

            private static readonly ColumnEffects Mask = CombineColumnEffects();

            private static ColumnEffects CombineColumnEffects()
            {
                var mask = default(ColumnEffects);
                foreach (var effect in Enum.GetValues<ColumnEffects>())
                {
                    mask |= effect;
                }

                return mask;
            }

            private static readonly (ColumnEffects On, ColumnEffects Off)[] _values =
            [
                (ColumnEffects.LargeCatch, ColumnEffects.AverageCatch),
                (ColumnEffects.CollectibleOn, ColumnEffects.CollectibleOff),
                (ColumnEffects.PatienceOn, ColumnEffects.PatienceOff),
                (ColumnEffects.Patience2On, ColumnEffects.Patience2Off),
                (ColumnEffects.IntuitionOn, ColumnEffects.IntuitionOff),
                (ColumnEffects.SnaggingOn, ColumnEffects.SnaggingOff),
                (ColumnEffects.FishEyesOn, ColumnEffects.FishEyesOff),
                (ColumnEffects.ChumOn, ColumnEffects.ChumOff),
                (ColumnEffects.PrizeCatchOn, ColumnEffects.PrizeCatchOff),
                (ColumnEffects.IdenticalCastOn, ColumnEffects.IdenticalCastOff),
                (ColumnEffects.SurfaceSlapOn, ColumnEffects.SurfaceSlapOff),
                (ColumnEffects.BigGameFishingOn, ColumnEffects.BigGameFishingOff),
                (ColumnEffects.AmbitiousLureOn, ColumnEffects.AmbitiousLureOff),
                (ColumnEffects.ModestLureOn, ColumnEffects.ModestLureOff),
            ];

            private static readonly string[] _names =
            [
                "嘉惠",
                "收藏品",
                "耐心",
                "耐心 II",
                "捕鱼人之识",
                "钓组",
                "鱼眼",
                "撒饵",
                "大鱼猎手",
                "专一垂钓",
                "拍击水面",
                "大鱼的知识",
                "雄心之饵",
                "谦逊之饵",
            ];

            protected override IReadOnlyList<(ColumnEffects On, ColumnEffects Off)> Values
                => _values;

            protected override string[] Names
                => _names;

            protected override void SetValue(ColumnEffects value, bool enable)
            {
                if (enable)
                {
                    _filter |= value;
                }
                else
                {
                    _filter &= ~value;
                }
            }

            protected override void SetValue((ColumnEffects On, ColumnEffects Off) value, bool? enable)
            {
                switch (enable)
                {
                    case null:
                        _filter |= value.On | value.Off;
                        break;
                    case true:
                        _filter = (_filter | value.On) & ~value.Off;
                        break;
                    case false:
                        _filter = (_filter | value.Off) & ~value.On;
                        break;
                }
            }

            private ColumnEffects _filter;

            public FlagHeader()
            {
                _effects =
                [
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211112), (Effects)_values[00].On), // Nature's Bounty
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211008), (Effects)_values[01].On), // Collector's Glove
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(216023), (Effects)_values[02].On), // Patience
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211106), (Effects)_values[03].On), // Patience II
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211101), (Effects)_values[04].On), // Intuition
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211102), (Effects)_values[05].On), // Snagging
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211103), (Effects)_values[06].On), // Fish Eyes
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211104), (Effects)_values[07].On), // Chum
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211119), (Effects)_values[08].On), // Prize Catch
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211116), (Effects)_values[09].On), // Identical Cast
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211115), (Effects)_values[10].On), // Surface Slap
                    (Icons.DefaultStorage.TextureProvider.GetFromGameIcon(211122), (Effects)_values[11].On), // Big Game Fishing
                ];
                AllFlags = Mask;
                _filter = AllFlags;
                ComboFlags |= ImGuiComboFlags.HeightLarge;
            }

            public override float Width
            {
                get
                {
                    if (_iconScale == 0)
                    {
                        var scale = _effects[0].Item1.TryGetWrap(out var wrap, out _) ? (float)wrap.Width / wrap.Height : 0;
                        if (scale == 0)
                        {
                            return 10 * (TextHeight + 1);
                        }

                        _iconScale = scale;
                    }

                    return 14 * (_iconScale * TextHeight + 1);
                }
            }

            public override bool FilterFunc(FishRecord item)
            {
                var enabled = (Effects)(_filter & Mask);
                var disabled = (Effects)(((ulong)_filter >> 32) & (ulong)Mask);
                var flags = item.Flags & (Effects)Mask;
                var invFlags = ~flags & (Effects)Mask;
                return (flags & enabled) == flags && (invFlags & disabled) == invFlags;
            }

            public override int Compare(FishRecord lhs, FishRecord rhs)
                => lhs.Flags.CompareTo(rhs.Flags);

            public override ColumnEffects FilterValue
                => _filter;

            private void DrawIcon(FishRecord item, ISharedImmediateTexture icon, Effects flag)
                => DrawIcon(icon, item.Flags.HasFlag(flag), flag switch
                {
                    Effects.Large => "大尺寸",
                    Effects.Collectible => "收藏品",
                    Effects.Patience => "耐心",
                    Effects.Patience2 => "耐心 II",
                    Effects.Intuition => "捕鱼人之识",
                    Effects.Snagging => "钓组",
                    Effects.FishEyes => "鱼眼",
                    Effects.Chum => "撒饵",
                    Effects.PrizeCatch => "大鱼猎手",
                    Effects.IdenticalCast => "专一垂钓",
                    Effects.SurfaceSlap => "拍击水面",
                    Effects.BigGameFishing => "大鱼的知识",
                    _ => flag.ToString(),
                });

            private void DrawIcon(ISharedImmediateTexture icon, bool enabled, string tooltip)
            {
                var size = new Vector2(TextHeight * _iconScale, TextHeight);
                var tint = enabled ? Vector4.One : new Vector4(0.75f, 0.75f, 0.75f, 0.5f);
                if (!icon.TryGetWrap(out var wrap, out _))
                {
                    ImGui.Dummy(size);
                    return;
                }

                ImGui.Image(wrap.Handle, size, Vector2.Zero, Vector2.One, tint);
                if (!ImGui.IsItemHovered())
                {
                    return;
                }

                using var tt = ImRaii.Tooltip();
                ImGui.Image(wrap.Handle, new Vector2(wrap.Width, wrap.Height));
                ImGui.TextUnformatted(tooltip);
            }

            public override void DrawColumn(FishRecord item, int idx)
            {
                using var space = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.One);
                foreach (var (icon, flag) in _effects)
                {
                    DrawIcon(item, icon, flag);
                    ImGui.SameLine();
                }

                switch (item.Flags.AmbitiousLure())
                {
                    case 0:
                        DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218905), false, "雄心之饵");
                        ImGui.SameLine();
                        switch (item.Flags.ModestLure())
                        {
                            case 0:
                                DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218909), false, "谦逊之饵");
                                break;
                            case 1:
                                DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218909), true, "谦逊之饵");
                                break;
                            case 2:
                                DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218910), true, "谦逊之饵");
                                break;
                            case 3:
                                DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218911), true, "谦逊之饵");
                                break;
                        }

                        return;
                    case 1:
                        DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218905), true, "雄心之饵");
                        break;
                    case 2:
                        DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218906), true, "雄心之饵");
                        break;
                    case 3:
                        DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218907), true, "雄心之饵");
                        break;
                }

                ImGui.SameLine();
                DrawIcon(Icons.DefaultStorage.TextureProvider.GetFromGameIcon(218909), false, "谦逊之饵");
            }
        }

        public string CreateTsv()
        {
            var sb = new StringBuilder(Items.Count * 128);
            sb.Append(
                "Fish\tFishId\tBite\tBait\tBaitId\tSpot\tSpotId\tTug\tHookset\tTimestamp\tEorzea Time\tTransition\tWeather\tAmount\tIlm\tGathering\tPerception\tPatience\tPatience2\tIntuition\tSnagging\tFish Eyes\tChum\tPrize Catch\tIdentical Cast\tSurface Slap\tCollectible\tBig Game Fishing\tAmbitious Lure\tModest Lure\n");
            var sortedRecords = new List<(FishRecord Record, int Index)>(Items.Count);
            foreach (var item in Items)
            {
                sortedRecords.Add((item, sortedRecords.Count));
            }

            sortedRecords.Sort(static (lhs, rhs) =>
            {
                var order = lhs.Record.TimeStamp.CompareTo(rhs.Record.TimeStamp);
                return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
            });
            foreach (var (record, _) in sortedRecords)
            {
                var (hour, minute) = record.TimeStamp.CurrentEorzeaTimeOfDay();
                var spot = record.FishingSpot;
                var (weather, transition) = ("Unknown", "Unknown");
                if (spot != null)
                {
                    var weathers = WeatherManager.GetForecast(spot.Territory, 2, record.TimeStamp.AddEorzeaHours(-8));
                    transition = weathers[0].Weather.Name;
                    weather = weathers[1].Weather.Name;
                }

                sb.Append(_catchHeader.ToName(record)).Append('\t')
                    .Append(record.CatchId).Append('\t')
                    .Append(_durationHeader.ToName(record)).Append('\t')
                    .Append(_baitHeader.ToName(record)).Append('\t')
                    .Append(record.BaitId).Append('\t')
                    .Append(_spotHeader.ToName(record)).Append('\t')
                    .Append(record.SpotId).Append('\t')
                    .Append(record.Tug.ToString()).Append('\t')
                    .Append(record.Hook.ToString()).Append('\t')
                    .Append(_castStartHeader.ToName(record)).Append('\t')
                    .Append($"{hour}:{minute:D2}").Append('\t')
                    .Append(transition).Append('\t')
                    .Append(weather).Append('\t')
                    .Append(_amountHeader.ToName(record)).Append('\t')
                    .Append(_sizeHeader.ToName(record)).Append('\t')
                    .Append(_gatheringHeader.ToName(record)).Append('\t')
                    .Append(_perceptionHeader.ToName(record)).Append('\t')
                    .Append(record.Flags.HasFlag(Effects.Patience) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.Patience2) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.Intuition) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.Snagging) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.FishEyes) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.Chum) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.PrizeCatch) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.IdenticalCast) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.SurfaceSlap) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.Collectible) ? "x\t" : "\t")
                    .Append(record.Flags.HasFlag(Effects.BigGameFishing) ? "x\t" : "\t")
                    .Append($"{record.Flags.AmbitiousLure()}\t")
                    .Append($"{record.Flags.ModestLure()}\t")
                    .Append('\n');
            }

            return sb.ToString();
        }
    }

    private readonly RecordTable _recordTable;
    private bool WriteTsv = false;
    private bool WriteJson = false;


    private const string RecordExportDialogId = "##recordExportDialog";
    private string _recordExportPath = string.Empty;

    private void DrawRecordPage()
    {
        using var id = ImRaii.PushId("Fish Records");
        _recordTable.ExtraHeight = TableFooterHeight;
        _recordTable.Draw(ImGui.GetTextLineHeightWithSpacing());

        ImGui.Dummy(new Vector2(0, M3.Space1));
        var shown = _recordTable.CurrentItems;
        var total = _recordTable.TotalItems;
        var row = new Ui.Row(M3Widgets.ButtonHeight);
        row.Place(ImGui.GetFrameHeight());
        M3Badge.Draw(shown == total ? $"{total} 条记录" : $"显示 {shown} / {total} 条记录", shown == total ? M3.Scheme.Secondary : M3.Scheme.Primary,
            shown == total ? null : "列筛选条件隐藏了部分记录。");

        void NextButton()
            => row.Next(M3Widgets.ButtonHeight, M3.Space2);

        NextButton();
        if (M3Widgets.Button("##cleanup", "清理", M3ButtonStyle.Tonal, FontAwesomeIcon.Broom,
                tooltip: "删除所有标记为无效的记录，以及角色 ID 和时间戳均相同的重复记录。\n"
                  + "通常不会有此类记录。清理前不会自动创建备份，请谨慎操作。"))
        {
            _plugin.FishRecorder.RemoveDuplicates();
            _plugin.FishRecorder.RemoveInvalid();
        }

        NextButton();
        if (M3Widgets.Button("##copy", "复制到剪贴板", M3ButtonStyle.Tonal, FontAwesomeIcon.Copy,
                tooltip: "将所有钓鱼记录复制到剪贴板以便分享。数据量可能很大。"))
        {
            try
            {
                ImGui.SetClipboardText(_plugin.FishRecorder.ExportBase64());
                M3Snackbar.Show($"已复制 {total} 条钓鱼记录。");
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Warning($"Could not copy fish records:\n{e}");
            }
        }

        NextButton();
        if (M3Widgets.Button("##import", "从剪贴板导入", M3ButtonStyle.Tonal, FontAwesomeIcon.Paste,
                tooltip: "从剪贴板导入别人分享的钓鱼记录。重复记录将跳过。"))
        {
            try
            {
                _plugin.FishRecorder.ImportBase64(ImGui.GetClipboardText());
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Warning($"Could not import fish records:\n{e}");
            }
        }

        NextButton();
        if (M3Widgets.Button("##json", "导出 JSON", M3ButtonStyle.Text, FontAwesomeIcon.FileExport,
                tooltip: "将全部记录导出为一个 JSON 文件。"))
        {
            WriteJson = true;
            WriteTsv = false;
            M3Dialog.Open(RecordExportDialogId);
        }

        NextButton();
        if (M3Widgets.Button("##tsv", "导出 TSV", M3ButtonStyle.Text, FontAwesomeIcon.FileCsv,
                tooltip: "将全部记录导出为一个制表符分隔文件。"))
        {
            WriteTsv = true;
            WriteJson = false;
            M3Dialog.Open(RecordExportDialogId);
        }

        NextButton();
        if (M3Widgets.Button("##caught", "复制已捕获鱼类", M3ButtonStyle.Text, FontAwesomeIcon.ClipboardList,
                tooltip: "将钓鱼手册中已捕获鱼类的物品 ID 复制为 JSON。"))
        {
            try
            {
                var logFishCount = 0;
                var ids = new List<uint>();
                foreach (var fish in GatherBuddy.GameData.Fishes.Values)
                {
                    if (!fish.InLog || fish.FishingSpots.Count == 0)
                    {
                        continue;
                    }

                    logFishCount++;
                    if (GatherBuddy.FishLog.IsUnlocked(fish))
                    {
                        ids.Add(fish.ItemId);
                    }
                }

                Communicator.PrintClipboardMessage("列表 ", $"已捕获的鱼 {ids.Count}/{logFishCount} ");
                ImGui.SetClipboardText(JsonConvert.SerializeObject(ids, Formatting.Indented));
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Warning($"Could not copy the caught fish:\n{e}");
            }
        }

        DrawRecordExportDialog();
    }

    private void DrawRecordExportDialog()
    {
        using var dialog = M3Dialog.Begin(RecordExportDialogId, WriteTsv ? "导出钓鱼记录为 TSV" : "导出钓鱼记录为 JSON",
            "输入输出文件的完整路径。已有文件会被覆盖。", FontAwesomeIcon.FileExport, 420f);
        if (!dialog.IsOpen)
        {
            return;
        }

        M3Widgets.TextField("##path", "文件路径", ref _recordExportPath, ImGui.GetContentRegionAvail().X, 512);
        var pressed = M3Dialog.Actions(
        [
            new M3DialogAction("取消"),
            new M3DialogAction("导出", M3ButtonStyle.Filled, FontAwesomeIcon.Save, _recordExportPath.Trim().Length > 0),
        ]);
        if (pressed != 1)
        {
            return;
        }

        var name = _recordExportPath.Trim();
        if (WriteJson)
        {
            try
            {
                _plugin.FishRecorder.ExportJson(new FileInfo(name));
                M3Snackbar.Show($"已导出 {_recordTable.TotalItems} 条钓鱼记录。");
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Warning($"Could not export the json file to {name}:\n{e}");
                M3Snackbar.Show("无法导出钓鱼记录。", severity: M3Severity.Error);
            }
        }
        else if (WriteTsv)
        {
            try
            {
                File.WriteAllText(name, _recordTable.CreateTsv());
                GatherBuddy.Log.Information($"将 {_recordTable.TotalItems} 中的钓鱼记录导出至 {name}.");
                M3Snackbar.Show($"已导出 {_recordTable.TotalItems} 条钓鱼记录。");
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Warning($"无法导出tsv文件: {name}:\n{e}");
                M3Snackbar.Show("无法导出钓鱼记录。", severity: M3Severity.Error);
            }
        }

        WriteJson = false;
        WriteTsv = false;
    }
}
