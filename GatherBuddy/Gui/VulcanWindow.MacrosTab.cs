using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using GatherBuddy.Crafting;
using GatherBuddy.Vulcan;
using ElliLib.Raii;
using Lumina.Excel.Sheets;

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
    private readonly Dictionary<uint, uint> _skillIconCache = new();

    private void DrawMacrosTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("宏##macrosTab", 3, 10);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("宏##macrosTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }

        using (tabItem)
        {
            if (!tabOpen)
                return;

            var skipUnusable = GatherBuddy.Config.SkipMacroStepIfUnable;
            if (ImGui.Checkbox("无法使用技能时跳过宏步骤##skipUnusable", ref skipUnusable))
            {
                GatherBuddy.Config.SkipMacroStepIfUnable = skipUnusable;
                GatherBuddy.Config.Save();
            }
            ImGui.SameLine(0, 20);
            var fallbackEnabled = GatherBuddy.Config.MacroFallbackEnabled;
            if (ImGui.Checkbox("宏执行完毕时使用备用求解器继续制作##fallbackEnabled", ref fallbackEnabled))
            {
                GatherBuddy.Config.MacroFallbackEnabled = fallbackEnabled;
                GatherBuddy.Config.Save();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var avail = ImGui.GetContentRegionAvail();
            var leftWidth = 270f;

            using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
            {
                ImGui.BeginChild("##macrosLeft", new Vector2(leftWidth, avail.Y), true);
                DrawMacroListPanel();
                ImGui.EndChild();
            }

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
            {
                ImGui.BeginChild("##macrosRight", new Vector2(0, avail.Y), true);
                var macroLibrary = CraftingGameInterop.UserMacroLibrary;
                var selectedMacro = _selectedMacroId != null
                    ? macroLibrary.GetMacroByStringId(_selectedMacroId)
                    : null;
                if (selectedMacro != null)
                    DrawMacroDetail(selectedMacro, macroLibrary);
                else
                    DrawImportPanel();
                ImGui.EndChild();
            }
        }
    }

    private void DrawImportPanel()
    {
        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudYellow, "导入宏");
        ImGui.TextColored(ImGuiColors.DalamudGrey3, "从 TeamCraft 粘贴一个制作宏(游戏内宏格式)。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("浏览 TeamCraft##browseTC", new Vector2(200, 0)))
        {
            try
            {
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft disabled off");
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft url https://ffxivteamcraft.com/community-rotations");
                GatherBuddy.Log.Information("Opening TeamCraft in Browsingway overlay");
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"Could not open Browsingway overlay: {ex.Message}");
                ImGui.OpenPopup("BrowsingwayError");
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "在 Browsingway 覆盖层中打开 Teamcraft 社区宏\n\n" +
                "需要设置:\n" +
                "1. 安装 Browsingway 插件\n" +
                "2. 执行 /bw config\n" +
                "3. 创建一个新的覆盖层 (+ 按钮)\n" +
                "4. 将命令名称设置为 'teamcraft'\n" +
                "5. 关闭配置并点击此按钮\n\n" +
                "完成后使用“隐藏覆盖层”按钮关闭覆盖层。\n\n" +
                "或者直接在浏览器中访问 https://ffxivteamcraft.com/community-rotations");

        ImGui.SameLine();
        if (ImGui.Button("隐藏覆盖层##hideTC", new Vector2(120, 0)))
        {
            try
            {
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft disabled on");
                GatherBuddy.Log.Information("Hiding Teamcraft overlay");
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"Could not hide Browsingway overlay: {ex.Message}");
            }
        }

        if (ImGui.BeginPopup("BrowsingwayError"))
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "Browsingway 插件未找到或未加载。");
            ImGui.TextWrapped("可在浏览器中打开 TeamCraft, 然后在下方粘贴宏");
            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("名称:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##macroName", "输入宏名称...", ref _inGameMacroName, 100);

        ImGui.Spacing();
        ImGui.Text("宏文本:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##macroText", ref _inGameMacroText, 10000, new Vector2(-1, 200));

        ImGui.Spacing();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(_inGameMacroText)))
        {
            if (ImGui.Button("解析 & 预览##parseBtn", new Vector2(150, 0)))
                ParseInGameMacro();
        }

        if (_inGameMacroError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudRed, $"错误: {_inGameMacroError}");
        }

        if (_previewInGameMacro != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawInGameMacroPreview(_previewInGameMacro);
        }
    }

    private void DrawInGameMacroPreview(UserMacro macro)
    {
        ImGui.TextColored(ImGuiColors.ParsedGreen, "预览");
        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"{macro.Name}  —  {macro.Actions.Count} 个技能");
        ImGui.Spacing();

        ImGui.Text("最低属性 (可选):");
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("作业精度##previewMinCraft", ref _previewMinCraft);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("加工精度##previewMinCtrl", ref _previewMinCtrl);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("制作力##previewMinCP", ref _previewMinCP);
        _previewMinCraft = Math.Max(0, _previewMinCraft);
        _previewMinCtrl = Math.Max(0, _previewMinCtrl);
        _previewMinCP = Math.Max(0, _previewMinCP);

        ImGui.Spacing();
        if (ImGui.Button("导入##importInGameBtn", new Vector2(120, 0)))
        {
            macro.MinCraftsmanship = _previewMinCraft;
            macro.MinControl = _previewMinCtrl;
            macro.MinCP = _previewMinCP;
            ImportInGameMacro(macro);
        }
        ImGui.SameLine();
        if (ImGui.Button("取消##cancelInGameBtn", new Vector2(90, 0)))
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
            var macroName = string.IsNullOrWhiteSpace(_inGameMacroName) ? "已导入的宏" : _inGameMacroName;
            var macro = MacroParser.ParseInGameMacro(_inGameMacroText, macroName);

            if (macro == null || macro.Actions.Count == 0)
            {
                _inGameMacroError = "解析宏失败, 请确认内容包含有效的 /ac 或 /action 指令。";
            }
            else
            {
                _previewInGameMacro = macro;
                _previewMinCraft = 0;
                _previewMinCtrl = 0;
                _previewMinCP = 0;
            }
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"解析宏失败: {ex.Message}";
            GatherBuddy.Log.Error($"Failed to parse in-game macro: {ex.Message}");
        }
    }

    private void ImportInGameMacro(UserMacro macro)
    {
        try
        {
            var macroLibrary = CraftingGameInterop.UserMacroLibrary;
            macroLibrary.AddMacro(macro, 0);

            GatherBuddy.Log.Information($"Imported in-game macro: {macro.Name}");

            _selectedMacroId = macro.Id;
            _previewInGameMacro = null;
            _inGameMacroText = string.Empty;
            _inGameMacroName = string.Empty;
            _inGameMacroError = null;
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"导入宏失败: {ex.Message}";
            GatherBuddy.Log.Error($"Failed to import in-game macro: {ex.Message}");
        }
    }


    private void DrawMacroListPanel()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##macroSearch", "搜索宏...", ref _macroSearch, 128);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var macroLibrary = CraftingGameInterop.UserMacroLibrary;
        var allMacros = macroLibrary.GetAllMacros();

        if (allMacros.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "尚未添加任何宏");
            ImGui.TextColored(ImGuiColors.DalamudGrey, "请使用导入面板来添加。");
            return;
        }

        var filtered = string.IsNullOrWhiteSpace(_macroSearch)
            ? allMacros
            : allMacros
                .Where(m => m.Name.Contains(_macroSearch, StringComparison.OrdinalIgnoreCase)
                         || (m.Author?.Contains(_macroSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        if (filtered.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "没有符合搜索条件的宏。");
            return;
        }

        var iconSize = new Vector2(28f, 28f);
        var itemHeight = iconSize.Y + ImGui.GetStyle().ItemSpacing.Y;
        var contentMaxX = ImGui.GetContentRegionMax().X;

        foreach (var macro in filtered)
        {
            var isSelected = _selectedMacroId == macro.Id;
            var firstIconId = macro.Actions.Count > 0 ? GetSkillIconId(macro.Actions[0]) : 0u;

            if (firstIconId > 0)
            {
                var wrap = Icons.DefaultStorage.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(firstIconId))
                    .GetWrapOrDefault();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, iconSize);
                else
                    ImGui.Dummy(iconSize);
            }
            else
            {
                ImGui.Dummy(iconSize);
            }

            ImGui.SameLine(0, 6);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (iconSize.Y - ImGui.GetTextLineHeight()) / 2f);

            var displayName = string.IsNullOrEmpty(macro.Author)
                ? macro.Name
                : $"{macro.Name}  ({macro.Author})";

            if (ImGui.Selectable($"{displayName}##sel_{macro.Id}", isSelected, ImGuiSelectableFlags.None,
                    new Vector2(contentMaxX - ImGui.GetCursorPosX(), 0)))
                _selectedMacroId = isSelected ? null : macro.Id;

            var statsLine = macro.MinCraftsmanship > 0 || macro.MinControl > 0 || macro.MinCP > 0
                ? $"{macro.Actions.Count} 个技能  |  最低属性: {macro.MinCraftsmanship}/{macro.MinControl}/{macro.MinCP}"
                : $"{macro.Actions.Count} 个技能";
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(statsLine);
        }
    }

    private void DrawMacroDetail(UserMacro macro, UserMacroLibrary macroLibrary)
    {
        var largeIconSize = new Vector2(48f, 48f);

        var closeW = ImGui.CalcTextSize("X").X + ImGui.GetStyle().FramePadding.X * 2 + 4;
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - closeW);
        if (ImGui.SmallButton("X##closeDetail"))
            _selectedMacroId = null;

        ImGui.Spacing();

        var firstIconId = macro.Actions.Count > 0 ? GetSkillIconId(macro.Actions[0]) : 0u;
        if (firstIconId > 0)
        {
            var wrap = Icons.DefaultStorage.TextureProvider
                .GetFromGameIcon(new GameIconLookup(firstIconId))
                .GetWrapOrDefault();
            if (wrap != null)
            {
                ImGui.Image(wrap.Handle, largeIconSize);
                ImGui.SameLine(0, 10);
            }
        }

        var lineH = ImGui.GetTextLineHeightWithSpacing();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (largeIconSize.Y - lineH * 2f) / 2f);
        ImGui.TextColored(ImGuiColors.ParsedGold, macro.Name);
        ImGui.TextColored(ImGuiColors.DalamudGrey3,
            string.IsNullOrEmpty(macro.Author) ? macro.Source : $"作者: {macro.Author}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_editingMacroStatsId != macro.Id)
        {
            _editingMacroStatsId = macro.Id;
            _editingMacroMinCraft = macro.MinCraftsmanship;
            _editingMacroMinCtrl = macro.MinControl;
            _editingMacroMinCP = macro.MinCP;
        }

        ImGui.TextColored(ImGuiColors.DalamudYellow, "最低属性");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("作业精度##editMinCraft", ref _editingMacroMinCraft);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        ImGui.InputInt("加工精度##editMinCtrl", ref _editingMacroMinCtrl);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputInt("制作力##editMinCP", ref _editingMacroMinCP);
        _editingMacroMinCraft = Math.Max(0, _editingMacroMinCraft);
        _editingMacroMinCtrl = Math.Max(0, _editingMacroMinCtrl);
        _editingMacroMinCP = Math.Max(0, _editingMacroMinCP);
        ImGui.SameLine();
        if (ImGui.SmallButton("保存##saveStats"))
        {
            macro.MinCraftsmanship = _editingMacroMinCraft;
            macro.MinControl = _editingMacroMinCtrl;
            macro.MinCP = _editingMacroMinCP;
            MacroValidator.InvalidateByMacroId(macro.Id);
            macroLibrary.Save();
            GatherBuddy.Log.Debug($"[MacrosTab] Saved min stats for macro '{macro.Name}'");
        }

        ImGui.Spacing();
        ImGui.TextColored(ImGuiColors.DalamudGrey, $"创建时间: {macro.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrEmpty(macro.TeamcraftUrl))
            ImGui.TextColored(ImGuiColors.DalamudGrey, $"链接: {macro.TeamcraftUrl}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(ImGuiColors.ParsedGold, $"技能数量 ({macro.Actions.Count})");
        ImGui.Spacing();

        var actionIconSize = new Vector2(24f, 24f);
        var remainH = ImGui.GetContentRegionAvail().Y - 32f;
        ImGui.BeginChild("##macroActions", new Vector2(-1, remainH), false);

        for (var i = 0; i < macro.Actions.Count; i++)
        {
            var actionId = macro.Actions[i];
            var skillName = ((VulcanSkill)actionId).ToString();

            // 使用字典映射中文技能名
            var skillEnum = (VulcanSkill)actionId;
            var skillNameZh = VulcanSkillNamesZh.TryGetValue(skillEnum, out var zhName)
                ? zhName
                : skillEnum.ToString();

            var iconId = GetSkillIconId(actionId);

            if (iconId > 0)
            {
                var wrap = Icons.DefaultStorage.TextureProvider
                    .GetFromGameIcon(new GameIconLookup(iconId))
                    .GetWrapOrDefault();
                if (wrap != null)
                    ImGui.Image(wrap.Handle, actionIconSize);
                else
                    ImGui.Dummy(actionIconSize);
            }
            else
            {
                ImGui.Dummy(actionIconSize);
            }

            ImGui.SameLine(0, 6);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (actionIconSize.Y - ImGui.GetTextLineHeight()) / 2f);
            ImGui.Text($"{i + 1}. {skillNameZh}");
        }

        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button($"删除##deleteMacro_{macro.Id}", new Vector2(100, 0)))
        {
            macroLibrary.RemoveMacro(macro.Id);
            _selectedMacroId = null;
            GatherBuddy.Log.Debug($"[MacrosTab] Deleted macro '{macro.Name}'");
        }
    }

    private uint GetSkillIconId(uint skillId)
    {
        if (_skillIconCache.TryGetValue(skillId, out var cached))
            return cached;

        uint iconId = 0;
        try
        {
            if (skillId >= 100000)
            {
                var sheet = Dalamud.GameData.GetExcelSheet<CraftAction>();
                if (sheet != null && sheet.TryGetRow(skillId, out var row))
                    iconId = row.Icon;
            }
            else if (skillId > 0)
            {
                var sheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                if (sheet != null && sheet.TryGetRow(skillId, out var row))
                    iconId = row.Icon;
            }
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Debug($"[MacrosTab] Failed to get icon for skill {skillId}: {ex.Message}");
        }

        _skillIconCache[skillId] = iconId;
        return iconId;
    }

    public static readonly Dictionary<VulcanSkill, string> VulcanSkillNamesZh = new()
    {
        { VulcanSkill.None, "无" },
        { VulcanSkill.TouchCombo, "加工连段" },
        { VulcanSkill.TouchComboRefined, "加工连段(精炼加工路线)" },

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
}
