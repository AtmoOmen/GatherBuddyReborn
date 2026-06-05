using GatherBuddy.Enums;

namespace GatherBuddy.Classes;

public enum OceanArea : byte
{
    None,
    Unknown,
    Aldenard,
    Othard,
}

public class OceanRoute
{
    public string                                     Name       { get; internal init; } = string.Empty;
    public byte                                       Id         { get; internal init; }
    public OceanArea                                  Area       { get; internal init; }
    public OceanTime                                  StartTime  { get; internal init; }
    public (FishingSpot Normal, FishingSpot Spectral) SpotDay    { get; internal init; }
    public (FishingSpot Normal, FishingSpot Spectral) SpotSunset { get; internal init; }
    public (FishingSpot Normal, FishingSpot Spectral) SpotNight  { get; internal init; }

    public override string ToString()
        => $"{Name} ({StartTime})";

    public (FishingSpot Normal, FishingSpot Spectral) GetSpots(OceanTime time)
        => time switch
        {
            OceanTime.白昼    => SpotDay,
            OceanTime.日落 => SpotSunset,
            OceanTime.夜晚  => SpotNight,
            _                => throw new ArgumentOutOfRangeException(nameof(time)),
        };

    public (FishingSpot Normal, FishingSpot Spectral) GetSpots(int order)
        => (StartTime, order % 3) switch
        {
            (OceanTime.白昼, 0)    => SpotSunset,
            (OceanTime.白昼, 1)    => SpotNight,
            (OceanTime.白昼, 2)    => SpotDay,
            (OceanTime.日落, 0) => SpotNight,
            (OceanTime.日落, 1) => SpotDay,
            (OceanTime.日落, 2) => SpotSunset,
            (OceanTime.夜晚, 0)  => SpotDay,
            (OceanTime.夜晚, 1)  => SpotSunset,
            (OceanTime.夜晚, 2)  => SpotNight,
            _                     => throw new ArgumentOutOfRangeException(nameof(StartTime)),
        };

    public FishingSpot GetSpot(OceanTime time, bool spectral)
    {
        var (main, sub) = GetSpots(time);
        return spectral ? sub : main;
    }

    public FishingSpot GetSpot(int order, bool spectral)
    {
        var (main, sub) = GetSpots(order);
        return spectral ? sub : main;
    }
}
