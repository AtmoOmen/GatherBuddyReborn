namespace GatherBuddy.AutoHookIntegration.Models;

public enum AHBiteType : byte
{
    Unknown = 0,
    Weak = 36,
    Strong = 37,
    Legendary = 38,
    None = 255,
}

public enum AHHookType : uint
{
    None = 0,
    Normal = 296,
    Precision = 4179,
    Powerful = 4103,
    Double = 269,
    Triple = 27523,
    Stellar = 41287,
    Unknown = 255,
}
