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
    private async Task AssertEnergyResetPowerOrderAsync(CombatState combat, Player player)
    {
        CombatRootSnapshot emptyRoot = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator left = emptyRoot.ForkSimulator();
        CombatPredictionSimulator right = emptyRoot.ForkSimulator();
        SimulatedCombatState leftState = (SimulatedCombatState)left.State.CombatState;
        SimulatedCombatState rightState = (SimulatedCombatState)right.State.CombatState;
        leftState.Apply<SpinnerPower>(player.Creature, 1);
        leftState.Apply<LightningRodPower>(player.Creature, 1);
        rightState.Apply<LightningRodPower>(player.Creature, 1);
        rightState.Apply<SpinnerPower>(player.Creature, 1);
        StateFingerprintBuilder leftKey = new();
        StateFingerprintBuilder rightKey = new();
        leftState.AppendFingerprint(ref leftKey, left);
        rightState.AppendFingerprint(ref rightKey, right);
        if (leftKey.Finish() == rightKey.Finish())
            throw new InvalidOperationException("Energy-reset power order collides in the branch fingerprint.");
        if (CaptureSimulated(left, leftState, player, combat.Enemies[0]).ExactContinuationState
            == CaptureSimulated(right, rightState, player, combat.Enemies[0]).ExactContinuationState)
            throw new InvalidOperationException("Energy-reset power order collides in continuation state.");
        bool reverse = _request.ScenarioId.EndsWith("-REVERSE", StringComparison.Ordinal);
        string[] powerIds = reverse
            ? ["LIGHTNING_ROD_POWER", "SPINNER_POWER"]
            : ["SPINNER_POWER", "LIGHTNING_ROD_POWER"];
        foreach (string id in powerIds)
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            {
                PowerId = id, Target = "Player", Amount = 1
            });
        foreach (string id in new[] { "GENESIS_POWER", "STAR_NEXT_TURN_POWER", "RADIANCE_POWER" })
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            {
                PowerId = id, Target = "Player", Amount = 1
            });
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator simulator = root.ForkSimulator();
        CombatPredictionSimulator fork = simulator.Fork();
        SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
        if (!PersistentPowerSupport.TriggerAfterEnergyReset(simulator, shadow, player))
            throw new InvalidOperationException("Energy-reset fixture encountered a choice.");
        MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, combat.Enemies[0]);
        SimulatedCombatState forkState = (SimulatedCombatState)fork.State.CombatState;
        if (!PersistentPowerSupport.TriggerAfterEnergyReset(fork, forkState, player))
            throw new InvalidOperationException("Fork energy-reset fixture encountered a choice.");
        AssertSnapshotEqual(expected, CaptureSimulated(fork, forkState, player, combat.Enemies[0]),
            "EnergyResetPowerOrder", "Fork");
        await MegaCrit.Sts2.Core.Hooks.Hook.AfterEnergyReset(combat, player);
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        AssertSnapshotEqual(expected, CaptureActual(combat, player, combat.Enemies[0]), "EnergyResetPowerOrder", "NativeHook");
    }

    private async Task AssertReplayStartHistoryAsync(CombatState combat, Player player)
    {
        Creature enemy = combat.Enemies[0];
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatBeamSolver driver = new(root, SolverDisplayNames.Capture(combat), BattleDamageTracker.Observe(combat),
            SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null));
        PlanAction action = new(PlanActionKind.PlayCard, player.PlayerCombatState!.TurnNumber,
            CardId: "SLICE", TargetCombatId: enemy.CombatId);
        bool echo = _request.ScenarioId == "REPLAY-START-HISTORY-ECHO";
        SimulationSnapshot[] predictions = echo
            ? [InvokeForcedTerminalReplay(driver, [action], null, 0, null),
               InvokeForcedTerminalReplay(driver, [action, action], null, 0, null)]
            : [InvokeForcedTerminalReplay(driver, [action], null, 0, null)];
        try
        {
            for (int index = 0; index < predictions.Length; index++)
            {
                CombatPredictionSimulator simulator = predictions[index].Simulator;
                SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
                MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, enemy);
                CombatPredictionSimulator fork = simulator.Fork();
                if (!FindActualHandCard(player, "SLICE", 0).TryManualPlay(enemy))
                    throw new InvalidOperationException("Native repeated Slice was not playable.");
                await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
                AssertSnapshotEqual(expected, CaptureActual(combat, player, enemy), "ReplayStartHistory", $"NativeSlice{index}");
                CombatPredictionSimulator recaptured = CombatRootSnapshot.Capture(combat).ForkSimulator();
                foreach (CombatPredictionSimulator branch in new[] { simulator, fork, recaptured })
                {
                    SimulatedCombatState branchState = (SimulatedCombatState)branch.State.CombatState;
                    int expectedStarts = echo ? 3 + 2 * index : 2;
                    if (branchState.GetCardPlaySeriesStartedThisTurn(player.Creature) != index + 1
                        || branchState.GetZeroCostAttackStartsThisTurn(player.Creature) != expectedStarts
                        || branchState.GetManualCardsPlayedThisTurn(player.Creature) != index + 1)
                        throw new InvalidOperationException("Card history must distinguish repeated plays from card series and manual actions.");
                }
            }
        }
        finally
        {
            foreach (SimulationSnapshot prediction in predictions)
                prediction.ReleaseSimulator();
        }
    }

    private async Task AssertDeathEffectsOnceAsync(CombatState combat, Player player)
    {
        Creature killed = combat.Enemies[0];
        Creature survivor = combat.Enemies[1];
        int reward = survivor.GetPower<RavenousPower>()!.Amount;
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatBeamSolver driver = new(root, SolverDisplayNames.Capture(combat), BattleDamageTracker.Observe(combat),
            SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null));
        PlanAction action = new(PlanActionKind.PlayCard, player.PlayerCombatState!.TurnNumber,
            CardId: "STRIKE_IRONCLAD", TargetCombatId: killed.CombatId);
        SimulationSnapshot prediction = InvokeForcedTerminalReplay(driver, [action], null, 0, null);
        try
        {
            CombatPredictionSimulator simulator = prediction.Simulator;
            SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
            CorePowerSupport.ApplyEnemyDeathPowers(simulator, shadow, shadow.KnownEnemies, new HashSet<uint>());
            if (shadow.GetAmount<StrengthPower>(survivor) != reward)
                throw new InvalidOperationException($"Repeated death notification granted {shadow.GetAmount<StrengthPower>(survivor)} Strength, expected {reward}.");
            CombatPredictionSimulator fork = simulator.Fork();
            SimulatedCombatState forkState = (SimulatedCombatState)fork.State.CombatState;
            CorePowerSupport.ApplyEnemyDeathPowers(fork, forkState, forkState.KnownEnemies, new HashSet<uint>());
            if (forkState.GetAmount<StrengthPower>(survivor) != reward)
                throw new InvalidOperationException("Fork lost completed death identity.");
            MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, survivor);
            if (!FindActualHandCard(player, "STRIKE_IRONCLAD", 0).TryManualPlay(killed))
                throw new InvalidOperationException("Native Strike was not playable.");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            AssertSnapshotEqual(expected, CaptureActual(combat, player, survivor), "DeathEffectsOnce", "NativeStrike");
        }
        finally { prediction.ReleaseSimulator(); }
    }

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
