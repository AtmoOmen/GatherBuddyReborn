using System;
using System.Collections.Generic;
using GatherBuddy.Vulcan;

namespace GatherBuddy.Crafting;

public record RaphaelSolveRequest(
    uint RecipeId,
    int Level,
    int Craftsmanship,
    int Control,
    int CP,
    bool Manipulation,
    bool Specialist,
    int InitialQuality = 0
)
{
    public static RaphaelSolveRequest FromCraftState(CraftState craft, bool allowSpecialistActions)
        => new(
            RecipeId: craft.RecipeId,
            Level: craft.StatLevel,
            Craftsmanship: craft.StatCraftsmanship,
            Control: craft.StatControl,
            CP: craft.StatCP,
            Manipulation: craft.UnlockedManipulation,
            Specialist: allowSpecialistActions && craft.Specialist,
            InitialQuality: craft.InitialQuality
        );

    public string GetKey()
    {
        return $"{RecipeId}/{Level}/{Craftsmanship}/{Control}/{CP}/{(Manipulation ? "1" : "0")}/{(Specialist ? "1" : "0")}/{InitialQuality}";
    }
}

public class CachedRaphaelSolution
{
    public string Key { get; set; } = string.Empty;
    public RaphaelSolveRequest Request { get; set; } = null!;
    public List<uint> ActionIds { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public bool IsFailed { get; set; }
    public string? FailureReason { get; set; }

    public CachedRaphaelSolution() { }

    public CachedRaphaelSolution(string key, RaphaelSolveRequest request)
    {
        Key = key;
        Request = request;
        GeneratedAt = DateTime.UtcNow;
    }
}

public enum RaphaelSolvePriority
{
    Background,
    Urgent,
}

public enum VulcanSolverMode
{
    PureRaphael,     // Static Raphael rotations only
    StandardSolver,  // Dynamic standard solver
    ProgressOnly,    // Progress-only solver (no quality actions)
}

public class RaphaelSolveCoordinatorConfig
{
    public bool RaphaelEnabled { get; set; } = true;
    public int MaxConcurrentRaphaelProcesses { get; set; } = 1;
    public int RaphaelTimeoutMinutes { get; set; } = 5;
    public bool RaphaelBackloadProgress { get; set; } = false;
    public bool RaphaelAllowSpecialistActions { get; set; } = false;
    public bool AutoClearSolutionCache { get; set; } = true;
    public int SolutionCacheMaxAgeDays { get; set; } = 30;
    public VulcanSolverMode SolverMode { get; set; } = VulcanSolverMode.PureRaphael;
}
