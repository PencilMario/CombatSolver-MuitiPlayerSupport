using System.IO.Compression;
using System.Text.Json.Nodes;
using CombatSolver.Replay;

internal static class ArchiveContractTests
{
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "CombatSolver-ArchiveTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int assertions = 0;
        try
        {
            string valid = WriteFixture(root, "valid", false);
            JsonObject result = CheckpointArchive.Prepare(valid, "latest", Path.Combine(root, "import"));
            string metadata = result["paths"]!["metadataPath"]!.GetValue<string>();
            string replay = result["paths"]!["replayStatePath"]!.GetValue<string>();
            Check(metadata != replay && File.ReadAllText(metadata) != File.ReadAllText(replay), "separate_paired_json");
            Check(result["checkpoint"]!["checkpointId"]!.GetValue<string>() == "s:1", "latest_is_playable_before_end");
            Check(result["restorationVerified"]!.GetValue<bool>() == false, "preflight_is_not_restore_proof");
            Check(CheckpointArchive.Inspect(valid, "end")["checkpoint"]!["checkpointId"]!.GetValue<string>() == "s:2", "explicit_end_selector");
            string legacy = WriteFixture(root, "legacy", true);
            Check(CheckpointArchive.Inspect(legacy)["status"]!.GetValue<string>() == "materials_valid", "legacy_without_index");
            string mismatch = WriteFixture(root, "mismatch", false, wrongSession: true);
            Reject(() => CheckpointArchive.Inspect(mismatch), "checkpoint_pair_mismatch");
            string duplicate = WriteFixture(root, "duplicate", false, duplicate: true);
            Reject(() => CheckpointArchive.Inspect(duplicate), "duplicate_entry");
            foreach (string unsafePath in new[] { "../escape", "/absolute", "C:/absolute", "a\\b", "a/./b" })
                Reject(() => CheckpointArchive.ValidateEntryPath(unsafePath), "unsafe_entry");
            Console.WriteLine($"archive_contract_tests_passed assertions={assertions}");
            return 0;
        }
        finally
        {
            Directory.Delete(root, true);
        }

        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            assertions++;
        }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (InvalidDataException error) when (error.Message.StartsWith(reason, StringComparison.Ordinal))
            {
                assertions++;
                return;
            }
            throw new InvalidOperationException("expected_rejection:" + reason);
        }
    }

    private static string WriteFixture(string directory, string name, bool legacy, bool wrongSession = false, bool duplicate = false)
    {
        string path = Path.Combine(directory, name + ".zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        string prefix = "combat-solver/forensics/current/";
        Write(prefix + "session.json", "{\"sessionId\":\"s\"}");
        JsonArray checkpoints = [];
        for (int sequence = 1; sequence <= 2; sequence++)
        {
            string label = sequence == 1 ? "search_completed" : "combat_end";
            string file = $"{sequence:D6}-{label}.json";
            Write(prefix + "checkpoints/" + file, new JsonObject
            {
                ["sessionId"] = wrongSession ? "other" : "s", ["label"] = label,
                ["encounterId"] = "e", ["exactContinuationState"] = "root", ["playerPhase"] = "Play",
            }.ToJsonString());
            Write(prefix + "replay-state/" + file,
                """{"schemaVersion":1,"encounterId":"e","exactContinuationState":"root","ascensionLevel":0,"currentActIndex":0,"runRng":{"seed":"seed"},"players":[{"characterId":"c"}]}""");
            Write(prefix + "run-state/" + Path.ChangeExtension(file, ".save"), "{\"rng\":{\"seed\":\"seed\"}}");
            Write(prefix + "native-state/" + Path.ChangeExtension(file, ".bin"), "native");
            checkpoints.Add(new JsonObject
            {
                ["checkpointId"] = "s:" + sequence,
                ["metadataPath"] = prefix + "checkpoints/" + file,
                ["replayStatePath"] = prefix + "replay-state/" + file,
                ["nativeStatePath"] = prefix + "native-state/" + Path.ChangeExtension(file, ".bin"),
                ["runStatePath"] = prefix + "run-state/" + Path.ChangeExtension(file, ".save"),
            });
        }
        if (!legacy)
            Write(CheckpointArchive.IndexPath, new JsonObject
            {
                ["schemaVersion"] = 2, ["sessionId"] = "s", ["defaultCheckpointId"] = "s:1",
                ["combatEndCheckpointId"] = "s:2", ["checkpoints"] = checkpoints,
            }.ToJsonString());
        if (duplicate)
            Write(prefix + "session.json", "{}");
        return path;

        void Write(string entryName, string text)
        {
            using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
            writer.Write(text);
        }
    }
}
