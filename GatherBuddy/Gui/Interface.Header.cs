using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using GatherBuddy.Classes;
using GatherBuddy.Config;
using GatherBuddy.Plugin;
using GatherBuddy.Time;
using ElliLib;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class Interface
{
    private class HeaderCache : IDisposable
    {
        public readonly Vector4 LastWeatherTint = new(1f, 0.5f, 0.5f, 1f);

        private uint                     _currentTerritory;
        public  Structs.Weather          LastWeather    = Structs.Weather.Invalid;
        public  Structs.Weather          CurrentWeather = Structs.Weather.Invalid;
        public  Structs.Weather          NextWeather    = Structs.Weather.Invalid;
        public  ISharedImmediateTexture? LastWeatherIcon;
        public  ISharedImmediateTexture? CurrentWeatherIcon;
        public  ISharedImmediateTexture? NextWeatherIcon;
        public  Vector2                  AlarmButtonSize = Vector2.Zero;

        private void NullWeather()
        {
            LastWeatherIcon    = null;
            CurrentWeatherIcon = null;
            NextWeatherIcon    = null;
            LastWeather        = Structs.Weather.Invalid;
            CurrentWeather     = Structs.Weather.Invalid;
            NextWeather        = Structs.Weather.Invalid;
        }

        private void UpdateWeather()
        {
            if (_currentTerritory == 0)
            {
                NullWeather();
            }
            else
            {
                (LastWeather, CurrentWeather, NextWeather) = GatherBuddy.WeatherManager.FindLastCurrentNextWeather(_currentTerritory);
                if (LastWeather.Id != 0)
                {
                    LastWeatherIcon    = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(LastWeather.Icon);
                    CurrentWeatherIcon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(CurrentWeather.Icon);
                    NextWeatherIcon    = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(NextWeather.Icon);
                }
                else
                {
                    NullWeather();
                }
            }
        }

        public HeaderCache()
            => GatherBuddy.Time.WeatherChanged += UpdateWeather;

        public void Dispose()
            => GatherBuddy.Time.WeatherChanged -= UpdateWeather;

        public void UpdateCurrentTerritory()
        {
            if (_currentTerritory == Dalamud.ClientState.TerritoryType)
                return;

            _currentTerritory = Dalamud.ClientState.TerritoryType;
            UpdateWeather();
        }
    }

    private readonly HeaderCache _headerCache = new();

    private void DrawLastAlarm(bool which, string failureText)
    {
        var alarmData = which ? _plugin.AlarmManager.LastItemAlarm : _plugin.AlarmManager.LastFishAlarm;
        if (alarmData == null)
        {
            ImGuiUtil.DrawDisabledButton(failureText, _headerCache.AlarmButtonSize, "点击以 /gather 此闹钟", true);
            return;
        }

        var (alarm, loc, time) = alarmData.Value;

        var text = $"{(alarm.Name.Any() ? alarm.Name : alarm.Item.Name[GatherBuddy.Language])}###{(which ? "itemAlarm" : "fishAlarm")}";
        var desc =
            $"点击以 /gather 此闹钟\n{loc.Name} - {loc.ClosestAetheryte?.Name ?? "无"}\n{time.Start.LocalTime}\n{time.End.LocalTime}";

        if (!ImGuiUtil.DrawDisabledButton(text, _headerCache.AlarmButtonSize, desc, false))
            return;

        if (which)
            _plugin.Executor.GatherItemByName("alarm");
        else
            _plugin.Executor.GatherFishByName("alarm");
    }

    private void DrawLastItemAlarm()
        => DrawLastAlarm(true, "无物品闹钟触发");

    private void DrawLastFishAlarm()
        => DrawLastAlarm(false, "无鱼类闹钟触发");


    private void DrawAlarmRow()
    {
        using var _ = ImRaii.Group();
        ImGui.SameLine();
        ConfigFunctions.DrawAlarmToggle();
        ImGui.SameLine();
        var vulcanButtonWidth = Math.Max(95f * Scale, ImGui.CalcTextSize("Vulcan").X + FramePadding.X * 5f);
        var collectablesButtonWidth = Math.Max(125f * Scale, ImGui.CalcTextSize("收藏品").X + FramePadding.X * 5f);
        {
            using var buttonAlign = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.30f, 0.25f, 0.46f, 1f));
            using var buttonHoveredColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.36f, 0.30f, 0.55f, 1f));
            using var buttonActiveColor = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.24f, 0.20f, 0.38f, 1f));
            if (ImGui.Button("Vulcan", new Vector2(vulcanButtonWidth, 0f)))
            {
                if (GatherBuddy.VulcanWindow == null)
                {
                    GatherBuddy.Log.Debug("[Interface] Vulcan 标题按钮已点击, 但 Vulcan 窗口不可用");
                }
                else
                {
                    GatherBuddy.Log.Debug("[Interface] 从主标题按钮恢复 Vulcan");
                    GatherBuddy.VulcanWindow.RestoreWindow();
                }
            }
        }
            ImGuiUtil.HoverTooltip("打开 Vulcan 制作窗口");
        ImGui.SameLine();
        {
            using var buttonAlign = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
            using var buttonColor = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.23f, 0.37f, 0.52f, 1f));
            using var buttonHoveredColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.28f, 0.45f, 0.63f, 1f));
            using var buttonActiveColor = ImRaii.PushColor(ImGuiCol.ButtonActive, new Vector4(0.19f, 0.31f, 0.43f, 1f));
            if (ImGui.Button("收藏品", new Vector2(collectablesButtonWidth, 0f)))
            {
                if (GatherBuddy.CollectablesWindow == null)
                {
                    GatherBuddy.Log.Debug("[Interface] 收藏品标题按钮已点击, 但收藏品窗口不可用");
                }
                else
                {
                    GatherBuddy.Log.Debug("[Interface] 从主标题按钮打开收藏品");
                    GatherBuddy.CollectablesWindow.Open();
                }
            }
        }
        ImGuiUtil.HoverTooltip("打开收藏品窗口");
        ImGui.SameLine();
        _headerCache.AlarmButtonSize = (ImGui.GetContentRegionAvail().X - ItemSpacing.X) / 2 * Vector2.UnitX;
        DrawLastItemAlarm();
        ImGui.SameLine();
        DrawLastFishAlarm();
    }

    private static void DrawEorzeaTime(string time)
    {
        ImGuiUtil.DrawTextButton(time, Vector2.UnitY * WeatherIconSize.Y, ColorId.HeaderEorzeaTime.Value());
        if (ImGui.IsItemHovered())
        {
            using var tt = ImRaii.Tooltip();
            ImGui.TextUnformatted("如果此时间与游戏中艾欧泽亚时间不一致, 请确认 Windows 系统时间准确");
            ImGui.TextUnformatted($"下次阿尔迪纳德出海航路:");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Aldenard,                              TimeStamp.UtcNow)}");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Aldenard,                              TimeStamp.UtcNow.AddHours(2))}");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Aldenard,                              TimeStamp.UtcNow.AddHours(4))}");
            ImGui.TextUnformatted($"下次奥萨德出海航路:");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Othard, TimeStamp.UtcNow)}");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Othard, TimeStamp.UtcNow.AddHours(2))}");
            ImGui.BulletText($"{OceanUptime.NextOceanRoute(OceanArea.Othard, TimeStamp.UtcNow.AddHours(4))}");
        }
    }

    private static void DrawNextEorzeaHour(string hour, Vector2 size)
        => ImGuiUtil.DrawTextButton(hour, size, ColorId.HeaderNextHour.Value());

    private static void DrawIconTint(Structs.Weather weather, ISharedImmediateTexture? icon, Vector2 size, Vector4 tint)
    {
        if (icon != null && icon.TryGetWrap(out var wrap, out _))
        {
            ImGui.Image(wrap.Handle, size, Vector2.Zero, Vector2.One, tint);
            ImGuiUtil.HoverTooltip($"{weather.Name} ({weather.Id})");
        }
        else
        {
            ImGui.Dummy(size);
        }
    }

    private static void DrawIcon(Structs.Weather weather, ISharedImmediateTexture? wrap, Vector2 size)
        => DrawIconTint(weather, wrap, size, Vector4.One);

    private void DrawNextWeather(string nextWeather)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        DrawIconTint(_headerCache.LastWeather, _headerCache.LastWeatherIcon, WeatherIconSize, _headerCache.LastWeatherTint);
        ImGui.SameLine();
        DrawIcon(_headerCache.CurrentWeather, _headerCache.CurrentWeatherIcon, WeatherIconSize);
        style.Pop();
        ImGui.SameLine();
        ImGuiUtil.DrawTextButton(nextWeather, Vector2.UnitY * WeatherIconSize.Y, ColorId.HeaderWeather.Value());
        ImGui.SameLine();
        DrawIcon(_headerCache.NextWeather, _headerCache.NextWeatherIcon, WeatherIconSize);
    }

    private void DrawTimeRow()
    {
        var now       = GatherBuddy.Time.ServerTime;
        var nextHourS = (now.SyncToEorzeaHour().AddEorzeaHours(1) - GatherBuddy.Time.ServerTime) / RealTime.MillisecondsPerSecond;
        var nextHourM = nextHourS / RealTime.SecondsPerMinute;

        var nextWeatherS = (now.SyncToEorzeaWeather().AddEorzeaHours(8) - GatherBuddy.Time.ServerTime) / RealTime.MillisecondsPerSecond;
        var nextWeatherM = nextWeatherS / RealTime.SecondsPerMinute;

        nextHourS    -= nextHourM * RealTime.SecondsPerMinute;
        nextWeatherS -= nextWeatherM * RealTime.SecondsPerMinute;

        var nextWeatherString = $"  {nextWeatherM:D2}:{nextWeatherS:D2} Min.  ";
        var width = -(ImGui.CalcTextSize(nextWeatherString).X
          + (WeatherIconSize.X + ItemSpacing.X + FramePadding.X) * 3);

        _headerCache.UpdateCurrentTerritory();
        using var _ = ImRaii.Group();
        DrawEorzeaTime($"ET {GatherBuddy.Time.EorzeaHourOfDay:D2}:{GatherBuddy.Time.EorzeaMinuteOfHour:D2}");
        ImGui.SameLine();
        DrawNextEorzeaHour($"{nextHourM:D2}:{nextHourS:D2} 分钟后进入下个小时", new Vector2(width, WeatherIconSize.Y));
        ImGui.SameLine();
        DrawNextWeather(nextWeatherString);
    }

    private void DrawHeader()
    {
        DrawAlarmRow();
        ImGui.Dummy(HorizontalSpace);
        DrawTimeRow();
        ImGui.Dummy(HorizontalSpace);
    }
}
