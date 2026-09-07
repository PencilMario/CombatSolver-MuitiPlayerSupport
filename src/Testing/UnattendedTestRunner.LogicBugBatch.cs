using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.InCombat.Mirrors.Hooks.Card;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
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
