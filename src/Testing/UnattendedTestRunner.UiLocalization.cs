using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private async Task AssertUiLocalizationAsync()
    {
        using Stream stream = typeof(SolverText).Assembly.GetManifestResourceStream("CombatSolver.UI.English.json")!;
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        foreach ((string source, string english) in catalog)
        {
            CompositeFormat original = CompositeFormat.Parse(source);
            CompositeFormat translated = CompositeFormat.Parse(english);
            string[] Fields(string value) => Regex.Matches(value, @"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}")
                .Select(match => match.Value).Order().ToArray();
            if (original.MinimumArgumentCount != translated.MinimumArgumentCount
                || !Fields(source).SequenceEqual(Fields(english)))
                throw new InvalidOperationException($"Localization placeholders differ: {source}");
        }
        string language = LocManager.Instance.Language;
        try
        {
            foreach (string target in new[] { "eng", "zhs", "zht" })
            {
                LocManager.Instance.SetLanguage(target);
                await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.ProcessFrame);
                bool english = target == "eng";
                foreach ((string source, string translated) in catalog)
                {
                    if (SolverText.Get(source) != (english ? translated : source))
                        throw new InvalidOperationException($"Wrong locale selection: {target}/{source}");
                }
                string untouched = "玩家{0}[b]STRIKE[/b]";
                if (SolverText.Format($"联系QQ：{untouched}（可在“求解器设置”里修改）")
                    != (english ? $"QQ: {untouched} (change in Solver Settings)" : $"联系QQ：{untouched}（可在“求解器设置”里修改）"))
                    throw new InvalidOperationException("Localization changed inserted text.");
                string expectedNumber = (1.25).ToString("F1", CultureInfo.CurrentCulture);
                if (SolverText.Format($"已用 {1.25:F1} s") != (english ? $"Elapsed {expectedNumber} s" : $"已用 {expectedNumber} s"))
                    throw new InvalidOperationException("Localization changed numeric formatting.");

                Control harness = new() { Size = new Vector2(820, 900) };
                _host.AddChild(harness);
                try
                {
                    SolverSettingsPanel settings = new();
                    harness.AddChild(settings);
                    settings.Reload();
                    if (!settings.SettingsTabsConfiguredForTesting || !settings.UploadProgressConfiguredForTesting
                        || !settings.ExerciseSettingsTabSwitchingForTesting())
                        throw new InvalidOperationException($"Settings localization failed: {target}");
                    BugReportUploadDialog dialog = new("");
                    harness.AddChild(dialog);
                    if (english)
                        AssertEnglishControls(harness);
                    if (!settings.ExerciseUploadCompletionTransitionForTesting())
                        throw new InvalidOperationException($"Upload transitions failed: {target}");
                }
                finally { harness.Free(); }

                SolverOverlay.ShowSearching(_host, 3, false, 0);
                if (SolverOverlay.RouteHeadingForTesting != (english ? "Current candidate (unverified)" : "求解器当前考虑（尚未验证）")
                    || SolverOverlay.AdoptRouteButtonTextForTesting != (english ? "Use candidate" : "采用当前路线"))
                    throw new InvalidOperationException($"Dynamic overlay localization failed: {target}");
                string failure = SolverController.FormatSearchFailureForTesting(new InvalidOperationException(untouched), true);
                if (!failure.Contains(english ? "Search failed" : "计算失败", StringComparison.Ordinal)
                    || !failure.Contains(english ? "Off (one thread)" : "关闭（单线程）", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Failure instructions are not localized: {target}");
                _completedChecks.Add($"UiLocalization:{target}:Catalog{catalog.Count}:Settings:UploadTransitions:DynamicStatus:FailureInstructions");
            }
        }
        finally
        {
            LocManager.Instance.SetLanguage(language);
            SolverOverlay.Hide();
        }
    }

    private static void AssertEnglishControls(Node node)
    {
        static void Check(string text)
        {
            if (text.Any(character => character is >= '\u4e00' and <= '\u9fff'))
                throw new InvalidOperationException($"Untranslated English control: {text}");
        }
        if (node is Control control) Check(control.TooltipText);
        if (node is Label label) Check(label.Text);
        if (node is Button button) Check(button.Text);
        if (node is LineEdit input) Check(input.PlaceholderText);
        if (node is OptionButton options)
            for (int index = 0; index < options.ItemCount; index++) Check(options.GetItemText(index));
        foreach (Node child in node.GetChildren()) AssertEnglishControls(child);
    }
}
