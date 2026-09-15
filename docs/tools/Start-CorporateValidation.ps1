param(
    [ValidateSet('Economy', 'Visual', 'WithoutIdeology')][string]$Mode = 'Economy',
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$temporaryRoot = Join-Path $repository 'TMP'
$saveRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot ('CorporateValidation-' + $RunName)))
if (-not $saveRoot.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
if (Test-Path -LiteralPath $saveRoot) { throw 'Use a new RunName so old results cannot be mistaken for this run.' }
New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey')
$knownIds = @($expansions | ForEach-Object { 'ludeon.rimworld.' + $_ })
if ($Mode -eq 'WithoutIdeology') { $expansions = $expansions | Where-Object { $_ -ne 'ideology' } }
$expansionIds = @($expansions | ForEach-Object { 'ludeon.rimworld.' + $_ })
$active = @('brrainz.harmony', 'ludeon.rimworld') + $expansionIds + @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($knownIds | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$gameRoot = [System.IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$flag = if ($Mode -eq 'Visual') { '-mugirlCorporateVisual' } else { '-mugirlCorporateChecks' }
$arguments = @('-quicktest', $flag, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
if ($Mode -eq 'Visual') { $arguments += '-mugirlCorporateVisualCompact' }
if ($Mode -eq 'WithoutIdeology') { $arguments += '-mugirlCorporateNoIdeology' }
# The caller must first build with EnableCorporateValidation=true. Every test component
# additionally checks its explicit flag and the actual save path inside the running game.
$process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorldWin64.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[PSCustomObject]@{ Mode = $Mode; ProcessId = $process.Id; SaveRoot = $saveRoot }
