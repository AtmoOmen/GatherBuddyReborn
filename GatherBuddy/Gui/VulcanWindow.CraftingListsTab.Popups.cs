using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ElliLib.Raii;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private string  _newListName         = string.Empty;
    private bool    _newListEphemeral    = false;
    private string  _newListFolderPath   = string.Empty;
    private uint?   _newListRecipeId     = null;
    private string  _newListRecipeName   = string.Empty;
    private string  _newFolderName       = string.Empty;
    private string  _newFolderParentPath = string.Empty;
    private string  _importListText      = string.Empty;
    private bool    _importListEphemeral = false;
    private string? _importListError     = null;

    private void ResetCreateListPopupState()
    {
        _newListName = string.Empty;
        _newListEphemeral = false;
        _newListFolderPath = string.Empty;
        _newListRecipeId = null;
        _newListRecipeName = string.Empty;
        _openCreateListPopup = false;
    }

    private void ResetCreateFolderPopupState()
    {
        _newFolderName = string.Empty;
        _newFolderParentPath = string.Empty;
        _openCreateFolderPopup = false;
    }

    private void DrawImportListPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(540, 260), ImGuiCond.FirstUseEver);
        if (!ImGui.BeginPopupModal("ImportListPopup", ImGuiWindowFlags.None))
            return;

        ImGui.TextWrapped("将导出的清单字符串粘贴到下方, 然后点击“导入”");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##importListText", ref _importListText, 65536, new Vector2(-1, 120));

        if (_importListError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudRed, _importListError);
        }

        ImGui.Spacing();
        ImGui.Checkbox("临时##importListEphemeral", ref _importListEphemeral);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("制作完成后自动删除此清单\n可稍后在清单编辑器中关闭");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(_importListText)))
        {
            if (ImGui.Button("导入", new Vector2(120, 0)))
            {
                var (imported, error) = GatherBuddy.CraftingListManager.ImportList(_importListText);
                if (imported != null)
                {
                    if (_importListEphemeral)
                    {
                        imported.Ephemeral = true;
                        GatherBuddy.CraftingListManager.SaveList(imported);
                    }

                    OpenCraftingList(imported);
                    _deferEditorDraw    = true;
                    _importListText     = string.Empty;
                    _importListEphemeral = false;
                    _importListError    = null;
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    _importListError = error;
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("取消", new Vector2(100, 0)))
        {
            _importListText     = string.Empty;
            _importListEphemeral = false;
            _importListError    = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawCreateListPopup()
    {
        if (!ImGui.BeginPopupModal("CreateListPopup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (!string.IsNullOrEmpty(_newListFolderPath))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"文件夹: {CraftingListManager.FormatFolderPath(_newListFolderPath)}");
            ImGui.Spacing();
        }

        if (_newListRecipeId.HasValue)
        {
            ImGui.TextColored(ImGuiColors.ParsedGold, $"加入配方: {_newListRecipeName}");
            ImGui.Spacing();
        }

        ImGui.Text("输入清单名称:");
        ImGui.InputText("##newListName", ref _newListName, 256);

        if (!string.IsNullOrWhiteSpace(_newListName) && !GatherBuddy.CraftingListManager.IsNameUnique(_newListName))
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0, 1), "已存在同名清单");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "将自动重命名");
        }

        ImGui.Spacing();
        ImGui.Checkbox("临时##newListEphemeral", ref _newListEphemeral);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("制作完成后自动删除此清单\n可稍后在清单编辑器中关闭");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("创建", new Vector2(100, 0)) && !string.IsNullOrWhiteSpace(_newListName))
        {
            var newList = GatherBuddy.CraftingListManager.CreateNewList(_newListName, _newListEphemeral, _newListFolderPath);
            if (_newListRecipeId.HasValue)
            {
                newList.AddRecipe(_newListRecipeId.Value, 1);
                if (!GatherBuddy.CraftingListManager.SaveList(newList))
                    GatherBuddy.Log.Warning($"[VulcanWindow] Failed to save list '{newList.Name}' after adding {_newListRecipeName}");
                else
                    GatherBuddy.Log.Information($"[VulcanWindow] Created list '{newList.Name}' and added {_newListRecipeName}");
            }

            OpenCraftingList(newList);
            ResetCreateListPopupState();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("取消", new Vector2(100, 0)))
        {
            ResetCreateListPopupState();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawCreateFolderPopup()
    {
        if (!ImGui.BeginPopupModal("CreateFolderPopup", ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (!string.IsNullOrEmpty(_newFolderParentPath))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"上级文件夹: {CraftingListManager.FormatFolderPath(_newFolderParentPath)}");
            ImGui.Spacing();
        }

        ImGui.Text("输入文件夹名称:");
        ImGui.InputText("##newFolderName", ref _newFolderName, 256);

        var isAvailable = GatherBuddy.CraftingListManager.IsFolderNameAvailable(_newFolderName, _newFolderParentPath);
        if (!string.IsNullOrWhiteSpace(_newFolderName) && !isAvailable)
            ImGui.TextColored(ImGuiColors.DalamudRed, "当前目录下文件夹名称必须唯一, 且不能包含 / 或 \\");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using (ImRaii.Disabled(!isAvailable))
        {
            if (ImGui.Button("创建", new Vector2(100, 0)))
            {
                if (GatherBuddy.CraftingListManager.CreateFolder(_newFolderName, _newFolderParentPath))
                {
                    ResetCreateFolderPopupState();
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("取消", new Vector2(100, 0)))
        {
            ResetCreateFolderPopupState();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
