$ErrorActionPreference = 'Stop'
$repoPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$faTextureRoot = Join-Path $repoPath '1.6\FacialAnimation\Textures'

# This checks package source references and assets, not runtime-merged gameplay Def values.
[xml]$loadFolders = Get-Content -LiteralPath (Join-Path $repoPath 'LoadFolders.xml') -Raw
$faFolder = @($loadFolders.loadFolders.'v1.6'.li | Where-Object {
    $_.InnerText -eq '1.6/FacialAnimation' -and $_.IfModActive -eq 'Nals.FacialAnimation'
})
if ($faFolder.Count -ne 1 -or (Test-Path -LiteralPath (Join-Path $repoPath 'Textures\FA'))) {
    throw 'FA textures must exist only in the FacialAnimation conditional load folder.'
}

$referenceCount = 0
$roots = @('1.6', 'Bio_1.6', 'Odyssey_1.6', 'Versions')
foreach ($root in $roots) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoPath $root) -Recurse -File -Filter '*.xml') {
        [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($node in $document.SelectNodes('//*[not(*)]')) {
            $reference = $node.InnerText.Trim().Replace('\', '/')
            if (-not $reference.StartsWith('FA/', [StringComparison]::Ordinal)) { continue }
            if (-not $file.FullName.StartsWith((Join-Path $repoPath '1.6\FacialAnimation') + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unconditional source reference to FA asset: $($file.FullName) :: $reference"
            }
            $assetPath = [IO.Path]::GetFullPath((Join-Path $faTextureRoot $reference))
            if (-not $assetPath.StartsWith($faTextureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Asset reference outside FA texture directory: $reference"
            }
            if (-not (Test-Path -LiteralPath $assetPath)) { throw "Missing FA asset folder: $reference" }
            if (-not (Get-ChildItem -LiteralPath $assetPath -Recurse -File -Filter '*.png' | Select-Object -First 1)) {
                throw "FA asset folder is empty: $reference"
            }
            $referenceCount++
        }
    }
}

$pngCount = 0
$residual555 = @()
foreach ($root in @('Textures', '1.6\Textures', '1.6\FacialAnimation\Textures')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoPath $root) -Recurse -File -Filter '*.png') {
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $header = New-Object byte[] 24
            if ($stream.Read($header, 0, 24) -ne 24) { throw "Invalid PNG: $($file.FullName)" }
            $width = [int64]$header[16] * 16777216 + $header[17] * 65536 + $header[18] * 256 + $header[19]
            $height = [int64]$header[20] * 16777216 + $header[21] * 65536 + $header[22] * 256 + $header[23]
            if ($width -eq 555 -and $height -eq 555) { $residual555 += $file.FullName }
            $pngCount++
        }
        finally { $stream.Dispose() }
    }
}
if ($residual555.Count) { throw "Unoptimized 555x555 textures remain: $($residual555 -join ', ')" }
Write-Host "[textures] Passed: $pngCount PNG headers, $referenceCount FA source references, conditional loading, no 555x555 textures."
