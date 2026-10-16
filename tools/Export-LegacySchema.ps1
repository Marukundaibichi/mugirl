<#
.SYNOPSIS
    提取旧存档名字语料（Source/Tests/MugirlRace.Validation/Baselines/legacy/），或用 -Verify 逐字节核对。

.DESCRIPTION
    语料是 C.save-upgrade 的输入：每个旧构建写进存档的类型、Scribe 标签、枚举、defName 等，以及真实存档中出现过的 Mugirl 名字。
    本脚本只负责收集输入并交给验证程序的 --extract-legacy-schema 模式：

      published（必需）  DevData/Releases/*.zip（有 .sha256 旁注时先核对）、创意工坊 3578882219、main 上改动过
                         1.6/Assemblies/MugirlRace.dll 或 MooGirlRace.dll 的每个提交
      observed（必需）   用户存档目录与 DevData/Evidence/**/Saves 中的 *.rws（只读，流式解析）
      dev（提示）        其他 Git 对象中的 Mugirl 程序集（含 refs/stash、refs/codex/*、WRace/obj）、DevData/Backups/**、
                         冻结的工作树 1.6 DLL 及其内容
      info               重构前 1.7 快照（DevData/Backups/PreRefactor-1.7-20261015）

    对仓库严格只读：只用 git rev-list / ls-tree / cat-file / log / archive，Git 内容导出到 -WorkRoot 下带标记文件的临时目录，
    结束时只删除这些带标记、且不含目录链接的临时目录。从不 checkout、stash 或改动工作树，不写用户存档与配置。

    语料对同一提取器版本的输入只追加：重新提取不会删除已有的行；不再存在的输入（被覆盖的自动存档、更新了的创意工坊版本）的行
    保留，同一事实的层级取并集。提取器版本改变时（派生规则修正）必须用 -Rewrite 重写并审阅差异。
    -Verify 只用 builds.tsv 中记录过的输入（新输入只报告），要求它们的 sha256 不变，并且重新提取（有输入不在时与已有行合并）
    与已提交的文件逐字节相同。

    存档以 sha256 命名（o.<sha8>，来源 saves:<sha8>.rws），文件名不进语料；创意工坊版本的 id 含 sha256（每次更新是新输入）。
    ZIP 条目时间没有时区，按 -ZipTimeZone 解释，结果不随本机时区变化。

.PARAMETER WorkRoot
    临时目录（默认 %TEMP%\mg17-legacy）。导出的 Git 内容与解压的 ZIP 放在其下的 inputs 中。

.PARAMETER Out
    语料目录（默认 Source/Tests/MugirlRace.Validation/Baselines/legacy）。

.PARAMETER Verify
    不写文件，核对现有语料能被逐字节复现。

.PARAMETER Rewrite
    不与现有语料合并，按可用的输入重新生成（提取器版本改变后使用；审阅差异后提交）。

.PARAMETER ZipTimeZone
    发布 ZIP 条目时间所用的时区（默认 China Standard Time，发布包在该时区打包）。

.PARAMETER Ref16
    RimWorld 1.6 参考程序集目录（分号分隔）。第一个含完整 Assembly-CSharp.dll（带方法体）的目录作为 1.6 游戏，其余目录中的程序集
    （HAR、Facial Animation、Harmony 2）用来解析旧 XML 中的类名。默认使用本机已知的位置。

.PARAMETER SavesDir
    用户存档目录（默认 %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves）。只读。

.PARAMETER Validator
    已构建的 MugirlRace.Validation.exe；省略时构建到 -WorkRoot\validator。

.PARAMETER KeepWorkDir
    保留导出的输入（调试用）。

.EXAMPLE
    pwsh tools/Export-LegacySchema.ps1
.EXAMPLE
    pwsh tools/Export-LegacySchema.ps1 -Verify
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $WorkRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'mg17-legacy'),
    [string] $Out = '',
    [switch] $Verify,
    [switch] $Rewrite,
    [string] $ZipTimeZone = 'China Standard Time',
    [string] $Ref16 = '',
    [string] $SavesDir = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves'),
    [string] $Validator = '',
    [switch] $KeepWorkDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$modRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$gameDir = (Resolve-Path -LiteralPath (Join-Path $modRoot '..\..')).Path
$workshop = [System.IO.Path]::GetFullPath((Join-Path $gameDir '..\..\workshop\content\294100'))
$validationDir = Join-Path $modRoot 'Source\Tests\MugirlRace.Validation'
if (-not $Out) { $Out = Join-Path $validationDir 'Baselines\legacy' }
$marker = '.mugirl-legacy-tmp'
if ($Verify -and $Rewrite) { throw '-Verify and -Rewrite exclude each other.' }
$zipZone = [TimeZoneInfo]::FindSystemTimeZoneById($ZipTimeZone)

if (-not $Ref16) {
    $candidates = @(
        (Join-Path $env:USERPROFILE 'source\repos\ExtraWeapon2\ExtraWeapon2\bin\Release'),
        (Join-Path $workshop '839005762\1.6\Assemblies'),
        (Join-Path $workshop '1635901197\1.6\Assemblies'),
        (Join-Path $workshop '2009463077\1.6\Assemblies'))
    $Ref16 = ($candidates | Where-Object { Test-Path -LiteralPath $_ }) -join ';'
}

# 只在 %TEMP% 内、带标记且不含目录链接时才删除（不跟随链接）。
function Remove-MarkedDirectory([string] $Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full)) { return }
    if (-not $full.StartsWith([System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove $full (outside %TEMP%)."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $full $marker))) { throw "Refusing to remove $full (no $marker marker)." }
    $links = @(Get-ChildItem -LiteralPath $full -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue)
    if ($links.Count -gt 0) { throw "Refusing to remove $full (contains links: $($links[0].FullName))." }
    Remove-Item -LiteralPath $full -Recurse -Force
}

function New-MarkedDirectory([string] $Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Content -LiteralPath (Join-Path $Path $marker) -Value 'temporary legacy-corpus input (tools/Export-LegacySchema.ps1)' -Encoding utf8
    return $Path
}

function Get-Utc([datetime] $Time) { return $Time.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) }

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $modRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed ($LASTEXITCODE)" }
    return $output
}

# git archive 只导出 XML 与 Mugirl 程序集（贴图与音效与存档名字无关）。
function Export-GitTree([string] $Treeish, [string] $Directory) {
    New-MarkedDirectory $Directory | Out-Null
    $tar = Join-Path $Directory 'tree.tar'
    & git -C $modRoot archive --format=tar -o $tar $Treeish -- ':(glob)**/*.xml' ':(glob)**/*Race.dll' 2>$null
    if ($LASTEXITCODE -ne 0) {
        # 没有匹配的路径时 git archive 失败：只导出 XML。
        & git -C $modRoot archive --format=tar -o $tar $Treeish -- ':(glob)**/*.xml'
        if ($LASTEXITCODE -ne 0) { throw "git archive $Treeish failed" }
    }
    & tar -xf $tar -C $Directory
    if ($LASTEXITCODE -ne 0) { throw "tar -xf $tar failed" }
    Remove-Item -LiteralPath $tar
}

$inputsDir = Join-Path $WorkRoot 'inputs'
if (Test-Path -LiteralPath $inputsDir) { Remove-MarkedDirectory $inputsDir }
New-MarkedDirectory $WorkRoot | Out-Null
New-MarkedDirectory $inputsDir | Out-Null
$rows = [System.Collections.Generic.List[string]]::new()
function Add-Input([string] $Id, [string] $Tier, [string] $Kind, [string] $Source, [string] $File, [string] $Root, [string] $First, [string] $Last) {
    $rows.Add(($Id, $Tier, $Kind, $Source, $(if ($File) { $File } else { '-' }), $(if ($Root) { $Root } else { '-' }), $First, $Last) -join "`t")
}

# 1. 发布 ZIP（核对旁注 sha256）。
$releases = Join-Path $modRoot 'DevData\Releases'
foreach ($zipFile in Get-ChildItem -LiteralPath $releases -Filter *.zip | Sort-Object Name) {
    $sidecar = "$($zipFile.FullName).sha256"
    if (Test-Path -LiteralPath $sidecar) {
        $expected = ((Get-Content -LiteralPath $sidecar -Raw).Trim() -split '\s+')[0]
        $actual = (Get-FileHash -LiteralPath $zipFile.FullName -Algorithm SHA256).Hash
        if ($expected -ne $actual) { throw "$($zipFile.Name): sha256 $actual does not match the sidecar $expected" }
    }
    $name = [System.IO.Path]::GetFileNameWithoutExtension($zipFile.Name) -replace '^雪牛娘', ''
    $dir = New-MarkedDirectory (Join-Path $inputsDir "p.zip.$name")
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipFile.FullName)
    try {
        $dllEntry = $null
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/') -or -not ($entry.FullName -match '\.xml$' -or $entry.FullName -match 'Race\.dll$')) { continue }
            $target = [System.IO.Path]::GetFullPath((Join-Path $dir $entry.FullName))
            if (-not $target.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase)) { throw "$($zipFile.Name): entry escapes the folder: $($entry.FullName)" }
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            if ($entry.FullName -match '/1\.6/Assemblies/(Mugirl|MooGirl)Race\.dll$') { $dllEntry = $entry }
        }
        if (-not $dllEntry) { throw "$($zipFile.Name): no 1.6/Assemblies/*Race.dll" }
        $top = $dllEntry.FullName.Split('/')[0]
        # ZIP 条目时间是打包机器的本地时间，没有时区：按 -ZipTimeZone 解释，不用本机时区。
        $local = [datetime]::SpecifyKind($dllEntry.LastWriteTime.DateTime, [DateTimeKind]::Unspecified)
        $time = Get-Utc ([TimeZoneInfo]::ConvertTimeToUtc($local, $zipZone))
        Add-Input "p.zip.$name" 'published' 'dll+content' "repo:DevData/Releases/$($zipFile.Name)!$($dllEntry.FullName)" (Join-Path $dir $dllEntry.FullName) (Join-Path $dir $top) $time $time
    }
    finally { $zip.Dispose() }
}

# 2. 创意工坊发布版（只读，原地）。
$ws = Join-Path $workshop '3578882219'
$wsDll = Join-Path $ws '1.6\Assemblies\MugirlRace.dll'
if (Test-Path -LiteralPath $wsDll) {
    $time = Get-Utc (Get-Item -LiteralPath $wsDll).LastWriteTimeUtc
    # 创意工坊条目随更新改变内容：id 含 sha256，旧版本作为不再存在的输入保留在语料中。
    $wsSha = (Get-FileHash -LiteralPath $wsDll -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
    Add-Input "p.ws.3578882219.$wsSha" 'published' 'dll+content' 'workshop:3578882219/1.6/Assemblies/MugirlRace.dll' $wsDll $ws $time $time
}

# 3. main 上的发布提交：改动过 Mugirl 程序集的每个提交。
$published = @{}
$releaseCommits = @(Invoke-Git @('rev-list', '--reverse', 'main', '--', '1.6/Assemblies/MugirlRace.dll', '1.6/Assemblies/MooGirlRace.dll'))
foreach ($commit in $releaseCommits) {
    $short = $commit.Substring(0, 7)
    $time = Get-Utc ([datetime]::Parse((Invoke-Git @('log', '-1', '--format=%cI', $commit)), [Globalization.CultureInfo]::InvariantCulture))
    $dir = Join-Path $inputsDir "p.git.$short"
    Export-GitTree $commit $dir
    $dll = @('1.6/Assemblies/MugirlRace.dll', '1.6/Assemblies/MooGirlRace.dll') | Where-Object { Test-Path -LiteralPath (Join-Path $dir $_) } | Select-Object -First 1
    if (-not $dll) { continue }
    $published[(Invoke-Git @('rev-parse', "${commit}:$dll"))] = $true
    Add-Input "p.git.$short" 'published' 'dll+content' "git:${short}:$dll" (Join-Path $dir $dll) $dir $time $time
}

# 4. 其他 Git 对象中的 Mugirl 程序集（全部提交，含 stash 与 codex 树引用）：dev。按 blob 去重，内容取首次出现的提交或树。
# 树引用（refs/codex/turn-diffs/checkpoints/.../<毫秒时间戳>/...）没有提交时间，取引用名中的时间戳。
$treeTimes = @{}
foreach ($line in @(Invoke-Git @('for-each-ref', '--format=%(objectname) %(objecttype) %(refname)'))) {
    $parts = $line -split ' '
    if ($parts[1] -ne 'tree') { continue }
    $time = if ($parts[2] -match '/(\d{13})/') { Get-Utc ([DateTimeOffset]::FromUnixTimeMilliseconds([long]$Matches[1]).UtcDateTime) } else { '-' }
    if (-not $treeTimes.ContainsKey($parts[0]) -or $time -lt $treeTimes[$parts[0]]) { $treeTimes[$parts[0]] = $time }
}
$treeRefs = @($treeTimes.Keys | Sort-Object)
$commits = @(Invoke-Git @('rev-list', '--all', '--reverse', '--date-order'))
$devBlobs = [ordered]@{}
foreach ($treeish in @($commits) + @($treeRefs)) {
    foreach ($line in @(Invoke-Git @('ls-tree', '-r', $treeish))) {
        if ($line -notmatch '^\d+ blob ([0-9a-f]{40})\t(.+)$') { continue }
        $blob = $Matches[1]; $path = $Matches[2]
        if ($path -notmatch '(^|/)(MugirlRace|MooGirlRace|WRace)\.dll$') { continue }
        if ($published.ContainsKey($blob) -or $devBlobs.Contains($blob)) { continue }
        $devBlobs[$blob] = [pscustomobject]@{ Treeish = $treeish; Path = $path }
    }
}
foreach ($blob in $devBlobs.Keys) {
    $item = $devBlobs[$blob]
    $id = "d.git.$($blob.Substring(0, 8))"
    $dir = Join-Path $inputsDir $id
    $kind = if ($item.Path -like '1.6/Assemblies/*') { 'dll+content' } else { 'dll' }
    if ($kind -eq 'dll+content') { Export-GitTree $item.Treeish $dir } else { New-MarkedDirectory $dir | Out-Null }
    # cmd 的重定向按字节写入（PowerShell 管道会按文本处理二进制）。
    $dllFile = Join-Path $dir 'build.dll'
    & cmd /c "git -C `"$modRoot`" cat-file blob $blob > `"$dllFile`""
    if ($LASTEXITCODE -ne 0) { throw "git cat-file blob $blob failed" }
    $isCommit = (Invoke-Git @('cat-file', '-t', $item.Treeish)) -eq 'commit'
    $time = if ($isCommit) { Get-Utc ([datetime]::Parse((Invoke-Git @('log', '-1', '--format=%cI', $item.Treeish)), [Globalization.CultureInfo]::InvariantCulture)) } else { $treeTimes[$item.Treeish] }
    $root = if ($kind -eq 'dll+content') { $dir } else { '' }
    Add-Input $id 'dev' $kind "git:$($item.Treeish.Substring(0, 7)):$($item.Path)" $dllFile $root $time $time
}

# 5. DevData/Backups 中的程序集（按 sha256 去重；重构前快照为 info）。
$seenBackups = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $modRoot 'DevData\Backups') -Recurse -File -Attributes !ReparsePoint |
        Where-Object { $_.Name -match '^(MugirlRace|MooGirl).*\.dll' } | Sort-Object FullName) {
    $sha = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($seenBackups.ContainsKey($sha)) { continue }
    $seenBackups[$sha] = $true
    $relative = [System.IO.Path]::GetRelativePath($modRoot, $file.FullName).Replace('\', '/')
    $tier = if ($relative -like 'DevData/Backups/PreRefactor-1.7-*') { 'info' } else { 'dev' }
    $time = Get-Utc $file.LastWriteTimeUtc
    Add-Input "d.bak.$($sha.Substring(0, 8).ToLowerInvariant())" $tier 'dll' "repo:$relative" $file.FullName '' $time $time
}

# 6. 冻结的工作树 1.6 构建与其内容（只读，原地）。
$wt16 = Join-Path $modRoot '1.6\Assemblies\MugirlRace.dll'
if (Test-Path -LiteralPath $wt16) {
    $time = Get-Utc (Get-Item -LiteralPath $wt16).LastWriteTimeUtc
    Add-Input 'd.wt16' 'dev' 'dll+content' 'repo:1.6/Assemblies/MugirlRace.dll' $wt16 $modRoot $time $time
}

# 7. 真实存档（只读；没有本 Mod 的存档由提取器跳过）。
$saves = [System.Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $SavesDir) {
    # 用户存档的文件名不进语料（来源只记 sha256）。
    Get-ChildItem -LiteralPath $SavesDir -Filter *.rws -File | ForEach-Object { $saves.Add([pscustomobject]@{ File = $_; Source = 'saves:' }) }
}
Get-ChildItem -LiteralPath (Join-Path $modRoot 'DevData\Evidence') -Recurse -Filter *.rws -File -Attributes !ReparsePoint |
    Where-Object { $_.FullName -match '\\Saves\\' } |
    ForEach-Object { $saves.Add([pscustomobject]@{ File = $_; Source = 'repo:' + [System.IO.Path]::GetRelativePath($modRoot, $_.FullName).Replace('\', '/') }) }
$seenSaves = @{}
foreach ($save in $saves | Sort-Object { $_.Source }, { $_.File.FullName }) {
    $sha = (Get-FileHash -LiteralPath $save.File.FullName -Algorithm SHA256).Hash
    if ($seenSaves.ContainsKey($sha)) { continue }
    $seenSaves[$sha] = $true
    $time = Get-Utc $save.File.LastWriteTimeUtc
    $sha8 = $sha.Substring(0, 8).ToLowerInvariant()
    $source = if ($save.Source -eq 'saves:') { "saves:$sha8.rws" } else { $save.Source }
    Add-Input "o.$sha8" 'observed' 'save' $source $save.File.FullName '' $time $time
}

$inputsFile = Join-Path $WorkRoot 'inputs.tsv'
[System.IO.File]::WriteAllText($inputsFile, "# id`ttier`tkind`tsource`tfile`troot`tfirstSeen`tlastSeen`n" + (($rows | Sort-Object) -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Collected $($rows.Count) input(s) into $inputsFile"

# 验证程序：构建到工作目录（不写 Mod 目录），或用 -Validator 指定。
if (-not $Validator) {
    $artifacts = Join-Path $WorkRoot 'validator'
    & dotnet build (Join-Path $validationDir 'MugirlRace.Validation.csproj') -c Release --nologo -v:minimal --artifacts-path ($artifacts.Replace('\', '/'))
    if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED: validator'; exit 2 }
    $Validator = Join-Path $artifacts 'bin\MugirlRace.Validation\release\MugirlRace.Validation.exe'
}

$arguments = @('--extract-legacy-schema', '--inputs', $inputsFile, '--out', $Out, '--ref16', $Ref16, '--mod-root', $modRoot, '--game', $gameDir, '--workshop', $workshop)
if ($Verify) { $arguments += '--verify' }
if ($Rewrite) { $arguments += '--rewrite' }
& $Validator @arguments
$exit = $LASTEXITCODE

if (-not $KeepWorkDir) { Remove-MarkedDirectory $inputsDir }
exit $exit
