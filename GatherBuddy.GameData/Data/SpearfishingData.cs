namespace GatherBuddy.Data;

public static class SpearfishingData
{
    public static void Apply(GameData data)
    {
        ApplyShadowNodeRequirements(data);
    }

    private static void ApplyShadowNodeRequirements(GameData data)
    {
        foreach (var fish in data.Fishes.Values.Where(f => f.IsSpearFish && f.Predators.Length > 0))
        {
            foreach (var (requiredFish, requiredCount) in fish.Predators)
            {
                AddRequirement(data, fish.ItemId, requiredFish.ItemId, requiredCount);
            }
        }
    }

    private static void AddRequirement(GameData data, uint fishItemId, uint requiredFishItemId, int count)
    {
        if (!data.Fishes.TryGetValue(fishItemId, out var fish) || !fish.IsSpearFish)
            return;

        if (!data.Fishes.TryGetValue(requiredFishItemId, out var requiredFish) || !requiredFish.IsSpearFish)
            return;

        var parentSpot = requiredFish.FishingSpots.FirstOrDefault(f => f.Spearfishing && !f.IsShadowNode);

        foreach (var spot in fish.FishingSpots.Where(f => f.Spearfishing && f.IsShadowNode))
        {
            if (!spot.SpawnRequirements.Any(r => r.RequiredFish.ItemId == requiredFishItemId))
                spot.SpawnRequirements.Add(new Classes.SpawnRequirement(requiredFish, count));

            if (spot.ParentNode == null && parentSpot != null)
                spot.ParentNode = parentSpot;
        }
    }
}
