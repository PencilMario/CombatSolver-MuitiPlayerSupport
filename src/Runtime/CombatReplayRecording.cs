using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2RitsuLib.Patching.Models;

namespace CombatSolver;

internal sealed record RecordedCombatEvent(long Sequence, string Origin, byte[] Payload, uint? DuringActionId,
    string? Kind = null, string? Description = null);
internal sealed record RecordedCombatOrigin(
    byte[] RunSave, uint NextActionId, uint NextHookId, uint[] ChoiceIds,
    int[] RewardIds, string GameVersion, string GameCommit, uint ModelIdHash);
internal sealed record RecordedCombatArchive(
    RecordedCombatOrigin Origin, RecordedCombatEvent[] Events, string? IncompleteReason);

// Records the native input protocol. Combat histories are reconstructed by executing
// these inputs, not by interpreting reflection-based diagnostic field dumps.
internal sealed class CombatReplayRecording : IDisposable
{
    private const long MaximumBufferedBytes = 8L * 1024 * 1024;
    private static CombatReplayRecording? _pending;
    internal static CombatReplayRecording? Pending => _pending;
    internal static Action<RecordedCombatEvent>? TestObserver { get; set; }
    internal static Action<CombatState>? TestCombatStartObserver { get; set; }
    internal static Action<CombatState>? TestCombatEndObserver { get; set; }
    internal static Action<SolverResult>? TestSearchResultObserver { get; set; }
    private ActionQueueSet? _actions;
    private PlayerChoiceSynchronizer? _choices;
    private readonly List<RecordedCombatEvent> _events = [];
    private readonly RecordedCombatOrigin _origin;
    private long _bufferedBytes;
    private string? _incompleteReason;
    private bool _disposed;

    public long EventCursor => _events.Count;
    public string? IncompleteReason => _incompleteReason;

    private CombatReplayRecording(SerializableRun run)
    {
        RunManager manager = RunManager.Instance;
        _actions = manager.ActionQueueSet;
        _choices = manager.PlayerChoiceSynchronizer;
        _origin = new RecordedCombatOrigin(
            JsonSerializer.SerializeToUtf8Bytes(run, JsonSerializationUtility.GetTypeInfo<SerializableRun>()),
            _actions.NextActionId, manager.ActionQueueSynchronizer.NextHookId,
            _choices.ChoiceIds.ToArray(), manager.RewardsSetSynchronizer.GetNextRewardIds().ToArray(),
            ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "UNRELEASED",
            ReleaseInfoManager.Instance.ReleaseInfo?.Commit ?? "UNKNOWN",
            ModelIdSerializationCache.Hash);
        _bufferedBytes = _origin.RunSave.LongLength;
        _actions.ActionEnqueued += OnAction;
        _actions.ActionResumed += OnResume;
        _choices.PlayerChoiceReceived += OnChoice;
    }

    internal static void Start(SerializableRun run)
    {
        _pending?.Dispose();
        _pending = run.Players.Count == 1 ? new CombatReplayRecording(run) : null;
    }

    public void MarkIncomplete(string reason) => _incompleteReason ??= reason;

    // Called on the main thread. Records and origin bytes are immutable after capture.
    public RecordedCombatArchive Capture()
        => new(_origin, _events.ToArray(), _incompleteReason);

    private void OnAction(GameAction action)
    {
        if (!CombatManager.Instance.IsInProgress)
            return;
        if (action is GenericHookGameAction hook)
        {
            Record(new CombatReplayEvent
            {
                playerId = action.OwnerId, eventType = CombatReplayEventType.HookAction,
                hookId = hook.HookId, gameActionType = action.ActionType,
            }, "system", action.ToString());
        }
        else if (action.RecordableToReplay)
        {
            Record(new CombatReplayEvent
            {
                playerId = action.OwnerId, eventType = CombatReplayEventType.GameAction,
                action = action.ToNetAction(),
            }, action is ReadyToBeginEnemyTurnAction ? "system"
                : SolverController.IsDeploying ? "solver" : "player", action.ToString());
        }
        else
        {
            MarkIncomplete($"unrecordable_action:{action.GetType().FullName}");
        }
    }

    private void OnResume(uint actionId)
    {
        if (CombatManager.Instance.IsInProgress)
            Record(new CombatReplayEvent
            {
                eventType = CombatReplayEventType.ResumeAction, actionId = actionId,
            }, "system", $"Resume action {actionId}");
    }

    private void OnChoice(Player player, uint choiceId, NetPlayerChoiceResult result)
    {
        if (CombatManager.Instance.IsInProgress)
            Record(new CombatReplayEvent
            {
                eventType = CombatReplayEventType.PlayerChoice, playerId = player.NetId,
                choiceId = choiceId, playerChoiceResult = result,
            }, SolverController.IsDeploying || PlayerTurnSetupCoordinator.IsDrivingChoiceForRecording ? "solver" : "player",
                $"Choice {choiceId}: {result.type}; indexes={string.Join(',', result.indexes ?? [])}");
    }

    private void Record(CombatReplayEvent value, string origin, string? description)
    {
        if (_incompleteReason != null)
            return;
        PacketWriter writer = new() { WarnOnGrow = false };
        value.Serialize(writer);
        writer.ZeroByteRemainder();
        byte[] payload = writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
        if (_bufferedBytes + payload.LongLength > MaximumBufferedBytes)
        {
            MarkIncomplete("event_buffer_limit");
            return;
        }
        RecordedCombatEvent captured = new(_events.Count, origin, payload,
            RunManager.Instance.ActionExecutor.CurrentlyRunningAction?.Id, value.eventType.ToString(), description);
        _events.Add(captured);
        _bufferedBytes += payload.LongLength;
        TestObserver?.Invoke(captured);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_actions != null)
        {
            _actions.ActionEnqueued -= OnAction;
            _actions.ActionResumed -= OnResume;
            _actions = null;
        }
        if (_choices != null)
        {
            _choices.PlayerChoiceReceived -= OnChoice;
            _choices = null;
        }
    }
}

internal sealed class CombatReplayRecordingPatch : IPatchMethod
{
    public static string PatchId => "combat_solver_record_native_combat_inputs";
    public static string Description => "从原生战前存档记录单场动作与选择";
    public static ModPatchTarget[] GetTargets() =>
        [new(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState), [typeof(SerializableRun)])];
    public static void Postfix(SerializableRun serializableRun)
        => CombatReplayRecording.Start(serializableRun);
}
