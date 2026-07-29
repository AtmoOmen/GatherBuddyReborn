using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatherBuddy.Plugin;

namespace GatherBuddy.SeFunctions;

public static unsafe class Teleporter
{
    public static bool IsAttuned(uint aetheryte)
    {
        var teleport = Telepo.Instance();
        if (teleport == null)
        {
            GatherBuddy.Log.Error("无法检查以太之光共鸣状态: Telepo 不可用");
            return false;
        }

        if (Control.Instance()->LocalPlayer == null)
            return true;

        teleport->UpdateAetheryteList();

        var endPtr = teleport->TeleportList.Last;
        for (var it = teleport->TeleportList.First; it != endPtr; ++it)
        {
            if (it->AetheryteId == aetheryte)
                return true;
        }

        return false;
    }

    public static bool Teleport(uint aetheryte)
    {
        if (IsAttuned(aetheryte))
        {
            Telepo.Instance()->Teleport(aetheryte, 0);
            return true;
        }

        Communicator.PrintError("无法传送至 ",
            GatherBuddy.GameData.Aetherytes.TryGetValue(aetheryte, out var a) ? a.Name : "未知以太之光", GatherBuddy.Config.SeColorNames,
            ", 尚未与其共鸣");
        return false;
    }

    // Teleport without checking for attunement. Use at own risk.
    public static void TeleportUnchecked(uint aetheryte)
    {
        Telepo.Instance()->Teleport(aetheryte, 0);
    }
}
