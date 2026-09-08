using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CombatSolver.Engine.Common;
using CombatSolver.Engine.InCombat.Simulation;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private void AssertMirroredHookFilter(CombatState combat, Player player)
    {
        string liveBefore = ContinuationStamp.CaptureLive(combat).StateText;
        AbstractModel[] models = ModelDb.All.ToArray();
        var filter = new MirroredHookListenerFilter(enabled: true);
        var snapshot = (MirroredHookListenerSnapshot)filter.Filter(models);
        int checkedMethods = 0;
        foreach (MirroredHookMask mask in Enum.GetValues<MirroredHookMask>())
        {
            if (mask == MirroredHookMask.All)
                continue;
            string name = mask.ToString();
            AbstractModel[] expected = models.Where(model =>
                model.GetType().Assembly != typeof(AbstractModel).Assembly
                || model.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Any(method => method.Name == name
                        && method.DeclaringType != typeof(AbstractModel)
                        && method.GetBaseDefinition().DeclaringType == typeof(AbstractModel)))
                .ToArray();
            AbstractModel[] selected = Enumerable.Range(0, snapshot.Count)
                .Where(index => (snapshot.Layout.Entries[index].Mask & mask) != 0)
                .Select(index => snapshot[index]).ToArray();
            if (!expected.SequenceEqual(selected, ReferenceEqualityComparer.Instance))
                throw new InvalidOperationException($"Filtered dispatch differs for {name}.");
            if (snapshot.HasAny(mask) != (expected.Length != 0))
                throw new InvalidOperationException($"Empty dispatch differs for {name}.");
            checkedMethods++;
        }

        AbstractModel noOp = ModelDb.Card<StrikeIronclad>();
        AbstractModel external = models.OfType<NoOverrideSubscriber>().Single();
        AbstractModel[] duplicates = [noOp, external, external, noOp];
        var duplicateSnapshot = (MirroredHookListenerSnapshot)filter.Filter(duplicates);
        AbstractModel[] kept = Enumerable.Range(0, duplicateSnapshot.Count)
            .Where(index => duplicateSnapshot.Layout.Entries[index].Mask != 0)
            .Select(index => duplicateSnapshot[index]).ToArray();
        if (kept.Length != 2 || !ReferenceEquals(kept[0], external) || !ReferenceEquals(kept[1], external))
            throw new InvalidOperationException("Filtering removed an external receiver or a duplicate.");
        if (filter.Filter([noOp]).Count != 0)
            throw new InvalidOperationException("A default-only card was not filtered.");

        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        CombatPredictionSimulator parent = root.ForkSimulator();
        SimulatedCombatState parentCombat = (SimulatedCombatState)parent.State.CombatState;
        parentCombat.SetAmount<StrengthPower>(player.Creature, 2);
        var parentSource = (ICombatPredictionHookListenerSource)parentCombat;
        IReadOnlyList<AbstractModel> parentList = parentSource.MirroredHookListeners;
        StrengthPower parentStrength = parentCombat.GetPower<StrengthPower>(player.Creature)!;
        CombatPredictionSimulator child = parent.Fork();
        SimulatedCombatState childCombat = (SimulatedCombatState)child.State.CombatState;
        var childSource = (ICombatPredictionHookListenerSource)childCombat;
        StrengthPower childStrength = childCombat.GetPower<StrengthPower>(player.Creature)!;
        if (ReferenceEquals(parentStrength, childStrength)
            || !childSource.MirroredHookListeners.Contains(childStrength)
            || childSource.MirroredHookListeners.Contains(parentStrength))
            throw new InvalidOperationException("Filtered receivers retained a parent Power across Fork.");
        childCombat.SetAmount<StrengthPower>(player.Creature, 0);
        if (childSource.MirroredHookListeners.Contains(childStrength)
            || childSource.MirroredRunHookListeners.Contains(childStrength))
            throw new InvalidOperationException("Zeroing a Power retained a filtered receiver.");
        childCombat.SetAmount<StrengthPower>(player.Creature, 1);
        StrengthPower restoredStrength = childCombat.GetPower<StrengthPower>(player.Creature)!;
        if (!childSource.MirroredHookListeners.Contains(restoredStrength)
            || !childSource.MirroredRunHookListeners.Contains(restoredStrength)
            || !ReferenceEquals(parentList, parentSource.MirroredHookListeners)
            || parentStrength.Amount != 2)
            throw new InvalidOperationException("Power restoration or parent isolation changed filtered receivers.");
        PredictedCard generated = PredictedCard.Create(ModelDb.Card<Reflex>(), player);
        child.AddToPile(generated, MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand);
        if (!childSource.MirroredHookListeners.Contains(generated.Preview))
            throw new InvalidOperationException("A generated callback card did not invalidate filtered receivers.");
        if (ContinuationStamp.CaptureLive(combat).StateText != liveBefore)
            throw new InvalidOperationException("Listener filtering changed the live root.");

        // A base-method patch installed after an earlier capture must disable filtering
        // at the next root, including receivers with no override of their own.
        MethodInfo methodToPatch = typeof(AbstractModel).GetMethod(nameof(AbstractModel.TryModifyEnergyCostInCombat))!;
        MethodInfo prefix = typeof(UnattendedTestRunner).GetMethod(nameof(HookFilterBasePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        Harmony harmony = new("CombatSolver.Tests.MirroredHookFilter");
        try
        {
            harmony.Patch(methodToPatch, prefix: new HarmonyMethod(prefix));
            AbstractModel[] source = [noOp];
            if (!ReferenceEquals(source, MirroredHookListenerFilter.Capture().Filter(source)))
                throw new InvalidOperationException("A newly patched base callback was filtered.");
        }
        finally
        {
            harmony.Unpatch(methodToPatch, prefix);
        }
        if (MirroredHookListenerFilter.Capture().Filter([noOp]).Count != 0)
            throw new InvalidOperationException("Removing the test patch did not restore root filtering.");
        _completedChecks.Add($"MirroredHookFilter:Methods={checkedMethods}:Models={models.Length}:OrderDuplicatesExternalForkInvalidationPatchRefresh");
    }

    private static void HookFilterBasePrefix() { }
}
