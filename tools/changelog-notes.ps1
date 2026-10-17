# Prints one version's entries from CHANGELOG.md, for GitHub release notes and the
# Changelog field in the hub repo.json.
#
#   .\tools\changelog-notes.ps1 -Version 0.1.0.0
#   .\tools\changelog-notes.ps1 -Version Unreleased
#   .\tools\changelog-notes.ps1 -Version 0.1.0.0 -OneLine    (bullets joined with spaces)
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$OneLine
)

$path = Join-Path $PSScriptRoot '..\CHANGELOG.md'
$lines = (Get-Content -Raw $path).Replace("`r`n", "`n").Split("`n")

$items = New-Object System.Collections.Generic.List[string]
$inside = $false
foreach ($line in $lines) {
    if ($line.StartsWith('## ')) {
        $heading = $line.Substring(3).Trim()
        $inside = $heading -eq $Version -or $heading.StartsWith("$Version ")
        continue
    }
    if (-not $inside) { continue }
    if ($line.StartsWith('- ')) { $items.Add($line.Substring(2).Trim()) }
    elseif ($line.StartsWith('  ') -and $line.Trim().Length -gt 0 -and $items.Count -gt 0) { $items[$items.Count - 1] += ' ' + $line.Trim() }
}

if ($items.Count -eq 0) { throw "No entries for '$Version' in CHANGELOG.md" }

if ($OneLine) { ($items | ForEach-Object { $_.TrimEnd('.') + '.' }) -join ' ' }
else { $items | ForEach-Object { "- $_" } }
