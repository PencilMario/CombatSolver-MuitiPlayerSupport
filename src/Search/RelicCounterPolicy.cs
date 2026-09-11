namespace CombatSolver;

internal enum RelicCounterId
{
    HappyFlower, FakeHappyFlower, Pendulum, PollinousCore, PenNib,
    Nunchaku, TuningFork, JossPaper, IronClub, GalacticDust,
}

internal sealed record RelicCounterRule(RelicCounterId Id, bool Enabled, int Minimum, int Maximum, int HpAllowance);
internal readonly record struct RelicCounterTarget(RelicCounterId Id, int Minimum, int Maximum, int HpAllowance, int Period);
internal readonly record struct RelicCounterEvaluation(ulong SatisfiedMask, ulong TargetMask, int HpCredit, int Distance)
{
    // Ten built-in counters have periods of at most ten; four bits preserve each
    // route's exact ending value without allocating a collection per search node.
    public ulong CounterValues { get; init; }
    public ulong TargetMinimums { get; init; }
    public ulong TargetMaximums { get; init; }
    public int Value(RelicCounterId id) => (int)((CounterValues >> ((int)id * 4)) & 15);
    public int Minimum(RelicCounterId id) => (int)((TargetMinimums >> ((int)id * 4)) & 15);
    public int Maximum(RelicCounterId id) => (int)((TargetMaximums >> ((int)id * 4)) & 15);
    public bool Satisfied => SatisfiedMask == TargetMask;
    public int SatisfiedCount => System.Numerics.BitOperations.PopCount(SatisfiedMask);
}

internal static class RelicCounterPolicy
{
    public static RelicCounterRule[] ValidateAndCopy(IEnumerable<RelicCounterRule> rules)
    {
        var result = rules.ToArray();
        HashSet<RelicCounterId> ids = [];
        foreach (var rule in result)
        {
            if (!Enum.IsDefined(rule.Id) || !ids.Add(rule.Id) || rule.Minimum < 0
                || rule.Maximum < rule.Minimum || rule.Maximum > 1000 || rule.HpAllowance is < 0 or > 1000)
                throw new ArgumentException("Invalid relic counter policy.", nameof(rules));
        }
        return result;
    }

    public static RelicCounterEvaluation Add(RelicCounterEvaluation evaluation, RelicCounterTarget target, int value)
    {
        ulong bit = 1UL << (int)target.Id;
        bool satisfied = value >= target.Minimum && value <= target.Maximum;
        int distance = satisfied ? 0 : value < target.Minimum ? target.Minimum - value : target.Period - value + target.Minimum;
        return new(evaluation.SatisfiedMask | (satisfied ? bit : 0), evaluation.TargetMask | bit,
            evaluation.HpCredit + (satisfied ? target.HpAllowance : 0), evaluation.Distance + distance)
        {
            CounterValues = (evaluation.CounterValues & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)value << ((int)target.Id * 4)),
            TargetMinimums = (evaluation.TargetMinimums & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)target.Minimum << ((int)target.Id * 4)),
            TargetMaximums = (evaluation.TargetMaximums & ~(15UL << ((int)target.Id * 4)))
                | ((ulong)target.Maximum << ((int)target.Id * 4)),
        };
    }
}
