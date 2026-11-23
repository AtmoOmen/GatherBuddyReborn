using Dalamud.Interface;
using GatherBuddy.AutoGather;
using GatherBuddy.Classes;
using GatherBuddy.RebornUI.Settings;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;

namespace GatherBuddy.RebornUI;

internal sealed partial class GatherBuddySettings
{
    private static readonly M3PluginRequirement Lifestream = new("Lifestream");
    private static readonly M3PluginRequirement AllaganTools = new("InventoryTools", "Allagan Tools");
    private static readonly M3PluginRequirement AutoHook = new("AutoHook");
    private static readonly M3PluginRequirement AutoRetainer = new("AutoRetainer");

    private List<(uint Id, string Name)> _mounts = [];
    private DateTime _mountsRefreshed = DateTime.MinValue;

    private Fish[]? _presetFish;
    private string[] _presetFishNames = [];
    private int _presetFishIndex = -1;
    private string _presetName = string.Empty;

    private M3SettingsPage BuildAutoGatherPage()
        => new(AutoGatherPageId, "自动采集设置", FontAwesomeIcon.Robot, "自动采集的移动、采集、钓鱼和装备维护方式",
            new M3SettingsGroup("general", "常规", FontAwesomeIcon.Leaf, null,
                Custom("Select mount mount roulette 选择坐骑 随机坐骑", DrawMountSelector),
                Float("上坐骑距离", "到采集点的距离达到此值时，上坐骑移动。",
                    () => AutoCfg.MountUpDistance, v => AutoCfg.MountUpDistance = v, 0.1f, 100f, 0.5f, unit: "y", keywords: "Mount up distance"),
                Float("降落距离",
                    "禁用随机降落位置或没有已记录数据时，在距采集点此固定距离处尝试降落。"
                  + "数值太小可能难以下坐骑，太大可能导致路径异常。"
                  + "建议设为 4 至 8 米。",
                    () => AutoCfg.LandingDistance, v => AutoCfg.LandingDistance = v, 0f, 50f, 0.5f, unit: "y"),
                Toggle("呼叫坐骑时开始移动", "召唤坐骑的同时开始向下一个采集点寻路。",
                    () => AutoCfg.MoveWhileMounting, v => AutoCfg.MoveWhileMounting = v),
                Custom("Play a sound when done gathering honk playback volume 采集完成音效 播放音量", () =>
                {
                    SettingRows.Switch("采集完成时播放音效",
                        "完成列表中的所有目标、结束自动采集时播放音效。",
                        AutoCfg.HonkMode, v => AutoCfg.HonkMode = v);
                    if (!AutoCfg.HonkMode)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.Slider("播放音量", null, AutoCfg.SoundPlaybackVolume, 0, 100, v => AutoCfg.SoundPlaybackVolume = v);
                }),
                PluginToggle("检查雇员背包", "通过 Allagan Tools 将雇员背包中的物品计入库存。",
                    () => AutoCfg.CheckRetainers, v => AutoCfg.CheckRetainers = v, AllaganTools),
                Toggle("传送至下一个限时目标",
                    "无其他目标可采集时，提前传送至即将刷新的限时采集点或渔场，并在以太之光旁等待。"
                  + "此选项优先于空闲时回家。",
                    () => AutoCfg.TeleportToNextNode, v => AutoCfg.TeleportToNextNode = v),
                PluginToggle("采集完成后回家", "采集完成时使用 /li auto 命令回家。",
                    () => AutoCfg.GoHomeWhenDone, v => AutoCfg.GoHomeWhenDone = v, Lifestream),
                PluginToggle("空闲时回家", "等待限时采集点时使用 /li auto 命令回家。",
                    () => AutoCfg.GoHomeWhenIdle, v => AutoCfg.GoHomeWhenIdle = v, Lifestream),
                Toggle("「大地的恩惠」可用时采集任意水晶",
                    "「大地的恩惠」可用时，会在常规采集点采集随机水晶。",
                    () => AutoCfg.UseGivingLandOnCooldown, v => AutoCfg.UseGivingLandOnCooldown = v),
                Toggle("采集后备物品时使用技能", "采集后备预设中的物品时使用技能。",
                    () => AutoCfg.UseSkillsForFallbackItems, v => AutoCfg.UseSkillsForFallbackItems = v),
                Toggle("离开没有所需物品的采集点",
                    "已采够物品，或当前采集点没有所需物品时，停止采集并离开。",
                    () => AutoCfg.AbandonNodes, v => AutoCfg.AbandonNodes = v),
                Toggle("有藏宝图时优先采集", "如果采集点有藏宝图，GBR 会优先采集。",
                    () => AutoCfg.AlwaysGatherMaps, v => AutoCfg.AlwaysGatherMaps = v)),
            new M3SettingsGroup("fishing", "钓鱼", FontAwesomeIcon.Fish, null,
                PluginToggle("使用 AutoHook 全局预设",
                    "清除 AutoHook 当前选择的自定义预设，钓竿钓鱼改用 AutoHook 内置的全局预设。"
                  + "此选项优先于 GBR 生成的预设及按鱼类 ID 命名的 AutoHook 预设。刺鱼仍使用 GBR 生成的 AutoGig 预设。",
                    () => AutoCfg.UseAutoHookGlobalPreset, v => AutoCfg.UseAutoHookGlobalPreset = v, AutoHook),
                PluginToggle("使用现有的 AutoHook 预设",
                    "使用你自己的 AutoHook 预设，代替 GBR 生成的预设。请用鱼的物品 ID 命名，例如 46188。"
                  + "在鱼类页面悬停可查看鱼的物品 ID。启用 AutoHook 全局预设时忽略此选项。"
                  + "不会删除你自己的预设，只会清理 GBR 生成的预设。",
                    () => AutoCfg.UseExistingAutoHookPresets, v => AutoCfg.UseExistingAutoHookPresets = v, AutoHook),
                IntSlider("单个渔场的钓鱼时限", "在单个渔场钓鱼的最大分钟数。",
                    () => AutoCfg.MaxFishingSpotMinutes, v => AutoCfg.MaxFishingSpotMinutes = v, 1, 40),
                Toggle("同意钓鱼数据收集",
                    "每次钓到鱼时会将相关数据上传至远程服务器，用于构建自动钓鱼功能。"
                  + "只收集与所钓鱼类有关的数据，不收集你或角色的个人信息。可随时退出。",
                    () => AutoCfg.FishDataCollection, v => AutoCfg.FishDataCollection = v, "share"),
                Toggle("自动处理收藏品", "根据最低收藏价值自动接受或放弃钓到的收藏品。",
                    () => AutoCfg.AutoCollectablesFishing, v => AutoCfg.AutoCollectablesFishing = v),
                Toggle("钓鱼增益期间推迟修理",
                    "耐心、拍击水面、专一垂钓或大鱼猎手等钓鱼增益生效时，避免 GBR 停下来修理装备。",
                    () => AutoCfg.DeferRepairDuringFishingBuffs, v => AutoCfg.DeferRepairDuringFishingBuffs = v),
                Toggle("钓鱼增益期间推迟精选",
                    "钓鱼增益生效时，避免 GBR 停下来进行精选。",
                    () => AutoCfg.DeferReductionDuringFishingBuffs, v => AutoCfg.DeferReductionDuringFishingBuffs = v),
                Toggle("钓鱼增益期间推迟精制魔晶石",
                    "钓鱼增益生效时，避免 GBR 停下来精制魔晶石。",
                    () => AutoCfg.DeferMateriaExtractionDuringFishingBuffs, v => AutoCfg.DeferMateriaExtractionDuringFishingBuffs = v),
                PluginToggle("AutoHook 预设使用咬钩计时", "在生成的 AutoHook 预设中启用咬钩时间窗口。",
                    () => AutoCfg.UseHookTimers, v => AutoCfg.UseHookTimers = v, AutoHook),
                Custom("Manual preset generator AutoHook preset generate fish 手动生成 AutoHook 钓鱼预设", DrawManualPresetGenerator)),
            new M3SettingsGroup("advanced", "高级", FontAwesomeIcon.Wrench, null,
                Custom("Repair gear when needed repair threshold durability 自动修理 阈值 耐久度", () =>
                {
                    SettingRows.Switch("需要时修理装备", "装备快损坏时进行修理。",
                        AutoCfg.DoRepair, v => AutoCfg.DoRepair = v);
                    if (!AutoCfg.DoRepair)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.Slider("修理阈值", "装备耐久度低于此百分比时修理装备。",
                        AutoCfg.RepairThreshold, 1, 100, v => AutoCfg.RepairThreshold = v);
                }),
                Toggle("启用魔晶石精制", "自动从精炼度已满的装备中精制魔晶石。",
                    () => AutoCfg.DoMaterialize, v => AutoCfg.DoMaterialize = v),
                Custom("Enable aetherial reduction always reduce all items 启用精选 自动精选 所有物品", () =>
                {
                    SettingRows.Switch("启用精选",
                        "空闲或背包空位不足 20 格时自动精选。",
                        AutoCfg.DoReduce, v => AutoCfg.DoReduce = v);
                    if (!AutoCfg.DoReduce)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.Switch("始终精选所有物品",
                        "采集中背包空位不足 20 格时，一次精选所有物品，"
                      + "而不是只精选一种。",
                        AutoCfg.AlwaysReduceAllItems, v => AutoCfg.AlwaysReduceAllItems = v);
                }),
                Custom("Wait for AutoRetainer multi-mode threshold seconds delay timed nodes 等待 AutoRetainer 多角色模式 阈值 秒数 延后 限时点", () =>
                {
                    SettingRows.PluginSwitch("等待 AutoRetainer 多角色模式",
                        "AutoRetainer 的多角色模式有雇员待处理时，自动暂停 GBR。",
                        AutoCfg.AutoRetainerMultiMode, v => AutoCfg.AutoRetainerMultiMode = v, AutoRetainer);
                    if (!AutoCfg.AutoRetainerMultiMode)
                    {
                        return;
                    }

                    using var sub = M3SubGroup.Begin();
                    SettingRows.Number("AutoRetainer 等待阈值",
                        "雇员探险结束前多少秒暂停 GBR，等待多角色模式。",
                        AutoCfg.AutoRetainerMultiModeThreshold, 0, 3600, v => AutoCfg.AutoRetainerMultiModeThreshold = v, 10, "s");
                    SettingRows.Switch("限时采集时推迟 AutoRetainer",
                        "等当前及即将刷新的限时采集点采完后再处理雇员。",
                        AutoCfg.AutoRetainerDelayForTimedNodes, v => AutoCfg.AutoRetainerDelayForTimedNodes = v);
                }),
                Toggle("云冠群岛自动发射以太钻孔机",
                    "炮击量表至少达到 200 时，自动锁定附近敌人并发射以太钻孔机。"
                  + "仅在未寻路时发射，两次发射至少间隔 2 秒。",
                    () => AutoCfg.DiademAutoAetherCannon, v => AutoCfg.DiademAutoAetherCannon = v),
                Toggle("云冠群岛使用风涡跳跃",
                    "如果比正常移动明显更近，允许在云冠群岛利用风涡在各岛屿之间跳跃。",
                    () => AutoCfg.DiademWindmireJumps, v => AutoCfg.DiademWindmireJumps = v),
                Toggle("重新进入云冠群岛以重置云雾节点",
                    "从云雾节点采集属性物品后，重新进入副本以刷新该采集点。",
                    () => AutoCfg.DiademFarmCloudedNodes, v => AutoCfg.DiademFarmCloudedNodes = v),
                Custom("Item sorting method location none 物品排序方式 位置 不排序", () => SettingRows.Combo("物品排序方式",
                    "插件内部采用的物品排序方式。", AutoCfg.SortingMethod, v => AutoCfg.SortingMethod = v)),
                Custom("Lifestream command auto Lifestream 命令 自动", () => SettingRows.Text("Lifestream 命令",
                    "空闲或采集完成时执行的命令。不要包含 /li。GBR 不会验证命令，修改前请确认。",
                    AutoCfg.LifestreamCommand, v => AutoCfg.LifestreamCommand = string.IsNullOrEmpty(v) ? "auto" : v, "auto", 100)),
                Float("防卡重置间隔", "卡住后，到导航系统重置前等待的秒数。",
                    () => AutoCfg.NavResetCooldown, v => AutoCfg.NavResetCooldown = v, 0.1f, 10f, 0.1f, unit: "s"),
                Float("卡住判定阈值", "导航系统判定卡住前等待的秒数。",
                    () => AutoCfg.NavResetThreshold, v => AutoCfg.NavResetThreshold = v, 0.1f, 10f, 0.1f, unit: "s"),
                Int("限时采集点提前判定", "GBR 提前多少秒将一个尚未刷新的限时采集点视为可用。",
                    () => AutoCfg.TimedNodePrecog, v => AutoCfg.TimedNodePrecog = v, 0, 600, 5, "s", "seconds"),
                Int("执行延迟", "延迟指定时间再执行各个操作。",
                    () => (int)AutoCfg.ExecutionDelay, v => AutoCfg.ExecutionDelay = (uint)Math.Clamp(v, 0, 10000), 0, 10000, 50, "ms", "milliseconds"),
                Toggle("启用采集窗口交互",
                    "自动采集物品。关闭后仅导航；不保证关闭后的行为受支持。",
                    () => AutoCfg.DoGathering, v => AutoCfg.DoGathering = v, "nav only mode"),
                Toggle("禁用地图旗标导航", "设置是否使用地图旗标导航，仅对限时采集点生效。",
                    () => AutoCfg.DisableFlagPathing, v => AutoCfg.DisableFlagPathing = v, "flag"),
                Toggle("使用 vnavmesh 导航", "通过 vnavmesh 自动移动角色。",
                    () => AutoCfg.UseNavigation, v => AutoCfg.UseNavigation = v),
                Toggle("强制步行", "前往采集点时强制步行，不使用坐骑。",
                    () => AutoCfg.ForceWalking, v => AutoCfg.ForceWalking = v, keywords: "Force walking"),
                Toggle("禁用随机降落位置",
                    "GBR 会记录玩家采集时的位置，并随机选择一处降落。"
                  + "启用此项后改用固定的降落距离。",
                    () => AutoCfg.DisableRandomLandingPositions, v => AutoCfg.DisableRandomLandingPositions = v, "offsets")));

    private unsafe void RefreshMounts()
    {
        var current = AutoCfg.AutoGatherMountId;
        if (_mounts.Count > 0 && (DateTime.UtcNow - _mountsRefreshed).TotalSeconds < 5 && _mounts.Exists(m => m.Id == current))
        {
            return;
        }

        _mountsRefreshed = DateTime.UtcNow;
        var sheet = Dalamud.GameData.GetExcelSheet<Mount>();
        var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        var mounts = new List<(uint Id, string Name)>();
        foreach (var mount in sheet)
        {
            if (mount.RowId == 0)
            {
                continue;
            }

            var name = mount.Singular.ToString();
            if (name.Length == 0)
            {
                continue;
            }

            var unlocked = playerState != null && playerState->IsMountUnlocked(mount.RowId);
            if (unlocked || mount.RowId == current)
            {
                mounts.Add((mount.RowId, name.ToProperCase()));
            }
        }

        mounts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        mounts.Insert(0, (0, "随机坐骑"));
        _mounts = mounts;
    }

    private void DrawMountSelector()
    {
        RefreshMounts();
        var current = AutoCfg.AutoGatherMountId;
        var index = Math.Max(0, _mounts.FindIndex(m => m.Id == current));
        if (M3Widgets.RowCombo("坐骑###Mount", ref index, _mounts.Count, i => _mounts[i].Name, "自动采集时召唤的坐骑。", search: true)
         && index >= 0 && index < _mounts.Count && _mounts[index].Id != current)
        {
            AutoCfg.AutoGatherMountId = _mounts[index].Id;
            Config.Save();
        }
    }

    private void DrawManualPresetGenerator()
    {
        M3Widgets.SectionLabel("手动生成预设");
        if (_presetFish == null)
        {
            var fishes = new List<Fish>();
            foreach (var candidate in GatherBuddy.GameData.Fishes.Values)
            {
                if (!candidate.IsSpearFish)
                {
                    fishes.Add(candidate);
                }
            }

            fishes.Sort(static (a, b) =>
            {
                var order = string.Compare(a.Name[GatherBuddy.Language], b.Name[GatherBuddy.Language], StringComparison.CurrentCulture);
                return order != 0 ? order : a.ItemId.CompareTo(b.ItemId);
            });
            _presetFish = [.. fishes];
            _presetFishNames = Array.ConvertAll(_presetFish, f => f.Name[GatherBuddy.Language]);
        }

        var index = _presetFishIndex;
        if (M3Widgets.RowCombo("目标鱼###Target fish##preset_fish", ref index, _presetFishNames, "为这条鱼生成 AutoHook 预设。", search: true)
         && index >= 0 && index < _presetFish.Length && index != _presetFishIndex)
        {
            _presetFishIndex = index;
            _presetName = _presetFish[index].ItemId.ToString();
        }

        if (_presetFishIndex < 0 || _presetFishIndex >= _presetFish.Length)
        {
            return;
        }

        var fish = _presetFish[_presetFishIndex];
        M3Widgets.RowText("预设名称###Preset name##preset_name", ref _presetName, fish.ItemId.ToString(),
            "为了让 GBR 自动使用此预设，预设名称应与鱼的物品 ID 一致。", 64);
        if (M3Widgets.RowButton("生成预设###Generate the preset##preset_generate", "生成预设", null, M3ButtonStyle.Filled, FontAwesomeIcon.Magic))
        {
            GenerateManualPreset(fish, _presetName);
        }
    }

    private void GenerateManualPreset(Fish fish, string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            presetName = fish.ItemId.ToString();
        }

        var name = fish.Name[GatherBuddy.Language];
        if (!AutoHookIntegration.AutoHookService.ExportPresetToAutoHook(presetName, [fish], _interface.MatchConfigPreset(fish)))
        {
            Dalamud.Chat.PrintError($"[GatherBuddy] 无法为 {name} 生成预设");
            M3Snackbar.Show($"无法为 {name} 生成预设。", severity: M3Severity.Error);
            return;
        }

        if (HasOnlyRodPredators(fish))
        {
            Dalamud.Chat.Print($"[GatherBuddy] 已为 {name} 生成两个预设：{presetName}_Predators 和 {presetName}_Target");
            M3Snackbar.Show($"已为 {name} 生成 {presetName}_Predators 和 {presetName}_Target。", severity: M3Severity.Success);
        }
        else
        {
            Dalamud.Chat.Print($"[GatherBuddy] 已为 {name} 生成预设 {presetName}");
            M3Snackbar.Show($"已为 {name} 生成预设 {presetName}。", severity: M3Severity.Success);
        }
    }

    private static bool HasOnlyRodPredators(Fish fish)
    {
        if (fish.Predators.Length == 0)
        {
            return false;
        }

        foreach (var (predator, _) in fish.Predators)
        {
            if (predator.IsSpearFish)
            {
                return false;
            }
        }

        return true;
    }
}
