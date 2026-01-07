using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
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
            Executor.GatherItemByName(arguments, GatheringType.Botanist);
    }

    private void OnGatherMin(string command, string arguments)
    {
        if (arguments.Length == 0)
            Communicator.NoItemName(command, "item");
        else
            Executor.GatherItemByName(arguments, GatheringType.Miner);
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

    private static void OnGatherDebug(string command, string arguments)
    {
        switch (arguments.ToLowerInvariant())
        {
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
            default:
                DebugMode = !DebugMode;
                break;
        }
    }
}
