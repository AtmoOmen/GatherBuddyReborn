using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using GatherBuddy.Plugin;
using GatherBuddy.Vulcan;
using ImRaii = ElliLib.Raii.ImRaii;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private void DrawStandardSolverConfigTab()
    {
        IDisposable tabItem;
        bool tabOpen;
        
        if (GatherBuddy.ControllerSupport != null)
        {
        var handle = GatherBuddy.ControllerSupport.TabNavigation.TabItem("标准求解器##standardSolverTab", 4, 9);
            tabItem = handle;
            tabOpen = handle;
        }
        else
        {
            var handle = ImRaii.TabItem("标准求解器##standardSolverTab");
            tabItem = handle;
            tabOpen = handle.Success;
        }
        
        using (tabItem)
        {
            if (!tabOpen)
                return;

        var config = GatherBuddy.Config.StandardSolverConfig;

        ImGui.Text("标准求解器配置");
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "配置动态标准求解器行为");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("秘诀设置");
        ImGui.Spacing();
        
        var useTricksGood = config.UseTricksGood;
        if (ImGui.Checkbox("状态良好时使用秘诀", ref useTricksGood))
        {
            config.UseTricksGood = useTricksGood;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("状态为良好时使用秘诀");

        var useTricksExcellent = config.UseTricksExcellent;
        if (ImGui.Checkbox("状态高品质时使用秘诀", ref useTricksExcellent))
        {
            config.UseTricksExcellent = useTricksExcellent;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("状态为高品质时使用秘诀");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("品质设置");
        ImGui.Spacing();

        var maxPercentage = config.MaxPercentage;
        ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(150f));
        if (ImGui.SliderInt("目标 HQ %##maxPercentage", ref maxPercentage, 0, 100))
        {
            config.MaxPercentage = maxPercentage;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("普通制作的目标 HQ 百分比 (0-100)");

        var useQualityStarter = config.UseQualityStarter;
        if (ImGui.Checkbox("使用品质开场 (闲静)", ref useQualityStarter))
        {
            config.UseQualityStarter = useQualityStarter;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("开场使用闲静提升品质, 不使用坚信推进进度");

        var maxIQPrepTouch = config.MaxIQPrepTouch;
        ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(150f));
        if (ImGui.SliderInt("坯料加工最大内静层数##maxIQPrepTouch", ref maxIQPrepTouch, 0, 10))
        {
            config.MaxIQPrepTouch = maxIQPrepTouch;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("使用坯料加工前允许的最大内静层数");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("收藏品设置");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(200f));
        var collectibleModes = new[] { "1 档 (最低)", "2 档 (中等)", "3 档 (最高)" };
        var collectibleMode = Math.Clamp(config.SolverCollectibleMode - 1, 0, collectibleModes.Length - 1);
        if (ImGui.Combo("收藏品目标##collectibleMode", ref collectibleMode, collectibleModes, collectibleModes.Length))
        {
            config.SolverCollectibleMode = collectibleMode + 1;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("目标收藏品档位 (1=最低, 3=最高)");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("专家设置");
        ImGui.Spacing();

        var useSpecialist = config.UseSpecialist;
        if (ImGui.Checkbox("使用专家技能", ref useSpecialist))
        {
            config.UseSpecialist = useSpecialist;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("可用时使用专心观察和心血来潮");
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.BeginGroup();
        ImGui.Text("奇迹之材设置");
        ImGui.Spacing();

        var useMaterialMiracle = config.UseMaterialMiracle;
        if (ImGui.Checkbox("使用奇迹之材", ref useMaterialMiracle))
        {
            config.UseMaterialMiracle = useMaterialMiracle;
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("制作过程中使用奇迹之材");

        if (config.UseMaterialMiracle)
        {
            var minSteps = config.MinimumStepsBeforeMiracle;
            ImGui.SetNextItemWidth(VulcanUiScaling.Scaled(150f));
            if (ImGui.SliderInt("奇迹之材前最少步骤##minSteps", ref minSteps, 1, 10))
            {
                config.MinimumStepsBeforeMiracle = minSteps;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("使用奇迹之材前的最少制作步骤");

            var materialMiracleMulti = config.MaterialMiracleMulti;
            if (ImGui.Checkbox("允许多次奇迹之材", ref materialMiracleMulti))
            {
                config.MaterialMiracleMulti = materialMiracleMulti;
                GatherBuddy.Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("允许单次制作中多次使用奇迹之材");
        }
        ImGui.EndGroup();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("恢复默认", VulcanUiScaling.Scaled(200f, 0f)))
        {
            GatherBuddy.Config.StandardSolverConfig = new Vulcan.StandardSolverConfig();
            GatherBuddy.Config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("将所有标准求解器设置恢复为默认值");
        }
    }
}
