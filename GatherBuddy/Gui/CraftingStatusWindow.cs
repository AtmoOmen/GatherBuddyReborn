using System;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using GatherBuddy.Crafting;

namespace GatherBuddy.Gui;

public class CraftingStatusWindow : Window
{
    private CraftingQueueProcessor? _queueProcessor;
    private bool? _pendingCollapseState = null;
    private bool _requestFocus = false;
    private bool _wasFocusedLastFrame = false;
    private IReadOnlyList<CraftingListItem>? _cachedEstimateQueue = null;
    private CraftingListConsumableSettings? _cachedEstimateConsumables = null;
    private long[] _cachedRemainingEstimatesByIndex = [];
    private int _cachedEstimateQueueCount = -1;
    private int _cachedEstimateActionDelayMs = -1;

    public CraftingStatusWindow() 
        : base("制作状态###GatherBuddyCraftingStatus", ImGuiWindowFlags.AlwaysAutoResize)
    {
        IsOpen = false;
        ShowCloseButton = true;
        RespectCloseHotkey = true;
        SizeCondition = ImGuiCond.Appearing;
    }

    public void SetQueueProcessor(CraftingQueueProcessor? processor)
    {
        _queueProcessor = processor;
        ResetRemainingEstimateCache();
        if (processor == null)
        {
            IsOpen = false;
            _pendingCollapseState = null;
            _requestFocus = false;
            return;
        }

        OpenOrRestore();
    }

    public bool HasActiveQueue
        => _queueProcessor != null;

    public void OpenOrRestore()
    {
        if (_queueProcessor == null)
            return;

        IsOpen = true;
        _pendingCollapseState = false;
        _requestFocus = true;
    }

    public override bool DrawConditions()
    {
        return _queueProcessor != null && IsOpen;
    }

    public override void PreDraw()
    {
        if (!IsOpen)
            return;

        if (_pendingCollapseState.HasValue)
        {
            ImGui.SetNextWindowCollapsed(_pendingCollapseState.Value, ImGuiCond.Always);
            _pendingCollapseState = null;
        }

        if (_requestFocus)
        {
            ImGui.SetNextWindowFocus();
            _requestFocus = false;
        }
    }

    public override void Draw()
    {
        if (_queueProcessor == null)
            return;
        
        var isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (isFocused)
        {
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow("制作状态###GatherBuddyCraftingStatus");
            _wasFocusedLastFrame = true;
        }
        else if (_wasFocusedLastFrame)
        {
            GatherBuddy.ControllerSupport?.UpdateFocusedWindow(null);
            _wasFocusedLastFrame = false;
        }

        var currentState = _queueProcessor.CurrentState;
        var currentIndex = _queueProcessor.CurrentQueueIndex;
        var totalCount = _queueProcessor.QueueCount;
        var currentItem = _queueProcessor.CurrentRecipeItem;

        ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.8f, 1.0f), "制作队列运行中");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text($"状态: {GetStateDisplayName(currentState)}");
        ImGui.Text($"进度: {Math.Min(currentIndex + 1, totalCount)} / {totalCount}");

        if (_queueProcessor.Paused && !string.IsNullOrWhiteSpace(_queueProcessor.PauseReason))
        {
            ImGui.PushTextWrapPos();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.82f, 0.24f, 1.0f), _queueProcessor.PauseReason);
            ImGui.PopTextWrapPos();
        }

        if (currentItem != null)
        {
            var recipeSheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
            if (recipeSheet != null && recipeSheet.TryGetRow(currentItem.RecipeId, out var recipe))
            {
                var itemName = recipe.ItemResult.Value.Name.ExtractText();
                ImGui.Text($"当前配方: {itemName}");
            }
        }

        if (ShouldShowRemainingEstimate(currentState))
        {
            var remainingMs = GetRemainingEstimate(currentIndex);
            ImGui.Text($"预计剩余时间: ~{CraftingTimeEstimator.FormatDuration(remainingMs)}");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("根据队列各项制作路径估算。宏、标准求解器和仅推进度通过模拟计算，Raphael 在有缓存方案时使用方案长度，否则暂用默认值。不含采集、修理和切换职业的耗时。");
        }
        else if (!ShouldRetainRemainingEstimateCache(currentState))
        {
            ResetRemainingEstimateCache();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (currentState == CraftingQueueProcessor.QueueState.Complete)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), "队列完成!");
            ImGui.Spacing();
            if (ImGui.Button("关闭"))
            {
                IsOpen = false;
                _queueProcessor = null;
            }
        }
        else
        {
            ImGui.TextDisabled(_queueProcessor.Paused ? "队列已暂停" : "队列处理中...");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (!_queueProcessor.Paused)
            {
                if (ImGui.Button("暂停"))
                {
                    _queueProcessor.Pause();
                }
            }
            else
            {
                if (ImGui.Button("继续"))
                {
                    _queueProcessor.Resume();
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button("停止"))
            {
                CraftingGatherBridge.StopQueue();
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var delay = GatherBuddy.Config.VulcanExecutionDelayMs;
            ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(150f));
            if (ImGui.SliderInt("操作延迟 (ms)", ref delay, 0, 1000))
            {
                GatherBuddy.Config.VulcanExecutionDelayMs = Math.Clamp(delay, 0, 1000);
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("每个制作操作之间的延迟时间");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            if (ImGui.Button("打开 Vulcan 窗口"))
            {
                if (GatherBuddy.VulcanWindow != null)
                {
                    GatherBuddy.VulcanWindow.IsOpen = true;
                }
            }
        }
    }

    private void ResetRemainingEstimateCache()
    {
        _cachedEstimateQueue = null;
        _cachedEstimateConsumables = null;
        _cachedRemainingEstimatesByIndex = [];
        _cachedEstimateQueueCount = -1;
        _cachedEstimateActionDelayMs = -1;
    }

    private long GetRemainingEstimate(int currentIndex)
    {
        if (_queueProcessor == null)
            return 0;

        var queue = _queueProcessor.Queue;
        var consumables = _queueProcessor.ListConsumables;
        var actionDelayMs = GatherBuddy.Config.VulcanExecutionDelayMs;
        if (ReferenceEquals(_cachedEstimateQueue, queue)
         && ReferenceEquals(_cachedEstimateConsumables, consumables)
         && _cachedEstimateQueueCount == queue.Count
         && _cachedEstimateActionDelayMs == actionDelayMs)
            return GetCachedRemainingEstimateAtIndex(currentIndex);

        _cachedEstimateQueue = queue;
        _cachedEstimateConsumables = consumables;
        _cachedEstimateQueueCount = queue.Count;
        _cachedEstimateActionDelayMs = actionDelayMs;
        _cachedRemainingEstimatesByIndex = BuildRemainingEstimates(queue, actionDelayMs, consumables);
        return GetCachedRemainingEstimateAtIndex(currentIndex);
    }

    private long GetCachedRemainingEstimateAtIndex(int currentIndex)
    {
        if (_cachedRemainingEstimatesByIndex.Length == 0)
            return 0;

        var safeIndex = Math.Clamp(currentIndex, 0, _cachedRemainingEstimatesByIndex.Length - 1);
        return _cachedRemainingEstimatesByIndex[safeIndex];
    }

    private static long[] BuildRemainingEstimates(IReadOnlyList<CraftingListItem> queue, int actionDelayMs, CraftingListConsumableSettings? consumables)
    {
        if (queue.Count == 0)
            return [];

        var remainingByIndex = new long[queue.Count];
        long remainingMs = 0;
        for (var i = queue.Count - 1; i >= 0; i--)
        {
            remainingMs += CraftingTimeEstimator.EstimateItemMs(queue[i], actionDelayMs, consumables);
            remainingByIndex[i] = remainingMs;
        }

        return remainingByIndex;
    }

    private static bool ShouldShowRemainingEstimate(CraftingQueueProcessor.QueueState state)
    {
        return state == CraftingQueueProcessor.QueueState.ReadyForCraft
            || state == CraftingQueueProcessor.QueueState.Crafting;
    }

    private static bool ShouldRetainRemainingEstimateCache(CraftingQueueProcessor.QueueState state)
    {
        return state == CraftingQueueProcessor.QueueState.ReadyForCraft
            || state == CraftingQueueProcessor.QueueState.Crafting
            || state == CraftingQueueProcessor.QueueState.WaitingForJobSwitch
            || state == CraftingQueueProcessor.QueueState.Repairing
            || state == CraftingQueueProcessor.QueueState.ExtractingMateria;
    }

    private static string GetStateDisplayName(CraftingQueueProcessor.QueueState state)
    {
        return state switch
        {
            CraftingQueueProcessor.QueueState.Idle => "空闲",
            CraftingQueueProcessor.QueueState.NavigatingToRetainerBell => "前往雇员铃",
            CraftingQueueProcessor.QueueState.WaitingForGather => "采集材料中",
            CraftingQueueProcessor.QueueState.WaitingForJobSwitch => "切换职业中",
            CraftingQueueProcessor.QueueState.Repairing => "修理装备中",
            CraftingQueueProcessor.QueueState.ExtractingMateria => "精制魔晶石中",
            CraftingQueueProcessor.QueueState.WaitingForRaphaelSolution => "使用 Raphael 求解中",
            CraftingQueueProcessor.QueueState.ReadyForCraft => "准备制作",
            CraftingQueueProcessor.QueueState.Crafting => "制作中",
            CraftingQueueProcessor.QueueState.Complete => "完成",
            _ => "未知"
        };
    }
    
    private (int currentCraft, int totalCrafts) GetCurrentRecipeCraftNumbers()
    {
        if (_queueProcessor == null)
            return (0, 0);
        
        var currentItem = _queueProcessor.CurrentRecipeItem;
        if (currentItem == null)
            return (0, 0);
        
        var currentIndex = _queueProcessor.CurrentQueueIndex;
        var currentRecipeId = currentItem.RecipeId;
        
        int firstIndex = currentIndex;
        while (firstIndex > 0)
        {
            var prevItem = GetQueueItemAt(firstIndex - 1);
            if (prevItem == null || prevItem.RecipeId != currentRecipeId)
                break;
            firstIndex--;
        }
        
        int lastIndex = currentIndex;
        while (lastIndex < _queueProcessor.QueueCount - 1)
        {
            var nextItem = GetQueueItemAt(lastIndex + 1);
            if (nextItem == null || nextItem.RecipeId != currentRecipeId)
                break;
            lastIndex++;
        }
        
        int currentCraftNumber = currentIndex - firstIndex + 1;
        int totalCrafts = lastIndex - firstIndex + 1;
        
        return (currentCraftNumber, totalCrafts);
    }
    
    private CraftingListItem? GetQueueItemAt(int index)
    {
        if (_queueProcessor == null || index < 0 || index >= _queueProcessor.QueueCount)
            return null;
        return _queueProcessor.Queue[index];
    }
}
