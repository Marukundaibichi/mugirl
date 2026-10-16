<#
.SYNOPSIS
    Mugirl Race 1.7 构建的离线验证：在标准 .NET 10 与游戏自带运行时（Waylith CLR）上各运行一遍并比较结果。

.DESCRIPTION
    下文的 <WorkRoot> 是 -WorkRoot 指定的目录，默认 %TEMP%。所有构建、报告与临时副本都在其中，不写 Mod 目录：
    RimWorld 1.7 的游戏内创意工坊上传会复制 Mod 目录下的一切。dotnet 在 Source/ 中启动，Source/global.json 固定的 SDK 因此生效。

    1. 把验证程序（Source/Tests/MugirlRace.Validation）构建到 <WorkRoot>\mg17-validation（--artifacts-path）。
    2. 除非 -ModDll 或 -Shipped 指定了程序集，把 Mod 构建到 <WorkRoot>\mg17build\validation（--artifacts-path 加 -p:OutDir），
       不写 Source/**/obj 与 bin（普通构建也从不写 1.7/Assemblies，见 MugirlRace.csproj）；只把 MugirlRace.dll 与其 PDB
       复制到一个干净目录交给验证程序。
    3. 先在标准 .NET 10 上运行全部检查，再经 tools/WaylithClr.ps1 在游戏运行时上运行（<WorkRoot>\mg17-clr，带标记文件的
       工作目录），各自在验证程序构建旁写 JSON 报告。
    4. 两个运行时必须报告相同的检查结果与相同的失败/警告键。
    5. 运行源码树中的其他离线套件（存在时）：单元测试 Source/Tests/MugirlRace.Tests 与原生 Shader 检查
       Source/Tests/MugirlRace.ShaderCheck，各自构建到 <WorkRoot>\mg17build 下的独立目录。

    -SelfTest 改为运行负对照（Source/Tests/MugirlRace.Validation/Controls：一个每条规则各违反一次的小 "MugirlRace.dll" 与
    Mod 目录），要求 Controls/expected-failures.txt 列出的每条规则在两个运行时上都触发，"absent:" 规则都不触发。

    基线（Source/Tests/MugirlRace.Validation/Baselines）：
      - 生成的黄金清单：load-folders.txt、patch-targets.txt、transpiler-hooks.txt。-UpdateBaselines 由标准 .NET 那次运行重写，
        之后像代码改动一样审阅差异。
      - unity-boundary.json 与 dynamic-lookups.allow.txt：-UpdateBaselines 加入新条目，理由写成 "REVIEW: ..."，之后作为警告报告，
        直到有人换成真实理由；unity-boundary.json 同时删除不再使用的条目。
      - 其余清单（例外、契约、dropped-*）只由人维护，每条都要写理由；不再匹配任何对象的条目报为过期。
    迁移结束时的推荐流程：pwsh tools/Validate.ps1 -UpdateBaselines，审阅 Baselines 的 git diff，替换每个 REVIEW 理由，
    再不带 -UpdateBaselines 运行一次，确认两个运行时一致且通过。

    退出码：0 全部通过，1 有检查失败或两个运行时不一致，2 构建或环境错误。

.PARAMETER GameDir
    RimWorld 1.7 安装目录（默认：Mod 目录往上两级）。

.PARAMETER HarmonyDir
    Harmony 3 的 0Harmony.dll 所在目录（默认：Harmony 创意工坊条目的 1.7/Assemblies）。

.PARAMETER ModDll
    验证这个 MugirlRace.dll，而不是重新构建（同目录的 PDB 会一并使用）。

.PARAMETER Shipped
    验证已发布的 1.7/Assemblies/MugirlRace.dll（不构建 Mod）。

.PARAMETER AssumeActive
    视为启用的可选整合 Mod 包 id（逗号分隔），其 IfModActive 加载目录与 MayRequire 内容因此受检；已安装的连同依赖加载。
    默认 all：LoadFolders.xml 的 IfModActive 条件中出现的全部包 id。传空串只检查 Core、DLC 与硬依赖。

.PARAMETER SkipWaylithClr
    只在标准 .NET 10 上运行。

.PARAMETER UpdateBaselines
    由标准 .NET 那次运行更新生成的基线（见上）。之后审阅差异。

.PARAMETER SkipUnitTests
    不构建、不运行 Source/Tests/MugirlRace.Tests。

.PARAMETER SkipShaderCheck
    不构建、不运行 Source/Tests/MugirlRace.ShaderCheck。

.PARAMETER SelfTest
    运行负对照并确认每条规则都会触发（不运行单元测试与 Shader 检查）。

.PARAMETER WorkRoot
    存放本脚本全部临时目录的目录（不存在时创建；不能位于 Mod 目录内）。默认 %TEMP%，即 %TEMP%\mg17-validation、
    %TEMP%\mg17build\…、%TEMP%\mg17-clr 等固定路径。脚本每次运行都会清空其中自己的子目录，所以同时运行的多个验证
    必须各用一个 -WorkRoot。Waylith CLR 的 apphost 路径必须短于 260 个字符，WorkRoot 不宜太深。

.EXAMPLE
    pwsh tools/Validate.ps1
.EXAMPLE
    pwsh tools/Validate.ps1 -WorkRoot $env:TEMP\mg17-agentA
.EXAMPLE
    pwsh tools/Validate.ps1 -Shipped
.EXAMPLE
    pwsh tools/Validate.ps1 -SelfTest
.EXAMPLE
    pwsh tools/Validate.ps1 -UpdateBaselines -SkipUnitTests
#>
#Requires -Version 7.0
[CmdletBinding()]
param(
    [string] $GameDir = (Join-Path $PSScriptRoot '..\..\..'),
    [string] $HarmonyDir = '',
    [string] $ModDll = '',
    [switch] $Shipped,
    [string] $AssumeActive = 'all',
    [switch] $SkipWaylithClr,
    [switch] $UpdateBaselines,
    [switch] $SkipUnitTests,
    [switch] $SkipShaderCheck,
    [switch] $SelfTest,
    [string] $WorkRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:DOTNET_NOLOGO = '1'

$modRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path

# 逐级解析目录链接（junction、符号链接）得到真实路径；尚不存在的部分原样接上。
function Resolve-RealPath([string] $Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    $current = [System.IO.Path]::GetPathRoot($full)
    foreach ($segment in $full.Substring($current.Length).Split([System.IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        $info = [System.IO.DirectoryInfo]::new($current)
        if ($info.Exists -and $info.LinkTarget) {
            $target = $info.ResolveLinkTarget($true)
            if ($target) { $current = Resolve-RealPath $target.FullName }
        }
    }
    return [System.IO.Path]::TrimEndingDirectorySeparator($current) + [System.IO.Path]::DirectorySeparatorChar
}

# 本脚本的全部临时目录都在 $work 之下；以目录分隔符结尾，前缀比较不会把 "...\mg17x" 当成 "...\mg17" 的子目录。
# 相对的 -WorkRoot 按 PowerShell 的当前位置解析（GetFullPath 用进程目录，cd 之后两者不同）。
$work = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($(if ($WorkRoot) { $PSCmdlet.GetUnresolvedProviderPathFromPSPath($WorkRoot) } else { [System.IO.Path]::GetTempPath() }))) + [System.IO.Path]::DirectorySeparatorChar
# 比较真实路径：经目录链接指进 Mod 目录的 WorkRoot 同样拒绝。
if ((Resolve-RealPath $work).StartsWith((Resolve-RealPath $modRoot), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host "ENVIRONMENT ERROR: -WorkRoot $work is inside the mod folder; build output there would be loaded and uploaded with the mod."
    exit 2
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
$artifacts = Join-Path $work 'mg17-validation'
$validationDir = Join-Path $modRoot 'Source\Tests\MugirlRace.Validation'
$controls = Join-Path $validationDir 'Controls'
$sourceDir = Join-Path $modRoot 'Source\MugirlRace'
# dotnet 从当前目录向上查找 global.json：在 Source/ 中启动，固定的 SDK 才会生效。
$sourceRoot = Join-Path $modRoot 'Source'

# 路径以正斜杠、无结尾斜杠传给 MSBuild（结尾的 "\" 会转义后面的引号）。Source/Directory.Build.props 从 RimWorldDir 与
# HarmonyAssembliesDir 推导其余目录，不要直接传 GameDir。
function ConvertTo-MSBuildPath([string] $Path) { return $Path.TrimEnd('\', '/').Replace('\', '/') }
$buildArgs = @()
if ($PSBoundParameters.ContainsKey('GameDir')) {
    $buildArgs += "-p:RimWorldDir=$(ConvertTo-MSBuildPath $GameDir)"
}
if ($HarmonyDir) {
    $HarmonyDir = (Resolve-Path -LiteralPath $HarmonyDir).Path
    $buildArgs += "-p:HarmonyAssembliesDir=$(ConvertTo-MSBuildPath $HarmonyDir)"
}

function Invoke-DotnetBuild([string[]] $Arguments) {
    Push-Location -LiteralPath $sourceRoot
    try {
        & dotnet build @Arguments -c Release --nologo -v:minimal @buildArgs
    }
    finally {
        Pop-Location
    }
}

function Invoke-Build([string] $Title, [string[]] $Arguments) {
    Write-Host "== Building $Title"
    Invoke-DotnetBuild $Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED: $Title"
        exit 2
    }
}

# 清空本脚本在 WorkRoot 下使用的目录：只接受 WorkRoot 内的绝对路径，目录链接只删链接本身。
function Reset-TempDirectory([string] $Path) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($work, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear $full (outside the work root $work)."
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $full | Out-Null
    return $full
}

# 只把 Mod 程序集与其 PDB 交给验证程序：编译期门面（UnityEngine.CoreModule）等引用输出不能放在它旁边。
function Copy-ModAssembly([string] $FromDir, [string] $ToDir) {
    $ToDir = Reset-TempDirectory $ToDir
    Copy-Item -LiteralPath (Join-Path $FromDir 'MugirlRace.dll') -Destination $ToDir
    $pdb = Join-Path $FromDir 'MugirlRace.pdb'
    if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination $ToDir }
    return (Join-Path $ToDir 'MugirlRace.dll')
}

# 1. 验证程序。
Invoke-Build 'the validator' @((Join-Path $validationDir 'MugirlRace.Validation.csproj'), '--artifacts-path', (ConvertTo-MSBuildPath $artifacts))
$appDir = Join-Path $artifacts 'bin\MugirlRace.Validation\release'
$validator = Join-Path $appDir 'MugirlRace.Validation.exe'

# 2. 要验证的程序集，以及它所属的 Mod 目录。
$extraArgs = @('--source', $sourceDir)
if ($SelfTest) {
    $controlBuild = Join-Path $work 'mg17build\validation-control'
    Invoke-Build 'the negative control' @((Join-Path $controls 'NegativeControl\NegativeControl.csproj'), '--artifacts-path', (ConvertTo-MSBuildPath $controlBuild))

    # 负对照 Mod 目录的一次性副本，“发布”控制 DLL，再注入属于文件而非代码的违规：DLL 旁多余的 deps.json、一个解析不到的
    # BannedSymbols.txt 条目；控制源码复制到 Source/MugirlRace 供源码文本检查读取。
    $controlRoot = Reset-TempDirectory (Join-Path $work 'mg17-control-mod')
    Copy-Item -Path (Join-Path $controls 'ModRoot\*') -Destination $controlRoot -Recurse
    $controlAssemblies = Join-Path $controlRoot '1.7\Assemblies'
    $controlSource = Join-Path $controlRoot 'Source\MugirlRace'
    New-Item -ItemType Directory -Force -Path $controlAssemblies, (Join-Path $controlRoot '1.6\Assemblies'), $controlSource | Out-Null
    $controlBin = Join-Path $controlBuild 'bin\NegativeControl\release'
    Copy-Item -LiteralPath (Join-Path $controlBin 'MugirlRace.dll'), (Join-Path $controlBin 'MugirlRace.pdb') -Destination $controlAssemblies
    Set-Content -LiteralPath (Join-Path $controlAssemblies 'MugirlRace.deps.json') -Value '{}' -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $modRoot '1.6\Assemblies\MugirlRace.dll') -Destination (Join-Path $controlRoot '1.6\Assemblies')
    Copy-Item -Path (Join-Path $controls 'NegativeControl\*.cs') -Destination $controlSource
    $banned = Join-Path $controlSource 'BannedSymbols.txt'
    Copy-Item -LiteralPath (Join-Path $sourceDir 'BannedSymbols.txt') -Destination $banned
    Add-Content -LiteralPath $banned -Value 'M:UnityEngine.Mathf.NoSuchMethodControl(System.Single);control: an entry that matches nothing' -Encoding utf8

    # 负对照的基线：规则类文件（架构规则、base 调用清单）用正式版本，证明的正是正式规则会触发；其余用 Controls/Baselines。
    $controlBaselines = Reset-TempDirectory (Join-Path $work 'mg17-control-baselines')
    foreach ($shared in @('architecture-rules.json', 'base-calls.json')) {
        Copy-Item -LiteralPath (Join-Path $validationDir "Baselines\$shared") -Destination $controlBaselines
    }
    Copy-Item -Path (Join-Path $controls 'Baselines\*') -Destination $controlBaselines -Force

    $ModDll = Join-Path $controlAssemblies 'MugirlRace.dll'
    $extraArgs = @('--mod-root', $controlRoot, '--baselines', $controlBaselines, '--source', $controlSource,
        '--frozen', (Join-Path $modRoot '1.6\Assemblies\MugirlRace.dll'))
    $AssumeActive = ''
}
elseif ($Shipped) {
    $ModDll = Join-Path $modRoot '1.7\Assemblies\MugirlRace.dll'
}
elseif (-not $ModDll) {
    # 隔离构建：中间文件进 --artifacts-path，输出进 WorkRoot 下的 OutDir，源码树中的 bin/obj 不受影响。
    $modBuild = Join-Path $work 'mg17build\validation'
    $modBin = Join-Path $modBuild 'bin'
    Invoke-Build 'MugirlRace (1.7)' @((Join-Path $sourceDir 'MugirlRace.csproj'), '--artifacts-path', (ConvertTo-MSBuildPath (Join-Path $modBuild 'artifacts')),
        "-p:OutDir=$(ConvertTo-MSBuildPath $modBin)/", "-p:OutputPath=$(ConvertTo-MSBuildPath $modBin)/")
    $ModDll = Copy-ModAssembly $modBin (Join-Path $modBuild 'mod')
}
$ModDll = (Resolve-Path -LiteralPath $ModDll).Path

$common = @('--mod', $ModDll, '--game', $GameDir) + $extraArgs
if ($AssumeActive) { $common += @('--assume-active', $AssumeActive) }
if ($HarmonyDir) { $common += @('--harmony', $HarmonyDir) }

# 3. 在两个运行时上运行。
$runs = [System.Collections.Generic.List[object]]::new()
$runs.Add([pscustomobject]@{ Name = 'dotnet'; Exe = $validator })
if (-not $SkipWaylithClr) {
    $clrExe = & (Join-Path $PSScriptRoot 'WaylithClr.ps1') -GameDir $GameDir -AppDir $appDir -AppName 'MugirlRace.Validation' `
        -WorkDir (Join-Path $work 'mg17-clr') -PrepareOnly
    $runs.Add([pscustomobject]@{ Name = 'waylithclr'; Exe = $clrExe })
}

$exitCodes = @{}
foreach ($run in $runs) {
    $report = Join-Path $artifacts "report-$($run.Name).json"
    $arguments = $common + @('--report', $report)
    if ($UpdateBaselines -and $run.Name -eq 'dotnet') { $arguments += '--update-baselines' }
    Write-Host ''
    Write-Host "=================== $($run.Name) ==================="
    & $run.Exe @arguments
    $exitCodes[$run.Name] = $LASTEXITCODE
    if ($LASTEXITCODE -eq 2) {
        Write-Host "ENVIRONMENT ERROR on $($run.Name)"
        exit 2
    }
}

# 4. 两个运行时的每个检查结果与每个失败/警告键必须一致（信息行与运行时相关的消息文本不比较）。
function Get-ReportKeys([string] $Path) {
    $json = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json
    $keys = @($json.checks | ForEach-Object { "check $($_.id): $($_.status)" })
    $keys += @($json.findings | Where-Object { $_.severity -ne 'info' } | ForEach-Object { "$($_.severity): $($_.key)" })
    return @($keys | Sort-Object -Unique)
}

$disagree = $false
if ($runs.Count -gt 1) {
    $a = Get-ReportKeys (Join-Path $artifacts 'report-dotnet.json')
    $b = Get-ReportKeys (Join-Path $artifacts 'report-waylithclr.json')
    $diff = @(Compare-Object -ReferenceObject $a -DifferenceObject $b)
    Write-Host ''
    if ($diff.Count -gt 0) {
        $disagree = $true
        Write-Host 'RUNTIMES DISAGREE (<= standard .NET only, => Waylith CLR only):'
        $diff | ForEach-Object { Write-Host "  $($_.SideIndicator) $($_.InputObject)" }
    }
    else {
        Write-Host "Both runtimes report identical results ($($a.Count) check results and failure/warning keys)."
    }
}

if ($SelfTest) {
    # expected-failures.txt："[warning: |absent: ]check | subject 子串 [| message 子串]<TAB>违规证明了什么"。
    # warning: 匹配警告而非失败；absent: 要求没有任何失败或警告匹配（正对照）。
    $rules = @(Get-Content -LiteralPath (Join-Path $controls 'expected-failures.txt') -Encoding utf8 |
        Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') } |
        ForEach-Object { ($_ -split "`t", 2)[0].Trim() })
    $problems = 0
    foreach ($run in $runs) {
        $json = Get-Content -LiteralPath (Join-Path $artifacts "report-$($run.Name).json") -Raw -Encoding utf8 | ConvertFrom-Json
        foreach ($rule in $rules) {
            $severities = @('failure')
            $pattern = $rule
            $absent = $false
            if ($rule.StartsWith('warning: ')) {
                $severities = @('warning')
                $pattern = $rule.Substring('warning: '.Length)
            }
            elseif ($rule.StartsWith('absent: ')) {
                $severities = @('failure', 'warning')
                $pattern = $rule.Substring('absent: '.Length)
                $absent = $true
            }
            $parts = @($pattern -split '\s*\|\s*')
            $hits = @($json.findings | Where-Object {
                    $_.severity -in $severities -and
                    $_.check -eq $parts[0] -and
                    ($parts.Count -lt 2 -or $_.subject.Contains($parts[1])) -and
                    ($parts.Count -lt 3 -or $_.message.Contains($parts[2])) })
            if ($absent -and $hits.Count -gt 0) {
                Write-Host "SELF-TEST: rule fired on $($run.Name) although it must not: $rule"
                $hits | ForEach-Object { Write-Host "    $($_.subject): $($_.message)" }
                $problems++
            }
            elseif (-not $absent -and $hits.Count -eq 0) {
                Write-Host "SELF-TEST: rule did not fire on $($run.Name): $rule"
                $problems++
            }
        }
    }

    Write-Host ''
    if ($problems -gt 0 -or $disagree) {
        Write-Host "SELF-TEST FAILED ($problems rule problem(s)$(if ($disagree) { ', runtimes disagree' }))."
        exit 1
    }
    Write-Host "SELF-TEST PASSED: all $($rules.Count) control rules behaved as expected on $($runs.Count) runtime(s)."
    exit 0
}

# 5. 其他离线套件。它们检查源码树与内容，与 -ModDll/-Shipped 指定的程序集无关。单元测试构建自己的 MugirlRace 副本：
#    -p:OutDir 与 artifacts 路径让它的输出和中间文件都留在 WorkRoot，不进源码树中共享的 bin/obj 目录。
function Invoke-Suite([string] $Title, [string[]] $BuildArguments, [string] $Exe, [string[]] $RunArguments) {
    Write-Host ''
    Write-Host "=================== $Title ==================="
    # Out-Host：工具输出不能成为函数返回值的一部分。
    Invoke-DotnetBuild $BuildArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        return 'build failed'
    }
    & $Exe @RunArguments | Out-Host
    return $(if ($LASTEXITCODE -eq 0) { 'passed' } else { "failed (exit code $LASTEXITCODE)" })
}

$suites = [ordered]@{}
$testProject = Join-Path $modRoot 'Source\Tests\MugirlRace.Tests\MugirlRace.Tests.csproj'
if (-not $SkipUnitTests) {
    if (Test-Path -LiteralPath $testProject) {
        $testBuild = Join-Path $work 'mg17build\validation-tests'
        $suites['unit tests'] = Invoke-Suite 'unit tests' @($testProject, '--artifacts-path', (ConvertTo-MSBuildPath $testBuild),
            "-p:OutDir=$(ConvertTo-MSBuildPath $testBuild)/bin/", "-p:OutputPath=$(ConvertTo-MSBuildPath $testBuild)/bin/") `
            (Join-Path $testBuild 'bin\MugirlRace.Tests.exe') @()
    }
    else {
        Write-Host "(unit tests skipped: $testProject does not exist)"
    }
}
$shaderProject = Join-Path $modRoot 'Source\Tests\MugirlRace.ShaderCheck\MugirlRace.ShaderCheck.csproj'
if (-not $SkipShaderCheck) {
    if (Test-Path -LiteralPath $shaderProject) {
        $shaderBuild = Join-Path $work 'mg17build\validation-shadercheck'
        $suites['shader check'] = Invoke-Suite 'shader check' @($shaderProject, '--artifacts-path', (ConvertTo-MSBuildPath $shaderBuild)) `
            (Join-Path $shaderBuild 'bin\MugirlRace.ShaderCheck\release\MugirlRace.ShaderCheck.exe') @($modRoot, $GameDir)
    }
    else {
        Write-Host "(shader check skipped: $shaderProject does not exist)"
    }
}

$failed = @($exitCodes.Values | Where-Object { $_ -ne 0 }).Count -gt 0
Write-Host ''
foreach ($run in $runs) {
    Write-Host ("{0,-14} {1}" -f $run.Name, $(if ($exitCodes[$run.Name] -eq 0) { 'passed' } else { 'failed' }))
}
foreach ($suite in $suites.GetEnumerator()) {
    Write-Host ("{0,-14} {1}" -f $suite.Key, $suite.Value)
    if ($suite.Value -ne 'passed') { $failed = $true }
}
if ($failed -or $disagree) {
    Write-Host 'VALIDATION FAILED'
    exit 1
}
Write-Host 'VALIDATION PASSED'
exit 0
