using Dalamud.Game.ClientState.Objects.Enums;
using GatherBuddy.Helpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using GatherBuddy.Plugin;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

using FFXIVClientStructs.FFXIV.Client.UI;
using GatherBuddy.CustomInfo;
using Newtonsoft.Json;
using ElliLib;
using ElliLib.Raii;

namespace GatherBuddy.AutoGather
{
    public static class AutoGatherUI
    {
        public static void DrawAutoGatherStatus()
        {
            var enabled = GatherBuddy.AutoGather.Enabled;
            if (ImGui.Checkbox("启用", ref enabled))
            {
                GatherBuddy.AutoGather.Enabled = enabled;
            }

            ImGui.Text($"状态: {GatherBuddy.AutoGather.AutoStatus}");
        }


        public static void DrawDebugTables()
        {
            if (ImGui.Button("从剪贴板导入节点偏移设置"))
            {
                var settings = new JsonSerializerSettings();
                var                          text    = ImGuiUtil.GetClipboardText();
                var vectors = JsonConvert.DeserializeObject<List<OffsetPair>>(text, settings) ?? [];
                foreach (var offset in vectors)
                {
                    WorldData.NodeOffsets[offset.Original] = offset.Offset;
                    GatherBuddy.Log.Information($"已添加偏移 {offset} 到字典");
                }
                WorldData.SaveOffsetsToFile();
                GatherBuddy.Log.Information("导入完成");
            }
            ImGui.SameLine();
            if (ImGui.Button("导出节点偏移设置至剪贴板"))
            {
                var settings = new JsonSerializerSettings();
                var offsetString = JsonConvert.SerializeObject(WorldData.NodeOffsets.Select(x => new OffsetPair(x.Key, x.Value)).ToList(), Formatting.Indented, settings);
                ImGui.SetClipboardText(offsetString);
                GatherBuddy.Log.Information("节点偏移设置已导出至剪贴板");
            }
            // First column: Nearby nodes table
            if (ImGui.BeginTable("##nearbyNodesTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("名称");
                ImGui.TableSetupColumn("可选中");
                ImGui.TableSetupColumn("节点 ID");
                ImGui.TableSetupColumn("位置");
                ImGui.TableSetupColumn("距离");
                ImGui.TableSetupColumn("自动偏移");
                ImGui.TableSetupColumn("操作");

                ImGui.TableHeadersRow();

                var playerPosition = Player.Object?.Position ?? Vector3.Zero;
                foreach (var node in Dalamud.Objects.Where(o => o.ObjectKind == ObjectKind.GatheringPoint)
                             .OrderBy(o => Vector3.Distance(o.Position, playerPosition)))
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(node.Name.ToString());
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(node.IsTargetable ? "是" : "否");
                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text(node.BaseId.ToString());
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text(node.Position.ToString());
                    ImGui.TableSetColumnIndex(4);
                    var distance = Vector3.Distance(playerPosition, node.Position);
                    ImGui.Text(distance.ToString());
                    ImGui.TableSetColumnIndex(5);
                    var autoOffsetCount = AutoOffsets.GetOffsetCount(node.BaseId, node.Position);
                    ImGui.Text(autoOffsetCount.ToString());
                    ImGui.TableSetColumnIndex(6);

                    var territoryId = Dalamud.ClientState.TerritoryType;
                    var isBlacklisted = GatherBuddy.Config.AutoGatherConfig.BlacklistedNodesByTerritoryId.TryGetValue(territoryId, out var list)
                     && list.Contains(node.Position);

                    if (isBlacklisted && list != null)
                    {
                        if (ImGui.Button($"移除黑名单##{node.Position}"))
                        {
                            list.Remove(node.Position);
                            if (list.Count == 0)
                            {
                                GatherBuddy.Config.AutoGatherConfig.BlacklistedNodesByTerritoryId.Remove(territoryId);
                            }

                            GatherBuddy.Config.Save();
                        }
                    }
                    else
                    {
                        if (ImGui.Button($"添加黑名单##{node.Position}"))
                        {
                            if (list == null)
                            {
                                list                                                                           = new List<Vector3>();
                                GatherBuddy.Config.AutoGatherConfig.BlacklistedNodesByTerritoryId[territoryId] = list;
                            }

                            list.Add(node.Position);
                            GatherBuddy.Config.Save();
                        }
                    }

                    if (ImGui.Button($"导航至##{node.Position}"))
                    {
                        if (GatherBuddy.AutoGather.Enabled)
                        {
                            Communicator.PrintError("[GatherBuddyReborn] 已启用自动采集, 无法使用手动导航");
                            return;
                        }
                        //VNavmesh_IPCSubscriber.Nav_PathfindCancelAll();
                        VNavmesh.Path.Stop();
                        VNavmesh.SimpleMove.PathfindAndMoveTo(node.Position, GatherBuddy.AutoGather.ShouldFly(node.Position));
                    }

                    if (WorldData.NodeOffsets.TryGetValue(node.Position, out var offset))
                    {
                        if (ImGui.Button($"移除该偏移##{node.Position}"))
                        {
                            WorldData.NodeOffsets.Remove(node.Position);
                            WorldData.SaveOffsetsToFile();
                        }
                        ImGui.Text(offset.ToString());
                        if (ImGui.Button($"导航至偏移##{node.Position}"))
                        {
                            if (GatherBuddy.AutoGather.Enabled)
                            {
                                Communicator.PrintError("[GatherBuddyReborn] 已启用自动采集, 无法使用手动导航");
                                return;
                            }
                            //VNavmesh_IPCSubscriber.Nav_PathfindCancelAll();
                            VNavmesh.Path.Stop();
                            VNavmesh.SimpleMove.PathfindAndMoveTo(offset, GatherBuddy.AutoGather.ShouldFly(offset));
                        }
                    }
                    else
                    {
                        if (ImGui.Button($"添加此偏移##{node.Position}"))
                        {
                            WorldData.AddOffset(node.Position, playerPosition);
                        }
                        ImGui.Text(playerPosition.ToString());
                    }
                    
                }

                ImGui.EndTable();
            }
        }

        public unsafe static void DrawMountSelector()
        {
            ImGui.PushItemWidth(200);
            var ps = PlayerState.Instance();
            var preview = Dalamud.GameData.GetExcelSheet<Mount>().First(x => x.RowId == GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId)
                .Singular.ToString().ToProperCase();
            if (string.IsNullOrEmpty(preview))
                preview = "随机坐骑";
            if (ImGui.BeginCombo("随机坐骑", preview))
            {
                if (ImGui.Selectable("随机坐骑", GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId == 0))
                {
                    GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId = 0;
                    GatherBuddy.Config.Save();
                }

                foreach (var mount in Dalamud.GameData.GetExcelSheet<Mount>().OrderBy(x => x.Singular.ToString().ToProperCase()))
                {
                    if (ps->IsMountUnlocked(mount.RowId))
                    {
                        var selected = ImGui.Selectable(mount.Singular.ToString().ToProperCase(),
                            GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId == mount.RowId);

                        if (selected)
                        {
                            GatherBuddy.Config.AutoGatherConfig.AutoGatherMountId = mount.RowId;
                            GatherBuddy.Config.Save();
                        }
                    }
                }

                ImGui.EndCombo();
            }
        }

        /// <summary>
        /// Extension method to convert the string to Proper Case.
        /// </summary>
        /// <param name="input">The string input.</param>
        /// <returns>The string in Proper Case.</returns>
        public static string ToProperCase(this string input)
        {
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
        }
    }
}

