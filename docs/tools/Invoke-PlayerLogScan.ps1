param(
    [string]$LogPath,
    [switch]$Previous,
    [int]$MaxMatches = 200
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $logFileName = if ($Previous) { 'Player-prev.log' } else { 'Player.log' }
    $LogPath = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\$logFileName"
}

if (-not (Test-Path -LiteralPath $LogPath)) {
    throw "Log file not found: $LogPath"
}

$patterns = @(
    'Error',
    'Exception',
    'Could not',
    'Failed',
    'missing',
    'NullReference',
    'Translation data',
    'Could not resolve',
    'XML error',
    'Patch operation'
)

$ignoredPatterns = @(
    'Fallback handler could not load library .*MonoBleedingEdge',
    'Failed Allocations\. Bucket layout:',
    '^\s*\d+B: .* Failed count:'
)

$matches = Select-String -LiteralPath $LogPath -Pattern $patterns -CaseSensitive:$false |
    Where-Object {
        $line = $_.Line
        $ignored = $false
        foreach ($ignoredPattern in $ignoredPatterns) {
            if ($line -match $ignoredPattern) {
                $ignored = $true
                break
            }
        }

        -not $ignored
    }

if (-not $matches) {
    Write-Host "[logscan] OK: no suspicious lines in $LogPath"
    exit 0
}

Write-Host "[logscan] Found suspicious lines in $LogPath"
$matches |
    Select-Object -First $MaxMatches |
    ForEach-Object {
        Write-Host ("{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim())
    }

if ($matches.Count -gt $MaxMatches) {
    Write-Host "[logscan] Output truncated at $MaxMatches matches."
}

exit 1
