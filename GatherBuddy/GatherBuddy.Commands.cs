using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using GatherBuddy.Crafting;
using GatherBuddy.Enums;
using GatherBuddy.Plugin;
using GatherBuddy.Time;

namespace GatherBuddy;

public partial class GatherBuddy
{
    public const string IdentifyCommand       = "identify";
    public const string GearChangeCommand     = "gearchange";
    public const string TeleportCommand       = "teleport";
    public const string MapMarkerCommand      = "mapmarker";
    public const string AdditionalInfoCommand = "information";
    public const string SetWaymarksCommand    = "waymarks";
    public const string AutoCommand           = "auto";
    public const string AutoOnCommand         = "auto on";
    public const string AutoOffCommand        = "auto off";
    public const string FullIdentify          = $"/gatherbuddy {IdentifyCommand}";
    public const string FullGearChange        = $"/gatherbuddy {GearChangeCommand}";
    public const string FullTeleport          = $"/gatherbuddy {TeleportCommand}";
    public const string FullMapMarker         = $"/gatherbuddy {MapMarkerCommand}";
    public const string FullAdditionalInfo    = $"/gatherbuddy {AdditionalInfoCommand}";
    public const string FullSetWaymarks       = $"/gatherbuddy {SetWaymarksCommand}";
    public const string FullAuto              = $"/gatherbuddy {AutoCommand}";
    public const string FullAutoOn            = $"/gatherbuddy {AutoOnCommand}";
    public const string FullAutoOff           = $"/gatherbuddy {AutoOffCommand}";

    private readonly Dictionary<string, CommandInfo> _commands = new();

    private void InitializeCommands()
    {
        _commands["/gatherbuddy"] = new CommandInfo(OnGatherBuddy)
        {
            HelpMessage = "打开插件界面",
            ShowInHelp  = false,
        };

        _commands["/gbr"] = new CommandInfo(OnGatherBuddy)
        {
            HelpMessage = "打开插件界面",
            ShowInHelp  = true,
        };

        _commands["/gather"] = new CommandInfo(OnGather)
        {
            HelpMessage = "传送至距离目标采集区域最近的以太之光, 切换至对应的套装, 并标记最近的包含待采集物品的采集点\n"
              + "可以传入 'alarm' 参数以去采集最近一次被触发的采集闹钟对应的物品, 或者传入 'next' 参数来采集与之前相同的物品",
            ShowInHelp = true,
        };

        _commands["/gatherbtn"] = new CommandInfo(OnGatherBtn)
        {
            HelpMessage =
                "传送至距离目标采集区域最近的以太之光, 切换至对应的套装, 并标记最近的包含待采集物品的园艺工采集点",
            ShowInHelp = true,
        };

        _commands["/gathermin"] = new CommandInfo(OnGatherMin)
        {
            HelpMessage =
                "传送至距离目标采集区域最近的以太之光, 切换至对应的套装, 并标记最近的包含待采集物品的采矿工采集点",
            ShowInHelp = true,
        };

        _commands["/gatherfish"] = new CommandInfo(OnGatherFish)
        {
            HelpMessage =
                "传送至距离目标采集区域最近的以太之光, 切换至对应的套装, 并标记最近的包含待采集物品的捕鱼人采集点",
            ShowInHelp = true,
        };

        _commands["/gathergroup"] = new CommandInfo(OnGatherGroup)
        {
            HelpMessage = "传送至与当前物品相关的采集组最近的以太之光, 单独输入以查看更多细节",
            ShowInHelp  = true,
        };

        _commands["/gbc"] = new CommandInfo(OnGatherBuddyShort)
        {
            HelpMessage = "一些快捷配置, 单独输入以查看更多细节",
            ShowInHelp  = true,
        };

        _commands["/gatherdebug"] = new CommandInfo(OnGatherDebug)
        {
            ShowInHelp = false,
        };

        _commands["/vulcan"] = new CommandInfo(OnVulcan)
        {
            HelpMessage = "打开 Vulcan 制作界面\n\t参数: <制作清单ID> → 直接跳转到该清单界面\n\t参数: craft <配方ID|名称> <数量> → 立即开始采集/制作流程",
            ShowInHelp  = true,
        };

        _commands["/vvendor"] = new CommandInfo(OnVendor)
        {
            HelpMessage = "Open the Vulcan Vendors tab.",
            ShowInHelp  = true,
        };

        _commands["/vulcanmb"] = new CommandInfo(OnVulcanMarketboard)
        {
            HelpMessage = "Open the Vulcan Marketboard tab.",
            ShowInHelp  = true,
        };

        _commands["/vcollect"] = new CommandInfo(OnCollectablesWindow)
        {
            HelpMessage = "Open the Collectables turn-in and purchase window.",
            ShowInHelp  = true,
        };

        foreach (var (command, info) in _commands)
            Dalamud.Commands.AddHandler(command, info);
    }

    private void DisposeCommands()
    {
        foreach (var command in _commands.Keys)
            Dalamud.Commands.RemoveHandler(command);
    }

    private void OnGatherBuddy(string command, string arguments)
    {
        if (arguments.Equals("vulcan", StringComparison.OrdinalIgnoreCase))
        {
            _vulcanWindow?.Toggle();
            return;
        }


        if (!Executor.DoCommand(arguments))
            Interface.Toggle();
    }

    private void OnGather(string command, string arguments)
    {
        if (arguments.Length == 0)
            Communicator.NoItemName(command, "item");
        else
            Executor.GatherItemByName(arguments);
    }

    private void OnGatherBtn(string command, string arguments)
    {
        if (arguments.Length == 0)
            Communicator.NoItemName(command, "item");
        else
            Executor.GatherItemByName(arguments, GatheringType.园艺工);
    }

    private void OnGatherMin(string command, string arguments)
    {
        if (arguments.Length == 0)
            Communicator.NoItemName(command, "item");
        else
            Executor.GatherItemByName(arguments, GatheringType.采矿工);
    }

    private void OnGatherFish(string command, string arguments)
    {
        if (arguments.Length == 0)
            Communicator.NoItemName(command, "fish");
        else
            Executor.GatherFishByName(arguments);
    }

    private void OnGatherGroup(string command, string arguments)
    {
        if (arguments.Length == 0)
        {
            Communicator.Print(GatherGroupManager.CreateHelp());
            return;
        }

        var argumentParts = arguments.Split();
        var minute = (Time.EorzeaMinuteOfDay + (argumentParts.Length < 2 ? 0 : int.TryParse(argumentParts[1], out var offset) ? offset : 0))
          % RealTime.MinutesPerDay;
        if (!GatherGroupManager.TryGetValue(argumentParts[0], out var group))
        {
            Communicator.NoGatherGroup(argumentParts[0]);
            return;
        }

        var node = group.CurrentNode((uint)minute);
        if (node == null)
        {
            Communicator.NoGatherGroupItem(argumentParts[0], minute);
        }
        else
        {
            if (node.Annotation.Any())
                Communicator.Print(node.Annotation);
            if (node.PreferLocation == null)
                Executor.GatherItem(node.Item);
            else
                Executor.GatherLocation(node.PreferLocation);
        }
    }

    private void OnGatherBuddyShort(string command, string arguments)
    {
        switch (arguments.ToLowerInvariant())
        {
            case "window":
                Config.ShowGatherWindow = !Config.ShowGatherWindow;
                break;
            case "alarm":
                if (Config.AlarmsEnabled)
                    AlarmManager.Disable();
                else
                    AlarmManager.Enable();
                break;
            case "spear":
                Config.ShowSpearfishHelper = !Config.ShowSpearfishHelper;
                break;
            case "fish":
                Config.ShowFishTimer = !Config.ShowFishTimer;
                break;
            case "edit":
                if (!Config.FishTimerEdit)
                {
                    Config.ShowFishTimer = true;
                    Config.FishTimerEdit = true;
                }
                else
                {
                    Config.FishTimerEdit = false;
                }

                break;
            case "unlock":
                Config.MainWindowLockPosition = false;
                Config.MainWindowLockResize   = false;
                break;
            case "collect":
                CollectableManager.Start();
                return;
            case "collectstop":
                CollectableManager.Stop();
                return;
            default:
                var shortHelpString = new SeStringBuilder().AddText("使用 ").AddColoredText(command, Config.SeColorCommands)
                    .AddText(" 搭配以下参数:\n")
                    .AddColoredText("        window", Config.SeColorArguments).AddText(" - 切换采集窗口开启状态\n")
                    .AddColoredText("        alarm",  Config.SeColorArguments).AddText(" - 切换闹钟开启状态\n")
                    .AddColoredText("        spear",  Config.SeColorArguments).AddText(" - 切换刺鱼助手开启状态\n")
                    .AddColoredText("        fish",   Config.SeColorArguments).AddText(" - 切换捕鱼计时器开启状态\n")
                    .AddColoredText("        edit",   Config.SeColorArguments).AddText(" - 切换捕鱼计时器至编辑状态\n")
                    .AddColoredText("        unlock", Config.SeColorArguments).AddText(" - 解锁主窗口位置与大小")
                    .BuiltString;
                Communicator.Print(shortHelpString);
                return;
        }

        Config.Save();
    }

    private void OnVulcan(string command, string arguments)
    {
        var trimmed = arguments.Trim();
        if (trimmed.Length == 0)
        {
            _vulcanWindow?.Toggle();
            return;
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals("craft", StringComparison.OrdinalIgnoreCase))
        {
            OnVulcanCraft(parts);
            return;
        }

        _vulcanWindow?.OpenToList(trimmed);
    }

    private void OnVendor(string command, string arguments)
        => _vulcanWindow?.OpenToVendors();

    private void OnVulcanMarketboard(string command, string arguments)
        => _vulcanWindow?.OpenToMarketboard();

    private void OnCollectablesWindow(string command, string arguments)
        => CollectablesWindow?.Open();

    private void OnVulcanCraft(string[] parts)
    {
        if (parts.Length < 2)
        {
            Communicator.Print("/vulcan craft <配方 ID | 物品名称> [数量]");
            return;
        }

        var rest = parts[1..];
        int quantity = 1;
        string recipeArg;

        if (rest.Length >= 2 && int.TryParse(rest[^1], out var qty) && qty > 0)
        {
            quantity = qty;
            recipeArg = string.Join(" ", rest[..^1]);
        }
        else
        {
            recipeArg = string.Join(" ", rest);
        }

        Lumina.Excel.Sheets.Recipe? recipe = null;
        if (uint.TryParse(recipeArg, out var recipeId))
        {
            recipe = RecipeManager.GetRecipe(recipeId);
            if (recipe == null)
            {
                Communicator.Print($"未找到 ID 为 {recipeId} 的配方。");
                return;
            }
        }
        else
        {
            var matches = RecipeManager.FindByItemName(recipeArg);
            if (matches.Count == 0)
            {
                Communicator.Print($"未找到与 {recipeArg} 匹配的配方。");
                return;
            }

            if (matches.Count > 1)
            {
                Communicator.Print($"多个配方匹配 {recipeArg} , 使用配方 ID:");
                var classJobSheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
                foreach (var m in matches)
                {
                    var jobAbbr = classJobSheet?.GetRow(m.CraftType.RowId + 8).Abbreviation.ExtractText() ?? "??";
                    Communicator.Print($"  {m.ItemResult.Value.Name.ExtractText()} [{jobAbbr}] - ID: {m.RowId}");
                }
                return;
            }

            recipe = matches[0];
        }

        var itemName = recipe.Value.ItemResult.Value.Name.ExtractText();
        var tempList = new CraftingListDefinition
        {
            ID   = -1,
            Name = $"Command: {itemName} x{quantity}",
        };
        tempList.Recipes.Add(new CraftingListItem(recipe.Value.RowId, quantity));

        GatherBuddy.Log.Information($"[Commands] /vulcan craft: {itemName} x{quantity} (recipe {recipe.Value.RowId})");
        Communicator.Print($"开始制作: {itemName} x{quantity}");
        _vulcanWindow?.StartCraftingList(tempList);
    }

    private static void OnGatherDebug(string command, string arguments)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subcommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        
        Communicator.Print($"[Debug] 子命令: {subcommand} , 参数: '{arguments}'");
        
        switch (subcommand)
        {
            case "findrecipe":
                if (parts.Length < 2)
                {
                    Communicator.Print("用法: /gatherdebug findrecipe <物品名称>\n" +
                        "示例: /gatherdebug findrecipe 收藏用无花果");
                    return;
                }
                
                var searchName = string.Join(" ", parts.Skip(1));
                var recipeSheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
                if (recipeSheet == null)
                {
                    Communicator.Print("加载配方表失败");
                    return;
                }
                
                var matches = recipeSheet
                    .Where(r => r.ItemResult.Value.Name.ExtractText()
                        .Contains(searchName, StringComparison.OrdinalIgnoreCase))
                    .Take(10)
                    .ToList();
                
                if (matches.Count == 0)
                {
                    Communicator.Print($"未找到匹配的配方: '{searchName}'");
                }
                else
                {
                    Communicator.Print($"找到 {matches.Count} 个匹配 '{searchName}' 的配方:");
                    foreach (var recipe in matches)
                    {
                        var itemName = recipe.ItemResult.Value.Name.ExtractText();
                        Communicator.Print($"  {itemName} - 配方 ID: {recipe.RowId}");
                    }
                }
                return;
                
            case "recipenote":
                if (parts.Length < 3)
                {
                    Communicator.Print("用法: /gatherdebug recipenote <材料索引> <点击次数> [hq] [打开|配方ID]\n" +
                        "示例: /gatherdebug recipenote 0 5 hq       (点击索引材料0, 点击5次HQ)\n" +
                        "示例: /gatherdebug recipenote 0 5 hq open  (打开制作笔记, 然后点击)\n" +
                        "示例: /gatherdebug recipenote 0 5 hq 30503 (打开配方 30503, 然后点击)");
                    return;
                }
                
                if (!uint.TryParse(parts[1], out var index))
                {
                    Communicator.Print($"无效的材料索引: {parts[1]}");
                    return;
                }
                
                if (!int.TryParse(parts[2], out var count))
                {
                    Communicator.Print($"无效的点击次数: {parts[2]}");
                    return;
                }
                
                var isHQ = parts.Length > 3 && parts[3].Equals("hq", StringComparison.OrdinalIgnoreCase);
                var autoOpen = parts.Any(p => p.Equals("open", StringComparison.OrdinalIgnoreCase));
                
                uint recipeId = 0;
                // Check if last arg is a recipe ID (number > 1000)
                if (parts.Length > 3)
                {
                    var lastArg = parts[^1];
                    if (uint.TryParse(lastArg, out var id) && id > 1000)
                    {
                        recipeId = id;
                        autoOpen = true; // Implies open
                    }
                }
                
                Crafting.CraftingGameInterop.DebugClickRecipeNote(index, count, isHQ, autoOpen, recipeId);
                return;
                
            case "wary":
                Dalamud.ToastGui.ShowQuest("The fish have become wary of your presence. It might be time to shift your position...");
                Communicator.Print("Debug: Triggered 'wary' quest toast [EN] (ID 5517)");
                break;
            case "amiss":
                Dalamud.ToastGui.ShowQuest("The fish sense something amiss. Perhaps it is time to try another location.");
                Communicator.Print("Debug: Triggered 'amiss' quest toast [EN] (ID 3516)");
                break;
            case "wary-de":
                Dalamud.ToastGui.ShowQuest("Die Fische in der Umgebung sind auf dich aufmerksam geworden. Besser, du wechselst den Ort ...");
                Communicator.Print("Debug: Triggered 'wary' quest toast [DE] (ID 5517)");
                break;
            case "amiss-de":
                Dalamud.ToastGui.ShowQuest("Die Fische sind misstrauisch und kommen keinen Ilm näher. Versuch es lieber an einer anderen Stelle.");
                Communicator.Print("Debug: Triggered 'amiss' quest toast [DE] (ID 3516)");
                break;
            case "wary-fr":
                Dalamud.ToastGui.ShowQuest("Les poissons des environs commencent à se méfier de vous. Il est temps d'aller voir ailleurs...");
                Communicator.Print("Debug: Triggered 'wary' quest toast [FR] (ID 5517)");
                break;
            case "amiss-fr":
                Dalamud.ToastGui.ShowQuest("Les poissons sont devenus méfiants. Vous devriez aller pêcher dans un autre endroit.");
                Communicator.Print("Debug: Triggered 'amiss' quest toast [FR] (ID 3516)");
                break;
            case "wary-jp":
                Dalamud.ToastGui.ShowQuest("周辺の魚が警戒し始めている。そろそろ移動した方が良さそうだ……");
                Communicator.Print("Debug: Triggered 'wary' quest toast [JP] (ID 5517)");
                break;
            case "amiss-jp":
                Dalamud.ToastGui.ShowQuest("魚たちに警戒されてしまったようだ……。少し場所を変えたほうがいいだろう。");
                Communicator.Print("Debug: Triggered 'amiss' quest toast [JP] (ID 3516)");
                break;
            case "wary-cn":
                Dalamud.ToastGui.ShowQuest("附近的鱼已经有所警惕了。最好换个位置试试。");
                Communicator.Print("Debug: Triggered 'wary' quest toast [CN] (ID 5517)");
                break;
            case "amiss-cn":
                Dalamud.ToastGui.ShowQuest("这里的鱼现在警惕性很高，看来还是换个地点比较好。");
                Communicator.Print("Debug: Triggered 'amiss' quest toast [CN] (ID 3516)");
                break;
            case "repair":
                Communicator.Print("[Debug] 强制进入修理模式进行测试...");
                Crafting.CraftingGatherBridge.TestRepairSystem();
                break;
            case "repairstop":
                Communicator.Print("[Debug] 停止修理测试...");
                Crafting.CraftingGatherBridge.StopQueue();
                Crafting.CraftingTasks.StopNavigation();
                break;
            case "repairnpcs":
                var repairNPCs = Crafting.RepairNPCHelper.RepairNPCs;
                if (repairNPCs.Count == 0)
                {
                    Communicator.Print("[Debug] 未找到修理 NPC, 请先执行: /gatherdebug repairpopulate");
                }
                else
                {
                    Communicator.Print($"[Debug] 找到 {repairNPCs.Count} 个修理 NPC:");
                    foreach (var npc in repairNPCs.Take(10))
                    {
                        var territorySheet = Dalamud.GameData.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
                        var territory = territorySheet?.GetRow(npc.TerritoryType);
                        var placeName = territory?.PlaceName.ValueNullable?.Name.ExtractText() ?? "未知";
                        Communicator.Print($"  {npc.Name} - {placeName} ({npc.TerritoryType})");
                    }
                    if (repairNPCs.Count > 10)
                        Communicator.Print($"  ... 以及另外 {repairNPCs.Count - 10} 个");
                }
                break;
            case "repairpopulate":
                Communicator.Print("[Debug] 正在填充修理 NPC (可能需要一些时间)...");
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        Crafting.RepairNPCHelper.PopulateRepairNPCs();
                        Communicator.Print($"[Debug] 已填充 {Crafting.RepairNPCHelper.RepairNPCs.Count} 个修理 NPC");
                    }
                    catch (Exception ex)
                    {
                        Communicator.Print($"[Debug] 错误: {ex.Message}");
                    }
                });
                break;
            default:
                DebugMode = !DebugMode;
                break;
        }
    }
}
