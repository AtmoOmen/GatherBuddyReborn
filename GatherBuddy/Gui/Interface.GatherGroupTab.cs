using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using GatherBuddy.Alarms;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.GatherGroup;
using GatherBuddy.GatherHelper;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using GatherBuddy.Time;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private class GatherGroupDragDropData(TimedGroup group, TimedGroupNode node, int idx)
    {
        public TimedGroup Group = group;
        public TimedGroupNode Node = node;
        public int NodeIdx = idx;
    }

    private class GatherGroupCache(GatherGroupManager gatherGroupManager)
    {
        public sealed class GatherGroupSelector(GatherGroupManager manager) : ItemSelector<TimedGroup>(manager.Groups.Values, Flags.All & ~Flags.Move)
        {
            private readonly GatherGroupManager _manager = manager;

            protected override bool Filtered(int idx)
                => Filter.Length != 0 && !Items[idx].Name.Contains(Filter, StringComparison.InvariantCultureIgnoreCase);

            protected override bool OnDraw(int idx)
                => ListRow(Items[idx].Name, idx == CurrentIdx);

            protected override string NameOf(TimedGroup item)
                => item.Name;

            protected override bool OnDelete(int idx)
            {
                if (Items.Count <= idx || idx < 0)
                {
                    return false;
                }

                _manager.Groups.RemoveAt(idx);
                _manager.Save();
                return true;
            }

            protected override bool OnAdd(string name)
                => _manager.AddGroup(name, new TimedGroup(name));

            protected override bool OnClipboardImport(string name, string data)
            {
                if (!TimedGroup.Config.FromBase64(data, out var cfgGroup))
                {
                    return false;
                }

                TimedGroup.FromConfig(cfgGroup, out var group);
                group.Name = name;
                return _manager.AddGroup(name, group);
            }

            protected override bool OnDuplicate(string name, int idx)
            {
                if (Items.Count <= idx || idx < 0)
                {
                    return false;
                }

                var group = _manager.Groups.Values[idx].Clone(name);
                return _manager.AddGroup(name, group);
            }

            protected override void OnDrop(object? data, int idx)
            {
                if (Items.Count <= idx || idx < 0 || data is not GatherGroupDragDropData d)
                {
                    return;
                }

                var group = Items[idx];

                if (!GatherGroupManager.ChangeGroupNode(@group, @group.Nodes.Count, d.Node.Item, d.Node.EorzeaStartMinute,
                        d.Node.EorzeaEndMinute, d.Node.Annotation, false))
                {
                    GatherBuddy.Log.Error($"无法将节点从组 {d.Group.Name} 移动到组 {group.Name}。");
                    return;
                }

                GatherGroupManager.ChangeGroupNode(d.Group, d.NodeIdx, null, null, null, null, true);
                _plugin.GatherGroupManager.Save();
            }
        }

        public static readonly IGatherable[] AllGatherables = CreateAllGatherables();

        private static IGatherable[] CreateAllGatherables()
        {
            var gatherables = new List<(IGatherable Item, string Name, int Index)>();
            foreach (var gatherable in GatherBuddy.GameData.Gatherables.Values)
            {
                if (gatherable.Locations.Count != 0)
                {
                    gatherables.Add((gatherable, gatherable.Name[GatherBuddy.Language], gatherables.Count));
                }
            }

            foreach (var fish in GatherBuddy.GameData.Fishes.Values)
            {
                if (fish.Locations.Count != 0)
                {
                    gatherables.Add((fish, fish.Name[GatherBuddy.Language], gatherables.Count));
                }
            }

            gatherables.Sort(static (lhs, rhs) =>
            {
                var order = string.Compare(lhs.Name, rhs.Name, StringComparison.CurrentCulture);
                return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
            });
            var result = new IGatherable[gatherables.Count];
            for (var i = 0; i < gatherables.Count; i++)
            {
                result[i] = gatherables[i].Item;
            }

            return result;
        }

        public readonly GatherGroupSelector Selector = new(gatherGroupManager);

        public readonly string DefaultGroupTooltip =
                "恢复被删除或修改的默认采集组。\n"
              + "按住 Ctrl 应用。默认采集组：\n\t- "
              + $"{string.Join("\n\t- ", GroupData.DefaultGroups.ConvertAll(g => g.Name))}";
        public int NewItemIdx = 0;

        private bool _itemPerMinuteDirty = true;
        private readonly List<short> _itemPerMinute = [with(24)];

        public void SetDirty()
            => _itemPerMinuteDirty = true;

        public List<short> UpdateItemPerMinute(TimedGroup group)
        {
            if (!_itemPerMinuteDirty && group.Nodes.Count + 1 == _itemPerMinute.Count)
            {
                return _itemPerMinute;
            }

            _itemPerMinute.Clear();
            for (var i = 0; i <= group.Nodes.Count; ++i)
            {
                _itemPerMinute.Add(0);
            }

            for (var i = 0; i < RealTime.MinutesPerDay; ++i)
            {
                var node = group.CurrentNode((uint)i);
                if (node == null)
                {
                    _itemPerMinute[0]++;
                }
                else
                {
                    var idx = group.Nodes.IndexOf(node);
                    _itemPerMinute[idx + 1]++;
                }
            }

            _itemPerMinuteDirty = false;
            return _itemPerMinute;
        }
    }

    private readonly GatherGroupCache _gatherGroupCache;

    private static bool DrawGatherableCombo(string id, ref int index, float width)
        => M3Widgets.Combo(id, ref index, GatherGroupCache.AllGatherables.Length, i => GatherGroupCache.AllGatherables[i].Name[GatherBuddy.Language],
            width, "选择物品", true);

    private static readonly string[] GatherGroupColumns = ["##delete", "Item", "Eorzea time", "Location", "Length", "Annotation"];
    private static readonly string[] GatherGroupColumnLabels = ["", "物品", "艾欧泽亚时间", "地点", "时长", "备注"];

    private static void DrawTimeInput(string label, float width, int value, Action<int> setter)
    {
        var hour = value / RealTime.MinutesPerHour;
        var minute = value % RealTime.MinutesPerHour;
        using var group = ImRaii.Group();
        using var id = ImRaii.PushId(label);
        ImGui.SetNextItemWidth(width);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.One * 2 * ImGuiHelpers.GlobalScale);
        var change = ImGui.DragInt("##hour", ref hour, 0.05f, 0, RealTime.HoursPerDay - 1, "%02d", ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine();
        ImGui.TextUnformatted(":");
        ImGui.SameLine();
        style.Pop();
        ImGui.SetNextItemWidth(width);
        change |= ImGui.DragInt("##minute", ref minute, 0.2f, 0, RealTime.MinutesPerHour - 1, "%02d", ImGuiSliderFlags.AlwaysClamp);

        if (change)
        {
            var newValue = Math.Clamp(hour * RealTime.MinutesPerHour + minute, 0, RealTime.MinutesPerDay - 1);
            if (newValue != value)
            {
                setter(newValue);
            }
        }
    }

    private void DrawTimeInput(int fromValue, int toValue, Action<int, int> setter)
    {
        var width = 24 * ImGuiHelpers.GlobalScale;
        using var group = ImRaii.Group();
        ImGui.AlignTextToFramePadding();
        using (M3Text.Push(M3TextStyle.Muted))
        {
            ImGui.TextUnformatted("从");
        }

        ImGui.SameLine();
        DrawTimeInput("##from", width, fromValue, v => setter(v, toValue));
        ImGui.SameLine();
        using (M3Text.Push(M3TextStyle.Muted))
        {
            ImGui.TextUnformatted("到");
        }

        ImGui.SameLine();
        DrawTimeInput("##to", width, toValue, v => setter(fromValue, v));
    }

    private static void DrawLocationTooltip(ILocation? loc)
    {
        if (loc != null && ImGui.IsItemHovered())
        {
            M3Tooltip.Show(LocationTooltip(loc));
        }
    }

    private static void DrawLocationInput(TimedGroup group, int nodeIdx, TimedGroupNode node)
    {
        if (DrawLocationInput(node.Item, node.PreferLocation, out var newLoc)
         && GatherGroupManager.ChangeGroupNodeLocation(group, nodeIdx, newLoc))
        {
            _plugin.GatherGroupManager.Save();
        }
    }

    private void DrawGatherGroupNode(TimedGroup group, ref int idx, int minutes)
    {
        var node = group.Nodes[idx];
        using var id = ImRaii.PushId(idx);
        var i = idx;
        var rowHeight = M3TextField.Height;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, ImGui.GetFrameHeight());
        if (M3Widgets.IconButton("##delete", FontAwesomeIcon.Trash, "移除此物品", diameter: ImGui.GetFrameHeight()))
        {
            if (GatherGroupManager.ChangeGroupNode(group, i, null, null, null, null, true))
            {
                --idx;
                _plugin.GatherGroupManager.Save();
                _gatherGroupCache.SetDirty();
            }

            return;
        }

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        var itemIdx = Array.IndexOf(GatherGroupCache.AllGatherables, node.Item);
        if (DrawGatherableCombo("##item", ref itemIdx, SetInputWidth * 1.25f)
         && GatherGroupManager.ChangeGroupNode(group, i, GatherGroupCache.AllGatherables[itemIdx], null, null, null, false))
        {
            _plugin.GatherGroupManager.Save();
        }

        _gatherGroupCache.Selector.CreateDropSource(new GatherGroupDragDropData(group, node, i), node.Item.Name[GatherBuddy.Language]);
        _gatherGroupCache.Selector.CreateDropTarget<GatherGroupDragDropData>(d => _plugin.GatherGroupManager.MoveNode(group, d.NodeIdx, i));

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, ImGui.GetFrameHeight());
        DrawTimeInput(node.EorzeaStartMinute, node.EorzeaEndMinute, (from, to) =>
        {
            if (GatherGroupManager.ChangeGroupNode(group, i, null, from, to, null, false))
            {
                _plugin.GatherGroupManager.Save();
                _gatherGroupCache.SetDirty();
            }
        });

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        DrawLocationInput(group, i, node);

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, ImGui.GetFrameHeight());
        var length = node.Length();
        if (minutes < length)
        {
            Ui.Framed($"{length} 分钟", ColorId.WarningBg.Value());
            HoverTooltip($"与排在前面的物品重叠的 {length - minutes} 分钟不会生效。");
        }
        else
        {
            Ui.Framed($"{length} 分钟", M3.Scheme.SurfaceContainerHighest);
        }

        ImGui.TableNextColumn();
        if (Ui.CommitField("##annotation", "备注", node.Annotation, out var annotation, 250 * M3.Scale)
         && GatherGroupManager.ChangeGroupNode(group, i, null, null, null, annotation, false))
        {
            _plugin.GatherGroupManager.Save();
        }
    }

    private void DrawGatherGroupNodeTable(TimedGroup group)
    {
        var times = _gatherGroupCache.UpdateItemPerMinute(group);
        if (times[0] > 0)
        {
            M3Widgets.Banner("##missingTimes", "有些时段未安排采集物品。", M3Severity.Warning, FontAwesomeIcon.ExclamationTriangle);
        }

        using var style = M3Table.PushStyle();
        using var table = ImRaii.Table("##nodes", GatherGroupColumns.Length, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX
          | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
        {
            return;
        }

        foreach (var column in GatherGroupColumns)
        {
            ImGui.TableSetupColumn(column);
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < GatherGroupColumns.Length; i++)
        {
            ImGui.TableNextColumn();
            if (!GatherGroupColumns[i].StartsWith("##"))
            {
                M3Table.Header(GatherGroupColumnLabels[i], center: false);
            }
        }

        for (var i = 0; i < group.Nodes.Count; ++i)
        {
            DrawGatherGroupNode(group, ref i, times[i + 1]);
        }

        var idx = _gatherGroupCache.NewItemIdx;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Ui.CenterInRow(M3Widgets.ComboHeight, ImGui.GetFrameHeight());
        if (M3Widgets.IconButton("##add", FontAwesomeIcon.Plus, "添加此物品", M3ButtonStyle.Tonal, diameter: ImGui.GetFrameHeight()))
        {
            var gatherable = GatherGroupCache.AllGatherables[idx];
            if (gatherable.InternalLocationId > 0)
            {
                var locations = gatherable.Locations;
                if (locations.Count is 1 && locations[0] is GatheringNode node)
                {
                    var changes = false;
                    foreach (var (start, end) in node.Times.AllUptimes())
                    {
                        changes |= GatherGroupManager.ChangeGroupNode(group, group.Nodes.Count, gatherable, (int)start * RealTime.MinutesPerHour,
                            (int)end * RealTime.MinutesPerHour, null, false);
                    }

                    if (changes)
                    {
                        _gatherGroupCache.SetDirty();
                        _plugin.GatherGroupManager.Save();
                    }
                }
            }
            else if (GatherGroupManager.ChangeGroupNode(group, group.Nodes.Count, gatherable, null, null, null, false))
            {
                _gatherGroupCache.SetDirty();
                _plugin.GatherGroupManager.Save();
            }
        }

        ImGui.TableNextColumn();
        if (DrawGatherableCombo("##newItem", ref idx, SetInputWidth * 1.25f))
        {
            _gatherGroupCache.NewItemIdx = idx;
        }
    }

    private static void DrawNameField(TimedGroup group)
    {
        if (!Ui.CommitField("##groupName", "名称", group.Name, out var newName, SetInputWidth, 64))
        {
            return;
        }

        newName = newName.Trim();
        if (newName.Length == 0)
        {
            M3Snackbar.Show("采集组必须有名称。", severity: M3Severity.Error);
        }
        else if (newName != group.Name && _plugin.GatherGroupManager.Groups.ContainsKey(newName.ToLowerInvariant()))
        {
            M3Snackbar.Show($"已存在名为 {newName} 的采集组。", severity: M3Severity.Error);
        }
        else if (_plugin.GatherGroupManager.RenameGroup(group, newName))
        {
            _plugin.GatherGroupManager.Save();
        }
    }

    private static void DrawDescField(TimedGroup group)
    {
        if (Ui.CommitField("##groupDesc", "描述", group.Description, out var newDesc, 2 * SetInputWidth, 128)
         && GatherGroupManager.ChangeDescription(group, newDesc))
        {
            _plugin.GatherGroupManager.Save();
        }
    }

    private void DrawGatherGroup(TimedGroup group)
    {
        using var id = ImRaii.PushId(group.Name);

        DrawNameField(group);
        ImGui.SameLine(0, M3.Space2);
        DrawDescField(group);
        ImGui.Dummy(new Vector2(0, M3.Space2));
        DrawGatherGroupNodeTable(group);
    }

    private void DrawGatherGroupHeaderLine()
    {
        var current = _gatherGroupCache.Selector.Current;
        var row = new Ui.Row(M3Widgets.ButtonHeight);
        row.Place(M3Widgets.IconButtonSize);
        if (M3Widgets.IconButton("##copy", FontAwesomeIcon.Copy, "将此采集组复制到剪贴板", enabled: current != null))
        {
            try
            {
                ImGui.SetClipboardText(current!.ToConfig().ToBase64());
                Communicator.PrintClipboardMessage("采集组 ", current.Name);
            }
            catch (Exception e)
            {
                Communicator.PrintClipboardMessage("采集组 ", current!.Name, e);
            }
        }

        row.Next(M3Widgets.ButtonHeight, M3.Space1);
        if (M3Widgets.Button("##list", "创建自动采集列表", M3ButtonStyle.Text, FontAwesomeIcon.Robot, enabled: current != null,
                tooltip: "根据此采集组创建新的自动采集列表。"))
        {
            _plugin.AutoGatherListsManager.AddList(new AutoGatherList(current!));
            M3Snackbar.Show("已创建自动采集列表。");
        }

        row.Next(M3Widgets.ButtonHeight, M3.Space1);
        if (M3Widgets.Button("##preset", "创建窗口预设", M3ButtonStyle.Text, FontAwesomeIcon.ListUl, enabled: current != null,
                tooltip: "根据此采集组创建新的采集窗口预设。"))
        {
            _plugin.GatherWindowManager.AddPreset(new GatherWindowPreset(current!));
            M3Snackbar.Show("已创建采集窗口预设。");
        }

        row.Next(M3Widgets.ButtonHeight, M3.Space1);
        if (M3Widgets.Button("##alarms", "创建闹钟", M3ButtonStyle.Text, FontAwesomeIcon.Bell, enabled: current != null,
                tooltip: "根据此采集组创建新的闹钟组。"))
        {
            _plugin.AlarmManager.AddGroup(new AlarmGroup(current!));
            M3Snackbar.Show("已创建闹钟组。");
        }

        row.Next(M3Widgets.ButtonHeight, M3.Space1);
        if (M3Widgets.HoldButton("##restore", "恢复默认采集组", 1f, M3ButtonStyle.Tonal, FontAwesomeIcon.Undo,
                tooltip: $"{_gatherGroupCache.DefaultGroupTooltip.Replace("按住 Ctrl 应用。", "长按以应用。")}")
         && _plugin.GatherGroupManager.SetDefaults(true))
        {
            _gatherGroupCache.Selector.TryRestoreCurrent();
            _plugin.GatherGroupManager.Save();
            M3Snackbar.Show("已恢复默认采集组。");
        }

        row.Next(ImGui.GetFrameHeight(), M3.Space2);
        M3Widgets.HelpMarker("使用 /gathergroup [组名] [可选：偏移分钟数] 调用采集组。\n"
          + "使用 /gather 采集当前时段（或指定艾欧泽亚分钟后）可采集的物品。\n"
          + "若多个物品的时段重叠，优先采集列表中排在最前面的物品。");
    }

    private void DrawGatherGroupPage()
    {
        using var id = ImRaii.PushId("Gather Groups");
        _gatherGroupCache.Selector.Draw(SelectorWidth);
        ImGui.SameLine(0, M3.Space2);

        ItemDetailsWindow.Draw("采集组详情", DrawGatherGroupHeaderLine, () =>
        {
            if (_gatherGroupCache.Selector.Current != null)
            {
                DrawGatherGroup(_gatherGroupCache.Selector.Current);
            }
            else
            {
                M3Widgets.EmptyState("##noGroup", "未选择采集组", "点击列表下方的加号添加组。", FontAwesomeIcon.LayerGroup);
            }
        });
    }
}