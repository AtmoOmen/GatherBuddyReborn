using ElliLib.Filesystem;
using GatherBuddy.AutoGather.Extensions;
using GatherBuddy.Classes;
using GatherBuddy.Enums;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GatherBuddy.AutoGather.Lists;

public partial class AutoGatherListsManager
{
    public void AddList(AutoGatherList list, FileSystem<AutoGatherList>.Folder? folder = null)
    {
        folder ??= _fileSystem.Root;
        try
        {
            _fileSystem.CreateLeaf(folder, list.Name, list);
        }
        catch
        {
            _fileSystem.CreateDuplicateLeaf(folder, list.Name, list);
        }
        Save();
        if (list.HasItems())
            SetActiveItems();
    }

    public void DeleteList(AutoGatherList list)
    {
        if (!_fileSystem.TryGetValue(list, out var leaf))
            return;

        var enabled = list.HasItems();
        _fileSystem.Delete(leaf);
        Save();
        if (enabled)
            SetActiveItems();
    }

    public void CreateFolder(string name, FileSystem<AutoGatherList>.Folder? parent = null)
    {
        parent ??= _fileSystem.Root;
        try
        {
            _fileSystem.CreateFolder(parent, name);
            Save();
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Warning($"创建文件夹失败: {e.Message}");
        }
    }

    public void DeleteFolder(FileSystem<AutoGatherList>.Folder folder)
    {
        if (folder.IsRoot)
            return;

        try
        {
            _fileSystem.Delete(folder);
            Save();
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Warning($"删除文件夹失败: {e.Message}");
        }
    }

    public void ChangeName(AutoGatherList list, string newName)
    {
        if (newName == list.Name || !_fileSystem.TryGetValue(list, out var leaf))
            return;

        try
        {
            _fileSystem.Rename(leaf, newName);
            list.Name = newName;
            Save();
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Warning($"重命名列表失败: {e.Message}");
        }
    }

    public void ChangeDescription(AutoGatherList list, string newDescription)
    {
        if (newDescription == list.Description)
            return;

        list.Description = newDescription;
        Save();
    }

    public void ToggleList(AutoGatherList list)
    {
        if (!list.Enabled && !ValidateFishingBait(list))
        {
            return;
        }
        
        if (!list.Enabled && !ValidateGatherablePerception(list))
        {
            return;
        }
        
        list.Enabled = !list.Enabled;
        Save();
        if (list.Items.Count > 0)
            SetActiveItems();
    }
    
    private unsafe bool ValidateFishingBait(AutoGatherList list)
    {
        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHookGlobalPreset)
            return true;
        try
        {
            var fishInList = list.Items.OfType<Fish>().Where(f => !f.IsSpearFish && list.EnabledItems.TryGetValue(f, out var enabled) && enabled).ToList();
            if (fishInList.Count == 0)
                return true;

            static uint GetInventoryItemCount(uint itemRowId)
            {
                return (uint)FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(itemRowId < 100000 ? itemRowId : itemRowId - 100000, itemRowId >= 100000);
            }

            var missingBaits = new System.Collections.Generic.HashSet<uint>();
            const uint VersatileLureId = 29717;
            var versatileLureCount = GetInventoryItemCount(VersatileLureId);
            var customPresetBaitIds = GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets
                ? GetCustomPresetBaitIds()
                : null;
            var inventoryCounts = new Dictionary<uint, uint>();
            var stopwatch = Stopwatch.StartNew();
            foreach (var fish in fishInList)
            {
                var baitId = fish.InitialBait?.Id ?? 0;
                if (baitId == 0)
                    continue;

                if (customPresetBaitIds != null && customPresetBaitIds.TryGetValue(fish.ItemId, out var customBaitId))
                {
                    baitId = customBaitId;
                    GatherBuddy.Log.Debug($"[自动采集] 为鱼 {fish.ItemId} 使用自定义预设钓饵 ID {baitId}");
                }

                if (!inventoryCounts.TryGetValue(baitId, out var baitCount))
                {
                    baitCount = GetInventoryItemCount(baitId);
                    inventoryCounts[baitId] = baitCount;
                }

                if (baitCount > 0 || versatileLureCount > 0)
                    continue;

                missingBaits.Add(baitId);
            }

            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds >= 10)
                GatherBuddy.Log.Debug($"[自动采集] 已为列表 '{list.Name}' 验证钓饵，耗时 {stopwatch.ElapsedMilliseconds} 毫秒（{fishInList.Count} 条鱼，{inventoryCounts.Count} 个唯一钓饵 ID）");
            if (missingBaits.Count > 0)
            {
                var baitIds = string.Join(", ", missingBaits);
                Communicator.PrintError($"[Auto-Gather] 无法启用列表: '{list.Name}', 缺少钓饵 IDs: {baitIds}, 并且没有万能拟饵");
                GatherBuddy.Log.Error($"[Auto-Gather] List '{list.Name}' not enabled: Missing bait IDs {baitIds} and no Versatile Lure");
                return false;
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            GatherBuddy.Log.Error($"[自动采集] 验证钓饵时出错: {ex.Message}\n{ex.StackTrace}");
            return true;
        }
    }
    
    private unsafe bool ValidateSingleFishBait(IGatherable item)
    {
        if (item is not Fish fish || fish.IsSpearFish)
            return true;
        if (GatherBuddy.Config.AutoGatherConfig.UseAutoHookGlobalPreset)
            return true;
        
        try
        {
            static uint GetInventoryItemCount(uint itemRowId)
            {
                return (uint)FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(itemRowId < 100000 ? itemRowId : itemRowId - 100000, itemRowId >= 100000);
            }
            
            const uint VersatileLureId = 29717;
            var baitId = fish.InitialBait?.Id ?? 0;
            
            if (baitId == 0)
                return true;
            if (GatherBuddy.Config.AutoGatherConfig.UseExistingAutoHookPresets)
            {
                var customBaitId = GetCustomPresetBaitId(fish.ItemId);
                if (customBaitId.HasValue)
                    baitId = customBaitId.Value;
            }
            
            if (GetInventoryItemCount(baitId) == 0 && GetInventoryItemCount(VersatileLureId) == 0)
            {
                Communicator.PrintError($"[Auto-Gather] 无法添加/启用: {fish.Name[GatherBuddy.Language]}, 缺少钓饵 ID: {baitId}, 并且没有万能拟饵");
                GatherBuddy.Log.Error($"[Auto-Gather] Fish {fish.ItemId} not enabled: Missing bait ID {baitId} and no Versatile Lure");
                return false;
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            GatherBuddy.Log.Error($"[自动采集] 验证单条鱼钓饵时出错: {ex.Message}");
            return true;
        }
    }
    
    private bool ValidateGatherablePerception(AutoGatherList list)
    {
        try
        {
            var gatherablesInList = list.Items.OfType<Gatherable>().Where(g => list.EnabledItems.TryGetValue(g, out var enabled) && enabled).ToList();
            if (gatherablesInList.Count == 0)
                return true;
            
            var currentJob = Dalamud.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
            var isMiner = currentJob == 16;
            var isBotanist = currentJob == 17;
            
            if (!isMiner && !isBotanist)
            {
                GatherBuddy.Log.Debug($"[自动采集] 跳过鉴别力验证 - 玩家当前不是采矿工或园艺工（当前职业: {currentJob}）");
                return true;
            }
            
            var playerPerception = DiscipleOfLand.Perception;
            var insufficientPerception = new System.Collections.Generic.List<(string Name, int Required, int Current)>();
            
            foreach (var gatherable in gatherablesInList)
            {
                var requiredPerception = (int)gatherable.GatheringData.PerceptionReq;
                if (requiredPerception == 0)
                    continue;
                
                var gatheringType = gatherable.GatheringType.ToGroup();
                if ((isMiner && gatheringType != GatheringType.采矿工) || (isBotanist && gatheringType != GatheringType.园艺工))
                {
                    continue;
                }
                
                GatherBuddy.Log.Debug($"[自动采集] 验证 {gatherable.Name[GatherBuddy.Language]}: 需要 {requiredPerception} 鉴别力（当前: {playerPerception}）");
                
                if (playerPerception < requiredPerception)
                {
                    insufficientPerception.Add((gatherable.Name[GatherBuddy.Language], requiredPerception, playerPerception));
                }
            }
            
            if (insufficientPerception.Count > 0)
            {
                var itemDetails = string.Join(", ", insufficientPerception.Select(x => $"{x.Name} (需求: {x.Required})"));
                Communicator.PrintError($"[Auto-Gather] 无法启用列表: '{list.Name}'， 鉴别力不足 (当前: {playerPerception}): {itemDetails}");
                GatherBuddy.Log.Error($"[Auto-Gather] List '{list.Name}' not enabled: Insufficient perception {playerPerception}");
                return false;
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            GatherBuddy.Log.Error($"[自动采集] 验证可采集物品鉴别力时出错: {ex.Message}\n{ex.StackTrace}");
            return true;
        }
    }
    
    private bool ValidateSingleGatherablePerception(IGatherable item)
    {
        if (item is not Gatherable gatherable)
            return true;
        
        try
        {
            var requiredPerception = (int)gatherable.GatheringData.PerceptionReq;
            if (requiredPerception == 0)
                return true;
            
            var currentJob = Dalamud.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
            var gatheringType = gatherable.GatheringType.ToGroup();
            var isMiner = currentJob == 16;
            var isBotanist = currentJob == 17;
            
            if (!isMiner && !isBotanist)
            {
                GatherBuddy.Log.Debug($"[自动采集] 跳过 {gatherable.Name[GatherBuddy.Language]} 的鉴别力验证 - 玩家当前不是采矿工或园艺工（当前职业: {currentJob}）");
                return true;
            }
            
            if ((isMiner && gatheringType != GatheringType.采矿工) || (isBotanist && gatheringType != GatheringType.园艺工))
            {
                GatherBuddy.Log.Debug($"[自动采集] 跳过 {gatherable.Name[GatherBuddy.Language]} 的鉴别力验证 - 物品属于不同的采集职业");
                return true;
            }
            
            var playerPerception = DiscipleOfLand.Perception;
            
            GatherBuddy.Log.Debug($"[自动采集] 验证 {gatherable.Name[GatherBuddy.Language]}: 需要 {requiredPerception} 鉴别力（当前: {playerPerception}）");
            
            if (playerPerception < requiredPerception)
            {
                Communicator.PrintError($"[Auto-Gather] 无法添加/启用: {gatherable.Name[GatherBuddy.Language]}, 鉴别力不足 (需求: {requiredPerception}, 当前: {playerPerception})");
                GatherBuddy.Log.Error($"[Auto-Gather] Gatherable {gatherable.ItemId} not enabled: Needs {requiredPerception} perception, current: {playerPerception}");
                return false;
            }
            
            return true;
        }
        catch (System.Exception ex)
        {
            GatherBuddy.Log.Error($"[自动采集] 验证单个可采集物品鉴别力时出错: {ex.Message}");
            return true;
        }
    }
    
    private Dictionary<uint, uint>? GetCustomPresetBaitIds()
    {
        try
        {
            var pluginConfigsDir = Dalamud.PluginInterface.ConfigDirectory.Parent?.FullName;
            if (string.IsNullOrEmpty(pluginConfigsDir))
                return null;

            var configPath = System.IO.Path.Combine(pluginConfigsDir, "AutoHook.json");
            if (!System.IO.File.Exists(configPath))
                return null;

            var json = System.IO.File.ReadAllText(configPath);
            var config = Newtonsoft.Json.Linq.JObject.Parse(json);

            var customPresets = config["HookPresets"]?["CustomPresets"] as Newtonsoft.Json.Linq.JArray;
            if (customPresets == null)
                return null;

            var customPresetBaitIds = new Dictionary<uint, uint>();
            foreach (var preset in customPresets)
            {
                var presetName = preset?["PresetName"]?.ToString();
                if (presetName == null || !uint.TryParse(presetName, out var presetFishId))
                    continue;

                var forcedBaitIdToken = preset?["ExtraCfg"]?["ForcedBaitId"];
                if (forcedBaitIdToken == null)
                    continue;

                var forcedBaitId = forcedBaitIdToken.ToObject<uint>();
                if (forcedBaitId > 0)
                    customPresetBaitIds[presetFishId] = forcedBaitId;
            }

            return customPresetBaitIds.Count > 0 ? customPresetBaitIds : null;
        }
        catch (System.Exception ex)
        {
            GatherBuddy.Log.Error($"[自动采集] 读取 AutoHook 配置以进行钓饵验证时出错: {ex.Message}");
            return null;
        }
    }

    private uint? GetCustomPresetBaitId(uint fishId)
    {
        var customPresetBaitIds = GetCustomPresetBaitIds();
        if (customPresetBaitIds == null || !customPresetBaitIds.TryGetValue(fishId, out var forcedBaitId))
            return null;

        GatherBuddy.Log.Debug($"[自动采集] 为鱼 {fishId} 找到自定义预设，钓饵 ID {forcedBaitId}");
        return forcedBaitId;
    }

    public void SetFallback(AutoGatherList list, bool value)
    {
        list.Fallback = value;
        Save();
        if (list.Items.Count > 0)
            SetActiveItems();
    }

    public void SetRemoveCompletedItems(AutoGatherList list, bool value)
    {
        if (list.RemoveCompletedItems == value)
            return;

        list.RemoveCompletedItems = value;
        Save();
        if (value && list.Enabled && !list.Fallback && list.Items.Count > 0)
            SetActiveItems(true);
    }

    public bool RemoveCompletedItemFromLists(IGatherable item)
    {
        var totalCount = item.GetTotalCount();
        var removedAny = false;
        foreach (var list in _fileSystem.Select(kvp => kvp.Key))
        {
            if (!list.Enabled || !list.RemoveCompletedItems || list.Fallback)
                continue;
            if (!list.EnabledItems.TryGetValue(item, out var itemEnabled) || !itemEnabled)
                continue;
            if (!list.Quantities.TryGetValue(item, out var quantity) || totalCount < quantity)
                continue;

            var index = list.Items.IndexOf(item);
            if (index < 0)
                continue;

            GatherBuddy.Log.Debug(
                $"[Auto-Gather] Auto-removing completed item '{item.Name[GatherBuddy.Language]}' from list '{list.Name}' ({totalCount}/{quantity}).");
            list.RemoveAt(index);
            removedAny = true;
        }

        if (!removedAny)
            return false;

        Save();
        SetActiveItems();
        return true;
    }

    private bool RemoveCompletedItemsFromEnabledLists()
    {
        var removedAny = false;
        foreach (var list in _fileSystem.Select(kvp => kvp.Key))
        {
            if (!list.Enabled || !list.RemoveCompletedItems || list.Fallback)
                continue;

            for (var i = list.Items.Count - 1; i >= 0; --i)
            {
                var item = list.Items[i];
                if (!list.EnabledItems.TryGetValue(item, out var itemEnabled) || !itemEnabled)
                    continue;
                if (!list.Quantities.TryGetValue(item, out var quantity))
                    continue;

                var totalCount = item.GetTotalCount();
                if (totalCount < quantity)
                    continue;

                GatherBuddy.Log.Debug(
                    $"[Auto-Gather] Auto-removing completed item '{item.Name[GatherBuddy.Language]}' from list '{list.Name}' ({totalCount}/{quantity}).");
                list.RemoveAt(i);
                removedAny = true;
            }
        }

        return removedAny;
    }

    public void AddItem(AutoGatherList list, IGatherable item)
    {
        if (list.Add(item))
        {
            if (list.Enabled && !ValidateSingleFishBait(item))
            {
                list.SetEnabled(item, false);
            }
            if (list.Enabled && !ValidateSingleGatherablePerception(item))
            {
                list.SetEnabled(item, false);
            }
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void RemoveItem(AutoGatherList list, int idx)
    {
        if (idx < 0 || idx >= list.Items.Count)
            return;

        list.RemoveAt(idx);
        Save();
        if (list.Enabled)
            SetActiveItems();
    }

    public void ChangeItem(AutoGatherList list, IGatherable item, int idx)
    {
        if (idx < 0 || idx >= list.Items.Count)
            return;

        if (list.Replace(idx, item))
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void ChangeQuantity(AutoGatherList list, IGatherable item, uint quantity)
    {
        if (list.SetQuantity(item, quantity))
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void ChangeEnabled(AutoGatherList list, IGatherable item, bool enabled)
    {
        if (enabled && list.Enabled && !ValidateSingleFishBait(item))
        {
            return;
        }
        
        if (enabled && list.Enabled && !ValidateSingleGatherablePerception(item))
        {
            return;
        }
        
        if (list.SetEnabled(item, enabled))
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void ChangeEnabled(AutoGatherList list, IReadOnlyCollection<IGatherable> items, bool enabled)
    {
        var changed = false;
        var skipped = 0;

        foreach (var item in items)
        {
            if (enabled && list.Enabled && !ValidateSingleFishBait(item))
            {
                skipped++;
                continue;
            }

            if (enabled && list.Enabled && !ValidateSingleGatherablePerception(item))
            {
                skipped++;
                continue;
            }

            changed |= list.SetEnabled(item, enabled);
        }

        if (skipped > 0)
            GatherBuddy.Log.Debug($"[Auto-Gather] Skipped enabling {skipped:N0} visible item(s) in list '{list.Name}' because validation failed.");

        if (changed)
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void MoveItem(AutoGatherList list, int idx1, int idx2)
    {
        if (list.Move(idx1, idx2))
        {
            Save();
            if (list.Enabled)
                SetActiveItems();
        }
    }

    public void MoveItem(AutoGatherList source, AutoGatherList destination, int idx)
    {
        var item = source.Items[idx];
        var quantity = source.Quantities[item];
        if (destination.Add(item, quantity))
        {
            destination.SetEnabled(item, source.EnabledItems[item]);
            destination.SetPreferredLocation(item, source.PreferredLocations.GetValueOrDefault(item));
        }
        else
        {
            destination.SetQuantity(item, destination.Quantities[item] + quantity);
        }
        source.RemoveAt(idx);
        Save();
        if (source.Enabled || destination.Enabled)
            SetActiveItems();
    }

    public void ChangePreferredLocation(AutoGatherList list, IGatherable? item, ILocation? location)
    {
        if (item == null)
            return;
        if (list.SetPreferredLocation(item, location))
        {
            Save();
        }
    }

    public event Action? ListOrderChanged;

    public void MoveList(FileSystem<AutoGatherList>.Leaf movedList, FileSystem<AutoGatherList>.Leaf insertAt, bool movedFromUpperPart)
    {
        if (movedList.Parent != insertAt.Parent)
            throw new InvalidOperationException();

        var insertIdx = insertAt.Value.Order;
        if (movedFromUpperPart || movedList.Value.Order < insertIdx) insertIdx++;

        // Using an O(n) algorithm makes the numbering sparse, but it's fine for ordering.
        foreach (var leaf in movedList.Parent.GetLeaves())
        {
            if (leaf.Value.Order >= insertIdx)
                leaf.Value.Order++;
        }
        movedList.Value.Order = insertIdx;
        Save();
        ListOrderChanged?.Invoke();        
    }

    public void MoveListUp(FileSystem<AutoGatherList>.Leaf list)
        => MoveListOnce(list, false);

    public void MoveListDown(FileSystem<AutoGatherList>.Leaf list)
        => MoveListOnce(list, true);

    private void MoveListOnce(FileSystem<AutoGatherList>.Leaf list, bool down)
    {
        FileSystem<AutoGatherList>.Leaf? neighbor = null;
        // XOR flips the comparison, finding the right neighbor instead of the left when true.
        foreach (var sibling in list.Parent.GetLeaves())
        {
            if (sibling != list && sibling.Value.Order < list.Value.Order ^ down && (neighbor == null || neighbor.Value.Order < sibling.Value.Order ^ down))
                neighbor = sibling;
        }

        if (neighbor != null)
        {
            (list.Value.Order, neighbor.Value.Order) = (neighbor.Value.Order, list.Value.Order);
            Save();
            ListOrderChanged?.Invoke();
        }
    }

    public ILocation? GetPreferredLocation(IGatherable item)
    {
        foreach (var list in _fileSystem.Select(kvp => kvp.Key))
        {
            if (list.Enabled && !list.Fallback && list.PreferredLocations.TryGetValue(item, out var loc))
            {
                return loc;
            }
        }
        return null;
    }
}
