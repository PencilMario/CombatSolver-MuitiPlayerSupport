using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace CombatSolver;

internal sealed partial class CombatBeamSolver
{
    private void PrepareStandPatProbes(IEnumerable<SearchNode> nodes)
    {
        ParallelExpansionExecutor? executor = _run.ActiveParallelExpansion;
        if (executor == null)
            return;
        List<SearchNode> pending = [];
        HashSet<StateFingerprint> seen = [];
        foreach (SearchNode node in nodes)
        {
            // Preserve the first original cache representative. The caller will consume these
            // exact nodes in the same order and perform the same selection after preparation.
            if (!_run.StandPatCache.ContainsKey(node.StateKey) && seen.Add(node.StateKey))
                pending.Add(node);
        }
        if (pending.Count < 2)
            return;
        StandPatEvaluation[] evaluations = executor.EvaluateStandPatProbes(pending);
        for (int index = 0; index < pending.Count; index++)
        {
            _run.StandPatCache.Add(pending[index].StateKey, evaluations[index]);
            _run.StandPatProbes++;
        }
    }

    private sealed partial class ParallelExpansionExecutor
    {
        private int _activeStandPatWorkers;

        public StandPatEvaluation[] EvaluateStandPatProbes(IReadOnlyList<SearchNode> nodes)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ExpansionLane[] lanes = EnsureBackgroundLanes();
            using StandPatJobWave wave = new(DegreeOfParallelism);
            StandPatEvaluation[] evaluations = new StandPatEvaluation[nodes.Count];
            long startedAt = Stopwatch.GetTimestamp();
            int next = 0;
            int active = 0;
            ExceptionDispatchInfo? firstError = null;

            void Dispatch(int lane)
            {
                _coordinator.SearchCancellationToken.ThrowIfCancellationRequested();
                StandPatJob job = new(nodes[next], next, lane, wave);
                wave.Completed.AddCount();
                try { lanes[lane].Dispatch(job); }
                catch
                {
                    wave.Completed.Signal();
                    throw;
                }
                next++;
                active++;
            }

            try
            {
                for (int lane = 0; lane < DegreeOfParallelism && next < nodes.Count; lane++)
                    Dispatch(lane);
                while (active > 0)
                {
                    StandPatJobOutcome outcome = wave.Take();
                    active--;
                    _coordinator.MergeExpansionWorker(outcome.Worker, outcome.AllocatedBytes);
                    _workProfile.Record(ParallelExpansionWorkProfile.Kind.StandPat,
                        outcome.ElapsedTicks, outcome.Concurrency);
                    firstError ??= outcome.Error;
                    if (firstError != null)
                        continue;
                    evaluations[outcome.Job.Index] = outcome.Evaluation;
                    if (next < nodes.Count)
                        Dispatch(outcome.Job.Lane);
                }
                firstError?.Throw();
                return evaluations;
            }
            finally
            {
                // Prune already owns these live candidate roots. No new expansion is admitted,
                // and no checkpoint may release them until all probe lanes have completed.
                wave.Completed.Signal();
                wave.Completed.Wait();
                while (wave.TryTake(out StandPatJobOutcome? pending))
                    _coordinator.MergeExpansionWorker(pending!.Worker, pending.AllocatedBytes);
                _workProfile.Record(ParallelExpansionWorkProfile.Kind.StandPatWave,
                    Stopwatch.GetTimestamp() - startedAt);
            }
        }

        private sealed class StandPatJobWave(int capacity) : IDisposable
        {
            private readonly object _gate = new();
            private readonly Queue<StandPatJobOutcome> _outcomes = new(capacity);
            public CountdownEvent Completed { get; } = new(1);

            public void Publish(StandPatJobOutcome outcome)
            {
                lock (_gate)
                {
                    _outcomes.Enqueue(outcome);
                    Monitor.Pulse(_gate);
                }
            }

            public StandPatJobOutcome Take()
            {
                lock (_gate)
                {
                    while (_outcomes.Count == 0)
                        Monitor.Wait(_gate);
                    return _outcomes.Dequeue();
                }
            }

            public bool TryTake(out StandPatJobOutcome? outcome)
            {
                lock (_gate)
                    return _outcomes.TryDequeue(out outcome);
            }

            public void Dispose() => Completed.Dispose();
        }

        private sealed class StandPatJobOutcome(StandPatJob job, CombatBeamSolver worker)
        {
            public StandPatJob Job { get; } = job;
            public CombatBeamSolver Worker { get; } = worker;
            public StandPatEvaluation Evaluation;
            public ExceptionDispatchInfo? Error;
            public long AllocatedBytes;
            public long ElapsedTicks;
            public int Concurrency;
        }

        private sealed record StandPatJob(SearchNode Node, int Index, int Lane, StandPatJobWave Wave)
            : IExpansionLaneWorkItem
        {
            public void Execute(ParallelExpansionExecutor owner, CombatBeamSolver worker)
            {
                StandPatJobOutcome outcome = new(this, worker);
                long allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
                long startedAt = Stopwatch.GetTimestamp();
                outcome.Concurrency = Interlocked.Increment(ref owner._activeStandPatWorkers);
                try
                {
                    worker.SearchCancellationToken.ThrowIfCancellationRequested();
                    // First representatives have distinct immutable state keys and snapshots.
                    // No other expansion/probe job can fork this same candidate simultaneously.
                    outcome.Evaluation = worker.ComputeStandPat(Node);
                }
                catch (System.Exception error)
                {
                    outcome.Error = ExceptionDispatchInfo.Capture(error);
                }
                finally
                {
                    Interlocked.Decrement(ref owner._activeStandPatWorkers);
                    outcome.AllocatedBytes = Math.Max(
                        0, GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart);
                    outcome.ElapsedTicks = Stopwatch.GetTimestamp() - startedAt;
                    Wave.Publish(outcome);
                }
            }

            public void Signal() => Wave.Completed.Signal();
        }
    }
}
