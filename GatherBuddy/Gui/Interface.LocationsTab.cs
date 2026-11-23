using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Enums;
using GatherBuddy.Interfaces;
using GatherBuddy.RebornUI;
using GatherBuddy.RebornUI.Tables;
using GatherBuddy.Structs;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Globalization;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

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
            {
                return;
            }

            _nameColumnWidth = MaxTextWidth(_plugin.LocationManager.AllLocations, static l => l.Name) / ImGuiHelpers.GlobalScale;
            _territoryColumnWidth = MaxTextWidth(_plugin.LocationManager.AllLocations, static l => l.Territory.Name) / ImGuiHelpers.GlobalScale;
            _aetheryteColumnWidth = MaxTextWidth(GatherBuddy.GameData.Aetherytes.Values, static a => a.Name) / ImGuiHelpers.GlobalScale;
            _coordColumnWidth = TextWidth(_xCoordColumn.DisplayLabel) / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _radiusColumnWidth = TextWidth("半径") / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _levelColumnWidth = TextWidth("等级") / ImGuiHelpers.GlobalScale + Table.ArrowWidth;
            _typeColumnWidth = MaxTextWidth(Enum.GetValues<GatheringType>(), static t => EnumLocalization.Get(t)) / ImGuiHelpers.GlobalScale;
        }

        private static readonly NameColumn _nameColumn = new() { Label = "Name", LocalizedLabel = "名称" };
        private static readonly TypeColumn _typeColumn = new() { Label = "Type", LocalizedLabel = "类型" };
        private static readonly TerritoryColumn _territoryColumn = new() { Label = "Territory", LocalizedLabel = "区域" };
        private static readonly LevelColumn _levelColumn = new() { Label = "Level", LocalizedLabel = "等级" };
        private static readonly AetheryteColumn _aetheryteColumn = new() { Label = "Aetheryte", LocalizedLabel = "以太之光" };
        private static readonly XCoordColumn _xCoordColumn = new() { Label = "X-Coord", LocalizedLabel = "X 坐标" };
        private static readonly YCoordColumn _yCoordColumn = new() { Label = "Y-Coord", LocalizedLabel = "Y 坐标" };
        private static readonly RadiusColumn _radiusColumn = new() { Label = "Radius", LocalizedLabel = "半径" };
        private static readonly MarkerColumn _markerColumn = new() { Label = "Markers", LocalizedLabel = "标记" };
        private static readonly ItemColumn _itemColumn = new() { Label = "Items", LocalizedLabel = "物品" };

        private sealed class NameColumn : ColumnString<ILocation>
        {
            public NameColumn()
                => Flags |= ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoReorder;

            public override string ToName(ILocation location)
                => location.Name;

            public override float Width
                => _nameColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
                => Ui.RowText(ToName(item), M3Widgets.ComboHeight);
        }

        private sealed class TypeColumn : ColumnFlags<JobFlags, ILocation>
        {
            private static readonly string[] JobNames = [
                "采伐###Logging", "割草###Harvesting", "采掘###Mining",
                "碎石###Quarrying", "钓鱼###Fishing", "刺鱼###Spearfishing",
            ];

            protected override string[] Names => JobNames;

            public TypeColumn()
            {
                var allFlags = default(JobFlags);
                foreach (var flag in Enum.GetValues<JobFlags>())
                {
                    allFlags |= flag;
                }

                AllFlags = allFlags;
            }

            public override JobFlags FilterValue
                => GatherBuddy.Config.LocationFilter;

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
                => Ui.RowText(EnumLocalization.Get(location.GatheringType), M3Widgets.ComboHeight);

            public override int Compare(ILocation a, ILocation b)
                => a.GatheringType.CompareTo(b.GatheringType);

            public override bool FilterFunc(ILocation location)
            {
                return location.GatheringType switch
                {
                    GatheringType.Mining => FilterValue.HasFlag(JobFlags.Mining),
                    GatheringType.Quarrying => FilterValue.HasFlag(JobFlags.Quarrying),
                    GatheringType.Logging => FilterValue.HasFlag(JobFlags.Logging),
                    GatheringType.Harvesting => FilterValue.HasFlag(JobFlags.Harvesting),
                    GatheringType.Spearfishing => FilterValue.HasFlag(JobFlags.Spearfishing),
                    GatheringType.Fisher => FilterValue.HasFlag(JobFlags.Fishing),
                    _ => false,
                };
            }

            public override float Width
                => _typeColumnWidth * ImGuiHelpers.GlobalScale;
        }

        private sealed class TerritoryColumn : ColumnString<ILocation>
        {
            public override string ToName(ILocation location)
                => location.Territory.Name;

            public override float Width
                => _territoryColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
                => Ui.RowText(ToName(item), M3Widgets.ComboHeight);
        }

        private sealed class LevelColumn() : ColumnNumber<ILocation>(ComparisonMethod.LessEqual)
        {
            public override int ToValue(ILocation item)
                => (item as GatheringNode)?.Level ?? 1;

            public override float Width
                => _levelColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation item, int _)
                => Ui.RightAligned(ToName(item), rowHeight: M3Widgets.ComboHeight);
        }

        private sealed class ItemColumn : ColumnString<ILocation>
        {
            public ItemColumn()
                => Flags |= ImGuiTableColumnFlags.WidthStretch;

            public override string ToName(ILocation location)
            {
                var names = new List<string>();
                foreach (var gatherable in location.Gatherables)
                {
                    names.Add(gatherable.Name[GatherBuddy.Language]);
                }

                return string.Join(", ", names);
            }

            public override float Width
                => 0;

            public override void DrawColumn(ILocation item, int _)
                => Ui.RowText(ToName(item), M3Widgets.ComboHeight);
        }

        private sealed class AetheryteColumn : ColumnString<ILocation>
        {
            private readonly List<Aetheryte> _aetherytes = [.. GatherBuddy.GameData.Aetherytes.Values];

            public override string ToName(ILocation location)
                => location.ClosestAetheryte?.Name ?? "无";

            public override float Width
                => _aetheryteColumnWidth * ImGuiHelpers.GlobalScale;

            public override void DrawColumn(ILocation location, int _)
            {
                var overwritten = location.DefaultAetheryte != location.ClosestAetheryte;
                var index = location.ClosestAetheryte == null ? -1 : _aetherytes.IndexOf(location.ClosestAetheryte);
                if (M3Widgets.Combo("##aetheryte", ref index, _aetherytes.Count, i => _aetherytes[i].Name, ImGui.GetContentRegionAvail().X, "无",
                        search: true))
                {
                    _plugin.LocationManager.SetAetheryte(location, _aetherytes[index]);
                }

                if (overwritten)
                {
                    MarkChanged();
                    M3Tooltip.Hovered($"右键点击可恢复默认值（{location.DefaultAetheryte?.Name ?? "无"})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _plugin.LocationManager.SetAetheryte(location, location.DefaultAetheryte);
                    }
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
                var overwritten = location.DefaultXCoord != location.IntegralXCoord;
                using var color = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                Ui.CenterInRow(M3Widgets.ComboHeight, ImGui.GetFrameHeight());
                var x = location.IntegralXCoord / 100f;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##x", ref x, 0.05f, 1f, 42f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                {
                    _plugin.LocationManager.SetXCoord(location, (int)(x * 100f + 0.5f));
                }

                if (overwritten)
                {
                    M3Tooltip.Hovered($"右键点击以恢复默认设置。 ({location.DefaultXCoord / 100f:0.00})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _plugin.LocationManager.SetXCoord(location, location.DefaultXCoord);
                    }
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
                var overwritten = location.DefaultYCoord != location.IntegralYCoord;
                using var color = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                Ui.CenterInRow(M3Widgets.ComboHeight, ImGui.GetFrameHeight());
                var y = location.IntegralYCoord / 100f;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##y", ref y, 0.05f, 1f, 42f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
                {
                    _plugin.LocationManager.SetYCoord(location, (int)(y * 100f + 0.5f));
                }

                if (overwritten)
                {
                    M3Tooltip.Hovered($"右键点击以恢复默认设置。 ({location.DefaultYCoord / 100f:0.00})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _plugin.LocationManager.SetYCoord(location, location.DefaultYCoord);
                    }
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
                var overwritten = location.DefaultRadius != location.Radius;
                using var color = ImRaii.PushColor(ImGuiCol.FrameBg, ColorId.ChangedLocationBg.Value(), overwritten);
                Ui.CenterInRow(M3Widgets.ComboHeight, ImGui.GetFrameHeight());
                ImGui.SetNextItemWidth(-1);
                int radius = location.Radius;
                if (ImGui.DragInt("##radius", ref radius, 0.1f, 0, IMarkable.RadiusMax))
                {
                    _plugin.LocationManager.SetRadius(location, Math.Clamp((ushort)radius, (ushort)0, IMarkable.RadiusMax));
                }

                if (overwritten)
                {
                    M3Tooltip.Hovered($"右键点击以恢复默认设置。 ({location.DefaultRadius})");
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    {
                        _plugin.LocationManager.SetRadius(location, location.DefaultRadius);
                    }
                }
            }

            public override int Compare(ILocation a, ILocation b)
                => a.Radius.CompareTo(b.Radius);
        }

        [Flags]
        private enum MarkerFlags : byte
        {
            None = 0x01,
            Any = 0x02,
        }

        private sealed class MarkerColumn : ColumnFlags<MarkerFlags, ILocation>
        {
            private static readonly string[] MarkerNames = ["无标记###None", "有标记###Any"];

            protected override string[] Names => MarkerNames;

            public override int Compare(ILocation lhs, ILocation rhs)
            {
                if (lhs.Markers.CountSet != rhs.Markers.CountSet)
                {
                    return lhs.Markers.CountSet - rhs.Markers.CountSet;
                }

                var diff = lhs.Territory.Id.CompareTo(rhs.Territory.Id);
                if (diff != 0)
                {
                    return diff;
                }

                var lhsMarkers = lhs.Markers;
                var rhsMarkers = rhs.Markers;
                for (var i = 0; i < WaymarkSet.Count; ++i)
                {
                    var l = lhsMarkers[i];
                    var r = rhsMarkers[i];
                    diff = l.X.CompareTo(r.X);
                    if (diff != 0)
                    {
                        return diff;
                    }

                    diff = l.Y.CompareTo(r.Y);
                    if (diff != 0)
                    {
                        return diff;
                    }

                    diff = l.Z.CompareTo(r.Z);
                    if (diff != 0)
                    {
                        return diff;
                    }
                }

                return 0;
            }

            private MarkerFlags _filter = MarkerFlags.None | MarkerFlags.Any;

            public override MarkerFlags FilterValue
                => _filter;

            protected override void SetValue(MarkerFlags value, bool enable)
            {
                _filter = enable ? _filter | value : _filter & ~value;
            }


            public override bool FilterFunc(ILocation item)
                => FilterValue.HasFlag(item.Markers.CountSet == 0 ? MarkerFlags.None : MarkerFlags.Any);

            public override float Width
                => ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X + Table.ArrowWidth;

            public override void DrawColumn(ILocation location, int id)
            {
                using var _ = ImRaii.PushId(id);
                var markers = GatherBuddy.WaymarkManager.GetWaymarks();
                var markerCount = markers.CountSet;
                var locationCount = location.Markers.CountSet;
                var invalid = Dalamud.ClientState.TerritoryType != location.Territory.Id;
                var tt = invalid ? "当前不在该地点所在区域。" :
                    markerCount == 0 ? "没有可保存的场地标记。" :
                                       $"保存当前位置已放置的场地标记：\n\n{FormatMarkers(markers)}";

                if (locationCount > 0)
                {
                    tt +=
                        $"\n\n此地点保存的地图标记\n\n{FormatMarkers(location.Markers)}";
                }

                Ui.CenterInRow(M3Widgets.ComboHeight, ImGui.GetFrameHeight());
                if (M3Widgets.IconButton("##store", FontAwesomeIcon.Map, tt, diameter: ImGui.GetFrameHeight(), enabled: markerCount > 0 && !invalid))
                {
                    _plugin.LocationManager.SetMarkers(location, markers);
                }

                ImGui.SameLine();
                tt = locationCount == 0
                    ? "此位置没有已存储的标记。"
                    : $"移除此地点已保存的场地标记：\n\n{FormatMarkers(location.Markers)}";
                if (M3Widgets.IconButton("##remove", FontAwesomeIcon.Trash, tt, diameter: ImGui.GetFrameHeight(), enabled: locationCount > 0))
                {
                    _plugin.LocationManager.SetMarkers(location, WaymarkSet.None);
                }
            }

            private static string FormatMarkers(WaymarkSet markers)
            {
                var lines = new List<string>(WaymarkSet.Count);
                foreach (var marker in markers)
                {
                    lines.Add(float.IsNaN(marker.X) ? " - " : $"{marker.X:F2} - {marker.Y:F2} - {marker.Z:F2}");
                }

                return string.Join("\n", lines);
            }
        }

        private static void MarkChanged()
            => ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ColorId.ChangedLocationBg.Value(), M3.ShapeExtraSmall,
                ImDrawFlags.None, MathF.Max(1f, 1.5f * M3.Scale));

        public LocationTable()
            : base("##LocationTable", _plugin.LocationManager.AllLocations, _nameColumn,
                _typeColumn, _aetheryteColumn, _xCoordColumn, _yCoordColumn, _radiusColumn, _markerColumn, _territoryColumn, _levelColumn, _itemColumn)
        { }
    }

    private readonly LocationTable _locationTable;

    private void DrawLocationsPage()
    {
        using var id = ImRaii.PushId("Locations");
        _locationTable.Draw(ImGui.GetFrameHeightWithSpacing());
    }
}
