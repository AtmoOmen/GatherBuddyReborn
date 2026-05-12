using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using GatherBuddy.Config;
using GatherBuddy.Interfaces;
using GatherBuddy.Time;
using ElliLib;
using ElliLib.Table;
using ElliLib.Text;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    internal static bool DrawLocationInput(IGatherable item, ILocation? current, out ILocation? ret)
    {
        const string noPreferred = "无偏好采集位置";
        var          width       = SetInputWidth * 0.85f;
        ret = current;
        if (item.Locations.Count == 1)
        {
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f));
            ImUtf8.TextFramed(item.Locations[0].Name, ImGui.GetColorU32(ImGuiCol.FrameBg), new Vector2(width, 0));
            DrawLocationTooltip(item.Locations[0]);
            return false;
        }

        var text = current?.Name ?? noPreferred;
        ImGui.SetNextItemWidth(width);
        using var combo = ImUtf8.Combo("##Location"u8, text);
        DrawLocationTooltip(current);
        if (!combo)
            return false;

        var changed = false;

        if (ImGui.Selectable(noPreferred, current == null))
        {
            ret     = null;
            changed = true;
        }

        var idx = 0;
        foreach (var loc in item.Locations)
        {
            using var id = ImUtf8.PushId(idx++);
            if (ImUtf8.Selectable(loc.Name, loc.Id == (current?.Id ?? 0)))
            {
                ret     = loc;
                changed = true;
            }

            DrawLocationTooltip(loc);
        }

        return changed;
    }

    internal static void DrawTimeInterval(TimeInterval uptime, bool uptimeDependency = false, bool rightAligned = true)
    {
        var active = uptime.ToTimeString(GatherBuddy.Time.ServerTime, false, out var timeString);
        var colorId = (active, uptimeDependency) switch
        {
            (true, true)   => ColorId.DependentAvailableFish.Value(),
            (true, false)  => ColorId.AvailableItem.Value(),
            (false, true)  => ColorId.DependentUpcomingFish.Value(),
            (false, false) => ColorId.UpcomingItem.Value(),
        };
        using var color = ImRaii.PushColor(ImGuiCol.Text, colorId);
        if (rightAligned)
            ImUtf8.TextRightAligned(timeString);
        else
            ImUtf8.Text(timeString);
        color.Pop();
        if ((uptimeDependency || !char.IsLetter(timeString[0])) && ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();

            if (uptimeDependency)
                ImUtf8.TextFramed("依赖前置鱼"u8, 0xFF202080);

            if (!char.IsLetter(timeString[0]))
                ImUtf8.Text($"{uptime.Start}\n{uptime.End}\n{uptime.DurationString()}");
        }
    }

    internal static void HoverTooltip(string text)
    {
        if (!text.StartsWith('\0'))
            ImUtf8.HoverTooltip(text);
    }

    public static void AlignTextToSize(string text, Vector2 size)
    {
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(cursor + new Vector2(ImGui.GetStyle().ItemSpacing.X / 2, (size.Y - ImGui.GetTextLineHeight()) / 2));
        ImUtf8.Text(text);
        ImGui.SameLine();
        ImGui.SetCursorPosY(cursor.Y);
        ImGui.NewLine();
    }


    private static void DrawFormatInput(string label, string tooltip, string oldValue, string defaultValue, Action<string> setValue)
    {
        var       tmp = oldValue;
        using var id  = ImRaii.PushId(label);

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 50 * Scale);
        if (ImGui.InputText(string.Empty, ref tmp, 256) && tmp != oldValue)
        {
            setValue(tmp);
            GatherBuddy.Config.Save();
        }

        ImGuiUtil.HoverTooltip(tooltip);

        if (ImGuiUtil.DrawDisabledButton("Default", Vector2.Zero, defaultValue, defaultValue == oldValue))
        {
            setValue(defaultValue);
            GatherBuddy.Config.Save();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
    }

    private static void DrawStatusLine<T>(Table<T> table, string name)
    {
        if (!GatherBuddy.Config.ShowStatusLine)
            return;

        ImGui.SameLine();
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.NewLine();
        ImUtf8.Text($"{table.CurrentItems} / {table.TotalItems} {name} 可见");
        if (table.TotalColumns != table.VisibleColumns)
        {
            ImGui.SameLine(0, 50 * ImGuiHelpers.GlobalScale);
            ImUtf8.Text($"{table.TotalColumns - table.VisibleColumns} 列被隐藏");
        }

        if (typeof(T) == typeof(ExtendedFish))
        {
            if (File.Exists(GatherBuddy.GameData.OverrideFile))
            {
                ImGui.SameLine(0, 50 * ImGuiHelpers.GlobalScale);
                if (ImUtf8.SmallButton("重新导入鱼类覆盖数据") && GatherBuddy.GameData.ReimportOverrides())
                {
                    GatherBuddy.UptimeManager.ResetModifiedUptimes();
                    foreach (var fish in ExtendedFishList.Where(f => f.Data.HasOverridenData))
                        fish.Update();
                }
            }
            
            using (var popup = ImUtf8.PopupContextItem("##Context"u8))
            {
                if (popup)
                    if (ImUtf8.MenuItem("Move to Backup"u8))
                        try
                        {
                            File.Move(GatherBuddy.GameData.OverrideFile, Path.ChangeExtension(GatherBuddy.GameData.OverrideFile, ".json.bak"),
                                true);
                        }
                        catch (Exception ex)
                        {
                            GatherBuddy.Log.Error($"Could not move fish data override file to backup:\n{ex}");
                        }
            }
            
            if (GatherBuddy.GameData.OverriddenFish > 0)
            {
                ImGui.SameLine(0, 50 * ImGuiHelpers.GlobalScale);
                ImUtf8.Text($"{GatherBuddy.GameData.OverriddenFish} 鱼类覆盖已应用");
            }
        }
    }

    private static void DrawClippy()
    {
        const string popupName = "GatherClippy###ClippyPopup";
        const string text      = "找不到想要的物品？";
        if (GatherBuddy.Config.HideClippy)
            return;

        var textSize   = ImGui.CalcTextSize(text).X;
        var buttonSize = new Vector2(Math.Max(200, textSize) * ImGuiHelpers.GlobalScale, ImGui.GetFrameHeight());
        var padding    = ImGuiHelpers.ScaledVector2(9, 9);

        ImGui.SetCursorPos(ImGui.GetWindowSize() - buttonSize - padding);
        using var child = ImRaii.Child("##clippyChild", buttonSize, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration);
        if (!child)
            return;

        using var color = ImRaii.PushColor(ImGuiCol.Button, 0xFFA06020);

        if (ImGui.Button(text, buttonSize))
            ImGui.OpenPopup(popupName);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
        {
            GatherBuddy.Config.HideClippy = true;
            GatherBuddy.Config.Save();
        }

        ImGuiUtil.HoverTooltip("单击以打开本表的使用帮助\n"
          + "按住 Ctrl + Shift 键右键本按钮以永久隐藏");

        color.Pop();
        var windowSize = new Vector2(1024 * ImGuiHelpers.GlobalScale,
            ImGui.GetTextLineHeightWithSpacing() * 13 + 2 * ImGui.GetFrameHeightWithSpacing());
        ImGui.SetNextWindowSize(windowSize);
        ImGui.SetNextWindowPos((ImGui.GetIO().DisplaySize - windowSize) / 2);
        using var popup = ImRaii.Popup(popupName,
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.Modal);
        if (!popup)
            return;

        ImGui.BulletText(
            "可以使用诸如 \"物品名称...\" 之类的文本过滤器，只显示包含指定字符串的条目。它们对大小写不敏感，并且不会保存。");
        ImGui.BulletText(
            "文本过滤器还支持正则表达式，例如 \"(blue|green)\" 会匹配所有包含蓝色或绿色的条目。");
        ImGui.BulletText("类似 \"下次时间\"、\"采集点类型\" 或 \"鱼类类型\" 的按钮过滤器可以通过点击来过滤特定类型。");
        ImGui.BulletText("这些过滤器会保存。但对于具有活动过滤器的列，过滤按钮会变为红色。");
        ImGui.NewLine();
        ImGui.BulletText(
            "可以点击标题空白处来按该列对表格进行升序或降序排序，这会通过一个向上或向下的小三角形来表示。");
        ImGui.BulletText(
            "可以右键点击标题空白处来打开表格上下文菜单，在其中可以隐藏不感兴趣的列。");
        ImGui.BulletText(
            "可以通过拖动列的分隔线来调整文本列的大小。调整时会以蓝色高亮显示分隔线。大小会在会话之间保存。");
        ImGui.BulletText(
            "可以通过在空白处左键点击并拖动来重新排列大多数列。排列顺序会在会话之间保存。");
        ImGui.NewLine();
        ImGui.BulletText(
            "可以右键点击物品名称和其他一些列（如钓饵和钓鱼地点）来打开带有对象特定选项的右键菜单");
        ImGui.BulletText("你也可以重新排列标签页，但无法保存");

        ImGui.SetCursorPosY(windowSize.Y - ImGui.GetFrameHeight() - ImGui.GetStyle().WindowPadding.Y);
        if (ImGui.Button("明白了", -Vector2.UnitX))
            ImGui.CloseCurrentPopup();
    }
}
