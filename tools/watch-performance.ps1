param(
    [Parameter(Mandatory)][int]$TargetProcessId,
    [Parameter(Mandatory)][string]$SessionDirectory,
    [Parameter(Mandatory)][string]$TraceToolPath,
    [int]$SegmentSeconds = 300,
    [string]$GameLogDirectory = ''
)
$ErrorActionPreference = 'Stop'
$SessionDirectory = [IO.Path]::GetFullPath($SessionDirectory)
[IO.Directory]::CreateDirectory($SessionDirectory) | Out-Null
$journalPath = Join-Path $SessionDirectory 'collector.jsonl'
$healthPath = Join-Path $SessionDirectory 'collector-health.json'
$collector = $null
$dump = $null
$dumpRequest = 0L
$dumpPath = ''
$dumpTool = $null
$configurationPath = Join-Path $SessionDirectory 'configuration.json'
if ([IO.File]::Exists($configurationPath)) {
    $configuration = [IO.File]::ReadAllText($configurationPath) | ConvertFrom-Json
    $dumpTool = $configuration.DumpToolPath
}
function Record($value) {
    $value.utcMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    [IO.File]::AppendAllText($journalPath, (($value | ConvertTo-Json -Compress -Depth 8) + "`n"))
}
function Health([string]$status, [string]$detail, [long]$bytes = 0) {
    $value = @{ status = $status; detail = $detail; bytes = $bytes; utcMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
    $temporary = "$healthPath.tmp"
    [IO.File]::WriteAllText($temporary, ($value | ConvertTo-Json -Compress))
    if ([IO.File]::Exists($healthPath)) { [IO.File]::Replace($temporary, $healthPath, "$healthPath.backup") }
    else { [IO.File]::Move($temporary, $healthPath) }
}
function PollSnapshot {
    if ($null -ne $dump) {
        $dump.Refresh()
        if ($dump.HasExited) {
            $dump.WaitForExit()
            Record @{ kind = 'snapshot_end'; request = $dumpRequest; exitCode = $dump.ExitCode; path = $dumpPath }
            if ($dump.ExitCode -ne 0) { [IO.File]::WriteAllText((Join-Path $SessionDirectory 'SNAPSHOT_FAILED.txt'), "Memory snapshot failed: $($dump.ExitCode)") }
            $dump.Dispose()
            $script:dump = $null
        }
        return
    }
    $requestPath = Join-Path $SessionDirectory 'snapshot-request.json'
    if (!$dumpTool -or ![IO.File]::Exists($requestPath)) { return }
    $request = [IO.File]::ReadAllText($requestPath) | ConvertFrom-Json
    if ([long]$request.id -eq $dumpRequest) { return }
    $script:dumpRequest = [long]$request.id
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($SessionDirectory))
    $target.Refresh()
    if ($drive.AvailableFreeSpace -lt ($target.PrivateMemorySize64 + 5GB)) { throw 'Not enough free disk space for the requested heap snapshot.' }
    $script:dumpPath = Join-Path $SessionDirectory "heap-$dumpRequest.dmp"
    Record @{ kind = 'snapshot_start'; request = $dumpRequest; path = $dumpPath; note = 'Explicit user capture; process suspension can affect frame timing.' }
    $script:dump = Start-Process -FilePath $dumpTool -ArgumentList @('collect', '-p', "$TargetProcessId", '--type', 'Heap', '-o', ('"' + $dumpPath + '"')) `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $SessionDirectory "heap-$dumpRequest.stdout.txt") `
        -RedirectStandardError (Join-Path $SessionDirectory "heap-$dumpRequest.stderr.txt")
    $null = $dump.Handle
}
try {
    $target = [Diagnostics.Process]::GetProcessById($TargetProcessId)
    $startedAt = $target.StartTime.ToUniversalTime()
    Record @{ kind = 'start'; target = $TargetProcessId; processStart = $startedAt.ToString('O'); segmentSeconds = $SegmentSeconds; tool = $TraceToolPath }
    $segment = 0
    $totalBytes = 0L
    while (!$target.HasExited) {
        $segment++
        $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($SessionDirectory))
        if ($drive.AvailableFreeSpace -lt 5GB) {
            throw 'Trace recording stopped: less than 5 GB free disk space.'
        }
        $tracePath = Join-Path $SessionDirectory ('trace-{0:D4}.nettrace' -f $segment)
        $stdoutPath = Join-Path $SessionDirectory ('trace-{0:D4}.stdout.txt' -f $segment)
        $stderrPath = Join-Path $SessionDirectory ('trace-{0:D4}.stderr.txt' -f $segment)
        $duration = [TimeSpan]::FromSeconds($SegmentSeconds).ToString('dd\:hh\:mm\:ss')
        Health 'starting' $tracePath
        Record @{ kind = 'segment_start'; segment = $segment; path = $tracePath }
        # Start-Process joins arguments; quote the output path explicitly. Other arguments are fixed values/integers.
        $arguments = @('collect', '--process-id', "$TargetProcessId", '--duration', $duration,
            '--buffersize', '128', '--profile', 'dotnet-common,dotnet-sampled-thread-time',
            '--providers', 'Microsoft-Windows-DotNETRuntime:0x100003C01D:5',
            '--output', ('"' + $tracePath + '"'))
        $collector = Start-Process -FilePath $TraceToolPath -ArgumentList $arguments -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        # Retain the process handle before Refresh/HasExited; Windows PowerShell otherwise loses ExitCode.
        $null = $collector.Handle
        $start = [DateTime]::UtcNow
        $lastBytes = 0L
        $lastGrowth = [DateTime]::UtcNow
        while (!$collector.HasExited) {
            if ($drive.AvailableFreeSpace -lt 5GB) { throw 'Trace recording stopped: less than 5 GB free disk space.' }
            PollSnapshot
            $bytes = if ([IO.File]::Exists($tracePath)) { ([IO.FileInfo]::new($tracePath)).Length } else { 0L }
            if ($bytes -gt $lastBytes) { $lastGrowth = [DateTime]::UtcNow; $lastBytes = $bytes }
            $stalled = ([DateTime]::UtcNow - $lastGrowth).TotalSeconds -gt 30
            $status = if ($bytes -gt 0 -and !$stalled) { 'recording' } else { 'starting' }
            Health $status $tracePath $bytes
            $target.Refresh()
            Record @{ kind = 'collector_sample'; segment = $segment; bytes = $bytes; status = $status;
                targetExited = $target.HasExited; collectorCpuMs = $collector.TotalProcessorTime.TotalMilliseconds;
                collectorWorkingSet = $collector.WorkingSet64; freeDisk = $drive.AvailableFreeSpace }
            if (!$target.HasExited -and ([DateTime]::UtcNow - $start).TotalSeconds -gt ($SegmentSeconds + 120)) {
                throw 'Trace collector exceeded segment duration plus 120 seconds; capture is incomplete.'
            }
            if ($target.HasExited -and ([DateTime]::UtcNow - $lastGrowth).TotalSeconds -gt 30) {
                throw 'Game exited but trace collector failed to finalize within 30 seconds.'
            }
            Start-Sleep -Seconds 2
            $collector.Refresh()
        }
        $collector.WaitForExit()
        $code = $collector.ExitCode
        $bytes = if ([IO.File]::Exists($tracePath)) { ([IO.FileInfo]::new($tracePath)).Length } else { 0L }
        $totalBytes += $bytes
        Record @{ kind = 'segment_end'; segment = $segment; exitCode = $code; bytes = $bytes; elapsedSeconds = ([DateTime]::UtcNow - $start).TotalSeconds }
        $collector.Dispose()
        $collector = $null
        $target.Refresh()
        if ($code -ne 0) { throw "Trace collector exited with code $code. See segment stderr/stdout." }
        if ($bytes -eq 0) { throw 'Trace collector produced an empty trace.' }
    }
    $dumpDeadline = [DateTime]::UtcNow.AddSeconds(30)
    while ($null -ne $dump) {
        PollSnapshot
        if ([DateTime]::UtcNow -gt $dumpDeadline) { throw 'Memory snapshot did not finalize after game exit.' }
        if ($null -ne $dump) { Start-Sleep -Seconds 1 }
    }
    if ($GameLogDirectory -and [IO.File]::Exists((Join-Path $GameLogDirectory 'godot.log'))) {
        Copy-Item -LiteralPath (Join-Path $GameLogDirectory 'godot.log') -Destination (Join-Path $SessionDirectory 'godot.log')
    }
    Record @{ kind = 'complete'; segments = $segment; totalTraceBytes = $totalBytes }
    Health 'complete' 'Game process exited; all trace segments finalized.' $totalBytes
}
catch {
    if ($null -ne $collector -and !$collector.HasExited) { $collector.Kill() }
    if ($null -ne $dump -and !$dump.HasExited) { $dump.Kill() }
    Record @{ kind = 'failed'; error = $_.ToString() }
    Health 'failed' $_.ToString()
    [IO.File]::WriteAllText((Join-Path $SessionDirectory 'COLLECTOR_FAILED.txt'), $_.ToString())
    exit 1
}
