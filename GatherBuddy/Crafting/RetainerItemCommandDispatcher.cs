using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace GatherBuddy.Crafting;

internal enum RetainerItemCommand : long
{
    Retrieve = 0,
    RetrieveQuantity = 3,
}

internal static unsafe class RetainerItemCommandDispatcher
{
    private const int InventoryContextEventOffset = 0x28;

    internal static bool TryRetrieve(InventoryType retainerContainer, uint retainerSlot)
        => Dispatch(retainerContainer, retainerSlot, RetainerItemCommand.Retrieve);

    internal static bool TryRetrieveQuantity(InventoryType retainerContainer, uint retainerSlot)
        => Dispatch(retainerContainer, retainerSlot, RetainerItemCommand.RetrieveQuantity);

    private static bool Dispatch(InventoryType container, uint slot, RetainerItemCommand command)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
        if (agent == null)
        {
            GatherBuddy.Log.Debug($"[RetainerItemCommandDispatcher] 为 {container} 的栏位 {slot} 分派 {command} 时 AgentRetainer 为空");
            return false;
        }

        if (!agent->IsAgentActive())
        {
            GatherBuddy.Log.Debug($"[RetainerItemCommandDispatcher] 为 {container} 的栏位 {slot} 分派 {command} 时 AgentRetainer 未激活");
            return false;
        }

        void* contextEventPtr = (byte*)agent + InventoryContextEventOffset;
        void** vtable = *(void***)contextEventPtr;
        var sendCommand = (delegate* unmanaged<void*, uint, InventoryType, ulong, RetainerItemCommand, void>)vtable[0];

        GatherBuddy.Log.Debug($"[RetainerItemCommandDispatcher] 正在为 {container} 的栏位 {slot} 分派 {command}");
        sendCommand(contextEventPtr, slot, container, 0, command);
        return true;
    }
}
