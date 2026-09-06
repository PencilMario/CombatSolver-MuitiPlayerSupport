using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private void AssertPotionValueTiers(CombatState combat)
    {
        if (PotionUsePolicy.StrategicHpCost("SWIFT_POTION") != 18
            || PotionUsePolicy.StrategicHpCost("CLARITY") != 14
            || PotionUsePolicy.StrategicHpCost("FIRE_POTION") != 9
            || PotionUsePolicy.StrategicHpCost("AMBERGRIS") != 9
            || PotionUsePolicy.StrategicHpCost(ModelDb.Potion<PotionShapedRock>(), true) != 0
            || ModelDb.AllPotions.Where(potion => potion.Rarity == PotionRarity.Token)
                .Any(potion => PotionUsePolicy.StrategicHpCost(potion) != 0))
            throw new InvalidOperationException("药水档位或免费药水例外不符合预期。");

        foreach (int threshold in new[] { 9, 14, 18 })
        {
            bool Eligible(int saved) => PotionUsePolicy.IsEligible(
                SolverPotionPolicy.Smart, 1, threshold, true, 30, true, true, 30 - saved);
            if (Eligible(threshold - 1) || !Eligible(threshold))
                throw new InvalidOperationException($"Smart 药水 {threshold} HP 门槛错误。");
        }
        if (PotionUsePolicy.EffectiveStrategicHpCost(9, 1, 80) != 32
            || !PotionUsePolicy.IsEligible(SolverPotionPolicy.Smart, 1, 18, false, 0, true, true, 0)
            || !PotionUsePolicy.IsEligible(SolverPotionPolicy.RequireAtLeastOne, 1, 18, true, 0, true, true, 0))
            throw new InvalidOperationException("药水分档改变了龙涎香、救命或强制用药规则。");

        CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
        if (!root.SearchablePotions.Any(potion => potion.PotionId == "SWIFT_POTION" && potion.StrategicHpCost == 18))
            throw new InvalidOperationException("搜索根没有捕获 Swift 药水的 18 HP 成本。");
        _completedChecks.Add("PotionValueTiers:Thresholds:Free:Ambergris:Rescue:Forced:Root");
    }
}
