using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace CombatSolver;

internal static class SolverLocaleRefresh
{
    private static readonly HashSet<Action> Refreshers = [];
    private static bool _subscribed;
    private static bool _queued;
    private static string? _language;

    public static void Bind(Control owner, Action refresh)
    {
        if (!_subscribed)
        {
            _language = LocManager.Instance.Language;
            LocManager.Instance.SubscribeToLocaleChange(Queue);
            _subscribed = true;
        }
        owner.TreeEntered += () => { Refreshers.Add(refresh); refresh(); };
        owner.TreeExiting += () => Refreshers.Remove(refresh);
    }

    private static void Queue()
    {
        if (_queued) return;
        _queued = true;
        Callable.From(Refresh).CallDeferred();
    }

    private static void Refresh()
    {
        _queued = false;
        string language = LocManager.Instance.Language;
        if (_language == language) return;
        _language = language;
        foreach (Action refresh in Refreshers.ToArray()) refresh();
    }

    internal static int SubscriptionCountForTesting => Refreshers.Count;
}
