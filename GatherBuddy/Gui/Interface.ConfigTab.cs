using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;

using FFXIVClientStructs.STD;
using GatherBuddy.Alarms;
using GatherBuddy.AutoGather;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Enums;
using GatherBuddy.FishTimer;
using GatherBuddy.Utilities;
using Dalamud.Utility;
using ElliLib;
using ElliLib.Widgets;
using FishRecord = GatherBuddy.FishTimer.FishRecord;
using GatheringType = GatherBuddy.Enums.GatheringType;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private static class ConfigFunctions
    {
        public static Interface _base = null!;
        
        private static string _fishFilterText = "";
        private static Fish? _selectedFish = null;
        private static string _presetName = "";

        public static void DrawSetInput(string jobName, string oldName, Action<string> setName)
        {
            var tmp = oldName;
            ImGui.SetNextItemWidth(SetInputWidth);
            if (ImGui.InputText($"{jobName} Set", ref tmp, 15) && tmp != oldName)
            {
                setName(tmp);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip($"设置 {jobName.ToLowerInvariant()} 套装名称, 也可使用数字 ID");
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
            => DrawCheckbox("启用采集窗口交互 (禁用此选项不受支持)",
                "切换是否自动采集物品 (禁用后进入「仅导航模式」)",
                GatherBuddy.Config.AutoGatherConfig.DoGathering, b => GatherBuddy.Config.AutoGatherConfig.DoGathering = b);

        public static void DrawTeleportToNextNodeBox()
            => DrawCheckbox("传送到下一个限时物品",
                "当没有其他可采集物时, 传送到即将出现的限时节点或渔场, 并在以太之光处等待\n" +
                "此选项优先于闲置时回家",
                GatherBuddy.Config.AutoGatherConfig.TeleportToNextNode, b => GatherBuddy.Config.AutoGatherConfig.TeleportToNextNode = b);

        public static void DrawGoHomeBox()
        {
            DrawCheckbox("采集完成后回家",                       "使用 '/li auto' 命令在采集完成后传送回家",
                GatherBuddy.Config.AutoGatherConfig.GoHomeWhenDone, b => GatherBuddy.Config.AutoGatherConfig.GoHomeWhenDone = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("Lifestream")]);
            DrawCheckbox("闲置时回家",                       "使用 '/li auto' 命令在等待限时节点时传送回家",
                GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle, b => GatherBuddy.Config.AutoGatherConfig.GoHomeWhenIdle = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("Lifestream")]);
        }

        public static void DrawUseSkillsForFallabckBox()
            => DrawCheckbox("对备用物品使用技能", "在采集备用预设物品时使用技能",
                GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems,
                b => GatherBuddy.Config.AutoGatherConfig.UseSkillsForFallbackItems = b);

        public static void DrawAbandonNodesBox()
            => DrawCheckbox("舍弃没有所需物品的节点",
                "当已采集足够物品时, 停止采集并舍弃此节点,\n"
              + "或节点本身没有任何所需物品",
                GatherBuddy.Config.AutoGatherConfig.AbandonNodes, b => GatherBuddy.Config.AutoGatherConfig.AbandonNodes = b);

        public static void DrawCheckRetainersBox()
        {
            DrawCheckbox("检查雇员物品栏", "使用 Allagan Tools 在计算库存时检查雇员物品栏",
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
                "自动采集因清单完成而停止时播放声音的音量\n按住 CTRL 并点击可输入自定义值");
        }

        public static void DrawHonkModeBox()
            => DrawCheckbox("采集完成时播放声音", "自动采集因清单完成而停止时播放声音",
                GatherBuddy.Config.AutoGatherConfig.HonkMode,   b => GatherBuddy.Config.AutoGatherConfig.HonkMode = b);

        public static void DrawRepairBox()
            => DrawCheckbox("需要时修理装备",        "装备即将损坏时自动修理",
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

            ImGuiUtil.HoverTooltip("在此耐久百分比时修理装备");
        }

        public static void DrawFishingSpotMinutes()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes;
            if (ImGui.DragInt("最大渔场停留分钟数", ref tmp, 1, 1, 40))
            {
                GatherBuddy.Config.AutoGatherConfig.MaxFishingSpotMinutes = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("在单个渔场钓鱼的最大分钟数");
        }

        public static void DrawAutoretainerBox()
        {
            DrawCheckbox("等待 AutoRetainer 多模式", "当 AutoRetainer 在多模式下有雇员需要处理时自动暂停 GBR",
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

            ImGuiUtil.HoverTooltip("雇员探险完成前多少秒 GBR 应暂停并等待多模式");
        }

        public static void DrawAutoretainerTimedNodeDelayBox()
            => DrawCheckbox("为限时节点延迟 AutoRetainer",
                "待当前/即将出现的限时节点采集完毕后再处理雇员",
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
                "闲置或采集完成时使用的命令, 请勿包含 '/li'\n修改此命令时请小心, GBR 不会验证此命令!");
        }

        public static void DrawFishCollectionBox()
            => DrawCheckbox("同意钓鱼数据收集",
                "启用后, 每次钓到鱼时该鱼的数据将上传到远程服务器\n"
              + "此数据收集的目的是构建可用的自动钓鱼功能\n"
              + "不会收集任何个人信息, 仅收集与钓到鱼相关的数据\n"
              + "You can opt-out again at any time by simply disabling this checkbox.", GatherBuddy.Config.AutoGatherConfig.FishDataCollection,
                b => GatherBuddy.Config.AutoGatherConfig.FishDataCollection = b);

        public static void DrawMaterialExtraction()
            => DrawCheckbox("启用魔晶石精制",
                "自动从精炼度已满的物品中精制魔晶石",
                GatherBuddy.Config.AutoGatherConfig.DoMaterialize,
                b => GatherBuddy.Config.AutoGatherConfig.DoMaterialize = b);

        public static void DrawAetherialReduction()
            => DrawCheckbox("启用精炼减缩",
                "Automatically perform Aetherial Reduction when idling or if the number of free inventory slots drops below 20",
                GatherBuddy.Config.AutoGatherConfig.DoReduce,
                b => GatherBuddy.Config.AutoGatherConfig.DoReduce = b);

        public static void DrawAlwaysReduceAllItemsBox()
            => DrawCheckbox("始终精炼减缩所有物品",
                "When unchecked: If the number of free inventory slots drops below 20 while gathering,\n" +
                "emergency aetherial reduction is performed for only one item type.\n"
              + "勾选时: 对全部物品一次性执行紧急精炼减缩",
                GatherBuddy.Config.AutoGatherConfig.AlwaysReduceAllItems,
                b => GatherBuddy.Config.AutoGatherConfig.AlwaysReduceAllItems = b);

        public static void DrawUseFlagBox()
            => DrawCheckbox("禁用地图标记导航",            "是否使用地图标记导航（仅限时采集点）",
                GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing, b => GatherBuddy.Config.AutoGatherConfig.DisableFlagPathing = b);

        public static void DrawFarNodeFilterDistance()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance;
            if (ImGui.DragFloat("远距离节点过滤距离", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.FarNodeFilterDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "When looking for non-empty nodes GBR will filter out any nodes that are closer to you than this. Prevents checking nodes you can already see are empty.");
        }

        public static void DrawTimedNodePrecog()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog;
            if (ImGui.DragInt("Timed Node Precognition (Seconds)", ref tmp, 1, 0, 600))
            {
                GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("How far in advance of the node actually being up GBR should consider the node to be up");
        }

        public static void DrawExecutionDelay()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
            if (ImGui.DragInt("Execution delay (Milliseconds)", ref tmp, 1, 0, 1500))
            {
                GatherBuddy.Config.AutoGatherConfig.ExecutionDelay = (uint)Math.Min(Math.Max(0, tmp), 10000);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("按指定量延迟执行每个操作");
        }

        public static void DrawUseGivingLandOnCooldown()
            => DrawCheckbox("The Giving Land 冷却完毕时采集任意水晶",
                "当 The Giving Land 可用时, 在任意普通节点上采集随机水晶, 不论当前目标物品",
                GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown,
                b => GatherBuddy.Config.AutoGatherConfig.UseGivingLandOnCooldown = b);

        public static void DrawMountUpDistance()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.MountUpDistance;
            if (ImGui.DragFloat("上坐骑距离", ref tmp, 0.1f, 0.1f, 100f))
            {
                GatherBuddy.Config.AutoGatherConfig.MountUpDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("移动至节点时上坐骑的距离");
        }

        public static void DrawLandingDistance()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.LandingDistance;
            if (ImGui.DragFloat("落地距离", ref tmp, 0.1f, 0.0f, 50f))
            {
                GatherBuddy.Config.AutoGatherConfig.LandingDistance = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip(
                "The fixed distance from the node at which you will try to land.\n\n" +
                "Used when random landing positions are disabled, or when no collected data is available.\n\n" +
                "Low values increase the chance of being unable to dismount properly.\n" +
                "High values may produce weird-looking paths.\n" +
                "合理的值在 4 到 8 yalms 之间"
            );
        }

        public static void DrawMoveWhileMounting()
            => DrawCheckbox("上坐骑时移动",
                "召唤坐骑时开始寻路到下一个节点",
                GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting,
                b => GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting = b);

        public static void DrawAntiStuckCooldown()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetCooldown;
            if (ImGui.DragFloat("Anti-Stuck Cooldown", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetCooldown = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("导航系统在卡住后重置的等待时间（秒）");
        }

        public static void DrawForceWalkingBox()
            => DrawCheckbox("强制步行",                      "强制步行前往采集点，不使用坐骑。",
                GatherBuddy.Config.AutoGatherConfig.ForceWalking, b => GatherBuddy.Config.AutoGatherConfig.ForceWalking = b);

        public static void DrawDisableRandomLandingPositionsBox()
            => DrawCheckbox("禁用随机降落位置",
                "GBR 自动收集玩家的采集位置作为降落位置（偏移）。\n" +
                "未勾选时：在观察到玩家采集的随机位置降落。\n" +
                "勾选时：使用固定的降落距离。\n",
                GatherBuddy.Config.AutoGatherConfig.DisableRandomLandingPositions, b => GatherBuddy.Config.AutoGatherConfig.DisableRandomLandingPositions = b);

        public static void DrawUseNavigationBox()
            => DrawCheckbox("使用 vnavmesh 导航",             "使用 vnavmesh 导航自动移动角色",
                GatherBuddy.Config.AutoGatherConfig.UseNavigation, b => GatherBuddy.Config.AutoGatherConfig.UseNavigation = b);

        public static void DrawStuckThreshold()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetThreshold;
            if (ImGui.DragFloat("Stuck Threshold", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("导航系统认定你卡住的时间（秒）");
        }

        public static void DrawSortingMethodCombo()
        {
            var v = GatherBuddy.Config.AutoGatherConfig.SortingMethod;
            ImGui.SetNextItemWidth(150);

            using var combo = ImRaii.Combo("物品排序方式", v.ToString());
            ImGuiUtil.HoverTooltip("内部排序物品时使用的方法");
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
            => DrawCheckbox("启动时打开配置界面",
                "切换 GatherBuddy 界面是否在启动游戏后显示。",
                GatherBuddy.Config.OpenOnStart, b => GatherBuddy.Config.OpenOnStart = b);

        public static void DrawLockPositionBox()
            => DrawCheckbox("锁定配置界面移动",
                "切换 GatherBuddy 界面移动是否锁定。",
                GatherBuddy.Config.MainWindowLockPosition, b =>
                {
                    GatherBuddy.Config.MainWindowLockPosition = b;
                    _base.UpdateFlags();
                });

        public static void DrawLockResizeBox()
            => DrawCheckbox("锁定配置 UI 大小",
                "切换 GatherBuddy 界面大小是否锁定。",
                GatherBuddy.Config.MainWindowLockResize, b =>
                {
                    GatherBuddy.Config.MainWindowLockResize = b;
                    _base.UpdateFlags();
                });

        public static void DrawRespectEscapeBox()
            => DrawCheckbox("Esc 关闭主窗口",
                "切换主窗口获得焦点时按 Escape 是否关闭。",
                GatherBuddy.Config.CloseOnEscape, b =>
                {
                    GatherBuddy.Config.CloseOnEscape = b;
                    _base.UpdateFlags();
                });

        public static void DrawGearChangeBox()
            => DrawCheckbox("启用套装切换",
                "切换是否自动切换到对应采集点的职业套装。\n使用采矿工套装、园艺工套装和捕鱼人套装。",
                GatherBuddy.Config.UseGearChange, b => GatherBuddy.Config.UseGearChange = b);

        public static void DrawTeleportBox()
            => DrawCheckbox("启用以太传送",
                "切换是否自动传送到选定的采集点。",
                GatherBuddy.Config.UseTeleport, b => GatherBuddy.Config.UseTeleport = b);

        public static void DrawMapOpenBox()
            => DrawCheckbox("打开地图并显示位置",
                "切换是否自动打开所选采集点所在区域的地图，并高亮显示采集位置。",
                GatherBuddy.Config.UseCoordinates, b => GatherBuddy.Config.UseCoordinates = b);

        public static void DrawPlaceMarkerBox()
            => DrawCheckbox("在地图上放置标记",
                "切换是否在不打开地图的情况下自动在选定采集点的近似位置放置红色标记。",
                GatherBuddy.Config.UseFlag, b => GatherBuddy.Config.UseFlag = b);

        public static void DrawMapMarkerPrintBox()
            => DrawCheckbox("输出地图位置",
                "切换是否自动将选定采集点近似位置的地图链接输出到聊天栏。",
                GatherBuddy.Config.WriteCoordinates, b => GatherBuddy.Config.WriteCoordinates = b);

        public static void DrawPlaceWaymarkBox()
            => DrawCheckbox("放置自定义标记",
                "切换是否在已手动设置的特定位置放置自定义场地标记。",
                GatherBuddy.Config.PlaceCustomWaymarks, b => GatherBuddy.Config.PlaceCustomWaymarks = b);

        public static void DrawPrintUptimesBox()
            => DrawCheckbox("采集时输出出现时段",
                "当你使用 /gather 尝试采集非全天出现的物品时，在聊天栏输出其出现时段。",
                GatherBuddy.Config.PrintUptime, b => GatherBuddy.Config.PrintUptime = b);

        public static void DrawSkipTeleportBox()
            => DrawCheckbox("跳过近距离传送",
                "如果你在同一地图且比所选以太之光更接近目标，则跳过传送。",
                GatherBuddy.Config.SkipTeleportIfClose, b => GatherBuddy.Config.SkipTeleportIfClose = b);

        public static void DrawShowStatusLineBox()
            => DrawCheckbox("显示状态行",
                "在可采集物品和鱼类表格下方显示状态行。",
                GatherBuddy.Config.ShowStatusLine, v => GatherBuddy.Config.ShowStatusLine = v);

        public static void DrawHideClippyBox()
            => DrawCheckbox("隐藏 GatherClippy 按钮",
                "永久隐藏可采集物品和鱼类标签页中的 GatherClippy 按钮。",
                GatherBuddy.Config.HideClippy, v => GatherBuddy.Config.HideClippy = v);

        private const string ChatInformationString =
            "Note that the message only gets printed to your chat log, regardless of the selected channel"
          + " - other people will not see your 'Say' message.";

        public static void DrawPrintTypeSelector()
            => DrawChatTypeSelector("Chat Type for Messages",
                "The chat type used to print regular messages issued by GatherBuddy.\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeMessage, t => GatherBuddy.Config.ChatTypeMessage = t);

        public static void DrawErrorTypeSelector()
            => DrawChatTypeSelector("Chat Type for Errors",
                "The chat type used to print error messages issued by GatherBuddy.\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeError, t => GatherBuddy.Config.ChatTypeError = t);

        public static void DrawContextMenuBox()
            => DrawCheckbox("Add In-Game Context Menus",
                "Add a 'Gather' entry to in-game right-click context menus for gatherable items.",
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
            var current = v == GatheringType.Multiple ? "No Preference" : v.ToString();
            ImGui.SetNextItemWidth(SetInputWidth);
            using var combo = ImRaii.Combo("Preferred Job", current);
            ImGuiUtil.HoverTooltip(
                "Choose your job preference when gathering items that can be gathered by miners as well as botanists.\n"
              + "This effectively turns the regular gather command to /gathermin or /gatherbtn when an item can be gathered by both, "
              + "ignoring the other options even on successive tries.");
            if (!combo)
                return;

            if (ImGui.Selectable("No Preference", v == GatheringType.Multiple) && v != GatheringType.Multiple)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Multiple;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(GatheringType.Miner.ToString(), v == GatheringType.Miner) && v != GatheringType.Miner)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Miner;
                GatherBuddy.Config.Save();
            }

            if (ImGui.Selectable(GatheringType.Botanist.ToString(), v == GatheringType.Botanist) && v != GatheringType.Botanist)
            {
                GatherBuddy.Config.PreferredGatheringType = GatheringType.Botanist;
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawPrintClipboardBox()
            => DrawCheckbox("Print Clipboard Information",
                "Print to the chat whenever you save an object to the clipboard. Failures will be printed regardless.",
                GatherBuddy.Config.PrintClipboardMessages, b => GatherBuddy.Config.PrintClipboardMessages = b);

        // Weather Tab
        public static void DrawWeatherTabNamesBox()
            => DrawCheckbox("在天气标签中显示名称",
                "Toggle whether to write the names in the table for the weather tab, or just the icons with names on hover.",
                GatherBuddy.Config.ShowWeatherNames, b => GatherBuddy.Config.ShowWeatherNames = b);

        // Alarms
        public static void DrawAlarmToggle()
            => DrawCheckbox("启用闹钟", "Toggle all alarms on or off.", GatherBuddy.Config.AlarmsEnabled,
                b =>
                {
                    if (b)
                        _plugin.AlarmManager.Enable();
                    else
                        _plugin.AlarmManager.Disable();
                });

        public static void DrawAlarmsInDutyToggle()
            => DrawCheckbox("Enable Alarms in Duty", "Set whether alarms should trigger while you are bound by a duty.",
                GatherBuddy.Config.AlarmsInDuty,     b => GatherBuddy.Config.AlarmsInDuty = b);

        public static void DrawAlarmsOnlyWhenLoggedInToggle()
            => DrawCheckbox("Enable Alarms Only In-Game",  "Set whether alarms should trigger while you are not logged into any character.",
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
            => DrawAlarmPicker("Weather Change Alarm", "Choose a sound that is played every 8 Eorzea hours on regular weather changes.",
                GatherBuddy.Config.WeatherAlarm,       _plugin.AlarmManager.SetWeatherAlarm);

        public static void DrawHourAlarmPicker()
            => DrawAlarmPicker("Eorzea Hour Change Alarm", "Choose a sound that is played every time the current Eorzea hour changes.",
                GatherBuddy.Config.HourAlarm,              _plugin.AlarmManager.SetHourAlarm);

        // Fish Timer
        public static void DrawFishTimerBox()
            => DrawCheckbox("显示钓鱼计时器",
                "Toggle whether to show the fish timer window while fishing.",
                GatherBuddy.Config.ShowFishTimer, b => GatherBuddy.Config.ShowFishTimer = b);

        public static void DrawFishTimerEditBox()
            => DrawCheckbox("编辑钓鱼计时器",
                "Enable editing the fish timer window.",
                GatherBuddy.Config.FishTimerEdit, b => GatherBuddy.Config.FishTimerEdit = b);

        public static void DrawFishTimerClickthroughBox()
            => DrawCheckbox("启用钓鱼计时器点击穿透",
                "Allow clicking through the fish timer and disabling the context menus instead.",
                GatherBuddy.Config.FishTimerClickthrough, b => GatherBuddy.Config.FishTimerClickthrough = b);

        public static void DrawFishTimerHideBox()
            => DrawCheckbox("在钓鱼计时器中隐藏未捕获的鱼",
                "Hide all fish from the fish timer window that have not been recorded with the given combination of snagging and bait.",
                GatherBuddy.Config.HideUncaughtFish, b => GatherBuddy.Config.HideUncaughtFish = b);

        public static void DrawFishTimerHideBox2()
            => DrawCheckbox("在钓鱼计时器中隐藏不可用的鱼",
                "Hide all fish from the fish timer window that have have known requirements that are unfulfilled, like Fisher's Intuition or Snagging.",
                GatherBuddy.Config.HideUnavailableFish, b => GatherBuddy.Config.HideUnavailableFish = b);

        public static void DrawFishTimerUptimesBox()
            => DrawCheckbox("在钓鱼计时器中显示出现时间段",
                "Show the uptimes for restricted fish in the fish timer window.",
                GatherBuddy.Config.ShowFishTimerUptimes, b => GatherBuddy.Config.ShowFishTimerUptimes = b);

        public static void DrawKeepRecordsBox()
            => DrawCheckbox("Keep Fish Records",
                "Store Fish Records on your computer. This is necessary for bite timings for the fish timer window.",
                GatherBuddy.Config.StoreFishRecords, b => GatherBuddy.Config.StoreFishRecords = b);

        public static void DrawShowLocalTimeInRecordsBox()
            => DrawCheckbox("在记录中使用本地时间",
                "When displaying timestamps in the Fish Records Tab, use local time instead of Unix time.",
                GatherBuddy.Config.UseUnixTimeFishRecords, b => GatherBuddy.Config.UseUnixTimeFishRecords = b);
        
        public static void DrawFishTimerScale()
        {
            var value = GatherBuddy.Config.FishTimerScale / 1000f;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragFloat("钓鱼计时器咬钩时间缩放", ref value, 0.1f, FishRecord.MinBiteTime / 500f,
                FishRecord.MaxBiteTime / 1000f,
                "%2.3f Seconds");

            ImGuiUtil.HoverTooltip("The fishing timer window bite times are scaled to this value.\n"
              + "If your bite time exceeds the value, the progress bar and bite windows will not be displayed.\n"
              + "You should probably keep this as high as your highest bite window and as low as possible. About 40 seconds is usually enough.");

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
            var ret = ImGui.DragInt("钓鱼计时器间隔分隔线", ref value, 0.01f, 0, 16);
            ImGuiUtil.HoverTooltip("The fishing timer window can show a number of interval lines and corresponding seconds between 0 and 16.\n"
              + "Set to 0 to turn this feature off.");
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
            var ret = ImGui.DragInt("钓鱼计时器间隔小数位", ref value, 0.01f, 0, 3);
            ImGuiUtil.HoverTooltip("Round the displayed second value to this number of digits past the decimal. \n"
                + "Set to 0 to display only whole numbers.");
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
                "Prevents the popup window that shows you your caught fish and its size, amount and quality from being shown.",
                GatherBuddy.Config.HideFishSizePopup, b => GatherBuddy.Config.HideFishSizePopup = b);

        public static void DrawCollectableHintPopupBox()
            => DrawCheckbox("显示收藏品提示",
                "Show if a fish is collectable in the fish timer window.",
                GatherBuddy.Config.ShowCollectableHints, b => GatherBuddy.Config.ShowCollectableHints = b);

        public static void DrawDoubleHookHintPopupBox()
            => DrawCheckbox("显示多重提钩提示",
                "Show if a fish can be double or triple hooked in Cosmic Exploration and Ocean Fishing",
                GatherBuddy.Config.ShowMultiHookHints, b => GatherBuddy.Config.ShowMultiHookHints = b);
        public static void DrawOceanTypeHintPopupBox()
            => DrawCheckbox("Show Ocean Type Hints",
                "Show what type of fish in Ocean Fishing",
                GatherBuddy.Config.ShowOceanTypeHints, b => GatherBuddy.Config.ShowOceanTypeHints = b);
        
        // Fish Stats Window
        public static void DrawEnableFishStats()
            => DrawCheckbox("启用鱼类统计",
                "New tab for aggregating and reporting fish stats based on local records. Currently in testing.",
                GatherBuddy.Config.EnableFishStats, b => GatherBuddy.Config.EnableFishStats = b);
        public static void DrawEnableReportTime()  
            => DrawCheckbox("Copy Time Stats when reporting.",
                "When copying the report, add min and max times to the report.",
                GatherBuddy.Config.EnableReportTime, b => GatherBuddy.Config.EnableReportTime = b);
        public static void DrawEnableReportSize()  
            => DrawCheckbox("Copy Sizes Stats when reporting.",
                "When copying the report, add min and max sizes to the report.",
                GatherBuddy.Config.EnableReportSize, b => GatherBuddy.Config.EnableReportSize = b);
        public static void DrawEnableReportMulti() 
            => DrawCheckbox("Copy Multi Hook Stats when reporting.",
                "When copying the report, add stats about multi-hook yields to the report.",
                GatherBuddy.Config.EnableReportMulti, b => GatherBuddy.Config.EnableReportMulti = b);
        public static void DrawEnableGraphs()      
            => DrawCheckbox("Enable Graphs.",
                "When viewing a fishing spot, enable visualization of fish report data. Extreme Testing!",
                GatherBuddy.Config.EnableFishStatsGraphs, b => GatherBuddy.Config.EnableFishStatsGraphs = b);

        // Spearfishing Helper
        public static void DrawSpearfishHelperBox()
            => DrawCheckbox("显示刺鱼辅助器",
                "Toggle whether to show the Spearfishing Helper while spearfishing.",
                GatherBuddy.Config.ShowSpearfishHelper, b => GatherBuddy.Config.ShowSpearfishHelper = b);

        public static void DrawSpearfishNamesBox()
            => DrawCheckbox("显示鱼名覆盖层",
                "Toggle whether to show the identified names of fish in the spearfishing window.",
                GatherBuddy.Config.ShowSpearfishNames, b => GatherBuddy.Config.ShowSpearfishNames = b);

        public static void DrawAvailableSpearfishBox()
            => DrawCheckbox("显示可用鱼类列表",
                "Toggle whether to show the list of fish available in your current spearfishing spot on the side of the spearfishing window.",
                GatherBuddy.Config.ShowAvailableSpearfish, b => GatherBuddy.Config.ShowAvailableSpearfish = b);

        public static void DrawSpearfishSpeedBox()
            => DrawCheckbox("在覆盖层中显示鱼的速度",
                "Toggle whether to show the speed of fish in the spearfishing window in addition to their names.",
                GatherBuddy.Config.ShowSpearfishSpeed, b => GatherBuddy.Config.ShowSpearfishSpeed = b);

        public static void DrawSpearfishCenterLineBox()
            => DrawCheckbox("显示中心线",
                "Toggle whether to show a straight line up from the center of the spearfishing gig in the spearfishing window.",
                GatherBuddy.Config.ShowSpearfishCenterLine, b => GatherBuddy.Config.ShowSpearfishCenterLine = b);

        public static void DrawSpearfishIconsAsTextBox()
            => DrawCheckbox("将速度和尺寸显示为文字",
                "Toggle whether to show the speed and size of available fish as text instead of icons.",
                GatherBuddy.Config.ShowSpearfishListIconsAsText, b => GatherBuddy.Config.ShowSpearfishListIconsAsText = b);

        public static void DrawSpearfishFishNameFixed()
            => DrawCheckbox("在固定位置显示鱼名",
                "Toggle whether to show the identified names of fish on the moving fish themselves or in a fixed position.",
                GatherBuddy.Config.FixNamesOnPosition, b => GatherBuddy.Config.FixNamesOnPosition = b);

        public static void DrawSpearfishFishNamePercentage()
        {
            if (!GatherBuddy.Config.FixNamesOnPosition)
                return;

            var tmp = (int)GatherBuddy.Config.FixNamesPercentage;
            ImGui.SetNextItemWidth(SetInputWidth);
            if (!ImGui.DragInt("鱼名位置百分比", ref tmp, 0.1f, 0, 100, "%i%%"))
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
                "Show a small window with pinned Gatherables and their uptimes.",
                GatherBuddy.Config.ShowGatherWindow, b => GatherBuddy.Config.ShowGatherWindow = b);

        public static void DrawGatherWindowAnchorBox()
            => DrawCheckbox("将采集窗口锚定到左下角",
                "Lets the Gather Window grow to the top and shrink from the top instead of the bottom.",
                GatherBuddy.Config.GatherWindowBottomAnchor, b => GatherBuddy.Config.GatherWindowBottomAnchor = b);

        public static void DrawGatherWindowTimersBox()
            => DrawCheckbox("显示采集窗口计时器",
                "Show the uptimes for gatherables in the gather window.",
                GatherBuddy.Config.ShowGatherWindowTimers, b => GatherBuddy.Config.ShowGatherWindowTimers = b);

        public static void DrawGatherWindowAlarmsBox()
            => DrawCheckbox("在采集窗口中显示活动闹钟",
                "Additionally show active alarms as a last gather window preset, obeying the regular rules for the window.",
                GatherBuddy.Config.ShowGatherWindowAlarms, b =>
                {
                    GatherBuddy.Config.ShowGatherWindowAlarms = b;
                    _plugin.GatherWindowManager.SetShowGatherWindowAlarms(b);
                });

        public static void DrawSortGatherWindowBox()
            => DrawCheckbox("按出现时间排序采集窗口",
                "Sort the items selected for the gather window by their uptimes.",
                GatherBuddy.Config.SortGatherWindowByUptime, b => GatherBuddy.Config.SortGatherWindowByUptime = b);

        public static void DrawGatherWindowShowOnlyAvailableBox()
            => DrawCheckbox("仅显示可用物品",
                "Show only those items from your gather window setup that are currently available.",
                GatherBuddy.Config.ShowGatherWindowOnlyAvailable, b => GatherBuddy.Config.ShowGatherWindowOnlyAvailable = b);

        public static void DrawHideGatherWindowCompletedItemsBox()
            => DrawCheckbox("隐藏已完成物品",
                "Hide items that have the required inventory amount present in inventory.",
                GatherBuddy.Config.HideGatherWindowCompletedItems, b => GatherBuddy.Config.HideGatherWindowCompletedItems = b);

        public static void DrawHideGatherWindowInDutyBox()
            => DrawCheckbox("在副本中隐藏采集窗口",
                "Hide the gather window when bound by any duty.",
                GatherBuddy.Config.HideGatherWindowInDuty, b => GatherBuddy.Config.HideGatherWindowInDuty = b);

        public static void DrawGatherWindowHoldKey()
        {
            DrawCheckbox("仅按住按键时显示采集窗口",
                "Only show the gather window if you are holding your selected key.",
                GatherBuddy.Config.OnlyShowGatherWindowHoldingKey, b => GatherBuddy.Config.OnlyShowGatherWindowHoldingKey = b);

            if (!GatherBuddy.Config.OnlyShowGatherWindowHoldingKey)
                return;

            ImGui.SetNextItemWidth(SetInputWidth);
            Widget.KeySelector("Hotkey to Hold", "Set the hotkey to hold to keep the window visible.",
                GatherBuddy.Config.GatherWindowHoldKey,
                k => GatherBuddy.Config.GatherWindowHoldKey = k, Configuration.ValidKeys);
        }

        public static void DrawGatherWindowLockBox()
            => DrawCheckbox("锁定采集窗口位置",
                "Prevent moving the gather window by dragging it around.",
                GatherBuddy.Config.LockGatherWindow, b => GatherBuddy.Config.LockGatherWindow = b);


        public static void DrawGatherWindowHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开采集窗口的热键", "Set a hotkey to open the Gather Window.", SetInputWidth,
                    GatherBuddy.Config.GatherWindowHotkey, k => GatherBuddy.Config.GatherWindowHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }

        public static void DrawMainInterfaceHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开主界面的热键", "Set a hotkey to open the main GatherBuddy interface.",
                    SetInputWidth,
                    GatherBuddy.Config.MainInterfaceHotkey, k => GatherBuddy.Config.MainInterfaceHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }


        public static void DrawGatherWindowDeleteModifierInput()
        {
            ImGui.SetNextItemWidth(SetInputWidth);
            if (Widget.ModifierSelector("右键删除物品的修饰键",
                    "Set the modifier key to be used while right-clicking items in the gather window to delete them.",
                    GatherBuddy.Config.GatherWindowDeleteModifier, k => GatherBuddy.Config.GatherWindowDeleteModifier = k))
                GatherBuddy.Config.Save();
        }


        public static void DrawAetherytePreference()
        {
            var tmp     = GatherBuddy.Config.AetherytePreference == AetherytePreference.Cost;
            var oldPref = GatherBuddy.Config.AetherytePreference;
            if (ImGui.RadioButton("Prefer Cheaper Aetherytes", tmp))
                GatherBuddy.Config.AetherytePreference = AetherytePreference.Cost;
            var hovered = ImGui.IsItemHovered();
            ImGui.SameLine();
            if (ImGui.RadioButton("Prefer Less Travel Time", !tmp))
                GatherBuddy.Config.AetherytePreference = AetherytePreference.Distance;
            hovered |= ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetTooltip(
                    "Specify whether you prefer aetherytes that are closer to your target (less travel time)"
                  + " or aetherytes that are cheaper to teleport to when scanning through all available nodes for an item."
                  + " Only matters if the item is not timed and has multiple sources.");

            if (oldPref != GatherBuddy.Config.AetherytePreference)
            {
                GatherBuddy.UptimeManager.ResetLocations();
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawAlarmFormatInput()
            => DrawFormatInput("闹钟聊天格式",
                "Keep empty to have no chat output.\nCan replace:\n- {Alarm} with the alarm name in brackets.\n- {Item} with the item link.\n- {Offset} with the alarm offset in seconds.\n- {DurationString} with 'will be up for the next ...' or 'is currently up for ...'.\n- {Location} with the map flag link and location name.",
                GatherBuddy.Config.AlarmFormat, Configuration.DefaultAlarmFormat, s => GatherBuddy.Config.AlarmFormat = s);

        public static void DrawIdentifiedGatherableFormatInput()
            => DrawFormatInput("已识别可采集物品的聊天格式",
                "Keep empty to have no chat output.\nCan replace:\n- {Input} with the entered search text.\n- {Item} with the item link.",
                GatherBuddy.Config.IdentifiedGatherableFormat, Configuration.DefaultIdentifiedGatherableFormat,
                s => GatherBuddy.Config.IdentifiedGatherableFormat = s);

        public static void DrawAlwaysMapsBox()
            => DrawCheckbox("Always gather maps when available",      "GBR will always grab maps first if it sees one in a node",
                GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps, b => GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps = b);

        public static void DrawUseExistingAutoHookPresetsBox()
        {
            DrawCheckbox("Use existing AutoHook presets",
                "Use your own AutoHook presets instead of GBR-generated ones.\n"
              + "Name your preset using the fish's Item ID (e.g., '46188' for Goldentail).\n"
              + "Find Fish IDs by hovering over fish in the Fish tab.\n"
              + "Ignored when 'Use AutoHook Global Preset' is enabled.\n"
              + "Your presets will never be deleted - only GBR-generated presets are cleaned up.",
                GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets,
                b => GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawUseAutoHookGlobalPresetBox()
        {
            DrawCheckbox("Use AutoHook Global Preset",
                "Clear AutoHook's selected custom preset and let AutoHook use its built-in Global Preset for rod fishing.\n"
              + "This takes precedence over both GBR-generated presets and fish-ID AutoHook presets.\n"
              + "Spearfishing still uses GBR-generated AutoGig presets.",
                GatherBuddy.Config.AutoGatherConfig.UseAutoHookGlobalPreset,
                b => GatherBuddy.Config.AutoGatherConfig.UseAutoHookGlobalPreset = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawSurfaceSlapConfig()
        {
            DrawCheckbox("Enable automatic Surface Slap",
                "Automatically enable Surface Slap for non-target fish that share the same bite type as your target fish.\n"
              + "This helps remove unwanted fish to increase catch rates of your target.",
                GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap,
                b => GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove;
                if (ImGui.RadioButton("Use Surface Slap when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Surface Slap will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawIdenticalCastConfig()
        {
            DrawCheckbox("Enable automatic Identical Cast",
                "Automatically enable Identical Cast for your target fish to increase catch rates.\n"
              + "Identical Cast improves catch rate when used on the same fishing hole.",
                GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast,
                b => GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove;
                if (ImGui.RadioButton("Use Identical Cast when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below##IdenticalCast", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold##IdenticalCast", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Identical Cast will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawAmbitiousLureConfig()
        {
            DrawCheckbox("Enable automatic Ambitious Lure",
                "Automatically enable Ambitious Lure for fish that use Powerful Hookset.",
                GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure,
                b => GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove;
                if (ImGui.RadioButton("Use Ambitious Lure when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below##AmbitiousLure", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold##AmbitiousLure", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Ambitious Lure will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawModestLureConfig()
        {
            DrawCheckbox("Enable automatic Modest Lure",
                "Automatically enable Modest Lure for fish that use Precision Hookset.",
                GatherBuddy.Config.AutoGatherConfig.EnableModestLure,
                b => GatherBuddy.Config.AutoGatherConfig.EnableModestLure = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableModestLure)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove;
                if (ImGui.RadioButton("Use Modest Lure when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below##ModestLure", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.ModestLureGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold##ModestLure", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Modest Lure will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawUseHookTimersBox()
        {
            DrawCheckbox("Use Hook Timers in AutoHook Presets",
                "Enable bite timer windows in generated AutoHook presets.",
                GatherBuddy.Config.AutoGatherConfig.UseHookTimers,
                b => GatherBuddy.Config.AutoGatherConfig.UseHookTimers = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawAutoCollectablesFishingBox()
            => DrawCheckbox("Auto Collectables",
                "Auto accept/decline collectable fish based on minimum collectability.",
                GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing,
                b => GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing = b);

        public static void DrawDeferRepairDuringFishingBuffsBox()
            => DrawCheckbox("Defer repairs during fishing buffs",
                "Prevents GBR from stopping fishing for repairs when you have active fishing skill buffs.\n"
              + "Buffs like Patience, Surface Slap, Identical Cast, Prize Catch, etc. will be respected.",
                GatherBuddy.Config.AutoGatherConfig.DeferRepairDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferRepairDuringFishingBuffs = b);

        public static void DrawDeferReductionDuringFishingBuffsBox()
            => DrawCheckbox("Defer aetherial reduction during fishing buffs",
                "Prevents GBR from stopping fishing for aetherial reduction when you have active fishing skill buffs.",
                GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs = b);

        public static void DrawDeferMateriaExtractionDuringFishingBuffsBox()
            => DrawCheckbox("Defer materia extraction during fishing buffs",
                "Prevents GBR from stopping fishing for materia extraction when you have active fishing skill buffs.",
                GatherBuddy.Config.AutoGatherConfig.DeferMateriaExtractionDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferMateriaExtractionDuringFishingBuffs = b);

        public static void DrawFishingCordialConfig()
        {
            DrawCheckbox("Use Cordial",
                "Automatically use cordials in generated fishing presets when GP falls below the minimum threshold.",
                GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing,
                b => GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.CordialForFishingGPThreshold;
                if (ImGui.DragInt("GP Threshold", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.CordialForFishingGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Use cordial when GP falls below this threshold (prevents overcapping).");
                ImGui.Unindent();
            }
        }

        public static void DrawUsePatienceBox()
            => DrawCheckbox("Use Patience/Patience II",
                "Automatically use Patience/Patience II in generated fishing presets when fishing for:\n"
              + "• Fish requiring mooch chains\n"
              + "• Collectable fish\n"
              + "• Fish that can be used for aetherial reduction",
                GatherBuddy.Config.AutoGatherConfig.UsePatience,
                b => GatherBuddy.Config.AutoGatherConfig.UsePatience = b);

        public static void DrawPrizeCatchConfig()
        {
            DrawCheckbox("Use Prize Catch",
                "Automatically use Prize Catch in generated fishing presets.\n"
              + "Recommended for mooching or Surface Slap fishing.",
                GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch,
                b => GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove;
                if (ImGui.RadioButton("Use Prize Catch when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below##PrizeCatch", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold##PrizeCatch", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Prize Catch will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawChumConfig()
        {
            DrawCheckbox("Use Chum",
                "Automatically use Chum in generated fishing presets.",
                GatherBuddy.Config.AutoGatherConfig.UseChum,
                b => GatherBuddy.Config.AutoGatherConfig.UseChum = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.UseChum)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.ChumGPAbove;
                if (ImGui.RadioButton("Use Chum when GP is Above", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("Below##Chum", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.ChumGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP Threshold##Chum", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Chum will be used when your GP is above/below this threshold.");
                
                ImGui.Unindent();
            }
        }

        public static void DrawFishingConsumablesConfig()
        {
            DrawCheckbox("Use Food",
                "Automatically use configured food when food buff expires (only when NOT fishing or no active fishing buffs).",
                GatherBuddy.Config.AutoGatherConfig.UseFood,
                b => GatherBuddy.Config.AutoGatherConfig.UseFood = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseFood)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                DrawConsumableCombo("Select food", AutoGather.AutoGather.PossibleFoods, 
                    GatherBuddy.Config.AutoGatherConfig.FoodItemId, 
                    id => 
                    {
                        GatherBuddy.Config.AutoGatherConfig.FoodItemId = id;
                        GatherBuddy.Config.Save();
                    });
                ImGui.Unindent();
            }

            DrawCheckbox("Use Medicine",
                "Automatically use configured medicine (like Draft of Spiritbond) when medicine buff expires (only when NOT fishing or no active fishing buffs).",
                GatherBuddy.Config.AutoGatherConfig.UseMedicine,
                b => GatherBuddy.Config.AutoGatherConfig.UseMedicine = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseMedicine)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                DrawConsumableCombo("Select medicine", AutoGather.AutoGather.PossiblePotions, 
                    GatherBuddy.Config.AutoGatherConfig.MedicineItemId, 
                    id => 
                    {
                        GatherBuddy.Config.AutoGatherConfig.MedicineItemId = id;
                        GatherBuddy.Config.Save();
                    });
                ImGui.Unindent();
            }
        }

        private static void DrawConsumableCombo(string label, Lumina.Excel.Sheets.Item[] items, uint currentItemId, Action<uint> onChanged)
        {
            var list = items
                .SelectMany(item => new[]
                {
                    (item, rowid: item.RowId, isHq: false),
                    (item, rowid: item.RowId + 1_000_000, isHq: true)
                })
                .Where(x => !x.isHq || x.item.CanBeHq)
                .Select(x => (name: ItemUtil.GetItemName(x.rowid, includeIcon: true).ExtractText(), x.rowid, count: AutoGather.AutoGather.GetInventoryItemCount(x.rowid)))
                .Where(x => !string.IsNullOrEmpty(x.name))
                .OrderBy(x => x.count == 0)
                .ThenBy(x => x.name)
                .Select(x => x with { name = $"{x.name} ({x.count})" })
                .ToList();

            var selected = (currentItemId > 0 ? list.FirstOrDefault(x => x.rowid == currentItemId).name : null) ?? string.Empty;
            using var combo = ImRaii.Combo(label, selected);
            if (combo)
            {
                if (ImGui.Selectable(string.Empty, currentItemId <= 0))
                {
                    onChanged(0);
                }

                bool? separatorState = null;
                foreach (var (itemname, rowid, count) in list)
                {
                    if (count != 0)
                        separatorState = true;
                    else if (separatorState ?? false)
                    {
                        ImGui.Separator();
                        separatorState = false;
                    }

                    if (ImGui.Selectable(itemname, currentItemId == rowid))
                    {
                        onChanged(rowid);
                    }
                }
            }
        }
        
        public static void DrawDiademAutoAetherCannonBox()
            => DrawCheckbox("Diadem Auto-Aethercannon",
                "Automatically target and fire aethercannon at nearby enemies when gauge is ready (≥200).\n"
              + "Only fires while not pathing/navigating. 2-second cooldown between uses.",
                GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon,
                b => GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon = b);

        public static void DrawDiademWindmireJumps()
            => DrawCheckbox("Diadem Windmire Jumps",
                "Allows the use of Windmires for jumping between islands in the Diadem.\n" +
                "Windmires will only be used when they provide a significant distance advantage over normal movement.",
                GatherBuddy.Config.AutoGatherConfig.DiademWindmireJumps,
                b => GatherBuddy.Config.AutoGatherConfig.DiademWindmireJumps = b);
        
        public static void DrawDiademFarmCloudedNodes()
            => DrawCheckbox("Re-enter The Diadem to Reset Clouded Nodes",
                "After gathering umbral items from a clouded node, re-enter the instance to make the node reappear.",
                GatherBuddy.Config.AutoGatherConfig.DiademFarmCloudedNodes,
                b => GatherBuddy.Config.AutoGatherConfig.DiademFarmCloudedNodes = b);

        public static void DrawManualPresetGenerator()
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Manual Preset Generator");
            ImGui.Spacing();
            
            var availableFish = GatherBuddy.GameData.Fishes.Values.Where(f => !f.IsSpearFish).ToList();
            
            ImGui.TextUnformatted("Select Target Fish:");
            ImGui.SetNextItemWidth(SetInputWidth);
            
            if (ImGui.BeginCombo("###FishSelector", _selectedFish?.Name[GatherBuddy.Language] ?? "None"))
            {
                ImGui.SetNextItemWidth(SetInputWidth - 20);
                ImGui.InputTextWithHint("###FishFilter", "Search...", ref _fishFilterText, 100);
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
                ImGui.TextUnformatted("Preset Name:");
                ImGui.SetNextItemWidth(SetInputWidth);
                ImGui.InputText("###PresetNameInput", ref _presetName, 64);
                ImGuiUtil.HoverTooltip("The preset name should match the fish's Item ID for GBR to use it automatically.");
                
                ImGui.Spacing();
                if (ImGui.Button("Generate Preset"))
                {
                    GenerateManualPreset(_selectedFish, _presetName);
                }
            }
        }
        
        private static void GenerateManualPreset(Fish fish, string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
                presetName = fish.ItemId.ToString();
            
            var success = AutoHookIntegration.AutoHookService.ExportPresetToAutoHook(presetName, [fish], _base.MatchConfigPreset(fish));
            
            if (success)
            {
                if (fish.Predators.Length > 0 && fish.Predators.All(p => !p.Item1.IsSpearFish))
                {
                    Dalamud.Chat.Print($"[GatherBuddy] Generated 2 presets for {fish.Name[GatherBuddy.Language]}: '{presetName}_Predators' and '{presetName}_Target'");
                }
                else
                {
                    Dalamud.Chat.Print($"[GatherBuddy] Generated preset '{presetName}' for {fish.Name[GatherBuddy.Language]}");
                }
            }
            else
            {
                Dalamud.Chat.PrintError($"[GatherBuddy] Failed to generate preset for {fish.Name[GatherBuddy.Language]}");
            }
        }
    }

    private string _configSearch       = string.Empty;
    private int    _selectedConfigPage  = 0;

    private readonly record struct ConfigEntry(string SearchText, Action<ConfigLayout> Draw)
    {
        public ConfigEntry(string searchText, Action draw)
            : this(searchText, _ => draw())
        { }
    }
    private readonly record struct ConfigPage(string Category, string Name, ConfigEntry[] Entries);
    private readonly record struct ConfigLayout(int Depth)
    {
        public static ConfigLayout Root { get; } = new(0);

        public ConfigLayout Child => new(Depth + 1);

        public void Draw(ConfigEntry entry)
        {
            using var indent = PushConfigIndent(Depth);
            entry.Draw(this);
        }

        public void Draw(Action draw)
        {
            using var indent = PushConfigIndent(Depth);
            draw();
        }
    }

    private readonly struct ConfigIndentScope : IDisposable
    {
        private readonly float _amount;

        public ConfigIndentScope(float amount)
        {
            _amount = amount;
            ImGui.Indent(amount);
        }

        public void Dispose()
        {
            if (_amount > 0f)
                ImGui.Unindent(_amount);
        }
    }

    private static readonly ConfigPage[] ConfigPages = BuildConfigPages();

    private static ConfigIndentScope PushConfigIndent(int depth)
    {
        if (depth <= 0)
            return default;

        return new ConfigIndentScope(depth * ImGui.GetStyle().IndentSpacing);
    }

    private static ConfigPage[] BuildConfigPages() =>
    [
        new("Auto-Gather", "通用",
        [
            new("Select Mount",                                   AutoGatherUI.DrawMountSelector),
            new("上坐骑距离",                              ConfigFunctions.DrawMountUpDistance),
            new("落地距离",                               ConfigFunctions.DrawLandingDistance),
            new("上坐骑时移动",                         ConfigFunctions.DrawMoveWhileMounting),
            new("Play a sound when done gathering Playback Volume",
                layout =>
                {
                    ConfigFunctions.DrawHonkModeBox();
                    if (GatherBuddy.Config.AutoGatherConfig.HonkMode)
                        layout.Child.Draw(ConfigFunctions.DrawHonkVolumeSlider);
                }),
            new("检查雇员物品栏",                     ConfigFunctions.DrawCheckRetainersBox),
            new("传送到下一个限时物品",                    ConfigFunctions.DrawTeleportToNextNodeBox),
            new("Go home when done Go home when idle",            ConfigFunctions.DrawGoHomeBox),
            new("The Giving Land 冷却完毕时采集任意水晶", ConfigFunctions.DrawUseGivingLandOnCooldown),
            new("对备用物品使用技能",                  ConfigFunctions.DrawUseSkillsForFallabckBox),
            new("舍弃没有所需物品的节点",             ConfigFunctions.DrawAbandonNodesBox),
            new("Always gather maps when available",              ConfigFunctions.DrawAlwaysMapsBox),
        ]),
        new("Auto-Gather", "Fishing",
        [
            new("Use AutoHook Global Preset",                    ConfigFunctions.DrawUseAutoHookGlobalPresetBox),
            new("Use existing AutoHook presets",                  ConfigFunctions.DrawUseExistingAutoHookPresetsBox),
            new("最大渔场停留分钟数",                       ConfigFunctions.DrawFishingSpotMinutes),
            new("同意钓鱼数据收集",              ConfigFunctions.DrawFishCollectionBox),
            new("Auto Collectables",                              ConfigFunctions.DrawAutoCollectablesFishingBox),
            new("Defer repairs during fishing buffs",             ConfigFunctions.DrawDeferRepairDuringFishingBuffsBox),
            new("Defer aetherial reduction during fishing buffs", ConfigFunctions.DrawDeferReductionDuringFishingBuffsBox),
            new("Defer materia extraction during fishing buffs",  ConfigFunctions.DrawDeferMateriaExtractionDuringFishingBuffsBox),
            new("Use Hook Timers in AutoHook Presets",            ConfigFunctions.DrawUseHookTimersBox),
            new("Manual Preset Generator",                        ConfigFunctions.DrawManualPresetGenerator),
        ]),
        new("Auto-Gather", "Advanced",
        [
            new("Repair gear when needed Repair Threshold",
                layout =>
                {
                    ConfigFunctions.DrawRepairBox();
                    if (GatherBuddy.Config.AutoGatherConfig.DoRepair)
                        layout.Child.Draw(ConfigFunctions.DrawRepairThreshold);
                }),
            new("启用魔晶石精制",                      ConfigFunctions.DrawMaterialExtraction),
            new("Enable Aetherial Reduction Always Reduce All Items",
                layout =>
                {
                    ConfigFunctions.DrawAetherialReduction();
                    if (GatherBuddy.Config.AutoGatherConfig.DoReduce)
                        layout.Child.Draw(ConfigFunctions.DrawAlwaysReduceAllItemsBox);
                }),
            new("Wait for AutoRetainer Multi-mode AutoRetainer Threshold Delay AutoRetainer for timed nodes",
                layout =>
                {
                    ConfigFunctions.DrawAutoretainerBox();
                    if (GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode)
                    {
                        layout.Child.Draw(ConfigFunctions.DrawAutoretainerThreshold);
                        layout.Child.Draw(ConfigFunctions.DrawAutoretainerTimedNodeDelayBox);
                    }
                }),
            new("Diadem Auto-Aethercannon",                       ConfigFunctions.DrawDiademAutoAetherCannonBox),
            new("Diadem Windmire Jumps",                          ConfigFunctions.DrawDiademWindmireJumps),
            new("Re-enter The Diadem to Reset Clouded Nodes",     ConfigFunctions.DrawDiademFarmCloudedNodes),
            new("Item Sorting Method",                            ConfigFunctions.DrawSortingMethodCombo),
            new("Lifestream 命令",                             ConfigFunctions.DrawLifestreamCommandTextInput),
            new("Anti-Stuck Cooldown",                            ConfigFunctions.DrawAntiStuckCooldown),
            new("Stuck Threshold",                                ConfigFunctions.DrawStuckThreshold),
            new("Timed Node Precognition",                        ConfigFunctions.DrawTimedNodePrecog),
            new("Execution delay Milliseconds",                   ConfigFunctions.DrawExecutionDelay),
            new("Enable Gathering Window Interaction",            ConfigFunctions.DrawAutoGatherBox),
            new("Disable map marker navigation",                  ConfigFunctions.DrawUseFlagBox),
            new("Use vnavmesh Navigation",                        ConfigFunctions.DrawUseNavigationBox),
            new("Force Walking",                                  ConfigFunctions.DrawForceWalkingBox),
            new("Disable Random Landing Positions",               ConfigFunctions.DrawDisableRandomLandingPositionsBox),
        ]),
        new("通用", "Gather Command",
        [
            new("Preferred Job No Preference Miner Botanist",     ConfigFunctions.DrawPreferredJobSelect),
            new("Enable Gear Change",                             ConfigFunctions.DrawGearChangeBox),
            new("启用以太传送",                                ConfigFunctions.DrawTeleportBox),
            new("Open Map With Location",                         ConfigFunctions.DrawMapOpenBox),
            new("Place Flag Marker on Map",                       ConfigFunctions.DrawPlaceMarkerBox),
            new("放置自定义标记",                          ConfigFunctions.DrawPlaceWaymarkBox),
            new("Prefer Cheaper Aetherytes Prefer Less Travel Time", ConfigFunctions.DrawAetherytePreference),
            new("Skip Nearby Teleports",                          ConfigFunctions.DrawSkipTeleportBox),
            new("Add In-Game Context Menus",                      ConfigFunctions.DrawContextMenuBox),
        ]),
        new("通用", "Set Names",
        [
            new("采矿工套装",    () => ConfigFunctions.DrawSetInput("Miner",    GatherBuddy.Config.MinerSetName,    s => GatherBuddy.Config.MinerSetName    = s)),
            new("园艺工套装", () => ConfigFunctions.DrawSetInput("Botanist", GatherBuddy.Config.BotanistSetName, s => GatherBuddy.Config.BotanistSetName = s)),
            new("捕鱼人套装",   () => ConfigFunctions.DrawSetInput("Fisher",   GatherBuddy.Config.FisherSetName,   s => GatherBuddy.Config.FisherSetName   = s)),
        ]),
        new("通用", "闹钟",
        [
            new("启用闹钟",                                  ConfigFunctions.DrawAlarmToggle),
            new("Enable Alarms in Duty",                          ConfigFunctions.DrawAlarmsInDutyToggle),
            new("Enable Alarms Only In-Game",                     ConfigFunctions.DrawAlarmsOnlyWhenLoggedInToggle),
            new("Weather Change Alarm",                           ConfigFunctions.DrawWeatherAlarmPicker),
            new("Eorzea Hour Change Alarm",                       ConfigFunctions.DrawHourAlarmPicker),
        ]),
        new("通用", "消息",
        [
            new("Chat Type for Messages",                         ConfigFunctions.DrawPrintTypeSelector),
            new("Chat Type for Errors",                           ConfigFunctions.DrawErrorTypeSelector),
            new("Print Map Location",                             ConfigFunctions.DrawMapMarkerPrintBox),
            new("Print Node Uptimes On Gather",                   ConfigFunctions.DrawPrintUptimesBox),
            new("Print Clipboard Information",                    ConfigFunctions.DrawPrintClipboardBox),
            new("闹钟聊天格式",                              ConfigFunctions.DrawAlarmFormatInput),
            new("已识别可采集物品的聊天格式",              ConfigFunctions.DrawIdentifiedGatherableFormatInput),
        ]),
        new("界面", "配置窗口",
        [
            new("Open Config UI On Start",                        ConfigFunctions.DrawOpenOnStartBox),
            new("Esc 关闭主窗口",                      ConfigFunctions.DrawRespectEscapeBox),
            new("Lock Config UI Movement",                        ConfigFunctions.DrawLockPositionBox),
            new("锁定配置 UI 大小",                            ConfigFunctions.DrawLockResizeBox),
            new("在天气标签中显示名称",                      ConfigFunctions.DrawWeatherTabNamesBox),
            new("显示状态行",                               ConfigFunctions.DrawShowStatusLineBox),
            new("隐藏 GatherClippy 按钮",                       ConfigFunctions.DrawHideClippyBox),
            new("打开主界面的热键",                  ConfigFunctions.DrawMainInterfaceHotkeyInput),
        ]),
        new("界面", "钓鱼计时器",
        [
            new("Keep Fish Records",                              ConfigFunctions.DrawKeepRecordsBox),
            new("在记录中使用本地时间",                      ConfigFunctions.DrawShowLocalTimeInRecordsBox),
            new("显示钓鱼计时器",                                ConfigFunctions.DrawFishTimerBox),
            new("编辑钓鱼计时器",                                ConfigFunctions.DrawFishTimerEditBox),
            new("启用钓鱼计时器点击穿透",                 ConfigFunctions.DrawFishTimerClickthroughBox),
            new("在钓鱼计时器中隐藏未捕获的鱼",               ConfigFunctions.DrawFishTimerHideBox),
            new("在钓鱼计时器中隐藏不可用的鱼",            ConfigFunctions.DrawFishTimerHideBox2),
            new("在钓鱼计时器中显示出现时间段",                     ConfigFunctions.DrawFishTimerUptimesBox),
            new("钓鱼计时器咬钩时间缩放",                     ConfigFunctions.DrawFishTimerScale),
            new("钓鱼计时器间隔分隔线",                 ConfigFunctions.DrawFishTimerIntervals),
            new("钓鱼计时器间隔小数位",                   ConfigFunctions.DrawFishTimerIntervalsRounding),
            new("隐藏捕获弹窗",                               ConfigFunctions.DrawHideFishPopupBox),
            new("显示收藏品提示",                         ConfigFunctions.DrawCollectableHintPopupBox),
            new("显示多重提钩提示",                          ConfigFunctions.DrawDoubleHookHintPopupBox),
        ]),
        new("界面", "鱼类统计",
        [
            new("启用鱼类统计",                              ConfigFunctions.DrawEnableFishStats),
            new("报告时复制时间统计",                 ConfigFunctions.DrawEnableReportTime),
            new("报告时复制尺寸统计",                ConfigFunctions.DrawEnableReportSize),
            new("报告时复制多重提钩统计",           ConfigFunctions.DrawEnableReportMulti),
            new("启用图表",                                  ConfigFunctions.DrawEnableGraphs),
        ]),
        new("界面", "采集窗口",
        [
            new("显示采集窗口",                             ConfigFunctions.DrawShowGatherWindowBox),
            new("将采集窗口锚定到左下角",            ConfigFunctions.DrawGatherWindowAnchorBox),
            new("显示采集窗口计时器",                      ConfigFunctions.DrawGatherWindowTimersBox),
            new("在采集窗口中显示活动闹钟",            ConfigFunctions.DrawGatherWindowAlarmsBox),
            new("按出现时间排序采集窗口",                   ConfigFunctions.DrawSortGatherWindowBox),
            new("仅显示可用物品",                      ConfigFunctions.DrawGatherWindowShowOnlyAvailableBox),
            new("隐藏已完成物品",                           ConfigFunctions.DrawHideGatherWindowCompletedItemsBox),
            new("在副本中隐藏采集窗口",                     ConfigFunctions.DrawHideGatherWindowInDutyBox),
            new("仅按住按键时显示采集窗口",         ConfigFunctions.DrawGatherWindowHoldKey),
            new("锁定采集窗口位置",                    ConfigFunctions.DrawGatherWindowLockBox),
            new("打开采集窗口的热键",                   ConfigFunctions.DrawGatherWindowHotkeyInput),
            new("右键删除物品的修饰键",        ConfigFunctions.DrawGatherWindowDeleteModifierInput),
        ]),
        new("界面", "刺鱼",
        [
            new("显示刺鱼辅助器",                       ConfigFunctions.DrawSpearfishHelperBox),
            new("显示鱼名覆盖层",                         ConfigFunctions.DrawSpearfishNamesBox),
            new("在覆盖层中显示鱼的速度",                  ConfigFunctions.DrawSpearfishSpeedBox),
            new("显示可用鱼类列表",                    ConfigFunctions.DrawAvailableSpearfishBox),
            new("将速度和尺寸显示为文字",                    ConfigFunctions.DrawSpearfishIconsAsTextBox),
            new("显示中心线",                               ConfigFunctions.DrawSpearfishCenterLineBox),
            new("在固定位置显示鱼名",              ConfigFunctions.DrawSpearfishFishNameFixed),
            new("鱼名位置百分比",                  ConfigFunctions.DrawSpearfishFishNamePercentage),
        ]),
        new("", "颜色",
        [
            new("颜色", DrawAllColors),
        ]),
    ];

    private static void DrawAllColors()
    {
        foreach (var color in Enum.GetValues<ColorId>())
        {
            var (defaultColor, name, description) = color.Data();
            var currentColor = GatherBuddy.Config.Colors.TryGetValue(color, out var current) ? current : defaultColor;
            if (Widget.ColorPicker(name, description, currentColor, c => GatherBuddy.Config.Colors[color] = c, defaultColor))
                GatherBuddy.Config.Save();
        }

        ImGui.NewLine();

        if (Widget.PaletteColorPicker("Names in Chat",         Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorNames,
                Configuration.DefaultSeColorNames,    Configuration.ForegroundColors, out var idx))
            GatherBuddy.Config.SeColorNames = idx;
        if (Widget.PaletteColorPicker("Commands in Chat",      Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorCommands,
                Configuration.DefaultSeColorCommands, Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorCommands = idx;
        if (Widget.PaletteColorPicker("Arguments in Chat",     Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorArguments,
                Configuration.DefaultSeColorArguments, Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorArguments = idx;
        if (Widget.PaletteColorPicker("Alarm Message in Chat", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorAlarm,
                Configuration.DefaultSeColorAlarm,    Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorAlarm = idx;
    }


    private void DrawConfigTab()
    {
        using var id  = ImRaii.PushId("Config");
        using var tab = ImRaii.TabItem("Config");
        ImGuiUtil.HoverTooltip("Set up your very own GatherBuddy to your meticulous specifications.\n"
          + "If you treat him well, he might even become a real boy.");

        if (!tab)
            return;

        ConfigFunctions._base = this;

        var leftPanelWidth = 175f * Scale;

        {
            using var leftChild = ImRaii.Child("##ConfigLeft", new Vector2(leftPanelWidth, 0), true);
            if (leftChild)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##ConfigSearch", "搜索设置...", ref _configSearch, 256);
                ImGui.Separator();
                DrawConfigPageSelector();
            }
        }

        ImGui.SameLine();

        using var rightChild = ImRaii.Child("##ConfigRight", Vector2.Zero, false);
        if (!rightChild)
            return;
        var padding = ImGui.GetStyle().WindowPadding;
        ImGui.SetCursorPosY(padding.Y);

        if (!string.IsNullOrWhiteSpace(_configSearch))
            DrawConfigSearchResults();
        else
            DrawConfigPage(ConfigPages[_selectedConfigPage]);
    }

    private void DrawConfigPageSelector()
    {
        var lastCategory = string.Empty;
        for (var i = 0; i < ConfigPages.Length; i++)
        {
            var page = ConfigPages[i];
            if (page.Category != lastCategory)
            {
                if (lastCategory.Length > 0)
                    ImGui.Spacing();
                if (page.Category.Length > 0)
                {
                    ImGui.TextDisabled(page.Category.ToUpperInvariant());
                    ImGui.Separator();
                }
                lastCategory = page.Category;
            }

            var isSelected = _selectedConfigPage == i;
            if (ImGui.Selectable(page.Name, isSelected) && !isSelected)
            {
                _selectedConfigPage = i;
                _configSearch       = string.Empty;
            }
        }
    }

    private void DrawConfigSearchResults()
    {
        var query = _configSearch.Trim();
        var any   = false;
        var layout = ConfigLayout.Root;

        foreach (var page in ConfigPages)
        {
            var hasMatch = false;
            foreach (var entry in page.Entries)
            {
                if (entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    hasMatch = true;
                    break;
                }
            }

            if (!hasMatch) continue;

            if (any)
                ImGui.Spacing();
            any = true;

            var header = page.Category.Length > 0 ? $"{page.Category}: {page.Name}" : page.Name;
            DrawConfigSearchHeader(header);

            foreach (var entry in page.Entries)
                if (entry.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
                    layout.Draw(entry);
        }

        if (!any)
        {
            var startY = ImGui.GetCursorPosY();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("没有匹配搜索条件的设置");
            var targetY = startY + ImGui.GetFrameHeightWithSpacing();
            if (ImGui.GetCursorPosY() < targetY)
                ImGui.SetCursorPosY(targetY);
        }
    }

    private static void DrawConfigSearchHeader(string header)
    {
        var startY = ImGui.GetCursorPosY();
        var startX = ImGui.GetCursorPosX();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(header.ToUpperInvariant());
        var targetY = startY + ImGui.GetFrameHeightWithSpacing();
        if (ImGui.GetCursorPosY() < targetY)
            ImGui.SetCursorPosY(targetY);
        ImGui.Separator();
        ImGui.SetCursorPosX(startX);
    }

    private static void DrawConfigPage(ConfigPage page)
    {
        var layout = ConfigLayout.Root;
        foreach (var entry in page.Entries)
            layout.Draw(entry);
    }
}

