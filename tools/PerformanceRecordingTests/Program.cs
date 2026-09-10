using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CombatSolver;
using Microsoft.Diagnostics.Tracing;

if (args[0] == "trace")
{
    using EventPipeEventSource source = new(args[1]);
    long samples = 0, allocations = 0, collections = 0, contentions = 0;
    HashSet<int> threads = [];
    source.Dynamic.All += e =>
    {
        if (e.ProviderName == "Microsoft-DotNETCore-SampleProfiler") { samples++; threads.Add(e.ThreadID); }
    };
    source.Clr.GCAllocationTick += _ => allocations++;
    source.Clr.GCStart += _ => collections++;
    source.Clr.ContentionStart += _ => contentions++;
    source.Process();
    Console.WriteLine(JsonSerializer.Serialize(new { samples, allocations, collections, contentions, sampledThreads = threads.Count, source.EventsLost }));
    if (samples == 0 || allocations == 0 || collections == 0 || source.EventsLost != 0)
        throw new Exception("Trace coverage contract failed.");
    return;
}

string directory = Path.GetFullPath(args[0]);
using (PerformanceSession session = new(directory))
{
    File.WriteAllText(Path.Combine(directory, "target-pid.txt"), Environment.ProcessId.ToString());
    Stopwatch duration = Stopwatch.StartNew();
    int counter = 0;
    while (duration.Elapsed.TotalSeconds < 36)
    {
        // A deliberate 3-second stopped main-thread heartbeat, while the writer continues.
        if (duration.Elapsed.TotalSeconds < 12 || duration.Elapsed.TotalSeconds > 15) session.Heartbeat();
        session.Write(new { kind = "fixture", utcMs = PerformanceSession.Now, counter = counter++ });
        AllocateAndCompute();
        Thread.Sleep(10);
    }
    if (session.Failure != null || session.Dropped != 0) throw new Exception("Recorder failed: " + session.Failure);
}
using (JsonDocument end = JsonDocument.Parse(File.ReadLines(Path.Combine(directory, "timeline.jsonl")).Last()))
    if (end.RootElement.GetProperty("kind").GetString() != "writer_end") throw new Exception("Writer did not drain.");
bool sawHang = false, sawThread = false;
foreach (string line in File.ReadLines(Path.Combine(directory, "timeline.jsonl")))
{
    using JsonDocument row = JsonDocument.Parse(line);
    string? kind = row.RootElement.GetProperty("kind").GetString();
    sawThread |= kind == "thread";
    sawHang |= kind == "sample" && row.RootElement.GetProperty("heartbeatAgeMs").GetDouble() > 1500;
}
if (!sawHang || !sawThread) throw new Exception("Missing stopped-heartbeat or thread samples.");
Console.WriteLine("PASS: writer drain, process/GC/thread samples, stopped-heartbeat detection.");

[MethodImpl(MethodImplOptions.NoInlining)]
static void AllocateAndCompute()
{
    for (int i = 0; i < 20; i++)
    {
        byte[] bytes = new byte[100_000];
        for (int j = 0; j < bytes.Length; j += 8) bytes[j] = (byte)(j * 17);
        GC.KeepAlive(bytes);
    }
}
