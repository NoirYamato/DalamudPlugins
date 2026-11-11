<#
Builds MitCue from its source checkout and publishes it to this plugin repository, next to NoirMech.

  .\publish-mitcue.ps1 -Changelog "..."            # test, build, scan, update repo.json, commit and push
  .\publish-mitcue.ps1 -Changelog "..." -Fetch     # same, fetching the pushed mitcue first
  .\publish-mitcue.ps1 -Changelog "..." -NoPush    # everything except the push
  .\publish-mitcue.ps1 -Changelog "..." -DryRun    # every gate, the tests, the build and the scan; nothing copied or committed

The MitCue checkout is ..\MitCue unless -Source names another: a git worktree of this repository on the mitcue
branch, made once from this folder with "git worktree add ..\MitCue mitcue".

On Windows it runs at Idle priority on the -Affinity CPUs (0xF0: 4 of them): it drops itself there before it
starts any process, so git, the tests and the whole Release build are born there too, and restores its own
priority on its way out; a setting Windows refuses is skipped with a note. Elsewhere (PowerShell 7 on Linux or
macOS) it says it can't and runs at normal priority on every CPU, since the processes it starts wouldn't inherit
either setting there.

It refuses to publish unless:
- -Changelog is given (the installer shows it);
- this checkout is on main with no uncommitted or untracked file, since the publish commits to main and pushes it,
  and holds icons\mitcue.png, which MitCue's IconUrl points at;
- the MitCue checkout is on mitcue with a clean tree;
- HEAD is in the pushed mitcue: the remote-tracking ref of the remote that points at NoirYamato/DalamudPlugins
  (found by its URL, "origin" in a worktree of this checkout; -Remote names it), as last fetched;
- the new version is higher than the one repo.json lists for MitCue, so Dalamud offers it as an update;
- the whole test suite (dotnet test MitCue.slnx -c Release) passes on that checkout;
- the identity terms file (-TermsFile; NoirMech's %APPDATA%\NoirMech\identity-terms.txt by default) lists the
  identity terms, one per line, and neither the built plugin zip nor the source zip holds any of them, in any
  case, as ASCII, UTF-8 or UTF-16 text, in the DLLs' embedded PDBs, the zips' entry names and the files inside
  zip-based files (the source's .xlsx fixtures) included. Known strings that must be found prove each part of
  the scan works.
Its commit takes only plugins\MitCue and repo.json, and in repo.json only MitCue's entry changes.

The version is the MAJOR.MINOR.PATCH <Version> from src\MitCue\MitCue.csproj with our own fourth segment, which
goes up by one on every publish, so Dalamud sees each publish as an update. Each build ships with a zip of the
exact source it was built from.
#>
param(
    [string]$Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'MitCue'),
    [string]$Branch = 'mitcue',
    # The remote that points at NoirYamato/DalamudPlugins; when not given, the one remote whose URL does.
    [string]$Remote = '',
    [string]$Changelog = '',
    # Default: NoirMech's list, %APPDATA%\NoirMech\identity-terms.txt.
    [string]$TermsFile = '',
    # A hex mask, 0x optional; a number typed in a PowerShell session arrives already parsed.
    [object]$Affinity = '0xF0',
    [switch]$Fetch,
    [switch]$DryRun,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
$InternalName = 'MitCue'
$RawBase = 'https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main'
$IconUrl = "$RawBase/icons/mitcue.png"
$IconFile = [IO.Path]::Combine($PSScriptRoot, 'icons', 'mitcue.png')
$PluginDir = [IO.Path]::Combine($PSScriptRoot, 'plugins', $InternalName)
$RepoJson = Join-Path $PSScriptRoot 'repo.json'
$RemotePattern = '[/:]NoirYamato/DalamudPlugins(\.git)?/?$'

# Strings a working scan finds in every build, so a scan that reads nothing can't pass as clean: a dependency's
# own build path (in another case, for the case-insensitive match), a UTF-16 string in MitCue.dll (a command),
# the PDB path the compiler wrote into MitCue.Core.dll, whose /_/ shows the build kept ContinuousIntegrationBuild,
# a document name in MitCue.dll's embedded PDB, an entry name, a MitCue file in the source zip, one with a
# non-ASCII character for the UTF-8 read, and a sheet name inside a fixture .xlsx, a zip inside the source zip.
$Controls = @(
    @{ Entry = 'ExcelNumberFormat.dll'; Encoding = 'ascii'; Text = 'D:\A\EXCELNUMBERFORMAT\' }
    @{ Entry = 'MitCue.dll'; Encoding = 'utf16'; Text = '/mitcue preview' }
    @{ Entry = 'MitCue.Core.dll'; Encoding = 'ascii'; Text = '/_/src/MitCue.Core/obj/' }
    @{ Entry = 'MitCue.dll embedded PDB'; Encoding = 'ascii'; Text = 'OverlayWindow.cs' }
    @{ Entry = 'the name MitCue.dll'; Encoding = 'ascii'; Text = 'MitCue.dll' }
    @{ Entry = '*-source/src/MitCue/Plugin.cs'; Encoding = 'ascii'; Text = 'class Plugin : IDalamudPlugin' }
    @{ Entry = '*-source/src/MitCue/Import/GoogleLinks.cs'; Encoding = 'utf8'; Text = ('"{0}" + id[^5..]' -f [char]0x2026) }
    @{ Entry = '*-source/tests/fixtures/sheets/lpdu/export/lpdu.xlsx > xl/workbook.xml'; Encoding = 'ascii'; Text = 'name="General Tank Mit til P5"' }
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

# Every entry of an open zip, and of the zips inside it: an .xlsx stores its sheets deflated, out of a plain read's
# sight, so a file that starts like a zip is opened and read too, two levels down at most. Inner entries are
# named "<outer entry> > <inner entry>".
function Get-ArchiveBlobs([IO.Compression.ZipArchive]$zip, [string]$prefix, [int]$depth) {
    foreach ($entry in $zip.Entries) {
        $name = "$prefix$($entry.FullName)"
        [pscustomobject]@{ Entry = "the name $name"; Texts = @{ ascii = $entry.FullName } }
        if ($entry.FullName.EndsWith('/')) { continue }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        $stream.CopyTo($memory)
        $stream.Dispose()
        $bytes = $memory.ToArray()
        [pscustomobject]@{ Entry = $name; Texts = Get-Texts $bytes }
        if ($entry.FullName -match '\.(dll|exe)$') {
            foreach ($pdb in @(Get-EmbeddedPdbs $bytes)) { [pscustomobject]@{ Entry = "$name embedded PDB"; Texts = Get-Texts $pdb } }
        }
        if ($depth -lt 2 -and $bytes.Length -gt 4 -and $bytes[0] -eq 0x50 -and $bytes[1] -eq 0x4B -and $bytes[2] -eq 3 -and $bytes[3] -eq 4) {
            try { $inner = [IO.Compression.ZipArchive]::new([IO.MemoryStream]::new($bytes), [IO.Compression.ZipArchiveMode]::Read) }
            catch { throw "$name starts like a zip but doesn't open as one, so the scan can't read inside it: $($_.Exception.Message)" }
            try { Get-ArchiveBlobs $inner "$name > " ($depth + 1) }
            finally { $inner.Dispose() }
        }
    }
}

function Get-ZipBlobs([string]$zipPath) {
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try { Get-ArchiveBlobs $zip '' 0 }
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
# one object, which ForEach-Object unrolls; PowerShell 7 already emits each entry.
function Get-RepoEntries([string]$path) {
    if (Test-Path $path) { Get-Content -Raw $path | ConvertFrom-Json | ForEach-Object { $_ } }
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

# Idle priority and the CPU mask are Windows behaviours: on Linux and macOS, .NET sets them on this process's
# main thread at most, and the processes it starts don't inherit them.
$onWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
if ($onWindows) {
    $mask = if ($Affinity -is [string]) { [Convert]::ToInt64(($Affinity -replace '^0[xX]', ''), 16) } else { [int64]$Affinity }
    if ($mask -le 0 -or $mask -ge ([int64]1 -shl [Math]::Min(62, [Environment]::ProcessorCount))) {
        throw "-Affinity $Affinity isn't a mask of this PC's $([Environment]::ProcessorCount) CPUs."
    }
    $cpuCount = @(0..([Environment]::ProcessorCount - 1) | Where-Object { $mask -band ([int64]1 -shl $_) }).Count
}
else { $cpuCount = [Environment]::ProcessorCount }

if (-not $Changelog.Trim()) { Stop-Publish 'give -Changelog "...": the plugin installer shows it.' }
if (-not $TermsFile) {
    if (-not $env:APPDATA) { Stop-Publish 'there is no %APPDATA% here to find NoirMech\identity-terms.txt in; pass -TermsFile.' }
    $TermsFile = Join-Path $env:APPDATA 'NoirMech\identity-terms.txt'
}
if (-not (Test-Path -LiteralPath $TermsFile -PathType Leaf)) { Stop-Publish "$TermsFile is missing: list the identity terms, one per line." }
$TermsFile = (Resolve-Path -LiteralPath $TermsFile).ProviderPath
$terms = @([IO.File]::ReadAllLines($TermsFile, [Text.Encoding]::UTF8) | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($terms.Count -eq 0) { Stop-Publish "$TermsFile lists no identity terms." }

$self = [Diagnostics.Process]::GetCurrentProcess()
$saved = @{}
try {
    # Before its first process, so git, the tests and the whole Release build are born at Idle on the mask. A
    # setting Windows refuses (a job object can pin the affinity, say) is skipped with a note.
    if ($onWindows) {
        try { $affinityBefore = $self.ProcessorAffinity; $self.ProcessorAffinity = [IntPtr]$mask; $saved.Affinity = $affinityBefore }
        catch { Write-Host "Running on every CPU: Windows won't set the CPU affinity ($($_.Exception.Message))" -ForegroundColor Yellow }
        try { $priorityBefore = $self.PriorityClass; $self.PriorityClass = [Diagnostics.ProcessPriorityClass]::Idle; $saved.Priority = $priorityBefore }
        catch { Write-Host "Running at normal priority: Windows won't lower it ($($_.Exception.Message))" -ForegroundColor Yellow }
    }
    else { Write-Host 'Idle priority and -Affinity work on Windows only: here the tests and the build run at normal priority on every CPU.' -ForegroundColor Yellow }

    $here = (& $git -C $PSScriptRoot rev-parse --abbrev-ref HEAD).Trim()
    if ($here -ne 'main') { Stop-Publish "$PSScriptRoot is on '$here'; switch it to 'main', which the publish commits to and pushes." }
    if (& $git -C $PSScriptRoot status --porcelain) {
        Stop-Publish "$PSScriptRoot has uncommitted changes or untracked files; commit or remove them first, since the publish pushes main."
    }
    if (-not (Test-Path $IconFile)) { Stop-Publish "$IconFile is missing; MitCue's IconUrl points at icons/mitcue.png on main." }

    if (-not (Test-Path ([IO.Path]::Combine($Source, 'src', 'MitCue', 'MitCue.csproj')))) {
        Stop-Publish "$Source isn't a MitCue checkout; make one with 'git worktree add ..\MitCue $Branch' from $PSScriptRoot, or pass -Source."
    }
    $Source = (Resolve-Path $Source).Path

    $currentBranch = (& $git -C $Source rev-parse --abbrev-ref HEAD).Trim()
    if ($currentBranch -ne $Branch) { Stop-Publish "$Source is on '$currentBranch'; switch it to '$Branch' first." }
    if (& $git -C $Source status --porcelain) { Stop-Publish "$Source has uncommitted changes or untracked files; commit or remove them first." }
    $commit = (& $git -C $Source rev-parse HEAD).Trim()

    if (-not $Remote) {
        $matching = @(Invoke-Quiet $git @('-C', $Source, 'remote') | Where-Object {
            ([string](Invoke-Quiet $git @('-C', $Source, 'remote', 'get-url', $_))).Trim() -match $RemotePattern
        })
        if ($matching.Count -eq 0) { Stop-Publish "no remote of $Source points at NoirYamato/DalamudPlugins; add one, or pass -Remote." }
        if ($matching.Count -gt 1) { Stop-Publish "remotes $($matching -join ', ') of $Source all point at NoirYamato/DalamudPlugins; pass -Remote with the one you push $Branch to." }
        $Remote = $matching[0]
    }
    $remoteUrl = [string](Invoke-Quiet $git @('-C', $Source, 'remote', 'get-url', $Remote))
    if ($LASTEXITCODE -ne 0 -or $remoteUrl.Trim() -notmatch $RemotePattern) {
        Stop-Publish "$Source has no remote '$Remote' pointing at NoirYamato/DalamudPlugins; pass -Remote."
    }
    # A full refspec, so a clone that fetches only main gets the remote-tracking mitcue too.
    if ($Fetch) { Invoke-Checked $git @('-C', $Source, 'fetch', '--quiet', $Remote, "+refs/heads/$($Branch):refs/remotes/$Remote/$Branch") }
    $pushed = "refs/remotes/$Remote/$Branch"
    Invoke-Quiet $git @('-C', $Source, 'rev-parse', '--verify', '--quiet', "$pushed^{commit}") | Out-Null
    if ($LASTEXITCODE -ne 0) { Stop-Publish "$Source has no $pushed; push $Branch to $Remote, or pass -Fetch." }
    Invoke-Quiet $git @('-C', $Source, 'merge-base', '--is-ancestor', $commit, $pushed)
    if ($LASTEXITCODE -ne 0) {
        $hint = if ($Fetch) { 'push it first (git push {0} {1})' } else { "push it (git push {0} {1}), or pass -Fetch if it's pushed" }
        Stop-Publish "$($commit.Substring(0, 7)) isn't in $Remote/$Branch as last fetched; $($hint -f $Remote, $Branch)."
    }

    $csproj = [IO.Path]::Combine($Source, 'src', 'MitCue', 'MitCue.csproj')
    $declared = ([string](([xml](Get-Content -Raw $csproj)).Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version).Trim()
    if ($declared -notmatch '^\d+(\.\d+){1,3}$') { Stop-Publish "<Version> in $csproj is '$declared'; Dalamud versions are plain numbers such as 0.1.0." }
    $parts = @($declared.Split('.')) + @('0', '0', '0')
    $base = '{0}.{1}.{2}' -f $parts[0], $parts[1], $parts[2]

    $revision = 1
    $previous = @(Get-RepoEntries $RepoJson) | Where-Object { $_.InternalName -eq $InternalName } | Select-Object -First 1
    if ($previous -and $previous.AssemblyVersion.StartsWith("$base.")) {
        $revision = [int]$previous.AssemblyVersion.Split('.')[3] + 1
    }
    $version = "$base.$revision"
    if ($previous -and [version]$version -le [version]$previous.AssemblyVersion) {
        Stop-Publish "$version isn't higher than the listed $InternalName $($previous.AssemblyVersion), so Dalamud wouldn't offer it; raise <Version> in $csproj."
    }
    Write-Host "Publishing $InternalName $version from $Branch @ $($commit.Substring(0, 7))$(if ($DryRun) { ' (dry run)' })"

    # The whole suite on this exact commit, before anything is built to ship. Without build servers the
    # compiler runs at this script's priority.
    & $dotnet test (Join-Path $Source 'MitCue.slnx') -c Release --disable-build-servers "-m:$cpuCount" --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { Stop-Publish "the MitCue tests failed on $($commit.Substring(0, 7)) (dotnet test exit code $LASTEXITCODE)." }

    $releaseOut = [IO.Path]::Combine($Source, 'src', 'MitCue', 'bin', 'Release')
    if (Test-Path $releaseOut) { Remove-Item -Recurse -Force $releaseOut }
    # ContinuousIntegrationBuild maps source paths to /_/, so PDB paths in the shipped DLLs don't carry the local
    # user folder. Without build servers the compiler runs at this script's priority: Roslyn starts its shared
    # server at normal priority whatever starts it.
    Invoke-Checked $dotnet @('build', $csproj, '-c', 'Release', "-p:Version=$version", '-p:ContinuousIntegrationBuild=true', '--no-incremental', '--disable-build-servers', "-m:$cpuCount", '-nologo', '-v:minimal')

    $packaged = Join-Path $releaseOut $InternalName
    $zip = Join-Path $packaged 'latest.zip'
    $manifestPath = Join-Path $packaged "$InternalName.json"
    if (-not (Test-Path $zip) -or -not (Test-Path $manifestPath)) { throw "Packager output not found in $packaged." }
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    if ($manifest.InternalName -ne $InternalName -or $manifest.AssemblyVersion -ne $version) {
        throw "Built manifest is $($manifest.InternalName) $($manifest.AssemblyVersion), expected $InternalName $version."
    }
    if ($manifest.IconUrl -ne $IconUrl) { throw "Built manifest's IconUrl is '$($manifest.IconUrl)', expected $IconUrl (<IconUrl> in $csproj)." }

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
        Write-Host "Dry run: every gate passed. latest.zip ($zipName) is in $packaged and $sourceName in $releaseOut; nothing was copied, committed or pushed."
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
    if ($saved.ContainsKey('Priority')) {
        try { $self.PriorityClass = $saved.Priority }
        catch { Write-Host "Couldn't restore this PowerShell's priority ($($_.Exception.Message))" -ForegroundColor Yellow }
    }
    if ($saved.ContainsKey('Affinity')) {
        try { $self.ProcessorAffinity = $saved.Affinity }
        catch { Write-Host "Couldn't restore this PowerShell's CPU affinity ($($_.Exception.Message))" -ForegroundColor Yellow }
    }
}
