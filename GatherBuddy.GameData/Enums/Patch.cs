using System;

namespace GatherBuddy.Enums;

public enum Patch : ushort
{
    Unknown                    = 0,
    ARealmReborn               = 200,
    ARealmAwoken               = 210,
    ThroughTheMaelstrom        = 220,
    DefendersOfEorzea          = 230,
    DreamsOfIce                = 240,
    BeforeTheFall              = 250,
    Heavensward                = 300,
    AsGoesLightSoGoesDarkness  = 310,
    TheGearsOfChange           = 320,
    RevengeOfTheHorde          = 330,
    SoulSurrender              = 340,
    TheFarEdgeOfFate           = 350,
    Stormblood                 = 400,
    TheLegendReturns           = 410,
    RiseOfANewSun              = 420,
    UnderTheMoonlight          = 430,
    PreludeInViolet            = 440,
    ARequiemForHeroes          = 450,
    Shadowbringers             = 500,
    VowsOfVirtueDeedsOfCruelty = 510,
    EchoesOfAFallenStar        = 520,
    ReflectionsInCrystal       = 530,
    FuturesRewritten           = 540,
    DeathUntoDawn              = 550,
    Endwalker                  = 600,
    NewfoundAdventure          = 610,
    BuriedMemory               = 620,
    GodsRevelLandsTremble      = 630,
    TheDarkThrone              = 640,
    GrowingLight               = 650,
    Dawntrail                  = 700,
    Crossroads                 = 710,
    SeekersOfEternity          = 720,
    ThePromiseOfTomorrow       = 730,
    IntoTheMist                = 740,
    TrailToTheHeavens          = 750,
}

[Flags]
public enum PatchFlag : ulong
{
    ARealmReborn               = 1ul << 0,
    ARealmAwoken               = 1ul << 1,
    ThroughTheMaelstrom        = 1ul << 2,
    DefendersOfEorzea          = 1ul << 3,
    DreamsOfIce                = 1ul << 4,
    BeforeTheFall              = 1ul << 5,
    Heavensward                = 1ul << 6,
    AsGoesLightSoGoesDarkness  = 1ul << 7,
    TheGearsOfChange           = 1ul << 8,
    RevengeOfTheHorde          = 1ul << 9,
    SoulSurrender              = 1ul << 10,
    TheFarEdgeOfFate           = 1ul << 11,
    Stormblood                 = 1ul << 12,
    TheLegendReturns           = 1ul << 13,
    RiseOfANewSun              = 1ul << 14,
    UnderTheMoonlight          = 1ul << 15,
    PreludeInViolet            = 1ul << 16,
    ARequiemForHeroes          = 1ul << 17,
    Shadowbringers             = 1ul << 18,
    VowsOfVirtueDeedsOfCruelty = 1ul << 19,
    EchoesOfAFallenStar        = 1ul << 20,
    ReflectionsInCrystal       = 1ul << 21,
    FuturesRewritten           = 1ul << 22,
    DeathUntoDawn              = 1ul << 23,
    Endwalker                  = 1ul << 24,
    NewfoundAdventure          = 1ul << 25,
    BuriedMemory               = 1ul << 26,
    GodsRevelLandsTremble      = 1ul << 27,
    TheDarkThrone              = 1ul << 28,
    GrowingLight               = 1ul << 29,
    Dawntrail                  = 1ul << 30,
    Crossroads                 = 1ul << 31,
    SeekersOfEternity          = 1ul << 32,
    ThePromiseOfTomorrow       = 1ul << 33,
    IntoTheMist                = 1ul << 34,
    TrailToTheHeavens          = 1ul << 35,
}

public static class PatchExtensions
{
    public static byte ToMajor(this Patch value)
        => (byte)((ushort)value / 100);

    public static byte ToMinor(this Patch value)
    {
        var val = (ushort)value % 100;
        if (val % 10 is 0)
            return (byte)(val / 10);

        return (byte)val;
    }

    public static Patch ToExpansion(this Patch value)
        => (Patch)(value.ToMajor() * 100);

    public static string ToVersionString(this Patch value)
        => value == Patch.Unknown ? "???" : $"{value.ToMajor()}.{value.ToMinor()}";

    public static string ToPatchName(this Patch value)
    {
        return ((ushort)value / 10) switch
        {
            20 => "重生之境",
            21 => "觉醒之境",
            22 => "混沌的漩涡",
            23 => "艾欧泽亚的守护者",
            24 => "寒冰的幻想",
            25 => "希望的灯火",
            30 => "苍穹之禁城",
            31 => "光与暗的境界",
            32 => "命运的齿轮",
            33 => "绝命怒嚎",
            34 => "灵魂继承者",
            35 => "命运的止境",
            40 => "红莲之狂潮",
            41 => "英雄归来",
            42 => "曙光微明",
            43 => "月下芳华",
            44 => "狂乱的前奏",
            45 => "英雄挽歌",
            50 => "暗影之逆焰",
            51 => "白之誓约、黑之密约",
            52 => "追忆的凶星",
            53 => "水晶的残光",
            54 => "另一个未来",
            55 => "死斗至黎明",
            60 => "晓月之终途",
            61 => "崭新的冒险",
            62 => "禁断的记忆",
            63 => "天之祝祭、地之鸣动",
            64 => "玉座的罪人",
            65 => "光明的起点",
            70 => "金曦之遗辉",
            71 => "未知的邂逅",
            72 => "永恒探索者",
            73 => "明日的路标",
            74 => "迷雾之境",
            75 => "天道征途",
            _  => "未知",
        };
    }

    public static PatchFlag ToPatchFlag(this Patch value)
    {
        return value switch
        {
            Patch.ARealmReborn               => PatchFlag.ARealmReborn,
            Patch.ARealmAwoken               => PatchFlag.ARealmAwoken,
            Patch.ThroughTheMaelstrom        => PatchFlag.ThroughTheMaelstrom,
            Patch.DefendersOfEorzea          => PatchFlag.DefendersOfEorzea,
            Patch.DreamsOfIce                => PatchFlag.DreamsOfIce,
            Patch.BeforeTheFall              => PatchFlag.BeforeTheFall,
            Patch.Heavensward                => PatchFlag.Heavensward,
            Patch.AsGoesLightSoGoesDarkness  => PatchFlag.AsGoesLightSoGoesDarkness,
            Patch.TheGearsOfChange           => PatchFlag.TheGearsOfChange,
            Patch.RevengeOfTheHorde          => PatchFlag.RevengeOfTheHorde,
            Patch.SoulSurrender              => PatchFlag.SoulSurrender,
            Patch.TheFarEdgeOfFate           => PatchFlag.TheFarEdgeOfFate,
            Patch.Stormblood                 => PatchFlag.Stormblood,
            Patch.TheLegendReturns           => PatchFlag.TheLegendReturns,
            Patch.RiseOfANewSun              => PatchFlag.RiseOfANewSun,
            Patch.UnderTheMoonlight          => PatchFlag.UnderTheMoonlight,
            Patch.PreludeInViolet            => PatchFlag.PreludeInViolet,
            Patch.ARequiemForHeroes          => PatchFlag.ARequiemForHeroes,
            Patch.Shadowbringers             => PatchFlag.Shadowbringers,
            Patch.VowsOfVirtueDeedsOfCruelty => PatchFlag.VowsOfVirtueDeedsOfCruelty,
            Patch.EchoesOfAFallenStar        => PatchFlag.EchoesOfAFallenStar,
            Patch.ReflectionsInCrystal       => PatchFlag.ReflectionsInCrystal,
            Patch.FuturesRewritten           => PatchFlag.FuturesRewritten,
            Patch.DeathUntoDawn              => PatchFlag.DeathUntoDawn,
            Patch.Endwalker                  => PatchFlag.Endwalker,
            Patch.NewfoundAdventure          => PatchFlag.NewfoundAdventure,
            Patch.BuriedMemory               => PatchFlag.BuriedMemory,
            Patch.GodsRevelLandsTremble      => PatchFlag.GodsRevelLandsTremble,
            Patch.TheDarkThrone              => PatchFlag.TheDarkThrone,
            Patch.GrowingLight               => PatchFlag.GrowingLight,
            Patch.Dawntrail                  => PatchFlag.Dawntrail,
            Patch.Crossroads                 => PatchFlag.Crossroads,
            Patch.SeekersOfEternity          => PatchFlag.SeekersOfEternity,
            Patch.ThePromiseOfTomorrow       => PatchFlag.ThePromiseOfTomorrow,
            Patch.IntoTheMist                => PatchFlag.IntoTheMist,
            Patch.TrailToTheHeavens          => PatchFlag.TrailToTheHeavens,
            _                                => 0,
        };
    }

    public static Patch ToPatch(this PatchFlag value)
    {
        return value switch
        {
            PatchFlag.ARealmReborn               => Patch.ARealmReborn,
            PatchFlag.ARealmAwoken               => Patch.ARealmAwoken,
            PatchFlag.ThroughTheMaelstrom        => Patch.ThroughTheMaelstrom,
            PatchFlag.DefendersOfEorzea          => Patch.DefendersOfEorzea,
            PatchFlag.DreamsOfIce                => Patch.DreamsOfIce,
            PatchFlag.BeforeTheFall              => Patch.BeforeTheFall,
            PatchFlag.Heavensward                => Patch.Heavensward,
            PatchFlag.AsGoesLightSoGoesDarkness  => Patch.AsGoesLightSoGoesDarkness,
            PatchFlag.TheGearsOfChange           => Patch.TheGearsOfChange,
            PatchFlag.RevengeOfTheHorde          => Patch.RevengeOfTheHorde,
            PatchFlag.SoulSurrender              => Patch.SoulSurrender,
            PatchFlag.TheFarEdgeOfFate           => Patch.TheFarEdgeOfFate,
            PatchFlag.Stormblood                 => Patch.Stormblood,
            PatchFlag.TheLegendReturns           => Patch.TheLegendReturns,
            PatchFlag.RiseOfANewSun              => Patch.RiseOfANewSun,
            PatchFlag.UnderTheMoonlight          => Patch.UnderTheMoonlight,
            PatchFlag.PreludeInViolet            => Patch.PreludeInViolet,
            PatchFlag.ARequiemForHeroes          => Patch.ARequiemForHeroes,
            PatchFlag.Shadowbringers             => Patch.Shadowbringers,
            PatchFlag.VowsOfVirtueDeedsOfCruelty => Patch.VowsOfVirtueDeedsOfCruelty,
            PatchFlag.EchoesOfAFallenStar        => Patch.EchoesOfAFallenStar,
            PatchFlag.ReflectionsInCrystal       => Patch.ReflectionsInCrystal,
            PatchFlag.FuturesRewritten           => Patch.FuturesRewritten,
            PatchFlag.DeathUntoDawn              => Patch.DeathUntoDawn,
            PatchFlag.Endwalker                  => Patch.Endwalker,
            PatchFlag.NewfoundAdventure          => Patch.NewfoundAdventure,
            PatchFlag.BuriedMemory               => Patch.BuriedMemory,
            PatchFlag.GodsRevelLandsTremble      => Patch.GodsRevelLandsTremble,
            PatchFlag.TheDarkThrone              => Patch.TheDarkThrone,
            PatchFlag.GrowingLight               => Patch.GrowingLight,
            PatchFlag.Dawntrail                  => Patch.Dawntrail,
            PatchFlag.Crossroads                 => Patch.Crossroads,
            PatchFlag.SeekersOfEternity          => Patch.SeekersOfEternity,
            PatchFlag.ThePromiseOfTomorrow       => Patch.ThePromiseOfTomorrow,
            PatchFlag.IntoTheMist                => Patch.IntoTheMist,
            PatchFlag.TrailToTheHeavens          => Patch.TrailToTheHeavens,
            _                                    => Patch.Unknown,
        };
    }
}
