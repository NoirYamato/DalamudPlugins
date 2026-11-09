<#
Builds once, then runs the tests as parallel `dotnet test --no-build` processes and prints one summary.

  powershell -ExecutionPolicy Bypass -File tools\test.ps1                  # full: every test, the release gate
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Preset quick    # no human suites, fewer seeds
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3        # one TOP phase's tests
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3 -Preset quick
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Phase P3 -SunnySeeds 2000   # with its soak
  powershell -ExecutionPolicy Bypass -File tools\test.ps1 -Plan            # print the shards, run nothing

Gentle by default: before it starts anything it drops itself to Idle priority on the -Affinity CPUs (0xF0: 4 of
them), so the build, every test process and the test hosts those start are born there too, and it runs one shard
per CPU in the mask. Test processes at normal priority on every CPU have frozen the PC.

AnoMech.Tests runs as -Shards processes, each its own since a run installs statics. The buckets below are dealt to
the shards by their time; a shard takes the tests its buckets match that no earlier shard took, and the shard
holding the rest takes everything else, so together they run every test once, a test added later included.
AnoMech.Relay.Tests runs as one more process, except for a phase.

A green full run of a clean tree writes "green <commit> full" to TestResults\green-stamp.txt, which the release
script requires. A full run removes it first, so one that fails or is stopped leaves none.
#>
[CmdletBinding()]
param(
    [ValidateSet('full', 'quick', 'phase')][string]$Preset = 'full',
    [ValidateSet('P2', 'P3', 'P4', 'P5', 'P6')][string]$Phase,
    # A hex mask, 0x optional; a number typed in a PowerShell session arrives already parsed.
    [object]$Affinity = '0xF0',
    [ValidateRange(1, 64)][int]$Shards,
    [ValidateRange(1, 100000)][int]$SunnySeeds,
    [switch]$NoBuild,
    [switch]$Plan
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$TestProject = Join-Path $Root 'tests\AnoMech.Tests\AnoMech.Tests.csproj'
$RelayProject = Join-Path $Root 'tests\AnoMech.Relay.Tests\AnoMech.Relay.Tests.csproj'
$RunSettings = Join-Path $Root 'tests\AnoMech.Tests\test.runsettings'
$Stamp = Join-Path $Root 'TestResults\green-stamp.txt'
$Runs = Join-Path $Root 'TestResults\gate'
$KeepRuns = 10

# A term is atoms joined by & (Property~Value, Property=Value or their negations !~ and !=, no spaces). The NUnit
# adapter reads ~ as a case-sensitive substring and !~ as exactly its complement, so negating the earlier shards'
# terms leaves no test out and none twice. Keep [Explicit] tests (the catalog's Replay ones) out of every term: a
# filter that selects one can run it.
# Seconds: the bucket's time in a single-process run at the run settings' seeds. SunnySeeds and PauseSeeds buckets
# scale with those seeds, a Human bucket drops out of quick, and a bucket of one Phase out of another phase's run.
# When the summary's shard times drift apart, refresh the times or split a bucket.
$HelloWorldHumans = 'FullyQualifiedName~TopP3HelloWorldLpduHumanTests'
$Catalog = 'FullyQualifiedName~ScenarioCatalogTests'
$Buckets = @(
    @{ Name = 'hello-sprinting'; Terms = @("$HelloWorldHumans&Name~Sprinting"); Seconds = 179; Human = $true; Phase = 'P3' }
    @{ Name = 'hello-early'; Terms = @("$HelloWorldHumans&Name~Early"); Seconds = 179; Human = $true; Phase = 'P3' }
    @{ Name = 'hello-lanes'; Terms = @("$HelloWorldHumans&Name~Lanes"); Seconds = 178; Human = $true; Phase = 'P3' }
    @{ Name = 'hello-straight'; Terms = @("$HelloWorldHumans&Name~Straight"); Seconds = 142; Human = $true; Phase = 'P3' }
    @{ Name = 'hello-curving'; Terms = @("$HelloWorldHumans&Name~Curving"); Seconds = 135; Human = $true; Phase = 'P3' }
    @{ Name = 'hello-stretching'; Terms = @("$HelloWorldHumans&Name~Stretching"); Seconds = 132; Human = $true; Phase = 'P3' }
    @{ Name = 'p3-intermission-human'; Terms = @('FullyQualifiedName~TopP3IntermissionLpduHumanTests'); Seconds = 62; Human = $true; Phase = 'P3' }
    @{ Name = 'p4-human'; Terms = @('FullyQualifiedName~TopP4BlueScreenLpduHumanTests'); Seconds = 53; Human = $true; Phase = 'P4' }
    @{ Name = 'p5-delta-human'; Terms = @('FullyQualifiedName~TopP5DeltaLpduHumanTests'); Seconds = 127; Human = $true; Phase = 'P5' }
    @{ Name = 'p5-delta-margin'; Terms = @('FullyQualifiedName~TopP5DeltaLpduMarginTests'); Seconds = 119; Phase = 'P5' }
    @{ Name = 'p5-omega-human'; Terms = @('FullyQualifiedName~TopP5OmegaLpduHumanTests'); Seconds = 89; Human = $true; Phase = 'P5' }
    @{ Name = 'p5-omega-margin'; Terms = @('FullyQualifiedName~TopP5OmegaLpduMarginTests'); Seconds = 43; Phase = 'P5' }
    @{ Name = 'p6-human'; Terms = @('FullyQualifiedName~TopP6Gauntlet&FullyQualifiedName~HumanTests'); Seconds = 182; Human = $true; Phase = 'P6' }
    @{ Name = 'p6-margin'; Terms = @('FullyQualifiedName~TopP6Gauntlet&FullyQualifiedName~MarginTests'); Seconds = 43; Phase = 'P6' }
    @{ Name = 'link-interruption'; Terms = @('FullyQualifiedName~LinkInterruptionTests'); Seconds = 89 }
    @{ Name = 'debug-bot-replay'; Terms = @('FullyQualifiedName~DebugBotReplayTests'); Seconds = 58 }
    @{ Name = 'peer-parity'; Terms = @('FullyQualifiedName~PeerParityTests'); Seconds = 25 }
    @{ Name = 'catalog-black-hole'; Terms = @("$Catalog&Name~Dancing&Name~Black"); Seconds = 15; Seeds = 'SunnySeeds' }
    @{ Name = 'catalog-dancing'; Terms = @("$Catalog&Name~Dancing&Name!~Black"); Seconds = 21; Seeds = 'SunnySeeds' }
    @{ Name = 'catalog-top'; Terms = @("$Catalog&Name~Protocol"); Seconds = 26; Seeds = 'SunnySeeds' }
    @{ Name = 'catalog-other'; Terms = @("$Catalog&Name!~Dancing&Name!~Protocol&Name!~Replay"); Seconds = 14; Seeds = 'SunnySeeds' }
    @{ Name = 'pause'; Terms = @('FullyQualifiedName~GamePauseTests'); Seconds = 13; Seeds = 'PauseSeeds' }
    @{ Name = 'rest'; Terms = @(); Seconds = 145; QuickSeconds = 133 }
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

function Get-RunParameter([string]$name) {
    $node = @(([xml](Get-Content -Raw $RunSettings)).RunSettings.TestRunParameters.Parameter | Where-Object { $_.name -eq $name })[0]
    if (-not $node) { throw "$RunSettings sets no $name." }
    [int]$node.value
}

function Get-Seconds($bucket) {
    if ($Phase -and $bucket.Phase -and $bucket.Phase -ne $Phase) { return 0.0 }
    if ($quick -and $bucket.Human) { return 0.0 }
    $seconds = if ($quick -and $bucket.ContainsKey('QuickSeconds')) { $bucket.QuickSeconds } else { $bucket.Seconds }
    if ($bucket.Seeds) { $seconds = $seconds * $seeds[$bucket.Seeds] / $defaultSeeds[$bucket.Seeds] }
    [double]$seconds
}

# The heaviest bucket goes onto the lightest shard, again and again. The shard holding the rest goes last: its filter
# is every other shard's terms negated. A bucket that has nothing to run in this preset is left to the rest.
function Get-Plan([int]$count) {
    $shards = @(for ($i = 0; $i -lt $count; $i++) { [pscustomobject]@{ Buckets = New-Object Collections.Generic.List[object]; Seconds = 0.0 } })
    $order = @(for ($i = 0; $i -lt $Buckets.Count; $i++) {
            [pscustomobject]@{ Index = $i; Bucket = $Buckets[$i]; Seconds = Get-Seconds $Buckets[$i] }
        }) | Where-Object { $_.Seconds -gt 0 -or $_.Bucket.Terms.Count -eq 0 } |
        Sort-Object @{ Expression = 'Seconds'; Descending = $true }, @{ Expression = 'Index'; Descending = $false }
    foreach ($item in $order) {
        $lightest = $shards[0]
        foreach ($shard in $shards) { if ($shard.Seconds -lt $lightest.Seconds) { $lightest = $shard } }
        $lightest.Buckets.Add($item.Bucket)
        $lightest.Seconds += $item.Seconds
    }
    $rest = @($shards | Where-Object { @($_.Buckets | Where-Object { $_.Terms.Count -eq 0 }).Count -gt 0 })[0]
    @($shards | Where-Object { $_ -ne $rest -and $_.Buckets.Count -gt 0 }) + @($rest)
}

function Get-Terms($shard) { @($shard.Buckets | ForEach-Object { $_.Terms }) }

function Get-ShardFilter([object[]]$plan, [int]$index, [string[]]$restrictions) {
    $parts = @()
    if ($index -lt $plan.Count - 1) { $parts += Get-AnyOf (Get-Terms $plan[$index]) }
    for ($j = 0; $j -lt $index; $j++) { foreach ($term in (Get-Terms $plan[$j])) { $parts += Get-Negation $term } }
    $parts += $restrictions
    $parts -join '&'
}

function Quote([string]$argument) {
    if ($argument -notmatch '[\s"]') { return $argument }
    '"' + ($argument -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function New-RunSettings([hashtable]$parameters, [string]$path) {
    $xml = [xml](Get-Content -Raw $RunSettings)
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
$quick = $Preset -eq 'quick'
$label = "$Preset$(if ($Phase) { " $Phase" })"

$mask = if ($Affinity -is [string]) { [Convert]::ToInt64(($Affinity -replace '^0[xX]', ''), 16) } else { [int64]$Affinity }
$cpus = @(0..([Environment]::ProcessorCount - 1) | Where-Object { $mask -band ([int64]1 -shl $_) })
if ($mask -le 0 -or $mask -ge ([int64]1 -shl [Math]::Min(62, [Environment]::ProcessorCount))) {
    throw "-Affinity $Affinity isn't a mask of this PC's $([Environment]::ProcessorCount) CPUs."
}
if (-not $PSBoundParameters.ContainsKey('Shards')) { $Shards = $cpus.Count }

$defaultSeeds = @{ SunnySeeds = Get-RunParameter 'SunnySeeds'; PauseSeeds = Get-RunParameter 'PauseSeeds' }
$seeds = $defaultSeeds.Clone()
$parameters = @{}
$restrictions = @()
if ($quick) {
    $restrictions += Get-Negation $HumanSuites
    $parameters.SunnySeeds = 4
    $parameters.PauseSeeds = 1
}
if ($Phase) { $restrictions += Get-AnyOf $PhaseTerms[$Phase] }
if ($PSBoundParameters.ContainsKey('SunnySeeds')) { $parameters.SunnySeeds = $SunnySeeds }
foreach ($name in $parameters.Keys) { $seeds[$name] = $parameters[$name] }

$shardPlan = @(Get-Plan $Shards)
if ($Plan) {
    for ($i = 0; $i -lt $shardPlan.Count; $i++) {
        $filter = Get-ShardFilter $shardPlan $i $restrictions
        Write-Host ('shard{0}  ~{1:F0}s  {2}' -f ($i + 1), $shardPlan[$i].Seconds, (($shardPlan[$i].Buckets | ForEach-Object { $_.Name }) -join ', '))
        Write-Host "  $(if ($filter) { $filter } else { '(no filter: every test)' })"
    }
    exit 0
}

$git = Find-Tool 'git' 'C:\Program Files\Git\cmd\git.exe'
$dotnet = Find-Tool 'dotnet' 'C:\Program Files\dotnet\dotnet.exe'
$commit = (& $git -C $Root rev-parse HEAD).Trim()
$dirty = [bool](& $git -C $Root status --porcelain)
$stampable = $Preset -eq 'full' -and -not $PSBoundParameters.ContainsKey('SunnySeeds') -and -not $NoBuild
if ($Preset -eq 'full' -and (Test-Path $Stamp)) { Remove-Item -Force $Stamp }

$results = Join-Path $Runs ('{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), ($label -replace ' ', '-'))
New-Item -ItemType Directory -Force $results | Out-Null
Get-ChildItem $Runs -Directory | Sort-Object Name -Descending | Select-Object -Skip $KeepRuns |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
$settings = $null
if ($parameters.Count -gt 0) {
    $settings = Join-Path $results 'test.runsettings'
    New-RunSettings $parameters $settings
}

function New-Run([string]$name, [string]$what, [string[]]$arguments, [string[]]$runSettings = @()) {
    $all = $arguments + @('--disable-build-servers', '--logger', "trx;LogFileName=$name.trx", '--results-directory', (Join-Path $results $name))
    if ($runSettings.Count -gt 0) { $all += @('--') + $runSettings }
    [pscustomobject]@{
        Name      = $name
        What      = $what
        Arguments = $all
        Trx       = Join-Path $results "$name\$name.trx"
        Log       = Join-Path $results "$name.log"
    }
}

$planned = New-Object Collections.Generic.List[object]
for ($i = 0; $i -lt $shardPlan.Count; $i++) {
    $arguments = @('test', $TestProject, '--no-build')
    $filter = Get-ShardFilter $shardPlan $i $restrictions
    if ($filter) { $arguments += @('--filter', $filter) }
    if ($settings) { $arguments += @('--settings', $settings) }
    # Past this many selected tests (2000 by default) the NUnit adapter runs the whole assembly and only drops the
    # others' results.
    $planned.Add((New-Run "shard$($i + 1)" (($shardPlan[$i].Buckets | ForEach-Object { $_.Name }) -join ', ') $arguments @('NUnit.AssemblySelectLimit=1000000')))
}
if (-not $Phase) { $planned.Add((New-Run 'relay' 'AnoMech.Relay.Tests' @('test', $RelayProject, '--no-build'))) }

$self = [Diagnostics.Process]::GetCurrentProcess()
$saved = @{ Priority = $self.PriorityClass; Affinity = $self.ProcessorAffinity }
$running = @()
$done = @()
$exitCode = 1
$wall = [Diagnostics.Stopwatch]::StartNew()
try {
    $self.ProcessorAffinity = [IntPtr]$mask
    $self.PriorityClass = [Diagnostics.ProcessPriorityClass]::Idle
    $where = "at Idle on CPUs $($cpus -join ',') (0x$('{0:X}' -f $mask))"

    if (-not $NoBuild) {
        Write-Host "Building $(Split-Path -Leaf $Root) @ $($commit.Substring(0, 7))$(if ($dirty) { ' (uncommitted changes)' }) $where"
        & $dotnet build (Join-Path $Root 'AnoMech.sln') -nologo -v:minimal "-m:$($cpus.Count)" --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }

    Write-Host "Running $($planned.Count) processes at once, $where ($label)"
    foreach ($next in $planned) {
        $process = Start-Process -FilePath $dotnet -ArgumentList (($next.Arguments | ForEach-Object { Quote $_ }) -join ' ') `
            -NoNewWindow -PassThru -RedirectStandardOutput $next.Log -RedirectStandardError "$($next.Log).err"
        $null = $process.Handle
        $running += [pscustomobject]@{ Run = $next; Process = $process; Clock = [Diagnostics.Stopwatch]::StartNew() }
    }
    while ($running.Count -gt 0) {
        Start-Sleep -Milliseconds 500
        foreach ($shard in @($running | Where-Object { $_.Process.HasExited })) {
            $shard.Clock.Stop()
            $trx = Read-Trx $shard.Run.Trx
            $exit = $shard.Process.ExitCode
            $done += [pscustomobject]@{ Name = $shard.Run.Name; Log = $shard.Run.Log; Exit = $exit; Time = $shard.Clock.Elapsed; Trx = $trx }
            $running = @($running | Where-Object { $_ -ne $shard })
            $state = if ($exit -eq 0 -and $trx.Read -and $trx.Failed -eq 0) { 'ok' } else { 'FAILED' }
            Write-Host ('  {0,-7} {1,-6} {2,5} passed {3,3} failed {4,3} skipped in {5,6}  {6}' -f $shard.Run.Name, $state, $trx.Passed, $trx.Failed, $trx.Skipped, (Format-Span $shard.Clock.Elapsed), $shard.Run.What)
        }
    }
    $exitCode = 0
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    $ErrorActionPreference = 'Continue'
    foreach ($shard in $running) { if (-not $shard.Process.HasExited) { & taskkill.exe /T /F /PID $shard.Process.Id 2>&1 | Out-Null } }
    $self.PriorityClass = $saved.Priority
    $self.ProcessorAffinity = $saved.Affinity
    $ErrorActionPreference = 'Stop'
}
$wall.Stop()
if ($exitCode -ne 0) { exit $exitCode }

$failures = @($done | ForEach-Object { $_.Trx.Failures })
$broken = @($done | Where-Object { ($_.Exit -ne 0 -or -not $_.Trx.Read) -and $_.Trx.Failed -eq 0 })
$seen = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$twice = @($done | ForEach-Object { $_.Trx.Tests } | Where-Object { -not $seen.Add($_) })
$total = ($done | ForEach-Object { $_.Trx.Total } | Measure-Object -Sum).Sum
$passed = ($done | ForEach-Object { $_.Trx.Passed } | Measure-Object -Sum).Sum
$skipped = ($done | ForEach-Object { $_.Trx.Skipped } | Measure-Object -Sum).Sum

Write-Host ''
Write-Host "$label @ $($commit.Substring(0, 7)): $passed passed, $($failures.Count) failed, $skipped skipped of $total in $(Format-Span $wall.Elapsed) ($($planned.Count) processes on $($cpus.Count) CPUs)"
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
