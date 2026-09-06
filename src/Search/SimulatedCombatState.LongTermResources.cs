namespace CombatSolver;

internal sealed partial class SimulatedCombatState
{
    private int _longTermResourceValue;
    private int _angerCopiesGenerated;
    private int _deathSaveRelicHpRestored;

    public int LongTermResourceValue => _longTermResourceValue;
    public int AngerCopiesGenerated => _angerCopiesGenerated;

    /// <summary>
    /// HP a one-shot death-save relic put back on this route: currently only Lizard Tail.
    /// </summary>
    /// <remarks>
    /// The player really does get this HP, but it is not HP the route <em>earned</em>: the relic is a
    /// cross-combat resource that is gone afterwards, and the same revive would have been available in every
    /// later fight. Scoring takes it back out and charges a premium on top; see
    /// <see cref="ActEndingBossPolicy.DeathSaveRelicPremium"/>.
    /// </remarks>
    public int DeathSaveRelicHpRestored => _deathSaveRelicHpRestored;

    public void RecordLongTermResource(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "长期资源增量必须为正数。");
        _longTermResourceValue = checked(_longTermResourceValue + value);
    }

    public void RecordAngerCopyGenerated()
        => _angerCopiesGenerated = checked(_angerCopiesGenerated + 1);

    public void RecordDeathSaveRelicHpRestored(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "保命遗物的回复量必须为正数。");
        _deathSaveRelicHpRestored = checked(_deathSaveRelicHpRestored + amount);
    }
}
