namespace GatherBuddy.Enums;

// Ordered by the progression of time throughout a fishing route.
[Flags]
public enum OceanTime : byte
{
    永不  = 0,
    日落 = 0x01,
    夜晚  = 0x02,
    白昼    = 0x04,

    总是 = 日落 | 夜晚 | 白昼,
}

public static class OceanTimeExtensions
{
    public static OceanTime Next(this OceanTime time)
        => time switch
        {
            OceanTime.日落 => OceanTime.夜晚,
            OceanTime.夜晚  => OceanTime.白昼,
            OceanTime.白昼    => OceanTime.日落,
            _                => OceanTime.日落,
        };

    public static OceanTime Previous(this OceanTime time)
        => time switch
        {
            OceanTime.日落 => OceanTime.白昼,
            OceanTime.夜晚  => OceanTime.日落,
            OceanTime.白昼    => OceanTime.夜晚,
            _                => OceanTime.日落,
        };

    public static IEnumerable<OceanTime> Enumerate(this OceanTime time)
    {
        if (time.HasFlag(OceanTime.日落))
            yield return OceanTime.日落;
        if (time.HasFlag(OceanTime.夜晚))
            yield return OceanTime.夜晚;
        if (time.HasFlag(OceanTime.白昼))
            yield return OceanTime.白昼;
    }
}
