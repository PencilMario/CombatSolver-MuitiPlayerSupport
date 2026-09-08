using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private async Task AssertSummonedAllyPowerOrderAsync(CombatState combat, Player player)
    {
        await InjectPowerAsync(combat, player, new UnattendedPowerInjection
        { PowerId = "STRENGTH_POWER", Target = "Enemy", Amount = 2 });
        CombatPredictionSimulator simulator = CombatRootSnapshot.Capture(combat).ForkSimulator();
        SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
        shadow.SummonOsty(simulator, player, 5);
        PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, shadow);
        MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, combat.Enemies[0]);
        CombatPredictionSimulator fork = simulator.Fork();
        AssertSnapshotEqual(expected, CaptureSimulated(fork,
            (SimulatedCombatState)fork.State.CombatState, player, combat.Enemies[0]),
            _request.ScenarioId, "Fork");
        await OstyCmd.Summon(new BlockingPlayerChoiceContext(), player, 5, null);
        AssertSnapshotEqual(expected, CaptureActual(combat, player, combat.Enemies[0]),
            _request.ScenarioId, "Native");
    }

    private async Task AssertReportPowerLifecycleAsync(CombatState combat, Player player)
    {
        bool reapplication = _request.ScenarioId == "REMOVED-POWER-REAPPLICATION";
        await InjectPowerAsync(combat, player, new UnattendedPowerInjection
        {
            PowerId = reapplication ? "DRAW_CARDS_NEXT_TURN_POWER" : "ORBIT_POWER",
            Target = "Player", Amount = 3
        });
        if (reapplication)
            player.Creature.GetPower<DrawCardsNextTurnPower>()!.AmountOnTurnStart = 5;
        CombatPredictionSimulator simulator = CombatRootSnapshot.Capture(combat).ForkSimulator();
        SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
        if (reapplication)
        {
            shadow.SetAmount<DrawCardsNextTurnPower>(player.Creature, 0);
            shadow.Apply<DrawCardsNextTurnPower>(player.Creature, 3, player.Creature);
            PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, shadow);
        }
        else
        {
            shadow.AddPowerInstance<PhantomBladesPower>(player.Creature, 9);
            shadow.Apply<StrengthPower>(player.Creature, 2);
            shadow.AddPowerInstance<OrbitPower>(player.Creature, 1);
            shadow.Apply<DexterityPower>(player.Creature, 1);
            PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, shadow);
        }
        var enemy = combat.Enemies[0];
        MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, enemy);
        CombatPredictionSimulator fork = simulator.Fork();
        SimulatedCombatState forkState = (SimulatedCombatState)fork.State.CombatState;
        AssertSnapshotEqual(expected, CaptureSimulated(fork, forkState, player, enemy),
            _request.ScenarioId, "Fork");
        if (reapplication)
        {
            await PowerCmd.Remove(player.Creature.GetPower<DrawCardsNextTurnPower>()!);
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            { PowerId = "DRAW_CARDS_NEXT_TURN_POWER", Target = "Player", Amount = 3 });
        }
        else
        {
            foreach (var (id, amount) in new[] { ("PHANTOM_BLADES_POWER", 9), ("STRENGTH_POWER", 2),
                         ("ORBIT_POWER", 1), ("DEXTERITY_POWER", 1) })
                await InjectPowerAsync(combat, player, new UnattendedPowerInjection
                { PowerId = id, Target = "Player", Amount = amount });
        }
        AssertSnapshotEqual(expected, CaptureActual(combat, player, enemy), _request.ScenarioId, "Native");
        if (!reapplication)
        {
            forkState.SetAmount<StrengthPower>(player.Creature, 0);
            forkState.Apply<StrengthPower>(player.Creature, 4);
            PowerLifecycleSupport.ResolvePowerAmountChanges(fork, forkState);
            MoveStateSnapshot reacquired = CaptureSimulated(fork, forkState, player, enemy);
            AssertSnapshotEqual(expected, CaptureSimulated(simulator, shadow, player, enemy),
                _request.ScenarioId, "ParentIsolation");
            await PowerCmd.Remove(player.Creature.GetPower<StrengthPower>()!);
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            { PowerId = "STRENGTH_POWER", Target = "Player", Amount = 4 });
            AssertSnapshotEqual(reacquired, CaptureActual(combat, player, enemy),
                _request.ScenarioId, "Reacquisition");
        }
    }
}
