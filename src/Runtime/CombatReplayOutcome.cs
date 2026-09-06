using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CombatSolver;

internal sealed record RecordedPotionUse(string PotionId, uint? TargetCombatId, int Turn);
internal sealed record CombatReplayOutcomeSnapshot(
    int InitialHp, int FinalHp, int HpLost, int HpHealed, int SelfDamage,
    bool CombatEnded, bool Survived, int FinalEnemyHp, RecordedPotionUse[] Potions);

// Independent observation ledger; recording must not advance the solver's damage accounting.
internal sealed class CombatReplayOutcome : IDisposable
{
    private Creature? _player;
    private readonly int _initialHp;
    private int _hpLost;
    private int _hpHealed;
    private CombatReplayOutcomeSnapshot? _finished;

    public CombatReplayOutcome(CombatState state)
    {
        _player = state.Players.Single().Creature;
        _initialHp = _player.CurrentHp;
        _player.CurrentHpChanged += OnHpChanged;
    }

    private void OnHpChanged(int before, int after)
    {
        _hpLost += Math.Max(0, before - after);
        _hpHealed += Math.Max(0, after - before);
    }

    public CombatReplayOutcomeSnapshot Capture(CombatState state, bool ended)
    {
        if (_finished != null)
            return _finished;
        Creature player = _player ?? throw new InvalidOperationException("Outcome observation has been disposed.");
        var history = CombatManager.Instance.History.Entries;
        return new CombatReplayOutcomeSnapshot(
            _initialHp, player.CurrentHp, _hpLost, _hpHealed,
            history.OfType<DamageReceivedEntry>().Where(entry => ReferenceEquals(entry.Receiver, player)
                && ReferenceEquals(entry.Dealer, player)).Sum(entry => Math.Max(0, entry.Result.UnblockedDamage)),
            ended, player.CurrentHp > 0, state.Enemies.Sum(enemy => Math.Max(0, enemy.CurrentHp)),
            history.OfType<PotionUsedEntry>().Where(entry => ReferenceEquals(entry.Actor, player))
                .Select(entry => new RecordedPotionUse(entry.Potion.Id.Entry, entry.Target?.CombatId, entry.RoundNumber)).ToArray());
    }

    public void Complete(CombatState state)
    {
        _finished = Capture(state, ended: true);
        Dispose();
    }

    public void Dispose()
    {
        if (_player != null)
        {
            _player.CurrentHpChanged -= OnHpChanged;
            _player = null;
        }
    }
}
