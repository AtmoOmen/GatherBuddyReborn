using System;
using System.Collections.Generic;

namespace GatherBuddy.Enums;

// Ordered by the progression of time throughout a fishing route.
[Flags]
public enum OceanTime : byte
{
    �Ӳ�  = 0,
    ���� = 0x01,
    ҹ��  = 0x02,
    ����    = 0x04,

    ���� = ���� | ҹ�� | ����,
}

public static class OceanTimeExtensions
{
    public static OceanTime Next(this OceanTime time)
        => time switch
        {
            OceanTime.���� => OceanTime.ҹ��,
            OceanTime.ҹ��  => OceanTime.����,
            OceanTime.����    => OceanTime.����,
            _                => OceanTime.����,
        };

    public static OceanTime Previous(this OceanTime time)
        => time switch
        {
            OceanTime.���� => OceanTime.����,
            OceanTime.ҹ��  => OceanTime.����,
            OceanTime.����    => OceanTime.ҹ��,
            _                => OceanTime.����,
        };

    public static IEnumerable<OceanTime> Enumerate(this OceanTime time)
    {
        if (time.HasFlag(OceanTime.����))
            yield return OceanTime.����;
        if (time.HasFlag(OceanTime.ҹ��))
            yield return OceanTime.ҹ��;
        if (time.HasFlag(OceanTime.����))
            yield return OceanTime.����;
    }
}
