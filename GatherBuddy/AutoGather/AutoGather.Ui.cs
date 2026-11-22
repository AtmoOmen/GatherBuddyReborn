using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using GatherBuddy.CustomInfo;
using GatherBuddy.Helpers;
using GatherBuddy.Plugin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Numerics;

namespace GatherBuddy.AutoGather
{
    public static class AutoGatherUI
    {
        public static void DrawDebugTables()
        {
            if (ImGui.Button("从剪贴板导入采集点偏移数据"))
            {
                var settings = new JsonSerializerSettings();
                var text = RebornUI.Ui.ReadClipboard();
                var vectors = JsonConvert.DeserializeObject<List<OffsetPair>>(text, settings) ?? [];
                foreach (var offset in vectors)
                {
                    WorldData.NodeOffsets[offset.Original] = offset.Offset;
                    GatherBuddy.Log.Information($"导入偏移 {offset} 到数据");
                }
                WorldData.SaveOffsetsToFile();
                GatherBuddy.Log.Information("已保存");
            }
            ImGui.SameLine();
            if (ImGui.Button("复制采集点偏移数据到剪贴板"))
            {
                var settings = new JsonSerializerSettings();
                var offsets = new List<OffsetPair>(WorldData.NodeOffsets.Count);
                foreach (var (original, offset) in WorldData.NodeOffsets)
                {
                    offsets.Add(new OffsetPair(original, offset));
                }

                var offsetString = JsonConvert.SerializeObject(offsets, Formatting.Indented, settings);
                ImGui.SetClipboardText(offsetString);
                GatherBuddy.Log.Information("采集点偏移数据已复制到剪贴板");
            }
            // First column: Nearby nodes table
            if (ImGui.BeginTable("##nearbyNodesTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("名称");
                ImGui.TableSetupColumn("可选择");
                ImGui.TableSetupColumn("采集点 ID");
                ImGui.TableSetupColumn("位置");
                ImGui.TableSetupColumn("距离");
                ImGui.TableSetupColumn("自动偏移");
                ImGui.TableSetupColumn("操作");

                ImGui.TableHeadersRow();

                var playerPosition = Player.Object?.Position ?? Vector3.Zero;
                var nearbyNodes = new List<(IGameObject Node, float Distance, int Index)>();
                foreach (var obj in Dalamud.Objects)
                {
                    if (obj.ObjectKind == ObjectKind.GatheringPoint)
                    {
                        nearbyNodes.Add((obj, Vector3.Distance(obj.Position, playerPosition), nearbyNodes.Count));
                    }
                }

                nearbyNodes.Sort(static (lhs, rhs) =>
                {
                    var order = lhs.Distance.CompareTo(rhs.Distance);
                    return order != 0 ? order : lhs.Index.CompareTo(rhs.Index);
                });
                foreach (var (node, _, _) in nearbyNodes)
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
                        if (ImGui.Button($"移出黑名单##{node.Position}"))
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
                        if (ImGui.Button($"加入黑名单##{node.Position}"))
                        {
                            if (list == null)
                            {
                                list = [];
                                GatherBuddy.Config.AutoGatherConfig.BlacklistedNodesByTerritoryId[territoryId] = list;
                            }

                            list.Add(node.Position);
                            GatherBuddy.Config.Save();
                        }
                    }

                    if (ImGui.Button($"导航到##{node.Position}"))
                    {
                        if (GatherBuddy.AutoGather.Enabled)
                        {
                            Communicator.PrintError("[GatherBuddyReborn] 请先禁用自动采集, 无法使用手动导航");
                            return;
                        }
                        //VNavmesh_IPCSubscriber.Nav_PathfindCancelAll();
                        VNavmesh.Path.Stop();
                        VNavmesh.SimpleMove.PathfindAndMoveTo(node.Position, GatherBuddy.AutoGather.ShouldFly(node.Position));
                    }

                    if (WorldData.NodeOffsets.TryGetValue(node.Position, out var offset))
                    {
                        if (ImGui.Button($"移出偏移##{node.Position}"))
                        {
                            WorldData.NodeOffsets.Remove(node.Position);
                            WorldData.SaveOffsetsToFile();
                        }
                        ImGui.Text(offset.ToString());
                        if (ImGui.Button($"导航到偏移##{node.Position}"))
                        {
                            if (GatherBuddy.AutoGather.Enabled)
                            {
                                Communicator.PrintError("[GatherBuddyReborn] 请先禁用自动采集, 无法使用手动导航");
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
