using System.Reflection;
using System.Runtime.CompilerServices;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Patching.Models;

namespace CombatSolver;

// Cache immutable assembly metadata, not framework presence or target predicates. In
// particular, new assemblies still enter Ritsu's original ordered enumeration, and
// dynamic assemblies must remain queryable after a new type has been emitted.
internal sealed class RitsuBaseLibTargetTypeLookupPatch : IPatchMethod
{
    internal const string MarkerTypeName = "BaseLib.Patches.Features.CustomTargetType";
    private const string BridgeTypeName =
        "STS2RitsuLib.Combat.CardTargeting.BaseLibTargetTypeBridge";
    private const string ResolverName = "ResolveBaseLibCustomTargetType";
    private sealed record Resolution(Type? Type);
    private static readonly ConditionalWeakTable<Assembly, Resolution> Resolutions = new();

    public static string PatchId => "combat_solver_ritsu_baselib_target_type_lookup_cache";
    public static string Description => "模拟期间复用静态程序集的 BaseLib 目标类型查询结果";

    public static ModPatchTarget[] GetTargets()
    {
        Type bridge = typeof(ModelCapabilities).Assembly.GetType(BridgeTypeName)
            ?? throw new TypeLoadException(BridgeTypeName);
        MethodInfo[] callbacks = bridge.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.Name.StartsWith($"<{ResolverName}>", StringComparison.Ordinal)
                && method.ReturnType == typeof(Type)
                && method.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(Assembly))
            .ToArray();
        if (callbacks.Length != 1)
            throw new MissingMethodException(BridgeTypeName,
                $"{ResolverName}: expected one Assembly -> Type lookup callback, found {callbacks.Length}");
        MethodInfo callback = callbacks[0];
        return [new(callback.DeclaringType!, callback.Name, [typeof(Assembly)])];
    }

    public static bool Prefix(Assembly __0, ref Type? __result)
    {
        if (!SimulationNotificationIsolation.IsActive || __0.IsDynamic)
            return true;
        __result = Resolutions.GetValue(__0, static assembly =>
            new Resolution(assembly.GetType(MarkerTypeName, throwOnError: false))).Type;
        return false;
    }
}
