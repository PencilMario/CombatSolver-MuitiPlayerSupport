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
    private readonly List<(GrowthSourceHandle Source, SpinBox Input)> _extraBudgets = [];
    private bool _refreshing;

    public event Action<GrowthValues>? PolicyChanged;

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
        // 第三方登记的来源排在原版八行之后，按登记顺序。
        foreach (GrowthSourceMirrors.Entry entry in GrowthSourceMirrors.All)
        {
            (string title, Texture2D? portrait) = ResolveThirdPartyRow(entry);
            SpinBox input = AddBudgetRow(rows, title, portrait, 1000);
            input.Name = entry.Id;
            input.TooltipText = $"{title}：每次实际获得局外收益允许的额外战损（HP）。0 仍优先获取同等战损下的收益；多次成功触发逐次累计。";
            _extraBudgets.Add((new GrowthSourceHandle(entry.Id), input));
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

    /// <summary>
    /// 取第三方来源这一行的标题和图标。取牌函数是 mod 提供的，抛异常不该连带整个侧栏起不来：
    /// 这一行退化成「没有图标、标题显示 id」，额度照样能填、照样进搜索。
    /// </summary>
    private static (string Title, Texture2D? Portrait) ResolveThirdPartyRow(GrowthSourceMirrors.Entry entry)
    {
        try
        {
            CardModel card = entry.Card();
            return (entry.Title?.Invoke(card) ?? card.Title, card.Portrait);
        }
        catch (Exception exception)
        {
            Entry.Logger.Warn(
                $"[CombatSolver] 第三方成长来源 {entry.Id} 的取牌或取标题函数抛了异常，"
                + $"这一行退化成纯文字：{exception}");
            return (entry.Id, null);
        }
    }

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
        try
        {
            SolverSettingsData settings = SolverSettings.Current;
            foreach ((GrowthSource source, SpinBox input) in _budgets)
            {
                input.Editable = !disabled;
                if (!input.GetLineEdit().HasFocus())
                    input.SetValueNoSignal(settings.GrowthBudgets.Get(source));
            }
            foreach ((GrowthSourceHandle source, SpinBox input) in _extraBudgets)
            {
                input.Editable = !disabled;
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
        foreach ((GrowthSourceHandle source, SpinBox input) in _extraBudgets)
            budgets = budgets.With(source, checked((int)input.Value));
        // 侧栏只认得已登记的来源；玩家临时停用某个 mod 期间，设置文件里它那份额度原样留着。
        budgets = budgets with { Extras = SolverSettings.Current.GrowthBudgets.Extras.MergeUnregistered(budgets.Extras) };
        PolicyChanged?.Invoke(budgets);
    }

    internal bool SettingsConfiguredForTesting
        => _budgets.All(pair => (int)pair.Value.Value == SolverSettings.Current.GrowthBudgets.Get(pair.Key))
            && _extraBudgets.All(row => (int)row.Input.Value == SolverSettings.Current.GrowthBudgets.Get(row.Source));

    internal IReadOnlyList<(GrowthSourceHandle Source, SpinBox Input)> ThirdPartyRowsForTesting => _extraBudgets;
}
