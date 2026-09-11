using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CombatSolver;

internal sealed partial class SolverRelicStrategyPanel : PanelContainer
{
    internal const float PreferredWidth = 390f;
    private sealed record Row(RelicCounterCatalog.Entry Entry, CheckButton Enabled, SpinBox Minimum, SpinBox Maximum, SpinBox Hp, Label Status);
    private readonly List<Row> _rows = [];
    private readonly CheckButton _enabled;
    private bool _refreshing;
    public event Action<bool, RelicCounterRule[]>? PolicyChanged;

    public SolverRelicStrategyPanel()
    {
        Name = "RelicStrategyPanel";
        Visible = false;
        CustomMinimumSize = new(PreferredWidth, 0);
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", SolverUiTokens.CreateBox(SolverUiTokens.Palette.Surface,
            SolverUiTokens.Palette.BorderSubtle, SolverUiTokens.Radius.Medium, SolverUiTokens.Spacing.Sm, SolverUiTokens.Spacing.Sm));
        VBoxContainer layout = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(layout);
        HBoxContainer master = new();
        Label title = Text("控制战斗结束时的遗物计数");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        master.AddChild(title);
        _enabled = SolverSettingsPanel.CreateToggle();
        _enabled.Name = "RelicStrategyEnabled";
        master.AddChild(_enabled);
        layout.AddChild(master);
        layout.AddChild(Text("总开关与单项开关同时开启才生效。范围包含两端；每项达标最多折算一次额外战损，多个遗物额度相加。"));
        layout.AddChild(Text("只考虑当前持有的遗物。计数目标满足后，仍按原战损、成长和药水条件达标早停。"));
        ScrollContainer scroll = new() { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new(0, 160) };
        VBoxContainer rows = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", SolverUiTokens.Spacing.Sm);
        foreach (var entry in RelicCounterCatalog.All)
        {
            RelicModel relic = entry.Canonical();
            VBoxContainer group = new();
            HBoxContainer heading = new();
            Label name = SolverUiTokens.CreateLabel(relic.Title.GetFormattedText(), SolverUiTokens.Type.Body, SolverUiTokens.Palette.TextPrimary);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            heading.AddChild(name);
            CheckButton toggle = SolverSettingsPanel.CreateToggle();
            toggle.Name = entry.Id + "Enabled";
            heading.AddChild(toggle);
            group.AddChild(heading);
            Label status = Text("未持有");
            group.AddChild(status);
            HBoxContainer values = new();
            SpinBox minimum = Number(values, "最小", entry.Period - 1);
            SpinBox maximum = Number(values, "最大", entry.Period - 1);
            SpinBox hp = Number(values, "额外战损", 1000);
            hp.Suffix = "HP";
            group.AddChild(values);
            rows.AddChild(group);
            rows.AddChild(new HSeparator());
            Row row = new(entry, toggle, minimum, maximum, hp, status);
            _rows.Add(row);
            toggle.Toggled += _ => Publish();
            minimum.ValueChanged += _ => { if (!_refreshing && minimum.Value > maximum.Value) maximum.SetValueNoSignal(minimum.Value); Publish(); };
            maximum.ValueChanged += _ => { if (!_refreshing && maximum.Value < minimum.Value) minimum.SetValueNoSignal(maximum.Value); Publish(); };
            hp.ValueChanged += _ => Publish();
        }
        Button others = new() { Text = SolverText.Get("查看其他计数遗物"), ToggleMode = true };
        VBoxContainer inventory = new() { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (RelicModel relic in ModelDb.AllRelics.Where(relic => RelicCounterCatalog.Identify(relic) == null
            && relic.GetType().GetProperty(nameof(RelicModel.DisplayAmount))!.DeclaringType != typeof(RelicModel)))
            inventory.AddChild(Text(relic.Title.GetFormattedText() + " — " + SolverText.Get(RelicCounterCatalog.UnavailableReason(relic)), localized: true));
        inventory.AddChild(Text(ModelDb.Relic<Lantern>().Title.GetFormattedText() + " — " + SolverText.Get("首回合触发，没有跨战斗计数。"), localized: true));
        others.Toggled += visible => inventory.Visible = visible;
        rows.AddChild(others);
        rows.AddChild(inventory);
        scroll.AddChild(rows);
        layout.AddChild(scroll);
        _enabled.Toggled += _ => Publish();
        Refresh(false);
    }

    private static Label Text(string text, bool localized = false)
    {
        Label label = SolverUiTokens.CreateLabel(localized ? text : SolverText.Get(text), SolverUiTokens.Type.Caption, SolverUiTokens.Palette.TextSecondary);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static SpinBox Number(HBoxContainer parent, string label, int maximum)
    {
        VBoxContainer column = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(Text(label));
        SpinBox input = new() { MinValue = 0, MaxValue = maximum, Step = 1, Rounded = true,
            CustomMinimumSize = new(100, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill, UpdateOnTextChanged = false };
        input.GetLineEdit().FocusExited += input.Apply;
        column.AddChild(input);
        parent.AddChild(column);
        return input;
    }

    public void Refresh(bool disabled)
    {
        _refreshing = true;
        try
        {
            var settings = SolverSettings.Current;
            _enabled.Disabled = disabled;
            _enabled.SetPressedNoSignal(settings.RelicStrategyEnabled);
            var state = CombatManager.Instance.DebugOnlyGetState();
            foreach (Row row in _rows)
            {
                var rule = settings.RelicCounterRules.SingleOrDefault(rule => rule.Id == row.Entry.Id)
                    ?? new RelicCounterRule(row.Entry.Id, false, row.Entry.Period - 1, row.Entry.Period - 1, 0);
                row.Enabled.Disabled = disabled || !settings.RelicStrategyEnabled;
                row.Enabled.SetPressedNoSignal(rule.Enabled);
                bool editable = !disabled && settings.RelicStrategyEnabled && rule.Enabled;
                foreach (var pair in new[] { (row.Minimum, rule.Minimum), (row.Maximum, rule.Maximum), (row.Hp, rule.HpAllowance) })
                {
                    pair.Item1.Editable = editable;
                    if (!pair.Item1.GetLineEdit().HasFocus()) pair.Item1.SetValueNoSignal(pair.Item2);
                }
                bool owned = state?.Players.SelectMany(player => player.Relics).Any(relic => !relic.IsMelted && RelicCounterCatalog.Identify(relic) == row.Entry.Id) == true;
                row.Status.Text = SolverText.Get(owned ? "已持有" : "未持有");
            }
        }
        finally { _refreshing = false; }
    }

    private void Publish()
    {
        if (_refreshing) return;
        var rules = _rows.Select(row => new RelicCounterRule(row.Entry.Id, row.Enabled.ButtonPressed,
            (int)row.Minimum.Value, (int)row.Maximum.Value, (int)row.Hp.Value)).ToArray();
        PolicyChanged?.Invoke(_enabled.ButtonPressed, rules);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (IsVisibleInTree() && inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            && GetViewport().GuiGetFocusOwner() is LineEdit focused && IsAncestorOf(focused)
            && !new Rect2(Vector2.Zero, focused.Size).HasPoint(focused.GetGlobalTransformWithCanvas().AffineInverse() * click.Position))
            focused.ReleaseFocus();
    }

    internal bool ExerciseControlsForTesting()
    {
        bool? enabled = null;
        RelicCounterRule[]? changed = null;
        PolicyChanged += (on, rules) => { enabled = on; changed = rules; };
        Row first = _rows[0];
        first.Hp.Value = 7;
        first.Enabled.ButtonPressed = false;
        bool independent = changed is { Length: 10 } && !changed[0].Enabled && changed[0].HpAllowance == 7
            && changed.Skip(1).All(rule => rule.Enabled);
        _enabled.ButtonPressed = false;
        return independent && enabled == false && changed![0].HpAllowance == 7;
    }
}
