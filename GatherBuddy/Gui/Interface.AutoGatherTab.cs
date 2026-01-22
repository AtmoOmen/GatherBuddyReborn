using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using GatherBuddy.AutoGather.Extensions;
using GatherBuddy.AutoGather.Lists;
using GatherBuddy.Classes;
using GatherBuddy.Data;
using GatherBuddy.Config;
using GatherBuddy.CustomInfo;
using GatherBuddy.Plugin;
using Dalamud.Bindings.ImGui;
using ElliLib;
using ElliLib.Widgets;
using ImRaii = ElliLib.Raii.ImRaii;
using GatherBuddy.Interfaces;
using GatherBuddy.Automation;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private class AutoGatherListsDragDropData
    {
        public AutoGatherList list;
        public IGatherable    Item;
        public int            ItemIdx;

        public AutoGatherListsDragDropData(AutoGatherList list, IGatherable item, int idx)
        {
            this.list = list;
            Item      = item;
            ItemIdx   = idx;
        }
    }

    private static AutoGatherListsDragDropData? _dragDropData;

    private class AutoGatherListsCache : IDisposable
    {
        public AutoGatherListsCache()
        {
            UpdateGatherables();
            WorldData.WorldLocationsChanged += UpdateGatherables;
            _plugin.AutoGatherListsManager.ListOrderChanged += OnListOrderChanged;
        }

        private void OnListOrderChanged()
        {
            Selector.RefreshView();
        }

        public readonly AutoGatherListFileSystemSelector Selector = new();

        public  ReadOnlyCollection<IGatherable>     AllGatherables      { get; private set; }
        public  ReadOnlyCollection<IGatherable>     FilteredGatherables { get; private set; }
        public  ClippedSelectableCombo<IGatherable> GatherableSelector  { get; private set; }
        private HashSet<IGatherable>                ExcludedGatherables = [];

        public void SetExcludedGatherbales(IEnumerable<IGatherable> exclude)
        {
            var excludeSet = exclude.ToHashSet();
            if (!ExcludedGatherables.SetEquals(excludeSet))
            {
                var newGatherables = AllGatherables.Except(excludeSet).ToList().AsReadOnly();
                UpdateGatherables(newGatherables, excludeSet);
            }
        }

        private static ReadOnlyCollection<IGatherable> GenAllGatherables()
        {
            var all = GatherBuddy.GameData.Gatherables.Values
                .Where(g => g.NodeList.SelectMany(l => l.WorldPositions.Values)
                    .SelectMany(p => p).Any())
                .Cast<IGatherable>()
                .Concat(GatherBuddy.GameData.Fishes.Values)
                .GroupBy(g => g.ItemId)
                .Select(g => g.First())
                .OrderBy(g => g.Name[GatherBuddy.Language])
                .ToList()
                .AsReadOnly();
            return all;
        }


        [MemberNotNull(nameof(FilteredGatherables)), MemberNotNull(nameof(GatherableSelector)), MemberNotNull(nameof(AllGatherables))]
        private void UpdateGatherables()
            => UpdateGatherables(AllGatherables = GenAllGatherables(), []);

        [MemberNotNull(nameof(FilteredGatherables)), MemberNotNull(nameof(GatherableSelector))]
        private void UpdateGatherables(ReadOnlyCollection<IGatherable> newGatherables, HashSet<IGatherable> newExcluded)
        {
            while (NewGatherableIdx > 0)
            {
                var item = FilteredGatherables![NewGatherableIdx];
                var idx  = newGatherables.IndexOf(item);
                if (idx < 0)
                    NewGatherableIdx--;
                else
                {
                    NewGatherableIdx = idx;
                    break;
                }
            }

            FilteredGatherables = newGatherables;
            ExcludedGatherables = newExcluded;
            GatherableSelector  = new("GatherablesSelector", string.Empty, 250, FilteredGatherables, g => g.Name[GatherBuddy.Language]);
        }

        public void Dispose()
        {
            WorldData.WorldLocationsChanged -= UpdateGatherables;
            _plugin.AutoGatherListsManager.ListOrderChanged -= OnListOrderChanged;
        }

        public int  NewGatherableIdx;
        public bool EditName;
        public bool EditDesc;
    }

    private readonly AutoGatherListsCache _autoGatherListsCache;

    public AutoGatherList? CurrentAutoGatherList
        => _autoGatherListsCache.Selector.Selected;

    private void DrawAutoGatherListsLine()
    {
        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Copy.ToIconString(), IconButtonSize, "复制当前自动采集列表至剪贴板",
                _autoGatherListsCache.Selector.Selected == null, true))
        {
            var list = _autoGatherListsCache.Selector.Selected!;
            try
            {
                var s = new AutoGatherList.Config(list).ToBase64();
                ImGui.SetClipboardText(s);
                Communicator.PrintClipboardMessage("自动采集列表 ", list.Name);
            }
            catch (Exception e)
            {
                Communicator.PrintClipboardMessage("自动采集列表 ", list.Name, e);
            }
        }

        if (GatherBuddy.AutoGather.ArtisanExporter.ArtisanAssemblyEnabled)
        {
            if (ImGuiUtil.DrawDisabledButton("从 Artisan 导入", Vector2.Zero,
                    "将您的 Artisan 制作清单导入到 GBR 中\n点击后在弹出的下拉菜单供您选择要导入的列表。\n当您在下拉菜单中点击某个制作清单名称时，GBR 会创建一个新的自动采集列表。",
                    !GatherBuddy.AutoGather.ArtisanExporter.ArtisanAssemblyEnabled))
            {
                ImGui.OpenPopup($"artisanImport");
            }

            if (ImGui.BeginPopup($"artisanImport"))
            {
                var lists = GatherBuddy.AutoGather.ArtisanExporter.GetArtisanListNames();

                float rowHeight       = ImGui.GetTextLineHeightWithSpacing();
                float totalListHeight = lists.Count * rowHeight;
                float totalListWidth  = lists.Max(n => ImGui.CalcTextSize(n.Value).X) + 40;

                float maxHeight   = ImGui.GetIO().DisplaySize.Y * 0.4f;
                float childHeight = Math.Min(totalListHeight, maxHeight);

                if (ImGui.BeginChild("ArtisanListsChild", new Vector2(totalListWidth, childHeight), true))
                {
                    foreach (var kvp in lists)
                    {
                        if (ImGui.Selectable($"{kvp.Value}##{kvp.Key}"))
                        {
                            Communicator.Print($"正在从 Artisan 导入 '{kvp.Value}'...");
                            GatherBuddy.AutoGather.ArtisanExporter.StartArtisanImport(kvp);
                        }

                        ImGuiUtil.HoverTooltip($"{kvp.Value} ({kvp.Key})\n(点击以导入到新的自动采集列表中)");
                    }
                }

                ImGui.EndChild();
                ImGui.EndPopup();
            }
        }

        if (ImGuiUtil.DrawDisabledButton("从 TeamCraft 导入", Vector2.Zero, "根据剪贴板内容填充列表 (TeamCraft 格式)\n备注: 如需手动导入，可通过 Artisan 制作清单导出为 TeamCraft 格式后点击此按钮实现",
                _autoGatherListsCache.Selector.Selected == null))
        {
            var clipboardText = ImGuiUtil.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboardText))
            {
                try
                {
                    Dictionary<string, int> items = new Dictionary<string, int>();

                    // Regex pattern
                    var pattern = @"\b(\d+)x\s(.+)\b";
                    var matches = Regex.Matches(clipboardText, pattern);

                    // Loop through matches and add them to dictionary
                    foreach (Match match in matches)
                    {
                        var quantity = int.Parse(match.Groups[1].Value);
                        var itemName = match.Groups[2].Value;
                        items[itemName] = quantity;
                    }

                    var list = _autoGatherListsCache.Selector.Selected!;

                    foreach (var (itemName, quantity) in items)
                    {
                        var gatherableItem = GatherBuddy.GameData.Gatherables.Values.FirstOrDefault(g => g.Name[Dalamud.ClientState.ClientLanguage] == itemName);
                        IGatherable? gatherable = gatherableItem;
                        
                        if (gatherableItem != null)
                        {
                            if (gatherableItem.NodeList.Count == 0)
                                continue;
                        }
                        else
                        {
                            gatherable = GatherBuddy.GameData.Fishes.Values.FirstOrDefault(f => f.Name[Dalamud.ClientState.ClientLanguage] == itemName);
                            
                            if (gatherable == null)
                                continue;
                        }

                        list.Add(gatherable, (uint)quantity);
                    }

                    _plugin.AutoGatherListsManager.Save();

                    if (list.Enabled)
                        _plugin.AutoGatherListsManager.SetActiveItems();
                }
                catch (Exception e)
                {
                    Communicator.PrintClipboardMessage("自动采集列表导入错误", e.ToString());
                }
            }
        }

        ImGui.SetCursorPosX(ImGui.GetWindowSize().X - 50);
        string agHelpText =
            "若设置中\"物品排序方法\"未选择\"按位置排序\"选项(设置-自动采集-高级), 物品会根据启用列表顺序添加, 其次按列表内的物品顺序添加到采集窗口。\n" +
            "但限时采集点和鱼类始终拥有最高优先级。\n" +
            "你可以拖放列表来调整列表位置顺序, 或将其移入/移出分组。\n" +
            "你可以在特定列表内通过拖放物品来重新排列其中的物品顺序。\n" +
            "在采集窗口中, 你可以按住 Ctrl 并右键点击某个物品, 将其从所属列表中删除。\n\t如果这会导致该列表没有任何物品, 则该列表也会被删除。";


        ImGuiEx.InfoMarker(agHelpText,                    null, FontAwesomeIcon.InfoCircle.ToIconString(), false);
        ImGuiEx.InfoMarker("自动采集 Discord 支持频道", null, FontAwesomeIcon.Comments.ToIconString(),   false);
        if (ImGuiEx.HoveredAndClicked())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://discord.gg/p54TZMPnC9",
                UseShellExecute = true
            });
        }
    }

    private void DrawAutoGatherList(AutoGatherList list)
    {
        if (ImGuiUtil.DrawEditButtonText(0, _autoGatherListsCache.EditName ? list.Name : CheckUnnamed(list.Name), out var newName,
                ref _autoGatherListsCache.EditName, IconButtonSize, SetInputWidth, 64))
            _plugin.AutoGatherListsManager.ChangeName(list, newName);
        if (ImGuiUtil.DrawEditButtonText(1, _autoGatherListsCache.EditDesc ? list.Description : CheckUndescribed(list.Description),
                out var newDesc, ref _autoGatherListsCache.EditDesc, IconButtonSize, 2 * SetInputWidth, 128))
            _plugin.AutoGatherListsManager.ChangeDescription(list, newDesc);

        var tmp = list.Enabled;
        if (ImGui.Checkbox("启用##list", ref tmp) && tmp != list.Enabled)
            _plugin.AutoGatherListsManager.ToggleList(list);

        ImGui.SameLine();
        ImGuiUtil.Checkbox("备选##list",
            "备选列表的物品不会被自动采集\n"
          + "仅当目标采集点不包含任意采集列表内所指定的物品, 又或者是采集点内列表所指定物品均已达到数量要求时,\n"
          + "才会尝试去采集备选列表内的物品。",
            list.Fallback, (v) => _plugin.AutoGatherListsManager.SetFallback(list, v));

        ImGui.Text($"{list.Items.Count} 个物品在列表中");
        ImGui.NewLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - ImGui.GetStyle().ItemInnerSpacing.X);
        using var box = ImRaii.ListBox("##gatherWindowList", new Vector2(-1.5f * ImGui.GetStyle().ItemSpacing.X, -1));
        if (!box)
            return;

        _autoGatherListsCache.SetExcludedGatherbales(list.Items.OfType<Gatherable>());
        var gatherables = _autoGatherListsCache.FilteredGatherables;
        var selector    = _autoGatherListsCache.GatherableSelector;
        int changeIndex = -1, changeItemIndex = -1, deleteIndex = -1;

        for (var i = 0; i < list.Items.Count; ++i)
        {
            var       item  = list.Items[i];
            using var id    = ImRaii.PushId((int)item.ItemId);
            using var group = ImRaii.Group();
            if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Trash.ToIconString(), IconButtonSize, "从列表中删除此物品", false,
                    true))
                deleteIndex = i;
            ImGui.SameLine();

            var enabled = list.EnabledItems[item];
            if (ImGui.Checkbox($"##{item.ItemId}", ref enabled))
                _plugin.AutoGatherListsManager.ChangeEnabled(list, item, enabled);

            ImGui.SameLine();
            if (selector.Draw(item.Name[GatherBuddy.Language], out var newIdx))
            {
                changeIndex     = i;
                changeItemIndex = newIdx;
            }

            ImGui.SameLine();
            ImGui.Text("物品: ");
            var invTotal = item.GetInventoryCount();
            ImGui.SameLine(0f, ImGui.CalcTextSize($"0000 / ").X - ImGui.CalcTextSize($"{invTotal} / ").X);
            ImGui.Text($"{invTotal} / ");
            ImGui.SameLine(0, 3f);
            var quantity = list.Quantities.TryGetValue(item, out var q) ? (int)q : 1;
            ImGui.SetNextItemWidth(100f * Scale);
            if (ImGui.InputInt("##quantity", ref quantity, 1, 10))
                _plugin.AutoGatherListsManager.ChangeQuantity(list, item, (uint)quantity);
            ImGui.SameLine();
            if (DrawLocationInput(item, list.PreferredLocations.GetValueOrDefault(item), out var newLoc))
                _plugin.AutoGatherListsManager.ChangePreferredLocation(list, item, newLoc);
            group.Dispose();

            // Custom drag-drop for moving items within and between lists
            using (var source = ImRaii.DragDropSource())
            {
                if (source.Success)
                {
                    _dragDropData = new AutoGatherListsDragDropData(list, item, i);
                    ImGui.SetDragDropPayload("AutoGatherListItem", ReadOnlySpan<byte>.Empty);
                    ImGui.TextUnformatted(item.Name[GatherBuddy.Language]);
                }
            }

            var localIdx = i;
            using (var target = ImRaii.DragDropTarget())
            {
                if (target.Success && ImGuiUtil.IsDropping("AutoGatherListItem") && _dragDropData != null)
                {
                    _plugin.AutoGatherListsManager.MoveItem(_dragDropData.list, _dragDropData.ItemIdx, localIdx);
                    _dragDropData = null;
                }
            }
        }

        if (deleteIndex >= 0)
            _plugin.AutoGatherListsManager.RemoveItem(list, deleteIndex);

        if (changeIndex >= 0)
            _plugin.AutoGatherListsManager.ChangeItem(list, gatherables[changeItemIndex], changeIndex);

        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Plus.ToIconString(), IconButtonSize, "添加此物品到列表末尾", false,
                true))
            _plugin.AutoGatherListsManager.AddItem(list, gatherables[_autoGatherListsCache.NewGatherableIdx]);

        ImGui.SameLine();
        var allEnabled = list.Items.All(i => list.EnabledItems[i]);
        if (ImGui.Checkbox("##AllEnabled", ref allEnabled))
        {
            foreach (var i in list.Items)
                _plugin.AutoGatherListsManager.ChangeEnabled(list, i, allEnabled);
        }
        ImGuiUtil.HoverTooltip((allEnabled ? "禁用" : "启用" ) + " 列表中所有物品");

        ImGui.SameLine();
        if (selector.Draw(_autoGatherListsCache.NewGatherableIdx, out var idx))
        {
            _autoGatherListsCache.NewGatherableIdx = idx;
            _plugin.AutoGatherListsManager.AddItem(list, gatherables[_autoGatherListsCache.NewGatherableIdx]);
        }
    }

    private void DrawAutoGatherTab()
    {
        using var id  = ImRaii.PushId("AutoGatherLists");
        using var tab = ImRaii.TabItem("自动采集");

        ImGuiUtil.HoverTooltip(
            "你没看错！自动采集！");

        if (!tab)
            return;

        AutoGather.AutoGatherUI.DrawAutoGatherStatus();

        var selectorWidth = _autoGatherListsCache.Selector.SelectorWidth;
        using (var child = ImRaii.Child("AutoGatherListSelector", new Vector2(selectorWidth, -1), false))
        {
            if (child)
                _autoGatherListsCache.Selector.Draw();
        }

        ImGui.SameLine();
        ImGui.Button("##splitter", new Vector2(4, -1));
        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta.X;
            selectorWidth += delta;
            selectorWidth = Math.Clamp(selectorWidth, 150f * Scale, ImGui.GetWindowWidth() * 0.5f);
            _autoGatherListsCache.Selector.SelectorWidth = selectorWidth;
        }

        ImGui.SameLine();

        ItemDetailsWindow.Draw("List Details", DrawAutoGatherListsLine, () =>
        {
            if (_autoGatherListsCache.Selector.Selected != null)
                DrawAutoGatherList(_autoGatherListsCache.Selector.Selected);
        });
    }
}

