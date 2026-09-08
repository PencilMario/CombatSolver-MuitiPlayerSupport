using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CombatSolver.Engine.Common;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    /// <summary>
    /// 第三方起手牌的移除估值登记点（<see cref="CardRemovalValueMirrors"/>）。这里拿一张原版
    /// 非起手牌当替身自行登记、断言完再撤销，所以同一个进程里后面的用例看不到它。
    /// </summary>
    private static void AssertThirdPartyBasicCardRemoval(Player player)
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Third-party card removal: " + message);
        }

        // 替身取拳锋打击：原版牌，但不在那张写死的起手牌表里，所以未登记时必然走通用估值。
        PredictedCard standIn = PredictedCard.Create(ModelDb.Card<PommelStrike>(), player);
        PredictedCard vanillaStrike = PredictedCard.Create(ModelDb.Card<StrikeIronclad>(), player);
        CardChoiceSpec spec = new(
            PlanChoiceEffect.Exhaust,
            PileType.Hand,
            MinCount: 0,
            MaxCount: 2,
            Options: [standIn, vanillaStrike],
            SourceCards: [standIn, vanillaStrike],
            ReplacementValue: 0d);

        Check(CardRemovalValueMirrors.IsEmpty, "the registry starts empty");
        double unregistered = CardChoiceSupport.RemovalPriorityForTesting(spec, standIn);
        double vanilla = CardChoiceSupport.RemovalPriorityForTesting(spec, vanillaStrike);
        // 未登记时走通用估值：伤害记满，于是它排在被压过权重的原版起手打击后面，不会先被烧。
        Check(unregistered > vanilla,
            $"an unregistered card outranks the vanilla basic strike: {unregistered} vs {vanilla}");

        CardRemovalValueMirrors.Register<PommelStrike>(BasicCardRemovalKind.Strike);
        try
        {
            Check(!CardRemovalValueMirrors.IsEmpty
                && CardRemovalValueMirrors.Kind(standIn.Preview) == BasicCardRemovalKind.Strike,
                "registration is visible");
            double registered = CardChoiceSupport.RemovalPriorityForTesting(spec, standIn);
            // 登记之后按同一组权重估值，排序键必须真的降下来——这就是「先烧起手牌」那件事。
            Check(registered < unregistered,
                $"registering as a basic strike lowers the removal key: {registered} vs {unregistered}");
            try
            {
                CardRemovalValueMirrors.Register<PommelStrike>(BasicCardRemovalKind.Defend);
                Check(false, "duplicate registration must throw");
            }
            catch (ArgumentException) { }
            try
            {
                CardRemovalValueMirrors.Register<StrikeSilent>((BasicCardRemovalKind)99);
                Check(false, "an undefined kind must throw");
            }
            catch (ArgumentOutOfRangeException) { }
            // 原版那张表优先：已经写死的类型不会被登记表改写。
            Check(CardChoiceSupport.RemovalPriorityForTesting(spec, vanillaStrike) == vanilla,
                "the vanilla table still wins for vanilla starters");
        }
        finally { CardRemovalValueMirrors.UnregisterForTesting<PommelStrike>(); }

        Check(CardRemovalValueMirrors.IsEmpty, "the test registration is cleaned up");
        Check(CardChoiceSupport.RemovalPriorityForTesting(spec, standIn) == unregistered,
            "cleanup restores the unregistered key");
    }
}
