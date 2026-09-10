using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace CombatSolver;

internal static partial class CombatSearchCoordinator
{
    // Wider beams can lose a narrow beam's useful trajectory when newly admitted parents
    // generate competing children. Retry a failed layer once with the standard profile,
    // preserving its policy constraints and spending only that layer's remaining budget.
    internal static SolverResult SolveWithNarrowBeamRecovery(
        CombatRootSnapshot root,
        SearchPolicySnapshot policy,
        SolverSearchProfile profile,
        CancellationToken searchCancellationToken,
        CancellationToken callerCancellationToken,
        Func<SolverSearchProfile, CancellationToken, SolverResult> solve)
    {
        SearchRequestWorkTotals totals = policy.RequestWorkTotals
            ?? throw new InvalidOperationException("Beam 恢复需要请求级工作量记录。");
        long expandedBefore = totals.Snapshot().ExpandedNodes;
        Stopwatch clock = Stopwatch.StartNew();
        SolverResult? original = null;
        ExceptionDispatchInfo? originalPolicyFailure = null;
        try
        {
            original = solve(profile, searchCancellationToken);
        }
        catch (PotionPolicyUnsatisfiedException exception)
        {
            originalPolicyFailure = ExceptionDispatchInfo.Capture(exception);
        }

        SolverResult ReturnOriginal()
        {
            if (original != null)
                return original;
            originalPolicyFailure!.Throw();
            throw new UnreachableException();
        }

        callerCancellationToken.ThrowIfCancellationRequested();
        if (original != null
            && (original.ResultScope != SolverResultScope.SearchCompletion
                || IsCompleteVictory(original)
                || original.BoundaryReason is SearchBoundaryReason.NodeLimit
                    or SearchBoundaryReason.TimeLimit))
        {
            return original;
        }
        if (searchCancellationToken.IsCancellationRequested
            || policy.Interaction?.CurrentTakeoverRequest != null)
        {
            return ReturnOriginal();
        }

        long originalExpanded = totals.Snapshot().ExpandedNodes - expandedBefore;
        SolverSearchProfile? recoveryProfile = BuildNarrowBeamRecoveryProfile(
            profile,
            originalExpanded,
            clock.ElapsedMilliseconds);
        if (recoveryProfile == null)
            return ReturnOriginal();

        policy.Diagnostics.Info(
            $"[CombatSolver/Test] NARROW_BEAM_RECOVERY start " +
            $"beam={profile.BeamWidth}->{recoveryProfile.BeamWidth} " +
            $"original_expanded={originalExpanded} " +
            $"remaining_nodes={recoveryProfile.MaxExpandedNodes} " +
            $"remaining_ms={recoveryProfile.SoftTimeBudgetMilliseconds} " +
            $"policy_missing={originalPolicyFailure != null}");
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(searchCancellationToken);
        deadline.CancelAfter(recoveryProfile.SoftTimeBudgetMilliseconds);
        SolverResult recovery;
        try
        {
            recovery = solve(recoveryProfile, deadline.Token);
        }
        catch (PotionPolicyUnsatisfiedException)
        {
            policy.Diagnostics.Info(
                "[CombatSolver/Test] NARROW_BEAM_RECOVERY result policy_missing=true selected=original");
            callerCancellationToken.ThrowIfCancellationRequested();
            return ReturnOriginal();
        }
        catch (OperationCanceledException)
            when (deadline.IsCancellationRequested
                && !callerCancellationToken.IsCancellationRequested)
        {
            policy.Diagnostics.Info(
                "[CombatSolver/Test] NARROW_BEAM_RECOVERY result deadline=true selected=original");
            callerCancellationToken.ThrowIfCancellationRequested();
            return ReturnOriginal();
        }

        callerCancellationToken.ThrowIfCancellationRequested();
        if (recovery.ResultScope != SolverResultScope.SearchCompletion)
            return recovery;
        bool selectRecovery = original == null
            || IsCompleteVictory(recovery)
                && IsBetterPotionPolicyResult(root, policy, recovery, original);
        policy.Diagnostics.Info(
            $"[CombatSolver/Test] NARROW_BEAM_RECOVERY result " +
            $"won={IsCompleteVictory(recovery)} selected={(selectRecovery ? "recovery" : "original")} " +
            $"expanded={totals.Snapshot().ExpandedNodes - expandedBefore}");
        return selectRecovery ? recovery : ReturnOriginal();
    }

    internal static SolverSearchProfile? BuildNarrowBeamRecoveryProfile(
        SolverSearchProfile profile,
        long expandedNodes,
        long elapsedMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expandedNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);
        SolverSearchProfile standard = profile.Phase == SolverSearchPhase.Short
            ? SolverSearchProfile.Short
            : SolverSearchProfile.Deep;
        long remainingNodes = profile.MaxExpandedNodes - expandedNodes;
        long remainingMilliseconds = profile.SoftTimeBudgetMilliseconds - elapsedMilliseconds;
        if (profile.BeamWidth <= standard.BeamWidth
            || remainingNodes <= 0
            || remainingMilliseconds <= 0)
        {
            return null;
        }

        // 收窄的是搜索面（Beam 宽度和分支上限），不是玩家配的预算。节点和时间用调用方这一层
        // 剩下的部分：这个机制只在 Beam 比内置档位宽时才触发，也就是只为 High 以上的玩家运行，
        // 把他们的预算夹回内置档位等于先把他们配的东西拿掉，而这里恰恰是主搜索一条胜利路线都
        // 没找到、最需要多给的时候。两侧都仍然被调用方自己的预算封顶。
        return profile with
        {
            RecoverDeferredTurnFrontier = true,
            BeamWidth = standard.BeamWidth,
            MaxExpandedNodes = (int)remainingNodes,
            MaxCardBranchesPerNode = Math.Min(
                profile.MaxCardBranchesPerNode,
                standard.MaxCardBranchesPerNode),
            MaxPileChoiceBranchesPerAction = Math.Min(
                profile.MaxPileChoiceBranchesPerAction,
                standard.MaxPileChoiceBranchesPerAction),
            MaxHandChoiceBranchesPerAction = Math.Min(
                profile.MaxHandChoiceBranchesPerAction,
                standard.MaxHandChoiceBranchesPerAction),
            SoftTimeBudgetMilliseconds = (int)remainingMilliseconds,
        };
    }
}
