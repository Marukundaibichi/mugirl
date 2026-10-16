param(
    [string]$RunName = ('BugReport-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [ValidateSet('ChineseSimplified', 'English')][string]$Language = 'ChineseSimplified',
    [switch]$CoreOnly
)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $modRoot 'TMP'))
$runPath = [IO.Path]::GetFullPath((Join-Path $temporaryRoot $RunName))
if (-not $runPath.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '验证目录必须位于本模组 TMP 内。'
}
if (Test-Path -LiteralPath $runPath) { throw '验证目录已经存在，请使用新 RunName。' }
if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) { throw '请先结束现有游戏进程；不会替换运行中的模组文件。' }
$probePath = Join-Path $modRoot '1.6/Assemblies/CorporateBugReportProbe.dll'
if (Test-Path -LiteralPath $probePath) { throw '验证 DLL 已经存在，请先核对其来源。' }
$gameRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '../..'))
$harmonyPath = [IO.Path]::GetFullPath((Join-Path $modRoot '../../../../workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll'))
$compiler = 'C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
$configPath = Join-Path $runPath 'Config'
New-Item -ItemType Directory -Path $configPath -Force | Out-Null
$references = @(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'RimWorldWin64_Data/Managed') -Filter '*.dll' | ForEach-Object { '/r:' + $_.FullName })
$references += '/r:' + (Join-Path $modRoot '1.6/Assemblies/MugirlRace.dll')
$references += '/r:' + $harmonyPath
$builtProbe = Join-Path $runPath 'CorporateBugReportProbe.dll'
& $compiler /nologo /target:library /langversion:latest ('/out:' + $builtProbe) @references (Join-Path $PSScriptRoot 'CorporateBugReportRuntimeProbe.cs') (Join-Path $PSScriptRoot 'CorporatePricingChecks.cs')
if ($LASTEXITCODE -ne 0) { throw '验证驱动编译失败。' }
$dlcs = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey') | ForEach-Object { 'ludeon.rimworld.' + $_ }
$mods = @('brrainz.harmony', 'ludeon.rimworld')
if (-not $CoreOnly) { $mods += $dlcs }
$mods += @('erdelf.humanoidalienraces', 'har.mugirlrace')
$active = ($mods | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$known = ($dlcs | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
[IO.File]::WriteAllText((Join-Path $configPath 'ModsConfig.xml'), '<ModsConfigData><version>1.6.4871</version><activeMods>' + $active + '</activeMods><knownExpansions>' + $known + '</knownExpansions></ModsConfigData>')
$languageFolder = if ($Language -eq 'ChineseSimplified') { 'ChineseSimplified (简体中文)' } else { 'English' }
[IO.File]::WriteAllText((Join-Path $runPath 'expected-language.txt'), $Language)
[IO.File]::WriteAllText((Join-Path $configPath 'Prefs.xml'), '<PrefsData><langFolderName>' + $languageFolder + '</langFolderName><devMode>true</devMode><fullscreen>false</fullscreen><screenWidth>1440</screenWidth><screenHeight>1000</screenHeight></PrefsData>')
$process = $null
try {
    Copy-Item -LiteralPath $builtProbe -Destination $probePath
    $oldSteamApp = $env:SteamAppId
    $oldSteamGame = $env:SteamGameId
    try {
        $env:SteamAppId = '294100'
        $env:SteamGameId = '294100'
        $arguments = @('-quicktest', '-mugirlBugReportProbe', '-screen-width', '1440', '-screen-height', '1000', '-screen-fullscreen', '0',
            ('-savedatafolder="' + $runPath + '"'), '-logFile', ('"' + (Join-Path $runPath 'Player.log') + '"'))
        $process = Start-Process -FilePath (Join-Path $gameRoot 'RimWorldWin64.exe') -ArgumentList $arguments -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
    } finally {
        $env:SteamAppId = $oldSteamApp
        $env:SteamGameId = $oldSteamGame
    }
    [IO.File]::WriteAllText((Join-Path $runPath 'game-process-id.txt'), [string]$process.Id)
    Write-Host ('验证进程：' + $process.Id + '，输出：' + $runPath)
    $deadline = (Get-Date).AddMinutes(12)
    while (-not $process.WaitForExit(1000)) {
        if ((Get-Date) -gt $deadline) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
            throw '隔离验证超时；仅结束本次启动的进程。'
        }
    }
    $completePath = Join-Path $runPath 'bug-complete.txt'
    if (-not (Test-Path -LiteralPath $completePath)) { throw '验证未产生完成记录。' }
    $result = Get-Content -LiteralPath $completePath -Raw
    Write-Host $result
    if ($result -notmatch 'FAIL=0\b') { throw '专项验证失败，详见 bug-checks.txt。' }
} finally {
    # 只删除本次复制的单个探针文件；不接触正式 DLL、既有探针或用户配置。
    if (($null -eq $process -or $process.HasExited) -and (Test-Path -LiteralPath $probePath)) {
        $resolvedProbe = (Get-Item -LiteralPath $probePath).FullName
        $expectedProbe = [IO.Path]::GetFullPath((Join-Path $modRoot '1.6/Assemblies/CorporateBugReportProbe.dll'))
        if ($resolvedProbe -eq $expectedProbe -and (Get-FileHash -LiteralPath $probePath).Hash -eq (Get-FileHash -LiteralPath $builtProbe).Hash) {
            Remove-Item -LiteralPath $resolvedProbe
        }
    }
}
