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
    private readonly CheckButton _ignoreLongTermRewards;
    private bool _refreshing;
    private bool _disabled;

    public event Action<GrowthValues>? PolicyChanged;

    /// <summary>「不考虑局外收益」这个总开关变了。</summary>
    public event Action<bool>? IgnoreLongTermRewardsChanged;

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
        HBoxContainer ignoreRow = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Label ignoreLabel = SolverUiTokens.CreateLabel(
            "不考虑局外收益", SolverUiTokens.Type.Body, SolverUiTokens.Palette.TextPrimary);
        ignoreLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ignoreLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ignoreRow.AddChild(ignoreLabel);
        _ignoreLongTermRewards = SolverSettingsPanel.CreateToggle();
        _ignoreLongTermRewards.Name = "IgnoreLongTermRewards";
        _ignoreLongTermRewards.TooltipText =
            "打开后，金币、永久升级这类只在战斗之外兑现的收益一律不参与打分：既不付出任何战损去换，"
            + "也不再靠它们在搜索里保留路线。白拿的收益照样拿——最终选择里它仍然排在战损之后当平局的分先手。"
            + "后期没有商店、不需要这些收益时打开它；下面每一项额度在打开期间不生效。";
        ignoreRow.AddChild(_ignoreLongTermRewards);
        layout.AddChild(ignoreRow);
        layout.AddChild(new HSeparator());
        layout.AddChild(SolverUiTokens.CreateLabel("每次收益允许的额外战损", SolverUiTokens.Type.Body, SolverUiTokens.Palette.TextPrimary));
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
            input.TooltipText = $"{title}：每次实际获得局外收益允许的额外战损（HP）。0 仍优先获取同等战损下的收益；多次成功触发逐次累计。";
            _budgets.Add(source, input);
            input.ValueChanged += _ => Publish();
        }
        scroll.AddChild(rows);
        layout.AddChild(scroll);
        AddChild(layout);
        _ignoreLongTermRewards.Toggled += ignore =>
        {
            if (_refreshing)
                return;
            IgnoreLongTermRewardsChanged?.Invoke(ignore);
        };
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
        input.GetLineEdit().FocusExited += input.Apply;
        parent.AddChild(row);
        return input;
    }

    public void Refresh(bool disabled)
    {
        _refreshing = true;
        _disabled = disabled;
        try
        {
            SolverSettingsData settings = SolverSettings.Current;
            _ignoreLongTermRewards.Disabled = disabled;
            _ignoreLongTermRewards.ButtonPressed = settings.IgnoreLongTermRewards;
            // 总开关打开时下面每一项都不生效，所以灰掉：不是为了拦住输入，是让「填了没用」看得见。
            bool budgetsUsable = !disabled && !settings.IgnoreLongTermRewards;
            foreach ((GrowthSource source, SpinBox input) in _budgets)
            {
                input.Editable = budgetsUsable;
                if (!input.GetLineEdit().HasFocus())
                    input.SetValueNoSignal(settings.GrowthBudgets.Get(source));
            }
        }
        finally { _refreshing = false; }
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (IsVisibleInTree() && inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            && GetViewport().GuiGetFocusOwner() is LineEdit focused && IsAncestorOf(focused)
            && !new Rect2(Vector2.Zero, focused.Size).HasPoint(focused.GetGlobalTransformWithCanvas().AffineInverse() * click.Position))
            focused.ReleaseFocus();
    }

    internal bool ExerciseOutsideClickForTesting()
    {
        SpinBox input = _budgets[GrowthSource.GeneticAlgorithm];
        LineEdit edit = input.GetLineEdit();
        edit.GrabFocus();
        edit.Text = "7";
        using InputEventMouseButton click = new() { Pressed = true, ButtonIndex = MouseButton.Left, Position = new Vector2(-1, -1) };
        _Input(click);
        return !edit.HasFocus() && input.Value == 7 && SolverSettings.Current.GrowthBudgets.GeneticAlgorithm == 7;
    }

    private void Publish()
    {
        if (_refreshing)
            return;
        GrowthValues budgets = default;
        foreach ((GrowthSource source, SpinBox input) in _budgets)
            budgets = budgets.With(source, checked((int)input.Value));
        PolicyChanged?.Invoke(budgets);
    }

    internal bool SettingsConfiguredForTesting
        => _budgets.All(pair => (int)pair.Value.Value == SolverSettings.Current.GrowthBudgets.Get(pair.Key))
            && _ignoreLongTermRewards.ButtonPressed == SolverSettings.Current.IgnoreLongTermRewards
            && _budgets.Values.All(input =>
                input.Editable == (!SolverSettings.Current.IgnoreLongTermRewards && !_disabled));

    /// <summary>点一下总开关，返回它发出去的新值。</summary>
    internal bool ToggleIgnoreLongTermRewardsForTesting()
    {
        _ignoreLongTermRewards.ButtonPressed = !_ignoreLongTermRewards.ButtonPressed;
        return _ignoreLongTermRewards.ButtonPressed;
    }
}
