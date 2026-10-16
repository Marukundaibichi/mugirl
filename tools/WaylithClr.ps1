<#
.SYNOPSIS
    在 RimWorld 1.7 自带的运行时（Waylith CLR）上运行一个已构建的 net10.0 程序，而不是标准 .NET 10。

.DESCRIPTION
    RimWorld 1.7 自带一份独立的 .NET 10 运行时（10.0.x-waylithclr），列在 RimWorld.deps.json 的 runtime pack 条目中。
    本脚本把这些运行时文件复制到工作目录，加入程序本身（不带 .deps.json，目录中每个 DLL 都会被探测），并把游戏的
    RimWorld.runtimeconfig.json 以程序名复制过去，使 WaylithClr.* 选项（宽松绑定、程序集名重定向、可 JIT 补丁的程序集等）
    与游戏内完全一致。游戏目录只读。

    每次运行前清空工作目录：残留的 DLL 会先于游戏文件被探测。只清空本脚本创建过的目录（带标记文件
    .rimworld17-waylithclr-workdir），其他非空目录一律拒绝。

.PARAMETER AppDir
    已构建程序所在目录（含 .exe apphost 与 .dll）。

.PARAMETER AppName
    程序名，不含扩展名。

.PARAMETER GameDir
    RimWorld 1.7 安装目录（默认：Mod 目录往上两级）。

.PARAMETER WorkDir
    %TEMP% 下的工作目录。路径要短：apphost 无法从 260 个字符及以上的路径启动。

.PARAMETER PrepareOnly
    只准备工作目录并输出 .exe 路径，不运行。

.PARAMETER AppArgs
    传给程序的参数。

.EXAMPLE
    pwsh tools/WaylithClr.ps1 -AppDir $env:TEMP\mg17-validation\bin\MugirlRace.Validation\release -AppName MugirlRace.Validation -- --help
#>
#Requires -Version 7.0
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)] [string] $AppDir,
    [Parameter(Mandatory)] [string] $AppName,
    [string] $GameDir = (Join-Path $PSScriptRoot '..\..\..'),
    [string] $WorkDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'mg17-clr'),
    [switch] $PrepareOnly,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $AppArgs = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$AppDir = (Resolve-Path -LiteralPath $AppDir).Path
$WorkDir = [System.IO.Path]::GetFullPath($WorkDir)

# 只清空本脚本创建的目录；目录链接不跟随（Remove-Item 删除链接本身而不是目标）。
$marker = Join-Path $WorkDir '.rimworld17-waylithclr-workdir'
if (Test-Path -LiteralPath $WorkDir) {
    $existing = @(Get-ChildItem -LiteralPath $WorkDir -Force)
    if ($existing.Count -gt 0 -and -not (Test-Path -LiteralPath $marker)) {
        throw "$WorkDir is not empty and was not created by this script; pass another -WorkDir."
    }
    $existing | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
New-Item -ItemType File -Force -Path $marker | Out-Null

$depsFile = Join-Path $GameDir 'RimWorld.deps.json'
if (-not (Test-Path -LiteralPath $depsFile)) { throw "RimWorld.deps.json not found in $GameDir." }
$deps = Get-Content -LiteralPath $depsFile -Raw -Encoding utf8 | ConvertFrom-Json
$pack = $deps.targets.PSObject.Properties |
    ForEach-Object { $_.Value.PSObject.Properties } |
    Where-Object { $_.Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.*' } |
    Select-Object -First 1
if (-not $pack) { throw 'RimWorld.deps.json has no Microsoft.NETCore.App runtime pack entry.' }

$count = 0
foreach ($group in @($pack.Value.runtime, $pack.Value.native)) {
    if (-not $group) { continue }
    foreach ($file in $group.PSObject.Properties.Name) {
        Copy-Item -LiteralPath (Join-Path $GameDir $file) -Destination $WorkDir -Force
        $count++
    }
}

# 在复制来的运行时上自包含运行：没有 deps.json（目录中每个 DLL 都被探测），游戏的 runtimeconfig.json 改用本程序的名字。
# 游戏程序集（Assembly-CSharp、UnityStubs 等）从不复制：程序从游戏目录解析它们。
Get-ChildItem -LiteralPath $AppDir -File |
    Where-Object { $_.Name -notlike '*.deps.json' -and $_.Name -notlike '*.runtimeconfig.json' } |
    Copy-Item -Destination $WorkDir -Force
Copy-Item -LiteralPath (Join-Path $GameDir 'RimWorld.runtimeconfig.json') `
    -Destination (Join-Path $WorkDir "$AppName.runtimeconfig.json") -Force

$exe = Join-Path $WorkDir "$AppName.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "$AppName.exe not found in $AppDir." }
if ($exe.Length -ge 260) { throw "The apphost path is too long ($($exe.Length) characters): $exe. Pass a shorter -WorkDir." }
Write-Host "$($pack.Name): $count runtime files copied to $WorkDir"

if ($PrepareOnly) {
    return $exe
}

& $exe @AppArgs
exit $LASTEXITCODE
