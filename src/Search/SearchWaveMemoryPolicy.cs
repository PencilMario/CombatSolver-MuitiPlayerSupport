namespace CombatSolver;

/// <summary>Pure admission arithmetic; Runtime owns the source of the remaining budget.</summary>
internal static class SearchWaveMemoryPolicy
{
    /// <summary>Parent reservation cap; the remaining allocation budget may reduce it.</summary>
    public static int MaximumQueuedParents(int degreeOfParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);
        return checked(degreeOfParallelism * 2);
    }

    /// <summary>Doubles the adaptive capacity without overflowing or overshooting its cap.</summary>
    public static int GrowCapacity(int current, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(current);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        return current >= maximum - current ? maximum : current * 2;
    }

    public static int Capacity(int desiredCapacity, long parentReserveBytes, long remainingBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(desiredCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentReserveBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(remainingBytes);
        return (int)Math.Min(desiredCapacity, remainingBytes / parentReserveBytes);
    }

    public static long Reserve(long observedBytes, int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        long margin = observedBytes / 2;
        long perParent = observedBytes > long.MaxValue - margin
            ? long.MaxValue
            : observedBytes + margin;
        return count == 0 ? 0
            : perParent > long.MaxValue / count ? long.MaxValue
            : perParent * count;
    }
}
