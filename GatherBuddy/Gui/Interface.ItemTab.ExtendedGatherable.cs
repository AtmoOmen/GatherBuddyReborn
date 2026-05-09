using System;
using System.Linq;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Classes;
using GatherBuddy.Enums;
using GatherBuddy.Interfaces;
using GatherBuddy.Time;

namespace GatherBuddy.Gui;

public partial class Interface
{
    public class ExtendedGatherable
    {
        public Gatherable              Data;
        public ISharedImmediateTexture Icon;
        public string                  Territories;
        public string                  Uptimes;
        public string                  Folklore;
        public string                  Level;
        public string                  NodeNames;
        public string                  Expansion;
        public string                  Aetherytes;
        public bool?                   Gathered;

        private bool _gatheredStatusFailureLogged;

        public (ILocation, TimeInterval) Uptime
            => GatherBuddy.UptimeManager.BestLocation(Data);

        public ExtendedGatherable(Gatherable data)
        {
            Data = data;
            Icon = Icons.DefaultStorage.TextureProvider.GetFromGameIcon(new GameIconLookup(data.ItemData.Icon));

            Territories = string.Join("\n", data.NodeList.Select(n => n.Territory.Name).Distinct());
            if (!Territories.Contains('\n'))
                Territories = '\0' + Territories;

            Folklore = data.NodeList.Count == 0 || data.NodeList.Any(n => n.Folklore.Length == 0)
                ? string.Empty
                : data.NodeList.First().Folklore;
            Uptimes = data.NodeType switch
            {
                NodeType.Regular => "常规",
                NodeType.Unknown => "未知",
                _                => data.NodeList.Select(n => n.Times).Aggregate(BitfieldUptime.Combine).PrintHours(true),
            };
            Level     = Data.LevelString();
            NodeNames = string.Join("\n", data.NodeList.Select(n => n.Name).Distinct());
            if (!NodeNames.Contains('\n'))
                NodeNames = '\0' + NodeNames;

            Expansion = data.ExpansionIdx switch
            {
                0 => "2.0",
                1 => "3.0",
                2 => "4.0",
                3 => "5.0",
                4 => "6.0",
                5 => "7.0",
                _ => "未知",

                /*0 => "重生之境",
                1 => "苍穹之禁城",
                2 => "红莲之狂潮",
                3 => "暗影之逆焰",
                4 => "晓月之终途",
                5 => "金曦之遗辉",
                _ => "未知",*/
            };
            Aetherytes = string.Join("\n",
                data.NodeList.Where(n => n.ClosestAetheryte != null).Select(n => n.ClosestAetheryte!.Name).Distinct());
            if (!Aetherytes.Contains('\n'))
                Aetherytes = '\0' + Aetherytes;

            UpdateGatheredStatus();
        }

        public void UpdateGatheredStatus()
        {
            if (Data.GatheringId == 0 || Data.GatheringId > ushort.MaxValue)
            {
                if (!_gatheredStatusFailureLogged)
                {
                    GatherBuddy.Log.Debug(
                        $"[GatherablesTab] Unable to check gathered status for item {Data.ItemId}: invalid gathering item id {Data.GatheringId}.");
                    _gatheredStatusFailureLogged = true;
                }

                Gathered = null;
                return;
            }

            try
            {
                Gathered = QuestManager.IsGatheringItemGathered((ushort)Data.GatheringId);
                _gatheredStatusFailureLogged = false;
            }
            catch (Exception ex)
            {
                if (!_gatheredStatusFailureLogged)
                {
                    GatherBuddy.Log.Debug(
                        $"[GatherablesTab] Failed to check gathered status for item {Data.ItemId} / gathering item {Data.GatheringId}: {ex.Message}");
                    _gatheredStatusFailureLogged = true;
                }

                Gathered = null;
            }
        }
    }
}
