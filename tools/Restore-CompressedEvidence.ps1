<#
.SYNOPSIS
    Restore one compressed validation artifact to its recorded original path without overwriting existing files.
.PARAMETER Path
    Original repository-relative path from DevData/Evidence/compression-manifest.jsonl, without the added .gz suffix.
#>
#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskEvidence = Join-Path $taskRoot 'DevData/Evidence'
$taskManifest = Join-Path $taskEvidence 'compression-manifest.jsonl'
$taskRelative = $Path.Replace('\','/')
$taskRows = @(Get-Content -LiteralPath $taskManifest | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object path -CEQ $taskRelative)
if ($taskRows.Count -ne 1) { throw "Expected exactly one archived artifact for '$Path'; found $($taskRows.Count)." }
$taskRecord = $taskRows[0]

function Resolve-SafePath([string] $Relative) {
    $resolved = [IO.Path]::GetFullPath((Join-Path $taskRoot $Relative))
    if (-not $resolved.StartsWith($taskEvidence + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact path leaves DevData/Evidence.' }
    $current = $resolved
    while ($current.Length -ge $taskRoot.Length) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refuse linked path: $current" }
        }
        if ($current -eq $taskRoot) { break }
        $current = Split-Path $current -Parent
    }
    return $resolved
}

$taskTarget = Resolve-SafePath $taskRecord.path
$taskGzip = Resolve-SafePath $taskRecord.gzip
if (Test-Path -LiteralPath $taskTarget) {
    if ((Get-FileHash -LiteralPath $taskTarget).Hash -ne $taskRecord.sha256) { throw 'Existing original differs; refusing to overwrite it.' }
    Write-Output "Already restored and verified: $taskTarget"
    exit 0
}
if ((Get-FileHash -LiteralPath $taskGzip).Hash -ne $taskRecord.gzipSha256) { throw 'Compressed artifact hash mismatch.' }
$taskTemporary = Resolve-SafePath ($taskRecord.path + '.restore-' + [guid]::NewGuid().ToString('N') + '.partial')
try {
    $taskInput = [IO.File]::OpenRead($taskGzip)
    try {
        $taskReader = [IO.Compression.GZipStream]::new($taskInput, [IO.Compression.CompressionMode]::Decompress, $true)
        try {
            $taskOutput = [IO.File]::Open($taskTemporary, [IO.FileMode]::CreateNew)
            try { $taskReader.CopyTo($taskOutput) } finally { $taskOutput.Dispose() }
        } finally { $taskReader.Dispose() }
    } finally { $taskInput.Dispose() }
    if ((Get-Item -LiteralPath $taskTemporary).Length -ne $taskRecord.bytes -or (Get-FileHash -LiteralPath $taskTemporary).Hash -ne $taskRecord.sha256) { throw 'Restored artifact content mismatch.' }
    Move-Item -LiteralPath $taskTemporary -Destination $taskTarget -ErrorAction Stop
    (Get-Item -LiteralPath $taskTarget).LastWriteTimeUtc = ([datetime]$taskRecord.lastWriteUtc).ToUniversalTime()
    Write-Output "Restored and SHA256 verified: $taskTarget"
} finally {
    if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary }
}
