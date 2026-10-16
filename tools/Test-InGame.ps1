#Requires -Version 7.0
<#
.SYNOPSIS
    在真实 RimWorld 1.7（隐藏窗口）中运行雪牛娘的游戏内验证套件，收集报告、标准输出与截图；也能读入存档夹具、驱动临时旧构建。

.DESCRIPTION
    做法见 1.7迁移文档/迁移与性能验证清单.md 的“游戏内自动验证”（证据 E36、E40）：

      1. 取得游戏内运行锁 %TEMP%\mugirl-ingame.lock（同一时间只运行一个测试游戏；已被占用时等待）。
         拒绝在 RimWorld 运行时执行（DLL 被锁定、两份游戏争用存档目录）。
      2. 把 Source/ 复制到 %TEMP%，在副本中构建测试模组 Source/Tests/MugirlRace.GameValidation（以及 -BuildMod 时的 Mod 本身）。
         脚本从不构建进 1.7/Assemblies；只有 -BuildMod -Deploy 时由 Mod 工程自己的部署步骤（-p:DeployToMod=true，K3）写入那里。
      3. 在 <游戏>/Mods 中建立带标记文件的临时测试模组 __MugirlRace_GameValidation_<随机>，结束时（finally）删除。
         只删除带标记的真实目录，遇到目录链接（Junction/符号链接）就停止，不跟随。
      4. 每次启动（boot）使用 %TEMP% 中的临时存档目录：Config/Prefs.xml（开发者模式、静音、后台运行、读档不暂停）、可选的
         Config/ModsConfig.xml（-mod= 总会启用全部 DLC，无 DLC 组合必须用 ModsConfig.xml）和测试参数文件 mugirl-gamevalidation.json。
      5. 启动 RimWorld.exe -NoWindow（或 -Headless）-NoFileLog -NoCrashDialog [-quicktest] -mugirlvalidate -savedatafolder=<临时目录>，
         标准输出重定向到文件；超时只结束本脚本启动的进程。
      6. 把报告、标准输出、配置、截图与套件结果（artifacts/）复制到 TMP/InGameValidation/<时间戳>/，分析渲染截图，写 summary.json。

    启动方式：
      - 默认：一次启动，-quicktest 开局，运行 -Suite 选择的套件。
      - -Fixture <id|all|control>：对每个存档夹具（DevData/Evidence/SaveFixtures/<id>/）启动两次。第 1 次把夹具解压到临时存档目录，
        从主菜单读入（entry load:<存档>），运行 upgrade 套件并写重存档；第 2 次在新进程中读入该重存档（存读档往返，升级器应保持不动）。
        种类（清单 kind）：P/E/R 直接读入夹具；X-settings 读入 base 夹具，并把夹具目录 config/ 中的设置文件放进临时 Config/
        （文件名中的 {folder} 换成本 Mod 的目录名）；X-alias 读入 base 夹具，并把夹具目录 mod/ 复制成临时依赖 Mod（带标记，结束时删除）。
        control 是对照组：先用 sweep 套件让被测构建自己写一份存档（-ControlTicks），再在新进程中读入它，升级器必须不动、差异必须为空。
        all = 清单标为 feasible 的全部夹具加上 control。-Fixture 时默认语言为 ChineseSimplified（本 Mod 的 DefInjected 主要在中文）。
      - -TargetModDir <目录>：被测构建换成 Mods 下的一个临时旧构建（tools/New-SaveFixture.ps1 布置，packageId mugirl.legacy.<id>），
        加载顺序为 Harmony、HAR、该构建、测试模组；测试模组不引用 MugirlRace 编译（与 -Plumbing 相同），HAR.MugirlRace 不加载。

    RimWorld 加载的是 Mods 目录中的 HAR.MugirlRace（开发目录；同 packageId 的创意工坊副本会被加上 _steam 后缀），
    所以被测 DLL 必须位于 1.7/Assemblies。被测 DLL 的来源：
      - 默认：1.7/Assemblies/MugirlRace.dll（必须已存在）；
      - -ModDll <路径>：指定的 DLL，必须与 1.7/Assemblies 中的相同（脚本不手工复制 DLL；部署新构建用 -BuildMod -Deploy）；
      - -BuildMod：在 %TEMP% 的源码副本中构建 Release（输出位置向 MSBuild 查询 TargetPath）；与已部署的不同时需要 -Deploy，
        由工程的部署步骤（-p:DeployToMod=true -p:MugirlDeployDir=<本 Mod>/1.7/Assemblies）复制 DLL 与 PDB。
    测试模组总是按路径引用被测 DLL 编译（Private=false），编译期与运行期一致。

    -Plumbing 只加载 Harmony 与测试模组，运行 plumbing 套件（原版内容上的框架自检），不加载雪牛娘，不需要 1.7 DLL，
    测试模组也不引用 MugirlRace 编译（-p:MugirlPlumbingOnly=true），Mod 源码编译不过时同样可用。

    退出码：0 全部通过；1 游戏运行了但有失败（FAIL 行、没有 DONE、超时、截图分析失败）；2 构建或环境错误。

.PARAMETER Suite
    要运行的套件：套件名、default 或 all，可用逗号分隔。默认 default（-Plumbing 时为 plumbing，-Fixture 时为 upgrade）。

.PARAMETER Plumbing
    只用原版内容调试脚本与测试框架，不加载 HAR.MugirlRace。

.PARAMETER Fixture
    存档夹具标识（DevData/Evidence/SaveFixtures 下的目录名），可用逗号分隔；control 表示本次生成的对照组；
    all 表示清单标为可用（feasible）的全部夹具加上 control。

.PARAMETER ControlTicks
    对照组播种时运行的 Tick 数。

.PARAMETER LegacyCorpusDir
    验证器的旧版存档语料目录（只读）。默认 <Mod>/Source/Tests/MugirlRace.Validation/Baselines/legacy。

.PARAMETER FixtureRoot
    夹具目录。默认 <Mod>/DevData/Evidence/SaveFixtures。

.PARAMETER TokenSet
    旧名单文件（每行一个名字）：upgrade 套件在重存档中搜索它们。不给出时由套件从升级清单推导。

.PARAMETER TargetModDir
    Mods 下的临时旧构建目录（含 About/About.xml 与标记文件 .mugirl-gamevalidation-tmp），代替 HAR.MugirlRace 作为被测构建。

.PARAMETER SuiteOption
    传给套件的参数，形如 key=value（例如 sweep.ticks=120000），可重复或用逗号分隔。

.PARAMETER CollectSaves
    把临时存档目录 Saves/ 中的存档复制到结果目录（夹具制作用）。

.PARAMETER ModDll
    被测的 MugirlRace.dll，必须与 1.7/Assemblies 中的相同（用来确认运行的就是这份 DLL）。

.PARAMETER BuildMod
    在 %TEMP% 的源码副本中构建 Mod（Release）并以它为被测 DLL。需要 -Deploy。

.PARAMETER Deploy
    与 -BuildMod 一起：由 Mod 工程的部署步骤把构建出的 DLL 与 PDB 写进 1.7/Assemblies（K3）。不给出时脚本不会写入 1.7/Assemblies。

.PARAMETER Config
    DLC 组合。AllDlc 用 -mod=；其余写 Config/ModsConfig.xml（-mod= 会启用全部 DLC）。-Fixture 时默认取夹具清单中的组合。

.PARAMETER Language
    游戏语言（Prefs.xml 的 langFolderName）。默认 English；-Fixture 时默认 ChineseSimplified。

.PARAMETER Headless
    用 -Headless 代替 -NoWindow：不渲染、不运行界面，截图与界面套件会跳过（E48）。

.PARAMETER NoScreenshots
    不截图（截图前后的检查照常运行）。

.PARAMETER TimeoutSeconds
    每次启动的游戏内会话实时上限（秒）。进程上限再加上 -LoadAllowanceSeconds。

.PARAMETER LoadAllowanceSeconds
    启动与加载内容所需的额外时间（秒），只计入进程上限。

.PARAMETER LockWaitSeconds
    游戏内运行锁被占用时最多等待的秒数。

.PARAMETER PrepareOnly
    构建、布置测试模组并写好第一次启动的配置，打印启动命令后清理退出，不启动游戏。用来调试脚本本身。

.PARAMETER KeepWorkDir
    保留 %TEMP% 中的工作目录（源码副本、临时存档目录与存档）。失败时总会保留。

.PARAMETER GameDir
    RimWorld 安装目录。默认为包含本 Mod 的安装。

.PARAMETER WorkshopDir
    创意工坊内容目录（workshop/content/294100）。默认从游戏目录推导。

.PARAMETER OutputRoot
    结果目录的上级。默认 <Mod>/TMP/InGameValidation（TMP 已被 .gitignore 排除）。

.EXAMPLE
    pwsh tools/Test-InGame.ps1 -Plumbing

.EXAMPLE
    pwsh tools/Test-InGame.ps1 -BuildMod -Deploy -Suite default

.EXAMPLE
    pwsh tools/Test-InGame.ps1 -Fixture all

.EXAMPLE
    pwsh tools/Test-InGame.ps1 -Suite startup,spawn -Config NoBiotech -Language ChineseSimplified
#>
[CmdletBinding()]
param(
    [string[]] $Suite = @(),
    [switch] $Plumbing,
    [string[]] $Fixture = @(),
    [ValidateRange(2500, 600000)]
    [int] $ControlTicks = 30000,
    [string] $FixtureRoot,
    [string] $LegacyCorpusDir,
    [string] $TokenSet,
    [string] $TargetModDir,
    [string[]] $SuiteOption = @(),
    [switch] $CollectSaves,
    [string] $ModDll,
    [switch] $BuildMod,
    [switch] $Deploy,
    [ValidateSet('AllDlc', 'CoreOnly', 'NoRoyalty', 'NoIdeology', 'NoBiotech', 'NoAnomaly', 'NoOdyssey')]
    [string] $Config,
    [ValidateSet('English', 'ChineseSimplified')]
    [string] $Language = 'English',
    [switch] $Headless,
    [switch] $NoScreenshots,
    [ValidateRange(60, 7200)]
    [int] $TimeoutSeconds = 900,
    [ValidateRange(60, 3600)]
    [int] $LoadAllowanceSeconds = 600,
    [ValidateRange(0, 86400)]
    [int] $LockWaitSeconds = 3600,
    [switch] $PrepareOnly,
    [switch] $KeepWorkDir,
    [string] $GameDir,
    [string] $WorkshopDir,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'

# ---------------------------------------------------------------------------------------------------------------
# 常量与路径
# ---------------------------------------------------------------------------------------------------------------

$ModPackageId = 'HAR.MugirlRace'
$TestPackageId = 'MugirlRace.GameValidation'
$TestFolderPrefix = '__MugirlRace_GameValidation_'
$TestMarker = '.mugirl-gamevalidation-tmp'
$WorkMarker = '.mugirl-gamevalidation-work'
$ReportPrefix = '[Mugirl.GameValidation]'
$OptionsFileName = 'mugirl-gamevalidation.json'
$RenderShotPattern = 'MugirlValidation-render-*.png'
$FixtureSavePrefix = 'MugirlFixture-'
$ControlFixtureId = 'control'
$AliasFolderPrefix = '__MugirlXAlias_'
$HarmonyWorkshopId = '2009463077'
$HarWorkshopId = '839005762'
$HarPackageId = 'erdelf.HumanoidAlienRaces'
$ExpansionOrder = @('ludeon.rimworld.royalty', 'ludeon.rimworld.ideology', 'ludeon.rimworld.biotech', 'ludeon.rimworld.anomaly', 'ludeon.rimworld.odyssey')
$LanguageFolders = @{ English = 'English'; ChineseSimplified = 'ChineseSimplified (简体中文)' }
# K5：所有游戏内运行（本脚本、tools/New-SaveFixture.ps1 与并行的代理）共用的锁。持锁的父脚本把令牌放进环境变量，子脚本据此不再等待。
$LockPath = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl-ingame.lock'
$LockTokenVariable = 'MUGIRL_INGAME_LOCK_TOKEN'

$ModRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $GameDir) { $GameDir = (Resolve-Path -LiteralPath (Join-Path $ModRoot '..\..')).Path }
$GameDir = [System.IO.Path]::GetFullPath($GameDir).TrimEnd('\', '/')
if (-not $WorkshopDir) { $WorkshopDir = Join-Path $GameDir '..\..\workshop\content\294100' }
$WorkshopDir = [System.IO.Path]::GetFullPath($WorkshopDir).TrimEnd('\', '/')
if (-not $OutputRoot) { $OutputRoot = Join-Path $ModRoot 'TMP\InGameValidation' }
if (-not $FixtureRoot) { $FixtureRoot = Join-Path $ModRoot 'DevData\Evidence\SaveFixtures' }
if (-not $LegacyCorpusDir) { $LegacyCorpusDir = Join-Path $ModRoot 'Source\Tests\MugirlRace.Validation\Baselines\legacy' }
if ($Fixture.Count -gt 0 -and -not $PSBoundParameters.ContainsKey('Language')) { $Language = 'ChineseSimplified' }
$ModsDir = Join-Path $GameDir 'Mods'
$GameExe = Join-Path $GameDir 'RimWorld.exe'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl17-ingame'
$Work = Join-Path $WorkRoot $Stamp
$OutDir = Join-Path $OutputRoot $Stamp
$DeployedDll = Join-Path $ModRoot '1.7\Assemblies\MugirlRace.dll'

class EnvironmentProblem : System.Exception {
    EnvironmentProblem([string] $message) : base($message) {}
}

function Stop-Environment([string] $Message) { throw [EnvironmentProblem]::new($Message) }

# ---------------------------------------------------------------------------------------------------------------
# 工具函数
# ---------------------------------------------------------------------------------------------------------------

# 本安装（$GameExe）的 RimWorld 进程：它们锁住被测 DLL、与本脚本争用 Mods 目录。其他游戏目录（别的项目复制的游戏树）中的进程不相干，不算。
# 可执行文件路径取自 Win32_Process（刚启动或正在退出的进程读 Process.Path 会失败）；读不到路径的进程按本安装的处理。
function Get-RimWorldProcess {
    $own = [System.IO.Path]::GetFullPath($GameExe)
    @(Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe' OR Name = 'RimWorldWin64.exe'" -ErrorAction SilentlyContinue | Where-Object {
            -not $_.ExecutablePath -or [string]::Equals([System.IO.Path]::GetFullPath($_.ExecutablePath), $own, [System.StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object { [pscustomobject]@{ Id = $_.ProcessId; Path = $_.ExecutablePath } })
}

function Test-ReparsePoint([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    return $null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Assert-Under([string] $Path, [string] $Parent) {
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        Stop-Environment "refusing to touch ${full}: it is not under $root"
    }
    return $full
}

# 建立带标记文件的真实目录；已存在就拒绝。
function New-MarkedDirectory([string] $Path, [string] $Marker) {
    if (Test-Path -LiteralPath $Path) { Stop-Environment "$Path already exists" }
    New-Item -ItemType Directory -Path $Path | Out-Null
    if (Test-ReparsePoint $Path) { Stop-Environment "$Path became a reparse point" }
    New-Item -ItemType File -Path (Join-Path $Path $Marker) | Out-Null
}

# 删除带标记的目录。目录本身或其中任何一项是链接时不删除（不跟随链接，AGENTS.md 清理规则），返回 $false。
function Remove-MarkedDirectory([string] $Path, [string] $Marker) {
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    if (Test-ReparsePoint $Path) {
        Write-Warning "not removing ${Path}: it is a junction or symbolic link"
        return $false
    }
    if (-not (Test-Path -LiteralPath (Join-Path $Path $Marker))) {
        Write-Warning "not removing ${Path}: the marker $Marker is missing"
        return $false
    }
    $links = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue)
    if ($links.Count -gt 0) {
        Write-Warning "not removing ${Path}: it contains links ($($links[0].FullName))"
        return $false
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
    return -not (Test-Path -LiteralPath $Path)
}

Import-Module (Join-Path $PSScriptRoot 'Mugirl.InGameLock.psm1') -Force

function Enter-InGameLock([string] $Label, [int] $WaitSeconds = 0) {
    try { return Enter-MugirlInGameLock -Label $Label -WaitSeconds $WaitSeconds -Path $LockPath -TokenVariable $LockTokenVariable }
    catch { Stop-Environment $_.Exception.Message }
}

function Exit-InGameLock($Lock) { Exit-MugirlInGameLock $Lock }

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

# MSBuild 属性值：正斜杠、无结尾斜杠（结尾的反斜杠会转义命令行中的引号）。
function ConvertTo-MSBuildPath([string] $Path) { $Path.TrimEnd('\', '/').Replace('\', '/') }

function Get-BuildProperties { @("-p:RimWorldDir=$(ConvertTo-MSBuildPath $GameDir)", "-p:WorkshopDir=$(ConvertTo-MSBuildPath $WorkshopDir)") }

function Invoke-Build([string] $Project, [string[]] $Properties, [string] $Name) {
    $log = Join-Path $Work "build-$Name.log"
    $arguments = @('build', $Project, '-c', 'Release', '-nologo', '-v:minimal') + (Get-BuildProperties) + $Properties
    Write-Host "[build] $Name"
    & dotnet @arguments *>&1 | Out-File -LiteralPath $log -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        Select-String -LiteralPath $log -Pattern ': error ' | Select-Object -First 30 | ForEach-Object { Write-Host "  $($_.Line.Trim())" -ForegroundColor Red }
        Stop-Environment "building $Name failed (exit $LASTEXITCODE); see $log"
    }
}

# 构建输出的位置由工程决定（K3：Mod 默认输出到自己的 bin/，只有显式部署才写 1.7/Assemblies），向 MSBuild 询问而不是猜测。
function Get-TargetPath([string] $Project, [string[]] $Properties) {
    $arguments = @('msbuild', $Project, '-nologo', '-getProperty:TargetPath', '-p:Configuration=Release') + (Get-BuildProperties) + $Properties
    $path = (& dotnet @arguments | Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0 -or -not $path) { Stop-Environment "cannot query the output path of $Project" }
    return $path.Trim()
}

function Write-Utf8([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function ConvertTo-XmlText([string] $Text) { [System.Security.SecurityElement]::Escape($Text) }

function Get-InstalledExpansions {
    $ids = foreach ($about in Get-ChildItem -LiteralPath (Join-Path $GameDir 'Data') -Directory | ForEach-Object { Join-Path $_.FullName 'About\About.xml' }) {
        if (-not (Test-Path -LiteralPath $about)) { continue }
        $id = ([xml](Get-Content -LiteralPath $about -Raw -Encoding utf8)).ModMetaData.packageId
        if ($id) { $id.ToLowerInvariant() }
    }
    @($ExpansionOrder | Where-Object { $ids -contains $_ }) + @($ids | Where-Object { $_ -ne 'ludeon.rimworld' -and $ExpansionOrder -notcontains $_ })
}

# 逐行读出报告文件的新内容（游戏每写一行就打开关闭一次文件，这里共享读取）。
function Read-NewReportLines([string] $Path, [ref] $Offset) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($stream.Length -le $Offset.Value) { return @() }
        [void]$stream.Seek($Offset.Value, 'Begin')
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $complete = $text.LastIndexOf("`n")
        if ($complete -lt 0) { return @() }
        $Offset.Value += [System.Text.Encoding]::UTF8.GetByteCount($text.Substring(0, $complete + 1))
        return @($text.Substring(0, $complete) -split "`n" | ForEach-Object { $_.TrimEnd("`r") })
    }
    finally { $stream.Dispose() }
}

function Write-ReportLine([string] $Line) {
    $body = if ($Line.StartsWith($ReportPrefix)) { $Line.Substring($ReportPrefix.Length).Trim() } else { $Line }
    $color = switch -Regex ($body) {
        '^FAIL ' { 'Red'; break }
        '^ok ' { 'Green'; break }
        '^skip ' { 'DarkYellow'; break }
        '^DONE ' { 'Cyan'; break }
        default { 'Gray' }
    }
    Write-Host "  $body" -ForegroundColor $color
}

# 渲染截图分析：四个朝向的中央区域应两两不同，且没有大片品红（引擎的回退 Shader，E36）。
function Test-RenderScreenshots([string] $Folder) {
    $shots = @(Get-ChildItem -LiteralPath $Folder -Filter $RenderShotPattern -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if ($shots.Count -eq 0) { return [pscustomobject]@{ Ran = $false; Passed = $true; Lines = @('no render screenshots') } }
    try { Add-Type -AssemblyName System.Drawing }
    catch { return [pscustomobject]@{ Ran = $false; Passed = $true; Lines = @("System.Drawing is unavailable ($($_.Exception.Message)); screenshots not analysed") } }

    $lines = [System.Collections.Generic.List[string]]::new()
    $passed = $true
    $crops = @{}
    foreach ($shot in $shots) {
        $bitmap = [System.Drawing.Bitmap]::new($shot.FullName)
        try {
            $side = [Math]::Max(32, [int]([Math]::Min($bitmap.Width, $bitmap.Height) / 3))
            $rect = [System.Drawing.Rectangle]::new([int](($bitmap.Width - $side) / 2), [int](($bitmap.Height - $side) / 2), $side, $side)
            $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $bytes = [byte[]]::new($data.Stride * $side)
                [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
            }
            finally { $bitmap.UnlockBits($data) }
            $magenta = 0
            for ($i = 0; $i -lt $bytes.Length; $i += 4) {
                if ($bytes[$i + 2] -gt 200 -and $bytes[$i] -gt 200 -and $bytes[$i + 1] -lt 64) { $magenta++ }
            }
            $fraction = $magenta / ($side * $side)
            $crops[$shot.Name] = @{ Bytes = $bytes; Side = $side; Stride = $data.Stride }
            $lines.Add(("{0}: {1}x{2}, centre {3}px, magenta {4:P1}" -f $shot.Name, $bitmap.Width, $bitmap.Height, $side, $fraction))
            if ($fraction -gt 0.02) { $passed = $false; $lines.Add("FAIL $($shot.Name): magenta pixels in the centre (fallback shader?)") }
        }
        finally { $bitmap.Dispose() }
    }

    $names = @($crops.Keys | Sort-Object)
    for ($a = 0; $a -lt $names.Count; $a++) {
        for ($b = $a + 1; $b -lt $names.Count; $b++) {
            $x = $crops[$names[$a]]; $y = $crops[$names[$b]]
            if ($x.Side -ne $y.Side) { continue }
            $changed = 0
            for ($i = 0; $i -lt $x.Bytes.Length; $i += 4) {
                $delta = [Math]::Abs([int]$x.Bytes[$i] - $y.Bytes[$i]) + [Math]::Abs([int]$x.Bytes[$i + 1] - $y.Bytes[$i + 1]) + [Math]::Abs([int]$x.Bytes[$i + 2] - $y.Bytes[$i + 2])
                if ($delta -gt 48) { $changed++ }
            }
            $lines.Add("$($names[$a]) vs $($names[$b]): $changed changed pixels")
            if ($changed -lt 100) { $passed = $false; $lines.Add("FAIL $($names[$a]) and $($names[$b]) look the same: the pawn did not turn or was not drawn") }
        }
    }
    return [pscustomobject]@{ Ran = $true; Passed = $passed; Lines = $lines.ToArray() }
}

function Get-OptionalProperty($Object, [string] $Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $null
}

# 夹具清单（DevData/Evidence/SaveFixtures/<id>/manifest.json）。all 选择清单标为 feasible 的全部夹具与对照组 control。
# X-settings 与 X-alias 夹具读入清单 base 指向的夹具的存档，另外带 config/ 设置文件或 mod/ 依赖 Mod。
function Resolve-Fixtures([string[]] $Requested) {
    $ids = @($Requested | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if (-not (Test-Path -LiteralPath $FixtureRoot)) { Stop-Environment "the fixture folder $FixtureRoot does not exist" }
    $all = @(Get-ChildItem -LiteralPath $FixtureRoot -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json') } | Sort-Object Name)
    $names = if ($ids -contains 'all') {
        @($all | Where-Object { (Get-Content -LiteralPath (Join-Path $_.FullName 'manifest.json') -Raw -Encoding utf8 | ConvertFrom-Json).feasible -eq $true } |
            ForEach-Object Name) + @($ControlFixtureId)
    }
    else { $ids }
    foreach ($id in ($names | Select-Object -Unique)) {
        if ($id -eq $ControlFixtureId) {
            [pscustomobject]@{ Id = $id; Kind = 'C'; Config = 'AllDlc'; Folder = $null; ManifestPath = $null; VariantManifestPath = $null; Manifest = $null; Archive = $null; Save = $null
                OfflineReport = $null; GameVersion = $null; ForeignTokens = @(); LogAllowlist = @(); ConfigFiles = $null; AliasMod = $null; AliasPackageId = $null }
            continue
        }
        $folder = $all | Where-Object Name -eq $id
        if (-not $folder) { Stop-Environment "no fixture $id in $FixtureRoot (known: $(($all.Name) -join ', '), $ControlFixtureId)" }
        $manifestPath = Join-Path $folder.FullName 'manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ($manifest.feasible -ne $true) { Stop-Environment "fixture $id is not feasible: $((Get-OptionalProperty $manifest 'gate').verdict)" }
        $kind = if (Get-OptionalProperty $manifest 'kind') { $manifest.kind } else { 'P' }
        if ($kind -notin @('P', 'E', 'R', 'X-settings', 'X-alias')) { Stop-Environment "fixture ${id}: unknown kind $kind" }

        # 存档：本夹具的 save，或 base 夹具的 save（X-*）。
        $saveManifest = $manifest
        $saveFolder = $folder.FullName
        $saveManifestPath = $manifestPath
        if ($kind -like 'X-*') {
            $base = Get-OptionalProperty $manifest 'base'
            $baseFolder = $all | Where-Object Name -eq $base
            if (-not $baseFolder) { Stop-Environment "fixture ${id}: its base fixture '$base' does not exist" }
            $saveFolder = $baseFolder.FullName
            $saveManifestPath = Join-Path $saveFolder 'manifest.json'
            $saveManifest = Get-Content -LiteralPath $saveManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
        }
        $archive = Join-Path $saveFolder $saveManifest.save.file
        if (-not (Test-Path -LiteralPath $archive)) { Stop-Environment "fixture ${id}: $archive is missing" }
        $hash = (Get-Sha256 $archive).ToLowerInvariant()
        if ($hash -ne $saveManifest.save.sha256Gz) { Stop-Environment "fixture ${id}: $archive has SHA-256 $hash, the manifest says $($saveManifest.save.sha256Gz)" }
        $offline = Join-Path $saveFolder 'offline-report.json'
        $configFiles = if ($kind -eq 'X-settings') { Join-Path $folder.FullName 'config' } else { $null }
        if ($configFiles -and -not (Test-Path -LiteralPath $configFiles)) { Stop-Environment "fixture ${id}: no config/ folder" }
        $aliasMod = if ($kind -eq 'X-alias') { Join-Path $folder.FullName 'mod' } else { $null }
        if ($aliasMod -and -not (Test-Path -LiteralPath (Join-Path $aliasMod 'About\About.xml'))) { Stop-Environment "fixture ${id}: no mod/About/About.xml" }
        [pscustomobject]@{
            Id = $id; Kind = $kind; Folder = $folder.FullName; ManifestPath = $saveManifestPath; VariantManifestPath = $(if ($kind -like 'X-*') { $manifestPath } else { $null })
            Manifest = $manifest; Archive = $archive; Save = $saveManifest.save
            Config = if (Get-OptionalProperty $saveManifest 'config') { $saveManifest.config } else { 'AllDlc' }
            OfflineReport = if (Test-Path -LiteralPath $offline) { $offline } else { $null }
            GameVersion = (Get-OptionalProperty (Get-OptionalProperty $saveManifest 'game') 'version')
            ForeignTokens = @(Get-OptionalProperty $manifest 'foreignTokens' | Where-Object { $_ }); LogAllowlist = @(Get-OptionalProperty $manifest 'logAllowlist' | Where-Object { $_ })
            ConfigFiles = $configFiles; AliasMod = $aliasMod
            AliasPackageId = if ($aliasMod) { ([xml](Get-Content -LiteralPath (Join-Path $aliasMod 'About\About.xml') -Raw -Encoding utf8)).ModMetaData.packageId } else { $null }
        }
    }
}

# 启动计划的一项：依次合并几张表，后面的表覆盖前面的同名键。
function New-BootPlan {
    $merged = [ordered]@{}
    foreach ($part in $args) { foreach ($key in $part.Keys) { $merged[$key] = $part[$key] } }
    return [pscustomobject]$merged
}

function Expand-Fixture($FixtureInfo, [string] $SavesDir, [string] $SaveName) {
    New-Item -ItemType Directory -Force -Path $SavesDir | Out-Null
    $target = Join-Path $SavesDir "$SaveName.rws"
    $archive = [System.IO.File]::OpenRead($FixtureInfo.Archive)
    try {
        $gzip = [System.IO.Compression.GZipStream]::new($archive, [System.IO.Compression.CompressionMode]::Decompress)
        $output = [System.IO.File]::Create($target)
        try { $gzip.CopyTo($output) }
        finally { $output.Dispose(); $gzip.Dispose() }
    }
    finally { $archive.Dispose() }
    $hash = (Get-Sha256 $target).ToLowerInvariant()
    if ($FixtureInfo.Save.sha256 -and $hash -ne $FixtureInfo.Save.sha256) {
        Stop-Environment "fixture $($FixtureInfo.Id): the expanded save has SHA-256 $hash, the manifest says $($FixtureInfo.Save.sha256)"
    }
    return $target
}

# 一次游戏启动：写临时存档目录的配置与参数文件，启动 RimWorld，跟随报告，返回结果。
function Invoke-Boot($Boot) {
    $saveData = $Boot.SaveData
    Write-Utf8 (Join-Path $saveData '.mugirl-gamevalidation-savedata') $Stamp
    $configDir = Join-Path $saveData 'Config'
    New-Item -ItemType Directory -Force -Path $configDir | Out-Null
    Write-Utf8 (Join-Path $configDir 'Prefs.xml') @"
<?xml version="1.0" encoding="utf-8"?>
<PrefsData>
  <volumeMaster>0</volumeMaster>
  <volumeGame>0</volumeGame>
  <volumeMusic>0</volumeMusic>
  <volumeAmbient>0</volumeAmbient>
  <volumeUI>0</volumeUI>
  <screenWidth>1280</screenWidth>
  <screenHeight>720</screenHeight>
  <fullscreen>False</fullscreen>
  <uiScale>1</uiScale>
  <customCursorEnabled>False</customCursorEnabled>
  <runInBackground>True</runInBackground>
  <devMode>True</devMode>
  <pauseOnLoad>False</pauseOnLoad>
  <pauseOnError>False</pauseOnError>
  <langFolderName>$(ConvertTo-XmlText $LanguageFolders[$Language])</langFolderName>
</PrefsData>
"@

    # X-settings：旧构建写出的 Mod 设置文件（文件名中的 {folder} 换成本 Mod 的目录名）。
    if ($Boot.ConfigFiles) {
        foreach ($file in Get-ChildItem -LiteralPath $Boot.ConfigFiles -File) {
            $name = $file.Name.Replace('{folder}', (Split-Path $ModRoot -Leaf))
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $configDir $name) -Force
        }
    }

    $bootMods = @($Mods | Select-Object -SkipLast 1) + @($Boot.ExtraMods) + @($Mods | Select-Object -Last 1)
    $modsConfig = Join-Path $configDir 'ModsConfig.xml'
    if ($Boot.Config -ne 'AllDlc') {
        # -mod= 总会启用全部 DLC（ModsConfig.ApplyCommandLineModList），所以 DLC 组合改用 ModsConfig.xml，不传 -mod=。
        $installed = @(Get-InstalledExpansions)
        $disabled = switch ($Boot.Config) {
            'CoreOnly' { $installed }
            'NoRoyalty' { @('ludeon.rimworld.royalty') }
            'NoIdeology' { @('ludeon.rimworld.ideology') }
            'NoBiotech' { @('ludeon.rimworld.biotech') }
            'NoAnomaly' { @('ludeon.rimworld.anomaly') }
            'NoOdyssey' { @('ludeon.rimworld.odyssey') }
        }
        $enabled = @($installed | Where-Object { $disabled -notcontains $_ })
        $active = @('brrainz.harmony', 'ludeon.rimworld') + $enabled + @($bootMods | Select-Object -Skip 1 | ForEach-Object { $_.ToLowerInvariant() })
        $activeXml = ($active | ForEach-Object { "    <li>$(ConvertTo-XmlText $_)</li>" }) -join "`n"
        $knownXml = ($installed | ForEach-Object { "    <li>$(ConvertTo-XmlText $_)</li>" }) -join "`n"
        Write-Utf8 $modsConfig @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$(ConvertTo-XmlText $gameVersion)</version>
  <activeMods>
$activeXml
  </activeMods>
  <knownExpansions>
$knownXml
  </knownExpansions>
</ModsConfigData>
"@
    }
    elseif (Test-Path -LiteralPath $modsConfig) {
        Remove-Item -LiteralPath $modsConfig -Force
    }

    $artifacts = Join-Path $Work "artifacts-$($Boot.Name)"
    $report = Join-Path $Work "report-$($Boot.Name).txt"
    $stdout = Join-Path $Work "stdout-$($Boot.Name).log"
    $stderr = Join-Path $Work "stderr-$($Boot.Name).log"
    $suiteOptions = [ordered]@{}
    foreach ($pair in @(@($SuiteOption) + @($Boot.SuiteOptions) | ForEach-Object { $_ -split ',' } | Where-Object { $_ -match '=' })) {
        $key, $value = $pair -split '=', 2
        $suiteOptions[$key.Trim()] = $value.Trim()
    }
    # X-settings 的第二个进程启动前取样：旧设置仅在 boot 1 布置，boot 2 必须保留升级后的字节与写入时间。
    if ($Boot.Index -eq 2 -and @($Boot.Fixtures | Where-Object { $_.kind -eq 'X-settings' }).Count -gt 0) {
        $settings = @(Get-ChildItem -LiteralPath $configDir -Filter 'Mod_*_MugirlMod.xml' -File)
        if ($settings.Count -ne 1) { Stop-Environment "X-settings expects one settings file before boot 2, found $($settings.Count)" }
        $suiteOptions['upgrade.settingsBeforeHash'] = Get-Sha256 $settings[0].FullName
        $suiteOptions['upgrade.settingsBeforeWriteUtcTicks'] = $settings[0].LastWriteTimeUtc.Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    $options = [ordered]@{
        reportPath           = $report
        suites               = [string[]]$Boot.Suites
        targetPackageId      = $TargetPackageId
        expectedModRoot      = $ExpectedModRoot
        expectedModDllSha256 = if ($Plumbing -or $TargetModDir) { $null } else { $runSummary.modDllSha256 }
        screenshots          = -not $NoScreenshots
        timeoutSeconds       = $TimeoutSeconds
        runId                = "$Stamp/$($Boot.Name)"
        entry                = $Boot.Entry
        boot                 = $Boot.Index
        resaveName           = $Boot.ResaveName
        fixtures             = @($Boot.Fixtures)
        artifactsDir         = $artifacts
        tokenSetPath         = if ($TokenSet) { (Resolve-Path -LiteralPath $TokenSet).Path } else { $null }
        offlineReportPath    = $Boot.OfflineReport
        legacyCorpusDir      = if (Test-Path -LiteralPath $LegacyCorpusDir) { (Resolve-Path -LiteralPath $LegacyCorpusDir).Path } else { $null }
        foreignTokens        = @($Boot.ForeignTokens)
        logAllowlist         = @($Boot.LogAllowlist)
        suiteOptions         = $suiteOptions
    }
    Write-Utf8 (Join-Path $saveData $OptionsFileName) ($options | ConvertTo-Json -Depth 6)

    $arguments = @($(if ($Headless) { '-Headless' } else { '-NoWindow' }), '-NoFileLog', '-NoCrashDialog')
    if ($Boot.Entry -eq 'quicktest') { $arguments += '-quicktest' }
    $arguments += @('-mugirlvalidate', "-savedatafolder=$saveData")
    if ($Boot.Config -eq 'AllDlc') { $arguments += "-mod=$($bootMods -join ';')" }
    $argumentLine = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    Write-Host ''
    Write-Host "==== boot $($Boot.Name): entry $($Boot.Entry), suites $($Boot.Suites -join ','), config $($Boot.Config)" -ForegroundColor Cyan
    Write-Host "command: `"$GameExe`" $argumentLine"

    $result = [ordered]@{
        name = $Boot.Name; entry = $Boot.Entry; suites = $Boot.Suites; config = $Boot.Config; started = $false; timedOut = $false
        report = $report; stdout = $stdout; stderr = $stderr; artifacts = $artifacts; saveData = $saveData
    }
    if ($PrepareOnly) { return [pscustomobject]$result }

    $running = @(Get-RimWorldProcess)
    if ($running.Count -gt 0) { Stop-Environment "RimWorld is running (PID $($running.Id -join ', ')); close it first" }
    $limit = $TimeoutSeconds + $LoadAllowanceSeconds
    $started = Get-Date
    $script:process = Start-Process -FilePath $GameExe -ArgumentList $argumentLine -WorkingDirectory $GameDir -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $result.started = $true
    Write-Host "RimWorld started (PID $($script:process.Id)); limit $limit s"
    $offset = 0L
    while (-not $script:process.WaitForExit(2000)) {
        foreach ($line in Read-NewReportLines $report ([ref]$offset)) { Write-ReportLine $line }
        if (((Get-Date) - $started).TotalSeconds -gt $limit) {
            $result.timedOut = $true
            Write-Host "RimWorld did not finish within $limit s; ending PID $($script:process.Id)" -ForegroundColor Red
            $script:process.Kill($true)
            [void]$script:process.WaitForExit(30000)
            break
        }
    }
    foreach ($line in Read-NewReportLines $report ([ref]$offset)) { Write-ReportLine $line }
    $elapsed = ((Get-Date) - $started).TotalSeconds
    $result.seconds = [int]$elapsed
    $result.processExitCode = if ($script:process.HasExited) { $script:process.ExitCode } else { $null }
    Write-Host ("RimWorld exited after {0:N0} s{1}" -f $elapsed, $(if ($result.timedOut) { ' (killed)' } else { " with code $($script:process.ExitCode)" }))

    $reportLines = if (Test-Path -LiteralPath $report) { @(Get-Content -LiteralPath $report -Encoding utf8) } else { @() }
    $lines = @($reportLines | Where-Object { $_.StartsWith($ReportPrefix) } | ForEach-Object { $_.Substring($ReportPrefix.Length).Trim() })
    if ($lines.Count -eq 0 -and (Test-Path -LiteralPath $stdout)) {
        # 报告文件写不出来时，结果行仍在标准输出中。
        $lines = @(Get-Content -LiteralPath $stdout -Encoding utf8 | Where-Object { $_ -match [regex]::Escape($ReportPrefix) } |
            ForEach-Object { $_.Substring($_.IndexOf($ReportPrefix) + $ReportPrefix.Length).Trim() })
    }
    $result.lines = $lines
    $result.failures = @($lines | Where-Object { $_ -match '^FAIL ' })
    $result.skips = @($lines | Where-Object { $_ -match '^skip ' })
    $result.ok = @($lines | Where-Object { $_ -match '^ok ' }).Count
    $result.done = $lines | Where-Object { $_ -match '^DONE ok=(\d+) failed=(\d+) skipped=(\d+)' } | Select-Object -Last 1
    $stdoutProblems = 0
    if (Test-Path -LiteralPath $stdout) {
        foreach ($line in [System.IO.File]::ReadLines($stdout)) {
            if ($line -match '^\S+\s+ERROR\s' -and $line -match 'mugirl' -and $line -notmatch 'GameValidation') { $stdoutProblems++ }
        }
    }
    $result.stdoutProblems = $stdoutProblems
    $result.passed = [bool]$result.done -and $result.failures.Count -eq 0 -and -not $result.timedOut
    return [pscustomobject]$result
}

# ---------------------------------------------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------------------------------------------

$exitCode = 2
$testModDir = $null
$aliasDirs = [System.Collections.Generic.List[string]]::new()
$process = $null
$lock = $null
$boots = [System.Collections.Generic.List[object]]::new()
$runSummary = [ordered]@{ runId = $Stamp; plumbing = [bool]$Plumbing; language = $Language; headless = [bool]$Headless }

try {
    if (-not (Test-Path -LiteralPath $GameExe)) { Stop-Environment "RimWorld.exe not found in $GameDir" }
    if ($Plumbing -and ($TargetModDir -or $Fixture.Count -gt 0)) { Stop-Environment '-Plumbing cannot be combined with -TargetModDir or -Fixture' }
    $requestedSuites = @($Suite | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $referenceResaveOnly = $requestedSuites.Count -eq 1 -and $requestedSuites[0] -eq 'reference-resave'
    if ($TargetModDir -and $Fixture.Count -gt 0 -and -not $referenceResaveOnly) { Stop-Environment '-TargetModDir with -Fixture requires reference-resave as the sole suite' }
    if ($referenceResaveOnly -and ($BuildMod -or $Deploy -or $ModDll)) { Stop-Environment 'reference-resave cannot build, deploy, or substitute a DLL; select the staged -TargetModDir' }
    if (-not $PrepareOnly) { $lock = Enter-InGameLock -Label "Test-InGame $Stamp" -WaitSeconds $LockWaitSeconds }
    $running = @(Get-RimWorldProcess)
    if ($running.Count -gt 0) { Stop-Environment "RimWorld is running (PID $($running.Id -join ', ')); close it first" }

    $fixtures = @(if ($Fixture.Count -gt 0) { Resolve-Fixtures $Fixture })
    if ($Fixture.Count -gt 0 -and $fixtures.Count -eq 0) { Stop-Environment "no feasible fixture matches -Fixture $($Fixture -join ',')" }
    $suites = @($Suite | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($suites.Count -eq 0) { $suites = @(if ($Plumbing) { 'plumbing' } elseif ($fixtures.Count -gt 0) { 'upgrade' } else { 'default' }) }
    $runSummary.suites = $suites
    $gameVersion = (Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -TotalCount 1).Trim()
    $runSummary.gameVersion = $gameVersion
    foreach ($dependency in @($HarmonyWorkshopId) + @(if (-not $Plumbing) { $HarWorkshopId })) {
        if (-not (Test-Path -LiteralPath (Join-Path $WorkshopDir $dependency))) { Write-Warning "workshop item $dependency is not installed in $WorkshopDir" }
    }

    # 被测构建：开发目录的 HAR.MugirlRace，或 -TargetModDir 指定的临时旧构建。
    $TargetPackageId = $ModPackageId
    $ExpectedModRoot = if ($Plumbing) { $null } else { $ModRoot }
    if ($TargetModDir) {
        $TargetModDir = Assert-Under (Resolve-Path -LiteralPath $TargetModDir).Path $ModsDir
        if (-not (Test-Path -LiteralPath (Join-Path $TargetModDir $TestMarker))) { Stop-Environment "$TargetModDir is not a marked temporary mod (no $TestMarker)" }
        $TargetPackageId = ([xml](Get-Content -LiteralPath (Join-Path $TargetModDir 'About\About.xml') -Raw -Encoding utf8)).ModMetaData.packageId
        if (-not $TargetPackageId -or $TargetPackageId -eq $ModPackageId) { Stop-Environment "$TargetModDir must have its own packageId (found '$TargetPackageId')" }
        $ExpectedModRoot = $TargetModDir
        $runSummary.target = [ordered]@{ packageId = $TargetPackageId; folder = $TargetModDir }
    }

    New-Item -ItemType Directory -Force -Path $WorkRoot | Out-Null
    $Work = Assert-Under $Work $WorkRoot
    New-MarkedDirectory $Work $WorkMarker
    Write-Host "work folder: $Work"

    # 源码副本：构建只发生在 %TEMP%，obj/bin 与输出都不进入 Mod 目录。
    $copy = Join-Path $Work 'build'
    $sourceCopy = Join-Path $copy 'Source'
    $null = robocopy (Join-Path $ModRoot 'Source') $sourceCopy /MIR /XJ /XD bin obj TestResults /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { Stop-Environment "copying Source/ failed (robocopy exit $LASTEXITCODE)" }

    # 被测 DLL
    $dllUnderTest = $null
    if (-not $Plumbing -and -not $TargetModDir) {
        if ($BuildMod) {
            # K3：普通构建只输出到工程的 bin/；-Deploy 时用工程自己的部署步骤（DeployToMod）把 DLL 与 PDB 写进本 Mod 的 1.7/Assemblies。
            $modProject = Join-Path $sourceCopy 'MugirlRace\MugirlRace.csproj'
            $deployProperties = if ($Deploy -and -not $PrepareOnly) {
                @('-p:DeployToMod=true', "-p:MugirlDeployDir=$(ConvertTo-MSBuildPath (Join-Path $ModRoot '1.7\Assemblies'))")
            } else { @() }
            Invoke-Build $modProject $deployProperties 'mod'
            $dllUnderTest = Get-TargetPath $modProject @()
            if ($deployProperties.Count -gt 0) { Write-Host "[deploy] the build deployed $dllUnderTest to 1.7/Assemblies (DeployToMod)" }
        }
        elseif ($ModDll) {
            $dllUnderTest = (Resolve-Path -LiteralPath $ModDll).Path
        }
        else {
            if (-not (Test-Path -LiteralPath $DeployedDll)) {
                Stop-Environment "1.7/Assemblies/MugirlRace.dll does not exist; use -BuildMod -Deploy"
            }
            $dllUnderTest = $DeployedDll
        }
        if (-not (Test-Path -LiteralPath $dllUnderTest)) { Stop-Environment "the DLL under test $dllUnderTest does not exist" }

        # RimWorld 加载的是 1.7/Assemblies 中的 DLL：被测 DLL 必须就是它。只有 -BuildMod -Deploy 经工程的部署步骤（K3）写入那里，脚本从不手工复制。
        $candidateHash = Get-Sha256 $dllUnderTest
        $deployedHash = if (Test-Path -LiteralPath $DeployedDll) { Get-Sha256 $DeployedDll } else { $null }
        if ($candidateHash -ne $deployedHash -and -not $PrepareOnly) {
            $hint = if ($BuildMod) { 'add -Deploy (the project deploys it with -p:DeployToMod=true)' } else { 'deploy it with -BuildMod -Deploy, or build it with -p:DeployToMod=true' }
            Stop-Environment "the DLL under test ($dllUnderTest) is not the one in 1.7/Assemblies, which is what RimWorld loads; $hint"
        }
        if ($Deploy -and -not $BuildMod) { Stop-Environment '-Deploy only applies to -BuildMod (the project deploys its own build)' }
        $runSummary.modDll = $dllUnderTest
        $runSummary.modDllSha256 = $candidateHash
        Write-Host "DLL under test: $dllUnderTest (SHA-256 $candidateHash)"
    }

    # 测试模组：正式运行按路径引用将要在游戏中运行的 DLL；-Plumbing 与旧构建运行完全不引用 MugirlRace（不编译 Suites/Mod/），
    # 所以 Mod 源码暂时编译不过时仍能调试脚本与框架，旧构建的类型也不会与测试模组的编译期引用冲突。
    $properties = if ($Plumbing -or $TargetModDir) { @('-p:MugirlPlumbingOnly=true') } else { @("-p:MugirlRaceDll=$(ConvertTo-MSBuildPath $dllUnderTest)") }
    $testProject = Join-Path $sourceCopy 'Tests\MugirlRace.GameValidation\MugirlRace.GameValidation.csproj'
    Invoke-Build $testProject $properties 'test-mod'
    $testDll = Get-TargetPath $testProject $properties
    if (-not (Test-Path -LiteralPath $testDll)) { Stop-Environment "the test mod build produced no $testDll" }

    # 清理上次中断留下的临时测试模组（只删带标记的）。
    foreach ($stale in Get-ChildItem -LiteralPath $ModsDir -Directory -Filter "$TestFolderPrefix*" -Force) {
        if (-not (Remove-MarkedDirectory $stale.FullName $TestMarker)) { Stop-Environment "a stale test mod folder could not be removed safely: $($stale.FullName)" }
        Write-Host "removed a stale test mod folder: $($stale.FullName)"
    }

    $testModDir = Assert-Under (Join-Path $ModsDir ($TestFolderPrefix + [guid]::NewGuid().ToString('N').Substring(0, 8))) $ModsDir
    New-MarkedDirectory $testModDir $TestMarker
    New-Item -ItemType Directory -Path (Join-Path $testModDir 'About'), (Join-Path $testModDir 'Assemblies') | Out-Null
    $publishedId = (Get-Content -LiteralPath (Join-Path $ModRoot 'About\PublishedFileId.txt') -TotalCount 1).Trim()
    # 依赖项写 steamWorkshopUrl，否则元数据警告会提到被测 Mod 的 packageId，被误判为本 Mod 的加载问题（E40）。
    $modDependency = if ($Plumbing -or $TargetModDir) { '' } else {
        @"

    <li>
      <packageId>$ModPackageId</packageId>
      <displayName>Mugirl Race</displayName>
      <steamWorkshopUrl>steam://url/CommunityFilePage/$publishedId</steamWorkshopUrl>
    </li>
"@
    }
    Write-Utf8 (Join-Path $testModDir 'About\About.xml') @"
<?xml version="1.0" encoding="utf-8"?>
<ModMetaData>
  <name>Mugirl Race in-game validation (temporary)</name>
  <author>Mugirl Race test tooling</author>
  <packageId>$TestPackageId</packageId>
  <supportedVersions>
    <li>1.7</li>
  </supportedVersions>
  <modDependencies>
    <li>
      <packageId>brrainz.harmony</packageId>
      <displayName>Harmony</displayName>
      <steamWorkshopUrl>steam://url/CommunityFilePage/$HarmonyWorkshopId</steamWorkshopUrl>
    </li>$modDependency
  </modDependencies>
  <loadAfter>
    <li>brrainz.harmony</li>
    <li>$(ConvertTo-XmlText $TargetPackageId)</li>
  </loadAfter>
  <description>Created and removed by tools/Test-InGame.ps1. Inert unless RimWorld is started with -mugirlvalidate.</description>
</ModMetaData>
"@
    Copy-Item -LiteralPath $testDll -Destination (Join-Path $testModDir 'Assemblies')
    $testPdb = [System.IO.Path]::ChangeExtension($testDll, '.pdb')
    if (Test-Path -LiteralPath $testPdb) { Copy-Item -LiteralPath $testPdb -Destination (Join-Path $testModDir 'Assemblies') }
    Write-Host "test mod staged in $testModDir"

    $Mods = @('brrainz.harmony')
    if (-not $Plumbing) { $Mods += @($HarPackageId, $TargetPackageId) }
    $Mods += $TestPackageId
    $runSummary.mods = $Mods

    # X-alias：夹具中的依赖 Mod 复制成带标记的临时 Mod（不建联接），加载在被测 Mod 之后。
    foreach ($stale in Get-ChildItem -LiteralPath $ModsDir -Directory -Filter "$AliasFolderPrefix*" -Force) {
        if (-not (Remove-MarkedDirectory $stale.FullName $TestMarker)) { Stop-Environment "a stale alias mod folder could not be removed safely: $($stale.FullName)" }
    }
    foreach ($info in @($fixtures | Where-Object { $_.AliasMod })) {
        $aliasDir = Assert-Under (Join-Path $ModsDir ($AliasFolderPrefix + [guid]::NewGuid().ToString('N').Substring(0, 8))) $ModsDir
        New-MarkedDirectory $aliasDir $TestMarker
        $aliasDirs.Add($aliasDir)
        $null = robocopy $info.AliasMod $aliasDir /E /XJ /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { Stop-Environment "copying the alias mod of $($info.Id) failed (robocopy exit $LASTEXITCODE)" }
        Write-Host "alias mod $($info.AliasPackageId) of fixture $($info.Id) staged in $aliasDir"
    }

    # 启动计划（每一项由几张表合成，后面的表覆盖前面的同名键）
    $plan = [System.Collections.Generic.List[object]]::new()
    $noExtras = @{ ExtraMods = @(); ConfigFiles = $null; ForeignTokens = @(); LogAllowlist = @(); SuiteOptions = @(); Expand = $null }
    if ($fixtures.Count -eq 0) {
        $plan.Add((New-BootPlan @{
                    Name = 'main'; Index = 1; Entry = 'quicktest'; Suites = $suites; Config = $(if ($Config) { $Config } else { 'AllDlc' })
                    SaveData = (Join-Path $Work 'savedata'); ResaveName = $null; Fixtures = @(); OfflineReport = $null; After = $null
                } $noExtras))
    }
    else {
        foreach ($info in $fixtures) {
            $saveData = Join-Path $Work "savedata-$($info.Id)"
            $bootConfig = if ($Config) { $Config } else { $info.Config }
            $saveName = "$FixtureSavePrefix$($info.Id)"
            $resave1 = "MugirlValidation-upgrade-$($info.Id)-boot1"
            if ($info.Kind -eq 'C') {
                # 对照组：被测构建先自己写一份存档（sweep 套件，快速测试开局），再在新进程中读入它。
                $manifest = Join-Path $Work "artifacts-$($info.Id)-seed\sweep.json"
                $reference = [ordered]@{ id = $info.Id; save = $saveName; manifest = $manifest; kind = 'C'; gameVersion = $gameVersion }
                $plan.Add((New-BootPlan @{
                            Name = "$($info.Id)-seed"; Index = 1; Entry = 'quicktest'; Suites = @('sweep'); Config = $bootConfig; SaveData = $saveData
                            ResaveName = $null; Fixtures = @(); OfflineReport = $null; After = $null
                        } $noExtras @{ SuiteOptions = @("sweep.ticks=$ControlTicks", "sweep.save=$saveName") }))
                $plan.Add((New-BootPlan @{
                            Name = "$($info.Id)-boot1"; Index = 1; Entry = "load:$saveName"; Suites = $suites; Config = $bootConfig; SaveData = $saveData
                            ResaveName = $resave1; Fixtures = @($reference); OfflineReport = $null; After = (Join-Path $saveData "Saves\$saveName.rws")
                        } $noExtras))
                $plan.Add((New-BootPlan @{
                            Name = "$($info.Id)-boot2"; Index = 2; Entry = "load:$resave1"; Suites = $suites; Config = $bootConfig; SaveData = $saveData
                            ResaveName = "MugirlValidation-upgrade-$($info.Id)-boot2"; Fixtures = @($reference); OfflineReport = $null
                            After = (Join-Path $saveData "Saves\$resave1.rws")
                        } $noExtras))
                continue
            }
            $extras = @{
                ExtraMods = @(if ($info.AliasPackageId) { $info.AliasPackageId }); ConfigFiles = $info.ConfigFiles
                ForeignTokens = $info.ForeignTokens; LogAllowlist = $info.LogAllowlist; SuiteOptions = @(); Expand = $info
            }
            $reference = [ordered]@{ id = $info.Id; save = $saveName; manifest = $info.ManifestPath; variantManifest = $info.VariantManifestPath; kind = $info.Kind; gameVersion = $info.GameVersion }
            $plan.Add((New-BootPlan @{
                        Name = "$($info.Id)-boot1"; Index = 1; Entry = "load:$saveName"; Suites = $suites; Config = $bootConfig; SaveData = $saveData
                        ResaveName = $resave1; Fixtures = @($reference); OfflineReport = $info.OfflineReport; After = $null
                    } $extras))
            $plan.Add((New-BootPlan @{
                        Name = "$($info.Id)-boot2"; Index = 2; Entry = "load:$resave1"; Suites = $suites; Config = $bootConfig; SaveData = $saveData
                        ResaveName = "MugirlValidation-upgrade-$($info.Id)-boot2"; Fixtures = @($reference); OfflineReport = $null
                        After = (Join-Path $saveData "Saves\$resave1.rws")
                    } $extras @{ Expand = $null; ConfigFiles = $null }))
        }
    }
    $runSummary.config = @($plan | ForEach-Object Config | Select-Object -Unique)

    foreach ($boot in $plan) {
        New-Item -ItemType Directory -Force -Path $boot.SaveData | Out-Null
        if ($boot.Expand) {
            $expanded = Expand-Fixture $boot.Expand (Join-Path $boot.SaveData 'Saves') "$FixtureSavePrefix$($boot.Expand.Id)"
            Write-Host "fixture $($boot.Expand.Id) expanded to $expanded"
        }
        if ($boot.After -and -not (Test-Path -LiteralPath $boot.After)) {
            Write-Host "boot $($boot.Name) skipped: the previous boot wrote no $($boot.After)" -ForegroundColor Red
            $boots.Add([pscustomobject]@{ name = $boot.Name; started = $false; passed = $false; skipped = "no $($boot.After)"; failures = @(); skips = @(); ok = 0; done = $null; timedOut = $false })
            continue
        }
        $boots.Add((Invoke-Boot $boot))
        if ($PrepareOnly) { Write-Host 'prepare only: not starting RimWorld' -ForegroundColor Cyan; break }
    }
    $exitCode = if ($PrepareOnly) { 0 } else { 1 }
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
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill($true)
        [void]$process.WaitForExit(30000)
    }
    if ($testModDir) {
        if (Remove-MarkedDirectory $testModDir $TestMarker) { Write-Host 'temporary test mod removed' }
        else { Write-Host "WARNING: remove the temporary test mod folder by hand: $testModDir" -ForegroundColor Yellow }
    }
    foreach ($aliasDir in $aliasDirs) {
        if (-not (Remove-MarkedDirectory $aliasDir $TestMarker)) { Write-Host "WARNING: remove the temporary alias mod folder by hand: $aliasDir" -ForegroundColor Yellow }
    }
    Exit-InGameLock $lock
}

# ---------------------------------------------------------------------------------------------------------------
# 结果
# ---------------------------------------------------------------------------------------------------------------

$startedBoots = @($boots | Where-Object { $_.started })
if ($startedBoots.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    foreach ($file in @('build-test-mod.log', 'build-mod.log')) {
        $path = Join-Path $Work $file
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $OutDir }
    }
    $single = $boots.Count -eq 1
    foreach ($boot in $startedBoots) {
        # 单次启动沿用原来的文件名（report.txt、stdout.log），多次启动按启动名区分。
        $suffix = if ($single) { '' } else { "-$($boot.name)" }
        foreach ($pair in @(@($boot.report, "report$suffix.txt"), @($boot.stdout, "stdout$suffix.log"), @($boot.stderr, "stderr$suffix.log"))) {
            if (Test-Path -LiteralPath $pair[0]) { Copy-Item -LiteralPath $pair[0] -Destination (Join-Path $OutDir $pair[1]) }
        }
        $bootOut = if ($single) { $OutDir } else { Join-Path $OutDir $boot.name }
        New-Item -ItemType Directory -Force -Path $bootOut | Out-Null
        foreach ($file in @('Config\Prefs.xml', 'Config\ModsConfig.xml', $OptionsFileName)) {
            $path = Join-Path $boot.saveData $file
            if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $bootOut }
        }
        $shotsSource = Join-Path $boot.saveData 'Screenshots'
        if (Test-Path -LiteralPath $shotsSource) {
            $shotsTarget = Join-Path $bootOut 'Screenshots'
            New-Item -ItemType Directory -Force -Path $shotsTarget | Out-Null
            Get-ChildItem -LiteralPath $shotsSource -Filter '*.png' -File | Copy-Item -Destination $shotsTarget
        }
        if (Test-Path -LiteralPath $boot.artifacts) {
            Copy-Item -LiteralPath $boot.artifacts -Destination (Join-Path $bootOut 'artifacts') -Recurse -Force
        }
        if ($CollectSaves -and (Test-Path -LiteralPath (Join-Path $boot.saveData 'Saves'))) {
            $savesTarget = Join-Path $bootOut 'Saves'
            New-Item -ItemType Directory -Force -Path $savesTarget | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $boot.saveData 'Saves') -Filter '*.rws' -File | Copy-Item -Destination $savesTarget
        }
    }

    # 截图在每次启动的结果目录中（单次启动时即结果目录本身）。
    $analysis = [pscustomobject]@{ Ran = $false; Passed = $true; Lines = @() }
    foreach ($boot in $startedBoots) {
        $shots = Join-Path $(if ($single) { $OutDir } else { Join-Path $OutDir $boot.name }) 'Screenshots'
        $result = Test-RenderScreenshots $shots
        $analysis.Passed = $analysis.Passed -and $result.Passed
        $analysis.Ran = $analysis.Ran -or $result.Ran
        $analysis.Lines += @($result.Lines | ForEach-Object { if ($single) { $_ } else { "[$($boot.name)] $_" } })
    }
    $runSummary.screenshotAnalysis = $analysis.Lines

    Write-Host ''
    Write-Host "==== results ($OutDir)"
    foreach ($boot in $boots) {
        $label = if ($single) { '' } else { "[$($boot.name)] " }
        if ($boot.done) { Write-Host "  $label$($boot.done)" -ForegroundColor $(if ($boot.failures.Count -eq 0) { 'Green' } else { 'Red' }) }
        elseif ($boot.PSObject.Properties['skipped'] -and $boot.skipped) { Write-Host "  ${label}not started: $($boot.skipped)" -ForegroundColor Red }
        else { Write-Host "  ${label}no DONE line: the run did not finish (crash, hang or timeout); see stdout" -ForegroundColor Red }
        foreach ($skip in $boot.skips) { Write-Host "  $label$skip" -ForegroundColor DarkYellow }
        foreach ($failure in $boot.failures) { Write-Host "  $label$failure" -ForegroundColor Red }
        if ($boot.PSObject.Properties['stdoutProblems'] -and $boot.stdoutProblems -gt 0) {
            Write-Host "  ${label}stdout has $($boot.stdoutProblems) ERROR line(s) mentioning mugirl; the report attributes them per step" -ForegroundColor DarkYellow
        }
    }
    foreach ($line in $analysis.Lines) { Write-Host "  [screenshots] $line" -ForegroundColor $(if ($line.StartsWith('FAIL')) { 'Red' } else { 'Gray' }) }

    $runSummary.boots = @($boots | ForEach-Object {
            [ordered]@{
                name = $_.name; started = $_.started; done = [bool]$_.done; ok = $_.ok; failed = @($_.failures).Count; skipped = @($_.skips).Count
                timedOut = $_.timedOut; seconds = if ($_.PSObject.Properties['seconds']) { $_.seconds } else { $null }
                processExitCode = if ($_.PSObject.Properties['processExitCode']) { $_.processExitCode } else { $null }
            }
        })
    $runSummary.ok = ($boots | Measure-Object -Property ok -Sum).Sum
    $runSummary.failed = @($boots | ForEach-Object { $_.failures }).Count
    $runSummary.skipped = @($boots | ForEach-Object { $_.skips }).Count
    $runSummary.done = @($boots | Where-Object { -not $_.done }).Count -eq 0
    $runSummary.timedOut = @($boots | Where-Object { $_.timedOut }).Count -gt 0
    $passed = $exitCode -ne 2 -and @($boots | Where-Object { -not $_.passed }).Count -eq 0 -and $analysis.Passed
    $exitCode = if ($exitCode -eq 2) { 2 } elseif ($passed) { 0 } else { 1 }
    $runSummary.exitCode = $exitCode
    Write-Utf8 (Join-Path $OutDir 'summary.json') ($runSummary | ConvertTo-Json -Depth 6)
    Write-Host $(if ($passed) { 'PASS' } else { 'FAIL' }) -ForegroundColor $(if ($passed) { 'Green' } else { 'Red' })
}

$after = @(Get-RimWorldProcess)
if ($after.Count -gt 0) { Write-Host "WARNING: a RimWorld process is still running (PID $($after.Id -join ', ')); it was not started by this run" -ForegroundColor Yellow }

if (Test-Path -LiteralPath $Work) {
    if ($KeepWorkDir -or $exitCode -ne 0) { Write-Host "work folder kept: $Work" }
    elseif (-not (Remove-MarkedDirectory $Work $WorkMarker)) { Write-Host "WARNING: could not remove $Work" -ForegroundColor Yellow }
}

exit $exitCode
