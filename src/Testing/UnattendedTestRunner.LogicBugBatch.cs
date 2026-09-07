using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private async Task AssertFeedThornsTerminalAsync(CombatState combat, Player player)
    {
        Creature enemy = combat.Enemies[0];
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatBeamSolver driver = new(root, SolverDisplayNames.Capture(combat), BattleDamageTracker.Observe(combat),
            SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null));
        PlanAction action = new(PlanActionKind.PlayCard, player.PlayerCombatState!.TurnNumber,
            CardId: "FEED", TargetCombatId: enemy.CombatId);
        SimulationSnapshot prediction = InvokeForcedTerminalReplay(driver, [action], null, 0, null);
        MoveStateSnapshot? actual = null;
        void OnEnded(CombatRoom room) => actual = CaptureActual(combat, player, enemy);
        try
        {
            if (!prediction.PlayerDead || prediction.AllEnemiesDead || prediction.PlayerHp <= 0
                || prediction.TerminalStamp is not { Outcome: CombatTerminalOutcome.Defeat })
                throw new InvalidOperationException("Feed after fatal thorns must retain defeat despite positive HP.");
            MoveStateSnapshot expected = CaptureSimulated(prediction.Simulator,
                (SimulatedCombatState)prediction.Simulator.State.CombatState, player, enemy);
            CombatManager.Instance.CombatEnded += OnEnded;
            if (!FindActualHandCard(player, "FEED", 0).TryManualPlay(enemy))
                throw new InvalidOperationException("Native Feed was not playable.");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            while (actual == null)
            {
                EnsureWithinDeadline();
                await NextFrameAsync();
            }
            AssertSnapshotEqual(expected, actual, "FeedThornsTerminal", "NativePendingLoss");
            if (CombatManager.Instance.IsInProgress || player.Creature.CurrentHp <= 0)
                throw new InvalidOperationException("Native combat must end even though Feed restored HP.");
        }
        finally
        {
            CombatManager.Instance.CombatEnded -= OnEnded;
            prediction.ReleaseSimulator();
        }
    }

    private async Task AssertSearchWaitsForNativeActionAsync(CombatState combat, Player player)
    {
        NGame host = NGame.Instance!;
        ActionExecutor executor = RunManager.Instance.ActionExecutor;
        bool requested = false;
        bool capturedInsideAction = false;
        void BeforeAction(GameAction action)
        {
            if (requested) return;
            requested = true;
            SolverController.RequestSearch(host, combat, SearchReason.Manual);
            capturedInsideAction = SolverController.HasActiveSearchSessionForTesting;
        }
        executor.BeforeActionExecuted += BeforeAction;
        try
        {
            if (!FindActualHandCard(player, "DEFEND_SILENT", 0).TryManualPlay(null))
                throw new InvalidOperationException("Action barrier fixture could not play Defend.");
            await executor.FinishedExecutingActions();
            if (!requested || capturedInsideAction)
                throw new InvalidOperationException("Search captured a root inside the native action queue.");
            long deadline = System.Environment.TickCount64 + 10_000;
            while (SolverController.LastCompletedResultForTesting == null && SolverController.LastSearchFailureForTesting == null)
            {
                if (System.Environment.TickCount64 >= deadline)
                    throw new TimeoutException("Deferred search did not complete after native action.");
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (SolverController.LastSearchFailureForTesting is { } failure)
                throw new InvalidOperationException("Deferred action-barrier search failed.", failure);
        }
        finally
        {
            executor.BeforeActionExecuted -= BeforeAction;
            SolverController.CancelSearchForTesting();
        }
    }

    private static void AssertSurroundedStateIdentity(CombatState combat)
    {
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator left = root.ForkSimulator();
        CombatPredictionSimulator right = root.ForkSimulator();
        SimulatedCombatState leftCombat = (SimulatedCombatState)left.State.CombatState;
        SimulatedCombatState rightCombat = (SimulatedCombatState)right.State.CombatState;
        SurroundedPower leftPower = leftCombat.GetPower<SurroundedPower>(root.PlayerIdentity.Creature)!;
        SurroundedPower rightPower = rightCombat.GetPower<SurroundedPower>(root.PlayerIdentity.Creature)!;
        left.StateStore.Get(leftPower, () => new SurroundedPredictionState(leftPower)).Facing = SurroundedPower.Direction.Left;
        right.StateStore.Get(rightPower, () => new SurroundedPredictionState(rightPower)).Facing = SurroundedPower.Direction.Right;
        StateFingerprintBuilder leftKey = new();
        StateFingerprintBuilder rightKey = new();
        leftCombat.AppendFingerprint(ref leftKey, left);
        rightCombat.AppendFingerprint(ref rightKey, right);
        if (leftKey.Finish() == rightKey.Finish())
            throw new InvalidOperationException("Surrounded facing collides in the branch fingerprint.");
        ContinuationStamp leftStamp = ContinuationStamp.CapturePredicted(root.PlayerIdentity, left, 1, root.Forecast, 1);
        ContinuationStamp rightStamp = ContinuationStamp.CapturePredicted(root.PlayerIdentity, right, 1, root.Forecast, 1);
        if (leftStamp == rightStamp)
            throw new InvalidOperationException("Surrounded facing collides in continuation state.");
        CombatPredictionSimulator fork = left.Fork();
        SimulatedCombatState forkCombat = (SimulatedCombatState)fork.State.CombatState;
        SurroundedPower forkPower = forkCombat.GetPower<SurroundedPower>(root.PlayerIdentity.Creature)!;
        fork.StateStore.Get(forkPower, () => new SurroundedPredictionState(forkPower)).Facing = SurroundedPower.Direction.Right;
        if (left.StateStore.Peek(leftPower, () => new SurroundedPredictionState(leftPower)).Facing != SurroundedPower.Direction.Left)
            throw new InvalidOperationException("Surrounded facing leaked across a fork.");
        int leftDamage = CorePowerSupport.AdjustForecastAttack(left, leftCombat, root.Enemies[0], root.PlayerIdentity.Creature, 10);
        int rightDamage = CorePowerSupport.AdjustForecastAttack(right, rightCombat, root.Enemies[0], root.PlayerIdentity.Creature, 10);
        if (leftDamage != 10 || rightDamage != 15)
            throw new InvalidOperationException($"Surrounded forecast damage {leftDamage}/{rightDamage}, expected 10/15.");
        Entry.Logger.Info("[CombatSolver/Test] SURROUNDED_STATE_IDENTITY_OK fingerprint=true continuation=true fork=true damage=10/15");
    }
}
