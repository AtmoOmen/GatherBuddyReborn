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
    private UserMacro? _viewingMacro = null;
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
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("宏##macrosTab", 2, 7);
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

        ImGui.TextWrapped("从 Teamcraft 导入制作宏, 只需粘贴游戏内宏格式。");
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("宏行为##macroBehaviorSection"))
        {
            ImGui.Spacing();
            var skipUnusable = GatherBuddy.Config.SkipMacroStepIfUnable;
            if (ImGui.Checkbox("无法使用技能时跳过宏步骤##skipUnusable", ref skipUnusable))
            {
                GatherBuddy.Config.SkipMacroStepIfUnable = skipUnusable;
                GatherBuddy.Config.Save();
            }
            var fallbackEnabled = GatherBuddy.Config.MacroFallbackEnabled;
            if (ImGui.Checkbox("宏执行完毕时使用备用求解器继续制作##fallbackEnabled", ref fallbackEnabled))
            {
                GatherBuddy.Config.MacroFallbackEnabled = fallbackEnabled;
                GatherBuddy.Config.Save();
            }
            ImGui.Spacing();
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("导入宏##inGameSection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawInGameMacroSection();
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("已保存的宏##savedSection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawSavedMacrosSection();
        }
        }
    }

    private void DrawInGameMacroSection()
    {
        ImGui.Spacing();
        
        if (ImGui.Button("浏览 Teamcraft##browseTC", new Vector2(200, 0)))
        {
            try
            {
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft disabled off");
                Dalamud.Commands.ProcessCommand("/bw overlay teamcraft url https://ffxivteamcraft.com/community-rotations");
                GatherBuddy.Log.Information("在 Browsingway 覆盖层中打开 Teamcraft");
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
                "2. 运行 /bw config\n" +
                "3. 创建一个新的覆盖层 (+ 按钮)\n" +
                "4. 将命令名称设置为 'teamcraft'\n" +
                "5. 关闭配置并点击此按钮\n\n" +
                "完成后使用“隐藏覆盖层”按钮关闭覆盖层。\n\n" +
                "或者直接在浏览器中访问 https://ffxivteamcraft.com/community-rotations");
        
        ImGui.SameLine();
        if (ImGui.Button("隐藏覆盖层##hideTC", new Vector2(150, 0)))
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
            ImGui.TextColored(ImGuiColors.DalamudYellow, "未找到或未加载 Browsingway 插件。");
            ImGui.TextWrapped("您可以在浏览器中打开 Teamcraft 并在下方粘贴宏。");
            ImGui.EndPopup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("从 Teamcraft 粘贴制作宏(在宏页面使用“转换为游戏内宏”按钮)。");
        ImGui.Spacing();

        ImGui.Text("宏名称:");
        ImGui.SetNextItemWidth(300);
        ImGui.InputTextWithHint("##macroName", "输入宏名称...", ref _inGameMacroName, 100);

        ImGui.Spacing();
        ImGui.Text("粘贴宏文本:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##macroText", ref _inGameMacroText, 10000, new Vector2(-1, 200));

        ImGui.Spacing();

        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(_inGameMacroText)))
        {
            if (ImGui.Button("解析宏##parseBtn", new Vector2(150, 0)))
            {
                ParseInGameMacro();
            }
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
        ImGui.TextColored(ImGuiColors.ParsedGreen, "宏预览");
        ImGui.Spacing();

        ImGui.Text($"名称: {macro.Name}");
        ImGui.Text($"技能数量: {macro.Actions.Count}");

        ImGui.Spacing();
        ImGui.Text("最低属性 (可选 - 用于验证):");
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("作业精度##previewMinCraft", ref _previewMinCraft);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("加工精度##previewMinCtrl", ref _previewMinCtrl);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.InputInt("制作力##previewMinCP", ref _previewMinCP);
        _previewMinCraft = Math.Max(0, _previewMinCraft);
        _previewMinCtrl  = Math.Max(0, _previewMinCtrl);
        _previewMinCP    = Math.Max(0, _previewMinCP);

        ImGui.Spacing();

        if (ImGui.Button("导入宏##importInGameBtn", new Vector2(150, 0)))
        {
            macro.MinCraftsmanship = _previewMinCraft;
            macro.MinControl       = _previewMinCtrl;
            macro.MinCP            = _previewMinCP;
            ImportInGameMacro(macro);
        }

        ImGui.SameLine();
        if (ImGui.Button("取消##cancelInGameBtn", new Vector2(100, 0)))
        {
            _previewInGameMacro = null;
            _inGameMacroError   = null;
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
                _previewMinCraft    = 0;
                _previewMinCtrl     = 0;
                _previewMinCP       = 0;
            }
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"Failed to parse macro: {ex.Message}";
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
            
            _previewInGameMacro = null;
            _inGameMacroText = string.Empty;
            _inGameMacroName = string.Empty;
            _inGameMacroError = null;
        }
        catch (Exception ex)
        {
            _inGameMacroError = $"Failed to import macro: {ex.Message}";
            GatherBuddy.Log.Error($"Failed to import in-game macro: {ex.Message}");
        }
    }


    private void DrawSavedMacrosSection()
    {
        var macroLibrary = CraftingGameInterop.UserMacroLibrary;
        var allMacros = macroLibrary.GetAllMacros();

        if (allMacros.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "未保存任何宏, 请从 Teamcraft 导入一些!");
            return;
        }

        ImGui.Spacing();
        ImGui.Text($"宏总数: {allMacros.Count}");
        ImGui.Spacing();

        using var table = ImRaii.Table("##macrosTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg);
        if (!table)
            return;

        ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("技能数量", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("属性", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("来源", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        foreach (var macro in allMacros)
        {
            ImGui.TableNextRow();
            
            ImGui.TableNextColumn();
            ImGui.Text(macro.Name);
            if (!string.IsNullOrEmpty(macro.Author))
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudGrey3, $"作者: {macro.Author}");
            }

            ImGui.TableNextColumn();
            ImGui.Text(macro.Actions.Count.ToString());

            ImGui.TableNextColumn();
            if (macro.MinCraftsmanship > 0 || macro.MinControl > 0 || macro.MinCP > 0)
                ImGui.Text($"{macro.MinCraftsmanship}/{macro.MinControl}/{macro.MinCP}");
            else
                ImGui.TextColored(ImGuiColors.DalamudGrey, "无");

            ImGui.TableNextColumn();
            ImGui.Text(macro.Source);

            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"查看##{macro.Id}"))
            {
                _viewingMacro = macro;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton($"删除##{macro.Id}"))
            {
                macroLibrary.RemoveMacro(macro.Id);
            }
        }
        
        DrawMacroDetailsPopup();
    }

    private void DrawMacroDetailsPopup()
    {
        if (_viewingMacro == null)
            return;

        bool isOpen = true;
        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"宏详情: {_viewingMacro.Name}##macroDetails", ref isOpen, ImGuiWindowFlags.None))
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, _viewingMacro.Name);
            ImGui.Separator();
            ImGui.Spacing();

            if (!string.IsNullOrEmpty(_viewingMacro.Author))
                ImGui.Text($"作者: {_viewingMacro.Author}");
            
            ImGui.Text($"来源: {_viewingMacro.Source}");
            ImGui.Text($"技能总数: {_viewingMacro.Actions.Count}");

            if (_editingMacroStatsId != _viewingMacro.Id)
            {
                _editingMacroStatsId  = _viewingMacro.Id;
                _editingMacroMinCraft = _viewingMacro.MinCraftsmanship;
                _editingMacroMinCtrl  = _viewingMacro.MinControl;
                _editingMacroMinCP    = _viewingMacro.MinCP;
            }

            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudYellow, "最低属性 (用于验证):");
            ImGui.SetNextItemWidth(120);
            ImGui.InputInt("作业精度##editMinCraft", ref _editingMacroMinCraft);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.InputInt("加工精度##editMinCtrl", ref _editingMacroMinCtrl);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.InputInt("制作力##editMinCP", ref _editingMacroMinCP);
            _editingMacroMinCraft = Math.Max(0, _editingMacroMinCraft);
            _editingMacroMinCtrl  = Math.Max(0, _editingMacroMinCtrl);
            _editingMacroMinCP    = Math.Max(0, _editingMacroMinCP);
            ImGui.SameLine();
            if (ImGui.SmallButton("保存属性##saveStats"))
            {
                _viewingMacro.MinCraftsmanship = _editingMacroMinCraft;
                _viewingMacro.MinControl       = _editingMacroMinCtrl;
                _viewingMacro.MinCP            = _editingMacroMinCP;
                MacroValidator.InvalidateByMacroId(_viewingMacro.Id);
                CraftingGameInterop.UserMacroLibrary.Save();
                GatherBuddy.Log.Debug($"[MacrosTab] Saved min stats for macro '{_viewingMacro.Name}'");
            }
            
            ImGui.Text($"创建时间: {_viewingMacro.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
            
            if (!string.IsNullOrEmpty(_viewingMacro.TeamcraftUrl))
            {
                ImGui.Text($"链接: {_viewingMacro.TeamcraftUrl}");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.ParsedGold, "技能:");
            ImGui.Spacing();

            ImGui.BeginChild("##actionsList", new Vector2(-1, -30), true);
            var iconSize = new Vector2(24, 24);
            for (int i = 0; i < _viewingMacro.Actions.Count; i++)
            {
                var actionId = _viewingMacro.Actions[i];
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
                        ImGui.Image(wrap.Handle, iconSize);
                    else
                        ImGui.Dummy(iconSize);
                }
                else
                    ImGui.Dummy(iconSize);
                
                ImGui.SameLine(0, 6);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (iconSize.Y - ImGui.GetTextLineHeight()) / 2);
                ImGui.Text($"{i + 1}. {skillNameZh}");
            }
            ImGui.EndChild();

            ImGui.Spacing();
            if (ImGui.Button("关闭##closeMacroDetails", new Vector2(100, 0)))
                _viewingMacro = null;
        }

        ImGui.End();

        if (!isOpen)
            _viewingMacro = null;
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
