param(
    [string]$PackageRoot,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
$DefaultPackageRoot = Join-Path $RepoRoot 'TMP\WorkshopPackage\MooGirlRace'
if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = $DefaultPackageRoot
}

function Write-Step {
    param([string]$Message)
    Write-Host "[package] $Message"
}

function Get-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-SafePackageRoot {
    param(
        [string]$Root,
        [string]$Repo
    )

    $tmpRoot = Get-FullPath (Join-Path $Repo 'TMP\WorkshopPackage')
    $destRoot = Get-FullPath $Root
    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    if (-not $destRoot.StartsWith($tmpRoot, $comparison)) {
        throw "PackageRoot must stay under '$tmpRoot'. Refusing to clean '$destRoot'."
    }
}

function Get-RelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    $rootUri = New-Object System.Uri(((Get-FullPath $Root).TrimEnd('\') + '\'))
    $pathUri = New-Object System.Uri((Get-FullPath $Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Test-PackageExcluded {
    param([string]$RelativePath)

    $normalized = $RelativePath.Replace('\', '/')
    $segments = $normalized.Split('/')
    foreach ($segment in $segments) {
        if ($segment -in @('bin', 'obj')) {
            return $true
        }
    }

    if ($normalized -like '1.6/Source/*') {
        return $true
    }

    $extension = [System.IO.Path]::GetExtension($RelativePath).ToLowerInvariant()
    return $extension -in @('.cs', '.csproj', '.pdb', '.tmp', '.bak', '.sai2', '.user', '.suo')
}

Push-Location $RepoRoot
try {
    if (-not $SkipBuild) {
        Write-Step "Release build"
        $msbuildCandidates = @(
            'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
            'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
            'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
            'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
        )
        $msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $msbuild) {
            $msbuild = 'MSBuild.exe'
        }

        & $msbuild '1.6\Source\MooGirlRace.csproj' /p:Configuration=Release /p:Platform=AnyCPU /nologo /v:m
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Step "Release build skipped"
    }

    Assert-SafePackageRoot -Root $PackageRoot -Repo $RepoRoot
    $destRoot = Get-FullPath $PackageRoot
    $destParent = Split-Path -Parent $destRoot
    if (-not (Test-Path -LiteralPath $destParent)) {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    if (Test-Path -LiteralPath $destRoot) {
        Write-Step "Clean destination"
        Remove-Item -LiteralPath $destRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $destRoot -Force | Out-Null

    $roots = @('About', '1.6', 'Bio_1.6', 'Textures', 'Sounds', 'Versions')
    $rootFiles = @('LoadFolders.xml')
    $copiedFiles = 0
    $excludedFiles = 0

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
            $relative = Get-RelativePath -Root $RepoRoot -Path $_.FullName
            if (Test-PackageExcluded -RelativePath $relative) {
                $script:excludedFiles++
            }
            else {
                $target = Join-Path $destRoot $relative
                $targetDir = Split-Path -Parent $target
                if (-not (Test-Path -LiteralPath $targetDir)) {
                    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
                }

                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
                $script:copiedFiles++
            }
        }
    }

    foreach ($file in $rootFiles) {
        if (-not (Test-Path -LiteralPath $file)) {
            continue
        }

        $target = Join-Path $destRoot $file
        Copy-Item -LiteralPath $file -Destination $target -Force
        $copiedFiles++
    }

    $forbidden = Get-ChildItem -LiteralPath $destRoot -Recurse -File |
        Where-Object {
            $rel = Get-RelativePath -Root $destRoot -Path $_.FullName
            Test-PackageExcluded -RelativePath $rel
        }

    if ($forbidden) {
        $list = ($forbidden | Select-Object -First 20 | ForEach-Object { Get-RelativePath -Root $destRoot -Path $_.FullName }) -join "`n"
        throw "Package contains excluded files:`n$list"
    }

    Write-Step "Package ready"
    Write-Host "Output: $destRoot"
    Write-Host "Copied files: $copiedFiles"
    Write-Host "Excluded files: $excludedFiles"
}
finally {
    Pop-Location
}
