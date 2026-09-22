param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName,
    [switch]$WithYaOpt,
    [switch]$FaceAccessories,
    [switch]$LongCascadeHair,
    [switch]$WithFacialAnimation
)

$ErrorActionPreference = 'Stop'
if ($FaceAccessories -and $LongCascadeHair) { throw 'Select only one runtime check mode.' }
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$temporaryRoot = Join-Path $repository 'TMP'
$saveRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot ('StylingStartup-' + $RunName)))
if (-not $saveRoot.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
if (Test-Path -LiteralPath $saveRoot) { throw 'Use a fresh RunName.' }
New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey') | ForEach-Object { 'ludeon.rimworld.' + $_ }
$active = @()
# YaOpt 的延迟纹理加载依赖 Prepatcher；包含它才能覆盖用户报告中的 ContentManager 路径。
if ($WithYaOpt) { $active += 'zetrith.prepatcher' }
$active += @('brrainz.harmony', 'ludeon.rimworld') + $expansions
if ($WithYaOpt) { $active += 'sz.yaopt' }
$active += @('erdelf.humanoidalienraces', 'har.mugirlrace')
if ($WithFacialAnimation) { $active += 'nals.facialanimation' }
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($expansions | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$gameRoot = [System.IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$arguments = @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
if ($WithYaOpt) { $arguments += '-mugirlStylingWithYaOpt' }
if ($LongCascadeHair) { $arguments += @('-quicktest', '-mugirlLongCascadeChecks') }
elseif ($FaceAccessories) { $arguments += @('-quicktest', '-mugirlFaceAccessoryChecks') }
else { $arguments += '-mugirlStylingStartupChecks' }
# 需先显式构建 EnableStylingValidation=true；外观专项模式在独立 quicktest 地图检查并截图后退出。
$process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorldWin64.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[PSCustomObject]@{ WithYaOpt = [bool]$WithYaOpt; ProcessId = $process.Id; SaveRoot = $saveRoot }
