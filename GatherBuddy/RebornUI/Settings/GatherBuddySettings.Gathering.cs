using Dalamud.Interface;
using GatherBuddy.Alarms;
using GatherBuddy.Config;
using GatherBuddy.Gui;
using GatherBuddy.RebornUI.Settings;
using RebornMaterial;
using System;
using GatheringType = GatherBuddy.Enums.GatheringType;

namespace GatherBuddy.RebornUI;

internal sealed partial class GatherBuddySettings
{
    private const string ChatNote = "消息只会出现在你自己的聊天记录里，无论选择哪个频道，其他人都看不到。";

    private static readonly string[] PreferredJobLabels = ["无偏好", "采矿工", "园艺工"];
    private static readonly GatheringType[] PreferredJobs = [GatheringType.Multiple, GatheringType.Miner, GatheringType.Botanist];

    private static readonly M3Segment[] AetheryteSegments =
    [
        new("优先省时", FontAwesomeIcon.Route, "优先选择离目标更近的以太之光。"),
        new("优先省钱", FontAwesomeIcon.Coins, "优先选择传送费用较低的以太之光。"),
    ];

    private M3SettingsPage BuildGatheringPage()
        => new(GatheringPageId, "采集设置", FontAwesomeIcon.MapMarkedAlt, "/gather 命令、职业套装、闹钟和聊天消息",
            new M3SettingsGroup("gather_command", "采集命令", FontAwesomeIcon.Terminal, "对物品使用 /gather 时执行的操作。",
                Custom("Preferred job no preference miner botanist 首选职业 无偏好 采矿工 园艺工", DrawPreferredJob),
                Toggle("启用套装切换",
                    "根据采集点自动切换到正确的职业，使用下面设置的采矿工、园艺工和捕鱼人套装。",
                    () => Config.UseGearChange, v => Config.UseGearChange = v),
                Toggle("启用传送", "自动传送至选中的采集点附近。",
                    () => Config.UseTeleport, v => Config.UseTeleport = v),
                Toggle("打开地图并显示位置",
                    "自动打开采集点所在区域的地图，并标出采集位置。",
                    () => Config.UseCoordinates, v => Config.UseCoordinates = v),
                Toggle("在地图上放置旗标",
                    "不打开地图，直接在所选采集点的大致位置放置红色旗标。",
                    () => Config.UseFlag, v => Config.UseFlag = v),
                Toggle("放置自定义场景标记", "放置你为特定地点设置的自定义场景标记。",
                    () => Config.PlaceCustomWaymarks, v => Config.PlaceCustomWaymarks = v),
                Custom("Prefer cheaper aetherytes prefer less travel time aetheryte preference 以太之光偏好 省钱 省时", DrawAetherytePreference),
                Toggle("跳过近距离传送",
                    "如果你在同一张地图，且比所选以太之光更靠近目标，则跳过传送。",
                    () => Config.SkipTeleportIfClose, v => Config.SkipTeleportIfClose = v),
                Toggle("添加游戏右键菜单", "为可采集物品的游戏右键菜单添加「采集」选项。",
                    () => Config.AddIngameContextMenus, SetContextMenus, "right-click")),
            new M3SettingsGroup("set_names", "职业套装", FontAwesomeIcon.Tshirt, "用于套装切换。填写各职业套装的名称或编号。",
                Custom("Miner set name 采矿工套装名称", () => SettingRows.Text("采矿工套装", "采矿工套装的名称或编号。",
                    Config.MinerSetName, v => Config.MinerSetName = v, "Miner", 15)),
                Custom("Botanist set name 园艺工套装名称", () => SettingRows.Text("园艺工套装", "园艺工套装的名称或编号。",
                    Config.BotanistSetName, v => Config.BotanistSetName = v, "Botanist", 15)),
                Custom("Fisher set name 捕鱼人套装名称", () => SettingRows.Text("捕鱼人套装", "捕鱼人套装的名称或编号。",
                    Config.FisherSetName, v => Config.FisherSetName = v, "Fisher", 15))),
            new M3SettingsGroup("alarms", "闹钟", FontAwesomeIcon.Bell, null,
                Toggle("启用闹钟", "开启或关闭所有闹钟。", () => Config.AlarmsEnabled, SetAlarmsEnabled),
                Toggle("在副本中启用闹钟", "允许闹钟在副本中触发。",
                    () => Config.AlarmsInDuty, v => Config.AlarmsInDuty = v),
                Toggle("仅在游戏中启用闹钟", "仅在已登录角色时触发闹钟。",
                    () => Config.AlarmsOnlyWhenLoggedIn, v => Config.AlarmsOnlyWhenLoggedIn = v),
                Custom("Weather change alarm sound 天气变化闹钟音效", () => DrawAlarmSound("天气变化闹钟",
                    "每 8 个艾欧泽亚小时，也就是天气正常变化时播放的音效。", Config.WeatherAlarm, AlarmManager.SetWeatherAlarm)),
                Custom("Eorzea hour change alarm sound 艾欧泽亚小时变化闹钟音效", () => DrawAlarmSound("艾欧泽亚小时变化闹钟",
                    "每当艾欧泽亚小时变化时播放的音效。", Config.HourAlarm, AlarmManager.SetHourAlarm))),
            new M3SettingsGroup("messages", "聊天消息", FontAwesomeIcon.CommentDots, null,
                Custom("Chat type for messages channel 常规消息聊天频道", () => SettingRows.Combo("常规消息聊天频道",
                    $"GatherBuddy 发送常规消息时使用的聊天类型。{ChatNote}", Config.ChatTypeMessage, v => Config.ChatTypeMessage = v,
                    search: true)),
                Custom("Chat type for errors channel 错误消息聊天频道", () => SettingRows.Combo("错误消息聊天频道",
                    $"GatherBuddy 发送错误消息时使用的聊天类型。{ChatNote}", Config.ChatTypeError, v => Config.ChatTypeError = v,
                    search: true)),
                Toggle("输出地图位置", "在聊天中输出选中采集点大致位置的地图链接。",
                    () => Config.WriteCoordinates, v => Config.WriteCoordinates = v),
                Toggle("采集时输出采集点出现时段", "使用 /gather 前往有时间限制的采集点时，在聊天中输出其出现时段。",
                    () => Config.PrintUptime, v => Config.PrintUptime = v),
                Toggle("输出剪贴板消息",
                    "复制内容到剪贴板时在聊天中提示。失败时始终提示。",
                    () => Config.PrintClipboardMessages, v => Config.PrintClipboardMessages = v),
                Custom("Alarm chat format Alarm Item Offset DurationString Location 闹钟聊天消息格式", () => SettingRows.FormatText("闹钟聊天消息格式",
                    "留空则不发送聊天消息。{Alarm} 为括号中的闹钟名称，{Item} 为物品链接，{Offset} 为闹钟的偏移秒数，"
                  + "{DurationString} 表示持续时间，{Location} 为地图旗标链接和地点名称。",
                    Config.AlarmFormat, Configuration.DefaultAlarmFormat, v => Config.AlarmFormat = v)),
                Custom("Identified gatherable chat format Input Item 已识别采集物聊天消息格式", () => SettingRows.FormatText("已识别采集物聊天消息格式",
                    "留空则不发送聊天消息。{Input} 为搜索文字，{Item} 为物品链接。",
                    Config.IdentifiedGatherableFormat, Configuration.DefaultIdentifiedGatherableFormat, v => Config.IdentifiedGatherableFormat = v))));

    private static void DrawPreferredJob()
    {
        var current = Config.PreferredGatheringType;
        var index = Math.Max(0, Array.IndexOf(PreferredJobs, current));
        SettingRows.Combo("首选职业",
            "对于采矿工和园艺工都能采集的物品，将 /gather 转为 /gathermin 或 /gatherbtn；即使重试也只考虑选定的职业。",
            index, PreferredJobLabels, i => Config.PreferredGatheringType = PreferredJobs[i]);
    }

    private static void DrawAetherytePreference()
    {
        var index = Config.AetherytePreference == AetherytePreference.Cost ? 1 : 0;
        if (!M3Widgets.RowSegmented("以太之光偏好###Aetheryte preference", ref index, AetheryteSegments,
                "当物品不限时且有多个采集来源时，优先选择距目标较近或传送费用较低的以太之光。"))
        {
            return;
        }

        var preference = index == 1 ? AetherytePreference.Cost : AetherytePreference.Distance;
        if (preference == Config.AetherytePreference)
        {
            return;
        }

        Config.AetherytePreference = preference;
        GatherBuddy.UptimeManager.ResetLocations();
        Config.Save();
    }

    private void SetContextMenus(bool enabled)
    {
        Config.AddIngameContextMenus = enabled;
        if (enabled)
        {
            _plugin.ContextMenu.Enable();
        }
        else
        {
            _plugin.ContextMenu.Disable();
        }
    }

    private void SetAlarmsEnabled(bool enabled)
    {
        if (enabled)
        {
            _plugin.AlarmManager.Enable();
        }
        else
        {
            _plugin.AlarmManager.Disable();
        }
    }

    private static void DrawAlarmSound(string label, string supporting, Sounds current, Action<Sounds> setter)
    {
        var sounds = Interface.AlarmSoundIds;
        var index = Array.IndexOf(sounds, current);
        SettingRows.Combo(label, supporting, index, Interface.AlarmSoundNames, i => setter(sounds[i]));
    }
}
