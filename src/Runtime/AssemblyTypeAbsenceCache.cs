using System.Reflection;

namespace CombatSolver;

// Evidence from a completed, unsuccessful lookup across the loaded assemblies. A
// static assembly cannot acquire a new type without loading another assembly, while
// Reflection.Emit assemblies must still be queried on every use of the evidence.
internal sealed class AssemblyTypeAbsenceCache(string markerTypeName)
{
    private sealed record Absence(long Generation, WeakReference<Assembly>[] DynamicAssemblies);
    private static long _assemblyGeneration;
    private Absence? _absence;
    private long _hits;
    private long _probes;
    private long _bypasses;

    static AssemblyTypeAbsenceCache()
    {
        AppDomain.CurrentDomain.AssemblyLoad += static (_, _) =>
            Interlocked.Increment(ref _assemblyGeneration);
    }

    internal (long Hits, long Probes, long Bypasses) Counts
        => (Interlocked.Read(ref _hits), Interlocked.Read(ref _probes), Interlocked.Read(ref _bypasses));

    public long BeginProbe()
    {
        Interlocked.Increment(ref _probes);
        return Volatile.Read(ref _assemblyGeneration);
    }

    public void ObserveResult(long generation, Type? result)
    {
        if (result != null)
        {
            Volatile.Write(ref _absence, null);
            return;
        }
        if (generation != Volatile.Read(ref _assemblyGeneration))
            return;
        WeakReference<Assembly>[] dynamicAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => assembly.IsDynamic)
            .Select(static assembly => new WeakReference<Assembly>(assembly))
            .ToArray();
        if (generation == Volatile.Read(ref _assemblyGeneration))
            Volatile.Write(ref _absence, new Absence(generation, dynamicAssemblies));
    }

    public bool IsStillAbsent()
    {
        Absence? absence = Volatile.Read(ref _absence);
        if (absence != null && absence.Generation == Volatile.Read(ref _assemblyGeneration))
        {
            foreach (WeakReference<Assembly> weak in absence.DynamicAssemblies)
            {
                if (weak.TryGetTarget(out Assembly? assembly)
                    && assembly.GetType(markerTypeName, throwOnError: false) != null)
                {
                    Interlocked.CompareExchange(ref _absence, null, absence);
                    Interlocked.Increment(ref _bypasses);
                    return false;
                }
            }
            if (absence.Generation == Volatile.Read(ref _assemblyGeneration))
            {
                Interlocked.Increment(ref _hits);
                return true;
            }
        }
        Interlocked.Increment(ref _bypasses);
        return false;
    }
}
