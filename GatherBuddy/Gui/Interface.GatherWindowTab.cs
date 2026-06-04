using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using GatherBuddy.Alarms;
using GatherBuddy.Config;
using GatherBuddy.GatherHelper;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using Dalamud.Bindings.ImGui;
using ElliLib;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private class GatherWindowDragDropData
    {
        public GatherWindowPreset Preset;
        public IGatherable        Item;
        public int                ItemIdx;

        public GatherWindowDragDropData(GatherWindowPreset preset, IGatherable item, int idx)
        {
            Preset  = preset;
            Item    = item;
            ItemIdx = idx;
        }
    }

    private class GatherWindowCache
    {
        public class GatherWindowSelector : ItemSelector<GatherWindowPreset>
        {
            public GatherWindowSelector()
                : base(_plugin.GatherWindowManager.Presets, Flags.All)
            { }

            protected override bool Filtered(int idx)
                => Filter.Length != 0 && !Items[idx].Name.Contains(Filter, StringComparison.InvariantCultureIgnoreCase);

            protected override bool OnDraw(int idx)
            {
                using var id    = ImRaii.PushId(idx);
                using var color = ImRaii.PushColor(ImGuiCol.Text, ColorId.DisabledText.Value(), !Items[idx].Enabled);
                return ImGui.Selectable(CheckUnnamed(Items[idx].Name), idx == CurrentIdx);
            }

            protected override bool OnDelete(int idx)
            {
                _plugin.GatherWindowManager.DeletePreset(idx);
                return true;
            }

            protected override bool OnAdd(string name)
            {
                _plugin.GatherWindowManager.AddPreset(new GatherWindowPreset()
                {
                    Name = name,
                });
                return true;
            }

            protected override bool OnClipboardImport(string name, string data)
            {
                if (!GatherWindowPreset.Config.FromBase64(data, out var cfg))
                    return false;

                GatherWindowPreset.FromConfig(cfg, out var preset);
                preset.Name = name;
                _plugin.GatherWindowManager.AddPreset(preset);
                return true;
            }

            protected override bool OnDuplicate(string name, int idx)
            {
                if (Items.Count <= idx || idx < 0)
                    return false;

                var preset = _plugin.GatherWindowManager.Presets[idx].Clone();
                preset.Name = name;
                _plugin.GatherWindowManager.AddPreset(preset);
                return true;
            }

            protected override void OnDrop(object? data, int idx)
            {
                if (Items.Count <= idx || idx < 0)
                    return;
                if (data is not GatherWindowDragDropData obj)
                    return;

                var preset = _plugin.GatherWindowManager.Presets[idx];
                _plugin.GatherWindowManager.RemoveItem(obj.Preset, obj.ItemIdx);
                _plugin.GatherWindowManager.AddItem(preset, obj.Item);
            }


            protected override bool OnMove(int idx1, int idx2)
            {
                _plugin.GatherWindowManager.MovePreset(idx1, idx2);
                return true;
            }
        }

        public readonly GatherWindowSelector Selector = new();

        public int  NewGatherableIdx;
        public bool EditName;
        public bool EditDesc;
    }

    private readonly GatherWindowCache _gatherWindowCache;

    private void DrawGatherWindowPresetHeaderLine()
    {
        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Copy.ToIconString(), IconButtonSize, "复制当前采集窗口预设至剪贴板",
                _gatherWindowCache.Selector.Current == null, true))
        {
            var preset = _gatherWindowCache.Selector.Current!;
            try
            {
                var s = new GatherWindowPreset.Config(preset).ToBase64();
                ImGui.SetClipboardText(s);
                Communicator.PrintClipboardMessage("采集窗口预设 ", preset.Name);
            }
            catch (Exception e)
            {
                Communicator.PrintClipboardMessage("采集窗口预设 ", preset.Name, e);
            }
        }

        if (ImGuiUtil.DrawDisabledButton("创建闹钟", Vector2.Zero, "新建一个对应该采集窗口预设的闹钟", _gatherWindowCache.Selector.Current == null))
        {
            var preset = new AlarmGroup(_gatherWindowCache.Selector.Current!);
            _plugin.AlarmManager.AddGroup(preset);
        }

        ImGuiComponents.HelpMarker(
            "若设置中\"物品排序方法\"未选择\"按位置排序\"选项(设置-自动采集-高级), 物品会根据启用列表顺序添加，其次按列表内的物品顺序添加到采集窗口。\n"
          + "你可以拖动列表来调整列表位置顺序。\n"
          + "你可以在某个列表中拖动物品来调整位置顺序。\n"
          + "你可以将某个物品从选择器拖到其他列表上, 以将其添加到该列表并从当前列表中移除。\n"
          + "在采集窗口中, 你可以按住 Ctrl 并右键点击某个物品, 将其从所属列表中删除。\n\t如果这会导致该列表没有任何物品，则该列表也会被删除。");
    }

    private void DrawGatherWindowPreset(GatherWindowPreset preset)
    {
        if (ImGuiUtil.DrawEditButtonText(0, _gatherWindowCache.EditName ? preset.Name : CheckUnnamed(preset.Name), out var newName,
                ref _gatherWindowCache.EditName, IconButtonSize, SetInputWidth, 64))
            _plugin.GatherWindowManager.ChangeName(preset, newName);
        if (ImGuiUtil.DrawEditButtonText(1, _gatherWindowCache.EditDesc ? preset.Description : CheckUndescribed(preset.Description),
                out var newDesc, ref _gatherWindowCache.EditDesc, IconButtonSize, 2 * SetInputWidth, 128))
            _plugin.GatherWindowManager.ChangeDescription(preset, newDesc);

        var tmp = preset.Enabled;
        if (ImGui.Checkbox("启用##preset", ref tmp) && tmp != preset.Enabled)
            _plugin.GatherWindowManager.TogglePreset(preset);

        ImGui.NewLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - ImGui.GetStyle().ItemInnerSpacing.X);
        using var box = ImRaii.ListBox("##gatherWindowList", new Vector2(-1.5f * ImGui.GetStyle().ItemSpacing.X, -1));
        if (!box)
            return;

        for (var i = 0; i < preset.Items.Count; ++i)
        {
            using var id    = ImRaii.PushId(i);
            using var group = ImRaii.Group();
            var       item  = preset.Items[i];
            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), IconButtonSize, "从此预设中删除...", false,
                    true))
                _plugin.GatherWindowManager.RemoveItem(preset, i--);

            ImGui.SameLine();
            if (_gatherGroupCache.GatherableSelector.Draw(item.Name[GatherBuddy.Language], out var newIdx))
                _plugin.GatherWindowManager.ChangeItem(preset, GatherGroupCache.AllGatherables[newIdx], i);
            group.Dispose();

            _gatherWindowCache.Selector.CreateDropSource(new GatherWindowDragDropData(preset, item, i), item.Name[GatherBuddy.Language]);

            var localIdx = i;
            _gatherWindowCache.Selector.CreateDropTarget<GatherWindowDragDropData>(d
                => _plugin.GatherWindowManager.MoveItem(d.Preset, d.ItemIdx, localIdx));
        }

        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Plus.ToIconString(), IconButtonSize,
                "如果此物品尚未包含在预设中，则添加至预设末尾...",
                preset.Items.Contains(GatherGroupCache.AllGatherables[_gatherWindowCache.NewGatherableIdx]), true))
            _plugin.GatherWindowManager.AddItem(preset, GatherGroupCache.AllGatherables[_gatherWindowCache.NewGatherableIdx]);

        ImGui.SameLine();
        if (_gatherGroupCache.GatherableSelector.Draw(_gatherWindowCache.NewGatherableIdx, out var idx))
            _gatherWindowCache.NewGatherableIdx = idx;
    }

    private void DrawGatherWindowTab()
    {
        using var id  = ImRaii.PushId("GatherWindow");
        using var tab = ImRaii.TabItem("采集窗口");

        ImGuiUtil.HoverTooltip(
            "配置窗口太大了吗？为什么要塞下这么多信息？\n"
          + "准备一个只包含你真正关心物品的小窗口吧！");

        if (!tab)
            return;

        _gatherWindowCache.Selector.Draw(SelectorWidth);
        ImGui.SameLine();

        ItemDetailsWindow.Draw("预设详情", DrawGatherWindowPresetHeaderLine, () =>
        {
            if (_gatherWindowCache.Selector.Current != null)
                DrawGatherWindowPreset(_gatherWindowCache.Selector.Current);
        });
    }
}
