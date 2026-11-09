<#
.SYNOPSIS
    Previews merging an AnoMech ref into this branch: the files that would conflict, and NoirMech's own lines
    that use a declaration the ref deletes.

.DESCRIPTION
    Nothing is merged and no branch moves: git merge-tree does the trial merge in memory.

    NoirMech's lines are the ones changed or added since the AnoMech release last merged (Base), in tracked and
    untracked .cs files alike, so a line upstream wrote is never reported. A deletion is a public, internal or
    protected declaration (or an interface member) the ref removed whose name its .cs files no longer mention.
    Where NoirMech declares the same name itself, only uses through the deleted one's type are reported.

    A grep, not a compiler: a changed signature, a moved member or a name the ref still mentions elsewhere goes
    unreported, so a clean report still needs a build of the merge. Exits with 1 when it finds a conflict or a use.

.PARAMETER Ref
    The AnoMech ref to preview: a release tag, or upstream/master for unreleased work.

.PARAMETER Base
    The AnoMech release this branch last merged. By default the newest v* tag HEAD contains.

.PARAMETER All
    Lists every deletion, used or not.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\upstream-check.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\upstream-check.ps1 -Ref v0.5.2.0
#>
param(
    [string]$Ref = 'upstream/master',
    [string]$Base = '',
    [switch]$All
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# git merge-tree and git grep exit with 1 for "conflicts" and "no match"; only above that is a failure.
function Invoke-Git {
    $output = & git -C $repo -c core.safecrlf=false @args
    if ($LASTEXITCODE -gt 1) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return $output
}

$modifiers = 'public|internal|private|protected|static|const|readonly|override|virtual|abstract|sealed|partial|' +
             'extern|unsafe|async|new|required|event|class|struct|record|interface|enum'
$declaration = New-Object regex ('^(?<indent>\s*)(?:\[[^\]]*\]\s*)*(?<modifiers>(?:(?:' + $modifiers + ')\s+)+)' +
                                 '[\w<>\[\],.?() ]*?\b(?<name>[A-Za-z_]\w*)\s*(?:\(|\{|=>|=(?!=)|;|:|$)')
$interfaceMember = New-Object regex ('^(?<indent> {4})(?!(?:return|throw|var|await|yield|using|if|for|foreach|while|switch|' +
                                     'case|else|new|goto|break|continue)\b)[\w<>\[\],.?]+\s+(?<name>[A-Za-z_]\w*)\s*(?:\(|\{|=>|;)')
$typeDeclaration = New-Object regex '^(?<indent>\s*)[\w\s]*\b(?:class|struct|record|interface|enum)\s+(?<name>[A-Za-z_]\w*)'
$visible = New-Object regex '\b(?:public|internal|protected)\b'

# The name a line declares, or $null. Without a modifier only an interface's member counts.
function Get-Declaration([string]$line, [bool]$inInterface) {
    $match = $declaration.Match($line)
    if ($match.Success) {
        return [pscustomobject]@{ Name = $match.Groups['name'].Value; Indent = $match.Groups['indent'].Length
                                  Visible = $visible.IsMatch($match.Groups['modifiers'].Value) }
    }
    if ($inInterface) {
        $match = $interfaceMember.Match($line)
        if ($match.Success) { return [pscustomobject]@{ Name = $match.Groups['name'].Value; Indent = 4; Visible = $true } }
    }
    return $null
}

function Test-InterfaceFile([string]$path) { return [IO.Path]::GetFileName($path) -cmatch '^I[A-Z]\w*\.cs$' }

# Every line a diff adds, with its line number in the newer file.
function Get-AddedLines([string[]]$diff) {
    $file = $null
    $number = 0
    foreach ($line in $diff) {
        if ($line.StartsWith('+++ ')) {
            $file = if ($line -eq '+++ /dev/null') { $null } else { $line.Substring(6) }
        }
        elseif ($line -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@') {
            $number = [int]$Matches[1]
        }
        elseif ($line.StartsWith('+') -and $file) {
            [pscustomobject]@{ File = $file; Line = $number; Text = $line.Substring(1) }
            $number++
        }
    }
}

# The innermost type around a line of a file at $Base: the deleted member's owner.
$baseFiles = @{}
function Get-Owner([string]$file, [int]$line, [int]$indent) {
    if (-not $baseFiles.ContainsKey($file)) { $baseFiles[$file] = @(Invoke-Git show "${Base}:$file") }
    $lines = $baseFiles[$file]
    for ($i = $line - 2; $i -ge 0; $i--) {
        $match = $typeDeclaration.Match($lines[$i])
        if ($match.Success -and $match.Groups['indent'].Length -lt $indent) { return $match.Groups['name'].Value }
    }
    return $null
}

# Comments and string literals can name anything.
function Get-Code([string]$line) {
    $code = [regex]::Replace($line, '"(?:[^"\\]|\\.)*"', '""')
    $comment = $code.IndexOf('//')
    if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
    return $code
}

if (-not $Base) { $Base = (Invoke-Git describe --tags --abbrev=0 --match 'v*' HEAD) }
$head = (Invoke-Git rev-parse --short HEAD)
$refCommit = (Invoke-Git rev-parse --short "$Ref^{commit}")
Write-Output "NoirMech at $head (AnoMech base $Base) against $Ref ($refCommit)"
Write-Output ''

$merge = @(Invoke-Git merge-tree --write-tree --name-only --no-messages HEAD $Ref)
$conflicts = @($merge | Select-Object -Skip 1 | Where-Object { $_ -ne '' })
if ($conflicts.Count -eq 0) {
    Write-Output 'Merges without conflicts.'
}
else {
    Write-Output "Conflicts ($($conflicts.Count)):"
    $conflicts | ForEach-Object { Write-Output "  $_" }
}
Write-Output ''

$removed = New-Object 'System.Collections.Generic.Dictionary[string,object]'
$sourceFile = $null
$oldLine = 0
foreach ($line in @(Invoke-Git diff -U0 $Base $Ref -- '*.cs')) {
    if ($line.StartsWith('--- ')) {
        $sourceFile = if ($line -eq '--- /dev/null') { $null } else { $line.Substring(6) }
    }
    elseif ($line -match '^@@ -(\d+)') {
        $oldLine = [int]$Matches[1]
    }
    elseif ($line.StartsWith('-') -and $sourceFile) {
        $found = Get-Declaration $line.Substring(1) (Test-InterfaceFile $sourceFile)
        if ($found -and $found.Visible -and -not $removed.ContainsKey($found.Name)) {
            $removed[$found.Name] = [pscustomobject]@{ File = $sourceFile; Line = $oldLine; Indent = $found.Indent }
        }
        $oldLine++
    }
}

$deleted = @()
if ($removed.Count -gt 0) {
    $alternation = ($removed.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $mentioned = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($word in @(Invoke-Git grep -h -o -w -E "($alternation)" $Ref -- '*.cs')) { [void]$mentioned.Add($word) }
    $deleted = @($removed.Keys | Where-Object { -not $mentioned.Contains($_) } | Sort-Object)
}

$noirLines = @(Get-AddedLines @(Invoke-Git diff -U0 $Base -- '*.cs'))
foreach ($file in @(Invoke-Git ls-files --others --exclude-standard -- '*.cs')) {
    $number = 0
    foreach ($text in Get-Content -LiteralPath (Join-Path $repo $file)) {
        $number++
        $noirLines += [pscustomobject]@{ File = $file; Line = $number; Text = $text }
    }
}

$owners = New-Object 'System.Collections.Generic.Dictionary[string,string]'
foreach ($name in $deleted) { $owners[$name] = Get-Owner $removed[$name].File $removed[$name].Line $removed[$name].Indent }

$uses = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[string]]'
if ($deleted.Count -gt 0) {
    $ownDeclarations = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($line in $noirLines) {
        $found = Get-Declaration $line.Text (Test-InterfaceFile $line.File)
        if ($found) { [void]$ownDeclarations.Add($found.Name) }
    }
    $names = New-Object regex ('\b(?:' + (($deleted | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b')
    foreach ($line in $noirLines) {
        $code = Get-Code $line.Text
        foreach ($match in $names.Matches($code)) {
            $name = $match.Value
            $found = Get-Declaration $code (Test-InterfaceFile $line.File)
            if ($found -and $found.Name -ceq $name) { continue }
            if ($ownDeclarations.Contains($name)) {
                if (-not $owners[$name]) { continue }
                $qualified = '\b' + [regex]::Escape($owners[$name]) + '\s*\.\s*' + [regex]::Escape($name) + '\b'
                if ($code -cnotmatch $qualified) { continue }
            }
            if (-not $uses.ContainsKey($name)) { $uses[$name] = New-Object 'System.Collections.Generic.List[string]' }
            $uses[$name].Add("$($line.File):$($line.Line): $($line.Text.Trim())")
            break
        }
    }
}

$count = ($uses.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
if (-not $count) { $count = 0 }
Write-Output "Declarations $Ref deletes: $($deleted.Count). NoirMech's lines use them $count times."
foreach ($name in $deleted) {
    if (-not $All -and -not $uses.ContainsKey($name)) { continue }
    $owner = if ($owners[$name]) { "$($owners[$name])." } else { '' }
    Write-Output "  $owner$name, in $($removed[$name].File) at ${Base}$(if ($uses.ContainsKey($name)) { ':' })"
    if ($uses.ContainsKey($name)) { $uses[$name] | ForEach-Object { Write-Output "    $_" } }
}
if ($conflicts.Count -gt 0 -or $count -gt 0) { exit 1 }
