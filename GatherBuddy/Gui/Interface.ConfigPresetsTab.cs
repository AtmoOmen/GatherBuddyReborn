using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using GatherBuddy.AutoGather;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using GatherBuddy.RebornUI.Settings;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using static GatherBuddy.AutoGather.AutoGather;

namespace GatherBuddy.Gui
{
    public partial class Interface
    {
        private static readonly (string name, uint id)[] CrystalTypes =
            [("火之水晶", 2), ("冰之水晶", 8), ("风之水晶", 14)];

        private readonly ConfigPresetsSelector _configPresetsSelector = new();

        public IReadOnlyCollection<ConfigPreset> GatherActionsPresets
            => _configPresetsSelector.Presets;

        public class ConfigPresetsSelector : ItemSelector<ConfigPreset>
        {
            private const string FileName = "actions.json";

            public ConfigPresetsSelector()
                : base([], Flags.All ^ Flags.Drop)
            {
                Load();
            }

            public IReadOnlyCollection<ConfigPreset> Presets
                => Items.AsReadOnly();

            protected override bool Filtered(int idx)
                => Filter.Length != 0 && !Items[idx].Name.Contains(Filter, StringComparison.InvariantCultureIgnoreCase);

            protected override bool OnDraw(int idx)
            {
                var isSelected = ListRow(CheckUnnamed(Items[idx].Name), idx == CurrentIdx, !Items[idx].Enabled);

                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && idx != Items.Count - 1)
                {
                    Items[idx].Enabled = !Items[idx].Enabled;
                    Save();
                }

                return isSelected;
            }

            protected override string NameOf(ConfigPreset item)
                => item.Name;

            protected override bool OnDelete(int idx)
            {
                if (idx == Items.Count - 1)
                {
                    return false;
                }

                Items.RemoveAt(idx);
                Save();
                return true;
            }

            protected override bool OnAdd(string name)
            {
                Items.Insert(Items.Count - 1, new()
                {
                    Name = name,
                });
                Save();
                return true;
            }

            protected override bool OnClipboardImport(string name, string data)
            {
                var preset = ConfigPreset.FromBase64String(data);
                if (preset == null)
                {
                    Communicator.PrintError("从剪贴板导入预设失败. 请确认数据有效");
                    return false;
                }

                preset.Name = name;

                Items.Insert(Items.Count - 1, preset);
                Save();
                Communicator.Print($"已成功从剪贴板导入预设: {preset.Name}");
                return true;
            }

            protected override bool OnDuplicate(string name, int idx)
            {
                var preset = Items[idx] with
                {
                    Enabled = false,
                    Name = name
                };
                Items.Insert(Math.Min(idx + 1, Items.Count - 1), preset);
                Save();
                return true;
            }

            protected override bool OnMove(int idx1, int idx2)
            {
                idx2 = Math.Min(idx2, Items.Count - 2);
                if (idx1 >= Items.Count - 1)
                {
                    return false;
                }

                if (idx1 < 0 || idx2 < 0)
                {
                    return false;
                }

                Plugin.Functions.Move(Items, idx1, idx2);
                Save();
                return true;
            }

            public void Save()
            {
                var file = Plugin.Functions.ObtainSaveFile(FileName);
                if (file == null)
                {
                    return;
                }

                try
                {
                    var text = JsonConvert.SerializeObject(Items, Formatting.Indented);
                    File.WriteAllText(file.FullName, text);
                }
                catch (Exception e)
                {
                    GatherBuddy.Log.Error($"序列化配置预设数据时出错:\n{e}");
                }
            }

            private void Load()
            {
                try
                {
                    List<ConfigPreset>? items = null;

                    var file = Plugin.Functions.ObtainSaveFile(FileName);
                    if (file != null && file.Exists)
                    {
                        var text = File.ReadAllText(file.FullName);
                        items = JsonConvert.DeserializeObject<List<ConfigPreset>>(text);
                    }

                    if (items != null && items.Count > 0)
                    {
                        foreach (var item in items)
                        {
                            MigrateHQItemIds(item);
                            Items.Add(item);
                        }

                        Items[Items.Count - 1] = Items[Items.Count - 1].MakeDefault();
                    }
                    else
                    {
                        Items.Add(new ConfigPreset().MakeDefault());
                    }
                }
                catch (Exception ex)
                {
                    GatherBuddy.Log.Error($"加载配置预设时出错，正在创建默认预设: {ex}");
                    Items.Clear();
                    try
                    {
                        Items.Add(new ConfigPreset().MakeDefault());
                    }
                    catch (Exception fallbackEx)
                    {
                        GatherBuddy.Log.Error($"创建默认预设时发生严重错误: {fallbackEx}");
                    }
                }
            }

            private static void MigrateHQItemIds(ConfigPreset preset)
            {
                MigrateConsumableItemId(preset.Consumables.Cordial);
                MigrateConsumableItemId(preset.Consumables.Food);
                MigrateConsumableItemId(preset.Consumables.Potion);
                MigrateConsumableItemId(preset.Consumables.Manual);
                MigrateConsumableItemId(preset.Consumables.SquadronManual);
                MigrateConsumableItemId(preset.Consumables.SquadronPass);
            }

            private static void MigrateConsumableItemId(ConfigPreset.ActionConfigConsumable consumable)
            {
                if (consumable.ItemId >= 100_000 && consumable.ItemId < 1_000_000)
                {
                    consumable.ItemId = consumable.ItemId - 100_000 + 1_000_000;
                }
            }

            public ConfigPreset Match(IGatherable? item)
            {
                var defaultPreset = Items[Items.Count - 1];
                if (item == null)
                {
                    return defaultPreset;
                }

                for (var i = 0; i < Items.Count - 1; i++)
                {
                    if (Items[i].Match(item))
                    {
                        return Items[i];
                    }
                }

                return defaultPreset;
            }
        }

        public ConfigPreset MatchConfigPreset(IGatherable? item)
            => _configPresetsSelector.Match(item);

        private const string PresetCheckerDialogId = "Config Presets Checker###gbr_preset_checker";

        private static readonly M3Segment[] LevelKindSegments =
        [
            new("物品等级", Tooltip: "采集笔记和采集窗口显示的物品等级。"),
            new("采集等级", Tooltip: "游戏内部的采集等级（GLV），可区分不同档次的传说采集点。"),
        ];

        private bool _presetsDirty;

        private readonly Dictionary<string, (DateTime Built, List<(string Name, uint RowId)> Items)> _consumableChoices = [];

        private void MarkPresetsDirty()
            => _presetsDirty = true;

        internal void FlushConfigPresets()
        {
            if (!_presetsDirty)
            {
                return;
            }

            _presetsDirty = false;
            _configPresetsSelector.Save();
        }

        public void DrawConfigPresetsPage()
        {
            if (_presetsDirty && !ImGui.IsAnyItemActive())
            {
                FlushConfigPresets();
            }

            var selector = _configPresetsSelector;
            using (VulcanUiStyle.PushTheme())
            {
                selector.Draw(SelectorWidth);
            }

            ImGui.SameLine();
            using var details = ImRaii.Child("##config_preset_details", -Vector2.One, false);
            if (!details)
            {
                return;
            }

            var preset = selector.EnsureCurrent()!;
            var isDefault = selector.CurrentIdx == selector.Presets.Count - 1;
            using var id = ImRaii.PushId(selector.CurrentIdx);

            DrawPresetOverview(preset, isDefault);
            if (!isDefault)
            {
                DrawPresetFilters(preset);
            }

            DrawPresetGeneral(preset);
            DrawPresetActions(preset);
            DrawPresetConsumables(preset);
            DrawPresetChecker();
        }

        private void DrawPresetOverview(ConfigPreset preset, bool isDefault)
        {
            using var card = M3Card.Begin("preset_overview", isDefault ? "默认预设" : CheckUnnamed(preset.Name), FontAwesomeIcon.SlidersH,
                M3.AccentFor(0), M3CardStyle.Filled,
                "按顺序检查预设，只使用第一个匹配当前采集物品的预设。"
              + "默认预设始终排在最后，适用于未匹配其他预设的物品。");

            if (!isDefault)
            {
                var name = preset.Name;
                if (M3Widgets.RowText("名称###Name", ref name, "预设名称", null, 64))
                {
                    preset.Name = name;
                    MarkPresetsDirty();
                }

                var enabled = preset.Enabled;
                if (M3Widgets.RowSwitch("启用###Enabled", ref enabled, "跳过已禁用的预设。也可以双击列表中的预设切换启用状态。"))
                {
                    preset.Enabled = enabled;
                    MarkPresetsDirty();
                }
            }

            if (M3Widgets.RowButton("分享此预设###Share this preset", "复制", "将预设复制到剪贴板，可通过列表的导入按钮导入。",
                    M3ButtonStyle.Tonal, FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(preset.ToBase64String());
                Communicator.Print($"已将预设「{preset.Name}」复制到剪贴板。");
                M3Snackbar.Show($"已将预设「{CheckUnnamed(preset.Name)}」复制到剪贴板。", severity: M3Severity.Success);
            }

            if (M3Widgets.RowButton("检查物品使用的预设###Check which preset each item uses", "检查",
                    "显示已启用的自动采集清单中的所有物品及水晶，并列出各自匹配的预设。", M3ButtonStyle.Tonal,
                    FontAwesomeIcon.Tasks))
            {
                M3Dialog.Open(PresetCheckerDialogId);
            }
        }

        private void DrawPresetFilters(ConfigPreset preset)
        {
            using var card = M3Card.Begin("preset_filters", "适用范围", FontAwesomeIcon.Filter, M3.AccentFor(1), M3CardStyle.Filled,
                "此预设适用的物品。");

            var kind = preset.ItemLevel.UseGlv ? 1 : 0;
            if (M3Widgets.RowSegmented("等级类型", ref kind, LevelKindSegments))
            {
                SwitchLevelKind(preset, kind == 1);
            }

            var low = preset.ItemLevel.Min;
            var high = preset.ItemLevel.Max;
            var useGlv = preset.ItemLevel.UseGlv;
            if (M3Widgets.RowIntRange(useGlv ? "采集等级范围" : "物品等级范围", ref low, ref high, 1, useGlv ? ConfigPreset.MaxGvl : ConfigPreset.MaxLevel,
                    supporting: "此预设适用的最低和最高等级。"))
            {
                preset.ItemLevel.Min = low;
                preset.ItemLevel.Max = high;
                MarkPresetsDirty();
            }

            var nodes = preset.NodeType;
            DrawChipGroup("preset_nodes", "采集点类型", null,
                ("常规", nodes.Regular, v => nodes.Regular = v),
                ("未知", nodes.Unspoiled, v => nodes.Unspoiled = v),
                ("传说", nodes.Legendary, v => nodes.Legendary = v),
                ("限时", nodes.Ephemeral, v => nodes.Ephemeral = v),
                ("云雾", nodes.Clouded, v => nodes.Clouded = v));

            var items = preset.ItemType;
            DrawChipGroup("preset_items", "物品类型", "选择鱼类时会关闭自动选择技能。",
                ("水晶", items.Crystals, v => items.Crystals = v),
                ("收藏品", items.Collectables, v => items.Collectables = v),
                ("普通采集品", items.Other, v => items.Other = v),
                ("鱼类", items.Fish, v =>
                {
                    items.Fish = v;
                    if (v)
                    {
                        preset.ChooseBestActionsAutomatically = false;
                    }
                }
            ));
        }

        private void SwitchLevelKind(ConfigPreset preset, bool useGlv)
        {
            if (useGlv == preset.ItemLevel.UseGlv)
            {
                return;
            }

            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var item in GatherBuddy.GameData.Gatherables.Values)
            {
                var glv = (int)item.GatheringData.GatheringItemLevel.RowId;
                var (oldLevel, newLevel) = useGlv ? (item.Level, glv) : (glv, item.Level);
                if (oldLevel >= preset.ItemLevel.Min && newLevel < min)
                {
                    min = newLevel;
                }

                if (oldLevel <= preset.ItemLevel.Max && newLevel > max)
                {
                    max = newLevel;
                }
            }

            if (min == int.MaxValue)
            {
                min = useGlv ? ConfigPreset.MaxGvl : ConfigPreset.MaxLevel;
            }

            if (max == int.MinValue)
            {
                max = 1;
            }

            preset.ItemLevel.UseGlv = useGlv;
            preset.ItemLevel.Min = min;
            preset.ItemLevel.Max = max;
            MarkPresetsDirty();
        }

        private void DrawChipGroup(string id, string label, string? supporting, params (string Label, bool Value, System.Action<bool> Set)[] chips)
        {
            var row = M3SettingRow.Begin(label, supporting, Vector2.Zero);
            M3SettingRow.End(row);

            var indent = M3Style.Spacing(12f, 8f);
            ImGui.Indent(indent);
            var flow = new M3Flow(M3.Space1, MathF.Max(1f, ImGui.GetContentRegionAvail().X - M3Card.RightInset));
            foreach (var (chipLabel, value, set) in chips)
            {
                var icon = value ? FontAwesomeIcon.Check : FontAwesomeIcon.None;
                flow.Next(M3Widgets.ChipWidth(chipLabel, icon));
                if (M3Widgets.Chip($"##{id}_{chipLabel}", chipLabel, value, icon))
                {
                    set(!value);
                    MarkPresetsDirty();
                }
            }

            ImGui.Unindent(indent);
            ImGui.Dummy(new Vector2(0f, M3.Space2));
        }

        private void DrawPresetGeneral(ConfigPreset preset)
        {
            var types = preset.ItemType;
            var showGeneralSettings = !types.Fish || types.Crystals || types.Other || types.Collectables;
            if (!showGeneralSettings)
            {
                return;
            }

            using var card = M3Card.Begin("preset_general", "常规", FontAwesomeIcon.Cog, M3.AccentFor(2));
            if (types.Crystals || types.Other)
            {
                var gp = preset.GatherableMinGP;
                if (M3Widgets.RowNumber("普通采集品和水晶所需的最低采集力", ref gp, 10, 0, ConfigPreset.MaxGP, "点"))
                {
                    preset.GatherableMinGP = gp;
                    MarkPresetsDirty();
                }
            }

            if (types.Collectables)
            {
                var gp = preset.CollectableMinGP;
                if (M3Widgets.RowNumber("收藏品所需的最低采集力", ref gp, 10, 0, ConfigPreset.MaxGP, "点"))
                {
                    preset.CollectableMinGP = gp;
                    MarkPresetsDirty();
                }

                gp = preset.CollectableActionsMinGP;
                if (M3Widgets.RowNumber("收藏品采集技能所需的最低采集力", ref gp, 10, 0, ConfigPreset.MaxGP, "点"))
                {
                    preset.CollectableActionsMinGP = gp;
                    MarkPresetsDirty();
                }

                var solidAge = ConcatNames(Actions.SolidAge);
                var alwaysSolidAge = preset.CollectableAlwaysUseSolidAge;
                if (M3Widgets.RowSwitch($"始终使用 {solidAge}", ref alwaysSolidAge,
                        $"达到目标收藏价值时，即使初始采集力未达到设定值，也尝试使用 {solidAge}。"))
                {
                    preset.CollectableAlwaysUseSolidAge = alwaysSolidAge;
                    MarkPresetsDirty();
                }

                var manual = preset.CollectableManualScores;
                if (M3Widgets.RowSwitch("手动设置收藏价值###Set collectability scores manually", ref manual,
                        "关闭后从游戏读取收藏价值；开启后可手动填写目标值和最低值。"))
                {
                    preset.CollectableManualScores = manual;
                    MarkPresetsDirty();
                }

                if (preset.CollectableManualScores)
                {
                    using var sub = M3SubGroup.Begin();
                    var score = preset.CollectableTagetScore;
                    if (M3Widgets.RowNumber("目标收藏价值", ref score, 10, 0, ConfigPreset.MaxCollectability, null,
                            "将收藏价值提高到这个数值后再采集。"))
                    {
                        preset.CollectableTagetScore = score;
                        MarkPresetsDirty();
                    }

                    score = preset.CollectableMinScore;
                    if (M3Widgets.RowNumber("最后一次采集的最低收藏价值", ref score, 10, 0, ConfigPreset.MaxCollectability, null,
                            $"只剩最后一次采集机会时，达到此收藏价值即可采集。设为 {ConfigPreset.MaxCollectability} 时关闭此规则。"))
                    {
                        preset.CollectableMinScore = score;
                        MarkPresetsDirty();
                    }
                }
            }

            var isFishExclusive = types.Fish && !types.Crystals && !types.Other && !types.Collectables;
            if (!isFishExclusive)
            {
                var automatic = preset.ChooseBestActionsAutomatically;
                if (M3Widgets.RowSwitch("自动决定使用的技能", ref automatic,
                        "收藏品使用包含全部可用技能的常规收藏品流程。未知和传说采集点优先提高总获得数，常规采集点优先提高每点采集力带来的获得数。"))
                {
                    preset.ChooseBestActionsAutomatically = automatic;
                    MarkPresetsDirty();
                }
            }

            if (!types.Fish && preset.ChooseBestActionsAutomatically && preset.NodeType.Regular)
            {
                var bestOnly = preset.SpendGPOnBestNodesOnly;
                if (M3Widgets.RowSwitch("优先为加成效率最高的采集点保留采集力", ref bestOnly,
                        "仅对常规采集点生效。优先保留采集力给加成效率最高的采集点。请确认该区域有采集次数 +2、获得数 +3 和额外采集奖励发生率 +100% 的隐藏加成，且角色符合触发条件。"
                      + $"当 {ConcatNames(Actions.Bountiful)} 能让获得数 +3 时跳过此规则。解锁「再次发现」（91 级）后建议关闭此项。"))
                {
                    preset.SpendGPOnBestNodesOnly = bestOnly;
                    MarkPresetsDirty();
                }
            }
        }

        private void DrawPresetActions(ConfigPreset preset)
        {
            var types = preset.ItemType;
            if ((types.Crystals || types.Other) && !preset.ChooseBestActionsAutomatically)
            {
                using var card = M3Card.Begin("preset_gathering_actions", "采集技能", FontAwesomeIcon.Leaf, M3.AccentFor(0),
                    M3CardStyle.Filled, "启用技能后可设置其使用时机。");
                var actions = preset.GatherableActions;
                DrawActionRow(ConcatNames(Actions.Luck), actions.Luck);
                DrawActionRow(ConcatNames(Actions.Bountiful), actions.Bountiful);
                DrawActionRow(ConcatNames(Actions.Yield1), actions.Yield1);
                DrawActionRow(ConcatNames(Actions.Yield2), actions.Yield2);
                DrawActionRow(ConcatNames(Actions.SolidAge), actions.SolidAge);
                DrawActionRow(ConcatNames(Actions.Gift1), actions.Gift1);
                DrawActionRow(ConcatNames(Actions.Gift2), actions.Gift2);
                DrawActionRow(ConcatNames(Actions.Tidings), actions.Tidings);
                if (types.Crystals)
                {
                    DrawActionRow(Actions.TwelvesBounty.Names.Botanist, actions.TwelvesBounty);
                    DrawActionRow(Actions.GivingLand.Names.Botanist, actions.GivingLand);
                }
            }

            if (types.Collectables && !preset.ChooseBestActionsAutomatically)
            {
                using var card = M3Card.Begin("preset_collectable_actions", "收藏品采集技能", FontAwesomeIcon.Gem, M3.AccentFor(1),
                    M3CardStyle.Filled, "启用技能后可设置其使用时机。");
                var actions = preset.CollectableActions;
                DrawActionRow(Actions.Scour.Names.Botanist, actions.Scour);
                DrawActionRow(Actions.Brazen.Names.Botanist, actions.Brazen);
                DrawActionRow(Actions.Meticulous.Names.Botanist, actions.Meticulous);
                DrawActionRow(Actions.Scrutiny.Names.Botanist, actions.Scrutiny);
                DrawActionRow(ConcatNames(Actions.SolidAge), actions.SolidAge);
            }

            if (types.Fish)
            {
                using var card = M3Card.Begin("preset_fishing_actions", "钓鱼技能", FontAwesomeIcon.Fish, M3.AccentFor(2),
                    M3CardStyle.Filled, "启用技能后可设置其使用时机。");
                var actions = preset.FishingActions;
                DrawToggleRow($"{Actions.Patience.Name} / {Actions.PatienceII.Name}", actions.Patience);
                DrawFishingActionRow(Actions.DoubleHook.Name, actions.DoubleHook, actions.TripleHook);
                DrawFishingActionRow(Actions.TripleHook.Name, actions.TripleHook, actions.DoubleHook);
                DrawFishingActionRow(Actions.PrizeCatch.Name, actions.PrizeCatch);
                DrawFishingActionRow(Actions.Chum.Name, actions.Chum);
                DrawFishingActionRow(Actions.SurfaceSlap.Name, actions.SurfaceSlap);
                DrawFishingActionRow(Actions.IdenticalCast.Name, actions.IdenticalCast);
                DrawFishingActionRow(Actions.AmbitiousLure.Name, actions.AmbitiousLure);
                DrawFishingActionRow(Actions.ModestLure.Name, actions.ModestLure);
            }
        }

        private void DrawPresetConsumables(ConfigPreset preset)
        {
            using var card = M3Card.Begin("preset_consumables", "消耗品", FontAwesomeIcon.Flask, M3.AccentFor(0), M3CardStyle.Filled,
                "优先列出背包中的物品，并显示持有数量。");
            var consumables = preset.Consumables;
            DrawActionRow("强心剂", consumables.Cordial, PossibleCordials);
            DrawActionRow("食物", consumables.Food, PossibleFoods, true);
            DrawActionRow("药品", consumables.Potion, PossiblePotions, true);
            DrawActionRow("生存学指南", consumables.Manual, PossibleManuals, true);
            DrawActionRow("军用及民用指南", consumables.SquadronManual, PossibleSquadronManuals, true);
            DrawActionRow("传送网使用优惠券", consumables.SquadronPass, PossibleSquadronPasses, true);
        }

        private void DrawActionRow(string name, ConfigPreset.ActionConfig action, IEnumerable<Item>? items = null, bool hideGP = false)
        {
            using var id = ImRaii.PushId(name);
            var enabled = action.Enabled;
            if (M3Widgets.RowSwitch(name, ref enabled))
            {
                action.Enabled = enabled;
                MarkPresetsDirty();
            }

            if (!action.Enabled)
            {
                return;
            }

            using var sub = M3SubGroup.Begin();
            if (action is ConfigPreset.ActionConfigIntegrity firstStep)
            {
                var firstStepOnly = firstStep.FirstStepOnly;
                if (M3Widgets.RowSwitch("仅在首次采集前使用", ref firstStepOnly, "只在本采集点尚未采集过时使用此技能。"))
                {
                    firstStep.FirstStepOnly = firstStepOnly;
                    MarkPresetsDirty();
                }
            }

            if (!hideGP)
            {
                var low = action.MinGP;
                var high = action.MaxGP;
                if (M3Widgets.RowIntRange("采集力范围", ref low, ref high, 0, ConfigPreset.MaxGP, supporting: "当前采集力在此范围内时使用。"))
                {
                    action.MinGP = low;
                    action.MaxGP = high;
                    MarkPresetsDirty();
                }
            }

            if (action is ConfigPreset.ActionConfigBoon boon)
            {
                var low = boon.MinBoonChance;
                var high = boon.MaxBoonChance;
                if (M3Widgets.RowIntRange("额外采集奖励发生率", ref low, ref high, 0, 100, "%", "额外采集奖励发生率在此范围内时使用。"))
                {
                    boon.MinBoonChance = low;
                    boon.MaxBoonChance = high;
                    MarkPresetsDirty();
                }
            }

            if (action is ConfigPreset.ActionConfigIntegrity integrity)
            {
                var minIntegrity = integrity.MinIntegrity;
                if (M3Widgets.RowNumber("最低初始采集完整性", ref minIntegrity, 1, 1, ConfigPreset.MaxIntegrity))
                {
                    integrity.MinIntegrity = minIntegrity;
                    MarkPresetsDirty();
                }
            }

            if (action is ConfigPreset.ActionConfigYieldBonus yieldBonus)
            {
                var minYieldBonus = yieldBonus.MinYieldBonus;
                if (M3Widgets.RowNumber("最低收获量加成", ref minYieldBonus, 1, 1, 3))
                {
                    yieldBonus.MinYieldBonus = minYieldBonus;
                    MarkPresetsDirty();
                }
            }

            if (action is ConfigPreset.ActionConfigYieldTotal yieldTotal)
            {
                var minYieldTotal = yieldTotal.MinYieldTotal;
                if (M3Widgets.RowNumber("最低总收获量", ref minYieldTotal, 1, 1, 30))
                {
                    yieldTotal.MinYieldTotal = minYieldTotal;
                    MarkPresetsDirty();
                }
            }

            if (action is ConfigPreset.ActionConfigConsumable consumable && items != null)
            {
                var choices = ConsumableChoices(name, items);
                var index = Math.Max(0, choices.FindIndex(c => c.RowId == consumable.ItemId));
                if (M3Widgets.RowCombo("物品", ref index, choices.Count, i => choices[i].Name, null, search: true)
                 && index >= 0 && index < choices.Count && choices[index].RowId != consumable.ItemId)
                {
                    consumable.ItemId = choices[index].RowId;
                    MarkPresetsDirty();
                }
            }
        }

        private void DrawToggleRow(string name, ConfigPreset.ToggleConfig action)
        {
            using var id = ImRaii.PushId(name);
            var enabled = action.Enabled;
            if (M3Widgets.RowSwitch(name, ref enabled))
            {
                action.Enabled = enabled;
                MarkPresetsDirty();
            }
        }

        private void DrawFishingActionRow(string name, ConfigPreset.FishingActionConfig action, ConfigPreset.FishingActionConfig? oppositeAction = null)
        {
            using var id = ImRaii.PushId(name);
            var enabled = action.Enabled;
            var supporting = oppositeAction != null ? "双重提钩和三重提钩互斥，开启其中一个会关闭另一个。" : null;
            if (M3Widgets.RowSwitch(name, ref enabled, supporting))
            {
                action.Enabled = enabled;
                if (enabled && oppositeAction != null)
                {
                    oppositeAction.Enabled = false;
                }

                MarkPresetsDirty();
            }

            if (!action.Enabled)
            {
                return;
            }

            using var sub = M3SubGroup.Begin();
            var above = action.GpThresholdAbove ? 0 : 1;
            if (M3Widgets.RowSegmented("采集力使用条件", ref above, SettingRows.AboveBelowSegments))
            {
                action.GpThresholdAbove = above == 0;
                MarkPresetsDirty();
            }

            var threshold = action.GpThreshold;
            if (M3Widgets.RowNumber("采集力阈值", ref threshold, 10, 0, ConfigPreset.MaxGP, "点"))
            {
                action.GpThreshold = threshold;
                MarkPresetsDirty();
            }
        }

        private List<(string Name, uint RowId)> ConsumableChoices(string key, IEnumerable<Item> items)
        {
            if (_consumableChoices.TryGetValue(key, out var cached) && (DateTime.UtcNow - cached.Built).TotalSeconds < 2)
            {
                return cached.Items;
            }

            var entries = new List<(string Name, uint RowId, int Count, int Order)>();
            foreach (var item in items)
            {
                AddConsumableChoice(entries, item.RowId);
                if (item.CanBeHq)
                {
                    AddConsumableChoice(entries, item.RowId + 1_000_000);
                }
            }

            entries.Sort(static (a, b) =>
            {
                var order = (a.Count == 0).CompareTo(b.Count == 0);
                if (order != 0)
                {
                    return order;
                }

                order = string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
                return order != 0 ? order : a.Order.CompareTo(b.Order);
            });

            var list = new List<(string Name, uint RowId)>(entries.Count + 1) { ("无", 0u) };
            foreach (var (name, rowId, count, _) in entries)
            {
                list.Add(($"{name} ({count})", rowId));
            }

            _consumableChoices[key] = (DateTime.UtcNow, list);
            return list;
        }

        private static void AddConsumableChoice(List<(string Name, uint RowId, int Count, int Order)> entries, uint rowId)
        {
            var name = ItemUtil.GetItemName(rowId, includeIcon: true).ExtractText();
            if (!string.IsNullOrEmpty(name))
            {
                entries.Add((name, rowId, GetInventoryItemCount(rowId), entries.Count));
            }
        }

        private void DrawPresetChecker()
        {
            using var dialog = M3Dialog.Begin(PresetCheckerDialogId, "物品匹配的预设",
                "显示已启用的自动采集清单中的物品及水晶，并列出各自匹配的预设。", FontAwesomeIcon.Tasks, 640f);
            if (!dialog.IsOpen)
            {
                return;
            }

            var rows = new List<(string List, string Name, IGatherable Item)>();
            foreach (var list in _plugin.AutoGatherListsManager.Lists)
            {
                if (!list.Enabled || list.Fallback)
                {
                    continue;
                }

                foreach (var item in list.Items)
                {
                    rows.Add((list.Name, item.Name[GatherBuddy.Language], item));
                }
            }

            foreach (var (name, id) in CrystalTypes)
            {
                if (GatherBuddy.GameData.Gatherables.TryGetValue(id, out var crystal))
                {
                    rows.Add((string.Empty, name, crystal));
                }
            }

            var lineHeight = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().CellPadding.Y * 2f;
            var height = MathF.Min(rows.Count + 1, 14) * lineHeight + ImGui.GetStyle().CellPadding.Y * 2f;
            using (var table = ImRaii.Table("##preset_checker", 3,
                       ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                       new Vector2(0f, height)))
            {
                if (table)
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("采集清单");
                    ImGui.TableSetupColumn("物品");
                    ImGui.TableSetupColumn("配置预设");
                    ImGui.TableHeadersRow();

                    foreach (var (list, name, item) in rows)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(list);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(name);
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(MatchConfigPreset(item).Name);
                    }
                }
            }

            M3Dialog.Actions([new M3DialogAction("关闭", M3ButtonStyle.Filled)]);
        }

        private static string ConcatNames(Actions.BaseAction action)
            => $"{action.Names.Miner} / {action.Names.Botanist}";
    }
}
