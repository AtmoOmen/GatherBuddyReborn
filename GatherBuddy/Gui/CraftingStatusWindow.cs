using System;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using GatherBuddy.Crafting;

namespace GatherBuddy.Gui;

public class CraftingStatusWindow : Window
{
    private CraftingQueueProcessor? _queueProcessor;
    private bool _wasFocusedLastFrame = false;

    public CraftingStatusWindow() 
        : base("制作状态###GatherBuddyCraftingStatus", 
               ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        IsOpen = false;
        ShowCloseButton = true;
        RespectCloseHotkey = true;
        SizeCondition = ImGuiCond.Appearing;
    }

    public void SetQueueProcessor(CraftingQueueProcessor? processor)
    {
        _queueProcessor = processor;
        IsOpen = processor != null;
    }

    public override bool DrawConditions()
    {
        return _queueProcessor != null && IsOpen;
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

        if (currentItem != null)
        {
            var recipeSheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
            if (recipeSheet != null && recipeSheet.TryGetRow(currentItem.RecipeId, out var recipe))
            {
                var itemName = recipe.ItemResult.Value.Name.ExtractText();
                ImGui.Text($"当前配方: {itemName}");
            }
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
            ImGui.TextDisabled("队列处理中...");
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
            ImGui.SetNextItemWidth(150);
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
        
        try
        {
            var queueField = typeof(CraftingQueueProcessor).GetField("_queue", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (queueField?.GetValue(_queueProcessor) is List<CraftingListItem> queue)
            {
                return queue[index];
            }
        }
        catch { }
        
        return null;
    }
}
