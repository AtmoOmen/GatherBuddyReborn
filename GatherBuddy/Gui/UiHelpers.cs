using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Interfaces;
using GatherBuddy.RebornUI;
using GatherBuddy.RebornUI.Tables;
using GatherBuddy.Time;
using GatherBuddy.Utility;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    internal static bool DrawLocationInput(IGatherable item, ILocation? current, out ILocation? ret)
    {
        const string noPreferred = "无首选地点";
        var width = SetInputWidth * 0.85f;
        ret = current;
        if (item.Locations.Count == 1)
        {
            var only = 0;
            M3Widgets.Combo("##Location", ref only, 1, _ => item.Locations[0].Name, width, enabled: false,
                tooltip: LocationTooltip(item.Locations[0]));
            return false;
        }

        var index = current == null ? 0 : item.Locations.IndexOf(l => l.Id == current.Id) + 1;
        if (!M3Widgets.Combo("##Location", ref index, item.Locations.Count + 1, i => i == 0 ? noPreferred : item.Locations[i - 1].Name, width,
                noPreferred, item.Locations.Count > 8, tooltip: LocationTooltip(current),
                itemTooltip: i => i == 0 ? null : LocationTooltip(item.Locations[i - 1])))
        {
            return false;
        }

        ret = index <= 0 ? null : item.Locations[index - 1];
        return true;
    }

    private static readonly Dictionary<ILocation, string> LocationTooltips = new(ReferenceEqualityComparer.Instance);

    internal static string? LocationTooltip(ILocation? loc)
    {
        if (loc == null)
        {
            return null;
        }

        if (LocationTooltips.TryGetValue(loc, out var tooltip))
        {
            return tooltip;
        }

        var builder = new StringBuilder();
        if (loc is GatheringNode node)
        {
            builder.Append(loc.Territory.Name).Append('\n').Append(loc.GatheringType).Append('\n').Append(node.Times.PrintHours()).Append('\n');
        }

        var first = true;
        foreach (var gatherable in loc.Gatherables)
        {
            if (!first)
            {
                builder.Append('\n');
            }

            builder.Append(gatherable.Name[GatherBuddy.Language]);
            first = false;
        }

        tooltip = builder.ToString();
        LocationTooltips[loc] = tooltip;
        return tooltip;
    }

    internal static void DrawTimeInterval(TimeInterval uptime, bool uptimeDependency = false, bool rightAligned = true, IGatherable? item = null)
    {
        var active = uptime.ToTimeString(GatherBuddy.Time.ServerTime, false, out var timeString);
        var colorId = (active, uptimeDependency) switch
        {
            (true, true) => ColorId.DependentAvailableFish.Value(),
            (true, false) => ColorId.AvailableItem.Value(),
            (false, true) => ColorId.DependentUpcomingFish.Value(),
            (false, false) => ColorId.UpcomingItem.Value(),
        };

        using (ImRaii.PushColor(ImGuiCol.Text, colorId))
        {
            if (rightAligned)
            {
                Ui.RightAligned(timeString);
            }
            else
            {
                ImGui.TextUnformatted(timeString);
            }
        }

        DrawTimeIntervalTooltip(uptime, uptimeDependency, item, !char.IsLetter(timeString[0]));
    }

    private static readonly string[] UpcomingUptimeHeaders = ["日期", "时间", "距开始", "持续时间", "间隔时间"];

    internal static void DrawTimeIntervalTooltip(TimeInterval uptime, bool uptimeDependency = false, IGatherable? item = null, bool displayNextWindow = true)
    {
        if (!uptimeDependency && !displayNextWindow || !ImGui.IsItemHovered())
        {
            return;
        }

        using var tt = ImRaii.Tooltip();

        if (uptimeDependency)
        {
            M3Badge.Draw("取决于另一种鱼的可捕获时段", M3.Scheme.Warning);
        }

        if (!displayNextWindow)
        {
            return;
        }

        if (item is null || GatherBuddy.Config.UpcomingUptimesCount <= 1)
        {
            ImGui.TextUnformatted($"{uptime.Start}\n{uptime.End}\n{uptime.DurationString()}");
            return;
        }

        var uptimes = GatherBuddy.UptimeManager.GetUpcomingUptimes(item, GatherBuddy.Config.UpcomingUptimesCount);
        if (uptimes.Count <= 0)
        {
            ImGui.TextUnformatted($"{uptime.Start}\n{uptime.End}\n{uptime.DurationString()}\n\n正在加载接下来的 {GatherBuddy.Config.UpcomingUptimesCount} 个可采集时段…");
            return;
        }

        using var tableStyle = M3Table.PushStyle();
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(10, 5) * ImGuiHelpers.GlobalScale);
        using var table = ImRaii.Table("##UpcomingUptimesTable", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX);
        if (!table)
        {
            return;
        }

        foreach (var header in UpcomingUptimeHeaders)
        {
            ImGui.TableSetupColumn(header);
        }

        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        foreach (var header in UpcomingUptimeHeaders)
        {
            ImGui.TableNextColumn();
            M3Table.Header(header, center: false);
        }

        DateTime? previousStartDate = null;
        var now = GatherBuddy.Time.ServerTime;

        for (var i = 0; i < uptimes.Count && i < GatherBuddy.Config.UpcomingUptimesCount; i++)
        {
            var time = uptimes[i];
            var startDate = time.Start.LocalTime;
            ImGui.TableNextColumn();
            if (previousStartDate is null || previousStartDate.Value.Date != startDate.Date)
            {
                ImGui.TextUnformatted(startDate.ToString("yyyy-MM-dd (ddd)", CultureInfo.InvariantCulture));
                previousStartDate = startDate;
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(startDate.ToString("HH:mm", CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(now < time.Start ? new TimeInterval(now, time.Start).DurationString(true) : "进行中");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(time.DurationString(true));

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(i < uptimes.Count - 1 ? new TimeInterval(uptimes[i].End, uptimes[i + 1].Start).DurationString(true) : "-");
        }
    }

    internal static void HoverTooltip(string text)
    {
        if (!text.StartsWith('\0'))
        {
            M3Tooltip.Hovered(text);
        }
    }

    public static void AlignTextToSize(string text, Vector2 size)
    {
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursor + new Vector2(ImGui.GetStyle().ItemSpacing.X / 2, (size.Y - ImGui.GetTextLineHeight()) / 2));
        ImGui.TextUnformatted(text);
        ImGui.SameLine();
        ImGui.SetCursorPosY(cursor.Y);
        ImGui.NewLine();
    }

    private static float TableFooterHeight
        => (M3Widgets.ButtonHeight + ImGui.GetStyle().ItemSpacing.Y * 2f + M3.Space1) / ImGuiHelpers.GlobalScale;

    private void DrawTableFooter<T>(Table<T> table, Func<T, IGatherable> selector, string label, string statusName)
    {
        ImGui.Dummy(new Vector2(0, M3.Space1));
        var rowTop = ImGui.GetCursorScreenPos().Y;
        var drewButton = DrawAddAllFilteredToAutoGather(table, selector, label);
        DrawStatusLine(table, statusName, drewButton, rowTop, M3Widgets.ButtonHeight);
    }

    private static void DrawStatusLine<T>(Table<T> table, string name, bool sameLine, float rowTop, float rowHeight)
    {
        if (!GatherBuddy.Config.ShowStatusLine)
        {
            return;
        }

        void Next(float itemHeight)
        {
            if (sameLine)
            {
                ImGui.SameLine(0, M3.Space3);
            }

            sameLine = true;
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowTop + (rowHeight - itemHeight) * 0.5f));
        }

        Next(ImGui.GetTextLineHeight());
        M3Text.Draw($"显示 {table.CurrentItems} / {table.TotalItems} 项{name}", M3TextStyle.Muted);
        if (table.TotalColumns != table.VisibleColumns)
        {
            Next(ImGui.GetTextLineHeight());
            var hidden = table.TotalColumns - table.VisibleColumns;
            M3Text.Draw(hidden == 1 ? "已隐藏 1 列" : $"{hidden} 列已隐藏", M3TextStyle.Muted);
        }

        if (typeof(T) != typeof(ExtendedFish))
        {
            return;
        }

        if (File.Exists(GatherBuddy.GameData.OverrideFile))
        {
            Next(M3Widgets.ChipHeight);
            if (M3Widgets.Chip("##reimport", "重新导入鱼类修正数据", false, FontAwesomeIcon.Redo,
                    "重新读取鱼类修正数据文件。\n右键点击可将文件移为备份。")
             && GatherBuddy.GameData.ReimportOverrides())
            {
                GatherBuddy.UptimeManager.ResetModifiedUptimes();
                LocationTooltips.Clear();
                foreach (var fish in ExtendedFishList)
                {
                    if (fish.Data.HasOverridenData)
                    {
                        fish.Update();
                    }
                }
            }

            using var menu = M3Menu.BeginContext("##overrideMenu");
            if (menu.IsOpen && M3Menu.Item("##backup", "移为备份", FontAwesomeIcon.Archive))
            {
                try
                {
                    File.Move(GatherBuddy.GameData.OverrideFile, Path.ChangeExtension(GatherBuddy.GameData.OverrideFile, ".json.bak"), true);
                }
                catch (Exception ex)
                {
                    GatherBuddy.Log.Error($"无法将鱼类数据覆盖文件移动到备份:\n{ex}");
                }
            }
        }

        if (GatherBuddy.GameData.OverriddenFish > 0)
        {
            Next(ImGui.GetTextLineHeight());
            M3Text.Draw($"{GatherBuddy.GameData.OverriddenFish} 种鱼使用修正数据", M3TextStyle.Muted);
        }
    }

    private static void DrawClippy()
    {
        const string popupName = "GatherClippy###ClippyPopup";
        const string text = "找不到想要的物品？";
        if (GatherBuddy.Config.HideClippy)
        {
            return;
        }

        const FontAwesomeIcon icon = FontAwesomeIcon.QuestionCircle;
        var buttonSize = M3Widgets.PillSize(text, icon);
        var padding = new Vector2(M3.Space3);

        ImGui.SetCursorPos(ImGui.GetWindowSize() - buttonSize - padding);
        using var child = ImRaii.Child("##clippyChild", buttonSize, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration);
        if (!child)
        {
            return;
        }

        if (M3Widgets.Pill("##clippy", text, M3.Scheme.Tertiary, icon,
                "点击查看表格操作说明。\n按住 Ctrl + Shift 并右键点击可永久隐藏该按钮。", true))
        {
            ImGui.OpenPopup(popupName);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
        {
            GatherBuddy.Config.HideClippy = true;
            GatherBuddy.Config.Save();
        }

        var width = 760f * M3.Scale;
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize / 2, ImGuiCond.Always, new Vector2(0.5f));
        using var popup = ImRaii.Popup(popupName,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.Modal);
        if (!popup)
        {
            return;
        }

        using (ImRaii.PushFont(M3.TitleMedium))
        {
            ImGui.TextUnformatted("表格操作说明");
        }

        ImGui.Dummy(new Vector2(width, M3.Space1));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
        M3Widgets.SectionLabel("筛选");
        M3Text.Bullet("「筛选物品」等文本筛选会保留包含输入文字的条目，忽略大小写。重启插件后会清除输入内容。");
        M3Text.Bullet("文本过滤器还支持正则表达式，例如 \"(blue|green)\" 会匹配所有包含蓝色或绿色的条目。");
        M3Text.Bullet("点击下次刷新、采集点类型或鱼类类型等按钮，可打开菜单选择要显示的内容。");
        M3Text.Bullet("这些筛选会保存。启用筛选的列会显示带筛选图标的着色按钮，右键点击可清除该筛选。");
        M3Widgets.SectionLabel("列");
        M3Text.Bullet("点击表头的空白处可按此列进行升序或降序排序，三角图标指示排序方向。");
        M3Text.Bullet("右键点击表头的空白处可打开表格菜单，其中可以隐藏不需要的列。");
        M3Text.Bullet("拖动列之间的分隔线可调整文本列宽，插件会保存列宽。");
        M3Text.Bullet("拖动表头空白处可调整多数列的顺序，插件会保存列顺序。");
        M3Widgets.SectionLabel("物品");
        M3Text.Bullet("右键点击物品名、鱼饵或钓场等列，可打开包含更多选项的菜单。");
        ImGui.PopTextWrapPos();

        ImGui.Dummy(new Vector2(0f, M3.Space2));
        if (M3Widgets.Button("##clippy_understood", "明白了", M3ButtonStyle.Filled, FontAwesomeIcon.Check, width))
        {
            ImGui.CloseCurrentPopup();
        }
    }
}
