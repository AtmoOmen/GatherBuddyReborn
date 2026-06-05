using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace GatherBuddy.Classes;

public class GatheringIcons
{
    private readonly Dictionary<Enums.GatheringType, (uint, uint)> _icons;

    public GatheringIcons(IDataManager gameData)
    {
        var sheet = gameData.GetExcelSheet<GatheringType>()!;
        _icons = new Dictionary<Enums.GatheringType, (uint, uint)>(Enum.GetValues<Enums.GatheringType>().Length - 2)
        {
            [Enums.GatheringType.采掘]       = ((uint)sheet.GetRow(0).IconMain, (uint)sheet.GetRow(0).IconOff),
            [Enums.GatheringType.碎石]    = ((uint)sheet.GetRow(1).IconMain, (uint)sheet.GetRow(1).IconOff),
            [Enums.GatheringType.采伐]      = ((uint)sheet.GetRow(2).IconMain, (uint)sheet.GetRow(2).IconOff),
            [Enums.GatheringType.割草]   = ((uint)sheet.GetRow(3).IconMain, (uint)sheet.GetRow(3).IconOff),
            [Enums.GatheringType.刺鱼] = ((uint)sheet.GetRow(4).IconMain, (uint)sheet.GetRow(4).IconOff),
            [Enums.GatheringType.捕鱼人]       = (60465, 60466),
        };
        _icons[Enums.GatheringType.采矿工]    = _icons[Enums.GatheringType.采掘];
        _icons[Enums.GatheringType.园艺工] = _icons[Enums.GatheringType.采伐];
    }

    public (uint, uint) this[Enums.GatheringType val]
        => _icons[val];
}
