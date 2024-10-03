using System;

namespace GatherBuddy.Enums;

public enum GatheringType : byte
{
    �ɿ�   = 0,
    ��ʯ   = 1,
    ��ľ   = 2,
    ���   = 3,
    ����   = 4,
    ԰�չ� = 5,
    �ɿ� = 6,
    ������ = 7,
    ��ְҵ = 8,
    δ֪   = byte.MaxValue,
};

public static class GatheringTypeExtension
{
    public static GatheringType ToGroup(this GatheringType type)
    {
        return type switch
        {
            GatheringType.�ɿ�   => GatheringType.�ɿ�,
            GatheringType.��ʯ   => GatheringType.�ɿ�,
            GatheringType.�ɿ� => GatheringType.�ɿ�,
            GatheringType.��ľ   => GatheringType.԰�չ�,
            GatheringType.���   => GatheringType.԰�չ�,
            GatheringType.԰�չ� => GatheringType.԰�չ�,
            GatheringType.����   => GatheringType.������,
            _                    => type,
        };
    }

    public static GatheringType Add(this GatheringType type, GatheringType other)
    {
        (type, other) = type < other ? (type, other) : (other, type);
        return type switch
        {
            GatheringType.�ɿ� => other switch
            {
                GatheringType.�ɿ�     => GatheringType.�ɿ�,
                GatheringType.��ʯ  => GatheringType.�ɿ�,
                GatheringType.��ľ    => GatheringType.��ְҵ,
                GatheringType.��� => GatheringType.��ְҵ,
                GatheringType.԰�չ�   => GatheringType.��ְҵ,
                GatheringType.�ɿ�      => GatheringType.�ɿ�,
                GatheringType.��ְҵ   => GatheringType.��ְҵ,
                GatheringType.δ֪    => GatheringType.�ɿ�,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.��ʯ => other switch
            {
                GatheringType.��ʯ  => GatheringType.��ʯ,
                GatheringType.��ľ    => GatheringType.��ְҵ,
                GatheringType.��� => GatheringType.��ְҵ,
                GatheringType.԰�չ�   => GatheringType.��ְҵ,
                GatheringType.�ɿ�      => GatheringType.�ɿ�,
                GatheringType.��ְҵ   => GatheringType.��ְҵ,
                GatheringType.δ֪    => GatheringType.��ʯ,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.��ľ => other switch
            {
                GatheringType.��ľ    => GatheringType.��ľ,
                GatheringType.��� => GatheringType.԰�չ�,
                GatheringType.԰�չ�   => GatheringType.԰�չ�,
                GatheringType.�ɿ�      => GatheringType.��ְҵ,
                GatheringType.��ְҵ   => GatheringType.��ְҵ,
                GatheringType.δ֪    => GatheringType.��ľ,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.��� => other switch
            {
                GatheringType.��� => GatheringType.���,
                GatheringType.԰�չ�   => GatheringType.԰�չ�,
                GatheringType.�ɿ�      => GatheringType.��ְҵ,
                GatheringType.��ְҵ   => GatheringType.��ְҵ,
                GatheringType.δ֪    => GatheringType.���,
                _                        => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.԰�չ� => other switch
            {
                GatheringType.԰�չ� => GatheringType.԰�չ�,
                GatheringType.�ɿ�    => GatheringType.��ְҵ,
                GatheringType.��ְҵ => GatheringType.��ְҵ,
                GatheringType.δ֪  => GatheringType.԰�չ�,
                _                      => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.�ɿ� => other switch
            {
                GatheringType.�ɿ�    => GatheringType.��ְҵ,
                GatheringType.��ְҵ => GatheringType.��ְҵ,
                GatheringType.δ֪  => GatheringType.�ɿ�,
                _                      => throw new ArgumentOutOfRangeException(nameof(other), other, null),
            },
            GatheringType.��ְҵ => GatheringType.��ְҵ,
            GatheringType.δ֪  => other,
            _                      => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
