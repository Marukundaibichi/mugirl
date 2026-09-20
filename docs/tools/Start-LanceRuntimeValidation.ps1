param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$temporaryRoot = Join-Path $repository 'TMP'
$saveRoot = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('LanceValidation-' + $RunName)))
if (-not $saveRoot.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated test path.' }
if (Test-Path -LiteralPath $saveRoot) { throw 'Use a fresh RunName.' }
# 此处只写普通配置和日志，不创建游戏目录或联接。
New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey') | ForEach-Object { 'ludeon.rimworld.' + $_ }
$active = @('brrainz.harmony', 'ludeon.rimworld') + $expansions + @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($expansions | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$gameRoot = [IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$arguments = @('-quicktest', '-mugirlLanceChecks', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
# 需先显式构建 EnableLanceValidation=true；只运行 quicktest 新地图，完成后自动退出。
$process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorldWin64.exe') -WorkingDirectory $gameRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[pscustomobject]@{ ProcessId = $process.Id; SaveRoot = $saveRoot }
