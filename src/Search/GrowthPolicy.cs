using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Models;
using Cards = MegaCrit.Sts2.Core.Models.Cards;
using Enchantments = MegaCrit.Sts2.Core.Models.Enchantments;

namespace CombatSolver;

internal enum GrowthSource
{
    HandOfGreed, TheHunt, Feed, Royalties, Alchemize, GeneticAlgorithm, TheScythe, Goopy,
}

// Immutable vectors are used for both per-event HP budgets and realized event counts.
internal readonly record struct GrowthValues(
    int HandOfGreed = 0, int TheHunt = 0, int Feed = 0, int Royalties = 0,
    int Alchemize = 0, int GeneticAlgorithm = 0, int TheScythe = 0, int Goopy = 0)
{
    [JsonIgnore]
    public bool IsEnabled => this != default;
    [JsonIgnore]
    public int Total => checked(HandOfGreed + TheHunt + Feed + Royalties + Alchemize + GeneticAlgorithm + TheScythe + Goopy);

    public static bool HasTarget(CardModel card)
        => card is Cards.HandOfGreed or Cards.TheHunt or Cards.Feed or Cards.Royalties or Cards.Alchemize
            || card.DeckVersion != null && (card is Cards.GeneticAlgorithm or Cards.TheScythe || card.Enchantment is Enchantments.Goopy);
    public int Get(GrowthSource source) => source switch
    {
        GrowthSource.HandOfGreed => HandOfGreed,
        GrowthSource.TheHunt => TheHunt,
        GrowthSource.Feed => Feed,
        GrowthSource.Royalties => Royalties,
        GrowthSource.Alchemize => Alchemize,
        GrowthSource.GeneticAlgorithm => GeneticAlgorithm,
        GrowthSource.TheScythe => TheScythe,
        GrowthSource.Goopy => Goopy,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public GrowthValues With(GrowthSource source, int value) => source switch
    {
        GrowthSource.HandOfGreed => this with { HandOfGreed = value },
        GrowthSource.TheHunt => this with { TheHunt = value },
        GrowthSource.Feed => this with { Feed = value },
        GrowthSource.Royalties => this with { Royalties = value },
        GrowthSource.Alchemize => this with { Alchemize = value },
        GrowthSource.GeneticAlgorithm => this with { GeneticAlgorithm = value },
        GrowthSource.TheScythe => this with { TheScythe = value },
        GrowthSource.Goopy => this with { Goopy = value },
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public int Credit(GrowthValues rewards) => checked(
        HandOfGreed * rewards.HandOfGreed + TheHunt * rewards.TheHunt
        + Feed * rewards.Feed + Royalties * rewards.Royalties
        + Alchemize * rewards.Alchemize + GeneticAlgorithm * rewards.GeneticAlgorithm
        + TheScythe * rewards.TheScythe + Goopy * rewards.Goopy);

    public void ValidateBudgets()
    {
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
            if (Get(source) is < 0 or > 1000)
                throw new InvalidDataException($"Growth budget {source} must be in 0..1000 HP.");
    }
}
