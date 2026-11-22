using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Exceptions;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using System.Runtime.CompilerServices;

namespace GatherBuddy.Utility;

public readonly struct MultiString(string en, string de, string fr, string jp, string? chs = null)
{
    // XIVLauncherCN uses language value 4; the international SDK need not name it.
    public const ClientLanguage SimplifiedChinese = (ClientLanguage)4;

    private static readonly ConditionalWeakTable<IDataManager, ChineseNameSheets> ChineseSheets = new();

    private sealed class ChineseNameSheets(IDataManager gameData)
    {
        public ExcelSheet<Item>? Items { get; } = TryGetChineseSheet<Item>(gameData);
        public ExcelSheet<PlaceName>? PlaceNames { get; } = TryGetChineseSheet<PlaceName>(gameData);
    }

    private static ExcelSheet<T>? TryGetChineseSheet<T>(IDataManager gameData) where T : struct, IExcelRow<T>
    {
        try
        {
            return gameData.GetExcelSheet<T>(SimplifiedChinese);
        }
        catch (UnsupportedLanguageException)
        {
            return null;
        }
    }

    public static string ParseSeStringLumina(ReadOnlySeString? luminaString)
        => luminaString?.ExtractText() ?? string.Empty;

    public readonly string English = en;
    public readonly string German = de;
    public readonly string French = fr;
    public readonly string Japanese = jp;
    public readonly string ChineseSimplified = chs ?? string.Empty;

    public string this[ClientLanguage lang]
        => Name(lang);

    public override string ToString()
        => Name(SimplifiedChinese);

    public string ToWholeString()
        => $"{English}|{German}|{French}|{Japanese}|{ChineseSimplified}";


    public static MultiString FromPlaceName(IDataManager gameData, uint id)
    {
        var en = ParseSeStringLumina(gameData.GetExcelSheet<PlaceName>(ClientLanguage.English).GetRowOrDefault(id)?.Name);
        var de = ParseSeStringLumina(gameData.GetExcelSheet<PlaceName>(ClientLanguage.German).GetRowOrDefault(id)?.Name);
        var fr = ParseSeStringLumina(gameData.GetExcelSheet<PlaceName>(ClientLanguage.French).GetRowOrDefault(id)?.Name);
        var jp = ParseSeStringLumina(gameData.GetExcelSheet<PlaceName>(ClientLanguage.Japanese).GetRowOrDefault(id)?.Name);
        var sheet = ChineseSheets.GetValue(gameData, static data => new ChineseNameSheets(data)).PlaceNames;
        return new MultiString(en, de, fr, jp, ParseSeStringLumina(sheet?.GetRowOrDefault(id)?.Name));
    }

    public static MultiString FromItem(IDataManager gameData, uint id)
    {
        var en = ParseSeStringLumina(gameData.GetExcelSheet<Item>(ClientLanguage.English).GetRowOrDefault(id)?.Name);
        var de = ParseSeStringLumina(gameData.GetExcelSheet<Item>(ClientLanguage.German).GetRowOrDefault(id)?.Name);
        var fr = ParseSeStringLumina(gameData.GetExcelSheet<Item>(ClientLanguage.French).GetRowOrDefault(id)?.Name);
        var jp = ParseSeStringLumina(gameData.GetExcelSheet<Item>(ClientLanguage.Japanese).GetRowOrDefault(id)?.Name);
        var sheet = ChineseSheets.GetValue(gameData, static data => new ChineseNameSheets(data)).Items;
        return new MultiString(en, de, fr, jp, ParseSeStringLumina(sheet?.GetRowOrDefault(id)?.Name));
    }

    private string Name(ClientLanguage lang)
        => lang switch
        {
            ClientLanguage.English => English,
            ClientLanguage.German => German,
            ClientLanguage.Japanese => Japanese,
            ClientLanguage.French => French,
            SimplifiedChinese => string.IsNullOrEmpty(ChineseSimplified) ? English : ChineseSimplified,
            _ => English,
        };

    public static readonly MultiString Empty = new(string.Empty, string.Empty, string.Empty, string.Empty);
}
