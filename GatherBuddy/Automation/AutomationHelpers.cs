using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace GatherBuddy.Automation;

public static unsafe class Callback
{
    private static delegate* unmanaged<AtkUnitBase*, uint, AtkValue*, bool, bool> _fireCallbackPtr;

    private static void Initialize()
    {
        if (_fireCallbackPtr != null) return;

        _fireCallbackPtr = AtkUnitBase.MemberFunctionPointers.FireCallback;
        GatherBuddy.Log.Debug($"[Callback] Initialized using ClientStructs MemberFunctionPointers");
    }

    public static void Fire(AtkUnitBase* unitBase, bool updateState, params object[] values)
    {
        if (unitBase == null)
        {
            GatherBuddy.Log.Error("[Callback] Attempted to fire callback on null UnitBase");
            return;
        }

        if (_fireCallbackPtr == null)
            Initialize();

        var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
        if (atkValues == null)
        {
            GatherBuddy.Log.Error("[Callback] Failed to allocate memory for AtkValues");
            return;
        }

        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                switch (value)
                {
                    case uint uintValue:
                        atkValues[i].Type = ValueType.UInt;
                        atkValues[i].UInt = uintValue;
                        break;
                    case int intValue:
                        atkValues[i].Type = ValueType.Int;
                        atkValues[i].Int = intValue;
                        break;
                    case float floatValue:
                        atkValues[i].Type = ValueType.Float;
                        atkValues[i].Float = floatValue;
                        break;
                    case bool boolValue:
                        atkValues[i].Type = ValueType.Bool;
                        atkValues[i].Byte = (byte)(boolValue ? 1 : 0);
                        break;
                    case string stringValue:
                        {
                            atkValues[i].Type = ValueType.String;
                            var stringBytes = Encoding.UTF8.GetBytes(stringValue);
                            var stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                            Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                            Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                            atkValues[i].String = (byte*)stringAlloc;
                            break;
                        }
                    case AtkValue rawValue:
                        atkValues[i] = rawValue;
                        break;
                    default:
                        GatherBuddy.Log.Error($"[Callback] Unable to convert type {value?.GetType()} to AtkValue");
                        throw new ArgumentException($"Unable to convert type {value?.GetType()} to AtkValue");
                }
            }

            var addonName = unitBase->NameString;
            GatherBuddy.Log.Verbose($"[Callback] Firing on {addonName}, valueCount={values.Length}, updateState={updateState}");

            _fireCallbackPtr(unitBase, (uint)values.Length, atkValues, updateState);
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"[Callback] Exception during callback fire: {e.Message}");
        }
        finally
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (atkValues[i].Type == ValueType.String)
                {
                    Marshal.FreeHGlobal(new IntPtr(atkValues[i].String));
                }
            }
            Marshal.FreeHGlobal(new IntPtr(atkValues));
        }
    }
}

public static unsafe class Chat
{
    private const string SendChatSignature = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";
    private const string SanitizeStringSignature = "48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 70 4D 8B F8 4C 89 44 24 ?? 4C 8B 05 ?? ?? ?? ?? 44 8B E2";

    private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4);
    private delegate void SanitizeStringDelegate(Utf8String* stringPtr, int a2, nint a3);

    private static ProcessChatBoxDelegate? _processChatBox;
    private static SanitizeStringDelegate? _sanitizeString;

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct ChatPayload : IDisposable
    {
        [FieldOffset(0)] private readonly IntPtr textPtr;
        [FieldOffset(16)] private readonly ulong textLen;
        [FieldOffset(8)] private readonly ulong unk1;
        [FieldOffset(24)] private readonly ulong unk2;

        internal ChatPayload(byte[] stringBytes)
        {
            textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, textPtr, stringBytes.Length);
            Marshal.WriteByte(textPtr + stringBytes.Length, 0);
            textLen = (ulong)(stringBytes.Length + 1);
            unk1 = 64;
            unk2 = 0;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }

    private static void InitializeProcessChatBox()
    {
        if (_processChatBox != null) return;

        var addr = Dalamud.SigScanner.ScanText(SendChatSignature); // 撤回到之前的提交
        _processChatBox = Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(addr);
        GatherBuddy.Log.Debug($"[Chat] ProcessChatBox initialized at 0x{addr:X16}");
    }

    private static void InitializeSanitizeString()
    {
        if (_sanitizeString != null) return;

        var addr = Dalamud.SigScanner.ScanText(SanitizeStringSignature); // 撤回到之前的提交
        _sanitizeString = Marshal.GetDelegateForFunctionPointer<SanitizeStringDelegate>(addr);
        GatherBuddy.Log.Debug($"[Chat] SanitizeString initialized at 0x{addr:X16}");
    }

    private static string SanitizeText(string text)
    {
        InitializeSanitizeString();

        var uText = Utf8String.FromString(text);
        _sanitizeString!(uText, 0x27F, IntPtr.Zero);
        var sanitized = uText->ToString();
        uText->Dtor();
        IMemorySpace.Free(uText);
        return sanitized;
    }

    private static void SendMessageUnsafe(byte[] message)
    {
        InitializeProcessChatBox();

        var uiModule = (IntPtr)Framework.Instance()->GetUIModule();
        using var payload = new ChatPayload(message);
        var mem1 = Marshal.AllocHGlobal(400);
        Marshal.StructureToPtr(payload, mem1, false);
        _processChatBox!(uiModule, mem1, IntPtr.Zero, 0);
        Marshal.FreeHGlobal(mem1);
    }

    public static void SendMessage(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);

        if (bytes.Length == 0)
        {
            GatherBuddy.Log.Error("[Chat] Attempted to send empty message");
            throw new ArgumentException("message is empty", nameof(message));
        }

        if (bytes.Length > 500)
        {
            GatherBuddy.Log.Error($"[Chat] Attempted to send message longer than 500 bytes: {bytes.Length}");
            throw new ArgumentException("message is longer than 500 bytes", nameof(message));
        }

        if (message.Length != SanitizeText(message).Length)
        {
            GatherBuddy.Log.Error("[Chat] Message contained invalid characters");
            throw new ArgumentException("message contained invalid characters", nameof(message));
        }

        if (message.Contains('\n'))
        {
            GatherBuddy.Log.Error("[Chat] Message cannot contain newlines");
            throw new ArgumentException("message can't contain multiple lines", nameof(message));
        }

        if (message.Contains('\r'))
        {
            GatherBuddy.Log.Error("[Chat] Message cannot contain carriage return");
            throw new ArgumentException("message can't contain carriage return", nameof(message));
        }

        SendMessageUnsafe(bytes);
    }

    public static void ExecuteCommand(string message)
    {
        if (!message.StartsWith("/"))
        {
            GatherBuddy.Log.Error($"[Chat] Attempted to execute command without slash prefix: {message}");
            throw new InvalidOperationException($"Attempted to execute command but was not prefixed with a slash: {message}");
        }

        SendMessage(message);
    }

    [Obsolete("Use Chat.ExecuteCommand directly")]
    public static class Instance
    {
        public static void ExecuteCommand(string message) => Chat.ExecuteCommand(message);
    }
}
