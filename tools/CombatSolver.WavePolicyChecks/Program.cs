using CombatSolver;

static void Equal(long expected, long actual)
{
    if (expected != actual)
        throw new InvalidOperationException($"Expected {expected}, observed {actual}.");
}

foreach (int parents in new[] { 8, 4, 2, 1, 0 })
    Equal(parents, SearchWaveMemoryPolicy.Capacity(8, 100, parents * 100));
Equal(3, SearchWaveMemoryPolicy.Capacity(8, 100, 399));
Equal(0, SearchWaveMemoryPolicy.Capacity(0, 100, 1000));
Equal(1, SearchWaveMemoryPolicy.Capacity(8, long.MaxValue, long.MaxValue));
Equal(150, SearchWaveMemoryPolicy.Reserve(100));
Equal(151, SearchWaveMemoryPolicy.Reserve(101));
Equal(1200, SearchWaveMemoryPolicy.Reserve(100, 8));
Equal(long.MaxValue, SearchWaveMemoryPolicy.Reserve(long.MaxValue));
Equal(long.MaxValue, SearchWaveMemoryPolicy.Reserve(long.MaxValue / 2, 4));
Equal(0, SearchWaveMemoryPolicy.Reserve(long.MaxValue, 0));

Random random = new(36);
for (int sample = 0; sample < 10_000; sample++)
{
    long reserve = random.NextInt64(1, long.MaxValue);
    long remaining = random.NextInt64(long.MaxValue);
    int desired = random.Next(1, 17);
    int accepted = SearchWaveMemoryPolicy.Capacity(desired, reserve, remaining);
    if (accepted < 0 || accepted > desired || (decimal)accepted * reserve > remaining)
        throw new InvalidOperationException("Admission exceeded its remaining allocation budget.");
    if (accepted < desired && (decimal)(accepted + 1) * reserve <= remaining)
        throw new InvalidOperationException("Admission failed to use a safe smaller wave.");
}
Console.WriteLine("PASS: remaining-budget admission, partial waves, zero capacity, overflow, 10000 bounded cases.");

Equal(16, SearchWaveMemoryPolicy.MaximumQueuedParents(8));
Equal(2, SearchWaveMemoryPolicy.MaximumQueuedParents(1));
Equal(0, SearchWaveMemoryPolicy.GrowCapacity(0, 1));
Equal(6, SearchWaveMemoryPolicy.GrowCapacity(3, 7));
Equal(7, SearchWaveMemoryPolicy.GrowCapacity(4, 7));
Equal(64, SearchWaveMemoryPolicy.GrowCapacity(48, 64));
Equal(int.MaxValue, SearchWaveMemoryPolicy.GrowCapacity(int.MaxValue, int.MaxValue));
Equal(1, SearchWaveMemoryPolicy.GrowCapacity(1, 1));
for (int sample = 0; sample < 10_000; sample++)
{
    int maximum = random.Next(1, int.MaxValue);
    int current = random.Next(0, int.MaxValue);
    Equal(Math.Min(maximum, 2L * current), SearchWaveMemoryPolicy.GrowCapacity(current, maximum));
}
Console.WriteLine("PASS: parent reservation cap, exact saturating growth, odd caps, zero and overflow.");
