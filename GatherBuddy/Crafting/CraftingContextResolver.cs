using FFXIVClientStructs.FFXIV.Client.Game;
using GatherBuddy.Vulcan;
using Lumina.Excel.Sheets;

namespace GatherBuddy.Crafting;

public enum CraftingStatsSource
{
    PreferCurrentJobStats,
    AlwaysGearsetStats,
}

public sealed record CraftingExecutionContext(
    RecipeCraftSettings? ConsumableSettings,
    CraftingQualityPolicy QualityPolicy,
    VulcanSolverMode EffectiveSolverMode,
    bool ForceProgressOnlyUnlockCraft,
    bool HasCraftedBefore,
    bool UseQuickSynthesis,
    string? SelectedMacroId
);

public sealed record CraftingSimulationContext(
    CraftingExecutionContext ExecutionContext,
    GameStateBuilder.PlayerStats Stats,
    CraftState SimulationState,
    RaphaelSolveRequest RaphaelRequest
);

public static class CraftingContextResolver
{
    public static CraftingExecutionContext ResolveExecutionContext(CraftingListItem item, Recipe recipe, CraftingListConsumableSettings? listConsumables)
    {
        var consumableSettings = BuildConsumableSettings(item, listConsumables);
        var qualityPolicy = GetQualityPolicy(item, recipe);
        var hasCraftedBefore = HasRecipeCraftedBefore(recipe);
        var useQuickSynthesis = item.Options.NQOnly && recipe.CanQuickSynth && hasCraftedBefore;
        var forceProgressOnlyUnlockCraft = item.Options.NQOnly
            && recipe.CanQuickSynth
            && !hasCraftedBefore
            && qualityPolicy.OverrideMode == CraftingQualityOverrideMode.RequireNQOnly;
        var craftSolverOverride = forceProgressOnlyUnlockCraft
            ? SolverOverrideMode.ProgressOnlySolver
            : item.CraftSettings?.SolverOverride ?? SolverOverrideMode.Default;
        var effectiveSolverMode = craftSolverOverride switch
        {
            SolverOverrideMode.StandardSolver => VulcanSolverMode.StandardSolver,
            SolverOverrideMode.RaphaelSolver => VulcanSolverMode.PureRaphael,
            SolverOverrideMode.ProgressOnlySolver => VulcanSolverMode.ProgressOnly,
            _ => GatherBuddy.Config.RaphaelSolverConfig.SolverMode,
        };
        var selectedMacroId = forceProgressOnlyUnlockCraft ? null : item.CraftSettings?.SelectedMacroId;
        return new(
            consumableSettings,
            qualityPolicy,
            effectiveSolverMode,
            forceProgressOnlyUnlockCraft,
            hasCraftedBefore,
            useQuickSynthesis,
            selectedMacroId);
    }

    public static bool TryBuildSimulationContext(
        CraftingListItem item,
        Recipe recipe,
        CraftingListConsumableSettings? listConsumables,
        CraftingStatsSource statsSource,
        out CraftingSimulationContext context)
    {
        var executionContext = ResolveExecutionContext(item, recipe, listConsumables);
        return TryBuildSimulationContext(recipe, executionContext, statsSource, out context);
    }

    public static bool TryBuildSimulationContext(
        Recipe recipe,
        CraftingExecutionContext executionContext,
        CraftingStatsSource statsSource,
        out CraftingSimulationContext context)
    {
        context = null!;

        var requiredJob = (uint)(recipe.CraftType.RowId + 8);
        var stats = ResolvePlayerStats(requiredJob, executionContext.ConsumableSettings, statsSource);
        if (stats == null)
            return false;

        var initialQuality = executionContext.QualityPolicy.CalculateGuaranteedInitialQuality(recipe);
        var craft = GameStateBuilder.BuildCraftState(CraftingStateBuilder.BuildRecipeInfo(recipe), stats) with { InitialQuality = initialQuality };
        var request = RaphaelSolveRequest.FromCraftState(craft, GatherBuddy.Config.RaphaelSolverConfig.RaphaelAllowSpecialistActions);
        context = new(executionContext, stats, craft, request);
        return true;
    }

    public static bool HasRecipeCraftedBefore(Recipe recipe)
    {
        if (recipe.SecretRecipeBook.RowId > 0)
            return true;
        return QuestManager.IsRecipeComplete(recipe.RowId);
    }

    public static CraftingQualityPolicy GetQualityPolicy(CraftingListItem item, Recipe recipe)
    {
        item.QualityPolicy ??= CraftingQualityPolicyResolver.Resolve(recipe, item.CraftSettings);
        if (item.IngredientPreferences.Count == 0)
            item.IngredientPreferences = item.QualityPolicy.BuildGuaranteedHQPreferences();
        return item.QualityPolicy;
    }

    public static RecipeCraftSettings? BuildConsumableSettings(CraftingListItem item, CraftingListConsumableSettings? listConsumables)
    {
        if (listConsumables == null && !item.ConsumableOverrides.HasAnyOverrides() && item.CraftSettings == null)
            return null;

        var foodItemId = listConsumables?.FoodItemId;
        var foodHQ = listConsumables?.FoodHQ ?? false;
        var medicineItemId = listConsumables?.MedicineItemId;
        var medicineHQ = listConsumables?.MedicineHQ ?? false;
        var manualItemId = listConsumables?.ManualItemId;
        var squadronManualItemId = listConsumables?.SquadronManualItemId;

        if (item.CraftSettings != null && item.CraftSettings.HasAnySettings())
        {
            var craftSettings = item.CraftSettings;
            var effectiveFoodMode = craftSettings.FoodMode == ConsumableOverrideMode.Inherit && craftSettings.FoodItemId.HasValue
                ? ConsumableOverrideMode.Specific
                : craftSettings.FoodMode;
            var effectiveMedicineMode = craftSettings.MedicineMode == ConsumableOverrideMode.Inherit && craftSettings.MedicineItemId.HasValue
                ? ConsumableOverrideMode.Specific
                : craftSettings.MedicineMode;
            var effectiveManualMode = craftSettings.ManualMode == ConsumableOverrideMode.Inherit && craftSettings.ManualItemId.HasValue
                ? ConsumableOverrideMode.Specific
                : craftSettings.ManualMode;
            var effectiveSquadronMode = craftSettings.SquadronManualMode == ConsumableOverrideMode.Inherit && craftSettings.SquadronManualItemId.HasValue
                ? ConsumableOverrideMode.Specific
                : craftSettings.SquadronManualMode;

            ApplyOverride(new ConsumableOverride { Mode = effectiveFoodMode, ItemId = craftSettings.FoodItemId, HQ = craftSettings.FoodHQ }, ref foodItemId, ref foodHQ);
            ApplyOverride(new ConsumableOverride { Mode = effectiveMedicineMode, ItemId = craftSettings.MedicineItemId, HQ = craftSettings.MedicineHQ }, ref medicineItemId, ref medicineHQ);
            ApplyOverride(new ConsumableOverride { Mode = effectiveManualMode, ItemId = craftSettings.ManualItemId }, ref manualItemId);
            ApplyOverride(new ConsumableOverride { Mode = effectiveSquadronMode, ItemId = craftSettings.SquadronManualItemId }, ref squadronManualItemId);
        }
        else
        {
            ApplyOverride(item.ConsumableOverrides.Food, ref foodItemId, ref foodHQ);
            ApplyOverride(item.ConsumableOverrides.Medicine, ref medicineItemId, ref medicineHQ);
            ApplyOverride(item.ConsumableOverrides.Manual, ref manualItemId);
            ApplyOverride(item.ConsumableOverrides.SquadronManual, ref squadronManualItemId);
        }

        if (!foodItemId.HasValue && !medicineItemId.HasValue && !manualItemId.HasValue && !squadronManualItemId.HasValue)
            return null;

        return new RecipeCraftSettings
        {
            FoodItemId = foodItemId,
            FoodHQ = foodHQ,
            MedicineItemId = medicineItemId,
            MedicineHQ = medicineHQ,
            ManualItemId = manualItemId,
            SquadronManualItemId = squadronManualItemId,
        };
    }

    private static GameStateBuilder.PlayerStats? ResolvePlayerStats(uint requiredJob, RecipeCraftSettings? consumableSettings, CraftingStatsSource statsSource)
    {
        if (statsSource == CraftingStatsSource.AlwaysGearsetStats)
        {
            var stats = GearsetStatsReader.ReadGearsetStatsForJob(requiredJob);
            var projectedConsumables = ConsumableChecker.GetProjectedCraftStatConsumables(consumableSettings);
            if (stats != null && projectedConsumables != null)
                stats = GearsetStatsReader.ApplyConsumablesToStats(stats, projectedConsumables);
            return stats;
        }

        var currentJob = Dalamud.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
        if (currentJob == requiredJob)
        {
            var stats = CraftingStateBuilder.GetCurrentPlayerStats();
            var pendingConsumables = ConsumableChecker.GetPendingCraftStatConsumables(consumableSettings);
            if (stats != null && pendingConsumables != null)
                stats = GearsetStatsReader.ApplyConsumablesToStats(stats, pendingConsumables);
            return stats;
        }

        var gearsetStats = GearsetStatsReader.ReadGearsetStatsForJob(requiredJob);
        var projected = ConsumableChecker.GetProjectedCraftStatConsumables(consumableSettings);
        if (gearsetStats != null && projected != null)
            gearsetStats = GearsetStatsReader.ApplyConsumablesToStats(gearsetStats, projected);
        return gearsetStats;
    }

    private static void ApplyOverride(ConsumableOverride? overrideSetting, ref uint? itemId, ref bool hq)
    {
        if (overrideSetting == null)
            return;

        switch (overrideSetting.Mode)
        {
            case ConsumableOverrideMode.Inherit:
                return;
            case ConsumableOverrideMode.None:
                itemId = null;
                hq = false;
                return;
            case ConsumableOverrideMode.Specific:
                itemId = overrideSetting.ItemId;
                hq = overrideSetting.HQ;
                return;
        }
    }

    private static void ApplyOverride(ConsumableOverride? overrideSetting, ref uint? itemId)
    {
        if (overrideSetting == null)
            return;

        switch (overrideSetting.Mode)
        {
            case ConsumableOverrideMode.Inherit:
                return;
            case ConsumableOverrideMode.None:
                itemId = null;
                return;
            case ConsumableOverrideMode.Specific:
                itemId = overrideSetting.ItemId;
                return;
        }
    }
}
