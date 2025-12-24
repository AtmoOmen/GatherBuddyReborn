using System;

namespace GatherBuddy.SeFunctions;

public delegate void UpdateCatchDelegate(IntPtr module, uint fishId, bool large, ushort size, byte amount, byte level, byte unk7, byte unk8, byte unk9, byte unk10,
    byte unk11, byte unk12);

public sealed class UpdateFishCatch : SeFunctionBase<UpdateCatchDelegate>
{
    public UpdateFishCatch(ISigScannerWrapper sigScanner)
        : base(sigScanner, "E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B 74 24 ?? 48 83 C4 ?? 5F C3 CC CC CC CC CC CC CC CC CC CC CC CC 48 83 EC ?? 48 81 C1")
    {}
}
