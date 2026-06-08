using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GatherBuddy.Automation;
using Functions = GatherBuddy.Plugin.Functions;

namespace GatherBuddy.Gui;

internal sealed unsafe class NativeItemTooltipBridge : IDisposable
{
    private const string HostAddonName = "ChatLog";
    private const string ItemDetailAddonName = "ItemDetail";
    private const string ItemDetailSetPositionPreservingOriginalSignature = "E8 ?? ?? ?? ?? 45 85 ED 4C 8B AC 24";
    private const float AnchorEpsilon = 0.5f;

    private AtkResNode* _anchorNode;
    private nint _attachedHostAddonAddress;
    private delegate* unmanaged[Thiscall]<AtkUnitBase*, short, short, byte, void> _itemDetailSetPositionPreservingOriginal;
    private bool _requestedThisFrame;
    private bool _tooltipVisible;
    private ushort _tooltipParentAddonId;
    private uint _tooltipItemId;
    private Vector2 _tooltipAnchorMin = new(float.NaN);
    private Vector2 _tooltipAnchorMax = new(float.NaN);
    private Vector2 _tooltipNativeAnchorMin = new(float.NaN);
    private Vector2 _tooltipNativeAnchorMax = new(float.NaN);
    private bool _tooltipExpandRight;
    private DateTime _lastHostFailureLog = DateTime.MinValue;
    private DateTime _lastItemDetailRepositionFailureLog = DateTime.MinValue;

    public NativeItemTooltipBridge()
    {
        TryInitializeItemDetailSetPositionFunction();
        Dalamud.AddonLifecycle.RegisterListener(AddonEvent.PreDraw, ItemDetailAddonName, HandleItemDetailLifecycle);
        Dalamud.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, ItemDetailAddonName, HandleItemDetailLifecycle);
        Dalamud.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, ItemDetailAddonName, HandleItemDetailLifecycle);
    }

    public void BeginImGuiFrame()
    {
        _requestedThisFrame = false;
        if (!TryCheckNativeTooltipReadiness(out _))
        {
            HideTooltip(false);
        }
    }

    public void EndImGuiFrame()
    {
        if (_requestedThisFrame)
            return;

        if (!TryCheckNativeTooltipReadiness(out _))
        {
            HideTooltip(false);
            return;
        }

        HideTooltip();
    }

    public void RequestItemTooltip(uint itemId, Vector2 rectMin, Vector2 rectMax, bool expandRight)
    {
        if (!TryCheckNativeTooltipReadiness(out _))
        {
            HideTooltip(false);
            return;
        }
        _requestedThisFrame = true;
        if (itemId == 0)
        {
            HideTooltip();
            return;
        }

        if (!TryPrepareAnchor(rectMin, rectMax, out var parentAddonId))
            return;

        var anchorChanged = !RectsEqual(_tooltipAnchorMin, rectMin) || !RectsEqual(_tooltipAnchorMax, rectMax);
        if (_tooltipVisible && _tooltipItemId == itemId && _tooltipParentAddonId == parentAddonId && !anchorChanged && _tooltipExpandRight == expandRight)
            return;

        if (_tooltipVisible)
            HideTooltip();

        var stage = AtkStage.Instance();
        if (stage == null)
        {
            GatherBuddy.Log.Debug("[NativeItemTooltipBridge] Unable to show tooltip: AtkStage unavailable.");
            return;
        }

        var tooltipArgs = default(AtkTooltipManager.AtkTooltipArgs);
        tooltipArgs.ItemArgs.Kind = DetailKind.Item;
        tooltipArgs.ItemArgs.ItemId = unchecked((int)itemId);
        tooltipArgs.ItemArgs.BuyQuantity = -1;
        tooltipArgs.ItemArgs.Flag1 = 0;

        try
        {
            _tooltipExpandRight = expandRight;
            SetAnchorVisibility(true);
            stage->TooltipManager.ShowTooltip(AtkTooltipType.Item, parentAddonId, _anchorNode, &tooltipArgs, &TooltipPositionCallback);
            _tooltipVisible = true;
            _tooltipParentAddonId = parentAddonId;
            _tooltipItemId = itemId;
            _tooltipAnchorMin = rectMin;
            _tooltipAnchorMax = rectMax;
        }
        catch (Exception ex)
        {
            SetAnchorVisibility(false);
            GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to show item tooltip for {itemId}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Dalamud.AddonLifecycle.UnregisterListener(AddonEvent.PreDraw, ItemDetailAddonName, HandleItemDetailLifecycle);
        Dalamud.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, ItemDetailAddonName, HandleItemDetailLifecycle);
        Dalamud.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, ItemDetailAddonName, HandleItemDetailLifecycle);
        HideTooltip();
        DetachAnchorNode();
        if (_anchorNode != null)
        {
            try
            {
                _anchorNode->Destroy(true);
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to destroy tooltip anchor node: {ex.Message}");
            }
            finally
            {
                _anchorNode = null;
            }
        }
    }

    private void HandleItemDetailLifecycle(AddonEvent type, AddonArgs args)
    {
        var itemDetailAddon = (AtkUnitBase*)args.Addon.Address;
        if (itemDetailAddon == null)
            return;

        TryRepositionItemDetailAddon(itemDetailAddon);
    }

    private void TryInitializeItemDetailSetPositionFunction()
    {
        try
        {
            var address = Dalamud.SigScanner.ScanText(ItemDetailSetPositionPreservingOriginalSignature);
            _itemDetailSetPositionPreservingOriginal = (delegate* unmanaged[Thiscall]<AtkUnitBase*, short, short, byte, void>)address;
        }
        catch (Exception ex)
        {
            _itemDetailSetPositionPreservingOriginal = null;
            GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to resolve ItemDetail reposition function: {ex.Message}");
        }
    }

    private void TryRepositionItemDetailAddon(AtkUnitBase* itemDetailAddon)
    {
        if (!_tooltipVisible || _tooltipItemId == 0 || itemDetailAddon->RootNode == null)
            return;

        if (float.IsNaN(_tooltipNativeAnchorMin.X) || float.IsNaN(_tooltipNativeAnchorMin.Y) || float.IsNaN(_tooltipNativeAnchorMax.X) || float.IsNaN(_tooltipNativeAnchorMax.Y))
            return;

        if (!TryGetItemDetailTargetPosition(itemDetailAddon, out var targetX, out var targetY))
            return;

        if (itemDetailAddon->X == targetX && itemDetailAddon->Y == targetY)
            return;

        try
        {
            if (_itemDetailSetPositionPreservingOriginal != null)
                _itemDetailSetPositionPreservingOriginal(itemDetailAddon, targetX, targetY, 1);
            else
                itemDetailAddon->SetPosition(targetX, targetY);
        }
        catch (Exception ex)
        {
            MaybeLogItemDetailRepositionFailure($"Failed to reposition ItemDetail addon: {ex.Message}");
        }
    }

    private bool TryGetItemDetailTargetPosition(AtkUnitBase* itemDetailAddon, out short targetX, out short targetY)
    {
        targetX = 0;
        targetY = 0;

        var tooltipWidth = itemDetailAddon->GetScaledWidth(true);
        var tooltipHeight = itemDetailAddon->GetScaledHeight(true);
        if (tooltipWidth <= 0f || tooltipHeight <= 0f)
        {
            MaybeLogItemDetailRepositionFailure("ItemDetail addon size is unavailable for native tooltip repositioning.");
            return false;
        }

        var nativeDisplaySize = ToNativeUi(ImGui.GetIO().DisplaySize);
        if (nativeDisplaySize.X <= 0f || nativeDisplaySize.Y <= 0f)
        {
            MaybeLogItemDetailRepositionFailure("Native display size is unavailable for ItemDetail repositioning.");
            return false;
        }

        var nativePadding = ToNativeUi(new Vector2(4f, 4f));
        if (nativePadding.X <= 0f)
            nativePadding.X = 1f;
        if (nativePadding.Y <= 0f)
            nativePadding.Y = 1f;

        var desiredX = _tooltipExpandRight
            ? _tooltipNativeAnchorMin.X
            : _tooltipNativeAnchorMax.X - tooltipWidth;
        var desiredY = _tooltipNativeAnchorMin.Y;

        var maxX = MathF.Max(nativePadding.X, nativeDisplaySize.X - tooltipWidth - nativePadding.X);
        var maxY = MathF.Max(nativePadding.Y, nativeDisplaySize.Y - tooltipHeight - nativePadding.Y);
        desiredX = Math.Clamp(desiredX, nativePadding.X, maxX);
        desiredY = Math.Clamp(desiredY, nativePadding.Y, maxY);

        targetX = (short)Math.Clamp((int)MathF.Round(desiredX), short.MinValue, short.MaxValue);
        targetY = (short)Math.Clamp((int)MathF.Round(desiredY), short.MinValue, short.MaxValue);
        return true;
    }

    private void MaybeLogItemDetailRepositionFailure(string message)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastItemDetailRepositionFailureLog).TotalSeconds < 5)
            return;

        _lastItemDetailRepositionFailureLog = now;
        GatherBuddy.Log.Debug($"[NativeItemTooltipBridge] {message}");
    }

    private static bool TryCheckNativeTooltipReadiness(out string reason)
    {
        if (!Dalamud.ClientState.IsLoggedIn)
        {
            reason = "client is not logged in";
            return false;
        }

        if (Dalamud.Objects.LocalPlayer == null)
        {
            reason = "local player is unavailable";
            return false;
        }

        if (Functions.BetweenAreas())
        {
            reason = "player is transitioning between areas";
            return false;
        }

        if (!GenericHelpers.IsScreenReady())
        {
            reason = "screen is not ready";
            return false;
        }

        reason = string.Empty;
        return true;
    }


    private bool TryPrepareAnchor(Vector2 rectMin, Vector2 rectMax, out ushort parentAddonId)
    {
        parentAddonId = 0;
        if (!TryCheckNativeTooltipReadiness(out _))
            return false;
        if (!EnsureAnchorNode())
            return false;

        if (!TryGetHostAddon(out var hostAddon))
        {
            MaybeLogHostFailure();
            return false;
        }

        if (!EnsureAnchorAttached(hostAddon))
            return false;

        UpdateAnchorBounds(hostAddon, rectMin, rectMax);
        parentAddonId = hostAddon->Id;
        return parentAddonId != 0;
    }

    private bool EnsureAnchorNode()
    {
        if (_anchorNode != null)
            return true;

        try
        {
            _anchorNode = AtkUldManager.CreateAtkResNode();
            if (_anchorNode == null)
            {
                GatherBuddy.Log.Warning("[NativeItemTooltipBridge] Failed to allocate tooltip anchor node.");
                return false;
            }

            _anchorNode->NodeFlags = NodeFlags.Enabled;
            _anchorNode->Width = 1;
            _anchorNode->Height = 1;
            _anchorNode->ScaleX = 1f;
            _anchorNode->ScaleY = 1f;
            _anchorNode->ToggleVisibility(false);
            return true;
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to create tooltip anchor node: {ex.Message}");
            _anchorNode = null;
            return false;
        }
    }

    private static bool TryGetHostAddon(out AtkUnitBase* hostAddon)
        => GenericHelpers.TryGetAddonByName(HostAddonName, out hostAddon) && IsUsableHost(hostAddon);

    private static bool IsUsableHost(AtkUnitBase* hostAddon)
        => hostAddon != null
         && hostAddon->Id != 0
         && hostAddon->RootNode != null
         && hostAddon->UldManager.Objects != null
         && hostAddon->UldManager.Objects->NodeList != null
         && hostAddon->UldManager.Objects->NodeCount > 0;

    private bool EnsureAnchorAttached(AtkUnitBase* hostAddon)
    {
        var hostAddress = (nint)hostAddon;
        if (_anchorNode->ParentNode == hostAddon->RootNode && _attachedHostAddonAddress == hostAddress)
            return true;

        if (_attachedHostAddonAddress != 0 && _attachedHostAddonAddress != hostAddress)
        {
            ResetAnchorLinks();
            _attachedHostAddonAddress = 0;
        }
        else if (_anchorNode->ParentNode != null)
        {
            DetachAnchorNode();
        }

        try
        {
            _anchorNode->NodeId = GetNextNodeId(hostAddon);
            AttachAsLastChild(_anchorNode, hostAddon->RootNode);
            AddNodeToObjectList(hostAddon, _anchorNode);
            hostAddon->UldManager.UpdateDrawNodeList();
            _attachedHostAddonAddress = hostAddress;
            return true;
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to attach tooltip anchor node: {ex.Message}");
            return false;
        }
    }

    private void DetachAnchorNode()
    {
        if (_anchorNode == null || _anchorNode->ParentNode == null || _attachedHostAddonAddress == 0)
            return;

        if (!TryGetHostAddon(out var hostAddon) || (nint)hostAddon != _attachedHostAddonAddress)
        {
            ResetAnchorLinks();
            _attachedHostAddonAddress = 0;
            return;
        }

        try
        {
            DetachFromParent(_anchorNode);
            RemoveNodeFromObjectList(hostAddon, _anchorNode);
            hostAddon->UldManager.UpdateDrawNodeList();
        }
        catch (Exception ex)
        {
            GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to detach tooltip anchor node: {ex.Message}");
        }
        finally
        {
            _attachedHostAddonAddress = 0;
        }
    }

    private void UpdateAnchorBounds(AtkUnitBase* hostAddon, Vector2 rectMin, Vector2 rectMax)
    {
        var hostRoot = hostAddon->RootNode;
        var nativeMin = ToNativeUi(rectMin);
        var nativeMax = ToNativeUi(rectMax);
        var size = nativeMax - nativeMin;
        if (size.X < 1f)
            size.X = 1f;
        if (size.Y < 1f)
            size.Y = 1f;
        _tooltipNativeAnchorMin = nativeMin;
        _tooltipNativeAnchorMax = nativeMin + size;

        var relativeX = nativeMin.X - hostRoot->ScreenX;
        var relativeY = nativeMin.Y - hostRoot->ScreenY;
        _anchorNode->SetPositionFloat(relativeX, relativeY);
        _anchorNode->SetWidth((ushort)Math.Clamp((int)MathF.Ceiling(size.X), 1, ushort.MaxValue));
        _anchorNode->SetHeight((ushort)Math.Clamp((int)MathF.Ceiling(size.Y), 1, ushort.MaxValue));
        _anchorNode->IsDirty = true;
    }

    private void HideTooltip(bool updateHost = true)
    {
        if (!_tooltipVisible && (_anchorNode == null || !_anchorNode->IsVisible()))
            return;

        var stage = AtkStage.Instance();
        if (stage != null && _tooltipVisible)
            try
            {
                stage->TooltipManager.HideTooltip(_tooltipParentAddonId);
            }
            catch (Exception ex)
            {
                GatherBuddy.Log.Warning($"[NativeItemTooltipBridge] Failed to hide tooltip: {ex.Message}");
            }
        SetAnchorVisibility(false, updateHost);
        _tooltipVisible = false;
        _tooltipParentAddonId = 0;
        _tooltipItemId = 0;
        _tooltipAnchorMin = new Vector2(float.NaN);
        _tooltipAnchorMax = new Vector2(float.NaN);
        _tooltipNativeAnchorMin = new Vector2(float.NaN);
        _tooltipNativeAnchorMax = new Vector2(float.NaN);
        _tooltipExpandRight = false;
    }

    private void MaybeLogHostFailure()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastHostFailureLog).TotalSeconds < 5)
            return;

        _lastHostFailureLog = now;
        GatherBuddy.Log.Debug($"[NativeItemTooltipBridge] Host addon {HostAddonName} is unavailable; skipping native item tooltip.");
    }

    private static uint GetNextNodeId(AtkUnitBase* hostAddon)
    {
        uint maxNodeId = 1;
        foreach (var node in hostAddon->UldManager.Nodes)
        {
            if (node.Value == null)
                continue;

            if (node.Value->NodeId > maxNodeId)
                maxNodeId = node.Value->NodeId;
        }

        return maxNodeId + 1;
    }

    private static void AddNodeToObjectList(AtkUnitBase* hostAddon, AtkResNode* node)
    {
        var objects = hostAddon->UldManager.Objects;
        if (objects == null || ContainsNode(objects, node))
            return;

        var oldCount = Math.Max(objects->NodeCount, 0);
        var newList = (AtkResNode**)IMemorySpace.GetUISpace()->Malloc((ulong)((oldCount + 1) * IntPtr.Size), 8);
        if (newList == null)
            throw new InvalidOperationException("Unable to allocate tooltip anchor object list.");

        for (var i = 0; i < oldCount; ++i)
            newList[i] = objects->NodeList[i];

        newList[oldCount] = node;
        if (objects->NodeList != null && oldCount > 0)
            IMemorySpace.Free((void*)objects->NodeList, (ulong)(oldCount * IntPtr.Size));

        objects->NodeList = newList;
        objects->NodeCount = oldCount + 1;
    }

    private static void RemoveNodeFromObjectList(AtkUnitBase* hostAddon, AtkResNode* node)
    {
        var objects = hostAddon->UldManager.Objects;
        if (objects == null || objects->NodeList == null || objects->NodeCount <= 0 || !ContainsNode(objects, node))
            return;

        var oldCount = objects->NodeCount;
        var newCount = oldCount - 1;
        AtkResNode** newList = null;
        if (newCount > 0)
        {
            newList = (AtkResNode**)IMemorySpace.GetUISpace()->Malloc((ulong)(newCount * IntPtr.Size), 8);
            if (newList == null)
                throw new InvalidOperationException("Unable to shrink tooltip anchor object list.");
        }

        var destIndex = 0;
        for (var i = 0; i < oldCount; ++i)
        {
            if (objects->NodeList[i] == node)
                continue;

            if (newList != null)
                newList[destIndex++] = objects->NodeList[i];
        }

        IMemorySpace.Free((void*)objects->NodeList, (ulong)(oldCount * IntPtr.Size));
        objects->NodeList = newList;
        objects->NodeCount = newCount;
    }

    private static bool ContainsNode(AtkUldObjectInfo* objects, AtkResNode* node)
    {
        if (objects == null || objects->NodeList == null || objects->NodeCount <= 0)
            return false;

        for (var i = 0; i < objects->NodeCount; ++i)
        {
            if (objects->NodeList[i] == node)
                return true;
        }

        return false;
    }

    private void SetAnchorVisibility(bool visible, bool updateHost = true)
    {
        if (_anchorNode == null || _anchorNode->IsVisible() == visible)
            return;

        _anchorNode->ToggleVisibility(visible);
        _anchorNode->IsDirty = true;
        if (updateHost && TryGetHostAddon(out var hostAddon) && (nint)hostAddon == _attachedHostAddonAddress)
            hostAddon->UldManager.UpdateDrawNodeList();
    }

    private void ResetAnchorLinks()
    {
        if (_anchorNode == null)
            return;

        _anchorNode->ParentNode = null;
        _anchorNode->PrevSiblingNode = null;
        _anchorNode->NextSiblingNode = null;
    }

    private static Vector2 ToNativeUi(Vector2 position)
    {
        var scale = AtkUnitBase.GetGlobalUIScale();
        if (scale <= 0f)
            scale = 1f;

        return position / scale;
    }

    private static bool RectsEqual(Vector2 left, Vector2 right)
        => MathF.Abs(left.X - right.X) <= AnchorEpsilon
        && MathF.Abs(left.Y - right.Y) <= AnchorEpsilon;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void TooltipPositionCallback(float* screenX, float* screenY, AtkResNode* node)
    {
        if (screenX == null || screenY == null || node == null)
            return;

        var bridge = GatherBuddy.NativeItemTooltipBridge;
        if (bridge == null || bridge._anchorNode == null || node != bridge._anchorNode)
        {
            *screenX = node->ScreenX;
            *screenY = node->ScreenY;
            return;
        }

        if (float.IsNaN(bridge._tooltipNativeAnchorMin.X) || float.IsNaN(bridge._tooltipNativeAnchorMin.Y) || float.IsNaN(bridge._tooltipNativeAnchorMax.X) || float.IsNaN(bridge._tooltipNativeAnchorMax.Y))
        {
            *screenX = node->ScreenX;
            *screenY = node->ScreenY;
            return;
        }

        *screenX = bridge._tooltipExpandRight ? bridge._tooltipNativeAnchorMin.X : bridge._tooltipNativeAnchorMax.X;
        *screenY = bridge._tooltipNativeAnchorMin.Y;
    }

    private static void AttachAsLastChild(AtkResNode* node, AtkResNode* parent)
    {
        node->ParentNode = parent;
        node->PrevSiblingNode = null;
        node->NextSiblingNode = null;

        if (parent->ChildNode == null)
        {
            parent->ChildNode = node;
            parent->ChildCount++;
            return;
        }

        var current = parent->ChildNode;
        while (current->PrevSiblingNode != null)
            current = current->PrevSiblingNode;

        current->PrevSiblingNode = node;
        node->NextSiblingNode = current;
        parent->ChildCount++;
    }

    private static void DetachFromParent(AtkResNode* node)
    {
        var parent = node->ParentNode;
        if (parent == null)
            return;

        if (parent->ChildNode == node)
            parent->ChildNode = node->PrevSiblingNode != null ? node->PrevSiblingNode : node->NextSiblingNode;

        if (node->PrevSiblingNode != null)
            node->PrevSiblingNode->NextSiblingNode = node->NextSiblingNode;

        if (node->NextSiblingNode != null)
            node->NextSiblingNode->PrevSiblingNode = node->PrevSiblingNode;

        if (parent->ChildCount > 0)
            parent->ChildCount--;

        node->ParentNode = null;
        node->PrevSiblingNode = null;
        node->NextSiblingNode = null;
    }
}
