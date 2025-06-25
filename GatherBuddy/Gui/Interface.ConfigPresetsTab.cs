using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using GatherBuddy.AutoGather;
using GatherBuddy.Config;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using OtterGui;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ECommons.ImGuiMethods;
using GatherBuddy.Classes;
using static GatherBuddy.AutoGather.AutoGather;

namespace GatherBuddy.Gui
{
    public partial class Interface
    {
        private static readonly (string name, uint id)[] CrystalTypes =
            [("碎晶", 2), ("水晶", 8), ("晶簇", 14)];

        private readonly ConfigPresetsSelector                _configPresetsSelector = new();
        private          (bool EditingName, bool ChangingMin) _configPresetsUIState;

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
                using var id    = ImRaii.PushId(idx);
                using var color = ImRaii.PushColor(ImGuiCol.Text, ColorId.DisabledText.Value(), !Items[idx].Enabled);
                return ImGui.Selectable(CheckUnnamed(Items[idx].Name), idx == CurrentIdx);
            }

            protected override bool OnDelete(int idx)
            {
                if (idx == Items.Count - 1)
                    return false;

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
                    Notify.Error("从剪贴板加载配置预设失败。确定它是有效的吗？");
                    return false;
                }

                preset.Name = name;

                Items.Insert(Items.Count - 1, preset);
                Save();
                Notify.Success($"已成功从剪贴板导入配置预设 {preset.Name}。");
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
                    return false;
                if (idx1 < 0 || idx2 < 0)
                    return false;

                Plugin.Functions.Move(Items, idx1, idx2);
                Save();
                return true;
            }

            public void Save()
            {
                var file = Plugin.Functions.ObtainSaveFile(FileName);
                if (file == null)
                    return;

                try
                {
                    var text = JsonConvert.SerializeObject(Items, Formatting.Indented);
                    File.WriteAllText(file.FullName, text);
                }
                catch (Exception e)
                {
                    GatherBuddy.Log.Error($"Error serializing config presets data:\n{e}");
                }
            }

            private void Load()
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
                        Items.Add(item);
                    }
                }
                else
                {
                    //Convert old settings to the new Default preset
                    Items.Add(GatherBuddy.Config.AutoGatherConfig.ConvertToPreset());
                    Items[0].ChooseBestActionsAutomatically = true;
                    Save();
                    GatherBuddy.Config.AutoGatherConfig.ConfigConversionFixed        = true;
                    GatherBuddy.Config.AutoGatherConfig.RotationSolverConversionDone = true;
                    GatherBuddy.Config.Save();
                }

                Items[Items.Count - 1] = Items[Items.Count - 1].MakeDefault();

                if (!GatherBuddy.Config.AutoGatherConfig.RotationSolverConversionDone)
                {
                    Items[Items.Count - 1].ChooseBestActionsAutomatically            = true;
                    GatherBuddy.Config.AutoGatherConfig.RotationSolverConversionDone = true;
                    Save();
                    GatherBuddy.Config.Save();
                }

                if (!GatherBuddy.Config.AutoGatherConfig.ConfigConversionFixed)
                {
                    var def = Items[Items.Count - 1];
                    fixAction(def.GatherableActions.Bountiful);
                    fixAction(def.GatherableActions.Yield1);
                    fixAction(def.GatherableActions.Yield2);
                    fixAction(def.GatherableActions.SolidAge);
                    fixAction(def.GatherableActions.TwelvesBounty);
                    fixAction(def.GatherableActions.GivingLand);
                    fixAction(def.GatherableActions.Gift1);
                    fixAction(def.GatherableActions.Gift2);
                    fixAction(def.GatherableActions.Tidings);
                    fixAction(def.GatherableActions.Bountiful);
                    fixAction(def.CollectableActions.Scrutiny);
                    fixAction(def.CollectableActions.Scour);
                    fixAction(def.CollectableActions.Brazen);
                    fixAction(def.CollectableActions.Meticulous);
                    fixAction(def.CollectableActions.SolidAge);
                    fixAction(def.Consumables.Cordial);
                    Save();
                    GatherBuddy.Config.AutoGatherConfig.ConfigConversionFixed = true;
                    GatherBuddy.Config.Save();
                }

                void fixAction(ConfigPreset.ActionConfig action)
                {
                    if (action.MaxGP == 0)
                        action.MaxGP = ConfigPreset.MaxGP;
                }
            }

            public ConfigPreset Match(Gatherable? item)
            {
                return item == null
                    ? Items[Items.Count - 1]
                    : Items.SkipLast(1).Where(i => i.Match(item)).FirstOrDefault(Items[Items.Count - 1]);
            }

            public ConfigPreset Match(Fish? item)
            {
                return item == null
                    ? Items[Items.Count - 1]
                    : Items.SkipLast(1).Where(i => i.Match(item)).FirstOrDefault(Items[Items.Count - 1]);
            }
        }

        public ConfigPreset MatchConfigPreset(Gatherable? item)
            => _configPresetsSelector.Match(item);

        public ConfigPreset MatchConfigPreset(Fish? item)
            => _configPresetsSelector.Match(item);

        public void DrawConfigPresetsTab()
        {
            using var tab = ImRaii.TabItem("设置预设");

            if (!tab)
                return;

            _configPresetsSelector.Draw(SelectorWidth);
            ImGui.SameLine();
            DrawConfigPreset(_configPresetsSelector.EnsureCurrent()!, _configPresetsSelector.CurrentIdx == _configPresetsSelector.Presets.Count - 1);
        }

        private void DrawConfigPresetHeader()
        {
            if (ImGui.Button("导出到剪贴板"))
            {
                var current = _configPresetsSelector.Current;
                if (current == null)
                {
                    Notify.Error("未选中任何设置");
                    return;
                }

                var text = current.ToBase64String();
                ImGui.SetClipboardText(text);
                Notify.Success($"已复制设置预设 {current.Name} 至剪贴板");
            }

            if (ImGui.Button("检查"))
            {
                ImGui.OpenPopup("配置预设检查器");
            }

            ImGuiUtil.HoverTooltip("检查自动采集列表中使用的预设的详细信息");

            var open = true;
            using (var popup = ImRaii.PopupModal("配置预set检查器", ref open,
                       ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                if (popup)
                {
                    using (var table = ImRaii.Table("Items", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("采集列表");
                        ImGui.TableSetupColumn("物品");
                        ImGui.TableSetupColumn("配置预设");
                        ImGui.TableHeadersRow();

                        var crystals = CrystalTypes
                            .Select(x => ("", x.name, GatherBuddy.GameData.Gatherables[x.id]));
                        var items = _plugin.AutoGatherListsManager.Lists
                            .Where(x => x.Enabled && !x.Fallback)
                            .SelectMany(x => x.Items.Select(i => (x.Name, i.Name[GatherBuddy.Language], i as Gatherable)));
                        var fish = _plugin.AutoGatherListsManager.Lists.Where(x => x.Enabled && !x.Fallback)
                            .SelectMany(x => x.Items.Select(i => (x.Name, i.Name[GatherBuddy.Language], i as Fish)));

                        foreach (var (list, name, item) in items)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text(list);
                            ImGui.TableNextColumn();
                            ImGui.Text(name);
                            ImGui.TableNextColumn();
                            ImGui.Text(MatchConfigPreset(item).Name);
                        }
                        foreach (var (list, name, item) in fish)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text(list);
                            ImGui.TableNextColumn();
                            ImGui.Text(name);
                            ImGui.TableNextColumn();
                            ImGui.Text(MatchConfigPreset(item).Name);
                        }
                        foreach (var (list, name, item) in crystals)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text(list);
                            ImGui.TableNextColumn();
                            ImGui.Text(name);
                            ImGui.TableNextColumn();
                            ImGui.Text(MatchConfigPreset(item).Name);
                        }
                    }

                    var size   = ImGui.CalcTextSize("关闭").X + ImGui.GetStyle().FramePadding.X * 2.0f;
                    var offset = (ImGui.GetContentRegionAvail().X - size) * 0.5f;
                    if (offset > 0.0f)
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
                    if (ImGui.Button("关闭"))
                        ImGui.CloseCurrentPopup();
                }
            }

            ImGuiComponents.HelpMarker(
                "预设按照从上到下的顺序检查当前目标物品。\n" +
                "只使用第一个匹配的预设，其余预设将被忽略。\n" +
                "默认预设始终在最后，当没有其他预设匹配物品时使用。");
        }

        private void DrawConfigPreset(ConfigPreset preset, bool isDefault)
        {
            var     selector = _configPresetsSelector;
            ref var state    = ref _configPresetsUIState;

            if (!isDefault)
            {
                if (ImGuiUtil.DrawEditButtonText(0, CheckUnnamed(preset.Name), out var name, ref state.EditingName, IconButtonSize,
                        SetInputWidth, 64)
                 && name != CheckUnnamed(preset.Name))
                {
                    preset.Name = name;
                    selector.Save();
                }

                var enabled = preset.Enabled;
                if (ImGui.Checkbox("启用", ref enabled) && enabled != preset.Enabled)
                {
                    preset.Enabled = enabled;
                    selector.Save();
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var useGlv = preset.ItemLevel.UseGlv;
                using var box = ImRaii.ListBox("##ConfigPresetListbox",
                    new Vector2(-1.5f * ImGui.GetStyle().ItemSpacing.X, ImGui.GetFrameHeightWithSpacing() * 3 + ItemSpacing.Y));
                Span<int> ilvl = [preset.ItemLevel.Min, preset.ItemLevel.Max];
                ImGui.SetNextItemWidth(SetInputWidth);
                if (ImGui.DragInt2("物品品级下限与上限", ref ilvl[0], 0.2f, 1, useGlv ? ConfigPreset.MaxGvl : ConfigPreset.MaxLevel))
                {
                    state.ChangingMin    = preset.ItemLevel.Min != ilvl[0];
                    preset.ItemLevel.Min = ilvl[0];
                    preset.ItemLevel.Max = ilvl[1];
                }

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (preset.ItemLevel.Min > preset.ItemLevel.Max)
                    {
                        if (state.ChangingMin)
                            preset.ItemLevel.Max = preset.ItemLevel.Min;
                        else
                            preset.ItemLevel.Min = preset.ItemLevel.Max;
                    }

                    selector.Save();
                }

                ImGui.SameLine();
                if (ImGui.RadioButton("等级", !useGlv))
                    useGlv = false;
                ImGuiUtil.HoverTooltip("采集日志和采集窗口中显示的等级");
                ImGui.SameLine();
                if (ImGui.RadioButton("采集等级", useGlv))
                    useGlv = true;
                ImGuiUtil.HoverTooltip("采集等级（隐藏属性）。用于区分不同层级的传说采集点");
                if (useGlv != preset.ItemLevel.UseGlv)
                {
                    int min, max;
                    if (useGlv)
                    {
                        min = GatherBuddy.GameData.Gatherables.Values
                            .Where(i => i.Level >= preset.ItemLevel.Min)
                            .Select(i => (int)i.GatheringData.GatheringItemLevel.RowId)
                            .DefaultIfEmpty(ConfigPreset.MaxGvl)
                            .Min();
                        max = GatherBuddy.GameData.Gatherables.Values
                            .Where(i => i.Level <= preset.ItemLevel.Max)
                            .Select(i => (int)i.GatheringData.GatheringItemLevel.RowId)
                            .DefaultIfEmpty(1)
                            .Max();
                    }
                    else
                    {
                        min = GatherBuddy.GameData.Gatherables.Values
                            .Where(i => i.GatheringData.GatheringItemLevel.RowId >= preset.ItemLevel.Min)
                            .Select(i => i.Level)
                            .DefaultIfEmpty(ConfigPreset.MaxLevel)
                            .Min();
                        max = GatherBuddy.GameData.Gatherables.Values
                            .Where(i => i.GatheringData.GatheringItemLevel.RowId <= preset.ItemLevel.Max)
                            .Select(i => i.Level)
                            .DefaultIfEmpty(1)
                            .Max();
                    }

                    preset.ItemLevel.UseGlv = useGlv;
                    preset.ItemLevel.Min    = min;
                    preset.ItemLevel.Max    = max;
                    selector.Save();
                }

                ImGui.Text("采集点类型：");
                ImGui.SameLine();
                if (ImGuiUtil.Checkbox("常规", "", preset.NodeType.Regular, x => preset.NodeType.Regular = x))
                    selector.Save();
                ImGui.SameLine(0, ImGui.CalcTextSize("水晶").X - ImGui.CalcTextSize("常规").X + ItemSpacing.X);
                if (ImGuiUtil.Checkbox("未知", "", preset.NodeType.Unspoiled, x => preset.NodeType.Unspoiled = x))
                    selector.Save();
                ImGui.SameLine(0, ImGui.CalcTextSize("收藏品").X - ImGui.CalcTextSize("未知").X + ItemSpacing.X);
                if (ImGuiUtil.Checkbox("传说", "", preset.NodeType.Legendary, x => preset.NodeType.Legendary = x))
                    selector.Save();
                ImGui.SameLine();
                if (ImGuiUtil.Checkbox("限时", "", preset.NodeType.Ephemeral, x => preset.NodeType.Ephemeral = x))
                    selector.Save();

                ImGui.Text("物品类型：");
                ImGui.SameLine(0, ImGui.CalcTextSize("采集点类型：").X - ImGui.CalcTextSize("物品类型：").X + ItemSpacing.X);
                if (ImGuiUtil.Checkbox("水晶", "碎晶、水晶、晶簇", preset.ItemType.Crystals, x => preset.ItemType.Crystals = x))
                    selector.Save();
                ImGui.SameLine();
                if (ImGuiUtil.Checkbox("收藏品", "", preset.ItemType.Collectables, x => preset.ItemType.Collectables = x))
                    selector.Save();
                ImGui.SameLine();
                if (ImGuiUtil.Checkbox("其他", "", preset.ItemType.Other, x => preset.ItemType.Other = x))
                    selector.Save();
                ImGui.SameLine();
                if (ImGuiUtil.Checkbox("捕鱼", "", preset.ItemType.Fish, x => preset.ItemType.Fish = x))
                    selector.Save();
            }

            using var child = ImRaii.Child("ConfigPresetSettings", new Vector2(-1.5f * ItemSpacing.X, -ItemSpacing.Y));

            using var width = ImRaii.ItemWidth(SetInputWidth);

            using (var node = ImRaii.TreeNode("常规设置", ImGuiTreeNodeFlags.Framed))
            {
                if (node)
                {
                    if (preset.ItemType.Crystals || preset.ItemType.Other)
                    {
                        var tmp = preset.GatherableMinGP;
                        if (ImGui.DragInt("采集普通物品或水晶所需的最小GP", ref tmp, 1f, 0, ConfigPreset.MaxGP))
                            preset.GatherableMinGP = tmp;
                        if (ImGui.IsItemDeactivatedAfterEdit())
                            selector.Save();
                    }

                    if (preset.ItemType.Collectables)
                    {
                        var tmp = preset.CollectableMinGP;
                        if (ImGui.DragInt("采集收藏品所需的最小GP", ref tmp, 1f, 0, ConfigPreset.MaxGP))
                            preset.CollectableMinGP = tmp;
                        if (ImGui.IsItemDeactivatedAfterEdit())
                            selector.Save();

                        tmp = preset.CollectableActionsMinGP;
                        if (ImGui.DragInt("对收藏品使用技能所需的最小GP", ref tmp, 1f, 0, ConfigPreset.MaxGP))
                            preset.CollectableActionsMinGP = tmp;
                        if (ImGui.IsItemDeactivatedAfterEdit())
                            selector.Save();

                        ImGui.SameLine();
                        if (ImGuiUtil.Checkbox($"总是使用 {ConcatNames(Actions.SolidAge)}",
                            $"如果达到目标收藏度，无论开始GP如何，都使用{ConcatNames(Actions.SolidAge)}",
                            preset.CollectableAlwaysUseSolidAge,
                            x => preset.CollectableAlwaysUseSolidAge = x))
                            selector.Save();

                        tmp = preset.CollectableTagetScore;
                        if (ImGui.DragInt("采集前需达到的目标收藏度", ref tmp, 1f, 0, ConfigPreset.MaxCollectability))
                            preset.CollectableTagetScore = tmp;
                        if (ImGui.IsItemDeactivatedAfterEdit())
                            selector.Save();

                        tmp = preset.CollectableMinScore;
                        if (ImGui.DragInt($"最后一次尝试时的最低收藏度 (设为 {ConfigPreset.MaxCollectability} 以禁用)", ref tmp, 1f, 0, ConfigPreset.MaxCollectability))
                            preset.CollectableMinScore = tmp;
                        if (ImGui.IsItemDeactivatedAfterEdit())
                            selector.Save();
                    }

                    if (ImGuiUtil.Checkbox("自动决定使用哪些技能",
                        "此设置根据物品或采集点类型有不同的工作方式。\n" +
                        "对于收藏品：使用常规的收藏品采集轮换，启用所有技能。\n" +
                        "对于未知和传说采集点：选择技能以最大化产量。\n" +
                        "对于普通采集点：选择技能以最大化每GP消耗的产量。\n",
                        preset.ChooseBestActionsAutomatically,
                        x => preset.ChooseBestActionsAutomatically = x))
                        selector.Save();

                    if (preset.ChooseBestActionsAutomatically && preset.NodeType.Regular)
                    {
                        if (ImGuiUtil.Checkbox("等待具有最佳加成的节点再消耗GP",
                            "此设置仅适用于普通采集点。启用后，会保留GP直到遇到能提供最佳产量/GP比的采集点。\n" +
                            "确保存在具有+2完整性、+3产量和+100%额外获得率隐藏加成的采集点，并且你能满足其要求。\n" +
                            $"如果{ConcatNames(Actions.Bountiful)}提供+3加成，则此设置将被忽略，因为没有比这更好的了。\n" +
                            "如果你有采集点恢复特性（91级+），不建议启用此选项。",
                            preset.SpendGPOnBestNodesOnly,
                            x => preset.SpendGPOnBestNodesOnly = x))
                            selector.Save();
                    }
                }
            }

            using var width2 = ImRaii.ItemWidth(SetInputWidth - ImGui.GetStyle().IndentSpacing);
            if ((preset.ItemType.Crystals || preset.ItemType.Other) && !preset.ChooseBestActionsAutomatically)
            {
                using var node = ImRaii.TreeNode("采集技能", ImGuiTreeNodeFlags.Framed);
                if (node)
                {
                    DrawActionConfig(ConcatNames(Actions.Bountiful), preset.GatherableActions.Bountiful, selector.Save);
                    DrawActionConfig(ConcatNames(Actions.Yield1),    preset.GatherableActions.Yield1,    selector.Save);
                    DrawActionConfig(ConcatNames(Actions.Yield2),    preset.GatherableActions.Yield2,    selector.Save);
                    DrawActionConfig(ConcatNames(Actions.SolidAge),  preset.GatherableActions.SolidAge,  selector.Save);
                    DrawActionConfig(ConcatNames(Actions.Gift1),     preset.GatherableActions.Gift1,     selector.Save);
                    DrawActionConfig(ConcatNames(Actions.Gift2),     preset.GatherableActions.Gift2,     selector.Save);
                    DrawActionConfig(ConcatNames(Actions.Tidings),   preset.GatherableActions.Tidings,   selector.Save);
                    if (preset.ItemType.Crystals)
                    {
                        DrawActionConfig(Actions.TwelvesBounty.Names.Botanist, preset.GatherableActions.TwelvesBounty, selector.Save);
                        DrawActionConfig(Actions.GivingLand.Names.Botanist,    preset.GatherableActions.GivingLand,    selector.Save);
                    }
                }
            }

            if (preset.ItemType.Collectables && !preset.ChooseBestActionsAutomatically)
            {
                using var node = ImRaii.TreeNode("收藏品技能", ImGuiTreeNodeFlags.Framed);
                if (node)
                {
                    DrawActionConfig(Actions.Scour.Names.Botanist,      preset.CollectableActions.Scour,      selector.Save);
                    DrawActionConfig(Actions.Brazen.Names.Botanist,     preset.CollectableActions.Brazen,     selector.Save);
                    DrawActionConfig(Actions.Meticulous.Names.Botanist, preset.CollectableActions.Meticulous, selector.Save);
                    DrawActionConfig(Actions.Scrutiny.Names.Botanist,   preset.CollectableActions.Scrutiny,   selector.Save);
                    DrawActionConfig(ConcatNames(Actions.SolidAge),     preset.CollectableActions.SolidAge,   selector.Save);
                }
            }

            {
                using var node = ImRaii.TreeNode("消耗品", ImGuiTreeNodeFlags.Framed);
                if (node)
                {
                    DrawActionConfig("强心剂", preset.Consumables.Cordial, selector.Save, PossibleCordials);
                    DrawActionConfig("食物", preset.Consumables.Food, selector.Save, PossibleFoods, true);
                    DrawActionConfig("药水", preset.Consumables.Potion, selector.Save, PossiblePotions, true);
                    DrawActionConfig("生存指南", preset.Consumables.Manual, selector.Save, PossibleManuals, true);
                    DrawActionConfig("军用生存指南", preset.Consumables.SquadronManual, selector.Save, PossibleSquadronManuals, true);
                    DrawActionConfig("传送网优惠券", preset.Consumables.SquadronPass, selector.Save, PossibleSquadronPasses, true);
                }
            }

            static string ConcatNames(Actions.BaseAction action)
                => $"{action.Names.Miner} / {action.Names.Botanist}";
        }

        private void DrawActionConfig(string name, ConfigPreset.ActionConfig action, System.Action save, IEnumerable<Item>? items = null,
            bool hideGP = false)
        {
            using var node = ImRaii.TreeNode(name);
            if (!node)
                return;

            ref var state = ref _configPresetsUIState;

            if (ImGuiUtil.Checkbox("启用", "", action.Enabled, x => action.Enabled = x))
                save();
            if (!action.Enabled)
                return;

            if (action is ConfigPreset.ActionConfigIntegrity action2)
            {
                if (ImGuiUtil.Checkbox("仅在首步使用", "仅在尚未采集任何物品时使用",
                        action2.FirstStepOnly, x => action2.FirstStepOnly = x))
                    save();
            }

            if (!hideGP)
            {
                Span<int> gp = [action.MinGP, action.MaxGP];
                if (ImGui.DragInt2("GP下限与上限", ref gp[0], 1, 0, ConfigPreset.MaxGP))
                {
                    state.ChangingMin = action.MinGP != gp[0];
                    action.MinGP      = gp[0];
                    action.MaxGP      = gp[1];
                }

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (action.MinGP > action.MaxGP)
                    {
                        if (state.ChangingMin)
                            action.MaxGP = action.MinGP;
                        else
                            action.MinGP = action.MaxGP;
                    }

                    save();
                }
            }

            if (action is ConfigPreset.ActionConfigBoon action3)
            {
                Span<int> chance = [action3.MinBoonChance, action3.MaxBoonChance];
                if (ImGui.DragInt2("额外获得率下限与上限", ref chance[0], 0.2f, 0, 100))
                {
                    state.ChangingMin     = action3.MinBoonChance != chance[0];
                    action3.MinBoonChance = chance[0];
                    action3.MaxBoonChance = chance[1];
                }

                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    if (action3.MinBoonChance > action3.MaxBoonChance)
                    {
                        if (state.ChangingMin)
                            action3.MaxBoonChance = action3.MinBoonChance;
                        else
                            action3.MinBoonChance = action3.MaxBoonChance;
                    }

                    save();
                }
            }

            if (action is ConfigPreset.ActionConfigIntegrity action4)
            {
                var tmp = action4.MinIntegrity;
                if (ImGui.DragInt("采集点耐久下限与上限", ref tmp, 0.1f, 1, ConfigPreset.MaxIntegrity))
                    action4.MinIntegrity = tmp;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    save();
            }

            if (action is ConfigPreset.ActionConfigYieldBonus action5)
            {
                var tmp = action5.MinYieldBonus;
                if (ImGui.DragInt("最小产量加成", ref tmp, 0.1f, 1, 3))
                    action5.MinYieldBonus = tmp;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    save();
            }

            if (action is ConfigPreset.ActionConfigYieldTotal action6)
            {
                var tmp = action6.MinYieldTotal;
                if (ImGui.DragInt("最小总产量", ref tmp, 0.1f, 1, 30))
                    action6.MinYieldTotal = tmp;
                if (ImGui.IsItemDeactivatedAfterEdit())
                    save();
            }

            if (action is ConfigPreset.ActionConfigConsumable action7 && items != null)
            {
                var list = items
                    .SelectMany(item => new[]
                    {
                        (item, rowid: item.RowId),
                        (item, rowid: item.RowId + 100000)
                    })
                    .Where(x => x.item.CanBeHq || x.rowid < 100000)
                    .Select(x => (name: x.item.Name.ExtractText(), x.rowid, count: GetInventoryItemCount(x.rowid)))
                    .OrderBy(x => x.count == 0)
                    .ThenBy(x => x.name)
                    .Select(x => x with { name = $"{(x.rowid > 100000 ? " " : "")}{x.name} ({x.count})" })
                    .ToList();

                var       selected = (action7.ItemId > 0 ? list.FirstOrDefault(x => x.rowid == action7.ItemId).name : null) ?? string.Empty;
                using var combo    = ImRaii.Combo($"选择 {name.ToLower()}", selected);
                if (combo)
                {
                    if (ImGui.Selectable(string.Empty, action7.ItemId <= 0))
                    {
                        action7.ItemId = 0;
                        save();
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

                        if (ImGui.Selectable(itemname, action7.ItemId == rowid))
                        {
                            action7.ItemId = rowid;
                            save();
                        }
                    }
                }
            }
        }
    }
}
