#Requires -Version 7.0
<#
.SYNOPSIS
    文档静态检查：必需文档、Markdown 相对链接与锚点、子目录不得有 README/AGENTS、git diff --check。

.DESCRIPTION
    检查项（AGENTS.md 与 README.md“文档维护约定”）：

      1. 必需文档存在，根 README 链接内容指南、验证手册、发布清单与 ADR 目录。
      2. README.md、AGENTS.md 与 docs/**/*.md 中的相对链接指向存在的文件或目录（跳过 http/https/mailto/steam 等外部链接，
         跳过代码块与行内代码）。指向 Markdown 标题的锚点不存在时只报警告（锚点按 GitHub 规则从标题推导，可能有出入）。
         链接指向仓库之外时只报警告。
      3. 根目录以外不得有 README.md 或 AGENTS.md（按 git ls-files 列出受版本控制与未忽略的文件，DevData、TMP 等被忽略的目录不检查）。
      4. git diff --check 与 git diff --cached --check 没有空白错误（行尾空格、冲突标记等）。

    退出码：0 通过（可以有警告）；1 有失败；2 环境错误（没有 git 或不是仓库）。

.PARAMETER SkipGitDiffCheck
    跳过第 4 项（例如工作区中有他人未完成的改动时）。

.PARAMETER WarningsAsErrors
    把警告（锚点、仓库外链接）也算作失败。

.EXAMPLE
    pwsh tools/Test-Docs.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipGitDiffCheck,
    [switch] $WarningsAsErrors
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

# 必需文档（原 docs/tools/Invoke-Phase6StaticValidation.ps1 的清单，加上 1.7 的 ADR）。移动或合并文档时同步更新这里。
$RequiredDocs = @(
    'README.md'
    'AGENTS.md'
    'docs/content-update-guide.md'
    'docs/compatibility-guide.md'
    'docs/localization-and-comments.md'
    'docs/validation-runbook.md'
    'docs/release-checklist.md'
    'docs/features/outline-guide.md'
    'docs/records/mech-throw-graze-2026-09-26.md'
    'docs/records/event-normalization-2026-10-04.md'
    'docs/records/bug-report-fixes-2026-10-11.md'
    'docs/records/release-1016-2026-10-16.md'
    'docs/architecture/ADR-001-single-dll.md'
    'docs/architecture/ADR-002-harmony-registration.md'
    'docs/architecture/ADR-003-compatibility-scope.md'
    'docs/architecture/ADR-004-root-namespace-markers.md'
    'docs/architecture/ADR-005-version-layout.md'
    'docs/architecture/ADR-006-unity-boundary.md'
    'docs/architecture/ADR-007-save-schema-upgrade.md'
    'docs/architecture/ADR-008-public-api.md'
    'docs/architecture/ADR-009-naming-and-layout.md'
    'docs/records/refactor-1.7-handoff-2026-10-15.md'
    'docs/records/refactor-1.7-resume-2026-10-15.md'
    'docs/records/refactor-1.7-core-content-2026-10-16.md'
    'docs/records/refactor-1.7-corporate-2026-10-15.md'
    'docs/records/refactor-1.7-incidents-2026-10-16.md'
    'docs/records/refactor-1.7-milk-2026-10-15.md'
    'docs/records/refactor-1.7-restraints-2026-10-16.md'
    'docs/records/refactor-1.7-combat-2026-10-15.md'
    'docs/records/refactor-1.7-mounting-2026-10-16.md'
    'docs/refactor-1.7/plan.md'
    'docs/refactor-1.7/decisions.md'
    'docs/refactor-1.7/design.md'
    'docs/refactor-1.7/rename-tool.md'
)

# 根 README 必须链接的目标（正则，匹配链接目标）。
$ReadmeMustLink = @(
    'docs/content-update-guide\.md'
    'docs/validation-runbook\.md'
    'docs/release-checklist\.md'
    'docs/architecture/'
    'docs/records/refactor-1\.7-handoff-2026-10-15\.md'
    'docs/records/refactor-1\.7-resume-2026-10-15\.md'
    'docs/refactor-1\.7/plan\.md'
)

$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$anchorCache = @{}

function Add-Failure([string] $Text) { $failures.Add($Text) }
function Add-Warning([string] $Text) { $warnings.Add($Text) }

function Get-Relative([string] $Path) { [System.IO.Path]::GetRelativePath($RepoRoot, $Path).Replace('\', '/') }

# 去掉 Markdown 格式后按 GitHub 规则生成锚点：小写，删除字母、数字、空格、连字符与下划线以外的字符，空格换成连字符。
function ConvertTo-Slug([string] $Heading) {
    $text = $Heading -replace '!?\[([^\]]*)\]\([^)]*\)', '$1'
    $text = $text -replace '<[^>]+>', '' -replace '[`*~]', ''
    $text = $text.Trim().ToLowerInvariant()
    $text = $text -replace '[^\p{L}\p{N}\p{M}_ -]', ''
    return $text -replace ' ', '-'
}

function Get-Anchors([string] $File) {
    if ($anchorCache.ContainsKey($File)) { return , $anchorCache[$File] }
    $anchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $counts = @{}
    $inFence = $false
    foreach ($line in [System.IO.File]::ReadLines($File)) {
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        foreach ($match in [regex]::Matches($line, '<a\s+(?:name|id)="([^"]+)"')) { [void]$anchors.Add($match.Groups[1].Value) }
        if ($line -notmatch '^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$') { continue }
        $slug = ConvertTo-Slug $Matches[1]
        if ($counts.ContainsKey($slug)) { $counts[$slug]++; [void]$anchors.Add("$slug-$($counts[$slug])") }
        else { $counts[$slug] = 0; [void]$anchors.Add($slug) }
    }
    $anchorCache[$File] = $anchors
    # 逗号运算符：原样返回集合，不让 PowerShell 展开成元素。
    return , $anchors
}

# 一个 Markdown 文件中的链接目标（行号、目标），跳过代码块与行内代码。
function Get-LinkTargets([string] $File) {
    $number = 0
    $inFence = $false
    foreach ($line in [System.IO.File]::ReadLines($File)) {
        $number++
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $plain = $line -replace '`[^`]*`', ''
        foreach ($match in [regex]::Matches($plain, '!?\[[^\]]*\]\(\s*(<[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\s*\)')) {
            [pscustomobject]@{ Line = $number; Target = $match.Groups[1].Value.Trim('<', '>') }
        }
        if ($plain -match '^\s{0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)') {
            [pscustomobject]@{ Line = $number; Target = $Matches[1].Trim('<', '>') }
        }
    }
}

function Test-Links([string] $File) {
    $relativeFile = Get-Relative $File
    foreach ($link in Get-LinkTargets $File) {
        $target = $link.Target
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
        $hash = $target.IndexOf('#')
        $pathPart = if ($hash -ge 0) { $target.Substring(0, $hash) } else { $target }
        $anchor = if ($hash -ge 0) { [System.Uri]::UnescapeDataString($target.Substring($hash + 1)) } else { '' }
        $where = "${relativeFile}:$($link.Line)"
        $resolved = if ($pathPart.Length -eq 0) { $File } else {
            [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $File) ([System.Uri]::UnescapeDataString($pathPart))))
        }
        if (-not (Test-Path -LiteralPath $resolved)) {
            Add-Failure "${where}: broken link '$target' ($resolved does not exist)"
            continue
        }
        if (-not $resolved.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-Warning "${where}: '$target' points outside the repository"
        }
        if ($anchor.Length -gt 0 -and $resolved.EndsWith('.md', [System.StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            if (-not (Get-Anchors $resolved).Contains($anchor.ToLowerInvariant())) {
                Add-Warning "${where}: anchor '#$anchor' not found in $(Get-Relative $resolved)"
            }
        }
    }
}

$exitCode = 2
try {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) { throw 'git is not on PATH' }
    $null = & git -C $RepoRoot rev-parse --is-inside-work-tree 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$RepoRoot is not a git work tree" }

    # 1. 必需文档与根 README 的导航
    foreach ($doc in $RequiredDocs) {
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $doc) -PathType Leaf)) { Add-Failure "required document missing: $doc" }
    }
    $readme = Join-Path $RepoRoot 'README.md'
    if (Test-Path -LiteralPath $readme) {
        $readmeTargets = @(Get-LinkTargets $readme | ForEach-Object { $_.Target })
        foreach ($pattern in $ReadmeMustLink) {
            if (-not ($readmeTargets | Where-Object { $_ -match $pattern })) { Add-Failure "README.md must link to $($pattern -replace '\\', '')" }
        }
    }

    # 2. 链接
    $markdown = @(
        Get-Item -LiteralPath (Join-Path $RepoRoot 'README.md'), (Join-Path $RepoRoot 'AGENTS.md') -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'docs') -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue
    )
    foreach ($file in $markdown) { Test-Links $file.FullName }
    Write-Host "checked links in $($markdown.Count) Markdown file(s)"

    # 3. 子目录中的入口文件
    $files = @(& git -C $RepoRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }
    foreach ($path in $files) {
        $leaf = ($path -split '/')[-1]
        if ($path.Contains('/') -and ($leaf -ieq 'README.md' -or $leaf -ieq 'AGENTS.md')) {
            Add-Failure "${path}: README.md and AGENTS.md are only allowed at the repository root (AGENTS.md)"
        }
    }

    # 4. 空白错误
    if (-not $SkipGitDiffCheck) {
        foreach ($mode in @(@('diff', '--check'), @('diff', '--cached', '--check'))) {
            $output = @(& git -C $RepoRoot -c core.quotepath=false @mode 2>&1)
            if ($LASTEXITCODE -ne 0) {
                $problems = @($output | Where-Object { $_ -match ':\d+:' })
                foreach ($problem in $problems | Select-Object -First 50) { Add-Failure "git $($mode -join ' '): $problem" }
                if ($problems.Count -gt 50) { Add-Failure "git $($mode -join ' '): $($problems.Count - 50) more" }
                if ($problems.Count -eq 0) { Add-Failure "git $($mode -join ' ') failed: $($output -join ' ')" }
            }
        }
    }

    foreach ($warning in $warnings) { Write-Host "WARN  $warning" -ForegroundColor DarkYellow }
    foreach ($failure in $failures) { Write-Host "FAIL  $failure" -ForegroundColor Red }
    $failed = $failures.Count -gt 0 -or ($WarningsAsErrors -and $warnings.Count -gt 0)
    Write-Host ("{0}: {1} failure(s), {2} warning(s)" -f $(if ($failed) { 'FAIL' } else { 'PASS' }), $failures.Count, $warnings.Count) `
        -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
    $exitCode = if ($failed) { 1 } else { 0 }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 2
}

exit $exitCode
