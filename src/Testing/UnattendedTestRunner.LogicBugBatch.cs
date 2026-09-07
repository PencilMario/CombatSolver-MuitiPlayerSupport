using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;
using CombatSolver.Engine.Common;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private static void AssertNarrowOrderedPileCapacity(CombatState combat, Player player)
    {
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatBeamSolver driver = new(root, SolverDisplayNames.Capture(combat), BattleDamageTracker.Observe(combat),
            SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null),
            searchProfile: SolverSearchProfile.Deep);
        List<SimulationSnapshot> snapshots = [];
        try
        {
            for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
            for (int c = 0; c < 4; c++)
            {
                if (a == b || a == c || b == c)
                    continue;
                int d = 6 - a - b - c;
                List<int> remaining = [0, 1, 2, 3];
                List<PlanAction> actions = [];
                foreach (int index in new[] { a, b, c, d })
                {
                    int occurrence = remaining.IndexOf(index);
                    actions.Add(new PlanAction(PlanActionKind.PlayCard, root.StartTurnNumber,
                        CardId: "DEFLECT", CardOccurrence: occurrence));
                    remaining.RemoveAt(occurrence);
                }
                actions.Add(new PlanAction(PlanActionKind.EndTurn, root.StartTurnNumber));
                snapshots.Add(InvokeForcedTerminalReplay(driver, actions, null, 0, null));
            }
            if (snapshots.Any(snapshot => snapshot.PocketwatchCardThreshold < 0)
                || snapshots.Select(snapshot => snapshot.StateKey).Distinct().Count() < 8
                || snapshots.Select(snapshot => snapshot.ProjectedShuffleOrderKey).Distinct().Count() < 8)
                throw new InvalidOperationException("Narrow retention fixture did not produce eight distinct Pocketwatch pile orders.");
            driver.VerifyNarrowOrderedPileCapacityForTesting(snapshots);
        }
        finally
        {
            foreach (SimulationSnapshot snapshot in snapshots)
                snapshot.ReleaseSimulator();
        }
    }

    private async Task AssertGamblingChipSlyOrderAsync(CombatState combat, Player player)
    {
        Creature enemy = combat.Enemies[0];
        CardModel nativeCard = FindActualHandCard(player, "RICOCHET", 0);
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator parent = root.ForkSimulator();
        CombatPredictionSimulator fork = parent.Fork();
        MoveStateSnapshot? expected = null;
        foreach (CombatPredictionSimulator simulator in new[] { parent, fork })
        {
            SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
            TurnStartChoiceCursor choices = TurnStartChoiceCursor.ForAutomaticPolicy(request =>
                CardChoiceSupport.BuildRequestedChoice(request.Spec!, ["RICOCHET"]));
            if (!TurnStartChoiceSupport.ResolveDiscardAndDraw(
                    simulator, shadow, player, choices, "GAMBLING_CHIP"))
                throw new InvalidOperationException("Gambling Chip fixture encountered a choice.");
            MoveStateSnapshot result = CaptureSimulated(simulator, shadow, player, enemy);
            if (expected != null)
                AssertSnapshotEqual(expected, result, "GamblingChipSlyOrder", "Fork");
            expected = result;
        }
        await CardCmd.DiscardAndDraw(new BlockingPlayerChoiceContext(), [nativeCard], 1);
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        AssertSnapshotEqual(expected!, CaptureActual(combat, player, enemy),
            "GamblingChipSlyOrder", "NativeDiscardAndDraw");
    }

    private async Task AssertGalvanicGeneratedPowerAsync(CombatState combat, Player player)
    {
        Creature enemy = combat.Enemies[0];
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator simulator = root.ForkSimulator();
        SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
        PredictedCard card = PredictedCard.Create(ModelDb.Card<Automation>(), player);
        simulator.AddGeneratedCardToCombat(card, PileType.Hand, player,
            resultKind: CardGenerationResultKind.Fixed);
        shadow.NormalizeCardAfflictions(simulator);
        MoveStateSnapshot expected = CaptureSimulated(simulator, shadow, player, enemy);
        CardModel actualCard = combat.CreateCard(ModelDb.Card<Automation>(), player);
        CardPileAddResult added = await CardPileCmd.AddGeneratedCardToCombat(actualCard, PileType.Hand, player);
        if (!added.success)
            throw new InvalidOperationException("Native generated power could not enter combat.");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        AssertSnapshotEqual(expected, CaptureActual(combat, player, enemy),
            "GalvanicGeneratedPower", "NativeEntry");
        CombatPredictionSimulator fork = simulator.Fork();
        SimulatedCombatState forkState = (SimulatedCombatState)fork.State.CombatState;
        AssertSnapshotEqual(expected, CaptureSimulated(fork, forkState, player, enemy),
            "GalvanicGeneratedPower", "ForkEntry");
        PlaySimulatedCard(fork, forkState, FindSimulatedHandCard(fork, player, "AUTOMATION", 0),
            null, combat.Enemies);
        AssertSnapshotEqual(expected, CaptureSimulated(simulator, shadow, player, enemy),
            "GalvanicGeneratedPower", "ParentAfterForkPlay");
        if (!actualCard.TryManualPlay(null))
            throw new InvalidOperationException("Native generated power was not playable.");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        AssertSnapshotEqual(CaptureSimulated(fork, forkState, player, enemy),
            CaptureActual(combat, player, enemy), "GalvanicGeneratedPower", "NativePlay");
    }

    private void AssertTurnEndPowerOrderFork(CombatState combat, Player player)
    {
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator left = root.ForkSimulator();
        CombatPredictionSimulator right = root.ForkSimulator();
        SimulatedCombatState leftState = (SimulatedCombatState)left.State.CombatState;
        SimulatedCombatState rightState = (SimulatedCombatState)right.State.CombatState;
        leftState.Apply<NoDrawPower>(player.Creature, 1);
        leftState.Apply<DarkEmbracePower>(player.Creature, 1);
        rightState.Apply<DarkEmbracePower>(player.Creature, 1);
        rightState.Apply<NoDrawPower>(player.Creature, 1);
        PowerLifecycleSupport.ResolvePowerAmountChanges(left, leftState);
        PowerLifecycleSupport.ResolvePowerAmountChanges(right, rightState);
        StateFingerprintBuilder leftKey = new();
        StateFingerprintBuilder rightKey = new();
        leftState.AppendFingerprint(ref leftKey, left);
        rightState.AppendFingerprint(ref rightKey, right);
        if (leftKey.Finish() == rightKey.Finish())
            throw new InvalidOperationException("Turn-end power order collides in the branch fingerprint.");
        Creature enemy = combat.Enemies[0];
        MoveStateSnapshot parentBefore = CaptureSimulated(left, leftState, player, enemy);
        if (parentBefore.ExactContinuationState
            == CaptureSimulated(right, rightState, player, enemy).ExactContinuationState)
            throw new InvalidOperationException("Turn-end power order collides in continuation state.");
        CombatPredictionSimulator fork = left.Fork();
        SimulatedCombatState forkState = (SimulatedCombatState)fork.State.CombatState;
        StateFingerprintBuilder forkKey = new();
        forkState.AppendFingerprint(ref forkKey, fork);
        if (forkKey.Finish() != leftKey.Finish())
            throw new InvalidOperationException("Fork changed the ordered power fingerprint.");
        if (!PlayerTurnEndLifecycle.RunPhaseTwo(fork, forkState, [player.Creature], 1))
            throw new InvalidOperationException("Turn-end order fixture encountered a choice.");
        AssertSnapshotEqual(parentBefore, CaptureSimulated(left, leftState, player, enemy),
            "TurnEndPowerOrder", "ParentAfterFork");
        if (!PlayerTurnEndLifecycle.RunPhaseTwo(left, leftState, [player.Creature], 1)
            || !PlayerTurnEndLifecycle.RunPhaseTwo(right, rightState, [player.Creature], 1))
            throw new InvalidOperationException("Turn-end order fixture encountered a choice.");
        AssertSnapshotEqual(CaptureSimulated(left, leftState, player, enemy),
            CaptureSimulated(fork, forkState, player, enemy), "TurnEndPowerOrder", "ForkResult");
        if (left.State.GetPlayerCombatState(player).Hand.Cards.Count != 1
            || right.State.GetPlayerCombatState(player).Hand.Cards.Count != 0)
            throw new InvalidOperationException("Power order must distinguish allowed and blocked end-turn draws.");
    }

    private static void AssertBoundCounterFork(CombatState combat)
    {
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator parent = root.ForkSimulator();
        SimulatedCombatState parentCombat = (SimulatedCombatState)parent.State.CombatState;
        Player player = root.PlayerIdentity;
        int turn = parentCombat.GetPlayerTurnNumber(player);
        ChainsOfBindingPower parentPower = parentCombat.GetPower<ChainsOfBindingPower>(player.Creature)!;
        ChainsOfBindingPredictionState parentState = parent.StateStore.Get(
            parentPower, static () => new ChainsOfBindingPredictionState());
        parentState.RecordBoundCardAfflicted(turn);
        CombatPredictionSimulator child = parent.Fork();
        SimulatedCombatState childCombat = (SimulatedCombatState)child.State.CombatState;
        ChainsOfBindingPower childPower = childCombat.GetPower<ChainsOfBindingPower>(player.Creature)!;
        ChainsOfBindingPredictionState childState = child.StateStore.Get(
            childPower, static () => new ChainsOfBindingPredictionState());
        childState.RecordBoundCardAfflicted(turn);
        if (parentState.GetBoundCardsAfflictedThisTurn(turn) != 1
            || childState.GetBoundCardsAfflictedThisTurn(turn) != 2)
            throw new InvalidOperationException("Bound quota leaked between forks.");
        StateFingerprintBuilder parentKey = new();
        StateFingerprintBuilder childKey = new();
        parentCombat.AppendFingerprint(ref parentKey, parent);
        childCombat.AppendFingerprint(ref childKey, child);
        if (parentKey.Finish() == childKey.Finish())
            throw new InvalidOperationException("Bound quotas collide in the state fingerprint.");
        if (childState.GetBoundCardsAfflictedThisTurn(turn + 1) != 0)
            throw new InvalidOperationException("Bound quota remained spent in a new player turn.");
        childState.RecordBoundCardAfflicted(turn + 1);
        if (childState.GetBoundCardsAfflictedThisTurn(turn + 1) != 1
            || parentState.GetBoundCardsAfflictedThisTurn(turn) != 1)
            throw new InvalidOperationException("Bound quota did not advance independently to the next turn.");
    }

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
        bool reapply = _request.ScenarioId.EndsWith("-REAPPLY", StringComparison.Ordinal);
        bool overflow = _request.ScenarioId.EndsWith("-OVERFLOW", StringComparison.Ordinal);
        bool reverse = reapply || _request.ScenarioId.EndsWith("-REVERSE", StringComparison.Ordinal);
        string[] powerIds = reverse
            ? ["LIGHTNING_ROD_POWER", "SPINNER_POWER"]
            : ["SPINNER_POWER", "LIGHTNING_ROD_POWER"];
        foreach (string id in powerIds)
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            {
                PowerId = id, Target = "Player", Amount = overflow && id == "SPINNER_POWER" ? 3 : 1
            });
        foreach (string id in new[] { "GENESIS_POWER", "STAR_NEXT_TURN_POWER", "RADIANCE_POWER" })
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            {
                PowerId = id, Target = "Player", Amount = 1
            });
        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator simulator = root.ForkSimulator();
        SimulatedCombatState shadow = (SimulatedCombatState)simulator.State.CombatState;
        if (reapply)
        {
            shadow.SetAmount<LightningRodPower>(player.Creature, 0);
            shadow.Apply<LightningRodPower>(player.Creature, 1);
            PowerLifecycleSupport.ResolvePowerAmountChanges(simulator, shadow);
            await PowerCmd.Remove(player.Creature.GetPower<LightningRodPower>()!);
            await InjectPowerAsync(combat, player, new UnattendedPowerInjection
            {
                PowerId = "LIGHTNING_ROD_POWER", Target = "Player", Amount = 1
            });
        }
        CombatPredictionSimulator fork = simulator.Fork();
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
