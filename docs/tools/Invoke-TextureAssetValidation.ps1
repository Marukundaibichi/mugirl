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

# FA 脸红采用 Talos 修改版的 cover 叠层方案（2026-09-21 引入，2026-09-24 依据
# TMP/FacialAnimation_0921_Talos修改版 参考包确认并固化为基线；参考包与
# 1.6/FacialAnimation 717 个文件逐字节一致，仅 Mugirl_Shapes.xml 行尾空格差异）。
# 动画仍以 headShapeDef 引用 blush/lovinblush；基础头图回退 normal，
# 脸红由 {shape}_cover_{east,south} 叠层绘制。旧的 42 张烘焙完整头图与
# Emotions 下的脸红素材属于已废弃方案（0.6.4-0.6.5），不得回流。
$headTextureRoot = Join-Path $faTextureRoot 'FA\Heads_Blank'
$faceVariants = @('Normal', 'Normal2', 'Normal3', 'Normal4', 'Normal5', 'Normal6', 'Normal7')
$blushShapes = @('blush', 'lovinblush')
$coverDirections = @('east', 'south')
$bakedDirections = @('north', 'east', 'south', 'west')
$missingBlushCovers = @()
$legacyBakedHeads = @()
foreach ($variant in $faceVariants) {
    $variantRoot = Join-Path (Join-Path $headTextureRoot $variant) 'Female'
    foreach ($shape in $blushShapes) {
        foreach ($direction in $coverDirections) {
            $asset = Join-Path $variantRoot "${shape}_cover_${direction}.png"
            if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { $missingBlushCovers += $asset }
        }
        foreach ($direction in $bakedDirections) {
            $asset = Join-Path $variantRoot "${shape}_${direction}.png"
            if (Test-Path -LiteralPath $asset -PathType Leaf) { $legacyBakedHeads += $asset }
        }
    }
}
if ($missingBlushCovers.Count) {
    throw "Missing FA blush cover overlays ($($missingBlushCovers.Count)): $($missingBlushCovers -join ', ')"
}
if ($legacyBakedHeads.Count) {
    throw "Legacy baked FA blush heads must not return; blush renders via cover overlays: $($legacyBakedHeads -join ', ')"
}
$legacyEmotionBlush = @()
$emotionsRoot = Join-Path $faTextureRoot 'FA\Emotions'
if (Test-Path -LiteralPath $emotionsRoot) {
    foreach ($shape in $blushShapes) {
        $legacyEmotionBlush += @(Get-ChildItem -LiteralPath $emotionsRoot -Recurse -File -Filter "${shape}_*.png" |
            Select-Object -ExpandProperty FullName)
    }
}
if ($legacyEmotionBlush.Count) {
    throw "Legacy FA emotion blush textures must not return; blush uses head cover overlays: $($legacyEmotionBlush -join ', ')"
}
Write-Host '[textures] Passed: 28 FA blush/lovinblush cover overlays present; no baked-head or emotion blush regressions.'

# 9.20 服装资源契约：这里只检查源码约定和打包文件，合并后的 Def 另由启动验证检查。
# 身体和自定义饰品层会追加体型；useWornGraphicMask 只控制染色遮罩，不能省略体型后缀。
[xml]$newApparel = Get-Content -LiteralPath (Join-Path $repoPath '1.6\Defs\Apparel\Apparel_0920.xml') -Raw
$apparelTextureCount = 0
$missingApparelTextures = @()
foreach ($definition in $newApparel.Defs.ThingDef | Where-Object { $_.defName }) {
    if ($definition.apparel.developmentalStageFilter -ne 'Adult') {
        throw "9.20 apparel must be adult-only: $($definition.defName)"
    }
    $path = Join-Path (Join-Path $repoPath 'Textures') $definition.apparel.wornGraphicPath
    $bodySuffixes = @('')
    $apparelLayers = @($definition.apparel.layers.li)
    $usesHeadGraphic = $definition.ParentName -in @('Mugirl_HeadBase', 'Mugirl_ArmorHelmetBase') `
        -or $apparelLayers -contains 'Overhead' `
        -or $apparelLayers -contains 'EyeCover'
    if (-not $usesHeadGraphic) {
        $bodySuffixes = @('_Female')
    }
    $directions = @('north', 'east', 'south')
    if ($definition.defName -eq 'Mugirl_HighCutSweater') { $directions += 'west' }
    $maskSuffixes = @('')
    if ($definition.apparel.useWornGraphicMask -eq 'true') { $maskSuffixes += 'm' }
    foreach ($bodySuffix in $bodySuffixes) {
        foreach ($direction in $directions) {
            foreach ($maskSuffix in $maskSuffixes) {
                $asset = "${path}${bodySuffix}_${direction}${maskSuffix}.png"
                if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { $missingApparelTextures += $asset }
                $apparelTextureCount++
            }
        }
    }
}
if ($missingApparelTextures.Count) {
    throw "Missing worn apparel textures ($($missingApparelTextures.Count)): $($missingApparelTextures -join ', ')"
}
Write-Host "[textures] Passed: $apparelTextureCount worn apparel direction/body-type/mask paths."

$pngCount = 0
$residual555 = @()
$legacyFa555Count = 0
foreach ($root in @('Textures', '1.6\FacialAnimation\Textures')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repoPath $root) -Recurse -File -Filter '*.png') {
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $header = New-Object byte[] 24
            if ($stream.Read($header, 0, 24) -ne 24) { throw "Invalid PNG: $($file.FullName)" }
            $width = [int64]$header[16] * 16777216 + $header[17] * 65536 + $header[18] * 256 + $header[19]
            $height = [int64]$header[20] * 16777216 + $header[21] * 65536 + $header[22] * 256 + $header[23]
            if ($width -eq 555 -and $height -eq 555) {
                # FA 已回退到 0.5.0，保留历史原图尺寸；其他资源仍执行优化检查。
                if ($file.FullName.StartsWith($faTextureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                    $legacyFa555Count++
                }
                else { $residual555 += $file.FullName }
            }
            $pngCount++
        }
        finally { $stream.Dispose() }
    }
}
if ($residual555.Count) { throw "Unoptimized 555x555 textures remain: $($residual555 -join ', ')" }
Write-Host "[textures] Passed: $pngCount PNG headers, $referenceCount FA source references, conditional loading, $legacyFa555Count original 0.5.0 FA textures at 555x555; no 555x555 textures outside FA."
