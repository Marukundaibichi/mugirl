param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName,
    [switch]$WithYaOpt
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$temporaryRoot = Join-Path $repository 'TMP'
$saveRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot ('StylingStartup-' + $RunName)))
if (-not $saveRoot.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
if (Test-Path -LiteralPath $saveRoot) { throw 'Use a fresh RunName.' }
New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey') | ForEach-Object { 'ludeon.rimworld.' + $_ }
$active = @('brrainz.harmony', 'ludeon.rimworld') + $expansions
if ($WithYaOpt) { $active += 'sz.yaopt' }
$active += @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($expansions | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$gameRoot = [System.IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$arguments = @('-mugirlStylingStartupChecks', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
if ($WithYaOpt) { $arguments += '-mugirlStylingWithYaOpt' }
# 需先显式构建 EnableStylingValidation=true；驱动在启动时检查图标和补丁后退出，不生成世界。
$process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorldWin64.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[PSCustomObject]@{ WithYaOpt = [bool]$WithYaOpt; ProcessId = $process.Id; SaveRoot = $saveRoot }
