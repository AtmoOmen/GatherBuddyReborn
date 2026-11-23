using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using GatherBuddy.Crafting;
using GatherBuddy.RebornUI;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private string _inGameMacroText = string.Empty;
    private string _inGameMacroName = string.Empty;
    private string? _inGameMacroError = null;
    private UserMacro? _previewInGameMacro = null;
    private int _previewMinCraft;
    private int _previewMinCtrl;
    private int _previewMinCP;
    private string? _selectedMacroId = null;
    private string _macroSearch = string.Empty;
    private string _editingMacroStatsId = string.Empty;
    private int _editingMacroMinCraft;
    private int _editingMacroMinCtrl;
    private int _editingMacroMinCP;
    private readonly Dictionary<uint, uint> _skillIconCache = [];

    private const string BrowsingwayErrorDialogId = "##browsingwayError";

    private void DrawMacrosPage()
    {
        var skipUnusable = GatherBuddy.Config.SkipMacroStepIfUnable;
        if (M3Widgets.LabeledCheckbox("跳过无法使用的技能##skipUnusable", ref skipUnusable,
                tooltip: "当前无法执行某一步时，继续执行下一步而不是停止。"))
        {
            GatherBuddy.Config.SkipMacroStepIfUnable = skipUnusable;
            GatherBuddy.Config.Save();
        }

        ImGui.SameLine(0, M3.Space3 * 2);
        var fallbackEnabled = GatherBuddy.Config.MacroFallbackEnabled;
        if (M3Widgets.LabeledCheckbox("宏执行完后继续由求解器制作##fallbackEnabled", ref fallbackEnabled,
                tooltip: "宏结束时如果制作尚未完成，由求解器继续。"))
        {
            GatherBuddy.Config.MacroFallbackEnabled = fallbackEnabled;
            GatherBuddy.Config.Save();
        }

        ImGui.Dummy(new Vector2(0, M3.Space1));
        var avail = ImGui.GetContentRegionAvail();
        var leftWidth = VulcanUiScaling.Scaled(290f);
        using (var left = M3Panel.Begin("##macrosLeft", new Vector2(leftWidth, avail.Y), false, VulcanUiStyle.PanelBackground))
        {
            if (left)
            {
                DrawMacroListPanel();
            }
        }

        ImGui.SameLine(0, M3.Space2);
        using (var right = M3Panel.Begin("##macrosRight", new Vector2(0, avail.Y)))
        {
            if (right)
            {
                var macroLibrary = CraftingGameInterop.UserMacroLibrary;
                var selectedMacro = _selectedMacroId != null ? macroLibrary.GetMacroByStringId(_selectedMacroId) : null;
                if (selectedMacro != null)
                {
                    DrawMacroDetail(selectedMacro, macroLibrary);
                }
                else
                {
                    DrawImportPanel();
                }
            }
        }

        DrawBrowsingwayErrorDialog();
    }

    private void DrawImportPanel()
    {
        using (ImRaii.PushFont(M3.TitleMedium))
        {
            ImGui.TextUnformatted("导入宏");
        }

        M3Text.Draw("粘贴从 Teamcraft 或 Artisan 导出的制作宏。支持 /ac 指令、每行一条技能以及 Artisan JSON。",
            M3TextStyle.Muted, true);
        ImGui.Dummy(new Vector2(0, M3.Space2));

        if (M3Widgets.Button("##browseTC", "浏览 Teamcraft", M3ButtonStyle.Tonal, FontAwesomeIcon.Globe,
                tooltip: "在 Browsingway 覆盖层中打开 Teamcraft 的社区制作流程。\n\n"
                  + "设置步骤\n"
                  + "1. 安装 Browsingway 插件\n"
                  + "2. 执行 /bw config\n"
                  + "3. 创建一个新的覆盖层 (+ 按钮)\n"
                  + "4. 将该覆盖层的命令名称设为 teamcraft\n"
                  + "5. 关闭设置后点击此按钮\n\n"
                  + "也可以在浏览器打开 https://ffxivteamcraft.com/community-rotations。"))
        {
            try
            {
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft disabled off");
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft url https://ffxivteamcraft.com/community-rotations");
                GatherBuddy.Log.Information("在 Browsingway 覆盖层中打开 TeamCraft");
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"无法打开 Browsingway 覆盖层: {ex.Message}");
                M3Dialog.Open(BrowsingwayErrorDialogId);
            }
        }

        ImGui.SameLine(0, M3.Space2);
        if (M3Widgets.Button("##hideTC", "隐藏浮层", M3ButtonStyle.Text, FontAwesomeIcon.EyeSlash))
        {
            try
            {
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft disabled on");
                GatherBuddy.Log.Information("隐藏 Teamcraft 覆盖层");
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"无法隐藏 Browsingway 覆盖层: {ex.Message}");
            }
        }

        ImGui.Dummy(new Vector2(0, M3.Space2));
        var width = ImGui.GetContentRegionAvail().X;
        M3Widgets.TextField("##macroName", "宏名称", ref _inGameMacroName, width, 100);
        ImGui.Dummy(new Vector2(0, M3.Space1));
        M3Widgets.SectionLabel("宏文本");
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, M3.ShapeExtraSmall)
                   .Push(ImGuiStyleVar.FramePadding, new Vector2(M3.Space3, M3.Space2)))
        using (ImRaii.PushColor(ImGuiCol.FrameBg, M3.Scheme.SurfaceContainerHighest))
        {
            ImGui.InputTextMultiline("##macroText", ref _inGameMacroText, 500000, new Vector2(width, VulcanUiScaling.Scaled(200f)));
        }

        ImGui.Dummy(new Vector2(0, M3.Space1));
        if (M3Widgets.Button("##parseBtn", "解析并预览", M3ButtonStyle.Filled, FontAwesomeIcon.Eye,
                enabled: !string.IsNullOrWhiteSpace(_inGameMacroText)))
        {
            ParseInGameMacro();
        }

        if (_inGameMacroError != null)
        {
            ImGui.Dummy(new Vector2(0, M3.Space1));
            M3Widgets.Banner("##macroError", _inGameMacroError, M3Severity.Error, FontAwesomeIcon.ExclamationCircle);
        }

        if (_previewInGameMacro != null)
        {
            ImGui.Dummy(new Vector2(0, M3.Space2));
            DrawInGameMacroPreview(_previewInGameMacro);
        }
    }

    private static void DrawBrowsingwayErrorDialog()
    {
        using var dialog = M3Dialog.Begin(BrowsingwayErrorDialogId, "Browsingway 不可用",
            "找不到 Browsingway 插件或插件尚未加载。你可以在浏览器中打开 Teamcraft，再把宏粘贴到这里。",
            FontAwesomeIcon.ExclamationTriangle);
        if (dialog.IsOpen)
        {
            M3Dialog.Actions([new M3DialogAction("确定", M3ButtonStyle.Filled)]);
        }
    }

    private void DrawInGameMacroPreview(UserMacro macro)
    {
        using var card = M3Card.Begin("##macroPreview", "预览", FontAwesomeIcon.Eye, M3.AccentFor(2), subtitle: $"{macro.Name}，{macro.Actions.Count} 个技能");
        M3Text.Draw("最低属性（可选）", M3TextStyle.Label);
        M3Widgets.RowNumber("作业精度###Craftsmanship##previewMinCraft", ref _previewMinCraft, 10, 0);
        M3Widgets.RowNumber("加工精度###Control##previewMinCtrl", ref _previewMinCtrl, 10, 0);
        M3Widgets.RowNumber("制作力###CP##previewMinCP", ref _previewMinCP, 5, 0);
        _previewMinCraft = Math.Max(0, _previewMinCraft);
        _previewMinCtrl = Math.Max(0, _previewMinCtrl);
        _previewMinCP = Math.Max(0, _previewMinCP);

        if (M3Widgets.Button("##importInGameBtn", "导入", M3ButtonStyle.Filled, FontAwesomeIcon.FileImport))
        {
            macro.MinCraftsmanship = _previewMinCraft;
            macro.MinControl = _previewMinCtrl;
            macro.MinCP = _previewMinCP;
            ImportInGameMacro(macro);
        }

        ImGui.SameLine(0, M3.Space2);
        if (M3Widgets.Button("##cancelInGameBtn", "取消", M3ButtonStyle.Text))
        {
            _previewInGameMacro = null;
            _inGameMacroError = null;
        }
    }
    private void ParseInGameMacro()
    {
        _inGameMacroError = null;
        _previewInGameMacro = null;

        try
        {
            var macroName = string.IsNullOrWhiteSpace(_inGameMacroName) ? null : _inGameMacroName;
            var macro = MacroParser.ParseInGameMacro(_inGameMacroText, macroName);

            if (macro == null || macro.Actions.Count == 0)
            {
                _inGameMacroError = "解析宏失败。请确认包含可识别的制作技能名称，或是受支持的 Artisan JSON 导出。";
            }
            else
            {
                _previewInGameMacro = macro;
                _previewMinCraft = macro.MinCraftsmanship;
                _previewMinCtrl = macro.MinControl;
                _previewMinCP = macro.MinCP;
            }
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"解析宏失败：{ex.Message}";
            GatherBuddy.Log.Error($"解析游戏内宏失败: {ex.Message}");
        }
    }

    private void ImportInGameMacro(UserMacro macro)
    {
        try
        {
            var macroLibrary = CraftingGameInterop.UserMacroLibrary;
            macroLibrary.AddMacro(macro, 0);

            GatherBuddy.Log.Information($"已导入游戏内宏: {macro.Name}");

            _selectedMacroId = macro.Id;
            _previewInGameMacro = null;
            _inGameMacroText = string.Empty;
            _inGameMacroName = string.Empty;
            _inGameMacroError = null;
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"导入宏失败：{ex.Message}";
            GatherBuddy.Log.Error($"导入游戏内宏失败: {ex.Message}");
        }
    }


    private void DrawMacroListPanel()
    {
        M3Widgets.SearchField("##macroSearch", "搜索宏", ref _macroSearch, ImGui.GetContentRegionAvail().X - M3Widgets.IconButtonSize - M3.Space1);
        ImGui.SameLine(0, M3.Space1);
        Ui.CenterInRow(ImGui.GetItemRectSize().Y, M3Widgets.IconButtonSize);
        if (M3Widgets.IconButton("##newImport", FontAwesomeIcon.Plus, "导入宏", M3ButtonStyle.Tonal))
        {
            _selectedMacroId = null;
        }

        ImGui.Dummy(new Vector2(0, M3.Space1));
        var macroLibrary = CraftingGameInterop.UserMacroLibrary;
        var allMacros = macroLibrary.GetAllMacros();
        if (allMacros.Count == 0)
        {
            M3Widgets.EmptyState("##noMacros", "暂无宏", "通过右侧面板导入一个宏。", FontAwesomeIcon.Scroll);
            return;
        }

        var filtered = string.IsNullOrWhiteSpace(_macroSearch)
            ? allMacros
            : allMacros.FindAll(m => m.Name.Contains(_macroSearch, StringComparison.OrdinalIgnoreCase)
                || (m.Author?.Contains(_macroSearch, StringComparison.OrdinalIgnoreCase) ?? false));

        if (filtered.Count == 0)
        {
            M3Text.Draw("未找到匹配的宏。", M3TextStyle.Faint);
            return;
        }

        var iconSize = VulcanUiScaling.Scaled(28f);
        using var rows = ImRaii.Child("##macroRows", new Vector2(-1, -1));
        if (!rows)
        {
            return;
        }

        foreach (var macro in filtered)
        {
            var isSelected = _selectedMacroId == macro.Id;
            var firstIconId = macro.Actions.Count > 0 ? GetSkillIconId(macro.Actions[0]) : 0u;
            var displayName = string.IsNullOrEmpty(macro.Author) ? macro.Name : $"{macro.Name}  ({macro.Author})";
            if (Ui.IconListRow($"##sel_{macro.Id}", firstIconId, displayName, isSelected, iconSize, macro.Actions.Count.ToString()))
            {
                _selectedMacroId = isSelected ? null : macro.Id;
            }

            M3Tooltip.Hovered(macro.MinCraftsmanship > 0 || macro.MinControl > 0 || macro.MinCP > 0
                ? $"{macro.Actions.Count} 个技能。最低要求为作业精度 {macro.MinCraftsmanship}、加工精度 {macro.MinControl}、制作力 {macro.MinCP}。"
                : $"{macro.Actions.Count} 个技能");
        }
    }

    private void DrawMacroDetail(UserMacro macro, UserMacroLibrary macroLibrary)
    {
        var largeIconSize = VulcanUiScaling.Scaled(48f);
        var start = ImGui.GetCursorScreenPos();
        var firstIconId = macro.Actions.Count > 0 ? GetSkillIconId(macro.Actions[0]) : 0u;
        if (firstIconId > 0)
        {
            M3Widgets.GameIcon(firstIconId, largeIconSize);
            ImGui.SameLine(0, M3.Space3);
        }

        using (ImRaii.Group())
        {
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, start.Y + (largeIconSize - ImGui.GetTextLineHeightWithSpacing() * 2f) * 0.5f));
            using (ImRaii.PushFont(M3.TitleMedium))
            {
                ImGui.TextUnformatted(macro.Name);
            }

            M3Text.Draw(string.IsNullOrEmpty(macro.Author) ? macro.Source : $"作者：{macro.Author}", M3TextStyle.Muted);
        }

        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X - M3Widgets.IconButtonSize, start.Y));
        if (M3Widgets.IconButton("##closeDetail", FontAwesomeIcon.Times, "关闭"))
        {
            _selectedMacroId = null;
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + MathF.Max(largeIconSize, M3Widgets.IconButtonSize) + M3.Space2));
        if (_editingMacroStatsId != macro.Id)
        {
            _editingMacroStatsId = macro.Id;
            _editingMacroMinCraft = macro.MinCraftsmanship;
            _editingMacroMinCtrl = macro.MinControl;
            _editingMacroMinCP = macro.MinCP;
        }

        using (M3Card.Begin("##macroStats", "最低属性", FontAwesomeIcon.ChartBar, M3.AccentFor(0),
                   subtitle: $"创建于 {macro.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"))
        {
            M3Widgets.RowNumber("作业精度###Craftsmanship##editMinCraft", ref _editingMacroMinCraft, 10, 0);
            M3Widgets.RowNumber("加工精度###Control##editMinCtrl", ref _editingMacroMinCtrl, 10, 0);
            M3Widgets.RowNumber("制作力###CP##editMinCP", ref _editingMacroMinCP, 5, 0);
            _editingMacroMinCraft = Math.Max(0, _editingMacroMinCraft);
            _editingMacroMinCtrl = Math.Max(0, _editingMacroMinCtrl);
            _editingMacroMinCP = Math.Max(0, _editingMacroMinCP);
            var changed = _editingMacroMinCraft != macro.MinCraftsmanship || _editingMacroMinCtrl != macro.MinControl || _editingMacroMinCP != macro.MinCP;
            if (M3Widgets.Button("##saveStats", "保存", M3ButtonStyle.Filled, FontAwesomeIcon.Save, enabled: changed))
            {
                macro.MinCraftsmanship = _editingMacroMinCraft;
                macro.MinControl = _editingMacroMinCtrl;
                macro.MinCP = _editingMacroMinCP;
                MacroValidator.InvalidateByMacroId(macro.Id);
                macroLibrary.Save();
                GatherBuddy.Log.Debug($"[MacrosTab] 已保存宏 '{macro.Name}' 的最低属性");
                M3Snackbar.Show("已保存最低属性。");
            }

            if (!string.IsNullOrEmpty(macro.TeamcraftUrl))
            {
                M3Widgets.RowCopy("Teamcraft 链接", macro.TeamcraftUrl, iconOnly: true);
            }
        }

        M3Widgets.SectionLabel($"技能（{macro.Actions.Count}）");
        var actionIconSize = VulcanUiScaling.Scaled(24f);
        using (var actions = ImRaii.Child("##macroActions", new Vector2(-1, ImGui.GetContentRegionAvail().Y - M3Widgets.ButtonHeight - M3.Space2), false))
        {
            if (actions)
            {
                for (var i = 0; i < macro.Actions.Count; i++)
                {
                    var actionId = macro.Actions[i];
                    var iconId = GetSkillIconId(actionId);
                    if (iconId > 0)
                    {
                        M3Widgets.GameIcon(iconId, actionIconSize);
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(actionIconSize));
                    }

                    ImGui.SameLine(0, M3.Space2);
                    Ui.RowText($"{i + 1}.", actionIconSize, M3TextStyle.Faint);
                    ImGui.SameLine(0, M3.Space1);
                    Ui.RowText(CraftSkillDisplayName((VulcanSkill)actionId), actionIconSize);
                }
            }
        }

        if (M3Widgets.HoldButton($"##deleteMacro_{macro.Id}", "删除宏", 1f, M3ButtonStyle.Danger, FontAwesomeIcon.Trash,
                tooltip: "长按删除此宏。"))
        {
            macroLibrary.RemoveMacro(macro.Id);
            _selectedMacroId = null;
            GatherBuddy.Log.Debug($"[MacrosTab] 已删除宏 '{macro.Name}'");
            M3Snackbar.Show($"已删除 {macro.Name}。");
        }
    }
    private uint GetSkillIconId(uint skillId)
    {
        if (_skillIconCache.TryGetValue(skillId, out var cached))
        {
            return cached;
        }

        uint iconId = 0;
        try
        {
            if (skillId >= 100000)
            {
                var sheet = Dalamud.GameData.GetExcelSheet<CraftAction>();
                if (sheet != null && sheet.TryGetRow(skillId, out var row))
                {
                    iconId = row.Icon;
                }
            }
            else if (skillId > 0)
            {
                var sheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                if (sheet != null && sheet.TryGetRow(skillId, out var row))
                {
                    iconId = row.Icon;
                }
            }
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Debug($"[MacrosTab] 获取技能图标失败 {skillId}: {ex.Message}");
        }

        _skillIconCache[skillId] = iconId;
        return iconId;
    }
    private static readonly Dictionary<VulcanSkill, string> VulcanSkillNamesZh = new()
    {
        { VulcanSkill.None, "无" },
        { VulcanSkill.TouchCombo, "加工连段" },
        { VulcanSkill.TouchComboRefined, "加工连段 (精炼加工路线)" },

        { VulcanSkill.BasicSynthesis, "制作" },
        { VulcanSkill.CarefulSynthesis, "模范制作" },
        { VulcanSkill.RapidSynthesis, "高速制作" },
        { VulcanSkill.Groundwork, "坯料制作" },
        { VulcanSkill.IntensiveSynthesis, "集中制作" },
        { VulcanSkill.PrudentSynthesis, "俭约制作" },
        { VulcanSkill.MuscleMemory, "坚信" },

        { VulcanSkill.BasicTouch, "加工" },
        { VulcanSkill.StandardTouch, "中级加工" },
        { VulcanSkill.AdvancedTouch, "上级加工" },
        { VulcanSkill.HastyTouch, "仓促" },
        { VulcanSkill.PreparatoryTouch, "坯料加工" },
        { VulcanSkill.PreciseTouch, "集中加工" },
        { VulcanSkill.PrudentTouch, "俭约加工" },
        { VulcanSkill.TrainedFinesse, "工匠的神技" },
        { VulcanSkill.Reflect, "闲静" },
        { VulcanSkill.RefinedTouch, "精炼加工" },
        { VulcanSkill.DaringTouch, "冒进" },

        { VulcanSkill.ByregotsBlessing, "比尔格的祝福" },
        { VulcanSkill.TrainedEye, "工匠的神速技巧" },
        { VulcanSkill.DelicateSynthesis, "精密制作" },

        { VulcanSkill.Veneration, "崇敬" },
        { VulcanSkill.Innovation, "改革" },
        { VulcanSkill.GreatStrides, "阔步" },
        { VulcanSkill.TricksOfTrade, "秘诀" },
        { VulcanSkill.MastersMend, "精修" },
        { VulcanSkill.Manipulation, "掌握" },
        { VulcanSkill.WasteNot, "俭约" },
        { VulcanSkill.WasteNot2, "长期俭约" },
        { VulcanSkill.Observe, "观察" },
        { VulcanSkill.CarefulObservation, "设计变动" },
        { VulcanSkill.FinalAppraisal, "最终确认" },
        { VulcanSkill.HeartAndSoul, "专心致志" },
        { VulcanSkill.QuickInnovation, "快速改革" },
        { VulcanSkill.ImmaculateMend, "巧夺天工" },
        { VulcanSkill.TrainedPerfection, "工匠的绝技" },

        { VulcanSkill.MaterialMiracle, "奇迹之材" },
    };

    private static string CraftSkillDisplayName(VulcanSkill skill)
        => VulcanSkillNamesZh.TryGetValue(skill, out var name) ? name : skill.ToString();
}
