using System;

namespace GatherBuddy.Config;

public enum ColorId
{
    AvailableItem,
    UpcomingItem,
    DependentAvailableFish,
    DependentUpcomingFish,
    DisabledText,
    WarningBg,
    ChangedLocationBg,
    HighlightText,
    AvailableBait,
    CustomFishData,

    FishTimerWeakTug,
    FishTimerStrongTug,
    FishTimerLegendaryTugPrecision,
    FishTimerLegendaryTugPowerful,
    FishTimerWeakTugUncaught,
    FishTimerStrongTugUncaught,
    FishTimerLegendaryTugPrecisionUncaught,
    FishTimerLegendaryTugPowerfulUncaught,
    FishTimerUnknown,
    FishTimerUnavailable,
    FishTimerBackground,
    FishTimerProgress,
    FishTimerMarkersBait,
    FishTimerMarkersAll,
    FishTimerText,
    FishTimerLureNoCatch,

    HeaderEorzeaTime,
    HeaderNextHour,
    HeaderWeather,

    WeatherTabCurrent,
    WeatherTabLast,
    WeatherTabHeaderCurrent,
    WeatherTabHeaderLast,

    SpearfishHelperBackgroundFish,
    SpearfishHelperBackgroundList,
    SpearfishHelperCenterLine,
    SpearfishHelperTextFish,

    GatherWindowBackground,
    GatherWindowText,
    GatherWindowAvailable,
    GatherWindowUpcoming,
}

public static class ColorIdExtensions
{
    // @formatter:off
    public static (uint DefaultColor, string Name, string Description) Data(this ColorId color)
        => color switch
        {
            ColorId.AvailableItem                          => (0xFF20B020, "界面: 当前可采集物品",                                      "始终或当前可采集的物品"),
            ColorId.UpcomingItem                           => (0xFF20B0A0, "界面: 即将可采集物品",                                      "当前不可采集的物品"),
            ColorId.DependentAvailableFish                 => (0xFFFF3030, "界面: 依赖条件可采集鱼",                                    "自身条件满足但需鱼识或以鱼以小钓大的鱼"),
            ColorId.DependentUpcomingFish                  => (0xFFA020A0, "界面: 依赖条件不可采集鱼",                                  "自身条件未满足且需鱼识或以鱼以小钓大的鱼"),
            ColorId.DisabledText                           => (0xFF606060, "界面: 禁用文本",                                            "选择器中禁用对象的文本颜色"),
            ColorId.WarningBg                              => (0xA00000A0, "警告背景",                                                 "用户界面中警告标识的背景颜色"),
            ColorId.ChangedLocationBg                      => (0x80009000, "自定义位置数据背景",                                        "为特定位置自定义设置的以太之光或坐标的背景颜色"),
            ColorId.HighlightText                          => (0xFF00A0FF, "高亮文本",                                                 "在特定情况下用于高亮文本的颜色"),
            ColorId.AvailableBait                          => (0xFFB0E0FF, "可用钓饵",                                                 "背包中携带的钓饵在鱼类窗口中高亮显示的颜色"),
            ColorId.CustomFishData                         => (0xFFFFFFA0, "自定义鱼类数据",                                            "鱼类表中具有自定义覆盖数据的鱼类高亮显示的颜色"),
                                                                                                                                            
            ColorId.FishTimerWeakTug                       => (0x8000A000, "钓鱼计时: 轻杆",                                             "轻杆 (!) 咬钩的鱼"),
            ColorId.FishTimerStrongTug                     => (0x8000A0A0, "钓鱼计时: 普通竿",                                           "普通竿 (!!) 咬钩的鱼"),
            ColorId.FishTimerLegendaryTugPrecision         => (0x8000A080, "钓鱼计时: 鱼王竿（精准提钩）",                                "鱼王竿 (!!!) 且在耐心状态下需精准提钩的鱼"),
            ColorId.FishTimerLegendaryTugPowerful          => (0x800080A0, "钓鱼计时: 鱼王竿（强力提钩）",                                "鱼王竿 (!!!) 且在耐心状态下需强力提钩的鱼"),
            ColorId.FishTimerWeakTugUncaught               => (0x4000A000, "钓鱼计时: 轻杆（未捕获）",                                   "轻杆 (!) 但尚未用当前钓饵捕获的鱼"),
            ColorId.FishTimerStrongTugUncaught             => (0x4000A0A0, "钓鱼计时: 普通竿（未捕获）",                                 "普通竿 (!!) 但尚未用当前钓饵捕获的鱼"),
            ColorId.FishTimerLegendaryTugPrecisionUncaught => (0x4000A080, "钓鱼计时: 鱼王竿（精准提钩，未捕获）",                       "鱼王竿 (!!!) 且在耐心状态下需精准提钩但尚未用当前钓饵捕获的鱼"),
            ColorId.FishTimerLegendaryTugPowerfulUncaught  => (0x400080A0, "钓鱼计时: 鱼王竿（强力提钩，未捕获）",                       "鱼王竿 (!!!) 且在耐心状态下需强力提钩但尚未用当前钓饵捕获的鱼"),
            ColorId.FishTimerUnknown                       => (0x80404040, "钓鱼计时: 未知咬钩",                                         "咬钩强度未知的鱼"),
            ColorId.FishTimerUnavailable                   => (0x800000A0, "钓鱼计时: 不可用鱼",                                         "当前因条件未满足而不可用的鱼"),
            ColorId.FishTimerProgress                      => (0xFF000000, "钓鱼计时: 进度条",                                           "指示当前时间进度的直线"),
            ColorId.FishTimerMarkersBait                   => (0xFF0000C0, "钓鱼计时: 咬钩时间标记（当前钓饵）",                          "指示特定鱼在当前钓饵上记录的咬钩窗口起止的两条线"),
            ColorId.FishTimerMarkersAll                    => (0xFFE00000, "钓鱼计时: 咬钩时间标记（所有钓饵）",                          "指示特定鱼在所有钓饵上记录的咬钩窗口起止的两条线"),
            ColorId.FishTimerText                          => (0xFFFFFFFF, "钓鱼计时: 文本",                                             "钓鱼计时窗口中的文本颜色"),
            ColorId.FishTimerBackground                    => (0x80000000, "钓鱼计时: 背景",                                             "钓鱼计时窗口的背景颜色"),
            ColorId.FishTimerLureNoCatch                   => (0xFF300030, "钓鱼计时: 诱饵冷却死区填充",                                  "表示因诱饵冷却无法捕获鱼的时间段的阴影填充块"),
                                                                                                                                            
            ColorId.HeaderEorzeaTime                       => (0xFF008080, "页眉: 艾欧泽亚时间背景",                                    "主界面页眉中艾欧泽亚时间字段的背景颜色"),
            ColorId.HeaderNextHour                         => (0xFF404040, "页眉: 距离下一个艾欧泽亚小时背景",                            "主界面页眉中距离下一个艾欧泽亚小时字段的背景颜色"),
            ColorId.HeaderWeather                          => (0xFFA0A000, "页眉: 距离下一个天气背景",                                  "主界面页眉中距离下一个天气字段的背景颜色"),
                                                                    
            ColorId.WeatherTabCurrent                      => (0x1000FF00, "天气标签: 当前天气列",                                      "天气标签中当前天气单元格的背景颜色"),
            ColorId.WeatherTabLast                         => (0x100000FF, "天气标签: 上次天气列",                                      "天气标签中上次天气单元格的背景颜色"),
            ColorId.WeatherTabHeaderCurrent                => (0xFF008000, "天气标签: 当前天气页眉",                                    "天气标签中当前天气起始时间页眉单元格的背景颜色"),
            ColorId.WeatherTabHeaderLast                   => (0xFF000080, "天气标签: 上次天气页眉",                                    "天气标签中上次天气起始时间页眉单元格的背景颜色"),
                                                                    
            ColorId.SpearfishHelperBackgroundFish          => (0x40000000, "刺鱼助手: 鱼名背景",                                        "刺鱼助手中覆盖在移动鱼上的鱼名背景颜色"),
            ColorId.SpearfishHelperBackgroundList          => (0x80000000, "刺鱼助手: 列表背景",                                        "刺鱼助手中可用鱼列表的背景颜色"),
            ColorId.SpearfishHelperCenterLine              => (0xFF0000C0, "刺鱼助手: 中心线",                                          "从鱼叉中心向上的直线颜色"),
            ColorId.SpearfishHelperTextFish                => (0xFFFFFFFF, "刺鱼助手: 鱼名文本",                                        "刺鱼助手中覆盖在移动鱼上的鱼名文本颜色"),

            ColorId.GatherWindowBackground                 => (0x80000000, "采集窗口: 背景",                                             "采集窗口的背景颜色"),
            ColorId.GatherWindowText                       => (0xFFFFFFFF, "采集窗口: 文本",                                             "采集窗口中始终可用物品的颜色"),
            ColorId.GatherWindowAvailable                  => (0xFF20B020, "采集窗口: 当前可用物品",                                     "采集窗口中当前可用但非始终可用物品的颜色"),
            ColorId.GatherWindowUpcoming                   => (0xFF20B0A0, "采集窗口: 即将可用物品",                                     "采集窗口中当前不可用物品的颜色"),

            _                                              => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
    // @formatter:on

    public static uint Value(this ColorId color)
        => GatherBuddy.Config.Colors.TryGetValue(color, out var value) ? value : color.Data().DefaultColor;
}
