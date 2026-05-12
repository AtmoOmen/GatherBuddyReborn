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
        private static string _scripShopFilterText = "";

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
                "当已采集足够物品时, 或节点本身没有任何所需物品时, 停止采集并舍弃此节点",
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
              + "可随时通过取消勾选此选项来退出", GatherBuddy.Config.AutoGatherConfig.FishDataCollection,
                b => GatherBuddy.Config.AutoGatherConfig.FishDataCollection = b);

        public static void DrawMaterialExtraction()
            => DrawCheckbox("启用魔晶石精制",
                "自动从精炼度已满的物品中精制魔晶石",
                GatherBuddy.Config.AutoGatherConfig.DoMaterialize,
                b => GatherBuddy.Config.AutoGatherConfig.DoMaterialize = b);

        public static void DrawAetherialReduction()
            => DrawCheckbox("启用精炼减缩",
                "空闲时或可用物品栏位低于 20 时自动执行精炼减缩",
                GatherBuddy.Config.AutoGatherConfig.DoReduce,
                b => GatherBuddy.Config.AutoGatherConfig.DoReduce = b);

        public static void DrawAlwaysReduceAllItemsBox()
            => DrawCheckbox("始终精炼减缩所有物品",
                "未勾选时: 采集过程中如果可用物品栏位低于 20, 仅对一种物品执行紧急精炼减缩\n"
              +                 "勾选时: 对全部物品一次性执行紧急精炼减缩",
                GatherBuddy.Config.AutoGatherConfig.AlwaysReduceAllItems,
                b => GatherBuddy.Config.AutoGatherConfig.AlwaysReduceAllItems = b);

        public static void DrawUseFlagBox()
            => DrawCheckbox("禁用地图标记导航",            "是否使用地图标记导航 (仅限时节点)",
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
                "在寻找非空节点时, GBR 会过滤掉距离更近的节点, 避免检查已见为空的节点");
        }

        public static void DrawTimedNodePrecog()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog;
            if (ImGui.DragInt("限时节点预知 (秒)", ref tmp, 1, 0, 600))
            {
                GatherBuddy.Config.AutoGatherConfig.TimedNodePrecog = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("GBR 应提前多少秒认为节点已出现");
        }

        public static void DrawExecutionDelay()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = (int)GatherBuddy.Config.AutoGatherConfig.ExecutionDelay;
            if (ImGui.DragInt("执行延迟 (毫秒)", ref tmp, 1, 0, 1500))
            {
                GatherBuddy.Config.AutoGatherConfig.ExecutionDelay = (uint)Math.Min(Math.Max(0, tmp), 10000);
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("按指定量延迟执行每个操作");
        }

        public static void DrawUseGivingLandOnCooldown()
            => DrawCheckbox(                "The Giving Land 冷却完毕时采集任意水晶",
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
                "尝试在距离节点此固定距离处落地\n\n" +
                "在随机落点功能禁用或没有可用采集数据时使用\n\n" +
                "较低的值会增加无法正常下坐骑的可能性\n" +
                "较高的值可能产生奇怪的路径\n" +
                "合理的值在 4 到 8 yalms 之间"
            );
        }

        public static void DrawMoveWhileMounting()
            => DrawCheckbox("上坐骑时同时移动",
                "召唤坐骑时开始寻路到下一个节点",
                GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting,
                b => GatherBuddy.Config.AutoGatherConfig.MoveWhileMounting = b);

        public static void DrawAntiStuckCooldown()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetCooldown;
            if (ImGui.DragFloat("防卡死冷却", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetCooldown = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("当角色卡住时导航系统重置所需的时间 (秒)");
        }

        public static void DrawForceWalkingBox()
            => DrawCheckbox("强制步行",                      "强制步行前往节点, 不使用坐骑",
                GatherBuddy.Config.AutoGatherConfig.ForceWalking, b => GatherBuddy.Config.AutoGatherConfig.ForceWalking = b);

        public static void DrawDisableRandomLandingPositionsBox()
            => DrawCheckbox("禁用随机落点",
                "GBR 自动收集玩家采集位置作为落点 (偏移量)\n" +
                "未勾选时: 在观察到的玩家采集位置中随机落点\n" +
                "勾选时: 使用固定落地距离\n",
                GatherBuddy.Config.AutoGatherConfig.DisableRandomLandingPositions, b => GatherBuddy.Config.AutoGatherConfig.DisableRandomLandingPositions = b);

        public static void DrawUseNavigationBox()
            => DrawCheckbox("使用 vnavmesh 导航",             "使用 vnavmesh 导航自动移动角色",
                GatherBuddy.Config.AutoGatherConfig.UseNavigation, b => GatherBuddy.Config.AutoGatherConfig.UseNavigation = b);

        public static void DrawStuckThreshold()
        {
            ImGui.SetNextItemWidth(150);
            var tmp = GatherBuddy.Config.AutoGatherConfig.NavResetThreshold;
            if (ImGui.DragFloat("卡死阈值", ref tmp, 0.1f, 0.1f, 10f))
            {
                GatherBuddy.Config.AutoGatherConfig.NavResetThreshold = tmp;
                GatherBuddy.Config.Save();
            }

            ImGuiUtil.HoverTooltip("导航系统认为角色卡住所需的时间 (秒)");
        }

        public static void DrawSortingMethodCombo()
        {
            var v = GatherBuddy.Config.AutoGatherConfig.SortingMethod;
            ImGui.SetNextItemWidth(150);

            using var combo = ImRaii.Combo("物品排序方法", v.ToString());
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
            => DrawCheckbox("启动时打开配置 UI",
                "切换 GatherBuddy GUI 是否在游戏启动后可见",
                GatherBuddy.Config.OpenOnStart, b => GatherBuddy.Config.OpenOnStart = b);

        public static void DrawLockPositionBox()
            => DrawCheckbox("锁定配置 UI 移动",
                "切换是否锁定 GatherBuddy GUI 的移动",
                GatherBuddy.Config.MainWindowLockPosition, b =>
                {
                    GatherBuddy.Config.MainWindowLockPosition = b;
                    _base.UpdateFlags();
                });

        public static void DrawLockResizeBox()
            => DrawCheckbox("锁定配置 UI 大小",
                "切换是否锁定 GatherBuddy GUI 的大小",
                GatherBuddy.Config.MainWindowLockResize, b =>
                {
                    GatherBuddy.Config.MainWindowLockResize = b;
                    _base.UpdateFlags();
                });

        public static void DrawRespectEscapeBox()
            => DrawCheckbox("Esc 关闭主窗口",
                "切换按下 Escape 键且主窗口处于焦点时是否关闭它",
                GatherBuddy.Config.CloseOnEscape, b =>
                {
                    GatherBuddy.Config.CloseOnEscape = b;
                    _base.UpdateFlags();
                });

        public static void DrawGearChangeBox()
            => DrawCheckbox("启用套装切换",
                "切换是否自动切换到节点对应职业的套装\n使用采矿工套装、园艺工套装和捕鱼人套装",
                GatherBuddy.Config.UseGearChange, b => GatherBuddy.Config.UseGearChange = b);

        public static void DrawTeleportBox()
            => DrawCheckbox("启用以太传送",
                "切换是否自动传送到选定的节点",
                GatherBuddy.Config.UseTeleport, b => GatherBuddy.Config.UseTeleport = b);

        public static void DrawMapOpenBox()
            => DrawCheckbox("打开地图标注位置",
                "切换是否自动打开所选节点所在区域的地图并高亮采集位置",
                GatherBuddy.Config.UseCoordinates, b => GatherBuddy.Config.UseCoordinates = b);

        public static void DrawPlaceMarkerBox()
            => DrawCheckbox("在地图上放置标记",
                "切换是否自动在所选节点的大致位置放置红色标记, 无需打开地图",
                GatherBuddy.Config.UseFlag, b => GatherBuddy.Config.UseFlag = b);

        public static void DrawMapMarkerPrintBox()
            => DrawCheckbox("打印地图位置",
                "切换是否自动将所选节点的大致位置地图链接写入聊天",
                GatherBuddy.Config.WriteCoordinates, b => GatherBuddy.Config.WriteCoordinates = b);

        public static void DrawPlaceWaymarkBox()
            => DrawCheckbox("放置自定义标记",
                "切换是否为特定位置放置手动设置的自定义标记",
                GatherBuddy.Config.PlaceCustomWaymarks, b => GatherBuddy.Config.PlaceCustomWaymarks = b);

        public static void DrawPrintUptimesBox()
            => DrawCheckbox("采集时打印节点出现时间段",
                "在聊天中打印使用 /gather 时节点的出现时间段 (如果节点并非始终出现)",
                GatherBuddy.Config.PrintUptime, b => GatherBuddy.Config.PrintUptime = b);

        public static void DrawSkipTeleportBox()
            => DrawCheckbox("跳过近距离传送",
                "在同一地图且距离目标比选定的以太之光更近时跳过传送",
                GatherBuddy.Config.SkipTeleportIfClose, b => GatherBuddy.Config.SkipTeleportIfClose = b);

        public static void DrawShowStatusLineBox()
            => DrawCheckbox("显示状态行",
                "在可采集物品和鱼类表格下方显示状态行",
                GatherBuddy.Config.ShowStatusLine, v => GatherBuddy.Config.ShowStatusLine = v);

        public static void DrawHideClippyBox()
            => DrawCheckbox("隐藏 GatherClippy 按钮",
                "永久隐藏可采集物品和鱼类标签中的 GatherClippy 按钮",
                GatherBuddy.Config.HideClippy, v => GatherBuddy.Config.HideClippy = v);

        private const string ChatInformationString =
            "消息仅打印到聊天记录中, 与所选频道无关"
          + " - 其他人不会看到「说话」消息";

        public static void DrawPrintTypeSelector()
            => DrawChatTypeSelector("消息聊天类型",
                "GatherBuddy 发出常规消息时使用的聊天类型\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeMessage, t => GatherBuddy.Config.ChatTypeMessage = t);

        public static void DrawErrorTypeSelector()
            => DrawChatTypeSelector("错误聊天类型",
                "GatherBuddy 发出错误消息时使用的聊天类型\n"
              + ChatInformationString,
                GatherBuddy.Config.ChatTypeError, t => GatherBuddy.Config.ChatTypeError = t);

        public static void DrawContextMenuBox()
            => DrawCheckbox("添加游戏内右键菜单",
                "在可采集物品的游戏内右键菜单中添加「采集」选项",
                GatherBuddy.Config.AddIngameContextMenus, b =>
                {
                    if (b)
                        _plugin.ContextMenu.Enable();
                    else
                        _plugin.ContextMenu.Disable();
                });

        public static void DrawPreferredJobSelect()
        {
            var v       = GatherBuddy.Config.PreferredGatheringType;
            var current = v == GatheringType.Multiple ? "无偏好" : v.ToString();
            ImGui.SetNextItemWidth(SetInputWidth);
            using var combo = ImRaii.Combo("偏好职业", current);
            ImGuiUtil.HoverTooltip(
                "在采集采矿工和园艺工都能采集的物品时选择职业偏好\n"
              + "这会将普通的 /gather 命令实际转换为 /gathermin 或 /gatherbtn (当物品可被两个职业采集时), "
              + "即使连续尝试也会忽略其他选项");
            if (!combo)
                return;

            if (ImGui.Selectable("无偏好", v == GatheringType.Multiple) && v != GatheringType.Multiple)
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
            => DrawCheckbox("打印剪贴板信息",
                "在聊天中打印保存到剪贴板的数据, 失败信息无论如何都会打印",
                GatherBuddy.Config.PrintClipboardMessages, b => GatherBuddy.Config.PrintClipboardMessages = b);

        // Weather Tab
        public static void DrawWeatherTabNamesBox()
            => DrawCheckbox("在天气标签中显示名称",
                "切换是否在天气标签表格中显示名称, 还是仅显示图标并在悬停时显示名称",
                GatherBuddy.Config.ShowWeatherNames, b => GatherBuddy.Config.ShowWeatherNames = b);

        // Alarms
        public static void DrawAlarmToggle()
            => DrawCheckbox("启用闹钟", "切换所有闹钟的开启/关闭", GatherBuddy.Config.AlarmsEnabled,
                b =>
                {
                    if (b)
                        _plugin.AlarmManager.Enable();
                    else
                        _plugin.AlarmManager.Disable();
                });

        public static void DrawAlarmsInDutyToggle()
            => DrawCheckbox("在副本中启用闹钟", "设置被副本约束时闹钟是否触发",
                GatherBuddy.Config.AlarmsInDuty,     b => GatherBuddy.Config.AlarmsInDuty = b);

        public static void DrawAlarmsOnlyWhenLoggedInToggle()
            => DrawCheckbox("仅在线时启用闹钟",  "设置未登录任何角色时闹钟是否触发",
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
            => DrawAlarmPicker("天气变化闹钟", "选择在艾欧泽亚每 8 小时天气变化时播放的声音",
                GatherBuddy.Config.WeatherAlarm,       _plugin.AlarmManager.SetWeatherAlarm);

        public static void DrawHourAlarmPicker()
            => DrawAlarmPicker("艾欧泽亚时间变化闹钟", "选择每次艾欧泽亚时间小时变化时播放的声音",
                GatherBuddy.Config.HourAlarm,              _plugin.AlarmManager.SetHourAlarm);

        // Fish Timer
        public static void DrawFishTimerBox()
            => DrawCheckbox("显示钓鱼计时器",
                "切换钓鱼时是否显示钓鱼计时器窗口",
                GatherBuddy.Config.ShowFishTimer, b => GatherBuddy.Config.ShowFishTimer = b);

        public static void DrawFishTimerEditBox()
            => DrawCheckbox("编辑钓鱼计时器",
                "启用在游戏中编辑钓鱼计时器窗口",
                GatherBuddy.Config.FishTimerEdit, b => GatherBuddy.Config.FishTimerEdit = b);

        public static void DrawFishTimerClickthroughBox()
            => DrawCheckbox("启用钓鱼计时器点击穿透",
                "允许点击穿透钓鱼计时器并禁用右键菜单",
                GatherBuddy.Config.FishTimerClickthrough, b => GatherBuddy.Config.FishTimerClickthrough = b);

        public static void DrawFishTimerHideBox()
            => DrawCheckbox("在钓鱼计时器中隐藏未捕获的鱼",
                "隐藏钓鱼计时器窗口中尚未用给定撒饵和鱼饵组合记录的鱼",
                GatherBuddy.Config.HideUncaughtFish, b => GatherBuddy.Config.HideUncaughtFish = b);

        public static void DrawFishTimerHideBox2()
            => DrawCheckbox("在钓鱼计时器中隐藏不可用的鱼",
                "隐藏钓鱼计时器窗口中已知要求未满足的鱼, 如捕鱼人之识或撒饵",
                GatherBuddy.Config.HideUnavailableFish, b => GatherBuddy.Config.HideUnavailableFish = b);

        public static void DrawFishTimerUptimesBox()
            => DrawCheckbox("在钓鱼计时器中显示出现时间段",
                "在钓鱼计时器窗口中显示限时鱼的出现时间段",
                GatherBuddy.Config.ShowFishTimerUptimes, b => GatherBuddy.Config.ShowFishTimerUptimes = b);

        public static void DrawKeepRecordsBox()
            => DrawCheckbox("保留钓鱼记录",
                "在电脑上存储钓鱼记录, 钓鱼计时器窗口依赖此记录显示咬钩时间",
                GatherBuddy.Config.StoreFishRecords, b => GatherBuddy.Config.StoreFishRecords = b);

        public static void DrawShowLocalTimeInRecordsBox()
            => DrawCheckbox("在记录中使用本地时间",
                "在钓鱼记录标签中显示时间戳时, 使用本地时间而非 Unix 时间",
                GatherBuddy.Config.UseUnixTimeFishRecords, b => GatherBuddy.Config.UseUnixTimeFishRecords = b);
        
        public static void DrawFishTimerScale()
        {
            var value = GatherBuddy.Config.FishTimerScale / 1000f;
            ImGui.SetNextItemWidth(SetInputWidth);
            var ret = ImGui.DragFloat("钓鱼计时器咬钩时间缩放", ref value, 0.1f, FishRecord.MinBiteTime / 500f,
                FishRecord.MaxBiteTime / 1000f,
                "%2.3f Seconds");

            ImGuiUtil.HoverTooltip("钓鱼计时器窗口的咬钩时间缩放到此值\n"
              + "如果咬钩时间超过此值, 进度条和咬钩窗口将不显示\n"
              + "将此值设为最高咬钩窗口时间, 并尽可能低, 大约 40 秒通常足够");

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
            ImGuiUtil.HoverTooltip("钓鱼计时器窗口可以显示 0 到 16 条间隔线及其对应的秒数\n"
              + "设为 0 关闭此功能");
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
            ImGuiUtil.HoverTooltip("将显示的秒数四舍五入到指定小数位数\n"
                + "设为 0 仅显示整数");
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
                "阻止显示捕获鱼的尺寸、数量和品质弹窗",
                GatherBuddy.Config.HideFishSizePopup, b => GatherBuddy.Config.HideFishSizePopup = b);

        public static void DrawCollectableHintPopupBox()
            => DrawCheckbox("显示收藏品提示",
                "在钓鱼计时器窗口中显示鱼是否为收藏品",
                GatherBuddy.Config.ShowCollectableHints, b => GatherBuddy.Config.ShowCollectableHints = b);

        public static void DrawDoubleHookHintPopupBox()
            => DrawCheckbox("显示多重提钩提示",
                "显示鱼在宇宙探索和出海垂钓中是否可以双重或三重提钩",
                GatherBuddy.Config.ShowMultiHookHints, b => GatherBuddy.Config.ShowMultiHookHints = b);
        public static void DrawOceanTypeHintPopupBox()
            => DrawCheckbox("显示海洋类型提示",
                "显示出海垂钓中的鱼类类型",
                GatherBuddy.Config.ShowOceanTypeHints, b => GatherBuddy.Config.ShowOceanTypeHints = b);
        
        // Fish Stats Window
        public static void DrawEnableFishStats()
            => DrawCheckbox("启用鱼类统计",
                "新标签页, 基于本地记录汇总和报告鱼类统计, 目前测试中",
                GatherBuddy.Config.EnableFishStats, b => GatherBuddy.Config.EnableFishStats = b);
        public static void DrawEnableReportTime()  
            => DrawCheckbox("报告时复制时间统计",
                "复制报告时, 在报告中添加最短和最长咬钩时间",
                GatherBuddy.Config.EnableReportTime, b => GatherBuddy.Config.EnableReportTime = b);
        public static void DrawEnableReportSize()  
            => DrawCheckbox("报告时复制尺寸统计",
                "复制报告时, 在报告中添加最短和最大尺寸",
                GatherBuddy.Config.EnableReportSize, b => GatherBuddy.Config.EnableReportSize = b);
        public static void DrawEnableReportMulti() 
            => DrawCheckbox("报告时复制多重提钩统计",
                "复制报告时, 在报告中添加多重提钩产出统计",
                GatherBuddy.Config.EnableReportMulti, b => GatherBuddy.Config.EnableReportMulti = b);
        public static void DrawEnableGraphs()      
            => DrawCheckbox("启用图表",
                "查看渔场时, 启用鱼类报告数据的可视化, 极度测试中!",
                GatherBuddy.Config.EnableFishStatsGraphs, b => GatherBuddy.Config.EnableFishStatsGraphs = b);

        // Spearfishing Helper
        public static void DrawSpearfishHelperBox()
            => DrawCheckbox("显示刺鱼辅助器",
                "切换刺鱼时是否显示刺鱼辅助器",
                GatherBuddy.Config.ShowSpearfishHelper, b => GatherBuddy.Config.ShowSpearfishHelper = b);

        public static void DrawSpearfishNamesBox()
            => DrawCheckbox("显示鱼名覆盖层",
                "切换是否在刺鱼窗口中显示识别出的鱼名",
                GatherBuddy.Config.ShowSpearfishNames, b => GatherBuddy.Config.ShowSpearfishNames = b);

        public static void DrawAvailableSpearfishBox()
            => DrawCheckbox("显示可用鱼类列表",
                "切换是否在刺鱼窗口侧边显示当前刺鱼点可用的鱼类列表",
                GatherBuddy.Config.ShowAvailableSpearfish, b => GatherBuddy.Config.ShowAvailableSpearfish = b);

        public static void DrawSpearfishSpeedBox()
            => DrawCheckbox("在覆盖层中显示鱼的速度",
                "切换是否在刺鱼窗口中显示鱼的速度以及名称",
                GatherBuddy.Config.ShowSpearfishSpeed, b => GatherBuddy.Config.ShowSpearfishSpeed = b);

        public static void DrawSpearfishCenterLineBox()
            => DrawCheckbox("显示中心线",
                "切换是否在刺鱼窗口中显示从鱼叉中心向上的直线",
                GatherBuddy.Config.ShowSpearfishCenterLine, b => GatherBuddy.Config.ShowSpearfishCenterLine = b);

        public static void DrawSpearfishIconsAsTextBox()
            => DrawCheckbox("将速度和尺寸显示为文字",
                "切换是否将可用鱼的速度和尺寸显示为文字而非图标",
                GatherBuddy.Config.ShowSpearfishListIconsAsText, b => GatherBuddy.Config.ShowSpearfishListIconsAsText = b);

        public static void DrawSpearfishFishNameFixed()
            => DrawCheckbox("在固定位置显示鱼名",
                "切换是否将识别出的鱼名显示在移动的鱼身上还是固定位置",
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
                "显示一个带有固定可采集物品及其出现时间段的小窗口",
                GatherBuddy.Config.ShowGatherWindow, b => GatherBuddy.Config.ShowGatherWindow = b);

        public static void DrawGatherWindowAnchorBox()
            => DrawCheckbox("将采集窗口锚定到左下角",
                "让采集窗口向上增长和收缩, 而非从底部",
                GatherBuddy.Config.GatherWindowBottomAnchor, b => GatherBuddy.Config.GatherWindowBottomAnchor = b);

        public static void DrawGatherWindowTimersBox()
            => DrawCheckbox("显示采集窗口计时器",
                "在采集窗口中显示可采集物品的出现时间段",
                GatherBuddy.Config.ShowGatherWindowTimers, b => GatherBuddy.Config.ShowGatherWindowTimers = b);

        public static void DrawGatherWindowAlarmsBox()
            => DrawCheckbox("在采集窗口中显示活动闹钟",
                "此外, 将活动闹钟显示为最后一个采集窗口预设, 遵守窗口的常规规则",
                GatherBuddy.Config.ShowGatherWindowAlarms, b =>
                {
                    GatherBuddy.Config.ShowGatherWindowAlarms = b;
                    _plugin.GatherWindowManager.SetShowGatherWindowAlarms(b);
                });

        public static void DrawSortGatherWindowBox()
            => DrawCheckbox("按出现时间排序采集窗口",
                "按出现时间段排序采集窗口中选定的物品",
                GatherBuddy.Config.SortGatherWindowByUptime, b => GatherBuddy.Config.SortGatherWindowByUptime = b);

        public static void DrawGatherWindowShowOnlyAvailableBox()
            => DrawCheckbox("仅显示可用物品",
                "仅显示当前可用的采集窗口设置中的物品",
                GatherBuddy.Config.ShowGatherWindowOnlyAvailable, b => GatherBuddy.Config.ShowGatherWindowOnlyAvailable = b);

        public static void DrawHideGatherWindowCompletedItemsBox()
            => DrawCheckbox("隐藏已完成物品",
                "隐藏物品栏中已达到所需数量的物品",
                GatherBuddy.Config.HideGatherWindowCompletedItems, b => GatherBuddy.Config.HideGatherWindowCompletedItems = b);

        public static void DrawHideGatherWindowInDutyBox()
            => DrawCheckbox("在副本中隐藏采集窗口",
                "被任何副本约束时隐藏采集窗口",
                GatherBuddy.Config.HideGatherWindowInDuty, b => GatherBuddy.Config.HideGatherWindowInDuty = b);

        public static void DrawGatherWindowHoldKey()
        {
            DrawCheckbox("仅按住按键时显示采集窗口",
                "仅当按住选定按键时显示采集窗口",
                GatherBuddy.Config.OnlyShowGatherWindowHoldingKey, b => GatherBuddy.Config.OnlyShowGatherWindowHoldingKey = b);

            if (!GatherBuddy.Config.OnlyShowGatherWindowHoldingKey)
                return;

            ImGui.SetNextItemWidth(SetInputWidth);
            Widget.KeySelector("按住的热键", "设置按住以保持窗口可见的热键",
                GatherBuddy.Config.GatherWindowHoldKey,
                k => GatherBuddy.Config.GatherWindowHoldKey = k, Configuration.ValidKeys);
        }

        public static void DrawGatherWindowLockBox()
            => DrawCheckbox("锁定采集窗口位置",
                "禁止通过拖动移动采集窗口",
                GatherBuddy.Config.LockGatherWindow, b => GatherBuddy.Config.LockGatherWindow = b);


        public static void DrawGatherWindowHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开采集窗口的热键", "设置打开采集窗口的热键", SetInputWidth,
                    GatherBuddy.Config.GatherWindowHotkey, k => GatherBuddy.Config.GatherWindowHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }

        public static void DrawMainInterfaceHotkeyInput()
        {
            if (Widget.ModifiableKeySelector("打开主界面的热键", "设置打开 GatherBuddy 主界面的热键",
                    SetInputWidth,
                    GatherBuddy.Config.MainInterfaceHotkey, k => GatherBuddy.Config.MainInterfaceHotkey = k, Configuration.ValidKeys))
                GatherBuddy.Config.Save();
        }


        public static void DrawGatherWindowDeleteModifierInput()
        {
            ImGui.SetNextItemWidth(SetInputWidth);
            if (Widget.ModifierSelector("右键删除物品的修饰键",
                    "设置在采集窗口中右键点击物品删除它们时需要按住的修饰键",
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
            if (ImGui.RadioButton("偏好更短的旅行时间", !tmp))
                GatherBuddy.Config.AetherytePreference = AetherytePreference.Distance;
            hovered |= ImGui.IsItemHovered();
            if (hovered)
                ImGui.SetTooltip(
                    "指定在扫描物品的所有可用节点时, 是偏好距离目标更近的以太之光 (更短旅行时间)"
                  + " 还是偏好传送费用更低的以太之光"
                  + " 仅当物品不限时且有多个来源时才有效");

            if (oldPref != GatherBuddy.Config.AetherytePreference)
            {
                GatherBuddy.UptimeManager.ResetLocations();
                GatherBuddy.Config.Save();
            }
        }

        public static void DrawAlarmFormatInput()
            => DrawFormatInput("闹钟聊天格式",
                "留空则不输出聊天\n可替换:\n- {Alarm} 为带括号的闹钟名称\n- {Item} 为物品链接\n- {Offset} 为闹钟偏移秒数\n- {DurationString} 为「将在接下来的...内持续出现」或「当前正出现至...」\n- {Location} 为地图标记链接和位置名称",
                GatherBuddy.Config.AlarmFormat, Configuration.DefaultAlarmFormat, s => GatherBuddy.Config.AlarmFormat = s);

        public static void DrawIdentifiedGatherableFormatInput()
            => DrawFormatInput("已识别可采集物品的聊天格式",
                "留空则不输出聊天\n可替换:\n- {Input} 为输入的搜索文本\n- {Item} 为物品链接",
                GatherBuddy.Config.IdentifiedGatherableFormat, Configuration.DefaultIdentifiedGatherableFormat,
                s => GatherBuddy.Config.IdentifiedGatherableFormat = s);

        public static void DrawAlwaysMapsBox()
            => DrawCheckbox("始终采集可用宝图",      "如果 GBR 在节点中看到宝图, 会优先采集",
                GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps, b => GatherBuddy.Config.AutoGatherConfig.AlwaysGatherMaps = b);

        public static void DrawUseExistingAutoHookPresetsBox()
        {
            DrawCheckbox("使用已有 AutoHook 预设",
                "使用自己设置的 AutoHook 预设, 而非 GBR 生成的\n"
              + "使用鱼的物品 ID 命名预设 (例如 Goldentail 用 '46188')\n"
              + "在鱼类标签中悬停鱼可查看鱼类 ID\n"
              + "预设永远不会被删除 - 只有 GBR 生成的预设会被清理",
                GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets,
                b => GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawSurfaceSlapConfig()
        {
            DrawCheckbox("启用自动 Surface Slap",
                "自动对与目标鱼具有相同咬钩类型的非目标鱼启用 Surface Slap\n"
              + "这有助于排除不需要的鱼, 提高目标鱼捕获率",
                GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap,
                b => GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableSurfaceSlap)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Surface Slap", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##SurfaceSlap", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##SurfaceSlap", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.SurfaceSlapGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Surface Slap 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawIdenticalCastConfig()
        {
            DrawCheckbox("启用自动 Identical Cast",
                "自动对目标鱼启用 Identical Cast 以提高捕获率\n"
              + "Identical Cast 在同一渔场使用时能提高捕获率",
                GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast,
                b => GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableIdenticalCast)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Identical Cast", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.IdenticalCastGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##IdenticalCast", !gpAbove))
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
                ImGuiUtil.HoverTooltip("Identical Cast 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawAmbitiousLureConfig()
        {
            DrawCheckbox("启用自动 Ambitious Lure",
                "自动为使用强力提钩的鱼启用 Ambitious Lure",
                GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure,
                b => GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableAmbitiousLure)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Ambitious Lure", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##AmbitiousLure", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##AmbitiousLure", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.AmbitiousLureGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Ambitious Lure 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawModestLureConfig()
        {
            DrawCheckbox("启用自动 Modest Lure",
                "自动为使用精准提钩的鱼启用 Modest Lure",
                GatherBuddy.Config.AutoGatherConfig.EnableModestLure,
                b => GatherBuddy.Config.AutoGatherConfig.EnableModestLure = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.EnableModestLure)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Modest Lure", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##ModestLure", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.ModestLureGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##ModestLure", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.ModestLureGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Modest Lure 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawUseHookTimersBox()
        {
            DrawCheckbox("在 AutoHook 预设中使用咬钩计时器",
                "在生成的 AutoHook 预设中启用咬钩计时器窗口",
                GatherBuddy.Config.AutoGatherConfig.UseHookTimers,
                b => GatherBuddy.Config.AutoGatherConfig.UseHookTimers = b);
            ImGui.SameLine();
            ImGuiEx.PluginAvailabilityIndicator([new("AutoHook")]);
        }

        public static void DrawAutoCollectablesFishingBox()
            => DrawCheckbox("自动收藏品",
                "根据最低收藏价值自动接受/拒绝收藏品鱼",
                GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing,
                b => GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing = b);

        public static void DrawDeferRepairDuringFishingBuffsBox()
            => DrawCheckbox("在有钓鱼增益时延迟修理",
                "阻止 GBR 在拥有活跃的钓鱼技能增益时停止钓鱼去修理\n"
              + "如耐心、Surface Slap、Identical Cast、Prize Catch 等增益都会被尊重",
                GatherBuddy.Config.AutoGatherConfig.DeferRepairDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferRepairDuringFishingBuffs = b);

        public static void DrawDeferReductionDuringFishingBuffsBox()
            => DrawCheckbox("在有钓鱼增益时延迟精炼减缩",
                "阻止 GBR 在拥有活跃的钓鱼技能增益时停止钓鱼去进行精炼减缩",
                GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferReductionDuringFishingBuffs = b);

        public static void DrawDeferMateriaExtractionDuringFishingBuffsBox()
            => DrawCheckbox("在有钓鱼增益时延迟魔晶石精制",
                "阻止 GBR 在拥有活跃的钓鱼技能增益时停止钓鱼去精制魔晶石",
                GatherBuddy.Config.AutoGatherConfig.DeferMateriaExtractionDuringFishingBuffs,
                b => GatherBuddy.Config.AutoGatherConfig.DeferMateriaExtractionDuringFishingBuffs = b);

        public static void DrawFishingCordialConfig()
        {
            DrawCheckbox("使用强心剂",
                "当 GP 降至最低阈值以下时自动在生成的钓鱼预设中使用强心剂",
                GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing,
                b => GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseCordialForFishing)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.CordialForFishingGPThreshold;
                if (ImGui.DragInt("GP 阈值", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.CordialForFishingGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip(                "当 GP 低于此阈值时使用强心剂 (防止溢出)");
                ImGui.Unindent();
            }
        }

        public static void DrawUsePatienceBox()
            => DrawCheckbox("使用耐心/耐心 II",
                "在钓鱼生成的预设中自动使用耐心/耐心 II, 当钓鱼目标为:\n"
              + "• 需要以小钓大链的鱼\n"
              + "• 收藏品鱼\n"
              + "• 可用于精炼减缩的鱼",
                GatherBuddy.Config.AutoGatherConfig.UsePatience,
                b => GatherBuddy.Config.AutoGatherConfig.UsePatience = b);

        public static void DrawPrizeCatchConfig()
        {
            DrawCheckbox("使用 Prize Catch",
                "在钓鱼生成的预设中自动使用 Prize Catch\n"
              + "推荐用于以小钓大或 Surface Slap 钓鱼",
                GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch,
                b => GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.UsePrizeCatch)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Prize Catch", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##PrizeCatch", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##PrizeCatch", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.PrizeCatchGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Prize Catch 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawChumConfig()
        {
            DrawCheckbox("使用 Chum",
                "在钓鱼生成的预设中自动使用 Chum",
                GatherBuddy.Config.AutoGatherConfig.UseChum,
                b => GatherBuddy.Config.AutoGatherConfig.UseChum = b);
            
            if (GatherBuddy.Config.AutoGatherConfig.UseChum)
            {
                ImGui.Indent();
                
                var gpAbove = GatherBuddy.Config.AutoGatherConfig.ChumGPAbove;
                if (ImGui.RadioButton("当 GP 高于以下值时使用 Chum", gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPAbove = true;
                    GatherBuddy.Config.Save();
                }
                
                ImGui.SameLine();
                if (ImGui.RadioButton("低于##Chum", !gpAbove))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPAbove = false;
                    GatherBuddy.Config.Save();
                }
                
                var gpThreshold = GatherBuddy.Config.AutoGatherConfig.ChumGPThreshold;
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt("GP 阈值##Chum", ref gpThreshold, 1, 0, 10000))
                {
                    GatherBuddy.Config.AutoGatherConfig.ChumGPThreshold = Math.Max(0, gpThreshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("Chum 在 GP 高于/低于此阈值时使用");
                
                ImGui.Unindent();
            }
        }

        public static void DrawFishingConsumablesConfig()
        {
            DrawCheckbox("使用食物",
                "食物增益过期时自动使用配置的食物 (仅在非钓鱼或无活跃钓鱼增益时)",
                GatherBuddy.Config.AutoGatherConfig.UseFood,
                b => GatherBuddy.Config.AutoGatherConfig.UseFood = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseFood)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                DrawConsumableCombo("选择食物", AutoGather.AutoGather.PossibleFoods, 
                    GatherBuddy.Config.AutoGatherConfig.FoodItemId, 
                    id => 
                    {
                        GatherBuddy.Config.AutoGatherConfig.FoodItemId = id;
                        GatherBuddy.Config.Save();
                    });
                ImGui.Unindent();
            }

            DrawCheckbox("使用药品",
                "药品增益过期时自动使用配置的药品 (如精炼药) (仅在非钓鱼或无活跃钓鱼增益时)",
                GatherBuddy.Config.AutoGatherConfig.UseMedicine,
                b => GatherBuddy.Config.AutoGatherConfig.UseMedicine = b);

            if (GatherBuddy.Config.AutoGatherConfig.UseMedicine)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                DrawConsumableCombo("选择药品", AutoGather.AutoGather.PossiblePotions, 
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
            => DrawCheckbox("云冠群岛自动以太炮",
                "当计量表就绪 (≥200) 时自动瞄准并射击附近敌人的以太炮\n"
              + "仅在非寻路/导航时开火, 每次使用间隔 2 秒",
                GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon,
                b => GatherBuddy.Config.AutoGatherConfig.DiademAutoAetherCannon = b);

        public static void DrawDiademWindmireJumps()
            => DrawCheckbox("云冠群岛风车跳跃",
                "允许使用风车在云冠群岛的岛屿之间跳跃\n" +
                "仅当风车能提供比正常移动显著的距离优势时才使用",
                GatherBuddy.Config.AutoGatherConfig.DiademWindmireJumps,
                b => GatherBuddy.Config.AutoGatherConfig.DiademWindmireJumps = b);
        
        public static void DrawDiademFarmCloudedNodes()
            => DrawCheckbox("重新进入云冠群岛以重置云冠节点",
                "从云冠节点采集阴云物品后, 重新进入副本使节点重新出现",
                GatherBuddy.Config.AutoGatherConfig.DiademFarmCloudedNodes,
                b => GatherBuddy.Config.AutoGatherConfig.DiademFarmCloudedNodes = b);

        public static void DrawCollectableAutoTurninBox()
            => DrawCheckbox("自动缴纳收藏品",
                "采集过程中当物品栏达到阈值时自动缴纳收藏品",
                GatherBuddy.Config.CollectableConfig.AutoTurnInCollectables,
                b => GatherBuddy.Config.CollectableConfig.AutoTurnInCollectables = b);
        
        public static void DrawCollectableThreshold()
        {
            var useInventoryFull = GatherBuddy.Config.CollectableConfig.UseInventoryFullThreshold;
            
            if (ImGui.RadioButton("收藏品数量阈值", !useInventoryFull))
            {
                GatherBuddy.Config.CollectableConfig.UseInventoryFullThreshold = false;
                GatherBuddy.Config.Save();
            }
            ImGuiUtil.HoverTooltip("当拥有一定数量的收藏品时缴纳");
            
            if (!useInventoryFull)
            {
                ImGui.Indent();
                var threshold = GatherBuddy.Config.CollectableConfig.CollectableInventoryThreshold;
                ImGui.SetNextItemWidth(150);
                if (ImGui.DragInt("收藏品数量", ref threshold, 1, 1, 999))
                {
                    GatherBuddy.Config.CollectableConfig.CollectableInventoryThreshold = Math.Max(1, threshold);
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("当拥有此数量的收藏品时缴纳");
                ImGui.Unindent();
            }
            
            if (ImGui.RadioButton("物品栏已满阈值", useInventoryFull))
            {
                GatherBuddy.Config.CollectableConfig.UseInventoryFullThreshold = true;
                GatherBuddy.Config.Save();
            }
            ImGuiUtil.HoverTooltip("当物品栏占用格数达到一定数量时缴纳收藏品");
            
            if (useInventoryFull)
            {
                ImGui.Indent();
                var fullThreshold = GatherBuddy.Config.CollectableConfig.InventoryFullThreshold;
                ImGui.SetNextItemWidth(150);
                if (ImGui.DragInt("已用物品栏格数", ref fullThreshold, 1, 1, 140))
                {
                    GatherBuddy.Config.CollectableConfig.InventoryFullThreshold = Math.Max(1, Math.Min(140, fullThreshold));
                    GatherBuddy.Config.Save();
                }
                ImGuiUtil.HoverTooltip("当物品栏有此数量的占用格数时缴纳 (最大 140)");
                ImGui.Unindent();
            }
        }
        
        
        public static void DrawBuyAfterEachCollectBox()
            => DrawCheckbox("每次缴纳后购买工票商店物品",
                "缴纳收藏品后自动购买工票商店物品",
                GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect,
                b => GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect = b);
        
        public static void DrawScripReserveAmount()
        {
            var reserveAmount = GatherBuddy.Config.CollectableConfig.ReserveScripAmount;
            ImGui.SetNextItemWidth(150);
            if (ImGui.DragInt("Reserve Scrips", ref reserveAmount, 10, 0, 4000))
            {
                GatherBuddy.Config.CollectableConfig.ReserveScripAmount = Math.Max(0, Math.Min(4000, reserveAmount));
                GatherBuddy.Config.Save();
            }
            ImGuiUtil.HoverTooltip("从工票商店购买物品时保留的最少工票数量\n如果购买会导致工票低于此数量, 则停止购买");
        }
        
        public static void DrawScripShopItemManager()
        {
            var shopItems = ScripShopItemManager.ShopItems;
            var purchaseList = GatherBuddy.Config.CollectableConfig.ScripShopItems;
            
            ImGui.TextUnformatted("购买队列中的物品:");
            ImGui.Spacing();
            
            if (purchaseList.Count == 0)
            {
                ImGui.TextDisabled("队列中无物品, 请在下方添加物品");
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
                    ImGui.SetNextItemWidth(80);
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
                ImGui.TextDisabled("没有可用的工票商店物品, 数据可能尚未加载");
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
                        if (item.Page < 3)
                            continue;
                        
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
            ImGui.TextUnformatted("Manual Preset Generator");
            ImGui.Spacing();
            
            var availableFish = GatherBuddy.GameData.Fishes.Values.Where(f => !f.IsSpearFish).ToList();
            
            ImGui.TextUnformatted("选择目标鱼:");
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
                ImGuiUtil.HoverTooltip("预设名称应匹配鱼的物品 ID, GBR 才能自动使用");
                
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
            
            var success = AutoHookIntegration.AutoHookService.ExportPresetToAutoHook(presetName, [fish], _base.MatchConfigPreset(fish));
            
            if (success)
            {
                if (fish.Predators.Length > 0 && fish.Predators.All(p => !p.Item1.IsSpearFish))
                {
                    Dalamud.Chat.Print($"[GatherBuddy] 为 {fish.Name[GatherBuddy.Language]} 生成了 2 个预设: '{presetName}_Predators' 和 '{presetName}_Target'");
                }
                else
                {
                    Dalamud.Chat.Print($"[GatherBuddy] 为 {fish.Name[GatherBuddy.Language]} 生成了预设 '{presetName}'");
                }
            }
            else
            {
                Dalamud.Chat.PrintError($"[GatherBuddy] 为 {fish.Name[GatherBuddy.Language]} 生成预设失败");
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
            new("选择坐骑",                                   AutoGatherUI.DrawMountSelector),
            new("上坐骑距离",                              ConfigFunctions.DrawMountUpDistance),
            new("落地距离",                               ConfigFunctions.DrawLandingDistance),
            new("上坐骑时同时移动",                         ConfigFunctions.DrawMoveWhileMounting),
            new("采集完成时播放声音 播放音量",
                layout =>
                {
                    ConfigFunctions.DrawHonkModeBox();
                    if (GatherBuddy.Config.AutoGatherConfig.HonkMode)
                        layout.Child.Draw(ConfigFunctions.DrawHonkVolumeSlider);
                }),
            new("检查雇员物品栏",                     ConfigFunctions.DrawCheckRetainersBox),
            new("传送到下一个限时物品",                    ConfigFunctions.DrawTeleportToNextNodeBox),
            new("采集完成后回家 闲置时回家",            ConfigFunctions.DrawGoHomeBox),
            new("The Giving Land 冷却完毕时采集任意水晶", ConfigFunctions.DrawUseGivingLandOnCooldown),
            new("对备用物品使用技能",                  ConfigFunctions.DrawUseSkillsForFallabckBox),
            new("舍弃没有所需物品的节点",             ConfigFunctions.DrawAbandonNodesBox),
            new("始终采集可用宝图",              ConfigFunctions.DrawAlwaysMapsBox),
        ]),
        new("Auto-Gather", "钓鱼",
        [
            new("使用已有 AutoHook 预设",                  ConfigFunctions.DrawUseExistingAutoHookPresetsBox),
            new("最大渔场停留分钟数",                       ConfigFunctions.DrawFishingSpotMinutes),
            new("同意钓鱼数据收集",              ConfigFunctions.DrawFishCollectionBox),
            new("自动收藏品",                              ConfigFunctions.DrawAutoCollectablesFishingBox),
            new("在有钓鱼增益时延迟修理",             ConfigFunctions.DrawDeferRepairDuringFishingBuffsBox),
            new("在有钓鱼增益时延迟精炼减缩", ConfigFunctions.DrawDeferReductionDuringFishingBuffsBox),
            new("在有钓鱼增益时延迟魔晶石精制",  ConfigFunctions.DrawDeferMateriaExtractionDuringFishingBuffsBox),
            new("在 AutoHook 预设中使用咬钩计时器",            ConfigFunctions.DrawUseHookTimersBox),
            new("Manual Preset Generator",                        ConfigFunctions.DrawManualPresetGenerator),
        ]),
        new("Auto-Gather", "高级",
        [
            new("需要时修理装备 修理阈值",
                layout =>
                {
                    ConfigFunctions.DrawRepairBox();
                    if (GatherBuddy.Config.AutoGatherConfig.DoRepair)
                        layout.Child.Draw(ConfigFunctions.DrawRepairThreshold);
                }),
            new("启用魔晶石精制",                      ConfigFunctions.DrawMaterialExtraction),
            new("启用精炼减缩 始终精炼减缩所有物品",
                layout =>
                {
                    ConfigFunctions.DrawAetherialReduction();
                    if (GatherBuddy.Config.AutoGatherConfig.DoReduce)
                        layout.Child.Draw(ConfigFunctions.DrawAlwaysReduceAllItemsBox);
                }),
            new("等待 AutoRetainer 多模式 AutoRetainer 阈值 为限时节点延迟 AutoRetainer",
                layout =>
                {
                    ConfigFunctions.DrawAutoretainerBox();
                    if (GatherBuddy.Config.AutoGatherConfig.AutoRetainerMultiMode)
                    {
                        layout.Child.Draw(ConfigFunctions.DrawAutoretainerThreshold);
                        layout.Child.Draw(ConfigFunctions.DrawAutoretainerTimedNodeDelayBox);
                    }
                }),
            new("云冠群岛自动以太炮",                       ConfigFunctions.DrawDiademAutoAetherCannonBox),
            new("云冠群岛风车跳跃",                          ConfigFunctions.DrawDiademWindmireJumps),
            new("重新进入云冠群岛以重置云冠节点",     ConfigFunctions.DrawDiademFarmCloudedNodes),
            new("物品排序方法",                            ConfigFunctions.DrawSortingMethodCombo),
            new("Lifestream 命令",                             ConfigFunctions.DrawLifestreamCommandTextInput),
            new("防卡死冷却",                            ConfigFunctions.DrawAntiStuckCooldown),
            new("卡死阈值",                                ConfigFunctions.DrawStuckThreshold),
            new("限时节点预知",                        ConfigFunctions.DrawTimedNodePrecog),
            new("执行延迟 毫秒",                   ConfigFunctions.DrawExecutionDelay),
            new("启用采集窗口交互",            ConfigFunctions.DrawAutoGatherBox),
            new("禁用地图标记导航",                  ConfigFunctions.DrawUseFlagBox),
            new("使用 vnavmesh 导航",                        ConfigFunctions.DrawUseNavigationBox),
            new("强制步行",                                  ConfigFunctions.DrawForceWalkingBox),
            new("禁用随机落点",               ConfigFunctions.DrawDisableRandomLandingPositionsBox),
        ]),
        new("Auto-Gather", "收藏品",
        [
            new("自动缴纳收藏品 收藏品数量阈值 物品栏已满阈值",
                layout =>
                {
                    ConfigFunctions.DrawCollectableAutoTurninBox();
                    if (GatherBuddy.Config.CollectableConfig.AutoTurnInCollectables)
                        layout.Child.Draw(ConfigFunctions.DrawCollectableThreshold);
                }),
            new("每次缴纳后购买工票商店物品 保留工票",
                layout =>
                {
                    ConfigFunctions.DrawBuyAfterEachCollectBox();
                    if (GatherBuddy.Config.CollectableConfig.BuyAfterEachCollect)
                        layout.Child.Draw(ConfigFunctions.DrawScripReserveAmount);
                }),
            new("工票商店购买列表",                       ConfigFunctions.DrawScripShopItemManager),
        ]),
        new("General", "采集命令",
        [
            new("偏好职业 无偏好 采矿工 园艺工",     ConfigFunctions.DrawPreferredJobSelect),
            new("启用套装切换",                             ConfigFunctions.DrawGearChangeBox),
            new("启用以太传送",                                ConfigFunctions.DrawTeleportBox),
            new("打开地图标注位置",                         ConfigFunctions.DrawMapOpenBox),
            new("在地图上放置标记",                       ConfigFunctions.DrawPlaceMarkerBox),
            new("放置自定义标记",                          ConfigFunctions.DrawPlaceWaymarkBox),
            new("偏好更便宜的以太之光 偏好更短的旅行时间", ConfigFunctions.DrawAetherytePreference),
            new("跳过近距离传送",                          ConfigFunctions.DrawSkipTeleportBox),
            new("添加游戏内右键菜单",                      ConfigFunctions.DrawContextMenuBox),
        ]),
        new("General", "套装名称",
        [
            new("采矿工套装",    () => ConfigFunctions.DrawSetInput("Miner",    GatherBuddy.Config.MinerSetName,    s => GatherBuddy.Config.MinerSetName    = s)),
            new("园艺工套装", () => ConfigFunctions.DrawSetInput("Botanist", GatherBuddy.Config.BotanistSetName, s => GatherBuddy.Config.BotanistSetName = s)),
            new("捕鱼人套装",   () => ConfigFunctions.DrawSetInput("Fisher",   GatherBuddy.Config.FisherSetName,   s => GatherBuddy.Config.FisherSetName   = s)),
        ]),
        new("General", "闹钟",
        [
            new("启用闹钟",                                  ConfigFunctions.DrawAlarmToggle),
            new("在副本中启用闹钟",                          ConfigFunctions.DrawAlarmsInDutyToggle),
            new("仅在线时启用闹钟",                     ConfigFunctions.DrawAlarmsOnlyWhenLoggedInToggle),
            new("天气变化闹钟",                           ConfigFunctions.DrawWeatherAlarmPicker),
            new("艾欧泽亚时间变化闹钟",                       ConfigFunctions.DrawHourAlarmPicker),
        ]),
        new("General", "消息",
        [
            new("消息聊天类型",                         ConfigFunctions.DrawPrintTypeSelector),
            new("错误聊天类型",                           ConfigFunctions.DrawErrorTypeSelector),
            new("打印地图位置",                             ConfigFunctions.DrawMapMarkerPrintBox),
            new("采集时打印节点出现时间段",                   ConfigFunctions.DrawPrintUptimesBox),
            new("打印剪贴板信息",                    ConfigFunctions.DrawPrintClipboardBox),
            new("闹钟聊天格式",                              ConfigFunctions.DrawAlarmFormatInput),
            new("已识别可采集物品的聊天格式",              ConfigFunctions.DrawIdentifiedGatherableFormatInput),
        ]),
        new("Interface", "配置窗口",
        [
            new("启动时打开配置 UI",                        ConfigFunctions.DrawOpenOnStartBox),
            new("Esc 关闭主窗口",                      ConfigFunctions.DrawRespectEscapeBox),
            new("锁定配置 UI 移动",                        ConfigFunctions.DrawLockPositionBox),
            new("锁定配置 UI 大小",                            ConfigFunctions.DrawLockResizeBox),
            new("在天气标签中显示名称",                      ConfigFunctions.DrawWeatherTabNamesBox),
            new("显示状态行",                               ConfigFunctions.DrawShowStatusLineBox),
            new("隐藏 GatherClippy 按钮",                       ConfigFunctions.DrawHideClippyBox),
            new("打开主界面的热键",                  ConfigFunctions.DrawMainInterfaceHotkeyInput),
        ]),
        new("Interface", "钓鱼计时器",
        [
            new("保留钓鱼记录",                              ConfigFunctions.DrawKeepRecordsBox),
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
        new("Interface", "鱼类统计",
        [
            new("启用鱼类统计",                              ConfigFunctions.DrawEnableFishStats),
            new("报告时复制时间统计",                 ConfigFunctions.DrawEnableReportTime),
            new("报告时复制尺寸统计",                 ConfigFunctions.DrawEnableReportSize),
            new("报告时复制多重提钩统计",           ConfigFunctions.DrawEnableReportMulti),
            new("启用图表",                                  ConfigFunctions.DrawEnableGraphs),
        ]),
        new("Interface", "采集窗口",
        [
            new("显示采集窗口",                             ConfigFunctions.DrawShowGatherWindowBox),
            new("将采集窗口锚定到左下角",            ConfigFunctions.DrawGatherWindowAnchorBox),
            new("显示采集窗口计时器",                      ConfigFunctions.DrawGatherWindowTimersBox),
            new("在采集窗口中显示活动闹钟",            ConfigFunctions.DrawGatherWindowAlarmsBox),
            new("按出现时间排序采集窗口",                   ConfigFunctions.DrawSortGatherWindowBox),
            new("仅显示可用物品",                      ConfigFunctions.DrawGatherWindowShowOnlyAvailableBox),
            new("隐藏已完成物品",                           ConfigFunctions.DrawHideGatherWindowCompletedItemsBox),
            new("在副本中隐藏采集窗口",                     ConfigFunctions.DrawHideGatherWindowInDutyBox),
            new("仅按住按键时显示采集窗口",                         ConfigFunctions.DrawGatherWindowHoldKey),
            new("锁定采集窗口位置",                    ConfigFunctions.DrawGatherWindowLockBox),
            new("打开采集窗口的热键",                   ConfigFunctions.DrawGatherWindowHotkeyInput),
            new("右键删除物品的修饰键",        ConfigFunctions.DrawGatherWindowDeleteModifierInput),
        ]),
        new("Interface", "刺鱼",
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

        if (Widget.PaletteColorPicker("聊天中的名称",         Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorNames,
                Configuration.DefaultSeColorNames,    Configuration.ForegroundColors, out var idx))
            GatherBuddy.Config.SeColorNames = idx;
        if (Widget.PaletteColorPicker("聊天中的命令",      Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorCommands,
                Configuration.DefaultSeColorCommands, Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorCommands = idx;
        if (Widget.PaletteColorPicker("聊天中的参数",     Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorArguments,
                Configuration.DefaultSeColorArguments, Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorArguments = idx;
        if (Widget.PaletteColorPicker("聊天中的闹钟消息", Vector2.One * ImGui.GetFrameHeight(), GatherBuddy.Config.SeColorAlarm,
                Configuration.DefaultSeColorAlarm,    Configuration.ForegroundColors, out idx))
            GatherBuddy.Config.SeColorAlarm = idx;
    }


    private void DrawConfigTab()
    {
        using var id  = ImRaii.PushId("Config");
        using var tab = ImRaii.TabItem("Config");
        ImGuiUtil.HoverTooltip("按照细致的要求设置专属 GatherBuddy\n"
          + "如果对他好, 他甚至可能成为真正的小伙伴");

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

