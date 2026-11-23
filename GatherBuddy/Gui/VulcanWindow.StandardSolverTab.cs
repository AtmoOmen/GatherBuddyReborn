using Dalamud.Interface;
using GatherBuddy.RebornUI.Settings;
using RebornMaterial;
using System;

namespace GatherBuddy.Gui;

public partial class VulcanWindow
{
    private static readonly string[] CollectibleModeNames = ["第一档（最低）", "第二档（中等）", "第三档（最高）"];

    private static void DrawStandardSolverPage()
    {
        var config = GatherBuddy.Config.StandardSolverConfig;

        using (M3Card.Begin("standard_solver_tricks", "秘诀", FontAwesomeIcon.Magic, M3.AccentFor(0)))
        {
            SettingRows.Switch("在高品质状态下使用", "制作状态为「高品质」时使用「秘诀」。",
                config.UseTricksGood, v => config.UseTricksGood = v);
            SettingRows.Switch("在最高品质状态下使用", "制作状态为「最高品质」时使用「秘诀」。",
                config.UseTricksExcellent, v => config.UseTricksExcellent = v);
        }

        using (M3Card.Begin("standard_solver_quality", "品质", FontAwesomeIcon.Star, M3.AccentFor(1)))
        {
            SettingRows.Slider("目标 HQ 概率", "普通制作要达到的 HQ 概率。",
                config.MaxPercentage, 0, 100, v => config.MaxPercentage = v);
            SettingRows.Switch("以「闲静」起手", "首个工次使用「闲静」提高品质。关闭时使用「坚信」推进进展。",
                config.UseQualityStarter, v => config.UseQualityStarter = v);
            SettingRows.Slider("使用「坯料加工」的内静层数上限", "内静层数不超过该值时可使用「坯料加工」。",
                config.MaxIQPrepTouch, 0, 10, v => config.MaxIQPrepTouch = v);
        }

        using (M3Card.Begin("standard_solver_collectables", "收藏品", FontAwesomeIcon.Gem, M3.AccentFor(2)))
        {
            var mode = Math.Clamp(config.SolverCollectibleMode - 1, 0, CollectibleModeNames.Length - 1);
            SettingRows.Combo("目标收藏价值档位", "选择要达到的收藏价值档位。", mode, CollectibleModeNames,
                i => config.SolverCollectibleMode = i + 1);
        }

        using (M3Card.Begin("standard_solver_specialist", "专家", FontAwesomeIcon.Award, M3.AccentFor(0)))
        {
            SettingRows.Switch("使用专家技能", "可用时使用「设计变动」和「专心致志」。",
                config.UseSpecialist, v => config.UseSpecialist = v);
        }

        using (M3Card.Begin("standard_solver_miracle", "奇迹之材", FontAwesomeIcon.Gift, M3.AccentFor(1)))
        {
            SettingRows.Switch("使用奇迹之材", "允许在制作中使用「奇迹之材」。",
                config.UseMaterialMiracle, v => config.UseMaterialMiracle = v);
            if (config.UseMaterialMiracle)
            {
                using var sub = M3SubGroup.Begin();
                SettingRows.Slider("使用「奇迹之材」前的最少工次", "达到指定工次后才使用「奇迹之材」。",
                    config.MinimumStepsBeforeMiracle, 1, 10, v => config.MinimumStepsBeforeMiracle = v);
                SettingRows.Switch("允许多次使用奇迹之材", "允许在同一次制作中多次使用「奇迹之材」。",
                    config.MaterialMiracleMulti, v => config.MaterialMiracleMulti = v);
            }
        }

        if (M3Widgets.RowHoldButton("重置标准求解器设置", "恢复默认值",
                "长按确认，将本页全部设置恢复为默认值。", 0.6f, M3ButtonStyle.Outlined, FontAwesomeIcon.Undo))
        {
            GatherBuddy.Config.StandardSolverConfig = new Vulcan.StandardSolverConfig();
            GatherBuddy.Config.Save();
            M3Snackbar.Show("已恢复标准求解器的默认设置。");
        }
    }
}
