using System;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.STD;
using GatherBuddy.Alarms;
using GatherBuddy.AutoGather;
using GatherBuddy.Config;
using ImGuiNET;
using OtterGui;
using OtterGui.Widgets;
using FishRecord = GatherBuddy.FishTimer.FishRecord;
using GatheringType = GatherBuddy.Enums.GatheringType;
using ImRaii = OtterGui.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private static class ConfigFunctions
    {
        public static Interface _base = null!;

        public static void DrawSetInput(string jobName, string oldName, Action<string> setName)
        {
            var tmp = oldName;
            ImGui.SetNextItemWidth(SetInputWidth);
            if (ImGui.InputText($"{jobName} 套装", ref tmp, 15) && tmp != oldName)
            {
                setName(tmp);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip($"设置你的 {jobName.ToLowerInvariant()} 套装的名称或编号。");
        }

        private static void DrawCheckbox(string label, string description, bool oldValue, Action<bool> setter)
        {
            if (ImGuiUtil.Checkbox(label, description, oldValue, setter))
                GatherBuddy.Config.Save();
        }

        private static void DrawChatTypeSelector(string label, string description, XivChatType currentValue, Action<XivChatType> setter)
        {
            ImGui.SetNextItemWidth(SetInputWidth);
            if (Widget.DrawChatTypeSelector(label, description, currentValue, setter))
                GatherBuddy.Config.Save();
        }

        // Auto-Gather Config
        public static void DrawAutoGatherBox()
            => DrawCheckbox("启用聚集窗口互动（不推荐禁用此功能）",
                "是否启用自动采集物品。（“仅寻路模式”中建议禁用)",
                GatherBuddy.Config.AutoGatherConfig.DoGathering, b => GatherBuddy.Config.AutoGatherConfig.DoGathering = b);

        public static void DrawGoHomeBox()
        {
            DrawCheckbox("采集完成时回家", "当采集完成时, 自动使用 '/li auto' 指令回家",
                        GatherBuddy.Config.AutoGatherConfig.GoHomeWhenDone, b => GatherBuddy.Config.AutoGatherConfig.GoHomeWhenDone = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("Lifestream")]);
            DrawCheckbox("空闲时回家", "当等待下一限时采集点出现时, 自动使用 '/li auto' 指令回家",
                        GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle, b => GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("Lifestream")]);
        }

        public static void DrawUseSkillsForFallabckBox()
            => DrawCheckbox("为备选采集列表物品使用技能", 
                "当采集备选采集列表内的物品时, 自动使用相关的采集技能",
                GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems, b => GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems = b);

        public static void DrawAbandonNodesBox()
            => DrawCheckbox("自动取消采集点",
                "当采集点没有目标物品或你已经采集了足够的目标物品, 且采集点已经无其他需要采集的物品时, 自动取消采集该采集点",
                GatherBuddy.Config.AutoGatherConfig.AbandonNodes, 
                b => GatherBuddy.Config.AutoGatherConfig.AbandonNodes = b);

        public static void DrawCheckRetainersBox()
        {
            DrawCheckbox("检查雇员物品", "使用Allagan Tools检查雇员物品以进行库存计算",
                GatherBuddy.Config.AutoGatherConfig.CheckRetainers, b => GatherBuddy.Config.AutoGatherConfig.CheckRetainers = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("InventoryTools", "Allagan Tools")]);
        }

        public static void DrawHonkVolumeSlider()
        {
            ImGui.SetNextItemWidth(150);
            var volume = GatherBuddy.Config.AutoGatherConfig.SoundPlaybackVolume;
            if (ImGui.DragInt("播放音量", ref volume, 1, 0, 100))
            {
                if (volume < 0)
                    volume = 0;
                else if (volume > 100)
                    volume = 100;
                GatherBuddy.Config.AutoGatherConfig.SoundPlaybackVolume = volume;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "完成列表采集后，自动采集停止时播放的声音音量。\n按住CTRL键并单击可输入自定义值");
        }

        public static void DrawHonkModeBox()
            => DrawCheckbox("采集完成时播放音效", "完成列表采集后结束自动采集并播放音效",
                GatherBuddy.Config.AutoGatherConfig.HonkMode,   b => GatherBuddy.Config.AutoGatherConfig.HonkMode = b);

        public static void DrawRepairBox()
            => DrawCheckbox("需要时修理装备",        "装备即将损坏时进行修理",
                GatherBuddy.Config.AutoGatherConfig.DoRepair, b => GatherBuddy.Config.AutoGatherConfig.DoRepair = b);

        public static void DrawRepairThreshold()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.RepairThreshold;
            if (ImGui.DragInt("修理阈值", ref tmp, 1, 1, 100))
            {
                GatherBuddy.Config.AutoGatherConfig.RepairThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("进行装备修理的耐久度百分比。");
        }

        public static void DrawFishingSpotMinutes()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes;
            if (ImGui.DragInt("钓鱼点最长停留时间（分钟）", ref tmp, 1, 1, 40))
            {
                GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("在同一个钓鱼点进行钓鱼的最长时间（分钟）。");
        }

        public static void DrawLifestreamCommandTextInput()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.LifestreamCommand;
            if (ImGui.InputText("Lifestream 命令", ref tmp, 100))
            {
                if (string.IsNullOrEmpty(tmp))
                    tmp = "auto";
                GatherBuddy.Config.AutoGatherConfig.LifestreamCommand = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "在空闲或完成采集时使用的命令。请勿包含“/li”\n谨慎更改，因为GBR不会验证此命令！");
        }

        public static void DrawFishCollectionBox()
            => DrawCheckbox("允许捕鱼数据收集",
                "启用此选项后，每当您钓到鱼时，该鱼的数据将被上传到远程服务器。\n"
              + "此数据收集的目的是为了能够构建自动钓鱼功能。\n"
              + "不会收集有关您或您角色的个人信息，仅收集与所获得鱼的相关的资料。\n"
              + "您可以随时通过禁用此复选框来禁用信息收集。", GatherBuddy.Config.AutoGatherConfig.FishDataCollection,
                b => GatherBuddy.Config.AutoGatherConfig.FishDataCollection = b);

        public static void DrawMaterialExtraction()
            => DrawCheckbox("自动精制",
                "你需要安装 Daily Routines 并开启 \"自动精制魔晶石\" 模块",
                GatherBuddy.Config.AutoGatherConfig.DoMaterialize,
                b => GatherBuddy.Config.AutoGatherConfig.DoMaterialize = b);

        public static void DrawAetherialReduction()
            => DrawCheckbox("自动精选",
                "在空闲或物品栏已满时自动进行精选",
                GatherBuddy.Config.AutoGatherConfig.DoReduce,
                b => GatherBuddy.Config.AutoGatherConfig.DoReduce = b);

        public static void DrawUseFlagBox()
            => DrawCheckbox("禁用地图标记导航",            "是否使用地图标记进行导航（仅限时采集点）",
                GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing, b => GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing = b);

        public static void DrawFarNodeFilterDistance()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance;
            if (ImGui.DragFloat("过远采集点距离过滤", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "在寻找非空采集点时，GBR将过滤掉任何比这更靠近你的采集点。防止检查已经看到为空的采集点。");
        }

        public static void DrawTimedNodePrecog()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog;
            if (ImGui.DragInt("限时采集点预知（秒）", ref tmp, 1, 0, 600))
            {
                GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("在限时采集点出现前，GBR应该提前多久认为限时采集点出现");
        }

        public static void DrawExecutionDelay()
        {
            var tmp = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
            if (ImGui.DragInt("执行延迟 (毫秒)", ref tmp, 1, 0, 1500))
            {
                GatherBuddy.Config.AutoGatherConfig.ExecutionDelay = (uint)Math.Min(Math.Max(0, tmp), 10000);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("使用任一技能前需要等待的时间");
        }

        public static void DrawUseGivingLandOnCooldown()
            => DrawCheckbox("当 大地的恩惠 冷却完成时, 优先采集任意水晶",
                "当大地的恩惠可用时，在任意普通采集点上随机采集水晶，不管当前的目标物品是什么",
                GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown,
                b => GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown = b);

        public static void DrawMountUpDistance()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.MountUpDistance;
            if (ImGui.DragFloat("需要上坐骑的距离", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.MountUpDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("你将会上坐骑来移动到下一个采集点的距离。");
        }

        public static void DrawMoveWhileMounting()
            => DrawCheckbox("召唤坐骑时便开始寻路",
                "当读条召唤坐骑时便直接开始寻路",
                GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting,
                b => GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting = b);

        public static void DrawAntiStuckCooldown()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetCooldown;
            if (ImGui.DragFloat("防卡冷却", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetCooldown = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("你卡死之后多少秒导航系统将会重置。");
        }

        public static void DrawForceWalkingBox()
            => DrawCheckbox("强制行走",                      "强制以行走替代骑乘前往采集点。",
                GatherBuddy.Config.AutoGatherConfig.ForceWalking, b => GatherBuddy.Config.AutoGatherConfig.ForceWalking = b);

        public static void DrawUseNavigationBox()
            => DrawCheckbox("使用vnavmesh导航",             "Use vnavmesh Navigation to move your character automatically",
                GatherBuddy.Config.AutoGatherConfig.UseNavigation, b => GatherBuddy.Config.AutoGatherConfig.UseNavigation = b);

        public static void DrawStuckThreshold()
        {
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetThreshold;
            if (ImGui.DragFloat("卡死阈值", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("多少秒之后导航系统将会判定你是否卡死。");
        }

        public static void DrawSortingMethodCombo()
        {
            var v = GatherBuddy.Config.AutoGatherConfig.SortingMethod;
            ImGui.SetNextItemWidth(SetInputWidth);

            using var combo = ImRaii.Combo("物品排序方法", v.ToString());
            ImGuiUtil.HoverTooltip("内部排序物品时所使用的方法");
            if (!combo)
                return;

            if (ImGui.Selectable(AutoGatherConfig.SortingType.Location.ToString(), v == AutoGatherConfig.SortingType.Location))
            {
                GatherBuddy.Config.AutoGatherConfig.SortingMethod = AutoGatherConfig.SortingType.Location;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(AutoGatherConfig.SortingType.None.ToString(), v == AutoGatherConfig.SortingType.None))
            {
                GatherBuddy.Config.AutoGatherConfig.SortingMethod = AutoGatherConfig.SortingType.None;
                GatherBuddy.Config.Save();
            }
        }

        // General Config
        public static void DrawOpenOnStartBox()
            => DrawCheckbox("启动时打开设置界面",
                "切换在启动游戏后 GatherBuddy 界面是否可见。",
                GatherBuddy.Config.OpenOnStart, b => GatherBuddy.Config.OpenOnStart = b);

        public static void DrawLockPositionBox()
            => DrawCheckbox("锁定设置界面移动",
                "切换 GatherBuddy 界面移动是否被锁定。",
                GatherBuddy.Config.MainWindowLockPosition, b =>
                {
                    GatherBuddy.Config.MainWindowLockPosition = b;
                    _base.UpdateFlags();
                });

        public static void DrawLockResizeBox()
            => DrawCheckbox("锁定设置界面尺寸",
                "切换 GatherBuddy 界面尺寸是否被锁定。",
                GatherBuddy.Config.MainWindowLockResize, b =>
                {
                    GatherBuddy.Config.MainWindowLockResize = b;
                    _base.UpdateFlags();
                });

        public static void DrawRespectEscapeBox()
            => DrawCheckbox("ESC 关闭主界面",
                "切换聚焦主窗口时按 ESC 键是否关闭主窗口。",
                GatherBuddy.Config.CloseOnEscape, b =>
                {
                    GatherBuddy.Config.CloseOnEscape = b;
                    _base.UpdateFlags();
                });

        public static void DrawGearChangeBox()
            => DrawCheckbox("启用套装切换",
                "切换是否根据采集点自动切换到正确的职业套装。\n使用采矿工套装，园艺工套装以及捕鱼人套装。",
                GatherBuddy.Config.UseGearChange, b => GatherBuddy.Config.UseGearChange = b);

        public static void DrawTeleportBox()
            => DrawCheckbox("启用传送",
                "切换是否自动传送到所选的采集点。",
                GatherBuddy.Config.UseTeleport, b => GatherBuddy.Config.UseTeleport = b);

        public static void DrawMapOpenBox()
            => DrawCheckbox("打开带位置的地图",
                "切换是否自动打开所选的采集点所在地区的地图且高亮其采集位置。",
                GatherBuddy.Config.UseCoordinates, b => GatherBuddy.Config.UseCoordinates = b);

        public static void DrawPlaceMarkerBox()
            => DrawCheckbox("在地图上放置旗帜标记",
                "切换是否自动在所选采集点的近似位置放一个红色小旗标记且不打开地图。",
                GatherBuddy.Config.UseFlag, b => GatherBuddy.Config.UseFlag = b);

        public static void DrawMapMarkerPrintBox()
            => DrawCheckbox("打印地图位置",
                "切换是否自动在聊天栏中发送一个所选采集点近似位置的地图链接。",
                GatherBuddy.Config.WriteCoordinates, b => GatherBuddy.Config.WriteCoordinates = b);

        public static void DrawPlaceWaymarkBox()
            => DrawCheckbox("放置自定义场景标记",
                "切换是否放置你为确定位置手动设置的场景标记。",
                GatherBuddy.Config.PlaceCustomWaymarks, b => GatherBuddy.Config.PlaceCustomWaymarks = b);

        public static void DrawPrintUptimesBox()
            => DrawCheckbox("采集时打印采集点出现时间",
                "打印你尝试 /gather 的限时采集点的出现时间到聊天栏。",
                GatherBuddy.Config.PrintUptime, b => GatherBuddy.Config.PrintUptime = b);

        public static void DrawSkipTeleportBox()
            => DrawCheckbox("跳过附近传送",
                "如果你已经在同一地图且比所选的以太之光更靠近目标，跳过传送。",
                GatherBuddy.Config.SkipTeleportIfClose, b => GatherBuddy.Config.SkipTeleportIfClose = b);

        public static void DrawShowStatusLineBox()
            => DrawCheckbox("显示状态线",
                "在可采集物和鱼类表下面显示一条状态线。",
                GatherBuddy.Config.ShowStatusLine, v => GatherBuddy.Config.ShowStatusLine = v);

        public static void DrawHideClippyBox()
            => DrawCheckbox("隐藏采集助手按钮",
                "永远隐藏可采集物和鱼类标签页中的采集助手按钮。",
                GatherBuddy.Config.HideClippy, v => GatherBuddy.Config.HideClippy = v);

        private const string ChatInformationString =
            "注意，无论选择的频道是什么，消息只会被打印到你的聊天日志中"
          + " - 其他人不会看到你“说”的话。";

        public static void DrawPrintTypeSelector()
            => DrawChatTypeSelector("消息的聊天类型",
                "用于打印由 GatherBuddy 发出的常规消息的聊天类型。\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeMessage, t => GatherBuddy.Config.ChatTypeMessage = t);

        public static void DrawErrorTypeSelector()
            => DrawChatTypeSelector("错误的聊天类型",
                "用于打印由 GatherBuddy 发出的错误消息的聊天类型。\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeError, t => GatherBuddy.Config.ChatTypeError = t);

        public static void DrawContextMenuBox()
            => DrawCheckbox("添加游戏内上下文菜单",
                "为可采集物的游戏内右键菜单添加“采集”条目。",
                GatherBuddy.Config.AddIngameContextMenus, b =>
                {
                    GatherBuddy.Config.AddIngameContextMenus = b;
                    if (b)
                        _plugin.ContextMenu.Enable();
                    else
                        _plugin.ContextMenu.Disable();
                });

        public static void DrawPreferredJobSelect()
        {
            var v       = GatherBuddy.Config.PreferredGatheringType;
            var current = v == GatheringType.多职业 ? "无偏好" : v.ToString();
            ImGui.SetNextItemWidth(SetInputWidth);
            using var combo = ImRaii.Combo("偏好职业", current);
            ImGuiUtil.HoverTooltip(
                "在采集采矿工和园艺工都可以采集物品时，选择你的职业偏好。\n"
              + "当两者都可以采集物品时，这实际上将通常的采集命令转换为 /gathermin 或 /gatherbtn，"
              + "即便连续尝试，也忽略其他选项。");
            if (!combo)
                return;

            if (ImGui.Selectable("无偏好", v == GatheringType.多职业) && v != GatheringType.多职业)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.多职业;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(GatheringType.采矿工.ToString(), v == GatheringType.采矿工) && v != GatheringType.采矿工)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.采矿工;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(GatheringType.园艺工.ToString(), v == GatheringType.园艺工) && v != GatheringType.园艺工)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.园艺工;
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawPrintClipboardBox()
            => DrawCheckbox("打印剪贴板信息",
                "当你保存一个对象到剪贴板时，不论是否可行都尝试打印到聊天栏中。",
                GatherBuddy.Config.PrintClipboardMessages, b => GatherBuddy.Config.PrintClipboardMessages = b);

        // Weather Tab
        public static void DrawWeatherTabNamesBox()
            => DrawCheckbox("在天气标签页显示名称",
                "切换是否在天气标签页的表格中显示名称，或者仅仅显示在鼠标悬浮时会显示名字的图标。",
                GatherBuddy.Config.ShowWeatherNames, b => GatherBuddy.Config.ShowWeatherNames = b);

        // Alarms
        public static void DrawAlarmToggle()
            => DrawCheckbox("启用闹钟", "切换所有闹钟开关", GatherBuddy.Config.AlarmsEnabled,
                b =>
                {
                    if (b)
                        _plugin.AlarmManager.Enable();
                    else
                        _plugin.AlarmManager.Disable();
                });

        private static bool _gatherDebug = false;

        public static void DrawAlarmsInDutyToggle()
            => DrawCheckbox("在副本任务中启用闹钟", "设置当你在副本任务中时闹钟是否应该触发。",
                GatherBuddy.Config.AlarmsInDuty,     b => GatherBuddy.Config.AlarmsInDuty = b);

        public static void DrawAlarmsOnlyWhenLoggedInToggle()
            => DrawCheckbox("仅在游戏内启用闹钟",  "设置当你未登入任何角色时闹钟是否应该触发。",
                GatherBuddy.Config.AlarmsOnlyWhenLoggedIn, b => GatherBuddy.Config.AlarmsOnlyWhenLoggedIn = b);

        private static void DrawAlarmPicker(string label, string description, Sounds current, Action<Sounds> setter)
        {
            var cur = (int)current;
            ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo(label, ref cur, AlarmCache.SoundIdNames))
                setter((Sounds)cur);
            ImGuiUtil.HoverTooltip(description);
        }

        public static void DrawWeatherAlarmPicker()
            => DrawAlarmPicker("天气改变闹钟提醒", "选择一个在每8个艾欧泽亚时的正常天气改变的时候播放的声音。",
                GatherBuddy.Config.WeatherAlarm,       _plugin.AlarmManager.SetWeatherAlarm);

        public static void DrawHourAlarmPicker()
            => DrawAlarmPicker("艾欧泽亚时改变闹钟提醒", "选择一个在当前艾欧泽亚时改变的时候播放的声音。",
                GatherBuddy.Config.HourAlarm,              _plugin.AlarmManager.SetHourAlarm);

        // Fish Timer
        public static void DrawFishTimerBox()
            => DrawCheckbox("显示捕鱼计时器",
                "切换是否在钓鱼时显示捕鱼计时器。",
                GatherBuddy.Config.ShowFishTimer, b => GatherBuddy.Config.ShowFishTimer = b);

        public static void DrawFishTimerEditBox()
            => DrawCheckbox("编辑捕鱼计时器",
                "启用编辑捕鱼计时器窗口。",
                GatherBuddy.Config.FishTimerEdit, b => GatherBuddy.Config.FishTimerEdit = b);

        public static void DrawFishTimerClickthroughBox()
            => DrawCheckbox("捕鱼计时器鼠标穿透",
                "允许鼠标穿透捕鱼计时器，并禁用其上下文菜单。",
                GatherBuddy.Config.FishTimerClickthrough, b => GatherBuddy.Config.FishTimerClickthrough = b);

        public static void DrawFishTimerHideBox()
            => DrawCheckbox("在捕鱼计时器中隐藏未捕获的鱼",
                "在捕鱼计时器窗口中隐藏所有未被给定的钓组和钓饵的组合记录的鱼。",
                GatherBuddy.Config.HideUncaughtFish, b => GatherBuddy.Config.HideUncaughtFish = b);

        public static void DrawFishTimerHideBox2()
            => DrawCheckbox("在捕鱼计时器中隐藏不可捕获的鱼",
                "在捕鱼计时器窗口中隐藏所有前置条件未满足的鱼，例如 捕鱼人之识 或 钓组。",
                GatherBuddy.Config.HideUnavailableFish, b => GatherBuddy.Config.HideUnavailableFish = b);

        public static void DrawFishTimerUptimesBox()
            => DrawCheckbox("在捕鱼计时器中显示出现时间",
                "在捕鱼计时器窗口中显示限时鱼的出现时间。",
                GatherBuddy.Config.ShowFishTimerUptimes, b => GatherBuddy.Config.ShowFishTimerUptimes = b);

        public static void DrawKeepRecordsBox()
            => DrawCheckbox("保持捕鱼记录",
                "在你的计算机上存储捕鱼记录。这对于捕鱼计时器窗口的咬钩计时是必要的。",
                GatherBuddy.Config.StoreFishRecords, b => GatherBuddy.Config.StoreFishRecords = b);

        public static void DrawFishTimerScale()
        {
            var value = GatherBuddy.Config.FishTimerScale / 1000f;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragFloat("捕鱼计时器咬钩计时尺寸", ref value, 0.1f, FishRecord.MinBiteTime / 500f,
                FishRecord.MaxBiteTime / 1000f,
                "%2.3f 秒");

            ImGuiUtil.HoverTooltip("捕鱼计时器窗口咬钩计时的尺寸取决于这个值。\n"
              + "如果你的咬钩时间超过该值，进度条和咬钩窗口将不显示。\n"
              + "你应该把这个值保持在你的最高咬钩窗口，且尽可能低。通常来说40秒够了。");

            if (!ret)
                return;

            var newValue = (ushort)Math.Clamp((int)(value * 1000f + 0.9), FishRecord.MinBiteTime * 2, FishRecord.MaxBiteTime);
            if (newValue == GatherBuddy.Config.FishTimerScale)
                return;

            GatherBuddy.Config.FishTimerScale = newValue;
            GatherBuddy.Config.Save();
        }

        public static void DrawFishTimerIntervals()
        {
            int value = GatherBuddy.Config.ShowSecondIntervals;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragInt("捕鱼计时器间隔分隔符", ref value, 0.01f, 0, 16);
            ImGuiUtil.HoverTooltip("捕鱼计时器窗口可以显示若干间隔线和0到16之间的相应的秒数。\n"
              + "设置为0关闭此功能。");
            if (!ret)
                return;

            var newValue = (byte)Math.Clamp(value, 0, 16);
            if (newValue == GatherBuddy.Config.ShowSecondIntervals)
                return;

            GatherBuddy.Config.ShowSecondIntervals = newValue;
            GatherBuddy.Config.Save();
        }

        public static void DrawHideFishPopupBox()
            => DrawCheckbox("隐藏捕获弹窗",
                "阻止弹出显示你捕获的鱼以及它的大小、数量和质量的弹窗。",
                GatherBuddy.Config.HideFishSizePopup, b => GatherBuddy.Config.HideFishSizePopup = b);

        public static void DrawCollectableHintPopupBox()
            => DrawCheckbox("收藏品提示",
                "在捕鱼计时器窗口中显示鱼是否为收藏品。",
                GatherBuddy.Config.ShowCollectableHints, b => GatherBuddy.Config.ShowCollectableHints = b);

        public static void DrawDoubleHookHintPopupBox()
            => DrawCheckbox("多重提钩提示",
                "在“宇宙探索”中显示鱼是否可以进行双重或三重提钩。", // TODO: add ocean fishing when implemented.
                GatherBuddy.Config.ShowMultiHookHints, b => GatherBuddy.Config.ShowMultiHookHints = b);


        // Spearfishing Helper
        public static void DrawSpearfishHelperBox()
            => DrawCheckbox("显示刺鱼助手",
                "切换是否在刺鱼时显示刺鱼助手。",
                GatherBuddy.Config.ShowSpearfishHelper, b => GatherBuddy.Config.ShowSpearfishHelper = b);

        public static void DrawSpearfishNamesBox()
            => DrawCheckbox("显示鱼名悬浮窗",
                "切换是否在刺鱼窗口中显示被识别的鱼的名称。",
                GatherBuddy.Config.ShowSpearfishNames, b => GatherBuddy.Config.ShowSpearfishNames = b);

        public static void DrawAvailableSpearfishBox()
            => DrawCheckbox("显示可捕获的鱼的列表",
                "切换是否在刺鱼窗口的一侧显示当前刺鱼点可捕获的鱼的列表。",
                GatherBuddy.Config.ShowAvailableSpearfish, b => GatherBuddy.Config.ShowAvailableSpearfish = b);

        public static void DrawSpearfishSpeedBox()
            => DrawCheckbox("在悬浮窗中显示鱼的速度",
                "切换是否在刺鱼窗口中鱼的名字旁额外显示鱼的速度。",
                GatherBuddy.Config.ShowSpearfishSpeed, b => GatherBuddy.Config.ShowSpearfishSpeed = b);

        public static void DrawSpearfishCenterLineBox()
            => DrawCheckbox("显示中心线",
                "切换是否在刺鱼窗口中显示一条从鱼叉的中心向上的直线。",
                GatherBuddy.Config.ShowSpearfishCenterLine, b => GatherBuddy.Config.ShowSpearfishCenterLine = b);

        public static void DrawSpearfishIconsAsTextBox()
            => DrawCheckbox("以文本方式显示速度和尺寸",
                "切换是否以文本替代图标显示可捕获的鱼的速度和大小。",
                GatherBuddy.Config.ShowSpearfishListIconsAsText, b => GatherBuddy.Config.ShowSpearfishListIconsAsText = b);

        public static void DrawSpearfishFishNameFixed()
            => DrawCheckbox("在固定位置显示鱼的名称",
                "切换是在移动的鱼上显示已识别的鱼的名称还是在一个固定位置上显示。",
                GatherBuddy.Config.FixNamesOnPosition, b => GatherBuddy.Config.FixNamesOnPosition = b);

        public static void DrawSpearfishFishNamePercentage()
        {
            if (!GatherBuddy.Config.FixNamesOnPosition)
                return;

            var tmp = (int)GatherBuddy.Config.FixNamesPercentage;
            ImGui.SetNextItemWidth(SetInputWidth);
            if (!ImGui.DragInt("鱼的名称的位置百分比", ref tmp, 0.1f, 0, 100, "%i%%"))
                return;

            tmp = Math.Clamp(tmp, 0, 100);
            if (tmp == GatherBuddy.Config.FixNamesPercentage)
                return;

            GatherBuddy.Config.FixNamesPercentage = (byte)tmp;
            GatherBuddy.Config.Save();
        }

        // Gather Window
        public static void DrawShowGatherWindowBox()
            => DrawCheckbox("显示采集窗口",
                "显示一个包含选定的可采集物和它们的出现时间的小窗口。",
                GatherBuddy.Config.ShowGatherWindow, b => GatherBuddy.Config.ShowGatherWindow = b);

        public static void DrawGatherWindowAnchorBox()
            => DrawCheckbox("以左下角锚定采集窗口",
                "让采集窗口向顶部增长并从顶部收缩。",
                GatherBuddy.Config.GatherWindowBottomAnchor, b => GatherBuddy.Config.GatherWindowBottomAnchor = b);

        public static void DrawGatherWindowTimersBox()
            => DrawCheckbox("显示采集窗口计时器",
                "在采集窗口中显示可采集物的出现时间。",
                GatherBuddy.Config.ShowGatherWindowTimers, b => GatherBuddy.Config.ShowGatherWindowTimers = b);

        public static void DrawGatherWindowAlarmsBox()
            => DrawCheckbox("在采集窗口中显示激活的闹钟",
                "额外显示激活的闹钟作为采集窗口的最后一个预设，遵守该窗口的常规规则。",
                GatherBuddy.Config.ShowGatherWindowAlarms, b =>
                {
                    GatherBuddy.Config.ShowGatherWindowAlarms = b;
                    _plugin.GatherWindowManager.SetShowGatherWindowAlarms(b);
                });

        public static void DrawSortGatherWindowBox()
            => DrawCheckbox("在采集窗口中以出现时间排序",
                "按出现时间对采集窗口中所选的物品进行排序。",
                GatherBuddy.Config.SortGatherWindowByUptime, b => GatherBuddy.Config.SortGatherWindowByUptime = b);

        public static void DrawGatherWindowShowOnlyAvailableBox()
            => DrawCheckbox("仅显示可采集物品",
                "仅显示采集窗口设置中的当前可采集的物品。",
                GatherBuddy.Config.ShowGatherWindowOnlyAvailable, b => GatherBuddy.Config.ShowGatherWindowOnlyAvailable = b);

        public static void DrawHideGatherWindowCompletedItemsBox()
            => DrawCheckbox("隐藏已经完成的物品",
                "隐藏已经满足自动采集数量要求的物品",
                GatherBuddy.Config.HideGatherWindowCompletedItems, b => GatherBuddy.Config.HideGatherWindowCompletedItems = b);

        public static void DrawHideGatherWindowInDutyBox()
            => DrawCheckbox("副本任务中隐藏采集窗口",
                "进入任何副本任务后隐藏采集窗口。",
                GatherBuddy.Config.HideGatherWindowInDuty, b => GatherBuddy.Config.HideGatherWindowInDuty = b);

        public static void DrawGatherWindowHoldKey()
        {
            DrawCheckbox("仅在按住快捷键时显示采集窗口",
                "仅在你按住你所选的快捷键时显示采集窗口。",
                GatherBuddy.Config.OnlyShowGatherWindowHoldingKey, b => GatherBuddy.Config.OnlyShowGatherWindowHoldingKey = b);

            if (!GatherBuddy.Config.OnlyShowGatherWindowHoldingKey)
                return;

            ImGui.SetNextItemWidth(SetInputWidth);
            Widget.KeySelector("需要按住的快捷键", "设置一个用来按下保持窗口可见的快捷键。",
                GatherBuddy.Config.GatherWindowHoldKey,
                k => GatherBuddy.Config.GatherWindowHoldKey = k, Configuration.ValidKeys);
        }

        public static void DrawGatherWindowLockBox()
            => DrawCheckbox("锁定采集窗口位置",
                "防止拖动移动采集窗口。",
                GatherBuddy.Config.LockGatherWindow, b => GatherBuddy.Config.LockGatherWindow = b);


        public static void DrawGatherWindowHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开采集窗口的快捷键", "设置一个用来打开采集窗口的快捷键。", SetInputWidth,
                    GatherBuddy.Config.GatherWindowHotkey, k => GatherBuddy.Config.GatherWindowHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }

        public static void DrawMainInterfaceHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开主界面的快捷键", "设置一个用来打开 GatherBuddy 主界面的快捷键",
                    SetInputWidth,
                    GatherBuddy.Config.MainInterfaceHotkey, k => GatherBuddy.Config.MainInterfaceHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }


        public static void DrawGatherWindowDeleteModifierInput()
        {
            ImGui.SetNextItemWidth(SetInputWidth);
            if (Widget.ModifierSelector("右键删除物品的修饰键",
                    "设置用来在右键单击采集窗口中的物品以删除它们时配合使用的修饰键。",
                    GatherBuddy.Config.GatherWindowDeleteModifier, k => GatherBuddy.Config.GatherWindowDeleteModifier = k))
                GatherBuddy.Config.Save();
        }


        public static void DrawAetherytePreference()
        {
            var tmp     = GatherBuddy.Config.AetherytePreference == AetherytePreference.Cost;
            var oldPref = GatherBuddy.Config.AetherytePreference;
            if (ImGui.RadioButton("偏好更便宜的以太之光", tmp))
                GatherBuddy.Config.AetherytePreference = AetherytePreference.Cost;
            var hovered = ImGui.IsItemHovered();
            ImGui.SameLine();
            if (ImGui.RadioButton("偏好更少跑图时长", !tmp))
                GatherBuddy.Config.AetherytePreference = AetherytePreference.Distance;
            hovered |= ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetTooltip(
                    "指定你是更喜欢离目标更近的以太之光（更少的跑图时间）"
                  + " 还是在为了指定物品扫描所有可采集点后传送花费更便宜的以太之光。"
                  + " 只有在物品没有时间限制并且有多个可采集点时有效。");

            if (oldPref != GatherBuddy.Config.AetherytePreference)
            {
                GatherBuddy.UptimeManager.ResetLocations();
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawAlarmFormatInput()
            => DrawFormatInput("闹钟的消息格式",
                "保持为空以不输出任何消息。\n"
              + "可替换：\n"
              + "- {Alarm} 带括号的闹钟名称。\n"
              + "- {Item} 物品链接。\n"
              + "- {Offset} 以秒为单位的闹钟偏移量。\n"
              + "- {DurationString} “还需要等待...”或“还能够维持...”。\n"
              + "- {Location} 地图旗帜链接与位置名称。",
                GatherBuddy.Config.AlarmFormat, Configuration.DefaultAlarmFormat, s => GatherBuddy.Config.AlarmFormat = s);

        public static void DrawIdentifiedGatherableFormatInput()
            => DrawFormatInput("被识别的可采集物的消息格式",
                "保持为空以不输出任何消息。\n"
              + "可替换：\n"
              + "- {Input} 输入的搜索文本。\n"
              + "- {Item} 物品链接。",
                GatherBuddy.Config.IdentifiedGatherableFormat, Configuration.DefaultIdentifiedGatherableFormat,
                s => GatherBuddy.Config.IdentifiedGatherableFormat = s);

        public static void DrawAlwaysMapsBox()
            => DrawCheckbox("有藏宝图时总是采集",      "如果GBR在采集点中看到藏宝图，它将始终首先采集藏宝图",
                GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps, b => GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps = b);
    }


    private void DrawConfigTab()
    {
        using var id  = ImRaii.PushId("设置");
        using var tab = ImRaii.TabItem("设置");

        if (!tab)
            return;

        using var child = ImRaii.Child("设置标签页");
        if (!child)
            return;

        if (ImGui.CollapsingHeader("自动采集设置"))
        {
            if (ImGui.TreeNodeEx("通用##autoGeneral"))
            {
                ConfigFunctions.DrawHonkModeBox();
                ConfigFunctions.DrawHonkVolumeSlider();
                AutoGatherUI.DrawMountSelector();
                ConfigFunctions.DrawMountUpDistance();
                ConfigFunctions.DrawMoveWhileMounting();
                ConfigFunctions.DrawSortingMethodCombo();
                ConfigFunctions.DrawUseGivingLandOnCooldown();
                ConfigFunctions.DrawGoHomeBox();
                ConfigFunctions.DrawUseSkillsForFallabckBox();
                ConfigFunctions.DrawAbandonNodesBox();
                ConfigFunctions.DrawCheckRetainersBox();
                ConfigFunctions.DrawFishCollectionBox();
                ConfigFunctions.DrawAlwaysMapsBox();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("高级"))
            {
                ConfigFunctions.DrawAutoGatherBox();
                ConfigFunctions.DrawUseFlagBox();
                ConfigFunctions.DrawUseNavigationBox();
                ConfigFunctions.DrawForceWalkingBox();
                ConfigFunctions.DrawRepairBox();
                if (GatherBuddy.Config.AutoGatherConfig.DoRepair)
                {
                    ConfigFunctions.DrawRepairThreshold();
                }

                ConfigFunctions.DrawFishingSpotMinutes();
                ConfigFunctions.DrawMaterialExtraction();
                ConfigFunctions.DrawAetherialReduction();
                ConfigFunctions.DrawLifestreamCommandTextInput();
                ConfigFunctions.DrawAntiStuckCooldown();
                ConfigFunctions.DrawStuckThreshold();
                ConfigFunctions.DrawTimedNodePrecog();
                ConfigFunctions.DrawExecutionDelay();
                ImGui.TreePop();
            }
        }

        if (ImGui.CollapsingHeader("通用设置"))
        {
            if (ImGui.TreeNodeEx("采集命令"))
            {
                ConfigFunctions.DrawPreferredJobSelect();
                ConfigFunctions.DrawGearChangeBox();
                ConfigFunctions.DrawTeleportBox();
                ConfigFunctions.DrawMapOpenBox();
                ConfigFunctions.DrawPlaceMarkerBox();
                ConfigFunctions.DrawPlaceWaymarkBox();
                ConfigFunctions.DrawAetherytePreference();
                ConfigFunctions.DrawSkipTeleportBox();
                ConfigFunctions.DrawContextMenuBox();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("套装名"))
            {
                ConfigFunctions.DrawSetInput("采矿工",    GatherBuddy.Config.MinerSetName,    s => GatherBuddy.Config.MinerSetName    = s);
                ConfigFunctions.DrawSetInput("园艺工", GatherBuddy.Config.BotanistSetName, s => GatherBuddy.Config.BotanistSetName = s);
                ConfigFunctions.DrawSetInput("捕鱼人",   GatherBuddy.Config.FisherSetName,   s => GatherBuddy.Config.FisherSetName   = s);
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("闹钟"))
            {
                ConfigFunctions.DrawAlarmToggle();
                ConfigFunctions.DrawAlarmsInDutyToggle();
                ConfigFunctions.DrawAlarmsOnlyWhenLoggedInToggle();
                ConfigFunctions.DrawWeatherAlarmPicker();
                ConfigFunctions.DrawHourAlarmPicker();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("消息"))
            {
                ConfigFunctions.DrawPrintTypeSelector();
                ConfigFunctions.DrawErrorTypeSelector();
                ConfigFunctions.DrawMapMarkerPrintBox();
                ConfigFunctions.DrawPrintUptimesBox();
                ConfigFunctions.DrawPrintClipboardBox();
                ConfigFunctions.DrawAlarmFormatInput();
                ConfigFunctions.DrawIdentifiedGatherableFormatInput();
                ImGui.TreePop();
            }

            ImGui.NewLine();
        }

        if (ImGui.CollapsingHeader("界面设置"))
        {
            if (ImGui.TreeNodeEx("设置窗口"))
            {
                ConfigFunctions._base = this;
                ConfigFunctions.DrawOpenOnStartBox();
                ConfigFunctions.DrawRespectEscapeBox();
                ConfigFunctions.DrawLockPositionBox();
                ConfigFunctions.DrawLockResizeBox();
                ConfigFunctions.DrawWeatherTabNamesBox();
                ConfigFunctions.DrawShowStatusLineBox();
                ConfigFunctions.DrawHideClippyBox();
                ConfigFunctions.DrawMainInterfaceHotkeyInput();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("钓鱼窗口"))
            {
                ConfigFunctions.DrawKeepRecordsBox();
                ConfigFunctions.DrawFishTimerBox();
                ConfigFunctions.DrawFishTimerEditBox();
                ConfigFunctions.DrawFishTimerClickthroughBox();
                ConfigFunctions.DrawFishTimerHideBox();
                ConfigFunctions.DrawFishTimerHideBox2();
                ConfigFunctions.DrawFishTimerUptimesBox();
                ConfigFunctions.DrawFishTimerScale();
                ConfigFunctions.DrawFishTimerIntervals();
                ConfigFunctions.DrawHideFishPopupBox();
                ConfigFunctions.DrawCollectableHintPopupBox();
                ConfigFunctions.DrawDoubleHookHintPopupBox();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("采集窗口"))
            {
                ConfigFunctions.DrawShowGatherWindowBox();
                ConfigFunctions.DrawGatherWindowAnchorBox();
                ConfigFunctions.DrawGatherWindowTimersBox();
                ConfigFunctions.DrawGatherWindowAlarmsBox();
                ConfigFunctions.DrawSortGatherWindowBox();
                ConfigFunctions.DrawGatherWindowShowOnlyAvailableBox();
                ConfigFunctions.DrawHideGatherWindowCompletedItemsBox();
                ConfigFunctions.DrawHideGatherWindowInDutyBox();
                ConfigFunctions.DrawGatherWindowHoldKey();
                ConfigFunctions.DrawGatherWindowLockBox();
                ConfigFunctions.DrawGatherWindowHotkeyInput();
                ConfigFunctions.DrawGatherWindowDeleteModifierInput();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("刺鱼助手"))
            {
                ConfigFunctions.DrawSpearfishHelperBox();
                ConfigFunctions.DrawSpearfishNamesBox();
                ConfigFunctions.DrawSpearfishSpeedBox();
                ConfigFunctions.DrawAvailableSpearfishBox();
                ConfigFunctions.DrawSpearfishIconsAsTextBox();
                ConfigFunctions.DrawSpearfishCenterLineBox();
                ConfigFunctions.DrawSpearfishFishNameFixed();
                ConfigFunctions.DrawSpearfishFishNamePercentage();
                ImGui.TreePop();
            }

            ImGui.NewLine();
        }

        if (ImGui.CollapsingHeader("颜色"))
        {
            foreach (var color in Enum.GetValues<ColorId>())
            {
                var (defaultColor, name, description) = color.Data();
                var currentColor = GatherBuddy.Config.Colors.TryGetValue(color, out var current) ? current : defaultColor;
                if (Widget.ColorPicker(name, description, currentColor, c => GatherBuddy.Config.Colors[color] = c, defaultColor))
                    GatherBuddy.Config.Save();
            }

            ImGui.NewLine();

            if (Widget.PaletteColorPicker("聊天栏中的名字", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorNames,
                    Configuration.DefaultSeColorNames, Configuration.ForegroundColors, out var idx))
                GatherBuddy.Config.SeColorNames = idx;
            if (Widget.PaletteColorPicker("聊天栏中的命令", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorCommands,
                    Configuration.DefaultSeColorCommands, Configuration.ForegroundColors, out idx))
                GatherBuddy.Config.SeColorCommands = idx;
            if (Widget.PaletteColorPicker("聊天栏中的参数", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorArguments,
                    Configuration.DefaultSeColorArguments, Configuration.ForegroundColors, out idx))
                GatherBuddy.Config.SeColorArguments = idx;
            if (Widget.PaletteColorPicker("聊天栏中的闹钟消息", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorAlarm,
                    Configuration.DefaultSeColorAlarm, Configuration.ForegroundColors, out idx))
                GatherBuddy.Config.SeColorAlarm = idx;

            ImGui.NewLine();
        }
    }
}
