param(
    [string]$OutputRoot,
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot 'TMP\GameValidationConfigs'
}

function Write-Step {
    param([string]$Message)
    Write-Host "[validation-config] $Message"
}

function Get-DefaultGameVersion {
    $modsConfigPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'
    if (Test-Path -LiteralPath $modsConfigPath) {
        [xml]$currentConfig = Get-Content -LiteralPath $modsConfigPath
        if (-not [string]::IsNullOrWhiteSpace($currentConfig.ModsConfigData.version)) {
            return $currentConfig.ModsConfigData.version
        }
    }

    return '1.6'
}

function New-ModsConfigText {
    param(
        [string]$GameVersion,
        [string[]]$ActiveMods
    )

    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('<?xml version="1.0" ?>')
    $lines.Add('<ModsConfigData>')
    $lines.Add("  <version>$GameVersion</version>")
    $lines.Add('  <activeMods>')
    foreach ($mod in $ActiveMods) {
        $lines.Add("    <li>$mod</li>")
    }
    $lines.Add('  </activeMods>')
    $lines.Add('  <knownExpansions>')
    foreach ($expansion in @(
        'ludeon.rimworld.royalty',
        'ludeon.rimworld.ideology',
        'ludeon.rimworld.biotech',
        'ludeon.rimworld.anomaly',
        'ludeon.rimworld.odyssey'
    )) {
        $lines.Add("    <li>$expansion</li>")
    }
    $lines.Add('  </knownExpansions>')
    $lines.Add('</ModsConfigData>')
    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-DefaultGameVersion
}

$core = @(
    'brrainz.harmony',
    'ludeon.rimworld',
    'erdelf.humanoidalienraces',
    'har.mugirlrace'
)

$allDlc = @(
    'brrainz.harmony',
    'ludeon.rimworld',
    'ludeon.rimworld.royalty',
    'ludeon.rimworld.ideology',
    'ludeon.rimworld.biotech',
    'ludeon.rimworld.anomaly',
    'ludeon.rimworld.odyssey',
    'erdelf.humanoidalienraces',
    'har.mugirlrace'
)

$facialAnimation = $allDlc + @('nals.facialanimation')
$searchAndDestroy = $allDlc + @('memegoddess.searchanddestroy')
$vcooke = $allDlc + @(
    'oskarpotocki.vanillafactionsexpanded.core',
    'vanillaexpanded.vcooke'
)
$allIntegrations = $allDlc + @(
    'nals.facialanimation',
    'memegoddess.searchanddestroy',
    'oskarpotocki.vanillafactionsexpanded.core',
    'vanillaexpanded.vcooke'
)

$configs = [ordered]@{
    '01-minimal.xml' = $core
    '02-all-dlc.xml' = $allDlc
    '03-facial-animation.xml' = $facialAnimation
    '04-search-and-destroy.xml' = $searchAndDestroy
    '05-vcooke.xml' = $vcooke
    '06-all-integrations.xml' = $allIntegrations
}

if (-not (Test-Path -LiteralPath $OutputRoot)) {
    New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
}

foreach ($entry in $configs.GetEnumerator()) {
    $path = Join-Path $OutputRoot $entry.Key
    New-ModsConfigText -GameVersion $Version -ActiveMods $entry.Value |
        Set-Content -LiteralPath $path -Encoding utf8NoBOM
    Write-Step "Wrote $path"
}

Write-Step "Done"
