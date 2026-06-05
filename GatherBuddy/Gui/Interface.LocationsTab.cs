using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Enums;
using GatherBuddy.Interfaces;
using GatherBuddy.Structs;
using ElliLib;
using ElliLib.Table;
using ElliLib.Widgets;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private sealed class LocationTable : Table<ILocation>
    {
        private static float _nameColumnWidth;
        private static float _territoryColumnWidth;
        private static float _aetheryteColumnWidth;
        private static float _coordColumnWidth;
        private static float _radiusColumnWidth;
        private static float _typeColumnWidth;
        private static float _levelColumnWidth;

        protected override void PreDraw()
        {
            if (_nameColumnWidth != 0)
                return;

            _nameColumnWidth      = _plugin.LocationManager.AllLocations.Max(l => TextWidth(l.Name)) / ImGuiHelpers.GlobalScale;
            _territoryColumnWidth = _plugin.LocationManager.AllLocations.Max(l => TextWidth(l.Territory.Name)) / ImGuiHelpers.GlobalScale;
            _aetheryteColumnWidth = GatherBuddy.GameData.Aetherytes.Values.Max(a => TextWidth(a.Name)) / ImGuiHelpers.GlobalScale;
            _coordColumnWidth     = TextWidth("X坐标") / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _radiusColumnWidth    = TextWidth("半径") / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _levelColumnWidth    = TextWidth("等级") / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _typeColumnWidth      = Enum.GetValues<GatheringType>().Max(t => TextWidth(t.ToString())) / ImGuiHelpers.GlobalScale;
        }


        private static readonly NameColumn      _nameColumn      = new() { Label = "名称" };
        private static readonly TypeColumn      _typeColumn      = new() { Label = "类型" };
        private static readonly TerritoryColumn _territoryColumn = new() { Label = "区域" };
        private static readonly LevelColumn     _levelColumn     = new() { Label = "等级" };
        private static readonly AetheryteColumn _aetheryteColumn = new() { Label = "传送点" };
        private static readonly XCoordColumn    _xCoordColumn    = new() { Label = "X坐标" };
        private static readonly YCoordColumn    _yCoordColumn    = new() { Label = "Y坐标" };
        private static readonly RadiusColumn    _radiusColumn    = new() { Label = "半径" };
        private static readonly MarkerColumn    _markerColumn    = new() { Label = "标记" };
        private static readonly ItemColumn      _itemColumn      = new() { Label = "物品" };
        private sealed class NameColumn : ColumnString<ILocation>
        {
            public NameColumn()
                => Flags |= ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoReorder;

            public override string ToName(ILocation location)
                => location.Name;

            public override float Width
                => _nameColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
            {
                ImGui.AlignTextToFramePadding();
                base.DrawColumn(item, _);
            }
        }

        private sealed class TypeColumn : ColumnFlags<JobFlags, ILocation>
        {
            private static readonly JobFlags[] _flagValues =
            {
                JobFlags.Logging,      // 伐木
                JobFlags.Harvesting,   // 割草
                JobFlags.Mining,       // 采矿
                JobFlags.Quarrying,    // 碎石
                JobFlags.Fishing,      // 钓鱼
                JobFlags.Spearfishing  // 刺鱼
            };

            private static readonly string[] _flagNames =
            {
                "伐木",
                "割草",
                "采矿",
                "碎石",
                "钓鱼",
                "刺鱼"
            };

            public TypeColumn()
            {
                AllFlags = _flagValues.Aggregate((a, b) => a | b);
            }

            protected override IReadOnlyList<JobFlags> Values => _flagValues;

            protected override string[] Names => _flagNames;

            public override JobFlags FilterValue => GatherBuddy.Config.LocationFilter;

            protected override void SetValue(JobFlags value, bool enable)
            {
                var val = enable
                    ? GatherBuddy.Config.LocationFilter | value
                    : GatherBuddy.Config.LocationFilter & ~value;

                if (val != GatherBuddy.Config.LocationFilter)
                {
                    GatherBuddy.Config.LocationFilter = val;
                    GatherBuddy.Config.Save();
                }
            }

            public override void DrawColumn(ILocation location, int _)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text(EnumLocalization.Get(location.GatheringType));
            }

            public override int Compare(ILocation a, ILocation b)
                => a.GatheringType.CompareTo(b.GatheringType);

            public override bool FilterFunc(ILocation location)
            {
                return location.GatheringType switch
                {
                    GatheringType.采伐 => FilterValue.HasFlag(JobFlags.Logging),
                    GatheringType.割草 => FilterValue.HasFlag(JobFlags.Harvesting),
                    GatheringType.采掘 => FilterValue.HasFlag(JobFlags.Mining),
                    GatheringType.碎石 => FilterValue.HasFlag(JobFlags.Quarrying),
                    GatheringType.捕鱼人 => FilterValue.HasFlag(JobFlags.Fishing),
                    GatheringType.刺鱼 => FilterValue.HasFlag(JobFlags.Spearfishing),
                    _ => false
                };
            }

            public override float Width => _typeColumnWidth * ImGuiHelpers.GlobalScale;
        }

        private sealed class TerritoryColumn : ColumnString<ILocation>
        {
            public override string ToName(ILocation location)
                => location.Territory.Name;

            public override float Width
                => _territoryColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
            {
                ImGui.AlignTextToFramePadding();
                base.DrawColumn(item, _);
            }
        }

        private sealed class LevelColumn() : ColumnNumber<ILocation>(ComparisonMethod.LessEqual)
        {
            public override int ToValue(ILocation item)
                => (item as GatheringNode)?.Level ?? 1;

            public override float Width
                => _levelColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
            {
                ImGui.AlignTextToFramePadding();
                base.DrawColumn(item, _);
            }
        }

        private sealed class ItemColumn : ColumnString<ILocation>
        {
            public ItemColumn()
                => Flags |= ImGuiTableColumnFlags.WidthStretch;

            public override string ToName(ILocation location)
                => string.Join(", ", location.Gatherables.Select(g => g.Name[GatherBuddy.Language]));

            public override float Width
                => 0;

            public override void DrawColumn(ILocation item, int _)
            {
                ImGui.AlignTextToFramePadding();
                base.DrawColumn(item, _);
            }
        }

        private sealed class AetheryteColumn : ColumnString<ILocation>
        {
            private readonly List<Aetheryte>                   _aetherytes;
            private readonly ClippedSelectableCombo<Aetheryte> _aetheryteCombo;

            public AetheryteColumn()
            {
                _aetherytes     = GatherBuddy.GameData.Aetherytes.Values.ToList();
                _aetheryteCombo = new ClippedSelectableCombo<Aetheryte>("##aetheryte", string.Empty, 200, _aetherytes, a => a.Name);
            }

            public override string ToName(ILocation location)
                => location.ClosestAetheryte?.Name ?? "无";

            public override float Width
                => _aetheryteColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation location, int _)
            {
                var       overwritten = location.DefaultAetheryte != location.ClosestAetheryte;
                using var color       = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                var       currentName = location.ClosestAetheryte?.Name ?? "无";
                if (_aetheryteCombo.Draw(currentName, out var newIdx))
                    _plugin.LocationManager.SetAetheryte(location, _aetherytes[newIdx]);
                if (overwritten)
                {
                    ImGuiUtil.HoverTooltip($"右键点击以重载默认设置。 ({location.DefaultAetheryte?.Name ?? "无"})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        _plugin.LocationManager.SetAetheryte(location, location.DefaultAetheryte);
                }
            }
        }

        private sealed class XCoordColumn : ColumnString<ILocation>
        {
            public override string ToName(ILocation location)
                => (location.IntegralXCoord / 100f).ToString("0.00", CultureInfo.InvariantCulture);

            public override float Width
                => _coordColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation location, int _)
            {
                var       overwritten = location.DefaultXCoord != location.IntegralXCoord;
                using var color       = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                var       x           = location.IntegralXCoord / 100f;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##x", ref x, 0.05f, 1f, 42f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                    _plugin.LocationManager.SetXCoord(location, (int)(x * 100f + 0.5f));
                if (overwritten)
                {
                    ImGuiUtil.HoverTooltip($"右键点击以重载默认设置。 ({location.DefaultXCoord / 100f:0.00})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        _plugin.LocationManager.SetXCoord(location, location.DefaultXCoord);
                }
            }

            public override int Compare(ILocation a, ILocation b)
                => a.IntegralXCoord.CompareTo(b.IntegralXCoord);
        }

        private sealed class YCoordColumn : ColumnString<ILocation>
        {
            public override string ToName(ILocation location)
                => location.IntegralYCoord.ToString("0.00", CultureInfo.InvariantCulture);

            public override float Width
                => _coordColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation location, int _)
            {
                var       overwritten = location.DefaultYCoord != location.IntegralYCoord;
                using var color       = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                var       y           = location.IntegralYCoord / 100f;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##y", ref y, 0.05f, 1f, 42f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                    _plugin.LocationManager.SetYCoord(location, (int)(y * 100f + 0.5f));
                if (overwritten)
                {
                    ImGuiUtil.HoverTooltip($"右键点击以重载默认设置。 ({location.DefaultYCoord / 100f:0.00})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        _plugin.LocationManager.SetYCoord(location, location.DefaultYCoord);
                }
            }

            public override int Compare(ILocation a, ILocation b)
                => a.IntegralYCoord.CompareTo(b.IntegralYCoord);
        }

        private sealed class RadiusColumn : ColumnString<ILocation>
        {
            public override string ToName(ILocation location)
                => location.Radius.ToString();

            public override float Width
                => _radiusColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation location, int _)
            {
                var       overwritten = location.DefaultRadius != location.Radius;
                using var color       = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                ImGui.SetNextItemWidth(-1);
                int radius = location.Radius;
                if (ImGui.DragInt("##radius", ref radius, 0.1f, 0, IMarkable.RadiusMax))
                    _plugin.LocationManager.SetRadius(location, Math.Clamp((ushort)radius, (ushort)0, IMarkable.RadiusMax));
                if (overwritten)
                {
                    ImGuiUtil.HoverTooltip($"右键点击以重载默认设置。 ({location.DefaultRadius})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        _plugin.LocationManager.SetRadius(location, location.DefaultRadius);
                }
            }

            public override int Compare(ILocation a, ILocation b)
                => a.Radius.CompareTo(b.Radius);
        }

        [Flags]
        private enum MarkerFlags : byte
        {
            None = 0x01,
            Any  = 0x02,
        }

        private sealed class MarkerColumn : ColumnFlags<MarkerFlags, ILocation>
        {
            // 1. 定义筛选栏的 Flags 顺序（逻辑层）
            private static readonly MarkerFlags[] _flagValues =
            {
                MarkerFlags.None, // 无标记
                MarkerFlags.Any,  // 有标记
            };

            // 2. 定义筛选栏的中文名称（展示层）
            private static readonly string[] _flagNames =
            {
                "无标记",
                "有标记",
            };

            public MarkerColumn()
            {
                // 3. 组合所有 Flags（逻辑层）
                AllFlags = _flagValues.Aggregate((a, b) => a | b);
            }

            // 4. 覆写 Values → 告诉 ColumnFlags 用这套 Flags
            protected override IReadOnlyList<MarkerFlags> Values
                => _flagValues;

            // 5. 覆写 Names → 告诉 ColumnFlags 用这套中文名称
            protected override string[] Names
                => _flagNames;

            private MarkerFlags _filter = MarkerFlags.None | MarkerFlags.Any;

            public override MarkerFlags FilterValue
                => _filter;

            protected override void SetValue(MarkerFlags value, bool enable)
                => _filter = enable ? _filter | value : _filter & ~value;

            public override bool FilterFunc(ILocation item)
                => FilterValue.HasFlag(item.Markers.CountSet == 0 ? MarkerFlags.None : MarkerFlags.Any);

            public override int Compare(ILocation lhs, ILocation rhs)
            {
                if (lhs.Markers.CountSet != rhs.Markers.CountSet)
                    return lhs.Markers.CountSet - rhs.Markers.CountSet;

                var diff = lhs.Territory.Id.CompareTo(rhs.Territory.Id);
                if (diff != 0)
                    return diff;

                foreach (var (l, r) in lhs.Markers.Zip(rhs.Markers))
                {
                    diff = l.X.CompareTo(r.X);
                    if (diff != 0)
                        return diff;

                    diff = l.Y.CompareTo(r.Y);
                    if (diff != 0)
                        return diff;

                    diff = l.Z.CompareTo(r.Z);
                    if (diff != 0)
                        return diff;
                }

                return 0;
            }

            public override float Width
                => ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X + Table.ArrowWidth;

            public override void DrawColumn(ILocation location, int id)
            {
                using var _ = ImRaii.PushId(id);
                var markers = GatherBuddy.WaymarkManager.GetWaymarks();
                var markerCount = markers.CountSet;
                var locationCount = location.Markers.CountSet;
                var invalid = Dalamud.ClientState.TerritoryType != location.Territory.Id;

                var tt = invalid
                    ? "不在此位置的对应的目标区域"
                    : markerCount == 0
                        ? "无此位置可用的标记信息"
                        : $"为此位置存储当前已放置的标记信息:\n\n{string.Join("\n", markers.Select(m => float.IsNaN(m.X) ? " - " : $"{m.X:F2} - {m.Y:F2} - {m.Z:F2}"))}";

                if (locationCount > 0)
                    tt +=
                        $"\n\n此位置已储存标记:\n\n{string.Join("\n", location.Markers.Select(m => float.IsNaN(m.X) ? " - " : $"{m.X:F2} - {m.Y:F2} - {m.Z:F2}"))}";

                if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Map.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt,
                        markerCount == 0 || invalid, true))
                    _plugin.LocationManager.SetMarkers(location, markers);

                ImGui.SameLine();
                tt = locationCount == 0
                    ? "此位置没有已存储的标记。"
                    : $"移除此位置的已存储标记:\n\n{string.Join("\n", location.Markers.Select(m => float.IsNaN(m.X) ? " - " : $"{m.X:F2} - {m.Y:F2} - {m.Z:F2}"))}";
                if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), new Vector2(ImGui.GetFrameHeight()), tt,
                        locationCount == 0, true))
                    _plugin.LocationManager.SetMarkers(location, WaymarkSet.None);
            }
        }

        public LocationTable()
            : base("##LocationTable", _plugin.LocationManager.AllLocations, _nameColumn,
                _typeColumn, _aetheryteColumn, _xCoordColumn, _yCoordColumn, _radiusColumn, _markerColumn, _territoryColumn, _levelColumn, _itemColumn)
        { }
    }

    private readonly LocationTable _locationTable;

    private void DrawLocationsTab()
    {
        using var id  = ImRaii.PushId("Locations");
        using var tab = ImRaii.TabItem("位置");
        ImGuiUtil.HoverTooltip("默认位置不够方便？\n"
          + "你可以为特定采集点设置自定义的以太之光或地图标记位置。");

        if (!tab)
            return;

        _locationTable.Draw(ImGui.GetFrameHeightWithSpacing());
    }
}
