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
using GatherBuddy.Crafting;
using GatherBuddy.CustomInfo;
using GatherBuddy.Plugin;
using Dalamud.Bindings.ImGui;
using ElliLib;
using ElliLib.Widgets;
using ImRaii = ElliLib.Raii.ImRaii;
using GatherBuddy.Interfaces;
using Lumina.Text.ReadOnly;
using GatherBuddy.AutoGather.Helpers;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private record class AutoGatherListsDragDropData(AutoGatherList List, IGatherable Item, int ItemIdx)
    {
        public static string Label => "AutoGatherListItem";
    }

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

        public int             NewGatherableIdx;
        public bool            EditName;
        public bool            EditDesc;
        public string          ItemFilter = string.Empty;
        public AutoGatherList? ItemFilterList;
    }

    private readonly AutoGatherListsCache _autoGatherListsCache;

    public AutoGatherList? CurrentAutoGatherList
        => _autoGatherListsCache.Selector.Selected;

    public CraftingListDefinition? CurrentCraftingList
        => _plugin._vulcanWindow?.CurrentCraftingList;

    private void DrawAutoGatherListsLine()
    {
        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Copy.ToIconString(), IconButtonSize, "将当前自动采集列表复制到剪贴板。",
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
                    "从 Artisan 导入列表到 GBR\n弹出下拉菜单选择要导入的列表。\n点击下拉菜单中的列表名称后，将在 GBR 中创建新列表。",
                    !GatherBuddy.AutoGather.ArtisanExporter.ArtisanAssemblyEnabled))
            {
                ImGui.OpenPopup($"artisanImport");
            }

            if (ImGui.BeginPopup($"artisanImport"))
            {
                var lists = GatherBuddy.AutoGather.ArtisanExporter.GetArtisanListNames();

                float rowHeight       = ImGui.GetTextLineHeightWithSpacing();
                float childPaddingY   = ImGui.GetStyle().WindowPadding.Y * 2f;
                float totalListHeight = lists.Count * rowHeight + childPaddingY;
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

                        ImGuiUtil.HoverTooltip($"{kvp.Value} ({kvp.Key})\n（点击导入到新的自动采集列表）");
                    }
                }

                ImGui.EndChild();
                ImGui.EndPopup();
            }
        }

        if (ImGuiUtil.DrawDisabledButton("从 TeamCraft 导入", Vector2.Zero, "从剪贴板内容填充列表（TeamCraft 格式）",
                _autoGatherListsCache.Selector.Selected == null))
        {
            var clipboardText = ImGuiUtil.GetClipboardText();
            if (!string.IsNullOrEmpty(clipboardText))
            {
                try
                {
                    // Regex pattern
                    var pattern = @"\b(\d+)x\s(.+)\b";
                    var matches = Regex.Matches(clipboardText, pattern);

                    var list = _autoGatherListsCache.Selector.Selected!;

                    Dictionary<ReadOnlySeString, uint>? diademItems = null;
                    Lumina.Excel.ExcelSheet<Lumina.Excel.Sheets.Item>? itemSheet = null;
                    Dictionary<string, IGatherable> normalItems = new(GatherBuddy.GameData.Gatherables.Count + GatherBuddy.GameData.Fishes.Count);
                    foreach (var item in ((IEnumerable<IGatherable>)GatherBuddy.GameData.Gatherables.Values).Concat(GatherBuddy.GameData.Fishes.Values))
                        normalItems[item.Name[GatherBuddy.Language]] = item;

                    foreach (Match match in matches)
                    {
                        var quantity = uint.Parse(match.Groups[1].Value);
                        var itemName = match.Groups[2].Value;

                        if (normalItems.TryGetValue(itemName, out var item))
                        {
                            if (!item.Locations.Any())
                                continue;
                        }
                        else
                        {
                            itemSheet ??= Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Item>(GatherBuddy.Language);
                            diademItems ??= Diadem.ApprovedToRawItemIds
                                .Select(kv => (itemSheet.GetRow(kv.Key).Name, kv.Value))
                                .ToDictionary();

                            if (!diademItems.TryGetValue(itemName, out var rawId))
                                continue;

                            if (GatherBuddy.GameData.Gatherables.TryGetValue(rawId, out var gatherable))
                                item = gatherable;
                            else if (GatherBuddy.GameData.Fishes.TryGetValue(rawId, out var fish))
                                item = fish;
                            else
                                continue;
                        }

                        if(!list.Add(item, quantity))
                            list.SetQuantity(item, quantity + list.Quantities[item]);
                    }

                    _plugin.AutoGatherListsManager.Save();

                    if (list.Enabled)
                        _plugin.AutoGatherListsManager.SetActiveItems();
                }
                catch (Exception e)
                {
                    Communicator.PrintClipboardMessage("导入自动采集列表时出错", e.ToString());
                }
            }
        }

        ImGui.SetCursorPosX(ImGui.GetWindowSize().X - 50);
        string agHelpText =
            "如果未选择按位置排序的配置选项，采集物品将按启用列表的顺序进行，然后按每个列表中物品的顺序进行，" +
            "但限时采集点和鱼类始终优先。\n" +
            "你可以拖放列表来移动它们。\n" +
            "你可以在特定列表内拖放物品来重新排列。\n" +
            "你可以将物品从选择器拖放到另一个列表上，以在列表之间移动物品。\n" +
            "在采集窗口中，你可以按住 Ctrl 并右键点击物品，将其从所属列表中删除。";


        ImGuiEx.InfoMarker(agHelpText,                    null, FontAwesomeIcon.InfoCircle.ToIconString(), false);
        ImGuiEx.InfoMarker("自动采集支持 Discord", null, FontAwesomeIcon.Comments.ToIconString(),   false);
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
        if (ImGui.Checkbox("Enabled##list", ref tmp) && tmp != list.Enabled)
            _plugin.AutoGatherListsManager.ToggleList(list);

        ImGui.SameLine();
        ImGuiUtil.Checkbox("备选##list",
            "备选列表中的物品不会被自动采集。\n"
          + "但如果采集点没有常规列表中的物品，或者你已经采集了足够的数量，\n"
          + "那么如果该采集点中有备选列表中的物品，就会转而采集它们。",
            list.Fallback, (v) => _plugin.AutoGatherListsManager.SetFallback(list, v));
        ImGui.SameLine();
        ImGuiUtil.Checkbox("完成后移除##list",
            "当你的背包达到配置的数量后，自动从此列表中移除已启用的物品。",
            list.RemoveCompletedItems, (v) => _plugin.AutoGatherListsManager.SetRemoveCompletedItems(list, v));
        if (!ReferenceEquals(_autoGatherListsCache.ItemFilterList, list))
        {
            _autoGatherListsCache.ItemFilterList = list;
            _autoGatherListsCache.ItemFilter     = string.Empty;
        }

        var itemFilter = _autoGatherListsCache.ItemFilter;
        ImGui.SetNextItemWidth(130f * Scale);
        if (ImGui.InputTextWithHint("##autoGatherItemFilter", "搜索物品...", ref itemFilter, 128))
            _autoGatherListsCache.ItemFilter = itemFilter;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("按名称过滤物品。搜索激活时禁用重新排序。");

        var filterKeywords = _autoGatherListsCache.ItemFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .ToArray();
        var filteringItems   = filterKeywords.Length > 0;
        var visibleItemIndices = new List<int>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; ++i)
        {
            var itemName = list.Items[i].Name[GatherBuddy.Language].ToString();
            if (filterKeywords.All(keyword => itemName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                visibleItemIndices.Add(i);
        }

        var visibleItems = visibleItemIndices.Select(index => list.Items[index]).ToList();
        var bulkActionButtonSize = new Vector2(ImGui.GetFrameHeight() + 6f * Scale, ImGui.GetFrameHeight());

        ImGui.SameLine();
        if (DrawAutoGatherIconButton("EnableVisibleItems", FontAwesomeIcon.Check.ToIconString(), bulkActionButtonSize, "启用此列表中可见的物品。", visibleItems.Count == 0))
            _plugin.AutoGatherListsManager.ChangeEnabled(list, visibleItems, true);

        ImGui.SameLine();
        if (DrawAutoGatherIconButton("DisableVisibleItems", FontAwesomeIcon.Ban.ToIconString(), bulkActionButtonSize, "禁用此列表中可见的物品。", visibleItems.Count == 0))
            _plugin.AutoGatherListsManager.ChangeEnabled(list, visibleItems, false);

        ImGui.SameLine();
        ImGui.Text($"{visibleItems.Count} / {list.Items.Count} 个物品在列表中");
        ImGui.NewLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - ImGui.GetStyle().ItemInnerSpacing.X);
        using var box = ImRaii.ListBox("##gatherWindowList", new Vector2(-1.5f * ImGui.GetStyle().ItemSpacing.X, -1));
        if (!box)
            return;

        _autoGatherListsCache.SetExcludedGatherbales(list.Items.OfType<Gatherable>());
        var gatherables = _autoGatherListsCache.FilteredGatherables;
        var selector    = _autoGatherListsCache.GatherableSelector;
        int changeIndex = -1, changeItemIndex = -1, deleteIndex = -1;

        for (var visibleIdx = 0; visibleIdx < visibleItemIndices.Count; ++visibleIdx)
        {
            var       i     = visibleItemIndices[visibleIdx];
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
            ImGui.Text("背包: ");
            var invTotal = item.GetTotalCount();
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

            if (!filteringItems)
            {
                using (var source = ImRaii.DragDropSource())
                {
                    if (source.Success)
                    {
                        _autoGatherListsCache.Selector.DragDropItem = new AutoGatherListsDragDropData(list, item, i);
                        ImGui.SetDragDropPayload(AutoGatherListsDragDropData.Label, []);
                        ImGui.TextUnformatted(item.Name[GatherBuddy.Language]);
                    }
                }

                var localIdx = i;
                using (var target = ImRaii.DragDropTarget())
                {
                    var dragDropData = _autoGatherListsCache.Selector.DragDropItem;
                    if (target.Success && ImGuiUtil.IsDropping(AutoGatherListsDragDropData.Label) && dragDropData != null)
                    {
                        _plugin.AutoGatherListsManager.MoveItem(dragDropData.List, dragDropData.ItemIdx, localIdx);
                        _autoGatherListsCache.Selector.DragDropItem = null;
                    }
                }
            }
        }

        if (visibleItemIndices.Count == 0)
            ImGui.TextDisabled("没有匹配的物品。");

        if (deleteIndex >= 0)
            _plugin.AutoGatherListsManager.RemoveItem(list, deleteIndex);

        if (changeIndex >= 0)
            _plugin.AutoGatherListsManager.ChangeItem(list, gatherables[changeItemIndex], changeIndex);

        if (ImGuiUtil.DrawDisabledButton(FontAwesomeIcon.Plus.ToIconString(), IconButtonSize, "将物品添加到列表末尾", false,
                true))
            _plugin.AutoGatherListsManager.AddItem(list, gatherables[_autoGatherListsCache.NewGatherableIdx]);

        ImGui.SameLine();
        var allEnabled = list.Items.All(i => list.EnabledItems[i]);
        if (ImGui.Checkbox("##AllEnabled", ref allEnabled))
        {
            foreach (var i in list.Items)
                _plugin.AutoGatherListsManager.ChangeEnabled(list, i, allEnabled);
        }
        ImGuiUtil.HoverTooltip((allEnabled ? "禁用" : "启用" ) + " 列表中的所有物品");

        ImGui.SameLine();
        if (selector.Draw(_autoGatherListsCache.NewGatherableIdx, out var idx))
        {
            _autoGatherListsCache.NewGatherableIdx = idx;
            _plugin.AutoGatherListsManager.AddItem(list, gatherables[_autoGatherListsCache.NewGatherableIdx]);
        }
    }

    private static bool DrawAutoGatherIconButton(string id, string iconText, Vector2 size, string tooltip, bool disabled = false)
    {
        var hoveredFlags = disabled ? ImGuiHoveredFlags.AllowWhenDisabled : ImGuiHoveredFlags.None;

        bool DrawCenteredButton()
        {
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            var cursor = ImGui.GetCursorScreenPos();
            var iconSize = ImGui.CalcTextSize(iconText);

            bool clicked;
            using (ImRaii.PushId(id))
                clicked = ImGui.Button(string.Empty, size);

            var iconPos = cursor + ((size - iconSize) / 2f);
            ImGui.GetWindowDrawList().AddText(iconPos, ImGui.GetColorU32(ImGuiCol.Text), iconText);
            return clicked;
        }

        if (disabled)
        {
            bool hovered;
            using (ImRaii.Disabled())
            {
                DrawCenteredButton();
                hovered = ImGui.IsItemHovered(hoveredFlags);
            }

            if (hovered)
                ImGui.SetTooltip(tooltip);
            return false;
        }

        var result = DrawCenteredButton();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return result;
    }

    private void DrawAutoGatherTab()
    {
        using var id  = ImRaii.PushId("AutoGatherLists");
        using var tab = ImRaii.TabItem("自动采集");

        ImGuiUtil.HoverTooltip(
            "没错！自动采集！");

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

        ItemDetailsWindow.Draw("列表详情", DrawAutoGatherListsLine, () =>
        {
            if (_autoGatherListsCache.Selector.Selected != null)
                DrawAutoGatherList(_autoGatherListsCache.Selector.Selected);
        });

        _autoGatherListsCache.Selector.DrawBaitBuyListResultPopup();
    }
}

