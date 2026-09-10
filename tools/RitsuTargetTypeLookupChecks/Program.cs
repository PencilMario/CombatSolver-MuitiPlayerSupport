using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CombatSolver;
using HarmonyLib;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {++checks}: {name}");
}
var target = RitsuBaseLibTargetTypeLookupPatch.GetTargets().Single();
var method = target.TargetType.GetMethod(target.MethodName,
    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
    null, [typeof(Assembly)], null)!;
object? instance = method.IsStatic ? null : Activator.CreateInstance(target.TargetType, nonPublic: true);
Type? Query(Assembly assembly) => (Type?)method.Invoke(instance, [assembly]);
Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
Type?[] original = loaded.Select(Query).ToArray();
Check(original.SequenceEqual(loaded.Select(assembly => assembly.GetType(
    RitsuBaseLibTargetTypeLookupPatch.MarkerTypeName, throwOnError: false))), "current Ritsu callback is exactly the marker lookup");
new Harmony("combat-solver-target-type-contract").Patch(method,
    prefix: new HarmonyMethod(typeof(RitsuBaseLibTargetTypeLookupPatch).GetMethod("Prefix")!));
SimulationNotificationIsolation.IsActive = false;
Check(original.SequenceEqual(loaded.Select(Query)), "live callback remains original");
SimulationNotificationIsolation.IsActive = true;
Check(original.SequenceEqual(loaded.Select(Query)), "all loaded assembly results identical");
Check(Query(typeof(Program).Assembly) == typeof(BaseLib.Patches.Features.CustomTargetType), "present marker remains the same Type");
Type? ignored = null;
for (int index = 0; index < 10_000; index++)
    RitsuBaseLibTargetTypeLookupPatch.Prefix(typeof(object).Assembly, ref ignored);
long before = GC.GetAllocatedBytesForCurrentThread();
for (int index = 0; index < 100_000; index++)
    ignored = typeof(object).Assembly.GetType(RitsuBaseLibTargetTypeLookupPatch.MarkerTypeName, throwOnError: false);
long originalAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
before = GC.GetAllocatedBytesForCurrentThread();
for (int index = 0; index < 100_000; index++)
    RitsuBaseLibTargetTypeLookupPatch.Prefix(typeof(object).Assembly, ref ignored);
long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Check(originalAllocated > 0 && allocated < originalAllocated / 100 && ignored == null,
    $"100000 cached absent probes remove over 99% of lookup allocation ({originalAllocated} -> {allocated})");
var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LateTargetMarker"), AssemblyBuilderAccess.RunAndCollect);
var module = dynamicAssembly.DefineDynamicModule("marker");
Check(Query(dynamicAssembly) == null, "dynamic marker initially absent");
Type emitted = module.DefineType(RitsuBaseLibTargetTypeLookupPatch.MarkerTypeName, TypeAttributes.Public).CreateType()!;
Check(Query(dynamicAssembly) == emitted, "late dynamic type is visible without cache invalidation");
Parallel.For(0, 2000, index =>
{
    SimulationNotificationIsolation.IsActive = true;
    if (Query(typeof(object).Assembly) != null || Query(typeof(Program).Assembly) != typeof(BaseLib.Patches.Features.CustomTargetType))
        throw new InvalidOperationException("parallel metadata result drift");
});
Check(true, "parallel positive and negative lookups remain exact");
WeakReference collectible = ProbeCollectibleAssembly();
for (int attempt = 0; collectible.IsAlive && attempt < 8; attempt++)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
}
Check(!collectible.IsAlive, "cache does not retain an unloaded collectible assembly");
SimulationNotificationIsolation.IsActive = false;
Check(Query(typeof(Program).Assembly) == typeof(BaseLib.Patches.Features.CustomTargetType), "live callback restored after simulation");
const string lateMarker = "CombatSolver.Tests.AbsentThenEmittedMarker";
var absence = new AssemblyTypeAbsenceCache(lateMarker);
Type? ProbeAll(string name) => AppDomain.CurrentDomain.GetAssemblies()
    .Select(assembly => assembly.GetType(name, throwOnError: false)).FirstOrDefault(type => type != null);
void RecordAbsence()
{
    long generation = absence.BeginProbe();
    Type? result = ProbeAll(lateMarker);
    Check(result == null, "full marker probe is absent");
    absence.ObserveResult(generation, result);
}
Check(!absence.IsStillAbsent(), "no absence accepted before a completed original lookup");
absence.BeginProbe();
Check(!absence.IsStillAbsent(), "failed or unfinished original lookup publishes no evidence");
RecordAbsence();
Check(absence.IsStillAbsent(), "unchanged assemblies reuse completed absence");
var emptyAssembly = AssemblyBuilder.DefineDynamicAssembly(
    new AssemblyName("AbsenceCacheLateAssembly"), AssemblyBuilderAccess.RunAndCollect);
var emptyModule = emptyAssembly.DefineDynamicModule("late");
Check(!absence.IsStillAbsent(), "a new empty assembly invalidates previous absence");
RecordAbsence();
Check(absence.IsStillAbsent(), "new empty dynamic assembly is checked and remains absent");
long beforeDefinition = absence.BeginProbe();
Type lateType = emptyModule.DefineType(lateMarker, TypeAttributes.Public).CreateType()!;
Check(absence.BeginProbe() == beforeDefinition, "emitting a type need not load another assembly");
Check(!absence.IsStillAbsent() && ProbeAll(lateMarker) == lateType,
    "new type in an existing dynamic assembly remains visible");
absence.ObserveResult(beforeDefinition, lateType);
Check(!absence.IsStillAbsent(), "a positive result never authorizes the absence shortcut");
var bridge = RitsuBaseLibTargetTypeResolution.Target("EnsureResolved").TargetType;
Check(bridge.GetMethod("EnsureResolved", BindingFlags.Static | BindingFlags.NonPublic,
    null, Type.EmptyTypes, null)?.ReturnType == typeof(void)
    && bridge.GetMethod("ResolveBaseLibCustomTargetType", BindingFlags.Static | BindingFlags.NonPublic,
        null, Type.EmptyTypes, null)?.ReturnType == typeof(Type),
    "current Ritsu resolver patch signatures match");
Check(RitsuBaseLibTargetTypeResolutionPatch.Prefix(), "live resolution always runs the original method");
RitsuBaseLibTargetTypeEvidencePatch.Prefix(out long liveState);
Check(liveState == -1, "live resolution cannot publish simulation-only evidence");
Console.WriteLine($"ABSENCE_COUNTS {absence.Counts}");
Console.WriteLine($"RITSU_TARGET_TYPE_LOOKUP_CHECKS_OK checks={checks}");

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference ProbeCollectibleAssembly()
{
    var context = new AssemblyLoadContext("lookup-cache-collectible", isCollectible: true);
    Assembly assembly = context.LoadFromAssemblyPath(typeof(Program).Assembly.Location);
    Type? result = null;
    if (RitsuBaseLibTargetTypeLookupPatch.Prefix(assembly, ref result) || result is null)
        throw new InvalidOperationException("collectible positive lookup did not run");
    var weak = new WeakReference(assembly);
    context.Unload();
    return weak;
}
namespace CombatSolver
{
    internal static class SimulationNotificationIsolation
    {
        [ThreadStatic] private static bool _active;
        internal static bool IsActive { get => _active; set => _active = value; }
    }
}
namespace BaseLib.Patches.Features
{
    public sealed class CustomTargetType;
}
