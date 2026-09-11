using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private async Task AssertHpTargetStopAsync(CombatState combat, Player player)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("HP target stop: " + message);
        }
        SolverSettingsData original = SolverSettings.Current;
        try
        {
            Check(new SolverSettingsData().StopAtAcceptableBattleHpLoss, "default enabled");
            var settings = original with { StopAtAcceptableBattleHpLoss = true,
                GrowthBudgets = new GrowthValues(ForbiddenGrimoire: 12), IgnoreLongTermRewards = false };
            Check(!SolverSettings.RoundTripForTesting(settings with { StopAtAcceptableBattleHpLoss = false }).StopAtAcceptableBattleHpLoss,
                "disabled preference round trip");
            SolverSettings.ApplyForTesting(settings);
            foreach (var relic in player.Relics.ToArray()) await RelicCmd.Remove(relic);
            foreach (var power in combat.Creatures.SelectMany(c => c.Powers).ToArray()) await PowerCmd.Remove(power);
            await ClearPlayerPilesAsync(player);
            foreach (string id in new[] { "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "BASH", "INFLAME" })
                await InjectCardAsync(combat, player, new UnattendedCardInjection { CardId = id, Pile = "Hand" });
            await CreatureCmd.SetCurrentHp(combat.Enemies[0], 1);
            SetEnergy(player, 3);
            SearchPolicySnapshot policy = SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null) with
            {
                ForceShortOnly = true, ShortBudgetOverrideMilliseconds = 1500,
                PotionPolicy = SolverPotionPolicy.Disabled, MaxDegreeOfParallelism = 1,
                DetailedDiagnostics = false, VerifyIncrementalSearch = false,
                ShortProfile = SolverSearchProfile.Short with { MaxExpandedNodes = 128 },
            };
            Check(policy.StopAtAcceptableBattleHpLoss && !policy.HasGrowthTargets && policy.CanStopAtHpTarget,
                "saved allowance without matching cards permits stopping");
            CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
            SolverDisplayNames names = SolverDisplayNames.Capture(combat);
            async Task<SolverResult> Search(SearchPolicySnapshot requested, int alreadyLost = 0)
                => await Task.Run(() => CombatSearchCoordinator.Solve(root, names,
                    new BattleDamageSnapshot(alreadyLost, 0, 0), requested, CancellationToken.None, null));
            SolverResult stopped = await Search(policy);
            Check(stopped.Snapshot.AllEnemiesDead && stopped.ProjectedBattleHpLost == 0, "zero-loss complete victory");
            SolverResult continued = await Search(policy with { StopAtAcceptableBattleHpLoss = false });
            Check(continued.Snapshot.AllEnemiesDead && continued.ProjectedBattleHpLost == 0
                && stopped.ShortExpandedNodes < continued.ShortExpandedNodes, "switch stops before remaining combinations");
            SolverResult threshold = await Search(policy with { AcceptableBattleHpLoss = 3 }, alreadyLost: 3);
            Check(threshold.ProjectedBattleHpLost == 3
                && CombatSearchCoordinator.HasReachedAcceptableBattleHpLoss(policy with { AcceptableBattleHpLoss = 3 }, threshold)
                && !CombatSearchCoordinator.HasReachedAcceptableBattleHpLoss(policy, threshold)
                && !CombatSearchCoordinator.HasReachedAcceptableBattleHpLoss(policy with { StopAtAcceptableBattleHpLoss = false, AcceptableBattleHpLoss = 3 }, threshold),
                "total battle threshold and toggle boundaries");
            await CreatureCmd.SetCurrentHp(combat.Enemies[0], 12);
            root = CombatRootSnapshot.Capture(combat);
            SolverResult parallel = await Search(policy with { MaxDegreeOfParallelism = 2 });
            Check(parallel.Snapshot.AllEnemiesDead && parallel.ProjectedBattleHpLost == 0
                && parallel.MaxParallelExpansionConcurrency == 2, "parallel wave drains and returns winner");
            await CreatureCmd.SetCurrentHp(combat.Enemies[0], 1);
            await InjectCardAsync(combat, player, new UnattendedCardInjection { CardId = "FORBIDDEN_GRIMOIRE", Pile = "Hand" });
            SearchPolicySnapshot growth = SolverController.CaptureSearchPolicy(SolverSettings.Capture(), combat, false, null);
            Check(growth.HasGrowthTargets && !growth.CanStopAtHpTarget
                && (growth with { IgnoreLongTermRewards = true }).CanStopAtHpTarget, "present growth card and ignore switch");
            root = CombatRootSnapshot.Capture(combat);
            names = SolverDisplayNames.Capture(combat);
            SolverResult rewarded = await Search(policy with { HasGrowthTargets = growth.HasGrowthTargets });
            Check(rewarded.Snapshot.AllEnemiesDead && rewarded.Snapshot.GrowthRewards.ForbiddenGrimoire == 1,
                "growth opportunity survives immediate zero-loss kill");
            _completedChecks.Add($"HpTargetStop:zero_nodes={stopped.ShortExpandedNodes}:off_nodes={continued.ShortExpandedNodes}:parallel_nodes={parallel.ShortExpandedNodes}:threshold3:growth_reward1");
        }
        finally { SolverSettings.ApplyForTesting(original); }
    }
}
