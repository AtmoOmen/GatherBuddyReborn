using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;

using FFXIVClientStructs.STD;
using GatherBuddy.Alarms;
using GatherBuddy.AutoGather;
using GatherBuddy.AutoGather.Collectables;
using GatherBuddy.AutoGather.Collectables.Data;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Enums;
using GatherBuddy.FishTimer;
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
        
        private static string _fishFilterText = "";
        private static Fish? _selectedFish = null;
        private static string _presetName = "";
        private static string _scripShopFilterText = "";

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
            => DrawCheckbox("启用采集窗口交互(不推荐禁用此功能)",
                "是否启用自动采集物品。(\"仅寻路模式\"中建议禁用)",
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
            => DrawCheckbox("为备选采集列表物品使用技能", "当采集备选采集列表内的物品时, 自动使用相关的采集技能",
                GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems,
                b => GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems = b);

        public static void DrawAbandonNodesBox()
            => DrawCheckbox("无所需物品时取消采集点",
                "当采集点没有目标物品或你已经采集了足够的目标物品时, 停止采集并取消采集点",
                GatherBuddy.Config.AutoGatherConfig.AbandonNodes, b => GatherBuddy.Config.AutoGatherConfig.AbandonNodes = b);

        public static void DrawCheckRetainersBox()
        {
            DrawCheckbox("检查雇员库存", "在进行库存计算时, 使用 Allagan Tools 插件检查雇员的库存。",
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
                "自动采集在列表完成后停止时播放的提示音音量。\n按住 Ctrl 并点击可输入自定义数值。");
        }

        public static void DrawHonkModeBox()
            => DrawCheckbox("采集完成时播放音效", "完成列表采集后结束自动采集并播放音效",
                GatherBuddy.Config.AutoGatherConfig.HonkMode,   b => GatherBuddy.Config.AutoGatherConfig.HonkMode = b);

        public static void DrawRepairBox()
            => DrawCheckbox("自动修理装备", "装备耐久度过低时进行修理",
                GatherBuddy.Config.AutoGatherConfig.DoRepair, b => GatherBuddy.Config.AutoGatherConfig.DoRepair = b);

        public static void DrawRepairThreshold()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.RepairThreshold;
            if (ImGui.DragInt("修理阈值", ref tmp, 1, 1, 100))
            {
                GatherBuddy.Config.AutoGatherConfig.RepairThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("当装备耐久度低于此百分比时自动修理。");
        }

        public static void DrawFishingSpotMinutes()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes;
            if (ImGui.DragInt("最大钓点停留时间", ref tmp, 1, 1, 40))
            {
                GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("在单个钓点进行钓鱼的最长时间(分钟)");
        }

        public static void DrawAutoretainerBox()
        {
            DrawCheckbox("等待 AutoRetainer 多角色模式", "在 AutoRetainer 多角色模式处理雇员期间自动暂停 GBR。",
                GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode, b => GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new ImGuiEx.RequiredPluginInfo("AutoRetainer")]);
        }

        public static void DrawAutoretainerThreshold()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiModeThreshold;
            if (ImGui.DragInt("AutoRetainer 阈值 (秒)", ref tmp, 1, 0, 3600))
            {
                GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiModeThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("GBR 会在雇员探险完成前的指定秒数暂停, 以等待多角色模式处理。");
        }

        public static void DrawAutoretainerTimedNodeDelayBox()
            => DrawCheckbox("为限时采集点延迟 AutoRetainer",
                "在处理雇员前, 先完成当前/即将到来的限时采集点。",
                GatherBuddy.Config.AutoGatherConfig.AutoRetainerDelayForTimedNodes,
                b => GatherBuddy.Config.AutoGatherConfig.AutoRetainerDelayForTimedNodes = b);

        public static void DrawLifestreamCommandTextInput()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.LifestreamCommand;
            if (ImGui.InputText("Lifestream 命令", ref tmp, 100))
            {
                if (string.IsNullOrEmpty(tmp))
                    tmp = "auto";
                GatherBuddy.Config.AutoGatherConfig.LifestreamCommand = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "用于空闲或采集完成时执行的命令, 请勿包含 ‘/li’。\n修改此命令需谨慎, GBR 不会验证命令的有效性。");
        }

        public static void DrawFishCollectionBox()
            => DrawCheckbox("参与钓鱼数据收集",
                "启用后, 每当你钓到一条鱼, 其相关数据都会上传到远程服务器。\n"
              + "这些数据用于构建可用的自动钓鱼功能。\n"
              + "不会收集与你或你的角色相关的任何个人信息, 只会上传与所钓鱼类相关的数据。\n"
              + "你可以随时取消参与, 只需关闭此复选框即可。", GatherBuddy.Config.AutoGatherConfig.FishDataCollection,
                b => GatherBuddy.Config.AutoGatherConfig.FishDataCollection = b);

        public static void DrawMaterialExtraction()
            => DrawCheckbox("自动精制魔晶石",
                "自动在装备精炼度达到 100% 时精制魔晶石",
                GatherBuddy.Config.AutoGatherConfig.DoMaterialize,
                b => GatherBuddy.Config.AutoGatherConfig.DoMaterialize = b);

        public static void DrawAetherialReduction()
            => DrawCheckbox("自动精选",
                "自动在空闲状态或物品栏已满时进行精选",
                GatherBuddy.Config.AutoGatherConfig.DoReduce,
                b => GatherBuddy.Config.AutoGatherConfig.DoReduce = b);

        public static void DrawUseFlagBox()
            => DrawCheckbox("禁用地图标记导航", "是否使用地图标记进行导航(仅限时采集点)",
                GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing, b => GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing = b);

        public static void DrawFarNodeFilterDistance()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance;
            if (ImGui.DragFloat("过远采集点距离过滤", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "寻找非空采集点时, GBR 将忽略距离低于此数值的采集点, 以避免重复检查已知为空的节点。");
        }

        public static void DrawTimedNodePrecog()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog;
            if (ImGui.DragInt("限时采集点提前时间(秒)", ref tmp, 1, 0, 600))
            {
                GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("GBR 会在采集点实际出现前的多少秒, 将其视为已出现。");
        }

        public static void DrawExecutionDelay()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
            if (ImGui.DragInt("执行延迟(毫秒)", ref tmp, 1, 0, 1500))
            {
                GatherBuddy.Config.AutoGatherConfig.ExecutionDelay = (uint)Math.Min(Math.Max(0, tmp), 10000);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("使用任一技能前需要等待的时间");
        }

        public static void DrawUseGivingLandOnCooldown()
            => DrawCheckbox("大地的恩惠 可用时采集任意水晶",
                "大地的恩惠 可用时, 在任意普通采集点采集任意水晶, 无视当前目标物品",
                GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown,
                b => GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown = b);

        public static void DrawMountUpDistance()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.MountUpDistance;
            if (ImGui.DragFloat("需要上坐骑的距离", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.MountUpDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("前往采集点的距离达到此值时自动上坐骑");
        }

        public static void DrawMoveWhileMounting()
            => DrawCheckbox("上坐骑时开始移动",
                "召唤坐骑期间就开始寻路到下一个采集点",
                GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting,
                b => GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting = b);

        public static void DrawAntiStuckCooldown()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetCooldown;
            if (ImGui.DragFloat("防卡冷却(秒)", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetCooldown = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("在角色被判定为卡住后, 寻路系统将在指定秒数后重置。");
        }

        public static void DrawForceWalkingBox()
            => DrawCheckbox("强制行走", "前往采集点时强制步行, 而不是使用坐骑。",
                GatherBuddy.Config.AutoGatherConfig.ForceWalking, b => GatherBuddy.Config.AutoGatherConfig.ForceWalking = b);

        public static void DrawUseNavigationBox()
            => DrawCheckbox("使用 vnavmesh 寻路", "使用 vnavmesh 寻路以自动移动角色。",
                GatherBuddy.Config.AutoGatherConfig.UseNavigation, b => GatherBuddy.Config.AutoGatherConfig.UseNavigation = b);

        public static void DrawStuckThreshold()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetThreshold;
            if (ImGui.DragFloat("卡住阈值(秒)", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("寻路系统在经过指定秒数后会将你判定为卡住。");
        }

        public static void DrawSortingMethodCombo()
        {
            var v = GatherBuddy.Config.AutoGatherConfig.SortingMethod;
            ImGui.SetNextItemWidth(150);

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
                "切换是否在启动游戏时自动显示 GatherBuddy 配置界面。",
                GatherBuddy.Config.OpenOnStart, b => GatherBuddy.Config.OpenOnStart = b);

        public static void DrawLockPositionBox()
            => DrawCheckbox("锁定设置界面移动",
                "切换是否锁定 GatherBuddy 界面的移动。",
                GatherBuddy.Config.MainWindowLockPosition, b =>
                {
                    GatherBuddy.Config.MainWindowLockPosition = b;
                    _base.UpdateFlags();
                });

        public static void DrawLockResizeBox()
            => DrawCheckbox("锁定设置界面尺寸",
                "切换是否锁定 GatherBuddy 界面的尺寸。",
                GatherBuddy.Config.MainWindowLockResize, b =>
                {
                    GatherBuddy.Config.MainWindowLockResize = b;
                    _base.UpdateFlags();
                });

        public static void DrawRespectEscapeBox()
            => DrawCheckbox("ESC 关闭主界面",
                "切换在主界面聚焦时按下 ESC 是否会关闭界面。",
                GatherBuddy.Config.CloseOnEscape, b =>
                {
                    GatherBuddy.Config.CloseOnEscape = b;
                    _base.UpdateFlags();
                });

        public static void DrawGearChangeBox()
            => DrawCheckbox("启用套装切换",
                "切换是否在前往采集点时自动更换为对应职业的套装\n使用采矿工套装，园艺工套装以及捕鱼人套装。",
                GatherBuddy.Config.UseGearChange, b => GatherBuddy.Config.UseGearChange = b);

        public static void DrawTeleportBox()
            => DrawCheckbox("启用传送",
                "切换是否自动传送到选定的采集点。",
                GatherBuddy.Config.UseTeleport, b => GatherBuddy.Config.UseTeleport = b);

        public static void DrawMapOpenBox()
            => DrawCheckbox("打开带位置的地图",
                "切换是否自动打开目标采集点所在区域的地图, 并高亮其采集位置。",
                GatherBuddy.Config.UseCoordinates, b => GatherBuddy.Config.UseCoordinates = b);

        public static void DrawPlaceMarkerBox()
            => DrawCheckbox("在地图上放置旗帜标记",
                "切换是否在不打开地图的情况下, 在目标采集点的大致位置放置红色旗帜标记。",
                GatherBuddy.Config.UseFlag, b => GatherBuddy.Config.UseFlag = b);

        public static void DrawMapMarkerPrintBox()
            => DrawCheckbox("打印地图位置",
                "切换是否自动在聊天栏发送目标采集点的大致位置地图链接。",
                GatherBuddy.Config.WriteCoordinates, b => GatherBuddy.Config.WriteCoordinates = b);

        public static void DrawPlaceWaymarkBox()
            => DrawCheckbox("放置自定义场景标记",
                "切换是否在特定位置放置你手动设置的自定义场景标记。",
                GatherBuddy.Config.PlaceCustomWaymarks, b => GatherBuddy.Config.PlaceCustomWaymarks = b);

        public static void DrawPrintUptimesBox()
            => DrawCheckbox("采集时打印采集点出现时间",
                "当你尝试 /gather 某采集点时, 如果该点不是常驻点, 则在聊天栏打印其出现时间。",
                GatherBuddy.Config.PrintUptime, b => GatherBuddy.Config.PrintUptime = b);

        public static void DrawSkipTeleportBox()
            => DrawCheckbox("跳过附近传送",
                "如果你已在同一张地图且距离目标比所选以太之光更近, 则跳过传送。",
                GatherBuddy.Config.SkipTeleportIfClose, b => GatherBuddy.Config.SkipTeleportIfClose = b);

        public static void DrawShowStatusLineBox()
            => DrawCheckbox("显示状态线",
                "在可采集物与鱼类列表下方显示状态线。",
                GatherBuddy.Config.ShowStatusLine, v => GatherBuddy.Config.ShowStatusLine = v);

        public static void DrawHideClippyBox()
            => DrawCheckbox("隐藏 使用帮助 按钮",
                "永久隐藏可采集物与鱼类标签页中在窗口右下角的 使用帮助 按钮。",
                GatherBuddy.Config.HideClippy, v => GatherBuddy.Config.HideClippy = v);

        private const string ChatInformationString =
            "请注意, 此消息只会显示在你的聊天记录中, 与所选频道无关——其他玩家不会看到你的『说』频道消息。";

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
            => DrawCheckbox("添加游戏内右键菜单",
                "在游戏内右键菜单中为可采集物品添加\"采集\"条目。",
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
            var current = v == GatheringType.Multiple ? "无偏好" : EnumLocalization.Get(v); // 使用字典映射
            ImGui.SetNextItemWidth(SetInputWidth);
            using var combo = ImRaii.Combo("偏好职业", current);
            ImGuiUtil.HoverTooltip(
                "在采集采矿工和园艺工都可以采集物品时, 选择你的职业偏好。\n"
              + "启用后, 普通的 /gather 指令会在该物品可由两职业采集时自动变为 /gathermin 或 /gatherbtn ,\n"
              + "并在连续尝试中忽略其他选项。");
            if (!combo)
                return;

            if (ImGui.Selectable("无偏好", v == GatheringType.Multiple) && v != GatheringType.Multiple)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Multiple;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(EnumLocalization.Get(GatheringType.Miner), v == GatheringType.Miner) && v != GatheringType.Miner) // 使用字典映射
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Miner;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(EnumLocalization.Get(GatheringType.Botanist), v == GatheringType.Botanist) && v != GatheringType.Botanist) // 使用字典映射
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Botanist;
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawPrintClipboardBox()
            => DrawCheckbox("打印剪贴板信息",
                "当你保存一个对象到剪贴板时, 不论是否可行都尝试打印到聊天栏中。",
                GatherBuddy.Config.PrintClipboardMessages, b => GatherBuddy.Config.PrintClipboardMessages = b);

        // Weather Tab
        public static void DrawWeatherTabNamesBox()
            => DrawCheckbox("在天气标签页显示名称",
                "切换是否在天气标签页的表格中显示名称, 或者仅仅显示在鼠标悬浮时会显示名字的图标。",
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
            => DrawCheckbox("仅在游戏内启用闹钟", "设置当你未登入任何角色时闹钟是否应该触发。",
                GatherBuddy.Config.AlarmsOnlyWhenLoggedIn, b => GatherBuddy.Config.AlarmsOnlyWhenLoggedIn = b);

        private static void DrawAlarmPicker(string label, string description, Sounds current, Action<Sounds> setter)
        {
            var cur = (int)current;
            ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
            if (ImGui.Combo(new ImU8String(label), ref cur, AlarmCache.SoundIdNames))
                setter((Sounds)cur);
            ImGuiUtil.HoverTooltip(description);
        }

        public static void DrawWeatherAlarmPicker()
            => DrawAlarmPicker("天气改变闹钟提醒", "选择一个在每 8 个艾欧泽亚时间的正常天气改变的时候播放的声音。",
                GatherBuddy.Config.WeatherAlarm,       _plugin.AlarmManager.SetWeatherAlarm);

        public static void DrawHourAlarmPicker()
            => DrawAlarmPicker("艾欧泽亚时间改变闹钟提醒", "选择一个在当前艾欧泽亚时间改变的时候播放的声音。",
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
                "允许鼠标穿透捕鱼计时器, 并禁用其右键菜单。",
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

        public static void DrawShowLocalTimeInRecordsBox()
            => DrawCheckbox("在钓鱼记录中使用本地时间",
                "在钓鱼记录标签页中显示时间戳时, 使用本地时间而非 Unix 时间。",
                GatherBuddy.Config.UseUnixTimeFishRecords, b => GatherBuddy.Config.UseUnixTimeFishRecords = b);
        
        public static void DrawFishTimerScale()
        {
            var value = GatherBuddy.Config.FishTimerScale / 1000f;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragFloat("捕鱼计时器咬钩计时尺寸", ref value, 0.1f, FishRecord.MinBiteTime / 500f,
                FishRecord.MaxBiteTime / 1000f,
                "%2.3f 秒");

            ImGuiUtil.HoverTooltip("捕鱼计时器窗口咬钩计时的尺寸取决于这个值。\n"
              + "如果你的咬钩时间超过该值, 进度条和咬钩窗口将不显示。\n"
              + "你应该把这个值保持在你的最高咬钩窗口, 且尽可能低。通常来说 40 秒够了。");

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
            ImGuiUtil.HoverTooltip("捕鱼计时器窗口可以显示若干间隔线和 0 到 16 之间的相应的秒数。\n"
              + "设置为 0 关闭此功能。");
            if (!ret)
                return;

            var newValue = (byte)Math.Clamp(value, 0, 16);
            if (newValue == GatherBuddy.Config.ShowSecondIntervals)
                return;

            GatherBuddy.Config.ShowSecondIntervals = newValue;
            GatherBuddy.Config.Save();
        }
        
        public static void DrawFishTimerIntervalsRounding()
        {
            var value = GatherBuddy.Config.SecondIntervalsRounding;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragInt("捕鱼计时器秒数小数位数", ref value, 0.01f, 0, 3);
            ImGuiUtil.HoverTooltip("将显示的秒数四舍五入到小数点后指定的位数。 \n"
                + "设为 0 时仅显示整数。");
            if (!ret)
                return;

            var newValue = (byte)Math.Clamp(value, 0, 3);
            if (newValue == GatherBuddy.Config.SecondIntervalsRounding)
                return;

            GatherBuddy.Config.SecondIntervalsRounding = newValue;
            GatherBuddy.Config.Save();
        }

        public static void DrawHideFishPopupBox()
            => DrawCheckbox("隐藏捕获弹窗",
                "阻止弹出显示你捕获的鱼以及它的尺寸、数量和品质的弹窗。",
                GatherBuddy.Config.HideFishSizePopup, b => GatherBuddy.Config.HideFishSizePopup = b);

        public static void DrawCollectableHintPopupBox()
            => DrawCheckbox("显示收藏品提醒",
                "在捕鱼计时器窗口中显示该鱼是否为收藏品",
                GatherBuddy.Config.ShowCollectableHints, b => GatherBuddy.Config.ShowCollectableHints = b);

        public static void DrawDoubleHookHintPopupBox()
            => DrawCheckbox("显示多重提钩提醒",
                "在宇宙探索中显示该鱼是否可被双重提钩或三重提钩。", // TODO: add ocean fishing when implemented.
                GatherBuddy.Config.ShowMultiHookHints, b => GatherBuddy.Config.ShowMultiHookHints = b);
        
        
        // Fish Stats Window
        public static void DrawEnableFishStats()
            => DrawCheckbox("启用鱼类统计",
                "新增基于本地记录汇总与报告的统计标签页(当前正在测试中)。",
                GatherBuddy.Config.EnableFishStats, b => GatherBuddy.Config.EnableFishStats = b);
        public static void DrawEnableReportTime()  
            => DrawCheckbox("报告时复制时长统计",
                "复制报告时, 附加最短与最长时间统计。",
                GatherBuddy.Config.EnableReportTime, b => GatherBuddy.Config.EnableReportTime = b);
        public static void DrawEnableReportSize()  
            => DrawCheckbox("报告时复制尺寸统计",
                "复制报告时, 附加最小与最大尺寸统计。",
                GatherBuddy.Config.EnableReportSize, b => GatherBuddy.Config.EnableReportSize = b);
        public static void DrawEnableReportMulti() 
            => DrawCheckbox("报告时复制多重提钩统计",
                "复制报告时，附加多重提钩(双重提钩/三重提钩)产量统计",
                GatherBuddy.Config.EnableReportMulti, b => GatherBuddy.Config.EnableReportMulti = b);
        public static void DrawEnableGraphs()      
            => DrawCheckbox("启用图表",
                "在查看钓点时启用鱼类报告数据的图表可视化, 非常实验性！",
                GatherBuddy.Config.EnableFishStatsGraphs, b => GatherBuddy.Config.EnableFishStatsGraphs = b);

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
                "切换在移动的鱼上显示已识别的鱼的名称还是在一个固定位置上显示。",
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
                "额外显示激活的闹钟作为采集窗口的最后一个预设, 遵守该窗口的常规规则。",
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
                "副本任务中隐藏采集窗口",
                GatherBuddy.Config.HideGatherWindowInDuty, b => GatherBuddy.Config.HideGatherWindowInDuty = b);

        public static void DrawGatherWindowHoldKey()
        {
            DrawCheckbox("仅在按住快捷键时显示采集窗口",
                "仅在你按住你所选的快捷键时显示采集窗口。",
                GatherBuddy.Config.OnlyShowGatherWindowHoldingKey, b => GatherBuddy.Config.OnlyShowGatherWindowHoldingKey = b);

            if (!GatherBuddy.Config.OnlyShowGatherWindowHoldingKey)
                return;

            ImGui.SetNextItemWidth(SetInputWidth);
            Widget.KeySelector("需要按住的快捷键", "置一个用来按下保持窗口可见的快捷键。",
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
                    "指定你是更喜欢离目标更近的以太之光(更少的跑图时间)"
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
            => DrawCheckbox("有藏宝图时优先采集", "如果采集点中出现藏宝图, GBR 会优先采集藏宝图。",
                GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps, b => GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps = b);

        public static void DrawUseExistingAutoHookPresetsBox()
        {
            DrawCheckbox("使用已存在的 AutoHook 预设",
                "使用你自己的 AutoHook 预设, 而不是由 GBR 自动生成的预设。\n"
              + "将预设名称设为该鱼的物品 ID(例如：\"46188\" 对应黄金鳗)。\n"
              + "在 钓/刺鱼 标签页将鼠标悬停在鱼上即可查看其物品 ID。\n"
              + "你的自定义预设永远不会被删除——只有 GBR 自动生成的预设会被清理。",
                GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets,
                b => GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawSurfaceSlapConfig()
        {
            DrawCheckbox("启用自动『拍击水面』",
                "当非目标鱼与目标鱼具有相同咬钩类型时, 自动启用『拍击水面』。\n"
              + "这有助于排除不需要的鱼, 提高目标鱼的上钩率。",
                GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap,
                b => GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove;
                if (ImGui.RadioButton("当 GP 高于阈值时使用『拍击水面』", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于阈值", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("当你的 GP 高于/低于此阈值时, 将使用『拍击水面』。");
                
                ImGui.Unindent();
            }
        }

        public static void DrawIdenticalCastConfig()
        {
            DrawCheckbox("启用自动『专一垂钓』",
                "为目标鱼自动启用『专一垂钓』以提高上钩率。\n"
              + "在同一钓点使用『专一垂钓』可提升捕获成功率。",
                GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast,
                b => GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove;
                if (ImGui.RadioButton("当 GP 高于阈值时使用『专一垂钓』", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于阈值##IdenticalCast", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##IdenticalCast", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("当你的 GP 高于/低于此阈值时，将使用『专一垂钓』。");
                
                ImGui.Unindent();
            }
        }

        public static void DrawUseHookTimersBox()
        {
            DrawCheckbox("在 AutoHook 预设中启用咬钩计时器",
                "在自动生成的 AutoHook 预设中启用咬钩计时窗口。",
                GatherBuddy.Config.AutoGatherConfig.UseHookTimers,
                b => GatherBuddy.Config.AutoGatherConfig.UseHookTimers = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawAutoCollectablesFishingBox()
            => DrawCheckbox("自动处理鱼类收藏品",
                "根据最低收藏价值自动接受或拒绝鱼类收藏品。",
                GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing,
                b => GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing = b);
        
        public static void DrawDiademAutoAetherCannonBox()
            => DrawCheckbox("云冠群岛自动以太钻孔机",
                "当以太钻孔机能量充足(≥200)时, 自动瞄准并攻击附近敌人。\n"
              + "仅在未进行寻路/导航时开火, 每次使用间隔 2 秒。",
                GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon,
                b => GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon = b);
        
        public static void DrawCollectOnAutogatherDisabledBox()
            => DrawCheckbox("自动采集停止时交易收藏品",
                "当自动采集停止时自动交易收藏品",
                GatherBuddy.Config.CollectableConfig.CollectOnAutogatherDisabled,
                b => GatherBuddy.Config.CollectableConfig.CollectOnAutogatherDisabled = b);
        
        public static void DrawEnableAutogatherOnFinishBox()
            => DrawCheckbox("交易完成后重新启用自动采集",
                "在收藏品交易完成后自动重新启用自动采集",
                GatherBuddy.Config.CollectableConfig.EnableAutogatherOnFinish,
                b => GatherBuddy.Config.CollectableConfig.EnableAutogatherOnFinish = b);
        
        public static void DrawBuyAfterEachCollectBox()
            => DrawCheckbox("交易后自动购买票据商店物品",
                "在收藏品交易完成后自动购买票据商店物品。",
                GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect,
                b => GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect = b);
        
        public static void DrawScripShopItemManager()
        {
            var shopItems = ScripShopItemManager.ShopItems;
            var purchaseList = GatherBuddy.Config.CollectableConfig.ScripShopItems;
            
            ImGui.TextUnformatted("购买队列中的物品:");
            ImGui.Spacing();
            
            if (purchaseList.Count == 0)
            {
                ImGui.TextDisabled("队列中没有物品, 请在下方添加。");
            }
            else
            {
                ItemToPurchase? toRemove = null;
                
                foreach (var purchaseItem in purchaseList)
                {
                    using var id = ImRaii.PushId($"{purchaseItem.Name}");
                    
                    if (purchaseItem.Item != null && purchaseItem.Item.IconTexture.TryGetWrap(out var wrap, out _))
                    {
                        ImGui.Image(wrap.Handle, new Vector2(24, 24));
                        ImGui.SameLine();
                    }
                    
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text($"{purchaseItem.Name}");
                    ImGui.SameLine(300);
                    
                    unsafe
                    {
                        var inventory = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
                        var currentInventory = purchaseItem.Item != null ? inventory->GetInventoryItemCount(purchaseItem.Item.ItemId) : 0;
                        ImGui.Text($"{currentInventory}");
                    }
                    
                    ImGui.SameLine();
                    ImGui.Text("/");
                    ImGui.SameLine();
                    
                    var quantity = purchaseItem.Quantity;
                    ImGui.SetNextItemWidth(120); // 增加窗口宽度，原先太窄了
                    if (ImGui.InputInt($"##{purchaseItem.Name}_Quantity", ref quantity, 1, 10))
                    {
                        purchaseItem.Quantity = Math.Max(0, quantity);
                        GatherBuddy.Config.Save();
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button($"移除##{purchaseItem.Name}"))
                    {
                        toRemove = purchaseItem;
                    }
                }
                
                if (toRemove != null)
                {
                    purchaseList.Remove(toRemove);
                    GatherBuddy.Config.Save();
                }
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextUnformatted("添加物品:");
            
            if (shopItems.Count() == 0)
            {
                ImGui.TextDisabled("没有可用的票据商店物品, 数据可能尚未加载。");
            }
            else
            {
                if (ImGui.BeginCombo("###AddScripShopItem", "选择物品..."))
                {
                    ImGui.SetNextItemWidth(SetInputWidth - 20);
                    ImGui.InputTextWithHint("###ScripShopFilter", "搜索...", ref _scripShopFilterText, 100);
                    ImGui.Separator();
                    
                    foreach (var item in shopItems)
                    {
                        if (_scripShopFilterText.Length > 0 && !item.Name.Contains(_scripShopFilterText, StringComparison.OrdinalIgnoreCase))
                            continue;
                        
                        using var id = ImRaii.PushId($"AddItem_{item.Name}");
                        
                        var alreadyAdded = purchaseList.Any(p => p.Name == item.Name);
                        if (alreadyAdded)
                        {
                            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                        }
                        
                        if (ImGui.Selectable(item.Name, false, alreadyAdded ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                        {
                            if (!alreadyAdded)
                            {
                                purchaseList.Add(new ItemToPurchase { Item = item, Quantity = 1 });
                                GatherBuddy.Config.Save();
                                _scripShopFilterText = "";
                            }
                        }
                        
                        if (alreadyAdded)
                        {
                            ImGui.PopStyleVar();
                        }
                    }
                    
                    ImGui.EndCombo();
                }
            }
        }
        
        public static void DrawManualPresetGenerator()
        {
            ImGui.Separator();
            ImGui.TextUnformatted("手动预设生成器");
            ImGui.Spacing();
            
            var availableFish = GatherBuddy.GameData.Fishes.Values.Where(f => !f.IsSpearFish).ToList();
            
            ImGui.TextUnformatted("选择目标鱼类:");
            ImGui.SetNextItemWidth(SetInputWidth);
            
            if (ImGui.BeginCombo("###FishSelector", _selectedFish?.Name[GatherBuddy.Language] ?? "无"))
            {
                ImGui.SetNextItemWidth(SetInputWidth - 20);
                ImGui.InputTextWithHint("###FishFilter", "搜索...", ref _fishFilterText, 100);
                ImGui.Separator();
                
                using (var child = ImRaii.Child("###FishList", new Vector2(0, 200 * ImGuiHelpers.GlobalScale), false))
                {
                    for (int i = 0; i < availableFish.Count; i++)
                    {
                        var fish = availableFish[i];
                        var fishName = fish.Name[GatherBuddy.Language];
                        
                        if (_fishFilterText.Length > 0 && !fishName.ToLower().Contains(_fishFilterText.ToLower()))
                            continue;
                        
                        using var id = ImRaii.PushId($"{fish.ItemId}###{i}");
                        if (ImGui.Selectable(fishName, _selectedFish?.ItemId == fish.ItemId))
                        {
                            _selectedFish = fish;
                            _presetName = fish.ItemId.ToString();
                            _fishFilterText = "";
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }
                
                ImGui.EndCombo();
            }
            
            if (_selectedFish != null)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("预设名称:");
                ImGui.SetNextItemWidth(SetInputWidth);
                ImGui.InputText("###PresetNameInput", ref _presetName, 64);
                ImGuiUtil.HoverTooltip("预设名称应与该鱼的物品 ID 一致, 以便 GBR 自动使用此预设。");
                
                ImGui.Spacing();
                if (ImGui.Button("生成预设"))
                {
                    GenerateManualPreset(_selectedFish, _presetName);
                }
            }
        }
        
        private static void GenerateManualPreset(Fish fish, string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
                presetName = fish.ItemId.ToString();
            
            var success = AutoHookIntegration.AutoHookService.ExportPresetToAutoHook(presetName, [fish]);
            
            if (success)
            {
                Dalamud.Chat.Print($"[GatherBuddy] 已为 {fish.Name[GatherBuddy.Language]} 生成预设: '{presetName}'");
            }
            else
            {
                Dalamud.Chat.PrintError($"[GatherBuddy] 无法为 {fish.Name[GatherBuddy.Language]} 生成预设");
            }
        }
    }


    private void DrawConfigTab()
    {
        using var id  = ImRaii.PushId("Config");
        using var tab = ImRaii.TabItem("设置");
        ImGuiUtil.HoverTooltip("按照你的严谨喜好来配置属于你的 GatherBuddy。\n"
          + "如果你对他好一点, 他说不定还能变成真正的男孩。");

        if (!tab)
            return;

        using var child = ImRaii.Child("ConfigTab");
        if (!child)
            return;

        if (ImGui.CollapsingHeader("自动采集"))
        {
            if (ImGui.TreeNodeEx("通用##autoGeneral"))
            {
                AutoGatherUI.DrawMountSelector();
                ConfigFunctions.DrawMountUpDistance();
                ConfigFunctions.DrawMoveWhileMounting();
                ConfigFunctions.DrawHonkModeBox();
                if (GatherBuddy.Config.AutoGatherConfig.HonkMode)
                {
                    ConfigFunctions.DrawHonkVolumeSlider();
                }
                ConfigFunctions.DrawCheckRetainersBox();
                ConfigFunctions.DrawGoHomeBox();
                ConfigFunctions.DrawUseGivingLandOnCooldown();
                ConfigFunctions.DrawUseSkillsForFallabckBox();
                ConfigFunctions.DrawAbandonNodesBox();
                ConfigFunctions.DrawAlwaysMapsBox();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("捕鱼"))
            {
                ConfigFunctions.DrawUseExistingAutoHookPresetsBox();
                ConfigFunctions.DrawFishingSpotMinutes();
                ConfigFunctions.DrawFishCollectionBox();
                ConfigFunctions.DrawAutoCollectablesFishingBox();
                ConfigFunctions.DrawUseHookTimersBox();
                ConfigFunctions.DrawSurfaceSlapConfig();
                ConfigFunctions.DrawIdenticalCastConfig();
                ConfigFunctions.DrawManualPresetGenerator();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("高级"))
            {
                ConfigFunctions.DrawRepairBox();
                if (GatherBuddy.Config.AutoGatherConfig.DoRepair)
                {
                    ConfigFunctions.DrawRepairThreshold();
                }
                ConfigFunctions.DrawMaterialExtraction();
                ConfigFunctions.DrawAetherialReduction();
                ConfigFunctions.DrawAutoretainerBox();
                if (GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode)
                {
                    ConfigFunctions.DrawAutoretainerThreshold();
                    ConfigFunctions.DrawAutoretainerTimedNodeDelayBox();
                }
                ConfigFunctions.DrawDiademAutoAetherCannonBox();
                ConfigFunctions.DrawSortingMethodCombo();
                ConfigFunctions.DrawLifestreamCommandTextInput();
                ConfigFunctions.DrawAntiStuckCooldown();
                ConfigFunctions.DrawStuckThreshold();
                ConfigFunctions.DrawTimedNodePrecog();
                ConfigFunctions.DrawExecutionDelay();
                ConfigFunctions.DrawAutoGatherBox();
                ConfigFunctions.DrawUseFlagBox();
                ConfigFunctions.DrawUseNavigationBox();
                ConfigFunctions.DrawForceWalkingBox();
                ImGui.TreePop();
            }
            
            if (ImGui.TreeNodeEx("收藏品"))
            {
                ConfigFunctions.DrawCollectOnAutogatherDisabledBox();
                ConfigFunctions.DrawEnableAutogatherOnFinishBox();
                ConfigFunctions.DrawBuyAfterEachCollectBox();
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                if (ImGui.CollapsingHeader("票据商店购买列表"))
                {
                    ConfigFunctions.DrawScripShopItemManager();
                }
                
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
            if (ImGui.TreeNodeEx("配置窗口"))
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

            if (ImGui.TreeNodeEx("捕鱼计时器窗口"))
            {
                ConfigFunctions.DrawKeepRecordsBox();
                ConfigFunctions.DrawShowLocalTimeInRecordsBox();
                ConfigFunctions.DrawFishTimerBox();
                ConfigFunctions.DrawFishTimerEditBox();
                ConfigFunctions.DrawFishTimerClickthroughBox();
                ConfigFunctions.DrawFishTimerHideBox();
                ConfigFunctions.DrawFishTimerHideBox2();
                ConfigFunctions.DrawFishTimerUptimesBox();
                ConfigFunctions.DrawFishTimerScale();
                ConfigFunctions.DrawFishTimerIntervals();
                ConfigFunctions.DrawFishTimerIntervalsRounding();
                ConfigFunctions.DrawHideFishPopupBox();
                ConfigFunctions.DrawCollectableHintPopupBox();
                ConfigFunctions.DrawDoubleHookHintPopupBox();
                ImGui.TreePop();
            }

            if (ImGui.TreeNodeEx("鱼类统计 [测试]"))
            {
                ConfigFunctions.DrawEnableFishStats();
                ConfigFunctions.DrawEnableReportTime();
                ConfigFunctions.DrawEnableReportSize();
                ConfigFunctions.DrawEnableReportMulti();
                ConfigFunctions.DrawEnableGraphs();
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

