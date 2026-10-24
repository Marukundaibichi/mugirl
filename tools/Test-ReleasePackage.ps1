#Requires -Version 7.0
<#
.SYNOPSIS
    独立复核一个发布暂存目录；也为 New-ReleasePackage.ps1 列出发布文件清单（两者共用同一套规则）。

.DESCRIPTION
    发布清单从开发目录推导，不手写（packaging_rules）：
      - LoadFolders.xml 与 About/ 下的 About.xml、Preview.png、PublishedFileId.txt；
      - LoadFolders.xml 每个版本块的每个 li 目录（含 IfModActive 条件目录）中，RimWorld 读取的内容子目录
        Assemblies、Defs、Patches、Languages、Textures、Sounds、Shaders、AssetBundles；根目录 "/" 因此只贡献 Textures 与 Sounds；
      - 冻结的 1.6 DLL 按路径读取的 1.6/Resources（描边与骑枪运动模糊的 AssetBundle）。
    即使位于上述目录中，源码、工具、文档、构建输出、缓存与备份也一律排除（tools/release-baseline.json 的 excluded*）。
    Assemblies 目录只允许 release-baseline.json 列出的文件：1.6 只有冻结的 MugirlRace.dll，1.7 只有 MugirlRace.dll 与 MugirlRace.pdb。

    复核（-Package）：
      1. About.xml 的 packageId；LoadFolders.xml 与开发目录逐字节相同。
      2. 每个文件都在发布清单中、没有被排除的路径或扩展名、没有目录链接；清单中的每个文件都在包中。
      3. 每个文件与开发目录中的同一路径逐字节相同（SHA-256）。
      4. 必需的程序集存在；1.6 DLL 的大小与 SHA-256 等于冻结记录；1.7 PDB 与 DLL 的 CodeView ID 一致。
      5. 有清单文件（<包>.manifest.json）时，清单中的路径、大小与 SHA-256 与包完全一致。
    About.xml 不含 1.7、或 1.7 DLL 比源码旧时只报警告（New-ReleasePackage.ps1 会拒绝旧 DLL）。

    退出码：0 通过；1 有失败；2 环境错误（找不到包、开发目录或基线）。

.PARAMETER Package
    要复核的暂存目录（HAR.MugirlRace 文件夹）。默认取 %LOCALAPPDATA%\MugirlRace\Release 下最新的一个。

.PARAMETER Manifest
    清单文件。默认 <Package>.manifest.json（存在时）。

.PARAMETER ListReleaseFiles
    不复核，向输出写出开发目录的发布清单（JSON：files、skipped、loadFolders）。供 New-ReleasePackage.ps1 使用。

.PARAMETER ModRoot
    开发目录。默认为本脚本的上级目录。

.PARAMETER Baseline
    发布基线。默认 tools/release-baseline.json。

.EXAMPLE
    pwsh tools/Test-ReleasePackage.ps1 -Package "$env:LOCALAPPDATA\MugirlRace\Release\20261014-1200-abc1234\HAR.MugirlRace"
#>
[CmdletBinding(DefaultParameterSetName = 'Verify')]
param(
    [Parameter(ParameterSetName = 'Verify')]
    [string] $Package,
    [Parameter(ParameterSetName = 'Verify')]
    [string] $Manifest,
    [Parameter(ParameterSetName = 'Plan', Mandatory)]
    [switch] $ListReleaseFiles,
    [string] $ModRoot,
    [string] $Baseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not $ModRoot) { $ModRoot = Join-Path $PSScriptRoot '..' }
$ModRoot = [System.IO.Path]::GetFullPath($ModRoot).TrimEnd('\', '/')
if (-not $Baseline) { $Baseline = Join-Path $PSScriptRoot 'release-baseline.json' }

function Get-Sha256([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function Test-ReparsePoint([System.IO.FileSystemInfo] $Item) {
    ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
}

function ConvertTo-RelativePath([string] $Root, [string] $Path) {
    [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

# 被排除的路径：任一目录段在排除目录中，或文件名匹配排除模式。返回原因，未排除时返回 $null。
function Get-ExclusionReason([string] $Relative, $Rules) {
    $segments = $Relative.Split('/')
    $folders = if ($segments.Count -gt 1) { $segments[0..($segments.Count - 2)] } else { @() }
    foreach ($segment in $folders) {
        foreach ($directory in $Rules.excludedDirectories) {
            if ($segment -ieq $directory -or ($directory -eq '.git' -and $segment -ilike '.git*')) { return "excluded directory '$segment'" }
        }
    }
    $leaf = $segments[-1]
    foreach ($pattern in $Rules.excludedFiles) {
        if ($leaf -ilike $pattern) { return "excluded file pattern '$pattern'" }
    }
    return $null
}

# LoadFolders.xml 中全部版本块的 li 目录（去重，"/" 记为空字符串）。
function Get-LoadFolders([string] $Root) {
    $path = Join-Path $Root 'LoadFolders.xml'
    if (-not (Test-Path -LiteralPath $path)) { throw "LoadFolders.xml not found in $Root" }
    [xml] $xml = Get-Content -LiteralPath $path -Raw -Encoding utf8
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($version in $xml.DocumentElement.ChildNodes) {
        if ($version.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        foreach ($li in $version.ChildNodes) {
            if ($li.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            $folder = $li.InnerText.Trim().Replace('\', '/').Trim('/')
            if ($seen.Add($folder)) { $folder }
        }
    }
}

# 从开发目录推导发布清单。
function Get-ReleasePlan([string] $Root, $Rules) {
    $files = [System.Collections.Generic.List[string]]::new()
    $skipped = [System.Collections.Generic.List[object]]::new()
    $loadFolders = @(Get-LoadFolders $Root)
    $files.Add('LoadFolders.xml')

    $about = Join-Path $Root 'About'
    foreach ($item in Get-ChildItem -LiteralPath $about -File -Force) {
        if ($Rules.aboutFiles -contains $item.Name) { $files.Add("About/$($item.Name)") }
        else { $skipped.Add([pscustomobject]@{ path = "About/$($item.Name)"; reason = 'not in the About allow-list' }) }
    }

    $directories = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($folder in $loadFolders) {
        foreach ($content in $Rules.contentFolders) {
            $relative = if ($folder.Length -eq 0) { $content } else { "$folder/$content" }
            if ((Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Container) -and $seen.Add($relative)) { $directories.Add($relative) }
        }
    }
    foreach ($extra in $Rules.extraFolders) {
        if ((Test-Path -LiteralPath (Join-Path $Root $extra) -PathType Container) -and $seen.Add($extra)) { $directories.Add($extra) }
    }

    $assemblyAllowList = @{}
    foreach ($property in $Rules.assemblies.PSObject.Properties) { $assemblyAllowList[$property.Name.ToLowerInvariant()] = @($property.Value) }

    foreach ($directory in $directories) {
        $full = Join-Path $Root $directory
        if (Test-ReparsePoint (Get-Item -LiteralPath $full -Force)) { throw "$directory is a junction or symbolic link; refusing to package link targets" }
        foreach ($item in Get-ChildItem -LiteralPath $full -Recurse -Force) {
            $relative = ConvertTo-RelativePath $Root $item.FullName
            if (Test-ReparsePoint $item) { throw "$relative is a junction or symbolic link; refusing to package link targets" }
            if ($item.PSIsContainer) { continue }
            $reason = Get-ExclusionReason $relative $Rules
            if ($null -eq $reason) {
                $parent = $relative.Substring(0, [Math]::Max(0, $relative.LastIndexOf('/')))
                if ($parent -imatch '(^|/)Assemblies$') {
                    $allowed = $assemblyAllowList[$parent.ToLowerInvariant()]
                    if ($null -eq $allowed -or $allowed -notcontains $item.Name) { $reason = 'not in the assemblies allow-list (tools/release-baseline.json)' }
                }
                elseif ($relative -imatch '(^|/)Assemblies/') {
                    $reason = 'nested folder inside an Assemblies folder'
                }
            }
            if ($null -ne $reason) { $skipped.Add([pscustomobject]@{ path = $relative; reason = $reason }) }
            else { $files.Add($relative) }
        }
    }

    return [pscustomobject]@{ files = $files.ToArray(); skipped = $skipped.ToArray(); loadFolders = $loadFolders; directories = $directories.ToArray() }
}

# 可移植 PDB 与 DLL 的 CodeView 记录必须指向同一次编译。
function Test-PdbMatches([string] $Dll, [string] $Pdb) {
    Add-Type -AssemblyName System.Reflection.Metadata
    $dllStream = [System.IO.File]::OpenRead($Dll)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($dllStream)
        try {
            $codeView = @($pe.ReadDebugDirectory() | Where-Object { $_.Type -eq [System.Reflection.PortableExecutable.DebugDirectoryEntryType]::CodeView }) | Select-Object -First 1
            if ($null -eq $codeView) { return 'the DLL has no CodeView debug entry' }
            $guid = $pe.ReadCodeViewDebugDirectoryData($codeView).Guid
        }
        finally { $pe.Dispose() }
    }
    finally { $dllStream.Dispose() }
    $pdbStream = [System.IO.File]::OpenRead($Pdb)
    try {
        $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        try {
            $id = $provider.GetMetadataReader().DebugMetadataHeader.Id
            $pdbGuid = [guid]::new([byte[]]@($id | Select-Object -First 16))
        }
        finally { $provider.Dispose() }
    }
    finally { $pdbStream.Dispose() }
    if ($guid -ne $pdbGuid) { return "CodeView GUID $guid in the DLL, $pdbGuid in the PDB" }
    return $null
}

function Find-NewestPackage {
    $root = Join-Path $env:LOCALAPPDATA 'MugirlRace\Release'
    if (-not (Test-Path -LiteralPath $root)) { return $null }
    Get-ChildItem -LiteralPath $root -Directory | ForEach-Object { Join-Path $_.FullName 'HAR.MugirlRace' } |
        Where-Object { Test-Path -LiteralPath $_ } | Sort-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc } | Select-Object -Last 1
}

$exitCode = 2
try {
    if (-not (Test-Path -LiteralPath $Baseline)) { throw "release baseline not found: $Baseline" }
    $rules = Get-Content -LiteralPath $Baseline -Raw -Encoding utf8 | ConvertFrom-Json
    $plan = Get-ReleasePlan $ModRoot $rules

    if ($ListReleaseFiles) {
        $plan | ConvertTo-Json -Depth 4
        $exitCode = 0
    }
    else {
        if (-not $Package) { $Package = Find-NewestPackage }
        if (-not $Package -or -not (Test-Path -LiteralPath $Package -PathType Container)) { throw "package folder not found: $Package" }
        $Package = [System.IO.Path]::GetFullPath($Package).TrimEnd('\', '/')
        if (-not $Manifest) {
            $candidate = "$Package.manifest.json"
            if (Test-Path -LiteralPath $candidate) { $Manifest = $candidate }
        }
        Write-Host "package:  $Package"
        Write-Host "dev tree: $ModRoot"

        $failures = [System.Collections.Generic.List[string]]::new()
        $warnings = [System.Collections.Generic.List[string]]::new()

        # 1. 元数据
        $aboutPath = Join-Path $Package 'About\About.xml'
        if (-not (Test-Path -LiteralPath $aboutPath)) { $failures.Add('About/About.xml is missing') }
        else {
            [xml] $about = Get-Content -LiteralPath $aboutPath -Raw -Encoding utf8
            if ($about.ModMetaData.packageId -ne $rules.packageId) { $failures.Add("About.xml packageId is '$($about.ModMetaData.packageId)', expected $($rules.packageId)") }
            $versions = @($about.ModMetaData.supportedVersions.li)
            if ($versions -notcontains '1.7') { $warnings.Add("About.xml supportedVersions is [$($versions -join ', ')]: 1.7 is not listed") }
        }

        # 2./3. 文件集合与逐字节比较
        $planned = [System.Collections.Generic.HashSet[string]]::new([string[]]$plan.files, [System.StringComparer]::Ordinal)
        $present = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $packageHashes = @{}
        foreach ($item in Get-ChildItem -LiteralPath $Package -Recurse -Force) {
            $relative = ConvertTo-RelativePath $Package $item.FullName
            if (Test-ReparsePoint $item) { $failures.Add("${relative}: junction or symbolic link inside the package"); continue }
            if ($item.PSIsContainer) { continue }
            [void]$present.Add($relative)
            $reason = Get-ExclusionReason $relative $rules
            if ($null -ne $reason) { $failures.Add("${relative}: $reason") }
            if (-not $planned.Contains($relative)) {
                $failures.Add("${relative}: not part of the release plan (not read by any load folder, excluded, or a case mismatch)")
                continue
            }
            $packageHashes[$relative] = Get-Sha256 $item.FullName
            $source = Join-Path $ModRoot $relative
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { $failures.Add("${relative}: missing from the development tree") }
            elseif ((Get-Sha256 $source) -ne $packageHashes[$relative]) { $failures.Add("${relative}: differs from the development tree") }
        }
        foreach ($relative in $plan.files) {
            if (-not $present.Contains($relative)) { $failures.Add("${relative}: planned but missing from the package") }
        }
        foreach ($skip in $plan.skipped | Where-Object { $_.reason -like '*allow-list*' -and $_.path -like '*Assemblies/*' }) {
            $warnings.Add("$($skip.path) in the development tree is not shipped: $($skip.reason)")
        }
        foreach ($folder in $plan.loadFolders) {
            $relative = if ($folder.Length -eq 0) { '.' } else { $folder }
            $hasContent = @($plan.directories | Where-Object { $(if ($_.Contains('/')) { $_.Substring(0, $_.LastIndexOf('/')) } else { '' }) -ieq $folder }).Count -gt 0
            if ($hasContent -and -not (Test-Path -LiteralPath (Join-Path $Package $relative) -PathType Container)) { $failures.Add("load folder '$folder' has content in the development tree but is missing from the package") }
        }

        # 4. 程序集
        foreach ($required in $rules.requiredAssemblies) {
            if (-not $present.Contains($required)) { $failures.Add("required assembly missing: $required") }
        }
        $frozen = Join-Path $Package $rules.frozen16.path
        if (Test-Path -LiteralPath $frozen) {
            $size = (Get-Item -LiteralPath $frozen).Length
            $hash = $packageHashes[$rules.frozen16.path]
            if ($size -ne $rules.frozen16.size -or $hash -ne $rules.frozen16.sha256) {
                $failures.Add("$($rules.frozen16.path) is not the frozen 1.6 build: $size bytes, SHA-256 $hash (expected $($rules.frozen16.size) bytes, $($rules.frozen16.sha256))")
            }
        }
        $dll17 = Join-Path $Package '1.7\Assemblies\MugirlRace.dll'
        $pdb17 = Join-Path $Package '1.7\Assemblies\MugirlRace.pdb'
        if ((Test-Path -LiteralPath $dll17) -and (Test-Path -LiteralPath $pdb17)) {
            $mismatch = Test-PdbMatches $dll17 $pdb17
            if ($mismatch) { $failures.Add("1.7/Assemblies/MugirlRace.pdb does not belong to the DLL: $mismatch") }
        }
        if (Test-Path -LiteralPath $dll17) {
            $dllTime = (Get-Item -LiteralPath $dll17).LastWriteTimeUtc
            # 与 New-ReleasePackage 的生产输入一致；验证仍只告警，不改变包内容或构建。
            $newer = Get-ChildItem -LiteralPath (Join-Path $ModRoot 'Source') -Recurse -File -Force `
                -Include '*.cs', '*.csproj', '*.props', '*.targets', 'BannedSymbols.txt', 'global.json', '.editorconfig' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '[\\/](obj|bin|Tests)[\\/]' -and $_.LastWriteTimeUtc -gt $dllTime } | Select-Object -First 1
            if ($newer) { $warnings.Add("the packaged 1.7 DLL is older than $(ConvertTo-RelativePath $ModRoot $newer.FullName)") }
        }

        # 5. 清单
        if ($Manifest) {
            $data = Get-Content -LiteralPath $Manifest -Raw -Encoding utf8 | ConvertFrom-Json
            $listed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($entry in $data.files) {
                [void]$listed.Add($entry.path)
                $path = Join-Path $Package $entry.path
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $failures.Add("manifest lists $($entry.path), which is not in the package"); continue }
                $hash = if ($packageHashes.ContainsKey($entry.path)) { $packageHashes[$entry.path] } else { Get-Sha256 $path }
                if ($entry.sha256 -ne $hash -or $entry.size -ne (Get-Item -LiteralPath $path).Length) { $failures.Add("manifest entry $($entry.path) does not match the file") }
            }
            foreach ($relative in $present) { if (-not $listed.Contains($relative)) { $failures.Add("$relative is not in the manifest") } }
            Write-Host "manifest: $Manifest ($($listed.Count) entries)"
        }
        else {
            $warnings.Add('no manifest next to the package; manifest checks skipped')
        }

        Write-Host "files: $($present.Count) in the package, $($plan.files.Count) planned, $(@($plan.skipped).Count) skipped in the development tree"
        foreach ($warning in $warnings) { Write-Host "WARN  $warning" -ForegroundColor DarkYellow }
        foreach ($failure in $failures | Select-Object -First 200) { Write-Host "FAIL  $failure" -ForegroundColor Red }
        if ($failures.Count -gt 200) { Write-Host "FAIL  ... $($failures.Count - 200) more" -ForegroundColor Red }
        $exitCode = if ($failures.Count -eq 0) { 0 } else { 1 }
        Write-Host ("{0}: {1} failure(s), {2} warning(s)" -f $(if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }), $failures.Count, $warnings.Count) `
            -ForegroundColor $(if ($exitCode -eq 0) { 'Green' } else { 'Red' })
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}

exit $exitCode
