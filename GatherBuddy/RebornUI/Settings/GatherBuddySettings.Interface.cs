using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Config;
using GatherBuddy.RebornUI.Settings;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using FishRecord = GatherBuddy.FishTimer.FishRecord;

namespace GatherBuddy.RebornUI;

internal sealed partial class GatherBuddySettings
{
    private M3SettingsPage BuildInterfacePage()
        => new(InterfacePageId, "界面设置", FontAwesomeIcon.Palette, "主题、窗口、叠加显示和颜色",
            new M3SettingsGroup("theme", "主题", FontAwesomeIcon.Palette, "应用于所有 GatherBuddy Reborn 和 Vulcan 窗口。",
                Custom("Accent color color theme scheme 强调色 颜色 主题 配色", DrawAccentColor),
                Custom("Text size scale font 文字大小 字号 字体", () => SettingRows.Slider("文字大小", "在 Dalamud 的字号基础上缩放文字。",
                    Config.UiTextScale, 0.75f, 2f, "%.2fx", v => Config.UiTextScale = MathF.Round(v, 2))),
                Custom("Control size element scale buttons switches 控件大小 缩放 按钮 开关", () => SettingRows.Slider("控件大小",
                    "缩放按钮、开关、输入框和其他控件。",
                    Config.UiElementScale, 0.75f, 2f, "%.2fx", v => Config.UiElementScale = MathF.Round(v, 2))),
                Custom("Spacing padding scale 间距 留白 缩放", () => SettingRows.Slider("间距", "调整各元素之间的间距而不改变其大小。",
                    Config.UiPaddingScale, 0.5f, 2f, "%.2fx", v => Config.UiPaddingScale = MathF.Round(v, 2))),
                Custom("Animation length motion 动画时长 动效", () => SettingRows.Slider("动画时长",
                    "调整折叠窗口、打开卡片等动画的时长；设为 0% 可关闭动画。",
                    Config.UiMotionScale * 100f, 0f, 200f, "%.0f%%", v => Config.UiMotionScale = MathF.Round(v) / 100f, 1f)),
                Toggle("紧凑布局", "减少留白和行高，说明改为悬停显示，以便放下更多内容。",
                    () => Config.UiCompactLayout, v => Config.UiCompactLayout = v, "density dense tight tooltips"),
                Toggle("侧栏仅显示图标", "将侧栏缩为一列图标，悬停时显示名称。",
                    () => Config.UiIconSidebar, v => Config.UiIconSidebar = v, "navigation narrow collapse strip"),
                Custom("Reset sizes 重置大小", DrawResetSizes)),
            new M3SettingsGroup("main_window", "主窗口", FontAwesomeIcon.WindowMaximize, null,
                Toggle("启动时打开", "启动游戏时显示 GatherBuddy 窗口。",
                    () => Config.OpenOnStart, v => Config.OpenOnStart = v, "open config ui on start"),
                Toggle("按 Esc 关闭主窗口", "主窗口获得焦点时，按 Esc 将其关闭。",
                    () => Config.CloseOnEscape, v => Config.CloseOnEscape = v),
                Toggle("锁定窗口位置", "禁止拖动主窗口。",
                    () => Config.MainWindowLockPosition, v => Config.MainWindowLockPosition = v, "lock config ui movement"),
                Toggle("锁定窗口大小", "禁止调整主窗口大小。",
                    () => Config.MainWindowLockResize, v => Config.MainWindowLockResize = v, "lock config ui size"),
                Custom("Hotkey to open main interface 主窗口快捷键", () => SettingRows.Hotkey("主窗口快捷键",
                    "打开或关闭 GatherBuddy 主窗口。", Config.MainInterfaceHotkey, v => Config.MainInterfaceHotkey = v)),
                Toggle("在天气页面显示天气名称", "在天气表中显示天气名称，不必悬停在图标上查看。",
                    () => Config.ShowWeatherNames, v => Config.ShowWeatherNames = v, "weather tab"),
                Toggle("显示状态栏", "在采集物和鱼类列表下方显示状态栏。",
                    () => Config.ShowStatusLine, v => Config.ShowStatusLine = v),
                Toggle("隐藏 GatherClippy 按钮", "始终在采集物和鱼类页面隐藏 GatherClippy 按钮。",
                    () => Config.HideClippy, v => Config.HideClippy = v),
                Int("即将出现的时段数量", "设置提示中列出的后续采集时段数量。",
                    () => Config.UpcomingUptimesCount, v => Config.UpcomingUptimesCount = (ushort)v, 1, 100)),
            new M3SettingsGroup("fish_timer", "钓鱼计时器", FontAwesomeIcon.Stopwatch, null,
                Toggle("保存钓鱼记录", "在本机保存钓鱼记录，供钓鱼计时器计算咬钩时间。",
                    () => Config.StoreFishRecords, v => Config.StoreFishRecords = v),
                Toggle("使用本地时间显示记录", "在钓鱼记录页面用本地时间显示时间戳，而不是 Unix 时间。",
                    () => Config.UseUnixTimeFishRecords, v => Config.UseUnixTimeFishRecords = v),
                Toggle("显示钓鱼计时器", "钓鱼时显示钓鱼计时器窗口。",
                    () => Config.ShowFishTimer, v => Config.ShowFishTimer = v),
                Toggle("编辑钓鱼计时器", "允许移动钓鱼计时器窗口并调整其大小。",
                    () => Config.FishTimerEdit, v => Config.FishTimerEdit = v),
                Toggle("启用钓鱼计时器点击穿透", "让点击穿过钓鱼计时器，同时禁用其右键菜单。",
                    () => Config.FishTimerClickthrough, v => Config.FishTimerClickthrough = v),
                Toggle("在钓鱼计时器中隐藏未捕获的鱼",
                    "隐藏在当前撒饵和鱼饵组合下从未记录过的鱼。",
                    () => Config.HideUncaughtFish, v => Config.HideUncaughtFish = v),
                Toggle("在钓鱼计时器中隐藏不可用的鱼",
                    "隐藏已知条件未满足的鱼，例如缺少捕鱼人之识或撒饵。",
                    () => Config.HideUnavailableFish, v => Config.HideUnavailableFish = v),
                Toggle("在钓鱼计时器中显示出现时段", "在钓鱼计时器窗口中显示受限鱼类的出现时段。",
                    () => Config.ShowFishTimerUptimes, v => Config.ShowFishTimerUptimes = v),
                Custom("Fish timer bite time scale seconds 钓鱼计时器 咬钩时间 秒数", DrawBiteTimeScale),
                Custom("Fish timer interval separators 钓鱼计时器 间隔分隔线", () => SettingRows.Slider("钓鱼计时器间隔分隔线",
                    "设置计时器显示的间隔分隔线及秒数的数量。设为 0 可关闭。",
                    Config.ShowSecondIntervals, 0, 16, v => Config.ShowSecondIntervals = (byte)v)),
                Custom("Fish timer interval rounding digits 钓鱼计时器 间隔 小数位数", () => SettingRows.Slider("钓鱼计时器间隔取整",
                    "设置间隔秒数保留的小数位数。设为 0 显示整数。",
                    Config.SecondIntervalsRounding, 0, 3, v => Config.SecondIntervalsRounding = v)),
                Toggle("隐藏上钩提示", "不显示捕获鱼的尺寸、数量和品质弹窗。",
                    () => Config.HideFishSizePopup, v => Config.HideFishSizePopup = v),
                Toggle("显示收藏品提示", "在钓鱼计时器中显示鱼是否为收藏品。",
                    () => Config.ShowCollectableHints, v => Config.ShowCollectableHints = v),
                Toggle("显示多重提钩提示", "显示宇宙探索和海钓中的鱼是否可用双重提钩或三重提钩。",
                    () => Config.ShowMultiHookHints, v => Config.ShowMultiHookHints = v)),
            new M3SettingsGroup("fish_stats", "捕鱼统计", FontAwesomeIcon.ChartBar, null,
                Toggle("启用捕鱼统计", "增加一个页面来汇总并报告本地钓鱼记录。此功能正在测试中。",
                    () => Config.EnableFishStats, v => Config.EnableFishStats = v),
                Toggle("复制报告时包含时间统计", "复制报告时附上最短和最长时间。",
                    () => Config.EnableReportTime, v => Config.EnableReportTime = v),
                Toggle("复制报告时包含尺寸统计", "复制报告时附上最小和最大尺寸。",
                    () => Config.EnableReportSize, v => Config.EnableReportSize = v),
                Toggle("复制报告时包含多重提钩统计", "复制报告时附上多重提钩的产量统计。",
                    () => Config.EnableReportMulti, v => Config.EnableReportMulti = v),
                Toggle("启用图表", "查看渔场时将报告数据绘成图表。此功能目前处于实验阶段。",
                    () => Config.EnableFishStatsGraphs, v => Config.EnableFishStatsGraphs = v)),
            new M3SettingsGroup("gather_window", "采集窗口", FontAwesomeIcon.ListUl, "显示已固定物品及其出现时段的小窗口。",
                Toggle("显示采集窗口", "显示一个小窗口，列出已固定的采集物和它们的出现时段。",
                    () => Config.ShowGatherWindow, v => Config.ShowGatherWindow = v),
                Toggle("将采集窗口固定在左下角", "窗口改为从顶部伸缩，而非从底部伸缩。",
                    () => Config.GatherWindowBottomAnchor, v => Config.GatherWindowBottomAnchor = v),
                Toggle("显示采集窗口计时", "在采集窗口中显示物品的出现时段。",
                    () => Config.ShowGatherWindowTimers, v => Config.ShowGatherWindowTimers = v),
                Toggle("在采集窗口中显示已启用的闹钟",
                    "按照采集窗口的原有规则，把已启用的闹钟作为最后一个预设一并显示。",
                    () => Config.ShowGatherWindowAlarms, SetGatherWindowAlarms),
                Toggle("按出现时段排序采集窗口", "按出现时段对采集窗口中的物品排序。",
                    () => Config.SortGatherWindowByUptime, v => Config.SortGatherWindowByUptime = v),
                Toggle("仅显示当前可采集的物品", "在采集窗口中只显示当前可获取的物品。",
                    () => Config.ShowGatherWindowOnlyAvailable, v => Config.ShowGatherWindowOnlyAvailable = v),
                Toggle("隐藏已采齐的物品", "如果背包中已有足够数量，则隐藏该物品。",
                    () => Config.HideGatherWindowCompletedItems, v => Config.HideGatherWindowCompletedItems = v),
                Toggle("在副本中隐藏采集窗口", "处于副本中时隐藏采集窗口。",
                    () => Config.HideGatherWindowInDuty, v => Config.HideGatherWindowInDuty = v),
                Custom("Only show gather window if holding key hotkey to hold 采集窗口 按住 快捷键", () =>
                {
                    SettingRows.Switch("仅在按住按键时显示采集窗口", "采集窗口仅在你按住下方设置的按键时显示。",
                        Config.OnlyShowGatherWindowHoldingKey, v => Config.OnlyShowGatherWindowHoldingKey = v);
                    if (!Config.OnlyShowGatherWindowHoldingKey)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.HoldKey("要按住的按键", "按住此键可显示采集窗口。点击后按键设置；Alt、Ctrl 和 Shift 可单独使用。",
                        Config.GatherWindowHoldKey, Configuration.ValidKeys, v => Config.GatherWindowHoldKey = v);
                }),
                Toggle("锁定采集窗口位置", "禁止拖动采集窗口。",
                    () => Config.LockGatherWindow, v => Config.LockGatherWindow = v),
                Custom("Hotkey to open gather window 采集窗口快捷键", () => SettingRows.Hotkey("采集窗口快捷键",
                    "打开或关闭采集窗口。", Config.GatherWindowHotkey, v => Config.GatherWindowHotkey = v)),
                Custom("Modifier to delete items on right-click 右键删除 修饰键", () => SettingRows.Modifier("右键删除物品的修饰键",
                    "按住此键并右键点击采集窗口中的物品，即可将其移除。",
                    Config.GatherWindowDeleteModifier, v => Config.GatherWindowDeleteModifier = v))),
            new M3SettingsGroup("spearfishing", "刺鱼", FontAwesomeIcon.Water, null,
                Toggle("显示刺鱼助手", "刺鱼时显示刺鱼助手。",
                    () => Config.ShowSpearfishHelper, v => Config.ShowSpearfishHelper = v),
                Toggle("叠加显示鱼名", "在刺鱼窗口中显示已识别鱼类的名称。",
                    () => Config.ShowSpearfishNames, v => Config.ShowSpearfishNames = v),
                Toggle("叠加显示游动速度", "在刺鱼窗口的鱼名旁显示其游动速度。",
                    () => Config.ShowSpearfishSpeed, v => Config.ShowSpearfishSpeed = v),
                Toggle("显示可捕获鱼类列表", "在刺鱼窗口旁列出当前刺鱼点可捕获的鱼。",
                    () => Config.ShowAvailableSpearfish, v => Config.ShowAvailableSpearfish = v),
                Toggle("用文字显示速度和尺寸", "以文字代替图标，显示可捕获鱼类的速度和尺寸。",
                    () => Config.ShowSpearfishListIconsAsText, v => Config.ShowSpearfishListIconsAsText = v),
                Toggle("显示中线", "从鱼叉中心向上绘制一条直线。",
                    () => Config.ShowSpearfishCenterLine, v => Config.ShowSpearfishCenterLine = v),
                Custom("Show fish names in fixed position fish name position percentage 鱼名固定位置 鱼名高度 百分比", () =>
                {
                    SettingRows.Switch("在固定位置显示鱼名",
                        "将已识别鱼类的名称固定在指定高度，不随鱼移动。",
                        Config.FixNamesOnPosition, v => Config.FixNamesOnPosition = v);
                    if (!Config.FixNamesOnPosition)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.Slider("鱼名位置", "设置鱼名在刺鱼窗口中的高度。",
                        Config.FixNamesPercentage, 0, 100, v => Config.FixNamesPercentage = (byte)v);
                })),
            new M3SettingsGroup("colors", "颜色", FontAwesomeIcon.PaintBrush, "采集列表、采集窗口和聊天消息使用的颜色。",
                Custom($"Colors colors {string.Join(' ', Array.ConvertAll(ColorIds, c => c.Data().Name))} 颜色 配色", DrawColors),
                Custom("Names in chat commands in chat arguments in chat alarm message in chat palette 聊天颜色 名称 命令 参数 闹钟 调色板", DrawChatColors)));

    private static void DrawAccentColor()
    {
        var accent = Config.UiAccentColor;
        if (M3Widgets.RowColorChoices("强调色###Accent color", ref accent, MaterialUI.AccentPresets,
                "整套配色方案以该颜色为基准。可选择预设，也可用最后一个色块自定义。"))
        {
            Config.UiAccentColor = accent with { W = 1f };
            Config.Save();
        }
    }

    private static void DrawResetSizes()
    {
        var isDefault = Config.UiTextScale == 1f && Config.UiElementScale == 1f && Config.UiPaddingScale == 1f && Config.UiMotionScale == 1f;
        if (!M3Widgets.Button("##theme_reset_sizes", "重置大小", M3ButtonStyle.Text, FontAwesomeIcon.Undo, enabled: !isDefault))
        {
            return;
        }

        Config.UiTextScale = 1f;
        Config.UiElementScale = 1f;
        Config.UiPaddingScale = 1f;
        Config.UiMotionScale = 1f;
        Config.Save();
    }

    private static void DrawBiteTimeScale()
    {
        var seconds = Config.FishTimerScale / 1000f;
        SettingRows.Number("钓鱼计时器咬钩时间上限",
            "计时器按此时间缩放。超过此时间的咬钩不会显示进度条和咬钩时段。"
          + "建议设置为略长于最长咬钩时段，除此之外尽量调低；通常约 40 秒就够了。",
            seconds, FishRecord.MinBiteTime * 2 / 1000f, FishRecord.MaxBiteTime / 1000f,
            v => Config.FishTimerScale = (ushort)Math.Clamp((int)(v * 1000f + 0.9f), FishRecord.MinBiteTime * 2, FishRecord.MaxBiteTime),
            0.5f, "%.1f", "s");
    }

    private void SetGatherWindowAlarms(bool enabled)
    {
        Config.ShowGatherWindowAlarms = enabled;
        _plugin.GatherWindowManager.SetShowGatherWindowAlarms(enabled);
    }

    private static readonly ColorId[] ColorIds = Enum.GetValues<ColorId>();

    private static readonly string[] ColorLabels = Array.ConvertAll(ColorIds, c => $"{c.Data().Name}##color_{c}");

    private static void DrawColors()
    {
        for (var i = 0; i < ColorIds.Length; ++i)
        {
            var color = ColorIds[i];
            var (defaultColor, _, description) = color.Data();
            var current = Config.Colors.TryGetValue(color, out var value) ? value : defaultColor;
            SettingRows.Color(ColorLabels[i], description, current, defaultColor, c => Config.Colors[color] = c);
        }
    }

    private static void DrawChatColors()
    {
        M3Widgets.SectionLabel("聊天颜色");
        DrawPaletteColor("聊天中的名称", Config.SeColorNames, Configuration.DefaultSeColorNames, i => Config.SeColorNames = i);
        DrawPaletteColor("聊天中的命令", Config.SeColorCommands, Configuration.DefaultSeColorCommands, i => Config.SeColorCommands = i);
        DrawPaletteColor("聊天中的参数", Config.SeColorArguments, Configuration.DefaultSeColorArguments, i => Config.SeColorArguments = i);
        DrawPaletteColor("聊天中的闹钟消息", Config.SeColorAlarm, Configuration.DefaultSeColorAlarm, i => Config.SeColorAlarm = i);
    }

    private static void DrawPaletteColor(string label, int current, int defaultIndex, Action<int> setter)
    {
        var colors = Configuration.ForegroundColors;
        var scale = M3.Scale;
        var swatch = M3Widgets.ColorSwatchSize;
        var resetSize = 32f * scale;
        var canReset = current != defaultIndex && colors.ContainsKey(defaultIndex);
        var controlSize = new Vector2(swatch + (canReset ? M3.Space1 + resetSize : 0f), MathF.Max(swatch, resetSize));

        var row = M3SettingRow.Begin(label, "点击色块选择颜色，右键点击可清除。", controlSize);
        var origin = row.ControlPosition;
        var popupId = $"##palette_{label}";

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, (controlSize.Y - swatch) * 0.5f));
        var hasColor = colors.TryGetValue(current, out var packed);
        if (ImGui.InvisibleButton($"##swatch_{label}", new Vector2(swatch)))
        {
            ImGui.OpenPopup(popupId);
        }

        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var s = M3.Scheme;
        if (hasColor)
        {
            drawList.AddRectFilled(min, max, packed, M3.ShapeSmall);
        }
        else
        {
            drawList.AddRectFilled(min, max, M3.U32(s.SurfaceContainerHighest), M3.ShapeSmall);
            drawList.AddLine(new Vector2(min.X + 4f * scale, max.Y - 4f * scale), new Vector2(max.X - 4f * scale, min.Y + 4f * scale),
                M3.U32(s.Error, 0.8f), 1.5f * scale);
        }

        drawList.AddRect(min, max, M3.U32(hovered ? s.OnSurface : s.Outline, hovered ? 0.9f : 0.6f), M3.ShapeSmall, ImDrawFlags.None, 1f * scale);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            M3Tooltip.Show(hasColor ? $"颜色 {current}" : "无颜色");
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && current != -1)
        {
            setter(-1);
            Config.Save();
        }

        if (canReset)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(swatch + M3.Space1, (controlSize.Y - resetSize) * 0.5f));
            if (M3Widgets.IconButton($"##palette_reset_{label}", FontAwesomeIcon.Undo, "恢复默认颜色", diameter: resetSize))
            {
                setter(defaultIndex);
                Config.Save();
            }
        }

        M3SettingRow.End(row);
        DrawPalettePopup(popupId, colors, current, setter);
    }

    private static void DrawPalettePopup(string popupId, IDictionary<int, uint> colors, int current, Action<int> setter)
    {
        if (!ImGui.BeginPopup(popupId))
        {
            return;
        }

        var scale = M3.Scale;
        var box = 22f * scale;
        var gap = 4f * scale;
        const int perLine = 10;
        var counter = 0;
        foreach (var (index, value) in colors)
        {
            if (counter++ % perLine != 0)
            {
                ImGui.SameLine(0f, gap);
            }

            if (ImGui.InvisibleButton($"##palette_entry_{index}", new Vector2(box)))
            {
                setter(index);
                Config.Save();
                ImGui.CloseCurrentPopup();
            }

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(min, max, value, M3.ShapeExtraSmall);
            if (index == current)
            {
                drawList.AddRect(min - new Vector2(2f * scale), max + new Vector2(2f * scale), M3.U32(M3.Scheme.Primary), M3.ShapeExtraSmall,
                    ImDrawFlags.None, 2f * scale);
            }

            if (ImGui.IsItemHovered())
            {
                M3Tooltip.Show($"颜色 {index}");
            }
        }

        ImGui.EndPopup();
    }
}
