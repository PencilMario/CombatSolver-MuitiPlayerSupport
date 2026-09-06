using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace CombatSolver;

internal sealed partial class UnattendedTestRunner
{
    private static void AssertNativeCheckpoint(CombatState state, string? nativePath)
    {
        if (string.IsNullOrWhiteSpace(nativePath))
            return;
        byte[] expected = File.ReadAllBytes(nativePath);
        NetFullCombatState actual = NetFullCombatState.FromRun(state.RunState, justFinishedAction: null);
        PacketWriter writer = new() { WarnOnGrow = false };
        actual.Serialize(writer);
        writer.ZeroByteRemainder();
        ReadOnlySpan<byte> bytes = writer.Buffer.AsSpan(0, writer.BytePosition);
        if (bytes.SequenceEqual(expected))
            return;
        int offset = 0;
        while (offset < Math.Min(expected.Length, bytes.Length) && expected[offset] == bytes[offset])
            offset++;
        PacketReader reader = new();
        reader.Reset(expected);
        NetFullCombatState saved = reader.Read<NetFullCombatState>();
        throw new InvalidDataException($"native_state_mismatch:byte={offset}:expected_bytes={expected.Length}:actual_bytes={bytes.Length}\nEXPECTED\n{saved}\nACTUAL\n{actual}");
    }
}
