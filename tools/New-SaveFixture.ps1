#Requires -Version 7.0
<#
.SYNOPSIS
    在 RimWorld 1.7 上重放已发布的雪牛娘旧构建，制作存档升级用的 P 夹具（DevData/Evidence/SaveFixtures/<id>/）。

.DESCRIPTION
    做法见 critique-validation §4.2 与 validator-tooling §4.3（改编自 hk 的冻结构建冒烟测试）：

      1. 取得游戏内运行锁 %TEMP%\mugirl-ingame.lock（整个制作过程持有；子进程 tools/Test-InGame.ps1 经环境变量中的令牌认出它；
         本脚本自己由持锁的父脚本启动时同样沿用父脚本的锁）。拒绝在 RimWorld 运行时执行。
         构建的来源与校验值取自验证器的旧版语料 Baselines/legacy/builds.tsv（发布 ZIP 中的 MugirlRace.dll 的 SHA-256；git 构建另核对 blob）。
      2. 把构建复制到 <游戏>/Mods/__MugirlLegacy_<构建>_<随机>/（带标记文件 .mugirl-gamevalidation-tmp）。只复制、不建联接（游戏会往
         加载目录写 Assets.zip 与 *.spv-cache，联接会让它写进冻结的源目录）。About.xml 的 packageId 改为 mugirl.legacy.<构建>、
         supportedVersions 加上 1.7；LoadFolders.xml 的 v1.7 列表等于该构建的 v1.6 列表；1.6/Assemblies 只带 MugirlRace.dll
         （从不带 NewbornBackstoryProbe.dll），不带 Source。
      3. 可行性闸门：第一次启动只运行 plumbing 与 fixture-gate 套件（快速测试开局、存读档往返、类型加载、Harmony 补丁、加载期问题）。
         不能开局或不能存读档往返的构建记为不可行，清单写明原因，不再播种。
      4. 播种：第二次启动运行 sweep 套件（ContentSweepSuite：只用原版 API，按 packageId 选 Def，放入各类内容，运行两天，让地图平静，
         在本 Mod 的工作进行中存档，并从原始 XML 中用独立的旧名查询提取事实与覆盖）。构建支持而存档中缺少的内容类别记入清单的
         coverageMissing，脚本以退出码 1 结束（夹具照常写出）。
      5. 后处理存档的副本：meta 中的 mugirl.legacy.<构建> 改为 har.mugirlrace、名称改为 Mugirl Race（顺序不变，读档时原版的“环境相同”
         判断成立，正是类型改名最严苛的路径），gzip 压缩，写 manifest.json（来源与 SHA-256、游戏版本、Mod 列表、DLC 组合、闸门结果、
         播种动作与结果、事实、文件大小与 SHA-256）。
      6. finally 中只删除带标记、且不含链接的临时目录（与 Test-InGame 相同的 Remove-MarkedDirectory 规则）。从不触碰用户的
         Saves/、Config/、1.7/Assemblies 与 1.6 目录。

    退出码：0 每个请求的构建都得到了结论（可行的已生成完整覆盖的夹具，不可行的已记录原因）；1 有构建因脚本或环境问题没有结论，
    或夹具有覆盖缺口；2 参数或环境错误。

.PARAMETER Build
    构建标识（1004、0927、0920、0.5.0、0.4.7），可用逗号分隔；all 表示全部。

.PARAMETER Config
    DLC 组合。

.PARAMETER Ticks
    播种后运行的游戏 Tick 数（默认两天）。

.PARAMETER GateOnly
    只运行可行性闸门，不播种、不写夹具。

.PARAMETER Force
    覆盖已存在的夹具目录（只覆盖本脚本写的文件）。

.PARAMETER TimeoutSeconds
    播种启动的游戏内会话上限（秒）。

.PARAMETER KeepWorkDir
    保留 %TEMP% 中的工作目录（解出的构建、两次启动的结果与原始存档）。

.PARAMETER FixtureRoot
    夹具目录。默认 <Mod>/DevData/Evidence/SaveFixtures。

.EXAMPLE
    pwsh tools/New-SaveFixture.ps1 -Build all

.EXAMPLE
    pwsh tools/New-SaveFixture.ps1 -Build 0927 -GateOnly
#>
[CmdletBinding()]
param(
    [string[]] $Build = @('all'),
    [ValidateSet('AllDlc', 'CoreOnly')]
    [string] $Config = 'AllDlc',
    [ValidateRange(2500, 600000)]
    [int] $Ticks = 120000,
    [switch] $GateOnly,
    [switch] $Force,
    [ValidateRange(300, 7200)]
    [int] $TimeoutSeconds = 3600,
    [ValidateRange(0, 86400)]
    [int] $LockWaitSeconds = 3600,
    [switch] $KeepWorkDir,
    [string] $FixtureRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---------------------------------------------------------------------------------------------------------------
# 构建目录：已发布构建（decisions §5：不重放 MooGirl 时代构建）。来源与校验值取自 builds.tsv（语料 id 见 Corpus 列）。
# ---------------------------------------------------------------------------------------------------------------

$Catalog = [ordered]@{
    '1004'  = @{ Key = 'r1004'; Corpus = 'p.zip.1004'; Note = 'latest published release ZIP' }
    '0927'  = @{ Key = 'r0927'; Corpus = 'p.zip.0927'; Note = 'release ZIP whose DLL is the Workshop item 3578882219' }
    '0920'  = @{ Key = 'r0920'; Corpus = 'p.zip.0920'; Note = 'release ZIP (DLL also in git a172c15)' }
    '0.5.0' = @{ Key = 'v050'; Corpus = 'p.git.46d2190'; Note = 'git release commit 0.5.0' }
    '0.4.7' = @{ Key = 'v047'; Corpus = 'p.git.324501c'; Note = 'git release commit 0.4.7 (first MugirlRace-era build)' }
}

# 构建中要带进临时 Mod 的部分（根目录的 Textures、Sounds 由 LoadFolders 的 "/" 加载）。
$ContentFolders = @('1.6', 'Bio_1.6', 'Odyssey_1.6', 'Versions', 'Textures', 'Sounds', 'Common')
$WorkshopDll = 'workshop\content\294100\3578882219\1.6\Assemblies\MugirlRace.dll'

$ModRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$GameDir = (Resolve-Path -LiteralPath (Join-Path $ModRoot '..\..')).Path
$ModsDir = Join-Path $GameDir 'Mods'
$TestInGame = Join-Path $PSScriptRoot 'Test-InGame.ps1'
$BuildsTsv = Join-Path $ModRoot 'Source\Tests\MugirlRace.Validation\Baselines\legacy\builds.tsv'
if (-not $FixtureRoot) { $FixtureRoot = Join-Path $ModRoot 'DevData\Evidence\SaveFixtures' }
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$WorkRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl-fixtures'
$TempModPrefix = '__MugirlLegacy_'
$TempMarker = '.mugirl-gamevalidation-tmp'
$WorkMarker = '.mugirl-gamevalidation-work'
$SweepSaveName = 'MugirlFixture-sweep'
$LockPath = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl-ingame.lock'
$LockTokenVariable = 'MUGIRL_INGAME_LOCK_TOKEN'
$Utf8 = [System.Text.UTF8Encoding]::new($false)

class EnvironmentProblem : System.Exception {
    EnvironmentProblem([string] $message) : base($message) {}
}

function Stop-Environment([string] $Message) { throw [EnvironmentProblem]::new($Message) }

# ---------------------------------------------------------------------------------------------------------------
# 临时目录与锁：与 tools/Test-InGame.ps1 相同的规则（只删带标记、不含链接的真实目录；K5 锁）。
# ---------------------------------------------------------------------------------------------------------------

function Test-ReparsePoint([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    return $null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Assert-Under([string] $Path, [string] $Parent) {
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { Stop-Environment "refusing to touch ${full}: it is not under $root" }
    return $full
}

function New-MarkedDirectory([string] $Path, [string] $Marker) {
    if (Test-Path -LiteralPath $Path) { Stop-Environment "$Path already exists" }
    New-Item -ItemType Directory -Path $Path | Out-Null
    if (Test-ReparsePoint $Path) { Stop-Environment "$Path became a reparse point" }
    New-Item -ItemType File -Path (Join-Path $Path $Marker) | Out-Null
}

function Remove-MarkedDirectory([string] $Path, [string] $Marker) {
    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    if (Test-ReparsePoint $Path) { Write-Warning "not removing ${Path}: it is a junction or symbolic link"; return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $Path $Marker))) { Write-Warning "not removing ${Path}: the marker $Marker is missing"; return $false }
    $links = @(Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue)
    if ($links.Count -gt 0) { Write-Warning "not removing ${Path}: it contains links ($($links[0].FullName))"; return $false }
    Remove-Item -LiteralPath $Path -Recurse -Force
    return -not (Test-Path -LiteralPath $Path)
}

Import-Module (Join-Path $PSScriptRoot 'Mugirl.InGameLock.psm1') -Force

function Enter-InGameLock([string] $Label, [int] $WaitSeconds = 0) {
    try { return Enter-MugirlInGameLock -Label $Label -WaitSeconds $WaitSeconds -Path $LockPath -TokenVariable $LockTokenVariable }
    catch { Stop-Environment $_.Exception.Message }
}

function Exit-InGameLock($Lock) { Exit-MugirlInGameLock $Lock }

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Write-Utf8([string] $Path, [string] $Text) { [System.IO.File]::WriteAllText($Path, $Text, $Utf8) }

function Copy-Tree([string] $From, [string] $To, [string[]] $ExcludeDirs = @(), [string[]] $ExcludeFiles = @()) {
    $arguments = @($From, $To, '/E', '/XJ', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($ExcludeDirs.Count -gt 0) { $arguments += @('/XD') + $ExcludeDirs }
    if ($ExcludeFiles.Count -gt 0) { $arguments += @('/XF') + $ExcludeFiles }
    $null = robocopy @arguments
    if ($LASTEXITCODE -ge 8) { Stop-Environment "copying $From failed (robocopy exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------------------------------------------------------
# 一个构建：取出、布置、闸门、播种、后处理
# ---------------------------------------------------------------------------------------------------------------

# builds.tsv 中语料 id 为 $CorpusId 的一行：来源（repo:<zip>!<dll> 或 git:<rev>:<dll>）、DLL 的 SHA-256 与 git blob。
function Get-CorpusBuild([string] $CorpusId) {
    if (-not (Test-Path -LiteralPath $BuildsTsv)) { Stop-Environment "the legacy corpus is missing: $BuildsTsv" }
    foreach ($line in [System.IO.File]::ReadLines($BuildsTsv)) {
        if ($line.StartsWith('#')) { continue }
        $columns = $line.Split("`t")
        if ($columns.Count -ge 6 -and $columns[0] -eq $CorpusId) {
            return [pscustomobject]@{ Id = $columns[0]; Tier = $columns[1]; Source = $columns[3]; Sha256 = $columns[4].ToLowerInvariant(); GitBlob = $columns[5] }
        }
    }
    Stop-Environment "builds.tsv has no row $CorpusId"
}

# 把构建取到 <work>/src，返回构建根目录与来源记录。
function Get-BuildSource([string] $Id, $Entry, [string] $Work) {
    $extract = Join-Path $Work 'src'
    New-Item -ItemType Directory -Path $extract | Out-Null
    $corpus = Get-CorpusBuild $Entry.Corpus
    if ($corpus.Source -match '^repo:(?<zip>[^!]+)!') {
        $zipPath = $Matches['zip']
        $zip = Join-Path $ModRoot $zipPath
        if (-not (Test-Path -LiteralPath $zip)) { Stop-Environment "build $Id is missing: $zip" }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
        $roots = @(Get-ChildItem -LiteralPath $extract -Directory)
        $root = if ($roots.Count -eq 1 -and -not (Test-Path -LiteralPath (Join-Path $extract 'About'))) { $roots[0].FullName } else { $extract }
        $source = [ordered]@{ kind = 'zip'; tier = $corpus.Tier; corpus = $corpus.Id; path = $zipPath; zipSha256 = Get-Sha256 $zip; note = $Entry.Note }
    }
    elseif ($corpus.Source -match '^git:(?<rev>[0-9a-f]+):') {
        $Entry.Rev = (& git -C $ModRoot rev-parse $Matches['rev']).Trim()
        if ($LASTEXITCODE -ne 0) { Stop-Environment "build ${Id}: git cannot resolve $($Matches['rev'])" }
        $blob = (& git -C $ModRoot rev-parse "$($Entry.Rev):1.6/Assemblies/MugirlRace.dll").Trim()
        if ($LASTEXITCODE -ne 0 -or $blob -ne $corpus.GitBlob) { Stop-Environment "build ${Id}: the DLL blob of $($Entry.Rev) is '$blob', builds.tsv says $($corpus.GitBlob)" }
        $archive = Join-Path $Work 'src.zip'
        $paths = @(& git -C $ModRoot ls-tree --name-only $Entry.Rev | Where-Object { $_ -in @('About', 'LoadFolders.xml') + $ContentFolders })
        & git -C $ModRoot archive --format=zip -o $archive $Entry.Rev -- @paths
        if ($LASTEXITCODE -ne 0) { Stop-Environment "git archive $($Entry.Rev) failed" }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $extract)
        Remove-Item -LiteralPath $archive -Force
        $root = $extract
        $commit = (& git -C $ModRoot log -1 --format='%H %s' $Entry.Rev).Trim()
        $source = [ordered]@{ kind = 'git'; tier = $corpus.Tier; corpus = $corpus.Id; rev = $Entry.Rev; commit = $commit; dllBlob = $blob; note = $Entry.Note }
    }
    else { Stop-Environment "build ${Id}: unsupported builds.tsv source $($corpus.Source)" }
    $dll = Join-Path $root '1.6\Assemblies\MugirlRace.dll'
    if (-not (Test-Path -LiteralPath $dll)) { Stop-Environment "build ${Id} has no 1.6/Assemblies/MugirlRace.dll" }
    $source.dllSha256 = Get-Sha256 $dll
    if ($source.dllSha256 -ne $corpus.Sha256) { Stop-Environment "build ${Id}: MugirlRace.dll has SHA-256 $($source.dllSha256), builds.tsv says $($corpus.Sha256)" }
    $workshop = Join-Path $GameDir "..\..\$WorkshopDll"
    if (Test-Path -LiteralPath $workshop) { $source.equalsWorkshopDll = ($source.dllSha256 -eq (Get-Sha256 $workshop)) }
    return @{ Root = $root; Source = $source }
}

# 布置临时 Mod：只复制、不建联接；About 与 LoadFolders 改写；Assemblies 只带 MugirlRace.dll。
function Install-LegacyBuild([string] $Id, $Entry, [string] $Root) {
    $packageId = "mugirl.legacy.$($Entry.Key)"
    foreach ($stale in Get-ChildItem -LiteralPath $ModsDir -Directory -Filter "$TempModPrefix$($Entry.Key)_*" -Force) {
        if (-not (Remove-MarkedDirectory $stale.FullName $TempMarker)) { Stop-Environment "a stale temporary build could not be removed safely: $($stale.FullName)" }
    }
    $folder = Assert-Under (Join-Path $ModsDir "$TempModPrefix$($Entry.Key)_$([guid]::NewGuid().ToString('N').Substring(0, 8))") $ModsDir
    New-MarkedDirectory $folder $TempMarker

    $aboutDir = Join-Path $folder 'About'
    New-Item -ItemType Directory -Path $aboutDir | Out-Null
    $about = [xml](Get-Content -LiteralPath (Join-Path $Root 'About\About.xml') -Raw -Encoding utf8)
    $meta = $about.ModMetaData
    $meta.packageId = $packageId
    $meta.name = "Mugirl Race $Id (legacy replay, test only)"
    $versions = $meta.SelectSingleNode('supportedVersions')
    if (-not ($versions.SelectNodes('li') | Where-Object { $_.InnerText.Trim() -eq '1.7' })) {
        $li = $about.CreateElement('li'); $li.InnerText = '1.7'; [void]$versions.AppendChild($li)
    }
    $about.Save((Join-Path $aboutDir 'About.xml'))

    $loadFolders = [xml](Get-Content -LiteralPath (Join-Path $Root 'LoadFolders.xml') -Raw -Encoding utf8)
    $v16 = $loadFolders.DocumentElement.SelectSingleNode('v1.6')
    if ($null -eq $v16) { Stop-Environment "build $Id has no v1.6 list in LoadFolders.xml" }
    $existing = $loadFolders.DocumentElement.SelectSingleNode('v1.7')
    if ($null -ne $existing) { [void]$loadFolders.DocumentElement.RemoveChild($existing) }
    $v17 = $loadFolders.CreateElement('v1.7')
    foreach ($li in $v16.SelectNodes('li')) { [void]$v17.AppendChild($li.Clone()) }
    [void]$loadFolders.DocumentElement.AppendChild($v17)
    $loadFolders.Save((Join-Path $folder 'LoadFolders.xml'))

    foreach ($name in $ContentFolders) {
        $from = Join-Path $Root $name
        if (Test-Path -LiteralPath $from) { Copy-Tree $from (Join-Path $folder $name) @('Source', 'Assemblies', 'obj', 'bin') }
    }
    $assemblies = Join-Path $folder '1.6\Assemblies'
    New-Item -ItemType Directory -Force -Path $assemblies | Out-Null
    Copy-Item -LiteralPath (Join-Path $Root '1.6\Assemblies\MugirlRace.dll') -Destination $assemblies
    $unexpected = @(Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.dll' | Where-Object { $_.FullName -ne (Join-Path $assemblies 'MugirlRace.dll') })
    if ($unexpected.Count -gt 0) { Stop-Environment "the temporary build carries other assemblies: $($unexpected.FullName -join ', ')" }
    return @{ Folder = $folder; PackageId = $packageId }
}

# 运行 Test-InGame（子进程，继承锁令牌），返回结果目录与摘要。
function Invoke-InGame([string] $Name, [string] $Work, [string[]] $Arguments) {
    $output = Join-Path $Work $Name
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $log = Join-Path $Work "$Name.log"
    Write-Host "[$Name] Test-InGame $($Arguments -join ' ')" -ForegroundColor Cyan
    & (Get-Process -Id $PID).Path -NoProfile -File $TestInGame @Arguments -OutputRoot $output *>&1 | Tee-Object -FilePath $log | Out-Host
    $exit = $LASTEXITCODE
    $result = @(Get-ChildItem -LiteralPath $output -Directory | Sort-Object Name | Select-Object -Last 1)
    $folder = if ($result.Count -eq 1) { $result[0].FullName } else { $null }
    $summary = if ($folder -and (Test-Path -LiteralPath (Join-Path $folder 'summary.json'))) { Get-Content -LiteralPath (Join-Path $folder 'summary.json') -Raw -Encoding utf8 | ConvertFrom-Json } else { $null }
    $report = if ($folder -and (Test-Path -LiteralPath (Join-Path $folder 'report.txt'))) { @(Get-Content -LiteralPath (Join-Path $folder 'report.txt') -Encoding utf8) } else { @() }
    $lines = @($report | Where-Object { $_.StartsWith('[Mugirl.GameValidation]') } | ForEach-Object { $_.Substring('[Mugirl.GameValidation]'.Length).Trim() })
    return [pscustomobject]@{
        Exit = $exit; Folder = $folder; Summary = $summary; Lines = $lines; Log = $log
        Done = [bool]($lines | Where-Object { $_ -match '^DONE ' }); Failures = @($lines | Where-Object { $_ -match '^FAIL ' })
    }
}

# meta/modIds 与 modNames 中的临时 packageId 换成 har.mugirlrace / Mugirl Race（同一位置，顺序不变）。只改 meta，其余字节不动。
function Convert-SaveMeta([string] $From, [string] $To, [string] $PackageId) {
    $text = [System.IO.File]::ReadAllText($From, $Utf8)
    $start = $text.IndexOf('<meta>', [System.StringComparison]::Ordinal)
    $end = $text.IndexOf('</meta>', [System.StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -lt $start) { Stop-Environment "$From has no <meta> block" }
    $end += '</meta>'.Length
    $meta = [System.Xml.XmlDocument]::new()
    $meta.PreserveWhitespace = $true
    $meta.LoadXml($text.Substring($start, $end - $start))
    $ids = @($meta.DocumentElement.SelectNodes('modIds/li'))
    $names = @($meta.DocumentElement.SelectNodes('modNames/li'))
    $index = -1
    for ($i = 0; $i -lt $ids.Count; $i++) { if ($ids[$i].InnerText.Trim() -eq $PackageId.ToLowerInvariant()) { $index = $i } }
    if ($index -lt 0) { Stop-Environment "$From does not list $PackageId in meta/modIds" }
    $before = [ordered]@{ modId = $ids[$index].InnerText; modName = if ($index -lt $names.Count) { $names[$index].InnerText } else { $null } }
    $ids[$index].InnerText = 'har.mugirlrace'
    if ($index -lt $names.Count) { $names[$index].InnerText = 'Mugirl Race' }
    $rewritten = $text.Substring(0, $start) + $meta.DocumentElement.OuterXml + $text.Substring($end)
    [System.IO.File]::WriteAllText($To, $rewritten, $Utf8)
    $residual = ([regex]::Matches($rewritten, [regex]::Escape($PackageId), 'IgnoreCase')).Count
    return [ordered]@{
        index = $index; from = $before; to = [ordered]@{ modId = 'har.mugirlrace'; modName = 'Mugirl Race' }
        residualOccurrences = $residual; modIds = @($meta.DocumentElement.SelectNodes('modIds/li') | ForEach-Object { $_.InnerText })
    }
}

function Compress-Save([string] $From, [string] $To) {
    $source = [System.IO.File]::OpenRead($From)
    try {
        $target = [System.IO.File]::Create($To)
        try {
            $gzip = [System.IO.Compression.GZipStream]::new($target, [System.IO.Compression.CompressionLevel]::SmallestSize)
            try { $source.CopyTo($gzip) }
            finally { $gzip.Dispose() }
        }
        finally { $target.Dispose() }
    }
    finally { $source.Dispose() }
}

function Invoke-Fixture([string] $Id, $Entry) {
    $fixtureId = "P-$Id"
    $fixtureDir = Join-Path $FixtureRoot $fixtureId
    if ((Test-Path -LiteralPath (Join-Path $fixtureDir 'manifest.json')) -and -not $Force -and -not $GateOnly) {
        Write-Host "fixture $fixtureId exists; use -Force to produce it again" -ForegroundColor DarkYellow
        return 'exists'
    }
    $work = Assert-Under (Join-Path $WorkRoot "$Stamp\$($Entry.Key)") $WorkRoot
    New-Item -ItemType Directory -Force -Path (Split-Path $work) | Out-Null
    New-MarkedDirectory $work $WorkMarker
    $staged = $null
    try {
        Write-Host ''
        Write-Host "==== build $Id ($($Entry.Note))" -ForegroundColor Cyan
        $built = Get-BuildSource $Id $Entry $work
        $staged = Install-LegacyBuild $Id $Entry $built.Root
        Write-Host "staged $($staged.PackageId) in $($staged.Folder) (MugirlRace.dll SHA-256 $($built.Source.dllSha256))"
        $common = @('-TargetModDir', $staged.Folder, '-Config', $Config, '-NoScreenshots') + @(if ($KeepWorkDir) { '-KeepWorkDir' })

        # 可行性闸门
        $gate = Invoke-InGame 'gate' $work ($common + @('-Suite', 'plumbing,fixture-gate', '-TimeoutSeconds', '900'))
        # 退出码 2：构建或环境问题（例如本安装的 RimWorld 正在运行），游戏没有启动；这不是构建不可行，不写清单。
        if ($gate.Exit -eq 2) { Stop-Environment "the gate run of $Id stopped before the game ran (exit 2); see $($gate.Log)" }
        $gateJson = if ($gate.Folder -and (Test-Path -LiteralPath (Join-Path $gate.Folder 'artifacts\gate.json'))) {
            Get-Content -LiteralPath (Join-Path $gate.Folder 'artifacts\gate.json') -Raw -Encoding utf8 | ConvertFrom-Json
        } else { $null }
        # 阻断：快速测试开局、计时、存读档往返或程序集识别本身失败。步骤中的日志失败（"…: log: …"）是旧构建在 1.7 上的问题，只记录。
        $blocking = @($gate.Failures | Where-Object { $_ -match '^FAIL (setup|session|plumbing/(environment|time|save-load)|fixture-gate/identity): (?!log: )' })
        $reason = if (-not $gate.Done) { 'the gate boot did not finish (no DONE line; crash, hang or timeout)' }
        elseif ($blocking.Count -gt 0) { "blocking failures: $($blocking -join ' | ')" }
        elseif ($null -eq $gateJson) { 'the gate wrote no gate.json' }
        else { $null }
        $feasible = $null -eq $reason
        Write-Host "gate: $(if ($feasible) { 'feasible' } else { "NOT feasible: $reason" })" -ForegroundColor $(if ($feasible) { 'Green' } else { 'Red' })
        $manifest = [ordered]@{
            format = 1; id = $fixtureId; kind = 'P'; feasible = $feasible; producedAt = (Get-Date).ToString('o'); producer = 'tools/New-SaveFixture.ps1'
            config = $Config; language = 'English'; build = $Id; source = $built.Source; packageId = $staged.PackageId
            gate = [ordered]@{ verdict = $(if ($feasible) { 'feasible' } else { "infeasible: $reason" }); exit = $gate.Exit; failures = $gate.Failures; result = $gateJson }
        }
        if ($GateOnly -or -not $feasible) {
            if (-not $GateOnly) {
                New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
                Write-Utf8 (Join-Path $fixtureDir 'manifest.json') ($manifest | ConvertTo-Json -Depth 12)
                Write-Host "recorded the infeasible build in $fixtureDir\manifest.json"
            }
            return $(if ($feasible) { 'feasible' } else { 'infeasible' })
        }

        # 播种
        $sweep = Invoke-InGame 'sweep' $work ($common + @('-Suite', 'sweep', '-CollectSaves', '-TimeoutSeconds', "$TimeoutSeconds",
                '-SuiteOption', "sweep.ticks=$Ticks,sweep.save=$SweepSaveName,sweep.ignoreCombatSlowdown=true"))
        if ($sweep.Exit -eq 2) { Stop-Environment "the seed run of $Id stopped before the game ran (exit 2); see $($sweep.Log)" }
        $sweepJsonPath = if ($sweep.Folder) { Join-Path $sweep.Folder 'artifacts\sweep.json' } else { $null }
        $savePath = if ($sweep.Folder) { Join-Path $sweep.Folder "Saves\$SweepSaveName.rws" } else { $null }
        if (-not $sweep.Done -or -not $sweepJsonPath -or -not (Test-Path -LiteralPath $sweepJsonPath) -or -not (Test-Path -LiteralPath $savePath)) {
            $manifest.feasible = $false
            $manifest.gate.verdict = 'infeasible: the seed boot produced no save or no sweep.json' + $(if (-not $sweep.Done) { ' (no DONE line)' } else { '' })
            $manifest.seed = [ordered]@{ exit = $sweep.Exit; failures = $sweep.Failures }
            New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
            Write-Utf8 (Join-Path $fixtureDir 'manifest.json') ($manifest | ConvertTo-Json -Depth 12)
            Write-Host "the seed boot failed; recorded in $fixtureDir\manifest.json" -ForegroundColor Red
            return 'infeasible'
        }
        $result = Get-Content -LiteralPath $sweepJsonPath -Raw -Encoding utf8 | ConvertFrom-Json

        # 后处理：meta 改写、gzip、清单
        $originalSha = Get-Sha256 $savePath
        if ($originalSha -ne $result.save.sha256) { Stop-Environment "the collected save differs from the one the sweep hashed ($originalSha vs $($result.save.sha256))" }
        New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
        $rewrittenPath = Join-Path $work "$fixtureId.rws"
        $metaRewrite = Convert-SaveMeta $savePath $rewrittenPath $staged.PackageId
        $gzName = "$fixtureId.rws.gz"
        $gzPath = Join-Path $fixtureDir $gzName
        Compress-Save $rewrittenPath $gzPath
        $manifest.game = [ordered]@{ version = $result.save.gameVersion; mods = $metaRewrite.modIds }
        $manifest.build = [ordered]@{ id = $Id; packageId = $staged.PackageId; target = $result.target }
        $manifest.seed = [ordered]@{
            ticks = $result.ticks; tick = $result.save.tick; exit = $sweep.Exit; failures = $sweep.Failures
            actionSummary = $result.actionSummary; inFlight = $result.inFlight; inventory = $result.inventory; actions = $result.actions
        }
        $integrityFailures = @($sweep.Failures | Where-Object { $_ -match 'is referenced.*but is not deep-saved|Could not resolve cross-reference|Could not load reference|related pawns remain saveable' })
        $manifest.seed.integrityFailures = $integrityFailures
        if ($integrityFailures.Count -gt 0) {
            $manifest.feasible = $false
            $manifest.gate.verdict = 'infeasible: the seed save has unresolved object references'
        }
        $manifest.save = [ordered]@{
            file = $gzName; bytes = (Get-Item -LiteralPath $rewrittenPath).Length; gzBytes = (Get-Item -LiteralPath $gzPath).Length
            sha256 = Get-Sha256 $rewrittenPath; sha256Gz = Get-Sha256 $gzPath; originalSha256 = $originalSha; metaRewrite = $metaRewrite
        }
        $manifest.modDefs = $result.modDefs
        $manifest.modThingDefs = $result.modThingDefs
        $manifest.modDefsByRoot = $result.modDefsByRoot
        $manifest.restraintDefaults = $result.restraintDefaults
        $manifest.coverage = $result.coverage
        $manifest.coverageMissing = @($result.coverageMissing)
        if ($manifest.coverageMissing.Count -gt 0) { $manifest.feasible = $false }
        $manifest.facts = $result.facts
        Write-Utf8 (Join-Path $fixtureDir 'manifest.json') ($manifest | ConvertTo-Json -Depth 12)
        Write-Host ("fixture {0}: {1:N0} KiB save, {2:N0} KiB gzip, {3} facts, {4} mod job(s) in flight" -f $fixtureId, ($manifest.save.bytes / 1KB),
            ($manifest.save.gzBytes / 1KB), @($result.facts.PSObject.Properties).Count, @($result.inFlight).Count) -ForegroundColor Green
        $gaps = @($result.coverageMissing)
        if ($integrityFailures.Count -gt 0) {
            Write-Host "fixture ${fixtureId}: object-reference integrity failures: $($integrityFailures -join ' | ')" -ForegroundColor Red
            return 'produced with integrity failures'
        }
        if ($gaps.Count -gt 0) {
            Write-Host "fixture ${fixtureId}: coverage gaps: $($gaps -join ', ')" -ForegroundColor Red
            return "produced with coverage gaps: $($gaps -join ', ')"
        }
        return 'produced'
    }
    finally {
        if ($staged -and -not (Remove-MarkedDirectory $staged.Folder $TempMarker)) { Write-Host "WARNING: remove the temporary build by hand: $($staged.Folder)" -ForegroundColor Yellow }
        if (-not $KeepWorkDir) {
            # Test-InGame 在失败时保留它的工作目录（%TEMP%\mugirl17-ingame\<时间戳>）；结果已复制到本脚本的工作目录，这里一并清理。
            $ingameRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl17-ingame'
            foreach ($run in @('gate', 'sweep')) {
                $log = Join-Path $work "$run.log"
                if (Test-Path -LiteralPath $log) {
                    foreach ($match in Select-String -LiteralPath $log -Pattern 'work folder: (.+mugirl17-ingame\\\d{8}-\d{6})\s*$') {
                        $kept = Assert-Under $match.Matches[0].Groups[1].Value.Trim() $ingameRoot
                        [void](Remove-MarkedDirectory $kept '.mugirl-gamevalidation-work')
                    }
                }
            }
            [void](Remove-MarkedDirectory $work $WorkMarker)
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------------------------------------------

$exitCode = 2
$lock = $null
$outcomes = [ordered]@{}
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $ids = @($Build | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($ids -contains 'all') { $ids = @($Catalog.Keys) }
    foreach ($id in $ids) { if (-not $Catalog.Contains($id)) { Stop-Environment "unknown build $id (known: $($Catalog.Keys -join ', '))" } }
    # 只看本安装的 RimWorld（与 tools/Test-InGame.ps1 相同）：其他游戏目录中的进程不锁本 Mod 的文件。
    $gameExe = [System.IO.Path]::GetFullPath((Join-Path $GameDir 'RimWorld.exe'))
    $running = @(Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe' OR Name = 'RimWorldWin64.exe'" -ErrorAction SilentlyContinue | Where-Object {
            -not $_.ExecutablePath -or [string]::Equals([System.IO.Path]::GetFullPath($_.ExecutablePath), $gameExe, [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($running.Count -gt 0) { Stop-Environment "RimWorld is running (PID $($running.ProcessId -join ', ')); close it first" }
    $lock = Enter-InGameLock -Label "New-SaveFixture $Stamp" -WaitSeconds $LockWaitSeconds
    New-Item -ItemType Directory -Force -Path $WorkRoot, $FixtureRoot | Out-Null
    $exitCode = 0
    foreach ($id in $ids) {
        try {
            $outcomes[$id] = Invoke-Fixture $id $Catalog[$id]
            if ($outcomes[$id] -like 'produced with coverage gaps*' -or $outcomes[$id] -eq 'produced with integrity failures') { $exitCode = 1 }
        }
        catch {
            $outcomes[$id] = "error: $($_.Exception.Message)"
            Write-Host "build ${id}: $($_.Exception.Message)" -ForegroundColor Red
            $exitCode = 1
        }
    }
}
catch [EnvironmentProblem] {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}
finally {
    Exit-InGameLock $lock
    $stampDir = Join-Path $WorkRoot $Stamp
    if ((Test-Path -LiteralPath $stampDir) -and -not (Get-ChildItem -LiteralPath $stampDir -Force)) { Remove-Item -LiteralPath $stampDir -Force }
}

Write-Host ''
Write-Host '==== fixtures'
foreach ($pair in $outcomes.GetEnumerator()) { Write-Host "  $($pair.Key): $($pair.Value)" }
exit $exitCode
