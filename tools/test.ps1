<#
Builds once, then runs the tests as parallel `dotnet test --no-build` processes and prints one summary.

  powershell -ExecutionPolicy Bypass -File tools\test.ps1                  # full: every test, the release gate
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Preset quick    # no human suites, fewer seeds
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3        # one TOP phase's tests
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3 -Preset quick
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3 -SunnySeeds 2000   # with its soak

AnoMech.Tests runs as fixed shards, each its own process since a run installs statics. A shard takes the
tests its terms match that no earlier shard took, and the last takes the rest, so together they run every
test once, a test added later included. AnoMech.Relay.Tests runs as one more process, except for a phase.
At most -Jobs processes run at once, by default half the logical processors.

A green full run of a clean tree writes "green <commit> full" to TestResults\green-stamp.txt, which the
release script requires. A full run removes it first, so one that fails or is stopped leaves none.
#>
[CmdletBinding()]
param(
    [ValidateSet('full', 'quick', 'phase')][string]$Preset = 'full',
    [ValidateSet('P2', 'P3', 'P4', 'P5', 'P6')][string]$Phase,
    [ValidateRange(1, 64)][int]$Jobs,
    [ValidateRange(1, 100000)][int]$SunnySeeds,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$TestProject = Join-Path $Root 'tests\AnoMech.Tests\AnoMech.Tests.csproj'
$RelayProject = Join-Path $Root 'tests\AnoMech.Relay.Tests\AnoMech.Relay.Tests.csproj'
$Stamp = Join-Path $Root 'TestResults\green-stamp.txt'
$Runs = Join-Path $Root 'TestResults\gate'
$KeepRuns = 10

# A shard takes a test any of its terms matches. A term is atoms joined by & (Property~Value, Property=Value or
# their negations !~ and !=, no spaces). The NUnit adapter reads ~ as a case-sensitive substring and !~ as
# exactly its complement, so negating the earlier shards' terms leaves no test out and none twice. A shard
# whose tests are all [Explicit] would run them: the adapter takes such a filter as asking for them.
# Balanced on a plain run's times, 4-4.5 single-process minutes each: the P3 Hello World human suite, near
# half the suite, split by walking style, each beside other heavy classes, and the quick preset's tests
# spread over the shards as well.
$HelloWorldHumans = 'FullyQualifiedName~TopP3HelloWorldLpduHumanTests'
$Shards = @(
    @{ Name = 'hello-sprinting'; Terms = @("$HelloWorldHumans&Name~Sprinting", 'FullyQualifiedName~LinkInterruptionTests') }
    @{ Name = 'hello-early'; Terms = @("$HelloWorldHumans&Name~Early", 'FullyQualifiedName~AnoMech.Tests.TopP6&FullyQualifiedName!~HumanTests') }
    @{ Name = 'hello-lanes'; Terms = @("$HelloWorldHumans&Name~Lanes", 'FullyQualifiedName~DebugBotReplayTests', 'FullyQualifiedName~PeerParityTests') }
    @{ Name = 'hello-straight'; Terms = @("$HelloWorldHumans&Name~Straight", 'FullyQualifiedName~TopP6GauntletExaflares2LpduHumanTests') }
    @{ Name = 'hello-curving'; Terms = @("$HelloWorldHumans&Name~Curving", 'FullyQualifiedName~TopP5DeltaLpduMarginTests') }
    @{ Name = 'hello-stretching'; Terms = @("$HelloWorldHumans&Name~Stretching", 'FullyQualifiedName~TopP5DeltaLpduHumanTests') }
    @{ Name = 'p5-omega-p3-p4'; Terms = @('FullyQualifiedName~TopP5OmegaLpduHumanTests', 'FullyQualifiedName~TopP5OmegaLpduMarginTests', 'FullyQualifiedName~TopP3IntermissionLpduHumanTests', 'FullyQualifiedName~TopP4BlueScreenLpduHumanTests') }
    @{ Name = 'rest'; Terms = @() }
)

# Heavy human suites: every seat scripted through every roll.
$HumanSuites = 'FullyQualifiedName~HumanTests'

# A phase's own classes, and the catalog-wide tests' cases for its scenarios ("The Omega Protocol - P3 ...").
$PhaseTerms = @{
    P2 = @('FullyQualifiedName~AnoMech.Tests.TopP2', 'Name~Protocol&Name~P2')
    P3 = @('FullyQualifiedName~AnoMech.Tests.TopP3', 'Name~Protocol&Name~P3')
    P4 = @('FullyQualifiedName~AnoMech.Tests.TopP4', 'Name~Protocol&Name~P4')
    P5 = @('FullyQualifiedName~AnoMech.Tests.TopP5', 'Name~Protocol&Name~P5')
    P6 = @('FullyQualifiedName~AnoMech.Tests.TopP6', 'Name~Protocol&Name~P6')
}

function Find-Tool([string]$name, [string]$fallback) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if (Test-Path $fallback) { return $fallback }
    throw "$name not found."
}
$git = Find-Tool 'git' 'C:\Program Files\Git\cmd\git.exe'
$dotnet = Find-Tool 'dotnet' 'C:\Program Files\dotnet\dotnet.exe'

function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
}

function Get-Negation([string]$term) {
    $atoms = foreach ($atom in $term -split '&') {
        if ($atom -notmatch '^(\w+)(!?)([~=])([^\s&|()!]+)$') { throw "Filter atom '$atom' isn't Property~Value, Property=Value or a negation." }
        '{0}{1}{2}{3}' -f $Matches[1], $(if ($Matches[2]) { '' } else { '!' }), $Matches[3], $Matches[4]
    }
    '(' + ($atoms -join '|') + ')'
}

function Get-AnyOf([string[]]$terms) {
    '(' + (($terms | ForEach-Object { "($_)" }) -join '|') + ')'
}

function Get-ShardFilter([int]$index, [string[]]$restrictions) {
    $shard = $Shards[$index]
    $last = $index -eq $Shards.Count - 1
    if ($last -ne ($shard.Terms.Count -eq 0)) { throw 'The last shard, and only the last, takes the rest and has no terms.' }
    $parts = @()
    if (-not $last) { $parts += Get-AnyOf $shard.Terms }
    for ($j = 0; $j -lt $index; $j++) { foreach ($term in $Shards[$j].Terms) { $parts += Get-Negation $term } }
    $parts += $restrictions
    $parts -join '&'
}

function Quote([string]$argument) {
    if ($argument -notmatch '[\s"]') { return $argument }
    '"' + ($argument -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function New-RunSettings([hashtable]$parameters, [string]$path) {
    $xml = [xml](Get-Content -Raw (Join-Path $Root 'tests\AnoMech.Tests\test.runsettings'))
    $list = $xml.RunSettings.TestRunParameters
    foreach ($name in $parameters.Keys) {
        $node = @($list.Parameter | Where-Object { $_.name -eq $name })[0]
        if (-not $node) {
            $node = $xml.CreateElement('Parameter')
            $node.SetAttribute('name', $name)
            [void]$list.AppendChild($node)
        }
        $node.SetAttribute('value', [string]$parameters[$name])
    }
    $xml.Save($path)
}

# A process writes its trx even when its filter matches nothing, so a missing one means it broke.
function Read-Trx([string]$path) {
    $result = [ordered]@{
        Read = $false; Total = 0; Passed = 0; Failed = 0; Skipped = 0
        Failures = New-Object Collections.Generic.List[string]
        Tests = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    }
    if (-not (Test-Path $path)) { return $result }
    try { $trx = [xml](Get-Content -Raw -Encoding UTF8 $path) } catch { return $result }
    $result.Read = $true
    $counters = $trx.TestRun.ResultSummary.Counters
    $result.Total = [int]$counters.total
    $result.Passed = [int]$counters.passed
    $result.Failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
    $result.Skipped = $result.Total - $result.Passed - $result.Failed
    $classes = @{}
    foreach ($test in @($trx.TestRun.TestDefinitions.UnitTest)) { if ($test) { $classes[$test.id] = $test.TestMethod.className } }
    foreach ($run in @($trx.TestRun.Results.UnitTestResult)) {
        if (-not $run) { continue }
        $name = '{0}.{1}' -f $classes[$run.testId], $run.testName
        [void]$result.Tests.Add($name)
        if ($run.outcome -in 'Failed', 'Error', 'Timeout', 'Aborted') { $result.Failures.Add($name) }
    }
    $result
}

function Format-Span([TimeSpan]$span) { '{0}:{1:00}' -f [int][Math]::Floor($span.TotalMinutes), $span.Seconds }

if ($Preset -eq 'phase' -and -not $Phase) { throw '-Preset phase needs -Phase P2, P3, P4, P5 or P6.' }
if ($Phase -and $Preset -eq 'full') { $Preset = 'phase' }
$label = "$Preset$(if ($Phase) { " $Phase" })"

$commit = (& $git -C $Root rev-parse HEAD).Trim()
$dirty = [bool](& $git -C $Root status --porcelain)
$stampable = $Preset -eq 'full' -and -not $PSBoundParameters.ContainsKey('SunnySeeds') -and -not $NoBuild
if ($Preset -eq 'full' -and (Test-Path $Stamp)) { Remove-Item -Force $Stamp }

$results = Join-Path $Runs ('{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), ($label -replace ' ', '-'))
New-Item -ItemType Directory -Force $results | Out-Null
Get-ChildItem $Runs -Directory | Sort-Object Name -Descending | Select-Object -Skip $KeepRuns |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$restrictions = @()
$parameters = @{}
if ($Preset -eq 'quick') {
    $restrictions += Get-Negation $HumanSuites
    $parameters.SunnySeeds = 4
    $parameters.PauseSeeds = 1
}
if ($Phase) { $restrictions += Get-AnyOf $PhaseTerms[$Phase] }
if ($PSBoundParameters.ContainsKey('SunnySeeds')) { $parameters.SunnySeeds = $SunnySeeds }
$settings = $null
if ($parameters.Count -gt 0) {
    $settings = Join-Path $results 'test.runsettings'
    New-RunSettings $parameters $settings
}

$wall = [Diagnostics.Stopwatch]::StartNew()
if (-not $NoBuild) {
    Write-Host "Building $(Split-Path -Leaf $Root) @ $($commit.Substring(0, 7))$(if ($dirty) { ' (uncommitted changes)' })"
    & $dotnet build (Join-Path $Root 'AnoMech.sln') -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.' -ForegroundColor Red; exit 1 }
}

function New-Run([string]$name, [string[]]$arguments, [string[]]$runSettings = @()) {
    $all = $arguments + @('--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $results $name))
    if ($runSettings.Count -gt 0) { $all += @('--') + $runSettings }
    [pscustomobject]@{
        Name      = $name
        Arguments = $all
        Trx       = Join-Path $results "$name\$name.trx"
        Log       = Join-Path $results "$name.log"
    }
}

$queue = New-Object Collections.Generic.List[object]
for ($i = 0; $i -lt $Shards.Count; $i++) {
    $arguments = @('test', $TestProject, '--no-build', '--filter', (Get-ShardFilter $i $restrictions))
    if ($settings) { $arguments += @('--settings', $settings) }
    # Past this many selected tests (2000 by default) the NUnit adapter runs the whole assembly and only drops the
    # others' results.
    $queue.Add((New-Run $Shards[$i].Name $arguments @('NUnit.AssemblySelectLimit=1000000')))
}
if (-not $Phase) { $queue.Add((New-Run 'relay' @('test', $RelayProject, '--no-build'))) }
if (-not $PSBoundParameters.ContainsKey('Jobs')) { $Jobs = [Math]::Max(1, [Math]::Min($queue.Count, [int][Math]::Floor([Environment]::ProcessorCount / 2))) }

Write-Host "Running $($queue.Count) processes, $Jobs at a time ($label)"
$running = @()
$done = @()
try {
    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        while ($queue.Count -gt 0 -and $running.Count -lt $Jobs) {
            $next = $queue[0]
            $queue.RemoveAt(0)
            $process = Start-Process -FilePath $dotnet -ArgumentList (($next.Arguments | ForEach-Object { Quote $_ }) -join ' ') `
                -NoNewWindow -PassThru -RedirectStandardOutput $next.Log -RedirectStandardError "$($next.Log).err"
            $null = $process.Handle
            $running += [pscustomobject]@{ Run = $next; Process = $process; Clock = [Diagnostics.Stopwatch]::StartNew() }
        }
        Start-Sleep -Milliseconds 500
        foreach ($shard in @($running | Where-Object { $_.Process.HasExited })) {
            $shard.Clock.Stop()
            $trx = Read-Trx $shard.Run.Trx
            $exit = $shard.Process.ExitCode
            $done += [pscustomobject]@{ Name = $shard.Run.Name; Log = $shard.Run.Log; Exit = $exit; Time = $shard.Clock.Elapsed; Trx = $trx }
            $running = @($running | Where-Object { $_ -ne $shard })
            $state = if ($exit -eq 0 -and $trx.Read -and $trx.Failed -eq 0) { 'ok' } else { 'FAILED' }
            Write-Host ('  {0,-16} {1,-6} {2,5} passed {3,3} failed {4,3} skipped in {5}' -f $shard.Run.Name, $state, $trx.Passed, $trx.Failed, $trx.Skipped, (Format-Span $shard.Clock.Elapsed))
        }
    }
}
finally {
    $ErrorActionPreference = 'Continue'
    foreach ($shard in $running) { if (-not $shard.Process.HasExited) { & taskkill.exe /T /F /PID $shard.Process.Id 2>&1 | Out-Null } }
    $ErrorActionPreference = 'Stop'
}
$wall.Stop()

$failures = @($done | ForEach-Object { $_.Trx.Failures })
$broken = @($done | Where-Object { ($_.Exit -ne 0 -or -not $_.Trx.Read) -and $_.Trx.Failed -eq 0 })
$seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$twice = @($done | ForEach-Object { $_.Trx.Tests } | Where-Object { -not $seen.Add($_) })
$total = ($done | ForEach-Object { $_.Trx.Total } | Measure-Object -Sum).Sum
$passed = ($done | ForEach-Object { $_.Trx.Passed } | Measure-Object -Sum).Sum
$skipped = ($done | ForEach-Object { $_.Trx.Skipped } | Measure-Object -Sum).Sum

Write-Host ''
Write-Host "$label @ $($commit.Substring(0, 7)): $passed passed, $($failures.Count) failed, $skipped skipped of $total in $(Format-Span $wall.Elapsed) ($Jobs at a time)"
foreach ($failure in $failures | Select-Object -First 50) { Write-Host "  failed: $failure" -ForegroundColor Red }
foreach ($shard in $broken) { Write-Host "  $($shard.Name) exited $($shard.Exit)$(if (-not $shard.Trx.Read) { ', no readable trx' }) with no failed test: see $($shard.Log)" -ForegroundColor Red }
foreach ($test in $twice | Select-Object -First 20) { Write-Host "  ran in more than one shard: $test" -ForegroundColor Red }
Write-Host "  logs and results: $results"

$green = $failures.Count -eq 0 -and $broken.Count -eq 0 -and $twice.Count -eq 0 -and $total -gt 0
if (-not $green) { exit 1 }
if ($stampable) {
    $now = (& $git -C $Root rev-parse HEAD).Trim()
    $stillClean = -not (& $git -C $Root status --porcelain)
    if ($dirty -or -not $stillClean -or $now -ne $commit) {
        Write-Host '  no stamp: the tree had uncommitted changes, or HEAD moved during the run'
    }
    else {
        Write-Utf8 $Stamp "green $commit full`n"
        Write-Host "  stamped: green $commit full"
    }
}
exit 0
