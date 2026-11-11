<#
Builds NoirMech from the AnoMech checkout and publishes it to this plugin repository.

  .\publish.ps1 -Changelog "..."            # build, scan, update repo.json, commit and push
  .\publish.ps1 -Changelog "..." -Fetch     # same, fetching the pushed noirmech-stable first
  .\publish.ps1 -Changelog "..." -NoPush    # everything except the push
  .\publish.ps1 -Changelog "..." -DryRun    # every gate, the build and the scan; nothing copied or committed

It runs at Idle priority on the -Affinity CPUs (0xF0: 4 of them): it drops itself there before it starts any
process, so git and the whole Release build are born there too, and restores its own priority on its way out.

It refuses to publish unless:
- -Changelog is given (the installer shows it);
- this checkout is on main with no uncommitted or untracked file, since the publish commits to main and pushes it;
- the AnoMech checkout is on noirmech-stable with a clean tree;
- its TestResults\green-stamp.txt says "green <HEAD> full", which tools\test.ps1 writes after a green full
  run of that exact commit;
- HEAD is in the pushed noirmech-stable: the remote-tracking ref of the remote that points at
  NoirYamato/DalamudPlugins ("plugins" in the AnoMech checkout), as last fetched;
- %APPDATA%\NoirMech\identity-terms.txt lists the identity terms, one per line, and neither the built
  plugin zip nor the source zip holds any of them, in any case, as ASCII, UTF-8 or UTF-16 text, in the
  DLLs' embedded PDBs and the zips' entry names included. Known strings that must be found prove each part
  of the scan works.
Its commit takes only plugins\NoirMech and repo.json.

The version is the upstream <Version> from AnoMech.csproj with our own fourth segment, which goes
up by one on every publish, so Dalamud sees each publish as an update. Each build ships with a zip
of the exact source it was built from, which AnoMech's AGPL-3.0 licence requires.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\Anomech'),
    [string]$Branch = 'noirmech-stable',
    [string]$Remote = 'plugins',
    [string]$Changelog = '',
    # A hex mask, 0x optional; a number typed in a PowerShell session arrives already parsed.
    [object]$Affinity = '0xF0',
    [switch]$Fetch,
    [switch]$DryRun,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$InternalName = 'NoirMech'
$RawBase = 'https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main'
$PluginDir = Join-Path $PSScriptRoot "plugins\$InternalName"
$RepoJson = Join-Path $PSScriptRoot 'repo.json'
$TermsFile = Join-Path $env:APPDATA 'NoirMech\identity-terms.txt'

# Strings a working scan finds in every build, so a scan that reads nothing can't pass as clean: a dependency's
# own build path (in another case, for the case-insensitive match), UTF-16 strings in NoirMech.dll (a scenario
# name and its /_/ caller paths), the embedded PDB's document names and /_/ paths, an entry name, and a NoirMech
# file in the source zip, with a non-ASCII character for the UTF-8 read. A /_/ path also shows the build kept
# ContinuousIntegrationBuild.
$Controls = @(
    @{ Entry = 'NAudio.Core.dll'; Encoding = 'ascii'; Text = 'USERS\MARKH' }
    @{ Entry = 'NoirMech.dll'; Encoding = 'utf16'; Text = 'Hello World' }
    @{ Entry = 'NoirMech.dll'; Encoding = 'utf16'; Text = '/_/AnoMech/' }
    @{ Entry = 'NoirMech.dll embedded PDB'; Encoding = 'ascii'; Text = 'TopP5OmegaLpduAi' }
    @{ Entry = 'NoirMech.dll embedded PDB'; Encoding = 'ascii'; Text = '/_/' }
    @{ Entry = 'the name NoirMech.dll'; Encoding = 'ascii'; Text = 'NoirMech.dll' }
    @{ Entry = '*-source/AnoMech/Scenarios/Top/P5Omega/TopP5OmegaLpduAi.cs'; Encoding = 'ascii'; Text = 'class TopP5OmegaLpduAi' }
    @{ Entry = '*-source/AnoMech/Scenarios/IScenario.cs'; Encoding = 'utf8'; Text = "Zone.Name} $([char]0x2014) {scenario" }
)

function Find-Tool([string]$name, [string]$fallback) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if (Test-Path $fallback) { return $fallback }
    throw "$name not found."
}
$git = Find-Tool 'git' 'C:\Program Files\Git\cmd\git.exe'
$dotnet = Find-Tool 'dotnet' 'C:\Program Files\dotnet\dotnet.exe'

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $false))
}

# Windows PowerShell turns a native command's redirected stderr into errors, which 'Stop' would throw.
function Invoke-Quiet([string]$exe, [string[]]$arguments) {
    $ErrorActionPreference = 'Continue'
    & $exe @arguments 2>$null
}

function Stop-Publish([string]$reason) {
    Write-Host "Refusing to publish: $reason" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Latin1 = [Text.Encoding]::GetEncoding(28591)

# A blob read every way a string can sit in it; UTF-16 at both byte alignments.
function Get-Texts([byte[]]$bytes) {
    $odd = if ($bytes.Length -gt 1) { [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1) } else { '' }
    @{
        ascii = $Latin1.GetString($bytes)
        utf8  = [Text.Encoding]::UTF8.GetString($bytes)
        utf16 = [Text.Encoding]::Unicode.GetString($bytes) + "`n" + $odd
    }
}

# An embedded portable PDB is "MPDB", its size as an int32, then the PDB deflated.
function Get-EmbeddedPdbs([byte[]]$bytes) {
    $text = $Latin1.GetString($bytes)
    $at = $text.IndexOf('MPDB', [StringComparison]::Ordinal)
    while ($at -ge 0 -and $at + 8 -lt $bytes.Length) {
        $size = [BitConverter]::ToInt32($bytes, $at + 4)
        if ($size -gt 0 -and $size -lt 256MB) {
            try {
                $packed = [IO.MemoryStream]::new($bytes, $at + 8, $bytes.Length - $at - 8)
                $deflate = [IO.Compression.DeflateStream]::new($packed, [IO.Compression.CompressionMode]::Decompress)
                $pdb = [IO.MemoryStream]::new()
                $deflate.CopyTo($pdb)
                $deflate.Dispose()
                $inflated = $pdb.ToArray()
                if ($inflated.Length -eq $size -and $Latin1.GetString($inflated, 0, 4) -eq 'BSJB') { , $inflated }
            }
            catch { }
        }
        $at = $text.IndexOf('MPDB', $at + 4, [StringComparison]::Ordinal)
    }
}

function Get-ZipBlobs([string]$zipPath) {
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $zip.Entries) {
            [pscustomobject]@{ Entry = "the name $($entry.FullName)"; Texts = @{ ascii = $entry.FullName } }
            if ($entry.FullName.EndsWith('/')) { continue }
            $stream = $entry.Open()
            $memory = [IO.MemoryStream]::new()
            $stream.CopyTo($memory)
            $stream.Dispose()
            $bytes = $memory.ToArray()
            [pscustomobject]@{ Entry = $entry.FullName; Texts = Get-Texts $bytes }
            if ($entry.FullName -match '\.(dll|exe)$') {
                foreach ($pdb in @(Get-EmbeddedPdbs $bytes)) { [pscustomobject]@{ Entry = "$($entry.FullName) embedded PDB"; Texts = Get-Texts $pdb } }
            }
        }
    }
    finally { $zip.Dispose() }
}

# Names each problem without echoing a term: a term is shown by its line in the terms file.
function Test-Identity([string[]]$zips, [string[]]$terms) {
    $problems = @()
    $blobs = @()
    foreach ($zip in $zips) {
        $blobs += @(Get-ZipBlobs $zip | ForEach-Object { $_.Entry = "$(Split-Path -Leaf $zip): $($_.Entry)"; $_ })
    }
    foreach ($control in $Controls) {
        $found = @($blobs | Where-Object {
            $_.Entry -like "*: $($control.Entry)" -and $_.Texts.ContainsKey($control.Encoding) -and
            $_.Texts[$control.Encoding].IndexOf($control.Text, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
        if ($found.Count -eq 0) { $problems += "the scan didn't find its $($control.Encoding) control '$($control.Text)' in $($control.Entry)" }
    }
    for ($i = 0; $i -lt $terms.Length; $i++) {
        foreach ($blob in $blobs) {
            foreach ($encoding in $blob.Texts.Keys) {
                if ($blob.Texts[$encoding].IndexOf($terms[$i], [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $problems += "identity term on line $($i + 1) of the terms file is in $($blob.Entry) ($encoding)"
                }
            }
        }
    }
    Write-Host "Scanned $($blobs.Count) blobs in $($zips.Count) zips for $($terms.Length) identity terms and $($Controls.Count) controls"
    $problems
}

# repo.json's entries, one per pipeline object: Windows PowerShell's ConvertFrom-Json emits a top-level array as
# one object, which ForEach-Object unrolls; PowerShell 7 already emits each entry. Read as UTF-8, which Write-Utf8
# writes: Windows PowerShell reads a file without a BOM in the ANSI code page, which would garble the non-ASCII
# text of every entry this script writes back.
function Get-RepoEntries([string]$path) {
    if (Test-Path $path) { Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json | ForEach-Object { $_ } }
}

# repo.json lists every plugin of this repository: replace this plugin's entry where it stands, keep the others
# and their order, and add it at the end if it isn't listed yet.
function Set-RepoEntry([string]$path, [object]$entry) {
    $entries = @()
    $placed = $false
    foreach ($listed in @(Get-RepoEntries $path)) {
        if ($listed.InternalName -ne $entry.InternalName) { $entries += $listed }
        elseif (-not $placed) { $entries += $entry; $placed = $true }
    }
    if (-not $placed) { $entries += $entry }
    Write-Utf8 $path ((ConvertTo-Json -InputObject $entries -Depth 5) + "`n")
}

$mask = if ($Affinity -is [string]) { [Convert]::ToInt64(($Affinity -replace '^0[xX]', ''), 16) } else { [int64]$Affinity }
if ($mask -le 0 -or $mask -ge ([int64]1 -shl [Math]::Min(62, [Environment]::ProcessorCount))) {
    throw "-Affinity $Affinity isn't a mask of this PC's $([Environment]::ProcessorCount) CPUs."
}
$cpuCount = @(0..([Environment]::ProcessorCount - 1) | Where-Object { $mask -band ([int64]1 -shl $_) }).Count

if (-not $Changelog.Trim()) { Stop-Publish 'give -Changelog "...": the plugin installer shows it.' }
if (-not (Test-Path $TermsFile)) { Stop-Publish '%APPDATA%\NoirMech\identity-terms.txt is missing: list the identity terms, one per line.' }
$terms = @([IO.File]::ReadAllLines($TermsFile, [Text.Encoding]::UTF8) | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($terms.Count -eq 0) { Stop-Publish '%APPDATA%\NoirMech\identity-terms.txt lists no identity terms.' }

$self = [Diagnostics.Process]::GetCurrentProcess()
$saved = @{ Priority = $self.PriorityClass; Affinity = $self.ProcessorAffinity }
try {
    # Before its first process, so git and the whole Release build are born at Idle on the mask.
    $self.ProcessorAffinity = [IntPtr]$mask
    $self.PriorityClass = [Diagnostics.ProcessPriorityClass]::Idle

    $here = (& $git -C $PSScriptRoot rev-parse --abbrev-ref HEAD).Trim()
    if ($here -ne 'main') { Stop-Publish "$PSScriptRoot is on '$here'; switch it to 'main', which the publish commits to and pushes." }
    if (& $git -C $PSScriptRoot status --porcelain) {
        Stop-Publish "$PSScriptRoot has uncommitted changes or untracked files; commit or remove them first, since the publish pushes main."
    }

    $Source = (Resolve-Path $Source).Path

    $currentBranch = (& $git -C $Source rev-parse --abbrev-ref HEAD).Trim()
    if ($currentBranch -ne $Branch) { Stop-Publish "$Source is on '$currentBranch'; switch it to '$Branch' first." }
    if (& $git -C $Source status --porcelain) { Stop-Publish "$Source has uncommitted changes; commit them first." }
    $commit = (& $git -C $Source rev-parse HEAD).Trim()

    $stampPath = Join-Path $Source 'TestResults\green-stamp.txt'
    $stamp = if (Test-Path $stampPath) { ([string](Get-Content -Raw $stampPath)).Trim() } else { '' }
    if ($stamp -ne "green $commit full") {
        Stop-Publish "$stampPath $(if ($stamp) { "says '$stamp'" } else { 'is missing' }), not 'green $commit full': run tools\test.ps1 on this commit."
    }

    $remoteUrl = [string](Invoke-Quiet $git @('-C', $Source, 'remote', 'get-url', $Remote))
    if ($LASTEXITCODE -ne 0 -or $remoteUrl.Trim() -notmatch '[/:]NoirYamato/DalamudPlugins(\.git)?/?$') {
        Stop-Publish "$Source has no remote '$Remote' pointing at NoirYamato/DalamudPlugins; pass -Remote."
    }
    if ($Fetch) { Invoke-Checked $git @('-C', $Source, 'fetch', '--quiet', $Remote, $Branch) }
    $pushed = "refs/remotes/$Remote/$Branch"
    Invoke-Quiet $git @('-C', $Source, 'rev-parse', '--verify', '--quiet', "$pushed^{commit}") | Out-Null
    if ($LASTEXITCODE -ne 0) { Stop-Publish "$Source has no $pushed; push $Branch to $Remote, or pass -Fetch." }
    Invoke-Quiet $git @('-C', $Source, 'merge-base', '--is-ancestor', $commit, $pushed)
    if ($LASTEXITCODE -ne 0) {
        $hint = if ($Fetch) { 'push it first (git push {0} {1})' } else { "push it (git push {0} {1}), or pass -Fetch if it's pushed" }
        Stop-Publish "$($commit.Substring(0, 7)) isn't in $Remote/$Branch as last fetched; $($hint -f $Remote, $Branch)."
    }

    $csproj = Join-Path $Source 'AnoMech\AnoMech.csproj'
    $upstream = (([xml](Get-Content -Raw $csproj)).Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version.Trim()
    $parts = @($upstream.Split('.')) + @('0', '0', '0')
    $base = '{0}.{1}.{2}' -f $parts[0], $parts[1], $parts[2]

    $revision = 1
    if (Test-Path $RepoJson) {
        $previous = @(Get-RepoEntries $RepoJson) | Where-Object { $_.InternalName -eq $InternalName } | Select-Object -First 1
        if ($previous -and $previous.AssemblyVersion.StartsWith("$base.")) {
            $revision = [int]$previous.AssemblyVersion.Split('.')[3] + 1
        }
    }
    $version = "$base.$revision"
    Write-Host "Publishing $InternalName $version from $Branch @ $($commit.Substring(0, 7))$(if ($DryRun) { ' (dry run)' })"

    $releaseOut = Join-Path $Source 'AnoMech\bin\Release'
    if (Test-Path $releaseOut) { Remove-Item -Recurse -Force $releaseOut }
    # ContinuousIntegrationBuild maps source paths to /_/, so caller-file strings and PDB paths in the
    # shipped DLLs don't carry the local user folder. Without build servers the compiler runs at this
    # script's priority: Roslyn starts its shared server at normal priority whatever starts it.
    Invoke-Checked $dotnet @('build', $csproj, '-c', 'Release', "-p:Version=$version", '-p:ContinuousIntegrationBuild=true', '--no-incremental', '--disable-build-servers', "-m:$cpuCount", '-nologo', '-v:minimal')

    $packaged = Join-Path $releaseOut $InternalName
    $zip = Join-Path $packaged 'latest.zip'
    $manifestPath = Join-Path $packaged "$InternalName.json"
    if (-not (Test-Path $zip) -or -not (Test-Path $manifestPath)) { throw "Packager output not found in $packaged." }
    $manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
    if ($manifest.InternalName -ne $InternalName -or $manifest.AssemblyVersion -ne $version) {
        throw "Built manifest is $($manifest.InternalName) $($manifest.AssemblyVersion), expected $InternalName $version."
    }

    $zipName = "$InternalName-$version.zip"
    $sourceName = "$InternalName-$version-source.zip"
    $sourceZip = Join-Path $releaseOut $sourceName
    Invoke-Checked $git @('-C', $Source, 'archive', '--format=zip', "--prefix=$InternalName-$version-source/", '-o', $sourceZip, $commit)

    $problems = @(Test-Identity @($zip, $sourceZip) $terms)
    if ($problems.Count -gt 0) {
        foreach ($problem in $problems | Select-Object -First 40) { Write-Host "  $problem" -ForegroundColor Red }
        Stop-Publish "the identity scan failed ($($problems.Count) problems)."
    }
    Write-Host 'Identity scan clean: every control found, no identity term.'

    if ($DryRun) {
        Write-Host "Dry run: every gate passed. $zipName and $sourceName are in $releaseOut; nothing was copied, committed or pushed."
        exit 0
    }

    New-Item -ItemType Directory -Force $PluginDir | Out-Null
    Copy-Item $zip (Join-Path $PluginDir $zipName) -Force
    Copy-Item $manifestPath (Join-Path $PluginDir "$InternalName.json") -Force
    Copy-Item $sourceZip (Join-Path $PluginDir $sourceName) -Force

    # Keep the previous build next to the new one: raw.githubusercontent.com can serve a cached
    # repo.json for a few minutes, and its download link must still resolve.
    foreach ($pattern in @("$InternalName-*-source.zip", "$InternalName-*.zip")) {
        Get-ChildItem $PluginDir -Filter $pattern |
            Where-Object { ($pattern -like '*-source.zip') -eq ($_.Name -like '*-source.zip') } |
            Sort-Object { [version]($_.Name -replace "^$InternalName-", '' -replace '(-source)?\.zip$', '') } -Descending |
            Select-Object -Skip 2 |
            Remove-Item -Force
    }

    $download = "$RawBase/plugins/$InternalName/$zipName"
    $entry = [ordered]@{
        Author              = $manifest.Author
        Name                = $manifest.Name
        Punchline           = $manifest.Punchline
        Description         = $manifest.Description
        InternalName        = $manifest.InternalName
        AssemblyVersion     = $manifest.AssemblyVersion
        RepoUrl             = $manifest.RepoUrl
        ApplicableVersion   = $manifest.ApplicableVersion
        Tags                = @($manifest.Tags)
        DalamudApiLevel     = $manifest.DalamudApiLevel
        IconUrl             = $manifest.IconUrl
        DownloadLinkInstall = $download
        DownloadLinkTesting = $download
        DownloadLinkUpdate  = $download
        IsHide              = $false
        IsTestingExclusive  = $false
        DownloadCount       = 0
        LastUpdate          = [string][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        Changelog           = $Changelog
    }
    Set-RepoEntry $RepoJson ([pscustomobject]$entry)

    # Only what the publish wrote, whatever else turns up in this folder while it builds.
    $written = @("plugins/$InternalName", 'repo.json')
    Invoke-Checked $git (@('-C', $PSScriptRoot, 'add', '-A', '--') + $written)
    Invoke-Checked $git (@('-C', $PSScriptRoot, 'commit', '-q', '-m', "Publish $InternalName $version", '-m', "Built from $Branch @ $commit", '--') + $written)
    if (-not $NoPush) { Invoke-Checked $git @('-C', $PSScriptRoot, 'push', 'origin', 'main') }
    Write-Host "Done: $InternalName $version$(if ($NoPush) { ' (not pushed)' })"
}
finally {
    $self.PriorityClass = $saved.Priority
    $self.ProcessorAffinity = $saved.Affinity
}
