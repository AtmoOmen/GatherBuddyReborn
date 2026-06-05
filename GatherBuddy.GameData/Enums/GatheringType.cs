namespace GatherBuddy.Enums;

public enum GatheringType : byte
{
    采掘       = 0,
    碎石    = 1,
    采伐      = 2,
    割草   = 3,
    刺鱼 = 4,
    园艺工     = 5,
    采矿工        = 6,
    捕鱼人       = 7,
    多职业     = 8,
    未知      = byte.MaxValue,
};

public static class GatheringTypeExtension
{
    public static GatheringType ToGroup(this GatheringType type)
    {
        return type switch
        {
            GatheringType.采掘       => GatheringType.采矿工,
            GatheringType.碎石    => GatheringType.采矿工,
            GatheringType.采矿工        => GatheringType.采矿工,
            GatheringType.采伐      => GatheringType.园艺工,
            GatheringType.割草   => GatheringType.园艺工,
            GatheringType.园艺工     => GatheringType.园艺工,
            GatheringType.刺鱼 => GatheringType.捕鱼人,
            _                          => type,
        };
    }

    public static GatheringType Add(this GatheringType type, GatheringType other)
    {
        (type, other) = type < other ? (type, other) : (other, type);
        return type switch
        {
            GatheringType.采掘 => other switch
            {
                GatheringType.采掘     => GatheringType.采掘,
                GatheringType.碎石  => GatheringType.采矿工,
                GatheringType.采伐    => GatheringType.多职业,
                GatheringType.割草 => GatheringType.多职业,
                GatheringType.园艺工   => GatheringType.多职业,
                GatheringType.采矿工      => GatheringType.采矿工,
                GatheringType.多职业   => GatheringType.多职业,
                GatheringType.未知    => GatheringType.采掘,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.碎石 => other switch
            {
                GatheringType.碎石  => GatheringType.碎石,
                GatheringType.采伐    => GatheringType.多职业,
                GatheringType.割草 => GatheringType.多职业,
                GatheringType.园艺工   => GatheringType.多职业,
                GatheringType.采矿工      => GatheringType.采矿工,
                GatheringType.多职业   => GatheringType.多职业,
                GatheringType.未知    => GatheringType.碎石,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.采伐 => other switch
            {
                GatheringType.采伐    => GatheringType.采伐,
                GatheringType.割草 => GatheringType.园艺工,
                GatheringType.园艺工   => GatheringType.园艺工,
                GatheringType.采矿工      => GatheringType.多职业,
                GatheringType.多职业   => GatheringType.多职业,
                GatheringType.未知    => GatheringType.采伐,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.割草 => other switch
            {
                GatheringType.割草 => GatheringType.割草,
                GatheringType.园艺工   => GatheringType.园艺工,
                GatheringType.采矿工      => GatheringType.多职业,
                GatheringType.多职业   => GatheringType.多职业,
                GatheringType.未知    => GatheringType.割草,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.园艺工 => other switch
            {
                GatheringType.园艺工 => GatheringType.园艺工,
                GatheringType.采矿工    => GatheringType.多职业,
                GatheringType.多职业 => GatheringType.多职业,
                GatheringType.未知  => GatheringType.园艺工,
                _                      => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.采矿工 => other switch
            {
                GatheringType.采矿工    => GatheringType.多职业,
                GatheringType.多职业 => GatheringType.多职业,
                GatheringType.未知  => GatheringType.采矿工,
                _                      => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.多职业 => GatheringType.多职业,
            GatheringType.未知  => other,
            _                      => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
