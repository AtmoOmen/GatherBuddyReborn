using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Utility;
using GatherBuddy.Automation;
using GatherBuddy.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace GatherBuddy.AutoGather
{
    public partial class AutoGather
    {
        private static readonly List<string> CollectablePatterns =
        [
            "collectability of",
            "収集価値",
            "Sammlerwert",
            "Valeur de collection",
            "收藏价值"
        ];

        private unsafe bool HandleFishingCollectable()
        {
            if (!GatherBuddy.Config.AutoGatherConfig.AutoCollectablesFishing)
                return false;

            var addon = SelectYesnoAddon;
            if (addon == null || !addon->IsReady)
                return false;

            var master = new AddonMaster.SelectYesno(addon);
            var text = master.TextLegacy;

            if (!CollectablePatterns.Any(text.Contains))
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] 确认对话框提示与收藏品模式不匹配: {text}");
                return false;
            }

            GatherBuddy.Log.Debug($"[AutoCollectable] 检测到收藏品对话框，文本: {text}");

            if (addon->AtkValuesCount < 15)
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] AtkValues 不足（{addon->AtkValuesCount}），无法读取物品 ID");
                return false;
            }
            
            var itemIdEncoded = addon->AtkValues[14].UInt;
            if (itemIdEncoded < 500000)
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] 无效的编码物品 ID: {itemIdEncoded}");
                return false;
            }
            
            var itemId = itemIdEncoded - 500000;
            GatherBuddy.Log.Debug($"[AutoCollectable] 已提取物品 ID: {itemId}（来自编码值 {itemIdEncoded}）");

            var itemSheet = Dalamud.GameData.GetExcelSheet<Item>();
            var item = itemSheet.GetRowOrDefault(itemId);
            
            if (item == null || item.Value.RowId == 0)
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] 找不到 ID 为 {itemId} 的物品");
                return false;
            }
            
            var itemValue = item.Value;
            if (!itemValue.IsCollectable)
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] 物品 [{itemValue.RowId}] {itemValue.Name} 不是收藏品");
                return false;
            }

            GatherBuddy.Log.Debug($"[AutoCollectable] 检测到物品 [{itemValue.RowId}] {itemValue.Name}");

            if (!int.TryParse(Regex.Match(text, @"\d+").Value, out var value))
            {
                GatherBuddy.Log.Debug($"[AutoCollectable] 无法从文本解析收藏价值");
                return false;
            }

            GatherBuddy.Log.Debug($"[AutoCollectable] 检测到收藏价值: {value}");
            GatherBuddy.Log.Debug($"[AutoCollectable] 物品数据 - 以太缩减: {itemValue.AetherialReduce}，附加数据.RowId: {itemValue.AdditionalData.RowId}");
            {
                if (itemValue.AetherialReduce > 0)
                {
                    GatherBuddy.Log.Debug($"[AutoCollectable] 接受 [{itemValue.RowId}] {itemValue.Name} - 以太沙鱼");
                    Callback.Fire(&addon->AtkUnitBase, true, 0);
                    return true;
                }
                else if (itemValue.AdditionalData.RowId != 0)
                {
                    var wksItem = Dalamud.GameData.GetExcelSheet<WKSItemInfo>().GetRow(itemValue.AdditionalData.RowId);
                    if (wksItem.RowId != 0)
                {
                        GatherBuddy.Log.Debug($"[AutoCollectable] 接受 [{itemValue.RowId}] {itemValue.Name} - 星际鱼 {wksItem.WKSItemSubCategory.ValueNullable?.Name ?? "null"}");
                        Callback.Fire(&addon->AtkUnitBase, true, 0);
                        return true;
                    }
                    else
                    {
                        GatherBuddy.Log.Debug($"[AutoCollectable] 找不到 [{itemValue.RowId}] {itemValue.Name} 的收藏品商店物品");
                    }
                }
                else
                {
                    GatherBuddy.Log.Debug($"[AutoCollectable] 找不到 [{itemValue.RowId}] {itemValue.Name} 的收藏品商店物品");
                }
            }

            GatherBuddy.Log.Debug($"[AutoCollectable] 接受 [{itemValue.RowId}] {itemValue.Name} - 普通收藏品鱼，价值 {value}");
            Callback.Fire(&addon->AtkUnitBase, true, 0);
            return true;
        }
    }
}
