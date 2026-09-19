using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using GatherBuddy.Config;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.Time;
using Newtonsoft.Json;

namespace GatherBuddy.GatherGroup;

internal static class SeStringBuilderExtension
{
    public static SeStringBuilder ColoredText(this SeStringBuilder builder, string text, int colorIdx)
    {
        if (!Configuration.ForegroundColors.ContainsKey(colorIdx))
            return builder.AddText(text);

        return builder.AddUiForeground((ushort)colorIdx).AddText(text).AddUiForegroundOff();
    }
}

public class GatherGroupManager
{
    public const string FileName = "gather_groups.json";

    public SortedList<string, TimedGroup> Groups { get; init; } = new();

    public TimedGroup? this[string value]
        => TryGetValue(value, out var ret) ? ret : null;

    public bool TryGetValue(string value, out TimedGroup ret)
        => Groups.TryGetValue(value.ToLowerInvariant().Trim(), out ret!);

    public SeString CreateHelp()
    {
        SeStringBuilder b = new();
        b.AddText("Please use with ")
            .ColoredText("/gathergroup ",                      GatherBuddy.Config.SeColorCommands)
            .ColoredText("[Name] ",                            GatherBuddy.Config.SeColorNames)
            .ColoredText("[optional: Eorzea Minute Offset]\n", GatherBuddy.Config.SeColorArguments)
            .AddText("Available groups are:\n");
        foreach (var value in Groups.Values)
        {
            b.ColoredText($"          {value.Name}", GatherBuddy.Config.SeColorNames)
                .AddText($" - {value.Description}\n");
        }

        return b.BuiltString;
    }

    public bool AddGroup(string name, TimedGroup group)
    {
        var lowerName = name.ToLowerInvariant().Trim();
        if (lowerName.Length == 0 || Groups.ContainsKey(lowerName))
            return false;

        Groups.Add(lowerName, group);
        return true;
    }

    public bool ChangeDescription(TimedGroup group, string description)
    {
        if (group.Description == description)
            return false;

        group.Description = description;
        return true;
    }

    public bool RenameGroup(TimedGroup group, string newName)
    {
        if (newName == group.Name)
            return false;

        var newSearchName = newName.ToLowerInvariant().Trim();
        if (newSearchName.Length == 0 || Groups.ContainsKey(newSearchName))
            return false;

        RemoveGroup(group);
        group.Name = newName;
        Groups.Add(newSearchName, group);
        return true;
    }

    public bool RemoveGroup(TimedGroup group)
        => Groups.Remove(group.Name.ToLowerInvariant().Trim());

    public bool ChangeGroupNodeLocation(TimedGroup group, int idx, ILocation? location)
    {
        if (ReferenceEquals(group.Nodes[idx].PreferLocation, location)
         || location != null && !location.Gatherables.Contains(@group.Nodes[idx].Item))
            return false;

        group.Nodes[idx].PreferLocation = location;
        return true;
    }

    public bool ChangeGroupNode(TimedGroup group, int idx, IGatherable? item, int? start, int? end, string? annotation, bool delete)
    {
        if (idx < 0 || group.Nodes.Count < idx)
            return false;

        if (delete)
        {
            if (idx == group.Nodes.Count)
                return false;

            group.Nodes.RemoveAt(idx);
            return true;
        }

        if (group.Nodes.Count == idx && item != null && !delete)
        {
            var newNode = new TimedGroupNode(item)
            {
                EorzeaStartMinute = start == null ? 0 : Math.Clamp(start.Value, 0, RealTime.MinutesPerDay - 1),
                EorzeaEndMinute   = end == null ? 0 : Math.Clamp(end.Value,     0, RealTime.MinutesPerDay - 1),
                Annotation        = annotation ?? string.Empty,
            };
            group.Nodes.Add(newNode);
            return true;
        }

        var changes = false;
        var node    = group.Nodes[idx];
        if (item != null)
        {
            if (!ReferenceEquals(node.Item, item))
                changes = true;
            node.Item = item;
            if (node.PreferLocation != null && !node.PreferLocation.Gatherables.Contains(item))
                node.PreferLocation = null;
        }

        if (start != null)
        {
            start = Math.Clamp(start.Value, 0, RealTime.MinutesPerDay - 1);
            if (start.Value != node.EorzeaStartMinute)
                changes = true;
            node.EorzeaStartMinute = start.Value;
        }

        if (end != null)
        {
            end = Math.Clamp(end.Value, 0, RealTime.MinutesPerDay - 1);
            if (end.Value != node.EorzeaEndMinute)
                changes = true;
            node.EorzeaEndMinute = end.Value;
        }

        if (annotation != null)
        {
            if (annotation != node.Annotation)
                changes = true;
            node.Annotation = annotation;
        }

        return changes;
    }

    public void MoveNode(TimedGroup group, int idx1, int idx2)
    {
        if (Functions.Move(group.Nodes, idx1, idx2))
            Save();
    }

    public void Save()
    {
        var file = Functions.ObtainSaveFile(FileName);
        if (file == null)
            return;

        try
        {
            var text = JsonConvert.SerializeObject(Groups.Values.Select(g => g.ToConfig()), Formatting.Indented);
            File.WriteAllText(file.FullName, text);
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"无法将采集组写入文件 {file.FullName}:\n{e}");
        }
    }

    public bool SetDefaults(bool restore = false)
    {
        var change = false;
        foreach (var cfgGroup in GroupData.DefaultGroups)
        {
            var searchName = cfgGroup.Name.ToLowerInvariant().Trim();
            if (Groups.ContainsKey(searchName))
            {
                if (!restore)
                    continue;

                Groups.Remove(searchName);
            }

            TimedGroup.FromConfig(cfgGroup, out var group);
            Groups.Add(searchName, group);
            change = true;
        }

        return change;
    }


    public static GatherGroupManager Load()
    {
        var manager = new GatherGroupManager();
        var file    = Functions.ObtainSaveFile(FileName);
        if (file is not { Exists: true })
        {
            manager.SetDefaults();
            manager.Save();
            return manager;
        }

        try
        {
            var text    = File.ReadAllText(file.FullName);
            var data    = JsonConvert.DeserializeObject<List<TimedGroup.Config>>(text)!;
            var changes = false;
            foreach (var config in data)
            {
                if (!TimedGroup.FromConfig(config, out var group))
                {
                    GatherBuddy.Log.Error($"采集组 {group.Name} 中的无效物品已跳过。");
                    changes = true;
                }

                var searchName = group.Name.ToLowerInvariant().Trim();
                if (searchName.Length == 0)
                {
                    changes = true;
                    GatherBuddy.Log.Error("发现没有名称的采集组，已跳过。");
                    continue;
                }

                if (!manager.Groups.TryAdd(searchName, group))
                {
                    changes = true;
                    GatherBuddy.Log.Error($"发现多个同名采集组 {searchName}，后续的已跳过。");
                }
            }

            if (changes)
            {
                Dalamud.Notifications.AddNotification(new Notification()
                {
                    Title = "GatherBuddy 错误",
                    Content =
                        "部分采集组加载失败。详情请查看插件日志。此状态不会被保存，如果持续出现，需要手动更改一个采集组以触发保存。",
                    MinimizedText = "采集组加载失败。",
                    Type          = NotificationType.Error,
                });
            }

            if (manager.SetDefaults() && !changes)
                manager.Save();
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"加载采集组时出错:\n{e}");
            manager.Groups.Clear();
            manager.SetDefaults();
            manager.Save();
        }

        return manager;
    }
}
