using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using CombatSolver.Replay;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private JsonObject? _checkpointImport;
    private string? _checkpointImportDirectory;

    private void PrepareCheckpointRequest()
    {
        if (string.IsNullOrWhiteSpace(_request.CheckpointArchivePath))
            return;
        SetStage("archive_preflight");
        if (_request.ReplayMode is not ("RestoreOnly" or "SearchOnly" or "DeploySolver" or "ReplayRecorded"))
            throw new InvalidDataException($"unsupported_replay_mode:{_request.ReplayMode}");
        _checkpointImportDirectory = Path.Combine(
            ProjectSettings.GlobalizePath("user://checkpoint-imports"), Guid.NewGuid().ToString("N"));
        _checkpointImport = CheckpointArchive.Prepare(
            _request.CheckpointArchivePath, _request.CheckpointSelector, _checkpointImportDirectory);
        _writer.ReplayVerification = new JsonObject
        {
            ["mode"] = _request.ReplayMode,
            ["status"] = _checkpointImport["status"]!.DeepClone(),
            ["reason"] = _checkpointImport["reason"]?.DeepClone(),
            ["checkpoint"] = _checkpointImport["checkpoint"]?.DeepClone(),
            ["recordedPolicy"] = _checkpointImport["recordedPolicy"]?.DeepClone(),
            ["restorationVerified"] = false,
            ["comparisonScope"] = "checkpoint",
        };
        if (_checkpointImport["status"]!.GetValue<string>() != "materials_valid")
            throw new InvalidDataException(_checkpointImport["reason"]!.GetValue<string>());
        JsonObject index = _checkpointImport["index"]!.AsObject();
        if (index["build"] is JsonObject build
            && build["gameModuleId"]?.GetValue<string>() != typeof(MegaCrit.Sts2.Core.Combat.CombatState)
                .Assembly.ManifestModule.ModuleVersionId.ToString())
        {
            _writer.ReplayVerification["status"] = "environment_mismatch";
            throw new InvalidDataException("environment_mismatch:gameModuleId");
        }
        if (_request.ReplayMode == "ReplayRecorded")
            throw new InvalidDataException("missing_native_event_recording");
        JsonObject input = JsonSerializer.SerializeToNode(_request, UnattendedTestFiles.JsonOptions)!.AsObject();
        foreach ((string key, JsonNode? value) in _checkpointImport["request"]!.AsObject())
            input[key] = value?.DeepClone();
        input["runSnapshotPath"] = _checkpointImport["paths"]!["runStatePath"]!.DeepClone();
        input["replayStatePath"] = _checkpointImport["paths"]!["replayStatePath"]!.DeepClone();
        if (_request.ReplayMode == "RestoreOnly")
            input["stopAfterCombatRootSnapshotAssertion"] = true;
        if (_request.ReplayMode == "SearchOnly")
            input["stopAfterInitialSolverResultAssertion"] = true;
        if (_request.ReplayMode == "DeploySolver")
        {
            input["deploymentFastModeForTest"] = "Instant";
            input["deploymentInterActionDelaySecondsForTest"] = 0;
            input["expectedUnexpectedReplansAtMost"] = 0;
        }
        _request = input.Deserialize<UnattendedTestRequest>(UnattendedTestFiles.JsonOptions)!;
    }

    private void RecordCheckpointRestored()
    {
        if (_writer.ReplayVerification == null)
            return;
        _writer.ReplayVerification["restorationVerified"] = true;
        _writer.ReplayVerification["status"] = "restored";
        _completedChecks.Add("CheckpointContinuationMatched");
    }

    private SolverSettingsData ApplyRecordedCheckpointPolicy(SolverSettingsData current)
    {
        if (_checkpointImport == null)
            return current;
        if (_checkpointImport["recordedPolicy"] is not JsonObject recorded)
        {
            if (_request.ReplayMode is "SearchOnly" or "DeploySolver")
                throw new InvalidDataException("legacy_missing_effective_policy");
            return current;
        }
        JsonObject settings = JsonSerializer.SerializeToNode(current, UnattendedTestFiles.JsonOptions)!.AsObject();
        foreach (string name in new[] { "potionDirectives", "actTransitionBossHpStrategy", "finalBossHpStrategy", "acceptableBattleHpLoss", "searchMaxDegreeOfParallelism" })
            settings[name] = recorded[name]?.DeepClone() ?? throw new InvalidDataException($"missing_policy:{name}");
        SolverSearchProfile shortProfile = recorded["shortProfile"]!.Deserialize<SolverSearchProfile>(UnattendedTestFiles.JsonOptions)!;
        SolverSearchProfile deepProfile = recorded["deepProfile"]!.Deserialize<SolverSearchProfile>(UnattendedTestFiles.JsonOptions)!;
        SolverSettingsData restored = settings.Deserialize<SolverSettingsData>(UnattendedTestFiles.JsonOptions)!;
        return restored with
        {
            PotionPolicy = recorded["potionPolicy"]!.Deserialize<SolverPotionPolicy>(UnattendedTestFiles.JsonOptions),
            PerformancePreset = SolverPerformancePreset.Custom,
            ShortBeamWidth = shortProfile.BeamWidth,
            DeepBeamWidth = deepProfile.BeamWidth,
            ShortMaxExpandedNodes = shortProfile.MaxExpandedNodes,
            DeepMaxExpandedNodes = deepProfile.MaxExpandedNodes,
            ShortMaxCardBranchesPerNode = shortProfile.MaxCardBranchesPerNode,
            DeepMaxCardBranchesPerNode = deepProfile.MaxCardBranchesPerNode,
            ShortMaxPileChoiceBranchesPerAction = shortProfile.MaxPileChoiceBranchesPerAction,
            DeepMaxPileChoiceBranchesPerAction = deepProfile.MaxPileChoiceBranchesPerAction,
            ShortMaxHandChoiceBranchesPerAction = shortProfile.MaxHandChoiceBranchesPerAction,
            DeepMaxHandChoiceBranchesPerAction = deepProfile.MaxHandChoiceBranchesPerAction,
            ShortTimeLimitSeconds = shortProfile.SoftTimeBudgetMilliseconds / 1000d,
            DeepTimeLimitSeconds = deepProfile.SoftTimeBudgetMilliseconds / 1000d,
        };
    }

    private void ReleaseCheckpointImport()
    {
        if (_checkpointImportDirectory != null && Directory.Exists(_checkpointImportDirectory))
            Directory.Delete(_checkpointImportDirectory, recursive: true);
    }
}
