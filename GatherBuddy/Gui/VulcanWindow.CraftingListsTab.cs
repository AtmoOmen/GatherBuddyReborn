using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using GatherBuddy.Crafting;
using GatherBuddy.Plugin;
using GatherBuddy.RebornUI;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;
using ImRaii = Dalamud.Interface.Utility.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private const string CraftingListDragDropPayload = "GatherBuddyCraftingListDragDrop";
    private const string DeleteListDialogId = "删除制作清单###gbr_delete_crafting_list";
    private const string ArtisanLoadedTooltip = "Artisan 已加载。请卸载 Artisan 后使用 Vulcan 的制作功能。";
    private const float CraftingListHoldSeconds = 0.6f;
    private int? _draggedCraftingListId = null;
    private int? _pendingDeleteListId;
    private bool _openDeleteListDialog;

    private void DrawCraftingListsPage()
        => DrawCraftingListsTabContent();

    private void DrawCraftingListsTabContent()
    {
        if (_openCreateListPopup)
        {
            M3Dialog.Open(CreateListDialogId);
            _openCreateListPopup = false;
        }

        if (_openCreateFolderPopup)
        {
            M3Dialog.Open(CreateFolderDialogId);
            _openCreateFolderPopup = false;
        }

        if (_openDeleteListDialog)
        {
            M3Dialog.Open(DeleteListDialogId);
            _openDeleteListDialog = false;
        }

        if (_editingList != null && _listEditor != null)
        {
            DrawOpenCraftingList();
        }
        else
        {
            DrawListManager();
        }

        DrawCreateListPopup();
        DrawCreateFolderPopup();
        DrawImportListPopup();
        DrawDeleteListDialog();
    }

    private void DrawOpenCraftingList()
    {
        if (_deferEditorDraw)
        {
            _deferEditorDraw = false;
            M3Text.Draw("加载中…", M3TextStyle.Muted);
            return;
        }

        var refreshedList = GatherBuddy.CraftingListManager.GetListByID(_editingList!.ID);
        if (refreshedList == null)
        {
            CloseCraftingList();
            DrawListManager();
            return;
        }

        _editingList = refreshedList;
        if (DrawOpenListHeader(refreshedList))
        {
            CloseCraftingList();
            DrawListManager();
            return;
        }

        _listEditor?.Draw();
    }

    private void CloseCraftingList()
    {
        _editingList = null;
        DisposeListEditor();
    }

    private static bool DrawOpenListHeader(CraftingListDefinition list)
    {
        var buttonSize = M3Widgets.IconButtonSize;
        float titleHeight;
        using (ImRaii.PushFont(M3.TitleLarge))
        {
            titleHeight = ImGui.CalcTextSize(list.Name).Y;
        }

        var recipeCount = list.Recipes.Count;
        var location = string.IsNullOrEmpty(list.FolderPath) ? "Crafting list" : CraftingListManager.FormatFolderPath(list.FolderPath);
        var subtitle = $"{location} · {recipeCount} {(recipeCount == 1 ? "recipe" : "recipes")}";
        var blockHeight = titleHeight + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight();

        var row = new Ui.Row(MathF.Max(buttonSize, blockHeight));
        row.Place(buttonSize);
        var back = M3Widgets.IconButton("##crafting_list_back", FontAwesomeIcon.ArrowLeft, "返回制作清单");

        row.Next(blockHeight, M3.Space2);
        ImGui.BeginGroup();
        using (ImRaii.PushFont(M3.TitleLarge))
        {
            ImGui.TextUnformatted(list.Name);
        }

        M3Text.Draw(subtitle, M3TextStyle.Muted);
        ImGui.EndGroup();

        if (list.Ephemeral)
        {
            row.Next(M3Widgets.ChipHeight, M3.Space3);
            if (M3Widgets.Chip("##crafting_list_ephemeral", "临时清单", true, FontAwesomeIcon.HourglassHalf,
                    "制作完成后自动删除；手动停止不受影响。\n点击保留清单。"))
            {
                list.Ephemeral = false;
                GatherBuddy.CraftingListManager.SaveList(list);
                M3Snackbar.Show($"制作完成后将保留 {list.Name}。");
            }
        }

        ImGui.Dummy(new Vector2(0f, M3.Space1));
        return back;
    }

    private void DrawListManager()
    {
        DrawListManagerToolbar();

        var avail = ImGui.GetContentRegionAvail();
        var leftWidth = Math.Clamp(avail.X * 0.3f, VulcanUiScaling.Scaled(220f), VulcanUiScaling.Scaled(340f));
        leftWidth = MathF.Min(leftWidth, MathF.Max(1f, avail.X * 0.6f));

        using (var left = M3Panel.Begin("##listSelectorPanel", new Vector2(leftWidth, avail.Y), fill: VulcanUiStyle.PanelBackground))
        {
            if (left)
            {
                DrawListSelectorPanel();
            }
        }

        ImGui.SameLine();
        using var right = ImRaii.Child("##listPreviewPanel", new Vector2(0f, avail.Y), false);
        if (right)
        {
            DrawListPreviewPanel();
        }
    }

    private void DrawListManagerToolbar()
    {
        const string newListLabel = "新建清单";
        const string newFolderLabel = "新建文件夹";
        const string importLabel = "导入";
        const string teamCraftLabel = "从 TeamCraft 导入";

        var flow = new M3Flow();
        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Plus, newListLabel));
        if (M3Widgets.Button("##crafting_new_list", newListLabel, M3ButtonStyle.Filled, FontAwesomeIcon.Plus))
        {
            PrepareCreateListPopup();
            M3Dialog.Open(CreateListDialogId);
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.FolderPlus, newFolderLabel));
        if (M3Widgets.Button("##crafting_new_folder", newFolderLabel, M3ButtonStyle.Tonal, FontAwesomeIcon.FolderPlus))
        {
            QueueCreateFolderPopup();
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.FileImport, importLabel));
        if (M3Widgets.Button("##crafting_import", importLabel, M3ButtonStyle.Text, FontAwesomeIcon.FileImport,
                tooltip: "导入从 Vulcan 导出的清单"))
        {
            _importListText = string.Empty;
            _importListError = null;
            M3Dialog.Open(ImportListDialogId);
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.FileDownload, teamCraftLabel));
        if (M3Widgets.Button("##crafting_teamcraft_import", teamCraftLabel, M3ButtonStyle.Text, FontAwesomeIcon.FileDownload,
                tooltip: "根据 TeamCraft 清单中的「最终物品」创建清单"))
        {
            OpenTeamCraftImport();
        }

        ImGui.Dummy(new Vector2(0f, M3.Space1));
    }

    private void DrawListSelectorPanel()
    {
        var manager = GatherBuddy.CraftingListManager;
        var rootFolders = manager.GetDirectSubfolderPaths();
        var rootLists = manager.GetListsInFolder();
        M3Widgets.SectionLabel($"清单 · {manager.Lists.Count}");
        if (rootFolders.Count == 0 && rootLists.Count == 0)
        {
            M3Widgets.EmptyState("##crafting_lists_none", "尚无清单", "可新建或导入清单。", FontAwesomeIcon.ClipboardList);
            return;
        }

        using var scroll = ImRaii.Child("##listSelectorScroll", new Vector2(-1f, 0f), false);
        if (!scroll)
        {
            return;
        }

        foreach (var folderPath in rootFolders)
        {
            DrawListFolderNode(folderPath);
        }

        foreach (var list in rootLists)
        {
            DrawCraftingListSelectorEntry(list);
        }
    }

    private void DrawListFolderNode(string folderPath)
    {
        var manager = GatherBuddy.CraftingListManager;
        var childFolders = manager.GetDirectSubfolderPaths(folderPath);
        var childLists = manager.GetListsInFolder(folderPath);
        var hasChildren = childFolders.Count > 0 || childLists.Count > 0;
        var highlighted = _previewFolderPath != null && _previewFolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase);

        using var node = M3Tree.Node($"##folder_{folderPath}", CraftingListManager.GetFolderDisplayName(folderPath), highlighted, !hasChildren,
            FontAwesomeIcon.Folder, trailing: manager.CountListsInFolderTree(folderPath).ToString());
        if (ImGui.IsItemHovered())
        {
            _previewFolderPath = folderPath;
            _previewList = null;
        }

        DrawCraftingListFolderDropTarget(folderPath);
        DrawFolderContextMenu(folderPath);

        if (!node.Open)
        {
            return;
        }

        foreach (var childFolderPath in childFolders)
        {
            DrawListFolderNode(childFolderPath);
        }

        foreach (var list in childLists)
        {
            DrawCraftingListSelectorEntry(list);
        }
    }

    private void DrawFolderContextMenu(string folderPath)
    {
        using var menu = M3Menu.BeginContext($"FolderContextMenu_{folderPath}");
        if (!menu.IsOpen)
        {
            return;
        }

        if (M3Menu.Item("##folder_new_list", "在此新建清单", FontAwesomeIcon.Plus))
        {
            PrepareCreateListPopup(folderPath);
            _openCreateListPopup = true;
            GatherBuddy.Log.Debug($"[VulcanWindow] Queued Create List popup for folder '{folderPath}'");
        }

        if (M3Menu.Item("##folder_new_subfolder", "新建子文件夹", FontAwesomeIcon.FolderPlus))
        {
            QueueCreateFolderPopup(folderPath);
        }

        M3Menu.Divider();
        var canDelete = GatherBuddy.CraftingListManager.CanDeleteFolder(folderPath);
        if (M3Menu.Item("##folder_delete", "删除文件夹", FontAwesomeIcon.Trash, canDelete,
                tooltip: canDelete ? null : "请先移动或删除此文件夹内的清单。"))
        {
            GatherBuddy.CraftingListManager.DeleteFolder(folderPath);
        }
    }

    private void DrawCraftingListSelectorEntry(CraftingListDefinition list)
    {
        var isHighlighted = _previewList?.ID == list.ID;
        using var node = M3Tree.Node($"##list_{list.ID}", list.Name, isHighlighted, true,
            list.Ephemeral ? FontAwesomeIcon.HourglassHalf : FontAwesomeIcon.ClipboardList,
            tooltip: list.Ephemeral ? $"{list.Name}\n临时清单：制作完成后自动删除。" : null,
            trailing: list.Recipes.Count.ToString());
        if (node.Clicked)
        {
            OpenCraftingList(list);
        }

        DrawCraftingListDragDropSource(list);
        DrawCraftingListEntryDropTarget(list);

        if (ImGui.IsItemHovered())
        {
            _previewList = list;
            _previewFolderPath = null;
        }

        DrawListContextMenu(list);
    }

    private void DrawListContextMenu(CraftingListDefinition list)
    {
        using var menu = M3Menu.BeginContext($"ListContextMenu_{list.ID}");
        if (!menu.IsOpen)
        {
            return;
        }

        if (M3Menu.Item("##list_edit", "打开", FontAwesomeIcon.Pen))
        {
            OpenCraftingList(list);
        }

        var artisanLoaded = IPCSubscriber.IsReady("Artisan");
        if (M3Menu.Item("##list_start", "开始制作", FontAwesomeIcon.Play, !artisanLoaded, tooltip: artisanLoaded ? ArtisanLoadedTooltip : null))
        {
            StartCraftingList(list);
        }

        var picked = false;
        using (var move = M3Menu.BeginSub("##list_move", "移动到文件夹", FontAwesomeIcon.FolderOpen))
        {
            if (move.IsOpen)
            {
                var isRoot = string.IsNullOrEmpty(list.FolderPath);
                if (M3Menu.Item("##move_root", "无文件夹", selected: isRoot))
                {
                    picked = true;
                    if (!isRoot)
                    {
                        GatherBuddy.CraftingListManager.MoveListToFolder(list, null);
                    }
                }

                foreach (var folderPath in GatherBuddy.CraftingListManager.GetAllFolderPaths())
                {
                    var isCurrentFolder = list.FolderPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase);
                    if (M3Menu.Item($"##move_{folderPath}", CraftingListManager.FormatFolderPath(folderPath), selected: isCurrentFolder))
                    {
                        picked = true;
                        if (!isCurrentFolder)
                        {
                            GatherBuddy.CraftingListManager.MoveListToFolder(list, folderPath);
                        }
                    }
                }
            }
        }

        if (picked)
        {
            M3Menu.Close();
        }

        if (M3Menu.Item("##list_export", "复制导出字符串", FontAwesomeIcon.Copy))
        {
            CopyListExport(list);
        }

        if (M3Menu.Item("##list_export_teamcraft", "复制 TeamCraft 链接", FontAwesomeIcon.Link))
        {
            CopyTeamCraftLink(list);
        }

        M3Menu.Divider();
        if (M3Menu.Item("##list_delete", "删除…", FontAwesomeIcon.Trash))
        {
            _pendingDeleteListId = list.ID;
            _openDeleteListDialog = true;
        }
    }

    internal static void CopyListExport(CraftingListDefinition list)
    {
        var exported = GatherBuddy.CraftingListManager.ExportList(list.ID);
        if (exported == null)
        {
            M3Snackbar.Show($"无法导出 {list.Name}。", severity: M3Severity.Error);
            return;
        }

        ImGui.SetClipboardText(exported);
        GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to clipboard");
        M3Snackbar.Show($"已复制 {list.Name} 的导出字符串。", severity: M3Severity.Success);
    }

    internal static void CopyTeamCraftLink(CraftingListDefinition list)
    {
        var (exported, error) = GatherBuddy.CraftingListManager.ExportListToTeamCraft(list.ID);
        if (exported != null)
        {
            ImGui.SetClipboardText(exported);
            GatherBuddy.Log.Information($"[VulcanWindow] Exported list '{list.Name}' to TeamCraft and copied the link to the clipboard");
            M3Snackbar.Show($"已复制 {list.Name} 的 TeamCraft 链接。", severity: M3Severity.Success);
        }
        else if (!string.IsNullOrEmpty(error))
        {
            GatherBuddy.Log.Warning($"[VulcanWindow] Failed to export '{list.Name}' to TeamCraft: {error}");
            M3Snackbar.Show($"无法创建 TeamCraft 链接：{error}", severity: M3Severity.Error);
        }
    }

    private void DeleteCraftingList(CraftingListDefinition list)
    {
        if (_previewList?.ID == list.ID)
        {
            _previewList = null;
        }

        GatherBuddy.CraftingListManager.DeleteList(list.ID);
        M3Snackbar.Show($"已删除 {list.Name}。");
    }

    private void DrawDeleteListDialog()
    {
        if (!ImGui.IsPopupOpen(DeleteListDialogId))
        {
            return;
        }

        var list = _pendingDeleteListId is int listId ? GatherBuddy.CraftingListManager.GetListByID(listId) : null;
        var supporting = list == null
            ? "此清单已不存在。"
            : $"{list.Name} and its {list.Recipes.Count} {(list.Recipes.Count == 1 ? "recipe" : "recipes")} will be removed. This can't be undone.";
        using var dialog = M3Dialog.Begin(DeleteListDialogId, "删除此清单？", supporting, FontAwesomeIcon.Trash);
        if (!dialog.IsOpen)
        {
            return;
        }

        var pressed = M3Dialog.Actions(
        [
            new M3DialogAction("取消"),
            new M3DialogAction("删除", M3ButtonStyle.Danger, FontAwesomeIcon.Trash, list != null),
        ]);
        if (pressed == 1 && list != null)
        {
            DeleteCraftingList(list);
        }

        if (pressed >= 0)
        {
            _pendingDeleteListId = null;
        }
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
            ImGui.Dummy(new Vector2(0f, M3.Space3 * 2f));
            M3Widgets.EmptyState("##crafting_list_preview_none", "没有可预览的项目",
                "悬停在清单或文件夹上可预览，点击清单可打开。", FontAwesomeIcon.MousePointer);
            return;
        }

        var list = GatherBuddy.CraftingListManager.GetListByID(_previewList.ID);
        if (list == null)
        {
            _previewList = null;
            return;
        }

        _previewList = list;
        DrawListPreviewHeader(list);
        DrawListPreviewActions(list);
        M3Widgets.SectionLabel("配方");

        using var recipes = ImRaii.Child("##previewRecipeList", new Vector2(-1f, 0f), false);
        if (recipes)
        {
            DrawPreviewRecipes(list);
        }
    }

    private static void DrawListPreviewHeader(CraftingListDefinition list)
    {
        using (ImRaii.PushFont(M3.TitleLarge))
        {
            ImGui.TextWrapped(list.Name);
        }

        var recipeCount = list.Recipes.Count;
        var details = $"{recipeCount} {(recipeCount == 1 ? "recipe" : "recipes")} · Created {list.CreatedAt.ToLocalTime():yyyy-MM-dd}";
        if (!string.IsNullOrEmpty(list.FolderPath))
        {
            details = $"{CraftingListManager.FormatFolderPath(list.FolderPath)} · {details}";
        }

        M3Text.Draw(details, M3TextStyle.Muted, true);
        if (list.Ephemeral)
        {
            M3Widgets.Pill("##preview_ephemeral", "临时清单", M3.Scheme.Tertiary, FontAwesomeIcon.HourglassHalf,
                "制作完成后自动删除。");
        }

        if (!string.IsNullOrWhiteSpace(list.Description))
        {
            ImGui.Dummy(new Vector2(0f, M3.Space1));
            M3Text.Draw(list.Description, M3TextStyle.Muted, true);
        }

        ImGui.Dummy(new Vector2(0f, M3.Space1));
    }

    private void DrawListPreviewActions(CraftingListDefinition list)
    {
        const string openLabel = "打开";
        const string startLabel = "开始制作";
        const string exportLabel = "复制导出字符串";
        const string deleteLabel = "删除";

        var artisanLoaded = IPCSubscriber.IsReady("Artisan");
        var flow = new M3Flow();
        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Pen, openLabel));
        if (M3Widgets.Button("##preview_edit", openLabel, M3ButtonStyle.Filled, FontAwesomeIcon.Pen))
        {
            OpenCraftingList(list);
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Play, startLabel));
        if (M3Widgets.Button("##preview_start", startLabel, M3ButtonStyle.Tonal, FontAwesomeIcon.Play, enabled: !artisanLoaded,
                tooltip: artisanLoaded ? ArtisanLoadedTooltip : null))
        {
            StartCraftingList(list);
            MinimizeWindow();
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Copy, exportLabel));
        if (M3Widgets.Button("##preview_export", exportLabel, M3ButtonStyle.Text, FontAwesomeIcon.Copy,
                tooltip: "将此清单的导出字符串复制到剪贴板"))
        {
            CopyListExport(list);
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Trash, deleteLabel));
        if (M3Widgets.HoldButton("##preview_delete", deleteLabel, CraftingListHoldSeconds, M3ButtonStyle.Danger, FontAwesomeIcon.Trash,
                tooltip: "按住以删除此清单"))
        {
            DeleteCraftingList(list);
        }

        ImGui.Dummy(new Vector2(0f, M3.Space1));
    }

    private static void DrawPreviewRecipes(CraftingListDefinition list)
    {
        if (list.Recipes.Count == 0)
        {
            M3Text.Draw("清单中尚无配方。", M3TextStyle.Muted);
            return;
        }

        var s = M3.Scheme;
        var scale = M3.Scale;
        var iconSize = VulcanUiScaling.Scaled(22f);
        var rowHeight = iconSize + 6f * scale;
        var lineHeight = ImGui.GetTextLineHeight();
        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(list.Recipes.Count);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
            {
                var item = list.Recipes[i];
                ImGui.Dummy(new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X), rowHeight));
                var recipe = RecipeManager.GetRecipe(item.RecipeId);
                if (recipe == null)
                {
                    continue;
                }

                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();
                var resultItem = recipe.Value.ItemResult.Value;
                var icon = M3ActionIcon.GameIcon(resultItem.Icon);
                if (icon != null)
                {
                    M3Draw.Texture(drawList, icon, new Vector2(min.X, min.Y + (rowHeight - iconSize) * 0.5f), iconSize);
                }

                var job = JobNames[recipe.Value.CraftType.RowId];
                var tagSize = M3Badge.Size(job);
                var tagMin = new Vector2(max.X - tagSize.X - 4f * scale, min.Y + (rowHeight - tagSize.Y) * 0.5f);
                M3Badge.Paint(drawList, tagMin, job, s.Secondary);

                var textX = min.X + iconSize + 8f * scale;
                var textY = min.Y + (rowHeight - lineHeight) * 0.5f;
                var quantity = $" ×{item.Quantity}";
                var room = MathF.Max(8f * scale, tagMin.X - 8f * scale - textX - ImGui.CalcTextSize(quantity).X);
                var name = M3Navigation.Truncate(resultItem.Name.ExtractText(), room);
                drawList.AddText(new Vector2(textX, textY), M3.U32(s.OnSurface), name);
                drawList.AddText(new Vector2(textX + ImGui.CalcTextSize(name).X, textY), M3.U32(s.OnSurfaceVariant), quantity);
            }
        }

        clipper.End();
        clipper.Destroy();
    }

    private static bool ContainsFolderPath(IReadOnlyList<string> folderPaths, string folderPath)
    {
        for (var i = 0; i < folderPaths.Count; i++)
        {
            if (folderPaths[i].Equals(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void DrawFolderPreviewPanel(string folderPath)
    {
        if (!ContainsFolderPath(GatherBuddy.CraftingListManager.GetAllFolderPaths(), folderPath))
        {
            _previewFolderPath = null;
            return;
        }

        var entries = GetFolderPreviewEntries(folderPath);
        using (ImRaii.PushFont(M3.TitleLarge))
        {
            ImGui.TextWrapped(CraftingListManager.GetFolderDisplayName(folderPath));
        }

        M3Text.Draw($"{CraftingListManager.FormatFolderPath(folderPath)} · {entries.Count} {(entries.Count == 1 ? "list" : "lists")} in this folder tree",
            M3TextStyle.Muted, true);
        ImGui.Dummy(new Vector2(0f, M3.Space1));

        const string newListLabel = "在此新建清单";
        const string newFolderLabel = "新建子文件夹";
        var flow = new M3Flow();
        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.Plus, newListLabel));
        if (M3Widgets.Button("##folder_preview_new_list", newListLabel, M3ButtonStyle.Tonal, FontAwesomeIcon.Plus))
        {
            PrepareCreateListPopup(folderPath);
            _openCreateListPopup = true;
        }

        flow.Next(M3Widgets.ButtonWidth(FontAwesomeIcon.FolderPlus, newFolderLabel));
        if (M3Widgets.Button("##folder_preview_new_folder", newFolderLabel, M3ButtonStyle.Text, FontAwesomeIcon.FolderPlus))
        {
            QueueCreateFolderPopup(folderPath);
        }

        ImGui.Dummy(new Vector2(0f, M3.Space1));
        M3Widgets.SectionLabel("清单");
        using var child = ImRaii.Child("##previewFolderList", new Vector2(-1f, 0f), false);
        if (!child)
        {
            return;
        }

        if (entries.Count == 0)
        {
            M3Text.Draw("此文件夹中没有清单。", M3TextStyle.Muted);
            return;
        }

        foreach (var (label, list) in entries)
        {
            var recipes = list.Recipes.Count;
            using var node = M3Tree.Node($"##folder_preview_{list.ID}", label, leaf: true, icon: FontAwesomeIcon.ClipboardList,
                tooltip: "点击打开此清单", trailing: $"{recipes} {(recipes == 1 ? "recipe" : "recipes")}");
            if (node.Clicked)
            {
                OpenCraftingList(list);
            }
        }
    }

    private static List<(string Label, CraftingListDefinition List)> GetFolderPreviewEntries(string folderPath, string? labelPrefix = null)
    {
        var entries = new List<(string Label, CraftingListDefinition List)>();
        foreach (var list in GatherBuddy.CraftingListManager.GetListsInFolder(folderPath))
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

    private void DrawCraftingListDragDropSource(CraftingListDefinition list)
    {
        using var source = ImRaii.DragDropSource();
        if (!source.Success)
        {
            return;
        }

        _draggedCraftingListId = list.ID;
        ImGui.SetDragDropPayload(CraftingListDragDropPayload, []);
        ImGui.TextUnformatted(list.Name);
    }

    private void DrawCraftingListEntryDropTarget(CraftingListDefinition targetList)
    {
        using var target = ImRaii.DragDropTarget();
        if (!target.Success || !Ui.IsDropping(CraftingListDragDropPayload) || _draggedCraftingListId is not int draggedListId)
        {
            return;
        }

        var draggedList = GatherBuddy.CraftingListManager.GetListByID(draggedListId);
        if (draggedList == null || draggedList.ID == targetList.ID)
        {
            return;
        }

        var itemMidpointY = (ImGui.GetItemRectMin().Y + ImGui.GetItemRectMax().Y) * 0.5f;
        var placeAfter = ImGui.GetMousePos().Y >= itemMidpointY;
        if (!GatherBuddy.CraftingListManager.MoveListRelative(draggedList, targetList, placeAfter))
        {
            return;
        }

        _previewList = GatherBuddy.CraftingListManager.GetListByID(draggedListId);
        _previewFolderPath = null;
    }

    private void DrawCraftingListFolderDropTarget(string folderPath)
    {
        using var target = ImRaii.DragDropTarget();
        if (!target.Success || !Ui.IsDropping(CraftingListDragDropPayload) || _draggedCraftingListId is not int draggedListId)
        {
            return;
        }

        var draggedList = GatherBuddy.CraftingListManager.GetListByID(draggedListId);
        if (draggedList == null)
        {
            return;
        }

        if (!GatherBuddy.CraftingListManager.MoveListToFolderEnd(draggedList, folderPath))
        {
            return;
        }

        _previewFolderPath = folderPath;
        _previewList = null;
    }


}
