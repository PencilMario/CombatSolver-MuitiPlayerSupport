namespace CombatSolver;

// The same 0/1, two-resource knapsack used by Snapshot. Storage is local to the
// evaluation; it never retains cards, simulators, or a previous search's capacity.
internal static class ReachableHandPotential
{
    internal static int Calculate(
        ReadOnlySpan<(int Energy, int Stars, int Value)> playable,
        int availableEnergy,
        int availableStars)
    {
        int totalEnergy = 0;
        int totalStars = 0;
        long totalValue = 0;
        foreach (var card in playable)
        {
            totalEnergy += card.Energy;
            totalStars += card.Stars;
            totalValue += card.Value;
        }
        int energyCapacity = Math.Min(Math.Max(0, availableEnergy), totalEnergy);
        int starCapacity = Math.Min(Math.Max(0, availableStars), totalStars);
        // Values and queried prices are nonnegative. When the whole hand fits,
        // every item can be taken; preserve the old integer-overflow behavior by
        // using the original recurrence if its sum cannot be represented.
        if (totalEnergy == energyCapacity && totalStars == starCapacity
            && totalValue <= int.MaxValue)
        {
            return (int)totalValue;
        }

        int stride = checked(starCapacity + 1);
        int cellCount = checked((energyCapacity + 1) * stride);
        Span<int> best = cellCount <= 512
            ? stackalloc int[cellCount]
            : new int[cellCount];
        if (cellCount <= 512)
            best.Clear();
        foreach (var card in playable)
        {
            for (int energy = energyCapacity; energy >= card.Energy; energy--)
            {
                int row = energy * stride;
                int previousRow = (energy - card.Energy) * stride;
                for (int stars = starCapacity; stars >= card.Stars; stars--)
                {
                    int index = row + stars;
                    best[index] = Math.Max(
                        best[index],
                        best[previousRow + stars - card.Stars] + card.Value);
                }
            }
        }
        return best[^1];
    }
}
