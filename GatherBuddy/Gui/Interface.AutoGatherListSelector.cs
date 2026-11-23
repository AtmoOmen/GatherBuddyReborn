using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.Classes;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan.Vendors;
using RebornMaterial;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private sealed class AutoGatherListSelector
    {
        private const string TreeDragDropLabel = "AutoGatherListTreeNode";
        private const string NewListDialogId = "##agNewList";
        private const string NewFolderDialogId = "##agNewFolder";
        private const string ImportDialogId = "##agImport";
        private const string RenameFolderDialogId = "##agRenameFolder";
        private const string DeleteFolderDialogId = "##agDeleteFolder";
        private const string BaitResultDialogId = "##agBaitResult";

        private sealed record BaitBuyListGenerationResult(
            bool VendorDataReady,
            IReadOnlyList<string> AllBaitNames,
            IReadOnlyList<VendorBuyListManager.VendorTargetRequest> Targets,
            IReadOnlyList<string> SkippedBaitNames);

        private sealed record BaitBuyListResultPopupState(string Summary, IReadOnlyList<string> SkippedBaitNames);

        private BaitBuyListResultPopupState? _baitBuyListResultPopup;
        private bool _openBaitResult;

        private AutoGatherList? _selected;
        private string _search = string.Empty;
        private string _dialogName = string.Empty;
        private FileSystem<AutoGatherList>.Folder? _dialogFolder;
        private FileSystem<AutoGatherList>.IPath? _dragged;

        private readonly Dictionary<FileSystem<AutoGatherList>.IPath, int> _rows = [];
        private int _row;

        private static AutoGatherListsManager Manager
            => _plugin.AutoGatherListsManager;

        private static FileSystem<AutoGatherList> FileSystem
            => Manager.FileSystem;

        public AutoGatherListsDragDropData? DragDropItem { set; get; }

        public AutoGatherList? Selected
        {
            get
            {
                if (_selected != null && !FileSystem.TryGetValue(_selected, out _))
                {
                    _selected = null;
                }

                return _selected;
            }
        }

        public static float SelectorWidth
        {
            get => GatherBuddy.Config.AutoGatherListSelectorWidth * ImGuiHelpers.GlobalScale;
            set
            {
                GatherBuddy.Config.AutoGatherListSelectorWidth = value / ImGuiHelpers.GlobalScale;
                GatherBuddy.Config.Save();
            }
        }

        public static void RefreshView()
        { }

        public void Draw()
        {
            DrawToolbar();
            var search = _search;
            if (M3Widgets.SearchField("##agSearch", "搜索清单", ref search, ImGui.GetContentRegionAvail().X))
            {
                _search = search;
            }

            ImGui.Dummy(new Vector2(0, M3.Space1));
            using (var panel = M3Panel.Begin("##agTree", ImGui.GetContentRegionAvail()))
            {
                if (panel)
                {
                    _row = 0;
                    DrawFolderContents(FileSystem.Root);
                    if (FileSystem.Root.Children.Count == 0)
                    {
                        M3Widgets.EmptyState("##agEmpty", "暂无清单", "点击上方按钮创建清单。", FontAwesomeIcon.Robot);
                    }

                    DrawRootDropZone();
                }
            }

            DrawDialogs();
        }

        private void DrawToolbar()
        {
            if (M3Widgets.IconButton("##agAddList", FontAwesomeIcon.Plus, "新建自动采集清单", M3ButtonStyle.Tonal))
            {
                OpenNameDialog(NewListDialogId, FileSystem.Root, string.Empty);
            }

            ImGui.SameLine(0, M3.Space1);
            if (M3Widgets.IconButton("##agAddFolder", FontAwesomeIcon.FolderPlus, "新建文件夹"))
            {
                OpenNameDialog(NewFolderDialogId, FileSystem.Root, string.Empty);
            }

            ImGui.SameLine(0, M3.Space1);
            if (M3Widgets.IconButton("##agImport", FontAwesomeIcon.Paste, "从剪贴板导入自动采集清单"))
            {
                OpenNameDialog(ImportDialogId, FileSystem.Root, string.Empty);
            }
        }

        private bool Searching
            => _search.Length > 0;

        private bool Matches(AutoGatherList list)
            => list.Name.Contains(_search, StringComparison.OrdinalIgnoreCase);

        private bool FolderMatches(FileSystem<AutoGatherList>.Folder folder)
        {
            foreach (var child in folder.Children)
            {
                switch (child)
                {
                    case FileSystem<AutoGatherList>.Leaf leaf when Matches(leaf.Value):
                    case FileSystem<AutoGatherList>.Folder subFolder when FolderMatches(subFolder):
                        return true;
                }
            }

            return false;
        }

        private void DrawFolderContents(FileSystem<AutoGatherList>.Folder folder)
        {
            foreach (var child in folder.GetChildren(AutoGatherListsManager.SortMode))
            {
                switch (child)
                {
                    case FileSystem<AutoGatherList>.Folder sub when !Searching || FolderMatches(sub):
                        DrawFolder(sub);
                        break;
                    case FileSystem<AutoGatherList>.Leaf leaf when !Searching || Matches(leaf.Value):
                        DrawLeaf(leaf);
                        break;
                }
            }
        }

        private void DrawFolder(FileSystem<AutoGatherList>.Folder folder)
        {
            var id = $"##folder_{folder.Name}";
            if (Searching)
            {
                M3Tree.SetOpen(id, true);
            }

            var lists = folder.CountLeaves();
            var open = M3Tree.IsOpen(id);
            using var node = M3Tree.Node(id, folder.Name, icon: open ? FontAwesomeIcon.FolderOpen : FontAwesomeIcon.Folder,
                trailing: lists.ToString());
            _rows[folder] = _row++;
            DrawDragSource(folder);
            DrawFolderDropTarget(folder);
            DrawFolderMenu(folder);
            if (node.Open)
            {
                DrawFolderContents(folder);
            }
        }

        private void DrawLeaf(FileSystem<AutoGatherList>.Leaf leaf)
        {
            var list = leaf.Value;
            using var node = M3Tree.Node($"##list_{leaf.Name}", CheckUnnamed(list.Name), ReferenceEquals(list, _selected), true,
                list.Enabled ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle,
                color: list.Enabled ? null : M3Text.ColorOf(M3TextStyle.Faint),
                tooltip: $"{CheckUnnamed(list.Name)}\n{(list.Enabled ? "已启用" : "已禁用")}{(list.Fallback ? "，备用清单" : string.Empty)}\n"
                  + $"共 {list.Items.Count} 项\n双击可{(list.Enabled ? "禁用" : "启用")}此清单。",
                trailing: list.Items.Count.ToString());
            _rows[leaf] = _row++;
            if (node.Clicked)
            {
                _selected = list;
            }

            if (node.DoubleClicked)
            {
                Manager.ToggleList(list);
            }

            DrawDragSource(leaf);
            DrawLeafDropTarget(leaf);
            DrawLeafMenu(leaf);
        }

        private void DrawDragSource(FileSystem<AutoGatherList>.IPath path)
        {
            using var source = ImRaii.DragDropSource();
            if (!source)
            {
                return;
            }

            _dragged = path;
            ImGui.SetDragDropPayload(TreeDragDropLabel, []);
            ImGui.TextUnformatted(path is FileSystem<AutoGatherList>.Leaf leaf ? $"正在移动 {CheckUnnamed(leaf.Value.Name)}" : $"正在移动文件夹 {path.Name}");
        }

        private static bool IsDropping(string label)
            => !ImGui.AcceptDragDropPayload(label).IsNull;

        private void DrawLeafDropTarget(FileSystem<AutoGatherList>.Leaf target)
        {
            using var dropTarget = ImRaii.DragDropTarget();
            if (!dropTarget)
            {
                return;
            }

            if (IsDropping(AutoGatherListsDragDropData.Label) && DragDropItem != null)
            {
                Manager.MoveItem(DragDropItem.List, target.Value, DragDropItem.ItemIdx);
                DragDropItem = null;
                return;
            }

            if (!IsDropping(TreeDragDropLabel) || _dragged == null)
            {
                return;
            }

            switch (_dragged)
            {
                case FileSystem<AutoGatherList>.Leaf moved when !ReferenceEquals(moved, target):
                    {
                        var fromAbove = _rows.TryGetValue(moved, out var from) && _rows.TryGetValue(target, out var to) && from < to;
                        if (!ReferenceEquals(moved.Parent, target.Parent) && !Manager.MoveToFolder(moved, target.Parent))
                        {
                            M3Snackbar.Show($"文件夹「{target.Parent.FullName()}」中已有名为「{moved.Name}」的清单。", severity: M3Severity.Error);
                            break;
                        }

                        Manager.MoveList(moved, target, fromAbove);
                        _selected = moved.Value;
                        break;
                    }
                case FileSystem<AutoGatherList>.Folder folder:
                    MoveInto(folder, target.Parent);
                    break;
            }

            _dragged = null;
        }

        private void DrawFolderDropTarget(FileSystem<AutoGatherList>.Folder folder)
        {
            using var dropTarget = ImRaii.DragDropTarget();
            if (!dropTarget || !IsDropping(TreeDragDropLabel) || _dragged == null)
            {
                return;
            }

            MoveInto(_dragged, folder);
            _dragged = null;
        }

        private void DrawRootDropZone()
        {
            if (_dragged == null || _dragged.Parent.IsRoot || ImGui.GetDragDropPayload().IsNull)
            {
                return;
            }

            ImGui.Dummy(new Vector2(0, M3.Space2));
            var size = new Vector2(ImGui.GetContentRegionAvail().X, M3Tree.RowHeight * 1.5f);
            var min = ImGui.GetCursorScreenPos();
            ImGui.Dummy(size);
            var s = M3.Scheme;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRect(min, min + size, M3.U32(s.Outline, 0.7f), M3.ShapeSmall, ImDrawFlags.None, M3.Scale);
            var text = "拖到这里可移出所在文件夹";
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(min + (size - textSize) * 0.5f, M3.U32(s.OnSurfaceVariant), text);

            using var dropTarget = ImRaii.DragDropTarget();
            if (dropTarget.Success && IsDropping(TreeDragDropLabel) && _dragged is { } dragged)
            {
                MoveInto(dragged, FileSystem.Root);
                _dragged = null;
            }
        }

        private static void MoveInto(FileSystem<AutoGatherList>.IPath path, FileSystem<AutoGatherList>.Folder folder)
        {
            if (ReferenceEquals(path.Parent, folder) || path is FileSystem<AutoGatherList>.Folder f && f.Contains(folder))
            {
                return;
            }

            if (!Manager.MoveToFolder(path, folder))
            {
                M3Snackbar.Show($"「{(folder.IsRoot ? "顶层" : folder.Name)}」中已存在名为「{path.Name}」的条目。", severity: M3Severity.Error);
            }
        }

        private void DrawLeafMenu(FileSystem<AutoGatherList>.Leaf leaf)
        {
            var list = leaf.Value;
            using var menu = M3Menu.BeginContext($"##listMenu_{leaf.Name}");
            if (!menu.IsOpen)
            {
                return;
            }

            M3Menu.Heading(CheckUnnamed(list.Name));
            if (M3Menu.Item("##toggle", list.Enabled ? "禁用" : "启用", list.Enabled ? FontAwesomeIcon.Pause : FontAwesomeIcon.Play))
            {
                Manager.ToggleList(list);
            }

            if (M3Menu.Item("##up", "上移", FontAwesomeIcon.ArrowUp))
            {
                Manager.MoveListUp(leaf);
            }

            if (M3Menu.Item("##down", "下移", FontAwesomeIcon.ArrowDown))
            {
                Manager.MoveListDown(leaf);
            }

            DrawMoveToMenu(leaf);
            if (M3Menu.Item("##duplicate", "复制清单", FontAwesomeIcon.Clone))
            {
                var clone = list.Clone();
                clone.Name = $"{list.Name} （副本）";
                Manager.AddList(clone, leaf.Parent);
            }

            if (M3Menu.Item("##export", "复制到剪贴板", FontAwesomeIcon.Copy))
            {
                try
                {
                    ImGui.SetClipboardText(new AutoGatherList.Config(list).ToBase64());
                    Communicator.PrintClipboardMessage("自动采集列表 ", list.Name);
                }
                catch (Exception e)
                {
                    Communicator.PrintClipboardMessage("自动采集列表 ", list.Name, e);
                }
            }

            DrawBaitBuyListMenu(list);
            M3Menu.Divider();
            if (M3Menu.Item("##delete", "删除", FontAwesomeIcon.Trash))
            {
                var folder = leaf.Parent;
                Manager.DeleteList(list);
                M3Snackbar.Show($"已删除清单「{CheckUnnamed(list.Name)}」。", "撤销", () =>
                    Manager.AddList(list, FileSystem.Root.Contains(folder) ? folder : FileSystem.Root), M3Snackbar.LongDuration);
            }
        }

        private static void DrawMoveToMenu(FileSystem<AutoGatherList>.Leaf leaf)
        {
            var folders = new List<FileSystem<AutoGatherList>.Folder>();
            foreach (var path in FileSystem.Root.GetAllDescendants(AutoGatherListsManager.SortMode))
            {
                if (path is FileSystem<AutoGatherList>.Folder folder)
                {
                    folders.Add(folder);
                }
            }

            using var sub = M3Menu.BeginSub("##moveTo", "移至", FontAwesomeIcon.FolderOpen, folders.Count > 0 || !leaf.Parent.IsRoot);
            if (!sub.IsOpen)
            {
                return;
            }

            if (!leaf.Parent.IsRoot && M3Menu.Item("##root", "顶层", FontAwesomeIcon.LevelUpAlt))
            {
                MoveInto(leaf, FileSystem.Root);
            }

            for (var i = 0; i < folders.Count; ++i)
            {
                var folder = folders[i];
                if (!ReferenceEquals(folder, leaf.Parent) && M3Menu.Item($"##folder{i}", folder.FullName(), FontAwesomeIcon.Folder))
                {
                    MoveInto(leaf, folder);
                }
            }
        }

        private void DrawFolderMenu(FileSystem<AutoGatherList>.Folder folder)
        {
            using var menu = M3Menu.BeginContext($"##folderMenu_{folder.Name}");
            if (!menu.IsOpen)
            {
                return;
            }

            M3Menu.Heading(folder.Name);
            if (M3Menu.Item("##newList", "在此新建清单", FontAwesomeIcon.Plus))
            {
                OpenNameDialog(NewListDialogId, folder, string.Empty);
            }

            if (M3Menu.Item("##newFolder", "新建子文件夹", FontAwesomeIcon.FolderPlus))
            {
                OpenNameDialog(NewFolderDialogId, folder, string.Empty);
            }

            if (M3Menu.Item("##rename", "重命名", FontAwesomeIcon.Pen))
            {
                OpenNameDialog(RenameFolderDialogId, folder, folder.Name);
            }

            M3Menu.Divider();
            if (M3Menu.Item("##delete", "删除文件夹", FontAwesomeIcon.Trash))
            {
                _dialogFolder = folder;
                _openDialog = DeleteFolderDialogId;
            }
        }

        private string? _openDialog;

        private void OpenNameDialog(string dialogId, FileSystem<AutoGatherList>.Folder folder, string name)
        {
            _dialogFolder = folder;
            _dialogName = name;
            _openDialog = dialogId;
        }

        private void DrawDialogs()
        {
            if (_openDialog != null)
            {
                M3Dialog.Open(_openDialog);
                _openDialog = null;
            }

            if (_openBaitResult)
            {
                M3Dialog.Open(BaitResultDialogId);
                _openBaitResult = false;
            }

            DrawNameDialog(NewListDialogId, "新建自动采集清单", FontAwesomeIcon.Plus, "创建", name =>
            {
                var list = new AutoGatherList { Name = name };
                Manager.AddList(list, Attached(_dialogFolder));
                _selected = list;
            });
            DrawNameDialog(NewFolderDialogId, "新建文件夹", FontAwesomeIcon.FolderPlus, "创建", name => Manager.CreateFolder(name, Attached(_dialogFolder)));
            DrawNameDialog(RenameFolderDialogId, "重命名文件夹", FontAwesomeIcon.Pen, "重命名", name =>
            {
                if (_dialogFolder != null && FileSystem.Root.Contains(_dialogFolder))
                {
                    Manager.RenameFolder(_dialogFolder, name);
                }
            });
            DrawNameDialog(ImportDialogId, "从剪贴板导入", FontAwesomeIcon.Paste, "导入", name =>
            {
                string clipboard;
                try
                {
                    clipboard = ImGui.GetClipboardText() ?? string.Empty;
                }
                catch
                {
                    clipboard = string.Empty;
                }

                if (!AutoGatherList.Config.FromBase64(clipboard, out var cfg))
                {
                    M3Snackbar.Show("剪贴板中没有自动采集清单。", severity: M3Severity.Error);
                    return;
                }

                AutoGatherList.FromConfig(cfg, out var list);
                list.Name = name;
                Manager.AddList(list);
                _selected = list;
            });
            DrawDeleteFolderDialog();
            DrawBaitBuyListResultDialog();
        }

        private static FileSystem<AutoGatherList>.Folder Attached(FileSystem<AutoGatherList>.Folder? folder)
            => folder != null && FileSystem.Root.Contains(folder) ? folder : FileSystem.Root;

        private void DrawNameDialog(string id, string headline, FontAwesomeIcon icon, string confirm, Action<string> onConfirm)
        {
            using var dialog = M3Dialog.Begin(id, headline, null, icon);
            if (!dialog.IsOpen)
            {
                return;
            }

            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
            }

            M3Widgets.TextField("##name", "名称", ref _dialogName, ImGui.GetContentRegionAvail().X, 128);
            var name = _dialogName.Trim();
            var submit = name.Length > 0 && ImGui.IsKeyPressed(ImGuiKey.Enter);
            var pressed = M3Dialog.Actions(
            [
                new M3DialogAction("取消"),
                new M3DialogAction(confirm, M3ButtonStyle.Filled, icon, name.Length > 0),
            ]);
            if (pressed != 1 && !submit)
            {
                return;
            }

            if (submit)
            {
                ImGui.CloseCurrentPopup();
            }

            onConfirm(name);
            _dialogName = string.Empty;
        }

        private void DrawDeleteFolderDialog()
        {
            var folder = _dialogFolder;
            var lists = folder?.CountLeaves() ?? 0;
            using var dialog = M3Dialog.Begin(DeleteFolderDialogId, $"删除{(folder == null ? "文件夹" : $"文件夹「{folder.Name}」")}？",
                lists == 0 ? "该文件夹为空。" : $"其中的 {lists} 份清单也会一并删除。",
                FontAwesomeIcon.Trash, 380f);
            if (!dialog.IsOpen)
            {
                return;
            }

            if (M3Dialog.Actions(
                [
                    new M3DialogAction("取消"),
                    new M3DialogAction("删除", M3ButtonStyle.Danger, FontAwesomeIcon.Trash),
                ]) == 1
             && folder != null
             && FileSystem.Root.Contains(folder))
            {
                Manager.DeleteFolder(folder);
            }
        }

        private void DrawBaitBuyListMenu(AutoGatherList list)
        {
            var vendorBuyListManager = GatherBuddy.VendorBuyListManager;
            var vendorBuyListWindow = GatherBuddy.VendorBuyListWindow;
            var result = BuildVendorBuyListGenerationResult(list, vendorBuyListManager);
            var canOpenMenu = vendorBuyListManager != null && vendorBuyListWindow != null && result.VendorDataReady && result.AllBaitNames.Count > 0;

            using var sub = M3Menu.BeginSub("##baitBuyList", "制作鱼饵购买清单", FontAwesomeIcon.ShoppingCart, canOpenMenu);
            if (!canOpenMenu)
            {
                M3Tooltip.Hovered(vendorBuyListManager == null || vendorBuyListWindow == null
                    ? "商店购买清单不可用"
                    : result.AllBaitNames.Count == 0
                        ? "此自动采集清单中没有直接鱼饵"
                        : VendorShopResolver.IsInitializing
                            ? "商店数据仍在加载"
                            : "商店数据尚未加载完成。");
            }

            if (!sub.IsOpen)
            {
                return;
            }

            if (M3Menu.Item("##createNew", "新建购买清单", FontAwesomeIcon.Plus, result.Targets.Count > 0))
            {
                OpenCreateVendorBuyListPopup(list, result, vendorBuyListWindow!);
            }

            using (var existing = M3Menu.BeginSub("##addExisting", "添加到购买清单", FontAwesomeIcon.ListUl,
                       result.Targets.Count > 0 && VendorBuyListManager.Lists.Count > 0))
            {
                if (existing.IsOpen)
                {
                    var index = 0;
                    foreach (var buyList in VendorBuyListManager.GetListsByCreation(true))
                    {
                        if (M3Menu.Item($"##buyList{index++}", buyList.Name))
                        {
                            AddTargetsToVendorBuyList(list, buyList.Id, buyList.Name, result, vendorBuyListManager!);
                        }
                    }
                }
            }

            if (result.SkippedBaitNames.Count > 0)
            {
                M3Menu.Divider();
                if (M3Menu.Item("##skipped", "显示商人不出售的鱼饵", FontAwesomeIcon.InfoCircle))
                {
                    OpenSkippedBaitPopup(list, result);
                }
            }
        }

        private static BaitBuyListGenerationResult BuildVendorBuyListGenerationResult(AutoGatherList list,
            VendorBuyListManager? vendorBuyListManager)
        {
            var sortedBaits = new List<(Structs.Bait Bait, int Index)>();
            var seenBaitIds = new HashSet<uint>();
            foreach (var item in list.Items)
            {
                if (item is Fish { InitialBait: { Id: not 0 } initialBait } && seenBaitIds.Add(initialBait.Id))
                {
                    sortedBaits.Add((initialBait, sortedBaits.Count));
                }
            }

            sortedBaits.Sort(static (lhs, rhs) =>
            {
                var order = StringComparer.OrdinalIgnoreCase.Compare(lhs.Bait.Name, rhs.Bait.Name);
                return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
            });
            var baits = sortedBaits.ConvertAll(static entry => entry.Bait);
            var baitNames = baits.ConvertAll(static bait => bait.Name);

            if (vendorBuyListManager == null)
            {
                return new BaitBuyListGenerationResult(false, baitNames, [], []);
            }

            VendorShopResolver.InitializeAsync();
            if (!VendorShopResolver.IsInitialized)
            {
                return new BaitBuyListGenerationResult(false, baitNames, [], []);
            }

            var targets = new List<VendorBuyListManager.VendorTargetRequest>();
            var skippedBaits = new List<string>();
            foreach (var bait in baits)
            {
                if (VendorBuyListManager.CanAddSupportedItem(bait.Id))
                {
                    targets.Add(new VendorBuyListManager.VendorTargetRequest(bait.Id, 1));
                }
                else
                {
                    skippedBaits.Add(bait.Name);
                }
            }

            return new BaitBuyListGenerationResult(true, baitNames, targets, skippedBaits);
        }

        private void OpenCreateVendorBuyListPopup(AutoGatherList sourceList, BaitBuyListGenerationResult result,
            VendorBuyListWindow vendorBuyListWindow)
        {
            var listName = string.IsNullOrWhiteSpace(sourceList.Name)
                ? "自动采集鱼饵"
                : $"{sourceList.Name} 鱼饵";
            GatherBuddy.Log.Debug(
                $"[AutoGatherListSelector] Creating a new vendor buy list '{listName}' from auto-gather list '{sourceList.Name}' with {result.Targets.Count:N0} bait target(s) and {result.SkippedBaitNames.Count:N0} skipped bait(s).");
            if (!vendorBuyListWindow.OpenCreateListPopup(listName, result.Targets))
            {
                GatherBuddy.Log.Debug(
                    $"[AutoGatherListSelector] Unable to create vendor buy list '{listName}' from auto-gather list '{sourceList.Name}'.");
                return;
            }

            OpenSkippedBaitPopup(sourceList, result, $"已创建商店购买清单 \"{listName}\"");
        }

        private void AddTargetsToVendorBuyList(AutoGatherList sourceList, Guid listId, string listName,
            BaitBuyListGenerationResult result, VendorBuyListManager vendorBuyListManager)
        {
            GatherBuddy.Log.Debug(
                $"[AutoGatherListSelector] Adding {result.Targets.Count:N0} bait target(s) from auto-gather list '{sourceList.Name}' to vendor buy list '{listName}' with {result.SkippedBaitNames.Count:N0} skipped bait(s).");
            if (vendorBuyListManager.TrySetTargets(listId, result.Targets, selectList: true, openWindow: true, announce: true) == 0)
            {
                GatherBuddy.Log.Debug(
                    $"[AutoGatherListSelector] Unable to add bait targets from auto-gather list '{sourceList.Name}' to vendor buy list '{listName}'.");
                return;
            }

            OpenSkippedBaitPopup(sourceList, result, $"已更新商店购买清单 \"{listName}\"");
        }

        private void OpenSkippedBaitPopup(AutoGatherList sourceList, BaitBuyListGenerationResult result, string? actionPrefix = null)
        {
            if (result.SkippedBaitNames.Count == 0)
            {
                return;
            }

            var sourceListName = string.IsNullOrWhiteSpace(sourceList.Name)
                ? "此自动采集清单"
                : $"'{sourceList.Name}'";
            var baitLabel = result.SkippedBaitNames.Count == 1 ? "鱼饵" : "鱼饵";
            var requirementText = result.SkippedBaitNames.Count == 1
                ? "需要制作或通过商店系统之外的方式获取"
                : "需要制作或通过商店系统之外的方式获取";
            var summary = actionPrefix == null
                ? $"以下{baitLabel}未从 {sourceListName} 加入, {requirementText}"
                : $"{actionPrefix}, 来源: {sourceListName}, 以下{baitLabel}未加入, {requirementText}";

            _baitBuyListResultPopup = new BaitBuyListResultPopupState(summary, result.SkippedBaitNames);
            _openBaitResult = true;
        }

        private void DrawBaitBuyListResultDialog()
        {
            var state = _baitBuyListResultPopup;
            using var dialog = M3Dialog.Begin(BaitResultDialogId, "需要自行获取的鱼饵", state?.Summary, FontAwesomeIcon.ShoppingCart, 460f);
            if (!dialog.IsOpen)
            {
                return;
            }

            if (state != null)
            {
                var height = Math.Clamp(state.SkippedBaitNames.Count * ImGui.GetTextLineHeightWithSpacing() + M3.Space3 * 2,
                    80f * M3.Scale, 220f * M3.Scale);
                using var list = M3Panel.Begin("##skippedBaits", new Vector2(ImGui.GetContentRegionAvail().X, height), fill: VulcanUiStyle.PanelBackground);
                if (list)
                {
                    foreach (var baitName in state.SkippedBaitNames)
                    {
                        M3Text.Bullet(baitName);
                    }
                }
            }

            if (M3Dialog.Actions([new M3DialogAction("关闭", M3ButtonStyle.Filled)]) == 0)
            {
                _baitBuyListResultPopup = null;
            }
        }
    }
}
