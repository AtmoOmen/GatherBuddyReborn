using System;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatherBuddy.Plugin;
using Lumina.Excel.Sheets;

namespace GatherBuddy.AutoGather.Collectables;

public static class TeleportHelper
{
    public static unsafe bool TryFindAetheryteByName(string name, out TeleportInfo info, out string aetherName)
    {
        info = new TeleportInfo();
        aetherName = string.Empty;
        try
        {
            var tp = Telepo.Instance();
            if (tp->UpdateAetheryteList() == null) return false;
            var tpInfos = tp->TeleportList;
            foreach (var tpInfo in tpInfos)
            {
                var aetheryteName = Dalamud.GameData.GetExcelSheet<Aetheryte>()
                    .FirstOrDefault(x => x.RowId == tpInfo.AetheryteId).PlaceName.ValueNullable?.Name
                    .ToString();
                
                var result = aetheryteName.Contains(name, StringComparison.OrdinalIgnoreCase);
                if (!result && !aetheryteName.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                info = tpInfo;
                aetherName = aetheryteName;
                return true;
            }
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Error($"查找传送信息失败: {ex}");
            return false;
        }
        GatherBuddy.Log.Error("查找传送信息失败");
        return false;
    }

    public static unsafe bool Teleport(uint aetheryteId, byte subIndex)
    {
        return Telepo.Instance()->Teleport(aetheryteId, subIndex);
    }
}
