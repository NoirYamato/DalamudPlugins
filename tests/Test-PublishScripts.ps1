<#
Checks the publish scripts without building anything: that both keep each other's repo.json entry, non-ASCII text
included, and read their JSON as UTF-8; that publish-mitcue.ps1's -ScanHistory finds identity terms anywhere in a
branch's history; and that both scripts stay ASCII and free of PowerShell 7-only syntax. Run it from this folder
after changing either script:

  powershell -ExecutionPolicy Bypass -File .\tests\Test-PublishScripts.ps1

It runs in Windows PowerShell 5.1, which the scripts are written for, and in PowerShell 7, where it emulates what
5.1 does differently: Get-Content reads a file without a BOM in the ANSI code page unless -Encoding is given, and
ConvertFrom-Json emits a top-level array as one object. It needs git, works in a temporary folder and exits 1 if a
check fails.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$noirScript = Join-Path $root 'publish.ps1'
$mitScript = Join-Path $root 'publish-mitcue.ps1'
$git = (Get-Command git).Source
# This PowerShell's own executable, to run publish-mitcue.ps1 the way the maintainer does.
$shell = if ($PSVersionTable.PSEdition -eq 'Core') {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) { $pwsh.Source } else { Join-Path $PSHOME 'pwsh' }
}
else { Join-Path $PSHOME 'powershell.exe' }
$utf8 = New-Object Text.UTF8Encoding $false
$script:checks = 0
$script:failures = New-Object Collections.Generic.List[string]

function Assert([bool]$ok, [string]$what) {
    $script:checks++
    if (-not $ok) { $script:failures.Add($what); Write-Host "FAIL $what" -ForegroundColor Red }
}

if ($PSVersionTable.PSVersion.Major -ge 6) {
    $global:AnsiForTests = [Text.Encoding]::GetEncoding(1252)
    function global:Get-Content {
        [CmdletBinding()]
        param([Parameter(Position = 0)][string]$Path, [switch]$Raw, [object]$Encoding)
        if ($PSBoundParameters.ContainsKey('Encoding')) {
            return Microsoft.PowerShell.Management\Get-Content -Path $Path -Raw:$Raw -Encoding $Encoding
        }
        $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Path).ProviderPath)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
        }
        else { $global:AnsiForTests.GetString($bytes) }
    }
    function global:ConvertFrom-Json {
        [CmdletBinding()]
        param([Parameter(Mandatory, ValueFromPipeline)][AllowEmptyString()][string]$InputObject)
        begin { $lines = New-Object Collections.Generic.List[string] }
        process { $lines.Add($InputObject) }
        end { Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject ($lines -join "`n") -NoEnumerate }
    }
    # 5.1's alias that makes ConvertTo-Json write a PSObject-wrapped array as {"value":[...],"Count":n}.
    Update-TypeData -TypeName System.Array -MemberType AliasProperty -MemberName Count -Value Length -Force
    Write-Host "PowerShell $($PSVersionTable.PSVersion), emulating Windows PowerShell 5.1's Get-Content and ConvertFrom-Json"
}
else { Write-Host "Windows PowerShell $($PSVersionTable.PSVersion)" }

function Get-ScriptAst([string]$path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    Assert ($errors.Count -eq 0) "$(Split-Path -Leaf $path) parses ($(@($errors | ForEach-Object { $_.Message }) -join '; '))"
    $ast
}

# Runs $code with the named functions of a publish script defined, in a scope of its own.
function Invoke-WithFunctions([string]$path, [string[]]$names, [scriptblock]$code, [object[]]$arguments) {
    $ast = Get-ScriptAst $path
    $definitions = foreach ($name in $names) {
        $found = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
        if (-not $found) { throw "$(Split-Path -Leaf $path) has no function $name." }
        $found.Extent.Text
    }
    & {
        param($definitions, $code, $arguments)
        . ([scriptblock]::Create($definitions))
        . $code @arguments
    } ($definitions -join "`n") $code $arguments
}

# repo.json's entries as written, read without the publish scripts' code.
function Read-Entries([string]$path) {
    [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | Microsoft.PowerShell.Utility\ConvertFrom-Json | ForEach-Object { $_ }
}

function Get-Json($value) { ConvertTo-Json -InputObject $value -Depth 5 -Compress }

function New-Entry([string]$name, [string]$version, [string]$changelog, [string]$description) {
    $download = "https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/plugins/$name/$name-$version.zip"
    [pscustomobject][ordered]@{
        Author = 'Noir Yamato'; Name = $name; Punchline = "$name punchline"; Description = $description
        InternalName = $name; AssemblyVersion = $version; RepoUrl = 'https://github.com/NoirYamato/DalamudPlugins'
        ApplicableVersion = 'any'; Tags = @('one', 'two'); DalamudApiLevel = 15
        IconUrl = "https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/icons/$($name.ToLowerInvariant()).png"
        DownloadLinkInstall = $download; DownloadLinkTesting = $download; DownloadLinkUpdate = $download
        IsHide = $false; IsTestingExclusive = $false; DownloadCount = 0; LastUpdate = '1794283074'; Changelog = $changelog
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("publish-scripts-test-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $temp | Out-Null
try {
    # --- Both scripts: ASCII only (5.1 reads a script without a BOM in the ANSI code page) and no 7-only syntax.
    $sevenOnlyParameters = @('AsByteStream', 'NoEnumerate', 'AsHashtable', 'Parallel', 'AdditionalChildPath', 'EscapeHandling', 'AsArray', 'SkipCertificateCheck', 'LeafBase')
    foreach ($path in @($noirScript, $mitScript)) {
        $leaf = Split-Path -Leaf $path
        Assert (@([IO.File]::ReadAllBytes($path) | Where-Object { $_ -ge 0x80 }).Count -eq 0) "$leaf is ASCII only"
        $ast = Get-ScriptAst $path
        $sevenOnly = @($ast.FindAll({
            param($node)
            $type = $node.GetType().Name
            ($type -eq 'TernaryExpressionAst') -or ($type -eq 'PipelineChainAst') -or
            ($type -eq 'BinaryExpressionAst' -and "$($node.Operator)" -eq 'QuestionQuestion') -or
            ($type -eq 'AssignmentStatementAst' -and "$($node.Operator)" -eq 'QuestionQuestionEquals') -or
            ($node.PSObject.Properties['NullConditional'] -and $node.NullConditional) -or
            ($type -eq 'CommandParameterAst' -and $sevenOnlyParameters -contains $node.ParameterName)
        }, $true) | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Extent.Text)" })
        Assert ($sevenOnly.Count -eq 0) "$leaf has no PowerShell 7-only syntax ($($sevenOnly -join '; '))"
    }

    # --- repo.json: each script replaces only its own entry and keeps the other's text, non-ASCII included.
    $dash = [string][char]0x2014; $apostrophe = [string][char]0x2019; $times = [string][char]0x00D7; $dots = [string][char]0x2026
    $noir = New-Entry 'NoirMech' '0.5.1.12' "P6 Gauntlet $dash R2${apostrophe}s Meteor is 3$times faster" "AnoMech $dash with LPDU"
    $mit = New-Entry 'MitCue' '0.1.0.1' "First build $dash reminders won${apostrophe}t fire twice$dots" "Mitigation cues $dash reminder-only"
    $repoJson = Join-Path $temp 'repo.json'
    $steps = @(
        @{ Script = $mitScript; Entry = (New-Entry 'MitCue' '0.1.0.2' "Second $dash it${apostrophe}s 2$times better" 'MitCue') }
        @{ Script = $noirScript; Entry = (New-Entry 'NoirMech' '0.5.1.13' "Practice $dash Retry${dots}" 'NoirMech') }
        @{ Script = $mitScript; Entry = (New-Entry 'MitCue' '0.1.0.3' "Third $dash fine" 'MitCue') }
        @{ Script = $noirScript; Entry = (New-Entry 'NoirMech' '0.5.1.14' "Again $dash fine" 'NoirMech') }
    )
    foreach ($bom in @($false, $true)) {
        $start = (ConvertTo-Json -InputObject @($noir, $mit) -Depth 5) + "`n"
        if ($bom) { [IO.File]::WriteAllText($repoJson, $start, (New-Object Text.UTF8Encoding $true)) }
        else { [IO.File]::WriteAllText($repoJson, $start, $utf8) }
        $expected = @{ NoirMech = $noir; MitCue = $mit }
        $label = if ($bom) { 'repo.json with a BOM' } else { 'repo.json' }
        foreach ($step in $steps) {
            $who = $step.Entry.InternalName
            $other = if ($who -eq 'MitCue') { 'NoirMech' } else { 'MitCue' }
            $listed = @(Invoke-WithFunctions $step.Script @('Get-RepoEntries') { param($path) Get-RepoEntries $path } @($repoJson))
            Assert ($listed.Count -eq 2 -and $listed[0].InternalName -eq 'NoirMech' -and $listed[1].InternalName -eq 'MitCue') "$label, $(Split-Path -Leaf $step.Script): Get-RepoEntries emits both entries one by one"
            $own = @($listed | Where-Object { $_.InternalName -eq $who })
            Assert ($own.Count -eq 1 -and (Get-Json $own[0]) -eq (Get-Json $expected[$who])) "$label, $(Split-Path -Leaf $step.Script): Get-RepoEntries reads $who's entry as written, non-ASCII text included"
            Invoke-WithFunctions $step.Script @('Write-Utf8', 'Get-RepoEntries', 'Set-RepoEntry') { param($path, $entry) Set-RepoEntry $path $entry } @($repoJson, $step.Entry)
            $expected[$who] = $step.Entry
            $written = @(Read-Entries $repoJson)
            Assert ($written.Count -eq 2 -and $written[0].InternalName -eq 'NoirMech' -and $written[1].InternalName -eq 'MitCue') "$label, $who publish: both entries stay, in their order"
            $kept = @($written | Where-Object { $_.InternalName -eq $other })
            Assert ($kept.Count -eq 1 -and (Get-Json $kept[0]) -eq (Get-Json $expected[$other])) "$label, $who publish: $other's entry is unchanged, non-ASCII text included (now: $(if ($kept.Count) { $kept[0].Changelog }))"
            $new = @($written | Where-Object { $_.InternalName -eq $who })
            Assert ($new.Count -eq 1 -and (Get-Json $new[0]) -eq (Get-Json $step.Entry)) "$label, $who publish: the new $who entry is written as given"
            $head = [IO.File]::ReadAllBytes($repoJson)
            Assert (-not ($head[0] -eq 0xEF -and $head[1] -eq 0xBB)) "$label, $who publish: repo.json is written without a BOM"
        }
    }

    # --- The built manifest is read as UTF-8 too.
    $manifestPath = Join-Path $temp 'manifest.json'
    $description = "Reminders $dash for your own actions${dots}"
    [IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject ([pscustomobject]@{ InternalName = 'X'; Description = $description })), $utf8)
    foreach ($path in @($noirScript, $mitScript)) {
        $statement = (Get-ScriptAst $path).Find({
            param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$manifest'
        }, $true)
        Assert ($null -ne $statement) "$(Split-Path -Leaf $path) reads the built manifest into `$manifest"
        if ($statement) {
            $read = & { param($manifestPath, $statement) . ([scriptblock]::Create($statement)); $manifest } $manifestPath $statement.Extent.Text
            Assert ($read.Description -eq $description) "$(Split-Path -Leaf $path) reads the built manifest as UTF-8 (read: $($read.Description))"
        }
    }

    # --- -ScanHistory: a repository whose last commit is clean, with a term in each place only history holds.
    function Invoke-Git([string]$repo, [string[]]$arguments) {
        $ErrorActionPreference = 'Continue'
        & $git -C $repo -c core.autocrlf=false -c commit.gpgsign=false @arguments 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $($arguments -join ' ') failed in $repo with exit code $LASTEXITCODE." }
    }
    function Write-File([string]$repo, [string]$path, [byte[]]$bytes) {
        $full = [IO.Path]::Combine($repo, $path)
        New-Item -ItemType Directory -Force (Split-Path -Parent $full) | Out-Null
        [IO.File]::WriteAllBytes($full, $bytes)
    }
    function New-Xlsx([string]$workbook) {
        Add-Type -AssemblyName System.IO.Compression
        $memory = New-Object IO.MemoryStream
        $zip = New-Object IO.Compression.ZipArchive($memory, [IO.Compression.ZipArchiveMode]::Create, $true)
        $writer = New-Object IO.StreamWriter($zip.CreateEntry('xl/workbook.xml').Open(), $utf8)
        $writer.Write($workbook)
        $writer.Dispose()
        $zip.Dispose()
        , $memory.ToArray()
    }
    function New-MitCueRepo([string]$name, [bool]$withXlsx) {
        $repo = Join-Path $temp $name
        New-Item -ItemType Directory $repo | Out-Null
        Invoke-Git $repo @('init', '-q')
        Invoke-Git $repo @('symbolic-ref', 'HEAD', 'refs/heads/mitcue')
        Write-File $repo 'src/MitCue/MitCue.csproj' $utf8.GetBytes("<Project />`n")
        Write-File $repo 'src/MitCue/Plugin.cs' $utf8.GetBytes("public sealed class Plugin : IDalamudPlugin { }`n")
        Write-File $repo 'src/MitCue/Import/GoogleLinks.cs' $utf8.GetBytes("return `"$dots`" + id[^5..];`n")
        if ($withXlsx) { Write-File $repo 'tests/fixtures/sheets/lpdu/export/lpdu.xlsx' (New-Xlsx '<sheet name="General Tank Mit til P5"/>') }
        $repo
    }
    function Save-Commit([string]$repo, [string]$message, [string]$author) {
        Invoke-Git $repo @('add', '-A')
        Invoke-Git $repo @('-c', "user.name=$author", '-c', 'user.email=dev@example.invalid', 'commit', '-q', '--allow-empty', '-m', $message)
    }
    function Invoke-ScanHistory([string]$repo, [string]$terms) {
        $termsFile = Join-Path $temp 'terms.txt'
        [IO.File]::WriteAllText($termsFile, $terms, $utf8)
        $ErrorActionPreference = 'Continue'
        $output = & $shell -NoProfile -ExecutionPolicy Bypass -File $mitScript -ScanHistory -Source $repo -TermsFile $termsFile -Affinity 1 2>&1 | Out-String
        @{ Code = $LASTEXITCODE; Output = $output }
    }

    $repo = New-MitCueRepo 'history' $true
    Write-File $repo 'docs/notes.md' $utf8.GetBytes("Notes by Alpha-Term`n")
    Write-File $repo 'data/utf16.txt' ([Text.Encoding]::Unicode.GetBytes("Gamma Term was here"))
    Write-File $repo 'docs/delta-term/readme.md' $utf8.GetBytes("nothing to see`n")
    Save-Commit $repo 'Skeleton' 'MitCue Dev'
    Write-File $repo 'tests/fixtures/sheets/lpdu/export/lpdu.xlsx' (New-Xlsx '<sheet name="General Tank Mit til P5"/><sheet name="Epsilon-Term"/>')
    Save-Commit $repo 'Thanks to Beta-Term for the sheet' 'MitCue Dev'
    Remove-Item -Recurse -Force (Join-Path $repo 'docs'), (Join-Path $repo 'data')
    Write-File $repo 'tests/fixtures/sheets/lpdu/export/lpdu.xlsx' (New-Xlsx '<sheet name="General Tank Mit til P5"/>')
    Save-Commit $repo 'Clean up' 'Zeta Term'
    $terms = "alpha-term`nBETA-TERM`ngamma term`nDelta-Term`nepsilon-term`nzeta term`nnever-here`n"

    $run = Invoke-ScanHistory $repo $terms
    Assert ($run.Code -eq 1) "-ScanHistory refuses a history that holds identity terms (exit $($run.Code))"
    Assert ($run.Output -match 'line 1 of the terms file is in history blob [0-9a-f]{7}: docs/notes\.md \(') "-ScanHistory finds a term in a file a later commit deleted"
    Assert ($run.Output -match 'line 2 of the terms file is in history commit [0-9a-f]{7}') "-ScanHistory finds a term in a commit message"
    Assert ($run.Output -match 'line 3 of the terms file is in history blob [0-9a-f]{7}: data/utf16\.txt \(utf16\)') "-ScanHistory finds a UTF-16 term in a deleted file"
    Assert ($run.Output -match 'line 4 of the terms file is in history commit [0-9a-f]{7}') "-ScanHistory finds a term in a file path"
    Assert ($run.Output -match 'line 5 of the terms file is in history blob [0-9a-f]{7}: tests/fixtures/sheets/lpdu/export/lpdu\.xlsx > xl/workbook\.xml \(') "-ScanHistory finds a term inside an old version of an .xlsx"
    Assert ($run.Output -match 'line 6 of the terms file is in history commit [0-9a-f]{7}') "-ScanHistory finds a term in an author name"
    Assert ($run.Output -notmatch 'line 7 of the terms file') "-ScanHistory reports no term that isn't there"
    Assert ($run.Output -notmatch "didn't find its") "-ScanHistory finds every control in the history"
    foreach ($term in @('alpha-term', 'beta-term', 'gamma term', 'delta-term', 'epsilon-term', 'zeta term')) {
        Assert ($run.Output.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -lt 0) "-ScanHistory never prints a term ($term)"
    }

    $run = Invoke-ScanHistory $repo "never-here`n"
    Assert ($run.Code -eq 0 -and $run.Output -match 'History scan clean') "-ScanHistory passes a history without the terms (exit $($run.Code))"

    $bare = New-MitCueRepo 'no-xlsx' $false
    Save-Commit $bare 'Skeleton' 'MitCue Dev'
    $run = Invoke-ScanHistory $bare "never-here`n"
    Assert ($run.Code -eq 1 -and $run.Output -match "didn't find its ascii control 'name=`"General Tank Mit til P5`"'") "-ScanHistory refuses when a control is missing (exit $($run.Code))"

    Invoke-Git $repo @('checkout', '-q', '-b', 'other')
    $run = Invoke-ScanHistory $repo "never-here`n"
    Assert ($run.Code -eq 1 -and $run.Output -match "switch it to 'mitcue' first") "-ScanHistory refuses a checkout that isn't on mitcue (exit $($run.Code))"
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}

if ($script:failures.Count -gt 0) {
    Write-Host "$($script:failures.Count) of $($script:checks) checks failed." -ForegroundColor Red
    exit 1
}
Write-Host "All $($script:checks) checks passed."
