using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver;

/// <summary>
/// How much the HP this fight costs actually matters to the run.
/// </summary>
internal enum BossHpRelief
{
    /// <summary>Normal fight: HP carries straight into the next one and is weighted in full.</summary>
    None,

    /// <summary>Clearing acts one and two restores 80% of the damage taken.</summary>
    ActClearHeal,

    /// <summary>Nothing follows this fight, so only surviving it matters.</summary>
    RunEnding,
}

internal static class ActEndingBossPolicy
{
    public static BossHpRelief ResolveStrategicHpRelief(
        BossHpRelief encounterHpRelief,
        BossHpStrategy actTransitionStrategy,
        BossHpStrategy finalBossStrategy)
        => encounterHpRelief switch
        {
            BossHpRelief.ActClearHeal when actTransitionStrategy == BossHpStrategy.MinimizeHpLoss
                => BossHpRelief.None,
            BossHpRelief.RunEnding when finalBossStrategy == BossHpStrategy.MinimizeHpLoss
                => BossHpRelief.None,
            _ => encounterHpRelief,
        };

    public static int RawHpRequiredForPersistentValue(
        int persistentHpValue,
        BossHpRelief bossHpRelief)
    {
        if (persistentHpValue <= 0)
            return 0;
        return bossHpRelief switch
        {
            BossHpRelief.ActClearHeal => persistentHpValue * 5,
            BossHpRelief.RunEnding => int.MaxValue / 4,
            _ => persistentHpValue,
        };
    }

    /// <summary>
    /// What burning a one-shot death-save relic costs a route, in the same units as its battle HP loss.
    /// </summary>
    /// <remarks>
    /// Lizard Tail revives the player at half their maximum HP, and that HP was free: nothing charged the
    /// route for spending the relic, so walking into lethal damage read as a large heal. Fairy in a Bottle
    /// does the same thing and never had this problem, because spending it goes through the potion
    /// accounting; the relic is the only unpriced death save in the game.
    ///
    /// The premium is a multiple of the restored HP rather than a match for it, because HP is not the only
    /// thing the revive buys. Enemy HP is worth <see cref="SolverWeights.EnemyHp"/> a point inside Beam
    /// ranking, so a two-boss fight puts a hundred-odd HP-equivalents of tempo on the table, and a
    /// one-to-one premium loses to it — measured, not assumed. At the current multiple every route that
    /// wins while alive beats every route that wins on the relic, while the charge stays orders of
    /// magnitude below <see cref="SolverWeights.VictoryBonus"/> and <see cref="SolverWeights.DeathPenalty"/>,
    /// so a route with no surviving alternative still spends the relic without hesitation.
    ///
    /// The run's last fight is the one exception. Saving the relic for later is worth nothing when there is
    /// no later, so the premium drops to zero and the revive goes back to being free.
    /// </remarks>
    public static int DeathSaveRelicPremium(int deathSaveRelicHpRestored, BossHpRelief bossHpRelief)
        => bossHpRelief == BossHpRelief.RunEnding
            ? 0
            : Math.Max(0, deathSaveRelicHpRestored)
                * SolverWeights.DeathSaveRelicPremiumPercent
                / 100;

    /// <summary>
    /// What a route's spent death saves cost inside Beam ranking, where the revive's own HP is still sitting
    /// in the node's projected HP: it is taken back out, and the premium is charged on top.
    /// </summary>
    public static int DeathSaveRelicBeamCost(int deathSaveRelicHpRestored, BossHpRelief bossHpRelief)
    {
        if (bossHpRelief == BossHpRelief.RunEnding)
            return 0;
        int restored = Math.Max(0, deathSaveRelicHpRestored);
        return restored + DeathSaveRelicPremium(restored, bossHpRelief);
    }

    public static BossHpRelief ResolveHpRelief(CombatState combatState)
    {
        if (combatState.Encounter?.RoomType != RoomType.Boss)
            return BossHpRelief.None;

        RunState runState = combatState.RunState as RunState
            ?? throw new InvalidOperationException("Boss 战没有可识别的 RunState。");
        if (runState.CurrentActIndex < runState.Acts.Count - 1)
            return BossHpRelief.ActClearHeal;

        // Final act. A single boss is the run's last fight; when the act has two, only the second one is,
        // and HP carries from the first into it exactly like a normal fight.
        return runState.Act.SecondBossEncounter is not { } second
            || second.Id == combatState.Encounter.Id
                ? BossHpRelief.RunEnding
                : BossHpRelief.None;
    }

    public static bool IsRecoveryFight(CombatState combatState)
        => ResolveHpRelief(combatState) != BossHpRelief.None;
}
