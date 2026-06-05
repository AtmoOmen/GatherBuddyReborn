namespace GatherBuddy.Enums;

public enum SpearfishSpeed : ushort
{
    Unknown       = 0,
    SuperSlow     = 100,
    ExtremelySlow = 150,
    VerySlow      = 200,
    Slow          = 250,
    Average       = 300,
    Fast          = 350,
    VeryFast      = 400,
    ExtremelyFast = 450,
    SuperFast     = 500,
    HyperFast     = 550,
    LynFast       = 600,

    None = ushort.MaxValue,
}

public static class SpearFishSpeedExtensions
{
    public static string ToName(this SpearfishSpeed speed)
        => speed switch
        {
            SpearfishSpeed.Unknown       => "未知速度",
            SpearfishSpeed.SuperSlow     => "极度缓慢",
            SpearfishSpeed.ExtremelySlow => "非常缓慢",
            SpearfishSpeed.VerySlow      => "很慢",
            SpearfishSpeed.Slow          => "缓慢",
            SpearfishSpeed.Average       => "普通",
            SpearfishSpeed.Fast          => "快速",
            SpearfishSpeed.VeryFast      => "很快",
            SpearfishSpeed.ExtremelyFast => "非常快速",
            SpearfishSpeed.SuperFast     => "极度快速",
            SpearfishSpeed.HyperFast     => "超高速",
            SpearfishSpeed.LynFast       => "极限高速",
            SpearfishSpeed.None          => "无速度",
            _                            => $"{(ushort)speed}",
        };
}