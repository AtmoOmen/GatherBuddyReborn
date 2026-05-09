using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using ElliLib;
using ElliLib.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using Lumina.Excel.Sheets;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private void DrawCraftingListsTab()
    {
        IDisposable tabItem;
        bool tabOpen;

        if (GatherBuddy.ControllerSupport != null && !_craftingListsRequestFocus)
        {
            var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("制作清单##craftingListsTab", 0, 9);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            ImRaii.IEndObject handle;
            if (_craftingListsRequestFocus)
            {
                bool dummy = true;
                handle = ImRaii.TabItem("制作清单##craftingListsTab", ref dummy, ImGuiTabItemFlags.SetSelected);
            }
            else
            {
                handle = ImRaii.TabItem("制作清单##craftingListsTab");
            }
            tabItem = handle;
            tabOpen = handle.Success;
            if (tabOpen)
                _craftingListsRequestFocus = false;
        }

        using (tabItem)
        {
            if (!tabOpen)
                return;

            DrawCraftingListsTabContent();
        }
    }
    
    private void DrawCraftingListsTabContent()
    {
        if (_openCreateListPopup)
        {
            ImGui.OpenPopup("CreateListPopup");
            _openCreateListPopup = false;
        }
        if (_openCreateFolderPopup)
        {
            ImGui.OpenPopup("CreateFolderPopup");
            _openCreateFolderPopup = false;
        }

        if (_editingList != null && _listEditor != null)
        {
            if (_deferEditorDraw)
            {
                _deferEditorDraw = false;
                ImGui.Text("加载中...");
            }
            else
            {
                var refreshedList = GatherBuddy.CraftingListManager.GetListByID(_editingList.ID);
                if (refreshedList == null)
                {
                    _editingList = null;
                    DisposeListEditor();
                    DrawListManager();
                }
                else
                {
                    _editingList = refreshedList;

                    if (ImGui.SmallButton("\u2190 清单##backToLists"))
                    {
                        _editingList = null;
                        DisposeListEditor();
                        DrawListManager();
                    }
                    else
                    {
                        ImGui.Spacing();
                        ImGui.TextColored(ImGuiColors.ParsedGold, _editingList.Name);
                        if (_editingList.Ephemeral)
                        {
                            var ephemeral = _editingList.Ephemeral;
                            if (ImGui.Checkbox("临时##listHeaderEphemeral", ref ephemeral))
                            {
                                _editingList.Ephemeral = ephemeral;
                                GatherBuddy.CraftingListManager.SaveList(_editingList);
                            }
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("制作完成后自动删除此清单\n手动停止时不会自动删除");
                        }
                        else
                        {
                            ImGui.TextColored(ImGuiColors.DalamudGrey3, "制作清单");
                        }
                        ImGui.Separator();
                        ImGui.Spacing();

                        if (_listEditor != null)
                            _listEditor.Draw();
                    }
                }
            }
        }
        else
        {
            DrawListManager();
        }

        DrawCreateListPopup();
        DrawCreateFolderPopup();
        DrawImportListPopup();
        DrawTeamCraftImportWindow();
    }

    private void DrawListManager()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow, "制作清单");
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("新建清单"))
        {
            PrepareCreateListPopup();
            ImGui.OpenPopup("CreateListPopup");
        }
        ImGui.SameLine();
        if (ImGui.Button("新建文件夹"))
            QueueCreateFolderPopup();
        ImGui.SameLine();
        if (ImGui.Button("导入 TeamCraft"))
            _showTeamCraftImport = true;
        ImGui.SameLine();
        if (ImGui.Button("导入清单"))
        {
            _importListText  = string.Empty;
            _importListError = null;
            ImGui.OpenPopup("ImportListPopup");
        }

        ImGui.Spacing();

        var avail  = ImGui.GetContentRegionAvail();
        var leftW  = 220f;
        var rightW = avail.X - leftW - ImGui.GetStyle().ItemSpacing.X;

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##listSelectorPanel", new Vector2(leftW, avail.Y), true);
            DrawListSelectorPanel();
            ImGui.EndChild();
        }

        ImGui.SameLine();

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.08f, 0.10f, 1.00f)))
        {
            ImGui.BeginChild("##listPreviewPanel", new Vector2(rightW, avail.Y), true);
            DrawListPreviewPanel();
            ImGui.EndChild();
        }

    }

    private void DrawListSelectorPanel()
    {
        var rootFolders = GatherBuddy.CraftingListManager.GetDirectSubfolderPaths();
        var rootLists = GatherBuddy.CraftingListManager.GetListsInFolder();
        if (rootFolders.Count == 0 && rootLists.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "还没有任何清单");
            ImGui.TextColored(ImGuiColors.DalamudGrey, "点击“新建清单”开始");
            return;
        }
        foreach (var folderPath in rootFolders)
            DrawListFolderNode(folderPath);

        foreach (var list in rootLists)
            DrawCraftingListSelectorEntry(list);
    }

    private void DrawListFolderNode(string folderPath)
    {
        var childFolders = GatherBuddy.CraftingListManager.GetDirectSubfolderPaths(folderPath);
        var childLists = GatherBuddy.CraftingListManager.GetListsInFolder(folderPath);
        var hasChildren = childFolders.Count > 0 || childLists.Count > 0;

        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (!hasChildren)
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

        var label = $"{CraftingListManager.GetFolderDisplayName(folderPath)}##folder_{folderPath}";
        var open = ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemHovered())
        {
            _previewFolderPath = folderPath;
            _previewList = null;
        }

        var isPopupOpen = GatherBuddy.ControllerSupport != null
            ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"FolderContextMenu_{folderPath}", Dalamud.GamepadState)
            : ImGui.BeginPopupContextItem($"FolderContextMenu_{folderPath}");
        if (isPopupOpen)
        {
            if (ImGui.Selectable("在此新建清单"))
            {
                PrepareCreateListPopup(folderPath);
                _openCreateListPopup = true;
                GatherBuddy.Log.Debug($"[VulcanWindow] Queued Create List popup for folder '{folderPath}'");
            }

            if (ImGui.Selectable("新建子文件夹"))
                QueueCreateFolderPopup(folderPath);

            var canDelete = GatherBuddy.CraftingListManager.CanDeleteFolder(folderPath);
            using (ImRaii.Disabled(!canDelete))
            {
                if (ImGui.Selectable("删除文件夹"))
                    GatherBuddy.CraftingListManager.DeleteFolder(folderPath);
            }
            if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("删除前需要先移动或删除此文件夹中的清单");

            ImGui.EndPopup();
        }

        if (!hasChildren || !open)
            return;

        foreach (var childFolderPath in childFolders)
            DrawListFolderNode(childFolderPath);

        foreach (var list in childLists)
            DrawCraftingListSelectorEntry(list);

        ImGui.TreePop();
    }

    private void DrawCraftingListSelectorEntry(CraftingListDefinition list)
    {
        var isHighlighted = _previewList?.ID == list.ID;
        if (isHighlighted)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedGold);

        if (ImGui.Selectable($"{list.Name}##list_{list.ID}", isHighlighted))
            OpenCraftingList(list);

        if (isHighlighted)
            ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            _previewList = list;
            _previewFolderPath = null;
        }

        var isPopupOpen = GatherBuddy.ControllerSupport != null
            ? GatherBuddy.ControllerSupport.ContextMenu.BeginPopupContextItemWithGamepad($"ListContextMenu_{list.ID}", Dalamud.GamepadState)
            : ImGui.BeginPopupContextItem($"ListContextMenu_{list.ID}");

        if (!isPopupOpen)
            return;

        if (ImGui.Selectable("编辑"))
            OpenCraftingList(list);

        if (ImGui.Selectable("开始制作"))
            StartCraftingList(list);

        if (ImGui.BeginMenu("移动到文件夹"))
        {
            var isRoot = string.IsNullOrEmpty(list.FolderPath);
            if (ImGui.MenuItem("根目录", string.Empty, isRoot) && !isRoot)
                GatherBuddy.CraftingListManager.MoveListToFolder(list, null);

            foreach (var folderPath in GatherBuddy.CraftingListManager.GetAllFolderPaths())
            {
                var isCurrentFolder = list.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase);
                if (ImGui.MenuItem(CraftingListManager.FormatFolderPath(folderPath), string.Empty, isCurrentFolder) && !isCurrentFolder)
                    GatherBuddy.CraftingListManager.MoveListToFolder(list, folderPath);
            }
            ImGui.EndMenu();
        }

        if (ImGui.Selectable("导出到剪贴板"))
        {
            var exported = GatherBuddy.CraftingListManager.ExportList(list.ID);
            if (exported != null)
            {
                ImGui.SetClipboardText(exported);
                GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to clipboard");
            }
        }

        if (ImGui.Selectable("导出到 TeamCraft"))
        {
            var (exported, error) = GatherBuddy.CraftingListManager.ExportListToTeamCraft(list.ID);
            if (exported != null)
            {
                ImGui.SetClipboardText(exported);
                GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to TeamCraft and copied the link to the clipboard");
            }
            else if (!string.IsNullOrEmpty(error))
            {
                GatherBuddy.Log.Warning($"[VulcanWindow] Failed to export '{list.Name}' to TeamCraft: {error}");
            }
        }

        ImGui.Separator();
        if (ImGui.Selectable("删除"))
        {
            if (_previewList?.ID == list.ID)
                _previewList = null;
            GatherBuddy.CraftingListManager.DeleteList(list.ID);
        }

        ImGui.EndPopup();
    }

    private void DrawListPreviewPanel()
    {
        if (!string.IsNullOrEmpty(_previewFolderPath))
        {
            DrawFolderPreviewPanel(_previewFolderPath);
            return;
        }
        if (_previewList == null)
        {
            var h = ImGui.GetContentRegionAvail().Y;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + h / 2f - ImGui.GetTextLineHeight());
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
            ImGui.TextColored(ImGuiColors.DalamudGrey, "将鼠标悬停在清单或文件夹上即可预览");
            return;
        }

        var list = GatherBuddy.CraftingListManager.GetListByID(_previewList.ID);
        if (list == null)
        {
            _previewList = null;
            return;
        }
        _previewList = list;

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.TextColored(ImGuiColors.ParsedGold, list.Name);

        if (!string.IsNullOrEmpty(list.FolderPath))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
            ImGui.TextColored(ImGuiColors.DalamudGrey3, $"文件夹: {CraftingListManager.FormatFolderPath(list.FolderPath)}");
        }

        if (!string.IsNullOrWhiteSpace(list.Description))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey3))
                ImGui.TextWrapped(list.Description);
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.TextColored(ImGuiColors.DalamudGrey3,
            $"{list.Recipes.Count} 个配方  \u00b7  创建于 {list.CreatedAt.ToLocalTime():yyyy-MM-dd}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var style   = ImGui.GetStyle();
        var buttonH = 22f * 2 + style.ItemSpacing.Y * 3 + 4f;
        var listH   = Math.Max(ImGui.GetContentRegionAvail().Y - buttonH, 40f);

        ImGui.BeginChild("##previewRecipeList", new Vector2(-1, listH), false);

        if (list.Recipes.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "此清单中还没有配方");
        }
        else
        {
            var iconSz = new Vector2(22f, 22f);
            var rowHeight = iconSz.Y + ImGui.GetStyle().ItemSpacing.Y;
            var clipper = ImGui.ImGuiListClipper();
            clipper.Begin(list.Recipes.Count, rowHeight);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var item = list.Recipes[i];
                    var recipe = RecipeManager.GetRecipe(item.RecipeId);
                    if (recipe == null)
                    {
                        ImGui.Dummy(new Vector2(0, rowHeight));
                        continue;
                    }

                    var resultItem = recipe.Value.ItemResult.Value;
                    var textY = ImGui.GetCursorPosY() + (iconSz.Y - ImGui.GetTextLineHeight()) / 2f;
                    var icon = Icons.DefaultStorage.TextureProvider
                        .GetFromGameIcon(new GameIconLookup(resultItem.Icon));
                    if (icon.TryGetWrap(out var wrap, out _))
                        ImGui.Image(wrap.Handle, iconSz);
                    else
                        ImGui.Dummy(iconSz);

                    ImGui.SameLine(0, 6);
                    ImGui.SetCursorPosY(textY);
                    ImGui.Text(resultItem.Name.ExtractText());
                    ImGui.SameLine();
                    ImGui.SetCursorPosY(textY);
                    ImGui.TextColored(ImGuiColors.DalamudGrey3,
                        $"x{item.Quantity}  ({JobNames[recipe.Value.CraftType.RowId]})");
                }
            }
            clipper.End();
            clipper.Destroy();
        }

        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Spacing();

        var halfW = (ImGui.GetContentRegionAvail().X - style.ItemSpacing.X) / 2f;
        if (ImGui.Button("编辑清单##previewEdit", new Vector2(halfW, 22)))
            OpenCraftingList(list);
        ImGui.SameLine();
        if (IPCSubscriber.IsReady("Artisan"))
        {
            ImGuiUtil.DrawDisabledButton("检测到 Artisan##previewStart", new Vector2(-1, ImGui.GetFrameHeight()),
                "Artisan 插件已加载, 请卸载 Artisan 后使用 Vulcan 制作系统", true);
        }
        else if (ImGui.Button("开始制作##previewStart"))
        {
            StartCraftingList(list);
            MinimizeWindow();
        }

        if (ImGui.Button("导出##previewExport"))
        {
            var exported = GatherBuddy.CraftingListManager.ExportList(list.ID);
            if (exported != null)
            {
                ImGui.SetClipboardText(exported);
                GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to clipboard");
            }
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.45f, 0.12f, 0.12f, 1f)))
        {
            if (ImGui.Button("删除##previewDelete", new Vector2(-1, 22)))
            {
                GatherBuddy.CraftingListManager.DeleteList(list.ID);
                _previewList = null;
            }
        }
    }

    private void DrawFolderPreviewPanel(string folderPath)
    {
        if (!GatherBuddy.CraftingListManager.GetAllFolderPaths().Any(path => path.Equals(folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            _previewFolderPath = null;
            return;
        }

        var entries = GetFolderPreviewEntries(folderPath);

        ImGui.Spacing();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.TextColored(ImGuiColors.ParsedGold, CraftingListManager.GetFolderDisplayName(folderPath));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"文件夹: {CraftingListManager.FormatFolderPath(folderPath)}");
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.TextColored(ImGuiColors.DalamudGrey3, $"此文件夹树中共有 {entries.Count} 个清单");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginChild("##previewFolderList", new Vector2(-1, 0), false);
        if (entries.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "此文件夹中没有清单");
        }
        else
        {
            foreach (var (label, list) in entries)
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey3, label);
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudGrey, $"· {list.Recipes.Count} 个配方");
            }
        }
        ImGui.EndChild();
    }

    private List<(string Label, CraftingListDefinition List)> GetFolderPreviewEntries(string folderPath, string? labelPrefix = null)
    {
        var entries = new List<(string Label, CraftingListDefinition List)>();

        foreach (var list in GatherBuddy.CraftingListManager.GetListsInFolder(folderPath).OrderBy(list => list.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrEmpty(labelPrefix)
                ? list.Name
                : $"{labelPrefix} / {list.Name}";
            entries.Add((label, list));
        }

        foreach (var childFolderPath in GatherBuddy.CraftingListManager.GetDirectSubfolderPaths(folderPath))
        {
            var childPrefix = string.IsNullOrEmpty(labelPrefix)
                ? CraftingListManager.GetFolderDisplayName(childFolderPath)
                : $"{labelPrefix} / {CraftingListManager.GetFolderDisplayName(childFolderPath)}";
            entries.AddRange(GetFolderPreviewEntries(childFolderPath, childPrefix));
        }

        return entries;
    }


}
