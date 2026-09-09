using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private async Task AssertStockRespawnAsync(CombatState combat, Player player)
    {
        var killed = combat.Enemies.Single();
        int turn = player.PlayerCombatState!.TurnNumber;
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatBeamSolver driver = new(root, SolverDisplayNames.Capture(combat), BattleDamageTracker.Observe(combat),
            SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null));
        bool thorns = _request.ScenarioId == "STOCK-THORNS-RESPAWN-HP";
        PlanAction strike = thorns ? new(PlanActionKind.EndTurn, turn) : new(PlanActionKind.PlayCard, turn,
            CardId: "STRIKE_SILENT", TargetCombatId: killed.CombatId);
        SimulationSnapshot first = InvokeForcedTerminalReplay(driver, [strike], null, 0, null);
        SimulationSnapshot next = InvokeForcedTerminalReplay(driver,
            thorns ? [strike] : [strike, new PlanAction(PlanActionKind.EndTurn, turn)], null, 0, null);
        try
        {
            MoveStateSnapshot[] expected = [
                CaptureSimulated(first.Simulator, (SimulatedCombatState)first.Simulator.State.CombatState, player, killed),
                CaptureSimulated(next.Simulator, (SimulatedCombatState)next.Simulator.State.CombatState, player, killed)];
            CombatPredictionSimulator fork = first.Simulator.Fork();
            AssertSnapshotEqual(expected[0], CaptureSimulated(fork,
                (SimulatedCombatState)fork.State.CombatState, player, killed), "StockRespawn", "Fork");
            if (!thorns)
            {
                if (!FindActualHandCard(player, "STRIKE_SILENT", 0).TryManualPlay(killed))
                    throw new InvalidOperationException("补货夹具原生打击未能执行。");
                await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
                AssertSnapshotEqual(expected[0], CaptureActual(combat, player, killed), "StockRespawn", "NativeDeath");
            }
            CombatManager.Instance.OnEndedTurnLocally();
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(player, turn));
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            while (player.PlayerCombatState is not { Phase: PlayerTurnPhase.Play } current || current.TurnNumber <= turn)
            {
                EnsureWithinDeadline();
                if (!CombatManager.Instance.IsInProgress)
                    throw new InvalidOperationException("补货夹具意外结束战斗。");
                await NextFrameAsync();
            }
            AssertSnapshotEqual(expected[1], CaptureActual(combat, player, killed), "StockRespawn", "NextTurn");
        }
        finally
        {
            first.ReleaseSimulator();
            next.ReleaseSimulator();
        }
    }
}
