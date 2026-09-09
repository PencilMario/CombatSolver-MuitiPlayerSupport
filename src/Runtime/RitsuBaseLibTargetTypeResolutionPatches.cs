using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Patching.Models;

namespace CombatSolver;

internal static class RitsuBaseLibTargetTypeResolution
{
    internal const string BridgeTypeName =
        "STS2RitsuLib.Combat.CardTargeting.BaseLibTargetTypeBridge";
    internal static readonly AssemblyTypeAbsenceCache MissingType =
        new(RitsuBaseLibTargetTypeLookupPatch.MarkerTypeName);

    internal static ModPatchTarget Target(string method)
        => new(typeof(ModelCapabilities).Assembly.GetType(BridgeTypeName)
            ?? throw new TypeLoadException(BridgeTypeName), method, []);
}

internal sealed class RitsuBaseLibTargetTypeResolutionPatch : IPatchMethod
{
    public static string PatchId => "combat_solver_ritsu_baselib_target_resolution_absence";
    public static string Description => "模拟期间复用当前程序集的 BaseLib 目标类型缺失证据";
    public static ModPatchTarget[] GetTargets()
        => [RitsuBaseLibTargetTypeResolution.Target("EnsureResolved")];

    public static bool Prefix()
        => !SimulationNotificationIsolation.IsActive
            || !RitsuBaseLibTargetTypeResolution.MissingType.IsStillAbsent();
}

internal sealed class RitsuBaseLibTargetTypeEvidencePatch : IPatchMethod
{
    public static string PatchId => "combat_solver_ritsu_baselib_target_resolution_evidence";
    public static string Description => "记录原版目标类型查询完成后的程序集代次";
    public static ModPatchTarget[] GetTargets()
        => [RitsuBaseLibTargetTypeResolution.Target("ResolveBaseLibCustomTargetType")];

    public static void Prefix(out long __state)
        => __state = SimulationNotificationIsolation.IsActive
            ? RitsuBaseLibTargetTypeResolution.MissingType.BeginProbe()
            : -1;

    public static void Postfix(Type? __result, long __state)
    {
        // Exceptions never reach this postfix; an incomplete/failed lookup is no evidence.
        if (__state >= 0)
            RitsuBaseLibTargetTypeResolution.MissingType.ObserveResult(__state, __result);
    }
}
