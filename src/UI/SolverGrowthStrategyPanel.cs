using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Localization;

namespace CombatSolver;

internal sealed partial class SolverGrowthStrategyPanel : PanelContainer
{
    internal const float PreferredWidth = 272f;
    private readonly Dictionary<GrowthSource, SpinBox> _budgets = [];
    private readonly SpinBox _acceptable;
    private bool _refreshing;

    public event Action<GrowthValues, int>? PolicyChanged;

    public SolverGrowthStrategyPanel()
    {
        Name = "GrowthStrategyPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(PreferredWidth, 0);
        AddThemeStyleboxOverride("panel", SolverUiTokens.CreateBox(
            SolverUiTokens.Palette.Surface, SolverUiTokens.Palette.BorderSubtle,
            SolverUiTokens.Radius.Medium, SolverUiTokens.Spacing.Sm, SolverUiTokens.Spacing.Sm));
        VBoxContainer layout = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", SolverUiTokens.Spacing.Sm);
        layout.AddChild(SolverUiTokens.CreateLabel("战损与成长", SolverUiTokens.Type.Body, SolverUiTokens.Palette.TextPrimary));
        _acceptable = AddBudgetRow(layout, "可接受战损", null, SolverSettings.MaximumAcceptableBattleHpLoss);
        _acceptable.TooltipText = "无成长目标时，找到不超过此战损的胜利路线便停止搜索。有成长目标时继续比较收益；额度为 0 仍优先选择同战损下的成长。单位：HP";
        _acceptable.ValueChanged += _ => Publish();
        layout.AddChild(new HSeparator());
        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            CustomMinimumSize = new Vector2(0, 160),
        };
        VBoxContainer rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", SolverUiTokens.Spacing.Sm);
        foreach (GrowthSource source in Enum.GetValues<GrowthSource>())
        {
            CardModel card = SourceCard(source);
            string title = source == GrowthSource.Goopy ? ModelDb.Enchantment<Goopy>().Title.GetFormattedText() + "防御" : card.Title;
            SpinBox input = AddBudgetRow(rows, title, card.Portrait, 1000);
            input.Name = source.ToString();
            input.TooltipText = $"{title}：每次实际获得局外收益，可接受的额外战损（HP）";
            _budgets.Add(source, input);
            input.ValueChanged += _ => Publish();
        }
        scroll.AddChild(rows);
        layout.AddChild(scroll);
        AddChild(layout);
        Refresh(false);
    }

    private static CardModel SourceCard(GrowthSource source) => source switch
    {
        GrowthSource.HandOfGreed => ModelDb.Card<HandOfGreed>(),
        GrowthSource.TheHunt => ModelDb.Card<TheHunt>(),
        GrowthSource.Feed => ModelDb.Card<Feed>(),
        GrowthSource.Royalties => ModelDb.Card<Royalties>(),
        GrowthSource.Alchemize => ModelDb.Card<Alchemize>(),
        GrowthSource.GeneticAlgorithm => ModelDb.Card<GeneticAlgorithm>(),
        GrowthSource.TheScythe => ModelDb.Card<TheScythe>(),
        GrowthSource.Goopy => ModelDb.Card<DefendIronclad>(),
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static SpinBox AddBudgetRow(VBoxContainer parent, string title, Texture2D? texture, int maximum)
    {
        HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 42) };
        if (texture != null)
            row.AddChild(new TextureRect
            {
                Texture = texture, CustomMinimumSize = new Vector2(36, 36),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
        Label label = SolverUiTokens.CreateLabel(title, SolverUiTokens.Type.Caption, SolverUiTokens.Palette.TextPrimary);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        SpinBox input = new()
        {
            MinValue = 0, MaxValue = maximum, Step = 1, Rounded = true,
            CustomMinimumSize = new Vector2(96, 36), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Suffix = "HP", UpdateOnTextChanged = false,
        };
        row.AddChild(input);
        parent.AddChild(row);
        return input;
    }

    public void Refresh(bool disabled)
    {
        _refreshing = true;
        try
        {
            SolverSettingsData settings = SolverSettings.Current;
            _acceptable.Editable = !disabled;
            if (!_acceptable.GetLineEdit().HasFocus())
                _acceptable.SetValueNoSignal(settings.AcceptableBattleHpLoss);
            foreach ((GrowthSource source, SpinBox input) in _budgets)
            {
                input.Editable = !disabled;
                if (!input.GetLineEdit().HasFocus())
                    input.SetValueNoSignal(settings.GrowthBudgets.Get(source));
            }
        }
        finally { _refreshing = false; }
    }

    private void Publish()
    {
        if (_refreshing)
            return;
        GrowthValues budgets = default;
        foreach ((GrowthSource source, SpinBox input) in _budgets)
            budgets = budgets.With(source, checked((int)input.Value));
        PolicyChanged?.Invoke(budgets, checked((int)_acceptable.Value));
    }

    internal bool SettingsConfiguredForTesting => (int)_acceptable.Value == SolverSettings.Current.AcceptableBattleHpLoss
        && _budgets.All(pair => (int)pair.Value.Value == SolverSettings.Current.GrowthBudgets.Get(pair.Key));
}
