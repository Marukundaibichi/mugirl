#Requires -Version 7.0
<#
.SYNOPSIS
    在 Mod 目录之外暂存一份干净的发布副本，写清单并独立复核。

.DESCRIPTION
    开发目录同时就是 Mod 目录。RimWorld 1.7 的游戏内创意工坊上传会复制 Mod 目录中的全部文件（DevData、TMP、.git、源码、测试……），
    所以不能从开发目录直接上传；在 Mods 中另放一份同 packageId 的副本又会被 RimWorld 忽略。本脚本（packaging_rules）：

      1. 拒绝：工作区有未提交改动（除非 -AllowDirty，记入清单）；冻结的 1.6 DLL 与 tools/release-baseline.json 不符；
         必需的 DLL/PDB 不存在，1.7 DLL 比 Source/ 中的生产构建输入旧（除非 -SkipBuildCheck）或其 PDB 不属于它；
         目标位于 Mod 目录或游戏的 Mods 目录中；目标已存在但不是之前的暂存副本。
      2. 发布文件清单由 tools/Test-ReleasePackage.ps1 -ListReleaseFiles 从 LoadFolders.xml 推导（两个脚本共用规则）。
      3. 复制到目标（默认 %LOCALAPPDATA%\MugirlRace\Release\<版本>\HAR.MugirlRace），在其旁边写
         HAR.MugirlRace.manifest.json（每个文件的大小与 SHA-256、游戏版本与所用程序集的 SHA-256、SDK 版本、git 提交）
         与 SteamCMD 用的 workshop_item.vdf。
      4. 运行 tools/Test-ReleasePackage.ps1 复核暂存目录。
      5. -Archive 时把暂存目录与清单打成 ZIP 放进 DevData/Releases（用户的归档约定），并核对条目数。

    本脚本只读开发目录，不构建、不修改仓库（-Archive 只在 DevData/Releases 新建 ZIP）。普通构建不写 1.7/Assemblies；
    发布用的 DLL 由在 Source/ 中启动的显式部署生成（Source/global.json 固定的 SDK 只在 Source/ 中生效）：
      dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true
    清单记录的 SDK 版本同样在 Source/ 中查询。

    上传：推荐用 SteamCMD 的 workshop_build_item 加载生成的 workshop_item.vdf（不烘焙 Assets.zip，订阅者首次加载时压缩贴图）。
    若用游戏内上传，必须从只含暂存内容、且是唯一一个 packageId 为 HAR.MugirlRace 的 Mod 目录上传。

    退出码：0 成功；1 复核失败；2 拒绝或环境错误。

.PARAMETER Destination
    暂存目录（最后一级通常为 HAR.MugirlRace）。默认 %LOCALAPPDATA%\MugirlRace\Release\<版本>\HAR.MugirlRace。

.PARAMETER Version
    版本标签。默认 yyyyMMdd-HHmm-<短提交号>。

.PARAMETER AllowDirty
    工作区有未提交改动时仍然暂存；清单记录 dirty=true。

.PARAMETER SkipBuildCheck
    不检查 1.7 DLL 是否比生产构建输入旧；仍要求 DLL/PDB 齐全。

.PARAMETER Archive
    另外把暂存目录与清单打包为 DevData/Releases/HAR.MugirlRace-<版本>.zip。

.EXAMPLE
    pwsh tools/New-ReleasePackage.ps1

.EXAMPLE
    pwsh tools/New-ReleasePackage.ps1 -AllowDirty -Destination "$env:TEMP\MugirlRace-Release\HAR.MugirlRace"
#>
[CmdletBinding()]
param(
    [string] $Destination,
    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string] $Version,
    [switch] $AllowDirty,
    [switch] $SkipBuildCheck,
    [switch] $Archive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ModRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$GameDir = [System.IO.Path]::GetFullPath((Join-Path $ModRoot '..\..')).TrimEnd('\')
$WorkshopDir = [System.IO.Path]::GetFullPath((Join-Path $GameDir '..\..\workshop\content\294100')).TrimEnd('\')
$BaselinePath = Join-Path $PSScriptRoot 'release-baseline.json'
$Verifier = Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1'
$DeployCommand = 'dotnet build MugirlRace/MugirlRace.csproj -c Release -p:DeployToMod=true (run in Source/)'

class EnvironmentProblem : System.Exception {
    EnvironmentProblem([string] $message) : base($message) {}
}

function Stop-Environment([string] $Message) { throw [EnvironmentProblem]::new($Message) }

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Test-Inside([string] $Path, [string] $Parent) {
    $Path.StartsWith($Parent.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase) -or $Path -ieq $Parent.TrimEnd('\')
}

function Test-ReparsePoint([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    return $null -ne $item -and (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $ModRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { Stop-Environment "git $($Arguments -join ' ') failed: $output" }
    return $output
}

function Get-OptionalHash([string] $Path) { if (Test-Path -LiteralPath $Path) { Get-Sha256 $Path } else { $null } }

$exitCode = 2
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Stop-Environment 'git is not on PATH' }
    $rules = Get-Content -LiteralPath $BaselinePath -Raw -Encoding utf8 | ConvertFrom-Json

    # 来源与新鲜度
    $commit = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
    $status = @(Invoke-Git @('status', '--porcelain'))
    $dirty = $status.Count -gt 0
    if ($dirty -and -not $AllowDirty) {
        Stop-Environment "the working tree has $($status.Count) uncommitted change(s); commit first or pass -AllowDirty (recorded in the manifest)"
    }
    if (-not $Version) { $Version = (Get-Date -Format 'yyyyMMdd-HHmm') + '-' + $commit.Substring(0, 7) + $(if ($dirty) { '-dirty' } else { '' }) }

    $frozen = Join-Path $ModRoot $rules.frozen16.path
    if (-not (Test-Path -LiteralPath $frozen)) { Stop-Environment "$($rules.frozen16.path) is missing" }
    $frozenHash = Get-Sha256 $frozen
    $frozenSize = (Get-Item -LiteralPath $frozen).Length
    if ($frozenHash -ne $rules.frozen16.sha256 -or $frozenSize -ne $rules.frozen16.size) {
        Stop-Environment "$($rules.frozen16.path) is not the frozen 1.6 build ($frozenSize bytes, SHA-256 $frozenHash; release-baseline.json expects $($rules.frozen16.size) bytes, $($rules.frozen16.sha256))"
    }

    $dll17 = Join-Path $ModRoot '1.7\Assemblies\MugirlRace.dll'
    foreach ($required in $rules.requiredAssemblies) {
        if (-not (Test-Path -LiteralPath (Join-Path $ModRoot $required) -PathType Leaf)) {
            Stop-Environment "$required is missing; build and deploy the complete Release DLL/PDB pair first: $DeployCommand"
        }
    }
    if (-not $SkipBuildCheck) {
        $dllTime = (Get-Item -LiteralPath $dll17).LastWriteTimeUtc
        # 主工程、Facade 与元数据重排工具的源码/项目/导入均影响产物；TypeOrder 是工具实际读取的文本输入。
        # global.json 固定 SDK，.editorconfig 控制生产分析器。Tests、bin、obj 的同名输入仍不参与此检查。
        $newer = Get-ChildItem -LiteralPath (Join-Path $ModRoot 'Source') -Recurse -File -Force `
            -Include '*.cs', '*.csproj', '*.props', '*.targets', 'BannedSymbols.txt', 'TypeOrder.txt', 'global.json', '.editorconfig' |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin|Tests)[\\/]' -and $_.LastWriteTimeUtc -gt $dllTime } | Select-Object -First 1
        if ($newer) { Stop-Environment "1.7/Assemblies/MugirlRace.dll is older than $($newer.FullName); rebuild and deploy ($DeployCommand), or pass -SkipBuildCheck" }
    }

    # 目标
    if (-not $Destination) { $Destination = Join-Path $env:LOCALAPPDATA "MugirlRace\Release\$Version\$($rules.packageId)" }
    $Destination = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if (Test-Inside $Destination $ModRoot) { Stop-Environment "the destination must be outside the mod folder: $Destination" }
    if (Test-Inside $Destination (Join-Path $GameDir 'Mods')) { Stop-Environment "the destination must be outside the game's Mods folder (RimWorld ignores a second copy with the same packageId): $Destination" }
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-ReparsePoint $parent) { Stop-Environment "$parent is a junction or symbolic link" }
    if (Test-Path -LiteralPath $Destination) {
        $existing = @(Get-ChildItem -LiteralPath $Destination -Force)
        if ($existing.Count -gt 0) {
            $about = Join-Path $Destination 'About\About.xml'
            $previous = (Test-Path -LiteralPath $about) -and
                (([xml](Get-Content -LiteralPath $about -Raw -Encoding utf8)).ModMetaData.packageId -eq $rules.packageId) -and
                -not (Test-Path -LiteralPath (Join-Path $Destination '.git')) -and -not (Test-Path -LiteralPath (Join-Path $Destination 'Source'))
            if (-not $previous -or (Test-ReparsePoint $Destination)) { Stop-Environment "$Destination is not empty and is not a previous staging copy of $($rules.packageId)" }
            if (@(Get-ChildItem -LiteralPath $Destination -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue).Count -gt 0) {
                Stop-Environment "$Destination contains junctions or symbolic links; remove it by hand"
            }
        }
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    # 清单与复制
    $planJson = & $Verifier -ListReleaseFiles -ModRoot $ModRoot -Baseline $BaselinePath
    if ($LASTEXITCODE -ne 0) { Stop-Environment 'Test-ReleasePackage.ps1 -ListReleaseFiles failed' }
    $plan = ($planJson -join "`n") | ConvertFrom-Json
    foreach ($skip in $plan.skipped | Where-Object { $_.path -like '*Assemblies/*' }) {
        Write-Host "WARNING: $($skip.path) is not shipped ($($skip.reason)); remove it from the load folder when it is not part of the release" -ForegroundColor Yellow
    }

    New-Item -ItemType Directory -Path $Destination | Out-Null
    $entries = [System.Collections.Generic.List[object]]::new()
    $total = 0L
    foreach ($relative in $plan.files) {
        $source = Join-Path $ModRoot $relative
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
        $size = (Get-Item -LiteralPath $target).Length
        $total += $size
        $entries.Add([ordered]@{ path = $relative; size = $size; sha256 = Get-Sha256 $target })
    }

    $harmonyDll = Join-Path $WorkshopDir '2009463077\1.7\Assemblies\0Harmony.dll'
    $alienRace = Join-Path $WorkshopDir '839005762\1.7\Assemblies\AlienRace.dll'
    # dotnet 从当前目录向上查找 global.json：在 Source/ 中查询，才是部署命令所用的 SDK。
    Push-Location -LiteralPath (Join-Path $ModRoot 'Source')
    try { $sdk = try { (& dotnet --version 2>$null) -join '' } catch { $null } }
    finally { Pop-Location }
    $manifest = [ordered]@{
        packageId   = $rules.packageId
        version     = $Version
        createdUtc  = (Get-Date).ToUniversalTime().ToString('o')
        git         = [ordered]@{ commit = $commit; dirty = $dirty; allowDirty = [bool]$AllowDirty; changes = $status.Count }
        game        = [ordered]@{
            version    = (Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -TotalCount 1 -ErrorAction SilentlyContinue)
            assemblies = [ordered]@{
                'Assembly-CSharp' = Get-OptionalHash (Join-Path $GameDir 'Assembly-CSharp.dll')
                UnityStubs        = Get-OptionalHash (Join-Path $GameDir 'UnityStubs.dll')
                Waylith           = Get-OptionalHash (Join-Path $GameDir 'Waylith.dll')
                '0Harmony'        = Get-OptionalHash $harmonyDll
                AlienRace         = Get-OptionalHash $alienRace
            }
        }
        sdk         = $sdk
        frozen16    = [ordered]@{ path = $rules.frozen16.path; size = $frozenSize; sha256 = $frozenHash }
        skipped     = @($plan.skipped)
        totalBytes  = $total
        files       = $entries.ToArray()
    }
    $manifestPath = "$Destination.manifest.json"
    [System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))

    $publishedId = (Get-Content -LiteralPath (Join-Path $ModRoot 'About\PublishedFileId.txt') -TotalCount 1).Trim()
    $vdfPath = Join-Path $parent 'workshop_item.vdf'
    # VDF 字符串中反斜杠与引号要转义。
    $vdf = { param([string] $Text) $Text.Replace('\', '\\').Replace('"', '\"') }
    $changeNote = "Release $Version (commit $($commit.Substring(0, 7)))"
    [System.IO.File]::WriteAllText($vdfPath, @"
"workshopitem"
{
  "appid" "294100"
  "publishedfileid" "$publishedId"
  "contentfolder" "$(& $vdf $Destination)"
  "previewfile" "$(& $vdf (Join-Path $Destination 'About\Preview.png'))"
  "changenote" "$(& $vdf $changeNote)"
}
"@, [System.Text.UTF8Encoding]::new($false))

    Write-Host ("staged {0} files ({1:N1} MB) in {2}" -f $entries.Count, ($total / 1MB), $Destination)
    Write-Host "manifest: $manifestPath"

    & $Verifier -Package $Destination -Manifest $manifestPath -ModRoot $ModRoot -Baseline $BaselinePath
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'the staged package did not pass Test-ReleasePackage.ps1; do not upload it' -ForegroundColor Red
        $exitCode = 1
    }
    else {
        $exitCode = 0
        if ($Archive) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $releases = Join-Path $ModRoot 'DevData\Releases'
            New-Item -ItemType Directory -Force -Path $releases | Out-Null
            $zip = Join-Path $releases "$($rules.packageId)-$Version.zip"
            if (Test-Path -LiteralPath $zip) { Stop-Environment "$zip already exists" }
            [System.IO.Compression.ZipFile]::CreateFromDirectory($Destination, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
            $zipArchive = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
            try {
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipArchive, $manifestPath, "$($rules.packageId).manifest.json")
            }
            finally { $zipArchive.Dispose() }
            $zipArchive = [System.IO.Compression.ZipFile]::OpenRead($zip)
            try { $count = @($zipArchive.Entries | Where-Object { -not $_.FullName.EndsWith('/') }).Count }
            finally { $zipArchive.Dispose() }
            if ($count -ne $entries.Count + 1) { Stop-Environment "$zip has $count file entries, expected $($entries.Count + 1)" }
            Write-Host "archive: $zip ($count entries)"
        }
        Write-Host "upload with SteamCMD: steamcmd +login <account> +workshop_build_item `"$vdfPath`" +quit"
    }
}
catch [EnvironmentProblem] {
    Write-Host "REFUSED: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    $exitCode = 2
}

exit $exitCode
