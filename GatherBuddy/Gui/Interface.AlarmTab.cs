using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Alarms;
using GatherBuddy.Config;
using GatherBuddy.GatherHelper;
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
    private static string CheckUnnamed(string name)
        => name.Length > 0 ? name : "<未命名>";

    private static string CheckUndescribed(string desc)
        => desc.Length > 0 ? desc : "<无描述>";

    private class AlarmWindowDragDropData(AlarmGroup group, Alarm alarm, int idx)
    {
        public AlarmGroup Group = group;
        public Alarm Alarm = alarm;
        public int AlarmIdx = idx;
    }

    private class AlarmCache(AlarmManager manager)
    {
        public sealed class AlarmSelector(AlarmManager manager) : ItemSelector<AlarmGroup>(manager.Alarms, Flags.All)
        {
            private readonly AlarmManager _manager = manager;

            protected override bool Filtered(int idx)
                => Filter.Length != 0 && !Items[idx].Name.Contains(Filter, StringComparison.InvariantCultureIgnoreCase);

            protected override bool OnDraw(int idx)
                => ListRow(CheckUnnamed(Items[idx].Name), idx == CurrentIdx, !Items[idx].Enabled);

            protected override string NameOf(AlarmGroup item)
                => item.Name;

            protected override bool OnDelete(int idx)
            {
                _manager.DeleteGroup(idx);
                return true;
            }

            protected override bool OnAdd(string name)
            {
                _manager.AddGroup(name);
                return true;
            }

            protected override bool OnClipboardImport(string name, string data)
            {
                if (!AlarmGroup.Config.FromBase64(data, out var configGroup))
                {
                    return false;
                }

                var alarms = new List<Alarm>();
                var configAlarmCount = 0;
                foreach (var alarmConfig in configGroup.Alarms)
                {
                    configAlarmCount++;
                    if (Alarm.FromConfig(alarmConfig, out var alarm) && alarm != null)
                    {
                        alarms.Add(alarm);
                    }
                }

                var group = new AlarmGroup()
                {
                    Name = name,
                    Description = configGroup.Description,
                    Enabled = false,
                    Alarms = alarms,
                };

                if (group.Alarms.Count < configAlarmCount)
                {
                    GatherBuddy.Log.Warning("已跳过无效的闹钟");
                }

                _manager.AddGroup(group);
                return true;
            }

            protected override bool OnDuplicate(string name, int idx)
            {
                var group = _manager.Alarms[idx].Clone();
                group.Name = name;
                _manager.AddGroup(group);
                return true;
            }

            protected override void OnDrop(object? data, int idx)
            {
                if (data is not AlarmWindowDragDropData obj)
                {
                    return;
                }

                var group = _plugin.AlarmManager.Alarms[idx];
                _plugin.AlarmManager.DeleteAlarm(obj.Group, obj.AlarmIdx);
                _plugin.AlarmManager.AddAlarm(group, obj.Alarm);
            }

            protected override bool OnMove(int idx1, int idx2)
            {
                _manager.MoveGroup(idx1, idx2);
                return idx1 != idx2;
            }
        }

        public static readonly Sounds[] SoundIds = Array.FindAll(Enum.GetValues<Sounds>(), s => s != Sounds.Unknown);

        public static readonly string[] SoundIdNames = Array.ConvertAll(SoundIds, s => s == Sounds.None ? "无音效" : $"音效 {s.ToIdx()}");

        public readonly AlarmSelector Selector = new(manager);

        public string NewName = string.Empty;
        public int NewItemIdx;
        public bool NewEnabled;
        public bool NewPrintMessage;
        public int NewSoundIdx;
        public int NewSecondOffset;

        public Alarm CreateAlarm()
            => new(GatherBuddy.UptimeManager.TimedGatherables[NewItemIdx])
            {
                Enabled = NewEnabled,
                SecondOffset = NewSecondOffset,
                PrintMessage = NewPrintMessage,
                Name = NewName,
                SoundId = SoundIds[NewSoundIdx],
            };
    }

    private readonly AlarmCache _alarmCache;

    private static readonly string[] AlarmColumns = ["##delete", "名称", "启用", "物品", "提前秒数", "聊天提示", "提示音", "地点", "下次提醒"];
    private static readonly string[] AlarmColumnLabels = ["", "名称", "启用", "物品", "提前秒数", "聊天", "音效", "地点", "下次触发"];

    private static float AlarmItemWidth
        => SetInputWidth * 1.15f;

    private static float AlarmSoundWidth
        => 120 * M3.Scale;

    private static bool DrawTimedItemCombo(string id, ref int index)
    {
        var items = GatherBuddy.UptimeManager.TimedGatherables;
        return M3Widgets.Combo(id, ref index, items.Length, i => items[i].Name[GatherBuddy.Language], AlarmItemWidth, "选择物品", true);
    }

    private void DrawAlarmInfo(ref int alarmIdx, AlarmGroup group)
    {
        var alarm = group.Alarms[alarmIdx];
        using var id = ImRaii.PushId(alarmIdx);

        var rowHeight = M3Widgets.NumberFieldHeight;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, ImGui.GetFrameHeight());
        if (M3Widgets.IconButton("##delete", FontAwesomeIcon.Trash, "删除此闹钟", diameter: ImGui.GetFrameHeight()))
        {
            _plugin.AlarmManager.DeleteAlarm(group, alarmIdx--);
            return;
        }

        ImGui.TableNextColumn();
        var name = alarm.Name;
        if (M3TextField.Draw("##name", CheckUnnamed(string.Empty), ref name, SetInputWidth, 64))
        {
            _plugin.AlarmManager.ChangeAlarmName(group, alarmIdx, name);
        }

        M3Tooltip.Hovered("名称可留空，可在发送到聊天栏的闹钟消息中使用。");

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.CheckboxHitSize().Y);
        var enabled = alarm.Enabled;
        if (M3Widgets.Checkbox("##enabled", ref enabled) && enabled != alarm.Enabled)
        {
            _plugin.AlarmManager.ToggleAlarm(group, alarmIdx);
        }

        M3Tooltip.Hovered("启用此闹钟。");

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        var itemIdx = alarm.Item.InternalLocationId - 1;
        if (DrawTimedItemCombo("##item", ref itemIdx))
        {
            _plugin.AlarmManager.ChangeAlarmItem(group, alarmIdx, GatherBuddy.UptimeManager.TimedGatherables[itemIdx]);
        }

        _alarmCache.Selector.CreateDropSource(new AlarmWindowDragDropData(group, alarm, alarmIdx), alarm.Item.Name[GatherBuddy.Language]);
        var localIdx = alarmIdx;
        _alarmCache.Selector.CreateDropTarget<AlarmWindowDragDropData>(d => _plugin.AlarmManager.MoveAlarm(group, d.AlarmIdx, localIdx));

        ImGui.TableNextColumn();
        var secondOffset = alarm.SecondOffset;
        if (M3Widgets.NumberField("##offset", ref secondOffset, SetInputWidth * 0.6f, 10, 0, RealTime.SecondsPerDay, "s"))
        {
            _plugin.AlarmManager.ChangeAlarmOffset(group, alarmIdx, Math.Clamp(secondOffset, 0, RealTime.SecondsPerDay));
        }

        M3Tooltip.Hovered("在物品下次可采集前提前指定秒数触发闹钟。");

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.CheckboxHitSize().Y);
        var printMessage = alarm.PrintMessage;
        if (M3Widgets.Checkbox("##print", ref printMessage))
        {
            _plugin.AlarmManager.ChangeAlarmMessage(group, alarmIdx, printMessage);
        }

        M3Tooltip.Hovered("闹钟触发时在聊天栏显示消息。");

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        var soundIdx = Array.IndexOf(AlarmCache.SoundIds, alarm.SoundId);
        if (M3Widgets.Combo("##sound", ref soundIdx, AlarmCache.SoundIdNames, AlarmSoundWidth, tooltip: "闹钟触发时播放的音效。"))
        {
            _plugin.AlarmManager.ChangeAlarmSound(group, alarmIdx, AlarmCache.SoundIds[soundIdx]);
            AlarmManager.PreviewAlarm(AlarmCache.SoundIds[soundIdx]);
        }

        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        if (DrawLocationInput(alarm.Item, alarm.PreferLocation, out var newLocation))
        {
            _plugin.AlarmManager.ChangeAlarmLocation(group, alarmIdx, newLocation);
        }

        ImGui.TableNextColumn();
        var (_, time) = AlarmManager.GetUptime(alarm);
        var now = GatherBuddy.Time.ServerTime.AddSeconds(alarm.SecondOffset);
        var size = new Vector2(150 * M3.Scale, ImGui.GetFrameHeight());
        Ui.CenterInRow(rowHeight, size.Y);
        if (time.Start > now)
        {
            Ui.Framed(TimeInterval.DurationString(time.Start, now, false), ColorId.WarningBg.Value(), size);
        }
        else
        {
            Ui.Framed("正在触发", ColorId.ChangedLocationBg.Value(), size);
        }
    }

    private static void DrawGroupData(AlarmGroup group, int idx)
    {
        if (Ui.CommitField("##groupName", "名称", group.Name, out var newName, SetInputWidth, 64))
        {
            _plugin.AlarmManager.ChangeGroupName(idx, newName);
        }

        ImGui.SameLine(0, M3.Space2);
        if (Ui.CommitField("##groupDesc", "描述", group.Description, out var newDesc, 2 * SetInputWidth, 128))
        {
            _plugin.AlarmManager.ChangeGroupDescription(idx, newDesc);
        }

        ImGui.SameLine(0, M3.Space3);
        var enabled = group.Enabled;
        Ui.CenterInRow(M3TextField.Height, M3Widgets.SwitchSize().Y);
        if (M3Widgets.Switch("##groupEnabled", ref enabled) && enabled != group.Enabled)
        {
            _plugin.AlarmManager.ToggleGroup(idx);
        }

        M3Tooltip.Hovered("启用此闹钟组。组内只有单独启用的闹钟才会生效。");
        ImGui.SameLine(0, M3.Space2);
        Ui.RowText(enabled ? "已启用" : "已禁用", M3TextField.Height, M3TextStyle.Muted);
    }

    private static void DrawAlarmTableHeader(AlarmGroup group)
    {
        var headerHeight = group.Alarms.Count > 0 ? M3Widgets.CheckboxHitSize().Y : 0f;
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var i = 0; i < AlarmColumns.Length; ++i)
        {
            ImGui.TableNextColumn();
            if (i == 2 && group.Alarms.Count > 0)
            {
                var allEnabled = true;
                var anyEnabled = false;
                foreach (var alarm in group.Alarms)
                {
                    if (alarm.Enabled)
                    {
                        anyEnabled = true;
                    }
                    else
                    {
                        allEnabled = false;
                    }
                }

                if (M3Widgets.Checkbox("##allEnabled", ref allEnabled, mixed: !allEnabled && anyEnabled))
                {
                    for (var j = 0; j < group.Alarms.Count; ++j)
                    {
                        if (group.Alarms[j].Enabled != allEnabled)
                        {
                            _plugin.AlarmManager.ToggleAlarm(group, j);
                        }
                    }
                }

                M3Tooltip.Hovered("启用或禁用全部闹钟。");
                continue;
            }

            if (!AlarmColumns[i].StartsWith("##"))
            {
                Ui.TableHeader(AlarmColumnLabels[i], headerHeight);
            }
        }
    }

    private void DrawAlarmTable(AlarmGroup group)
    {
        using var style = M3Table.PushStyle();
        using var table = ImRaii.Table("##alarms", AlarmColumns.Length, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoKeepColumnsVisible
          | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
        {
            return;
        }

        foreach (var column in AlarmColumns)
        {
            ImGui.TableSetupColumn(column);
        }

        DrawAlarmTableHeader(group);
        for (var i = 0; i < group.Alarms.Count; ++i)
        {
            DrawAlarmInfo(ref i, group);
        }

        using var id = ImRaii.PushId(-1);
        var rowHeight = M3Widgets.NumberFieldHeight;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, ImGui.GetFrameHeight());
        if (M3Widgets.IconButton("##add", FontAwesomeIcon.Plus, "添加闹钟", M3ButtonStyle.Tonal, diameter: ImGui.GetFrameHeight()))
        {
            _plugin.AlarmManager.AddAlarm(group, _alarmCache.CreateAlarm());
        }

        ImGui.TableNextColumn();
        M3TextField.Draw("##name", "新闹钟名称", ref _alarmCache.NewName, SetInputWidth, 64);
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.CheckboxHitSize().Y);
        M3Widgets.Checkbox("##enabled", ref _alarmCache.NewEnabled);
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        DrawTimedItemCombo("##item", ref _alarmCache.NewItemIdx);
        ImGui.TableNextColumn();
        M3Widgets.NumberField("##offset", ref _alarmCache.NewSecondOffset, SetInputWidth * 0.6f, 10, 0, RealTime.SecondsPerDay, "s");
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.CheckboxHitSize().Y);
        M3Widgets.Checkbox("##print", ref _alarmCache.NewPrintMessage);
        ImGui.TableNextColumn();
        Ui.CenterInRow(rowHeight, M3Widgets.ComboHeight);
        M3Widgets.Combo("##sound", ref _alarmCache.NewSoundIdx, AlarmCache.SoundIdNames, AlarmSoundWidth);
    }

    private void DrawAlarmInfo(AlarmGroup group, int idx)
    {
        DrawGroupData(group, idx);
        ImGui.Dummy(new Vector2(0, M3.Space2));
        DrawAlarmTable(group);
    }

    private void DrawAlarmGroupHeaderLine()
    {
        var current = _alarmCache.Selector.Current;
        var row = new Ui.Row(M3Widgets.ButtonHeight);
        row.Place(M3Widgets.IconButtonSize);
        if (M3Widgets.IconButton("##copy", FontAwesomeIcon.Copy, "将此闹钟组复制到剪贴板", enabled: current != null))
        {
            try
            {
                var s = new AlarmGroup.Config(current!).ToBase64();
                ImGui.SetClipboardText(s);
                Communicator.PrintClipboardMessage("闹钟组 ", current!.Name);
            }
            catch (Exception e)
            {
                GatherBuddy.Log.Error($"Could not write Alarm Group {current!.Name} to Clipboard:\n{e}");
                Communicator.PrintClipboardMessage("闹钟组 ", current!.Name, e);
            }
        }

        row.Next(M3Widgets.ButtonHeight, M3.Space1);
        if (M3Widgets.Button("##preset", "创建采集窗口预设", M3ButtonStyle.Text, FontAwesomeIcon.ListUl, enabled: current != null,
                tooltip: "根据此闹钟组创建新的采集窗口预设。"))
        {
            _plugin.GatherWindowManager.AddPreset(new GatherWindowPreset(current!));
            M3Snackbar.Show("已创建采集窗口预设。");
        }

        row.Next(ImGui.GetFrameHeight(), M3.Space2);
        M3Widgets.HelpMarker("使用 /gather alarm 采集上次触发闹钟的物品。\n"
          + "使用 /gatherfish alarm 钓取上次触发闹钟的鱼。");
    }

    private void DrawAlarmPage()
    {
        using var id = ImRaii.PushId("Alarms");
        _alarmCache.Selector.Draw(SelectorWidth);
        ImGui.SameLine(0, M3.Space2);

        ItemDetailsWindow.Draw("闹钟组详情", DrawAlarmGroupHeaderLine, () =>
        {
            if (_alarmCache.Selector.Current != null)
            {
                DrawAlarmInfo(_alarmCache.Selector.Current, _alarmCache.Selector.CurrentIdx);
            }
            else
            {
                M3Widgets.EmptyState("##noAlarms", "未选择闹钟组", "点击列表下方的加号添加组。", FontAwesomeIcon.Bell);
            }
        });
    }
}
