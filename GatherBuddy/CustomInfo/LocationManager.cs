using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GatherBuddy.Classes;
using GatherBuddy.Interfaces;
using GatherBuddy.Plugin;
using GatherBuddy.Structs;
using Newtonsoft.Json;

namespace GatherBuddy.CustomInfo;

public class LocationManager
{
    public readonly List<ILocation> AllLocations = GatherBuddy.GameData.GatheringNodes.Values
        .Cast<ILocation>()
        .Concat(GatherBuddy.GameData.FishingSpots.Values)
        .ToList();

    public const string FileName = "custom_locations.json";

    private static bool HasCustomization(ILocation loc)
        => !ReferenceEquals(loc.ClosestAetheryte, loc.DefaultAetheryte)
         || loc.IntegralXCoord != loc.DefaultXCoord
         || loc.IntegralYCoord != loc.DefaultYCoord
         || loc.Markers.AnySet
         || loc.Radius != loc.DefaultRadius;

    public IEnumerable<LocationData> CustomLocations
        => AllLocations.Where(HasCustomization).Select(l => new LocationData(l));

    private string CustomLocationData()
        => JsonConvert.SerializeObject(CustomLocations);

    public void SetMarkers(ILocation loc, in WaymarkSet markers)
    {
        if (loc.SetMarkers(markers))
            Save();
    }

    public void SetXCoord(ILocation loc, int newCoord)
    {
        if (loc.SetXCoord(newCoord))
            Save();
    }

    public void SetYCoord(ILocation loc, int newCoord)
    {
        if (loc.SetYCoord(newCoord))
            Save();
    }

    public void SetAetheryte(ILocation loc, Aetheryte? newAetheryte)
    {
        if (loc.SetAetheryte(newAetheryte))
            Save();
    }

    public void SetRadius(ILocation loc, ushort newRadius)
    {
        if (loc.SetRadius(newRadius))
            Save();
    }

    public void Save()
    {
        var file = Functions.ObtainSaveFile(FileName);
        if (file == null)
            return;

        try
        {
            var text = CustomLocationData();
            File.WriteAllText(file.FullName, text);
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"Could not write custom locations to file {file.FullName}:\n{e}");
        }
    }

    public static LocationManager Load()
    {
        var             file = Functions.ObtainSaveFile(FileName);
        LocationManager ret  = new();
        if (file is not { Exists: true })
        {
            ret.Save();
            return ret;
        }

        try
        {
            var changes = false;
            var text    = File.ReadAllText(file.FullName);
            foreach (var location in JsonConvert.DeserializeObject<LocationData[]>(text)!)
            {
                ILocation? loc = location.Type switch
                {
                    ObjectType.Gatherable => GatherBuddy.GameData.GatheringNodes.GetValueOrDefault(location.Id),
                    ObjectType.Fish       => GatherBuddy.GameData.FishingSpots.GetValueOrDefault(location.Id),
                    _                     => null,
                };
                if (loc == null)
                {
                    GatherBuddy.Log.Error($"类型为 {location.Type} 的自定义位置 {location.Id} 无效，已跳过。");
                    changes = true;
                    continue;
                }


                Aetheryte? aetheryte = null;
                if (location.AetheryteId != -1)
                    if (!GatherBuddy.GameData.Aetherytes.TryGetValue((uint)location.AetheryteId, out aetheryte))
                    {
                        GatherBuddy.Log.Error($"{loc.Name} 的自定义位置中的以太之光 ID {location.AetheryteId} 无效。");
                        changes = true;
                        continue;
                    }

                changes |= !loc.SetAetheryte(aetheryte);
                changes |= !loc.SetXCoord(location.XCoord);
                changes |= !loc.SetYCoord(location.YCoord);
                changes |= !loc.SetMarkers(location.Markers);
                changes |= !loc.SetRadius(location.Radius);
            }

            if (changes)
                ret.Save();
        }
        catch (Exception e)
        {
            GatherBuddy.Log.Error($"Error loading custom infos:\n{e}");
        }

        return ret;
    }
}
