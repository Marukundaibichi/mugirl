#Requires -Version 7.0
<#
.SYNOPSIS
    无界面启动真实的 RimWorld 1.7 两次（只有依赖 / 依赖加雪牛娘），只要雪牛娘在日志中新增任何 ERROR 或 WARN 行就失败。

.DESCRIPTION
    按黑科拓展 tools/SmokeTest.ps1 的做法（证据 E48，迁移与性能验证清单“无界面启动冒烟测试”）：

      1. 基线：Harmony 与 Humanoid Alien Races；
         候选：同样的依赖加 HAR.MugirlRace（开发目录中的副本；模组列表写入本次临时 ModsConfig.xml）。
      2. 每次启动用 %TEMP% 下一个新的临时存档目录（-savedatafolder），玩家的设置、模组列表与存档不受影响；
         -NoFileLog 让玩家的 Player.log 保持不变。参数：-Headless -QuitAfterBoot -NoCrashDialog -DisableLogStackTraces -NoFileLog。
      3. 比较游戏版本行之后的 ERROR/WARN 行（包括两侧相同的已安装模组元数据警告），
         去掉宿主固定底噪与消息为空的 WARN，把十六进制地址与数字归一后，求“候选有、基线没有”的行。

    -Headless 不创建帧渲染器、不编译 Shader、不绘制（E48）；画面、界面与 Shader 由 tools/Test-InGame.ps1 检查。
    候选启动加载 1.7/Assemblies/MugirlRace.dll；脚本不构建、不写入它。普通构建不写 1.7/Assemblies，要测试新构建先在 Source/ 中
    显式部署（在 Source/ 中启动 dotnet，Source/global.json 固定的 SDK 才会生效）：
      dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true

    游戏内运行互斥（K5）：检查 DLL 与启动 RimWorld 之前取得 %TEMP%\mugirl-ingame.lock，结束时按令牌释放；锁被占用时等待。
    锁文件格式与 tools/Test-InGame.ps1、tools/New-SaveFixture.ps1 相同（label=、pid=、token=、started= 各占一行）。
    环境变量 MUGIRL_INGAME_LOCK_TOKEN 与锁文件中的令牌一致时表示父脚本已持锁，本脚本直接运行、不释放。
    记录的进程已不存在的锁视为残留：独占打开、确认内容未变后原地改写为本脚本的锁。

    日志保存在 TMP/SmokeTest/<时间戳>/。退出码：0 通过；1 雪牛娘新增了 ERROR/WARN；2 启动失败或环境错误。

.PARAMETER QuickTest
    加载后再用 -quicktest 开局生成地图（游戏与地图组件构造、地图生成、最初的 Tick）。

.PARAMETER DevMode
    在临时存档目录写 Prefs.xml 以开发者模式启动（启用 StaticConstructorOnStartup 属性报告等开发期检查）。

.PARAMETER BaselineOnly
    只启动基线（只有依赖）。用来调试脚本或查看依赖自己的底噪，不加载雪牛娘。

.PARAMETER Dependencies
    基线与候选共同加载的依赖 packageId。

.PARAMETER GameDir
    RimWorld 安装目录。默认为包含本 Mod 的安装。

.PARAMETER TimeoutSeconds
    每次启动的上限（秒）；超时只结束本脚本启动的进程。

.PARAMETER KeepWorkDir
    保留 %TEMP% 中的临时存档目录。失败时总会保留。

.PARAMETER LogStackTraces
    保留游戏错误堆栈，用于定位首次出现或偶发的冒烟差异。默认关闭以缩减常规日志。

.PARAMETER OutputRoot
    日志目录的上级。默认 <Mod>/TMP/SmokeTest。

.PARAMETER LockTimeoutSeconds
    等待游戏内运行锁的上限（秒）；超时按环境错误退出。

.EXAMPLE
    pwsh tools/SmokeTest.ps1 -QuickTest -DevMode

.EXAMPLE
    pwsh tools/SmokeTest.ps1 -BaselineOnly -QuickTest
#>
[CmdletBinding()]
param(
    [switch] $QuickTest,
    [switch] $DevMode,
    [switch] $BaselineOnly,
    [string[]] $Dependencies = @('brrainz.harmony', 'erdelf.HumanoidAlienRaces'),
    [string] $GameDir,
    [ValidateRange(60, 7200)]
    [int] $TimeoutSeconds = 900,
    [switch] $KeepWorkDir,
    [switch] $LogStackTraces,
    [string] $OutputRoot,
    [ValidateRange(0, 86400)]
    [int] $LockTimeoutSeconds = 3600
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ModPackageId = 'HAR.MugirlRace'
$WorkMarker = '.mugirl-smoketest-work'
$BootMarker = 'RimWorld \d+\.'
$NewGameMarker = 'Initializing new game with mods'

# 宿主与原版的固定底噪（E48）以及无界面渲染器的提示；与任何模组无关。
$HostNoise = @(
    '^Resolution too small'
    '^DrawTexture \(Graphics\.cs:\d+\): Stub!'
    '^Deferred loading: \d+ textures have no pixels'
    '^Persistent allocation 0x[0-9A-Fa-f]+ .* was not freed before shutdown'
    '^Brutal Graphics\.DrawMeshNow is not supported yet'
    '^NativeAllocationRuntime'
)

$ModRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $GameDir) { $GameDir = (Resolve-Path -LiteralPath (Join-Path $ModRoot '..\..')).Path }
$GameDir = [System.IO.Path]::GetFullPath($GameDir).TrimEnd('\', '/')
if (-not $OutputRoot) { $OutputRoot = Join-Path $ModRoot 'TMP\SmokeTest' }
$GameExe = Join-Path $GameDir 'RimWorld.exe'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl17-smoke'
$Work = Join-Path $WorkRoot $Stamp
$OutDir = Join-Path $OutputRoot $Stamp
$LockPath = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl-ingame.lock'
$LockTokenVariable = 'MUGIRL_INGAME_LOCK_TOKEN'
$DeployCommand = 'dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true (run in Source/)'

class EnvironmentProblem : System.Exception {
    EnvironmentProblem([string] $message) : base($message) {}
}

function Stop-Environment([string] $Message) { throw [EnvironmentProblem]::new($Message) }

function Get-RimWorldProcess { @(Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue) }

function Test-ReparsePoint([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    return $null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

# 只删除带标记、且本身与内部都没有目录链接的目录（AGENTS.md 清理规则：不跟随链接）。
function Remove-MarkedDirectory([string] $Path, [string] $Marker) {
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    if ((Test-ReparsePoint $Path) -or -not (Test-Path -LiteralPath (Join-Path $Path $Marker))) { return $false }
    if (@(Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue).Count -gt 0) { return $false }
    Remove-Item -LiteralPath $Path -Recurse -Force
    return -not (Test-Path -LiteralPath $Path)
}

Import-Module (Join-Path $PSScriptRoot 'Mugirl.InGameLock.psm1') -Force

function Enter-InGameLock([string] $Label, [int] $WaitSeconds = 0) {
    try { return Enter-MugirlInGameLock -Label $Label -WaitSeconds $WaitSeconds -Path $LockPath -TokenVariable $LockTokenVariable }
    catch { Stop-Environment $_.Exception.Message }
}

function Exit-InGameLock($Lock) { Exit-MugirlInGameLock $Lock }
# 比较时去掉地址、数字与计时，它们每次运行都不同。
function ConvertTo-Key([string] $Line) { ($Line -replace '0x[0-9A-Fa-f]+', '0x#') -replace '\d+(\.\d+)?', '#' }

function Invoke-Boot([string] $Name, [string[]] $Mods) {
    $saveDir = Join-Path $Work "$Name-save"
    New-Item -ItemType Directory -Path $saveDir | Out-Null
    $configDir = New-Item -ItemType Directory -Path (Join-Path $saveDir 'Config')
    [System.IO.File]::WriteAllText((Join-Path $configDir.FullName 'Prefs.xml'),
        "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<PrefsData>`n  <devMode>$DevMode</devMode>`n  <volumeMaster>0</volumeMaster>`n  <volumeGame>0</volumeGame>`n  <volumeMusic>0</volumeMusic>`n  <volumeAmbient>0</volumeAmbient>`n  <volumeUI>0</volumeUI>`n</PrefsData>`n", [System.Text.UTF8Encoding]::new($false))

    # 4990 的 -mod= 拒绝工坊依赖；只在本次临时存档目录中明确指定加载列表。
    $expansionOrder = @('ludeon.rimworld.royalty', 'ludeon.rimworld.ideology', 'ludeon.rimworld.biotech', 'ludeon.rimworld.anomaly', 'ludeon.rimworld.odyssey')
    $installed = @(foreach ($folder in Get-ChildItem -LiteralPath (Join-Path $GameDir 'Data') -Directory) {
        $aboutPath = Join-Path $folder.FullName 'About/About.xml'
        if (Test-Path -LiteralPath $aboutPath) {
            $id = ([xml](Get-Content -LiteralPath $aboutPath -Raw -Encoding utf8)).ModMetaData.packageId
            if ($id -and $id -ne 'ludeon.rimworld') { $id.ToLowerInvariant() }
        }
    })
    $expansions = @($expansionOrder | Where-Object { $installed -contains $_ }) + @($installed | Where-Object { $expansionOrder -notcontains $_ })
    $active = @($Mods | Where-Object { $_ -ieq 'brrainz.harmony' }) + @('ludeon.rimworld') + $expansions + @($Mods | Where-Object { $_ -ine 'brrainz.harmony' })
    $activeXml = ($active | Select-Object -Unique | ForEach-Object { '    <li>' + [Security.SecurityElement]::Escape($_.ToLowerInvariant()) + '</li>' }) -join "`n"
    $knownXml = ($expansions | ForEach-Object { '    <li>' + [Security.SecurityElement]::Escape($_) + '</li>' }) -join "`n"
    $gameVersion = [Security.SecurityElement]::Escape((Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -TotalCount 1).Trim())
    $modsConfigPath = Join-Path $configDir.FullName 'ModsConfig.xml'
    [IO.File]::WriteAllText($modsConfigPath, @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$gameVersion</version>
  <activeMods>
$activeXml
  </activeMods>
  <knownExpansions>
$knownXml
  </knownExpansions>
</ModsConfigData>
"@, [Text.UTF8Encoding]::new($false))

    $log = Join-Path $OutDir "$Name.log"
    $arguments = @('-Headless', '-QuitAfterBoot', '-NoCrashDialog', '-NoFileLog', "-savedatafolder=$saveDir")
    if (-not $LogStackTraces) { $arguments += '-DisableLogStackTraces' }
    if ($QuickTest) { $arguments += '-quicktest' }
    $argumentLine = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '

    Write-Host "[$Name] RimWorld with $($Mods -join ', ')$(if ($QuickTest) { ' + quick-test map' })$(if ($DevMode) { ' (developer mode)' })"
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $GameExe -ArgumentList $argumentLine -WorkingDirectory $GameDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError (Join-Path $OutDir "$Name.stderr.log")
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            [void]$process.WaitForExit(30000)
            Stop-Environment "[$Name] RimWorld did not quit within $TimeoutSeconds s; see $log"
        }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
    }
    Write-Host ("[$Name] finished after {0:N0} s (exit code {1})" -f $clock.Elapsed.TotalSeconds, $process.ExitCode)
    Copy-Item -LiteralPath $modsConfigPath -Destination (Join-Path $OutDir "$Name.ModsConfig.xml")
    if ($process.ExitCode -ne 0) { Stop-Environment "[$Name] RimWorld exited with code $($process.ExitCode); see $log" }

    $lines = @(Get-Content -LiteralPath $log -Encoding utf8)
    if (-not ($lines | Where-Object { $_ -match "INFO $BootMarker" })) { Stop-Environment "[$Name] the log never reached game startup; see $log" }
    if ($QuickTest -and -not ($lines | Where-Object { $_ -match "INFO $NewGameMarker" })) { Stop-Environment "[$Name] the quick-test game never started; see $log" }
    $loadedConfig = [xml](Get-Content -LiteralPath $modsConfigPath -Raw -Encoding utf8)
    foreach ($mod in $Mods) {
        if (@($loadedConfig.ModsConfigData.activeMods.li) -notcontains $mod) { Stop-Environment "[$Name] $mod was removed from the active mod list" }
        if ($QuickTest -and -not ($lines | Where-Object { $_ -match "^\s+-\s+$([regex]::Escape($mod))\s*$" })) { Stop-Environment "[$Name] $mod is absent from the new game's actual mod list" }
    }

    $messages = [System.Collections.Generic.List[string]]::new()
    $started = $false
    foreach ($line in $lines) {
        if (-not $started) { $started = $line -match "^\S+\s+INFO $BootMarker"; continue }
        if ($line -notmatch '^\S+\s+(ERROR|WARN)(?:\s+(.*))?$') { continue }
        $level = $Matches[1]
        $text = "$($Matches[2])".Trim()
        # 消息为空的 WARN 随机出现在任一侧（E48），不参与比较。
        if ($text.Length -eq 0) { continue }
        if ($HostNoise | Where-Object { $text -match $_ }) { continue }
        $messages.Add("$level $text")
    }
    return , $messages
}

$exitCode = 2
$lock = $null
try {
    if (-not (Test-Path -LiteralPath $GameExe)) { Stop-Environment "RimWorld.exe not found in $GameDir" }
    $dll = Join-Path $ModRoot '1.7\Assemblies\MugirlRace.dll'
    $missingDll = "1.7/Assemblies/MugirlRace.dll does not exist; deploy a build first ($DeployCommand) or use -BaselineOnly"
    # 等锁之前先排除注定失败的情况；DLL 的哈希与新旧在持锁后记录，持锁者可能在等待期间重新部署。
    if (-not $BaselineOnly -and -not (Test-Path -LiteralPath $dll)) { Stop-Environment $missingDll }

    $lock = Enter-InGameLock -Label "SmokeTest $Stamp" -WaitSeconds $LockTimeoutSeconds
    $running = @(Get-RimWorldProcess)
    if ($running.Count -gt 0) { Stop-Environment "RimWorld is running (PID $($running.Id -join ', ')); close it first" }

    if (-not $BaselineOnly) {
        if (-not (Test-Path -LiteralPath $dll)) { Stop-Environment $missingDll }
        $dllTime = (Get-Item -LiteralPath $dll).LastWriteTimeUtc
        $newer = Get-ChildItem -LiteralPath (Join-Path $ModRoot 'Source\MugirlRace') -Recurse -File -Include '*.cs', '*.csproj', 'BannedSymbols.txt' |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.LastWriteTimeUtc -gt $dllTime } | Select-Object -First 1
        Write-Host "candidate DLL: $dll (SHA-256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash))"
        if ($newer) { Write-Warning "1.7/Assemblies/MugirlRace.dll is older than $($newer.FullName); the candidate run tests the deployed build (deploy a new one with: $DeployCommand)" }
    }

    New-Item -ItemType Directory -Force -Path $WorkRoot, $OutDir | Out-Null
    New-Item -ItemType Directory -Path $Work | Out-Null
    New-Item -ItemType File -Path (Join-Path $Work $WorkMarker) | Out-Null

    $baseline = Invoke-Boot 'baseline' $Dependencies
    Write-Host ''
    Write-Host "Baseline (dependencies only): $($baseline.Count) error/warning line(s) after game startup."
    foreach ($message in $baseline | Select-Object -Unique) { Write-Host "  [deps] $message" -ForegroundColor DarkGray }

    if ($BaselineOnly) {
        $exitCode = 0
    }
    else {
        $candidate = Invoke-Boot 'candidate' ($Dependencies + $ModPackageId)
        $baselineKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($message in $baseline) { [void]$baselineKeys.Add((ConvertTo-Key $message)) }
        $introduced = @($candidate | Where-Object { -not $baselineKeys.Contains((ConvertTo-Key $_)) })
        Write-Host ''
        if ($introduced.Count -eq 0) {
            Write-Host "PASS: $ModPackageId added no errors or warnings to the log." -ForegroundColor Green
            $exitCode = 0
        }
        else {
            Write-Host "FAIL: $ModPackageId added $($introduced.Count) error/warning line(s):" -ForegroundColor Red
            foreach ($message in $introduced) { Write-Host "  $message" -ForegroundColor Red }
            [System.IO.File]::WriteAllLines((Join-Path $OutDir 'introduced.txt'), [string[]]$introduced, [System.Text.UTF8Encoding]::new($false))
            $exitCode = 1
        }
    }
}
catch [EnvironmentProblem] {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    $exitCode = 2
}
finally {
    if (Test-Path -LiteralPath $Work) {
        if ($KeepWorkDir -or $exitCode -ne 0) { Write-Host "temporary save-data folders kept in $Work" }
        elseif (-not (Remove-MarkedDirectory $Work $WorkMarker)) { Write-Host "WARNING: could not remove $Work" -ForegroundColor Yellow }
    }
    if (Test-Path -LiteralPath $OutDir) { Write-Host "logs: $OutDir" }
    $after = @(Get-RimWorldProcess)
    if ($after.Count -gt 0) { Write-Host "WARNING: a RimWorld process is still running (PID $($after.Id -join ', '))" -ForegroundColor Yellow }
    Exit-InGameLock $lock
}

exit $exitCode
