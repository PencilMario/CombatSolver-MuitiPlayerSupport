using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace CombatSolver;

internal static class SolverStrategyOutcomeText
{
    internal static string? Format(RelicCounterEvaluation counters, GrowthValues rewards, bool victory)
    {
        List<string> completed = [], pending = [], growth = [];
        foreach (var entry in RelicCounterCatalog.All)
        {
            ulong bit = 1UL << (int)entry.Id;
            if ((counters.TargetMask & bit) == 0) continue;
            string name = entry.Canonical().Title.GetFormattedText() + " " + counters.Value(entry.Id);
            ((counters.SatisfiedMask & bit) != 0 && victory ? completed : pending).Add(name);
        }
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            int count = rewards.Get(source);
            if (count <= 0) continue;
            string name = source == GrowthSource.Goopy
                ? ModelDb.Enchantment<Goopy>().Title.GetFormattedText()
                : ModelDb.AllCards.Single(card => card.GetType().Name == source.ToString()).Title;
            growth.Add(count == 1 ? name : $"{name} ×{count}");
        }
        foreach (var entry in GrowthSourceMirrors.All)
        {
            int count = rewards.Get(new GrowthSourceHandle(entry.Id));
            if (count <= 0) continue;
            var card = entry.Card();
            string name = entry.Title?.Invoke(card) ?? card.Title;
            growth.Add(count == 1 ? name : $"{name} ×{count}");
        }
        List<string> parts = [];
        string Join(List<string> values) => string.Join(SolverText.IsEnglish ? ", " : "、", values);
        if (completed.Count > 0) parts.Add(SolverText.Format($"已卡：{Join(completed)}"));
        if (pending.Count > 0) parts.Add(SolverText.Format($"未达标：{Join(pending)}"));
        if (growth.Count > 0) parts.Add(SolverText.Format($"已获得：{Join(growth)}"));
        return parts.Count == 0 ? null : SolverText.Get(victory ? "预计路线结果" : "当前搜索进度") + " · " + string.Join("  │  ", parts);
    }
}
