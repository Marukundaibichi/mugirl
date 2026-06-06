param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')
Push-Location $RepoRoot

try {
    function Write-Step {
        param([string]$Message)
        Write-Host "[phase6] $Message"
    }

    function Fail {
        param([string]$Message)
        throw $Message
    }

    function Add-SetValue {
        param(
            [hashtable]$Map,
            [string]$Key,
            [string]$Value
        )

        if (-not $Map.ContainsKey($Key)) {
            $Map[$Key] = New-Object 'System.Collections.Generic.HashSet[string]'
        }

        [void]$Map[$Key].Add($Value)
    }

    function Get-RelativePath {
        param([string]$Path)
        return Resolve-Path -LiteralPath $Path -Relative
    }

    function Get-XmlDocument {
        param([string]$Path)
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.Load($Path)
        return $doc
    }

    function Get-DefRoots {
        return @(
            '..\..\Data\Core\Defs',
            '..\..\Data\Royalty\Defs',
            '..\..\Data\Ideology\Defs',
            '..\..\Data\Biotech\Defs',
            '..\..\Data\Anomaly\Defs',
            '..\..\Data\Odyssey\Defs',
            '1.6\Defs',
            '1.6\FacialAnimation\Defs',
            'Bio_1.6\Defs',
            'Versions\1.6\Integrations\VCookE\Defs'
        ) | Where-Object { Test-Path -LiteralPath $_ }
    }

    function Get-ModDefRoots {
        return @(
            '1.6\Defs',
            '1.6\FacialAnimation\Defs',
            'Bio_1.6\Defs',
            'Versions\1.6\Integrations\VCookE\Defs'
        ) | Where-Object { Test-Path -LiteralPath $_ }
    }

    function Get-VanillaDefRoots {
        return @(
            '..\..\Data\Core\Defs',
            '..\..\Data\Royalty\Defs',
            '..\..\Data\Ideology\Defs',
            '..\..\Data\Biotech\Defs',
            '..\..\Data\Anomaly\Defs',
            '..\..\Data\Odyssey\Defs'
        ) | Where-Object { Test-Path -LiteralPath $_ }
    }

    $ProductionRoots = @('1.6', 'Bio_1.6', 'Versions', 'Textures', 'Sounds') | Where-Object { Test-Path -LiteralPath $_ }
    $DefTypeAliases = @{
        'AlienRace.ThingDef_AlienRace' = 'ThingDef'
        'MooGirl.SlaveApparelDef' = 'ThingDef'
        'AlienRace.AlienBackstoryDef' = 'BackstoryDef'
    }

    Write-Step "Release build"
    if (-not $SkipBuild) {
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
            Fail "Release build failed with exit code $LASTEXITCODE"
        }
    }
    else {
        Write-Step "Release build skipped by parameter"
    }

    Write-Step "XML parse"
    $xmlFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($path in @('LoadFolders.xml')) {
        if (Test-Path -LiteralPath $path) {
            $xmlFiles.Add((Resolve-Path -LiteralPath $path).Path)
        }
    }
    foreach ($root in @('About', '1.6', 'Bio_1.6', 'Versions')) {
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -LiteralPath $root -Recurse -Filter '*.xml' | ForEach-Object {
                $xmlFiles.Add($_.FullName)
            }
        }
    }

    $xmlErrors = @()
    foreach ($file in $xmlFiles) {
        try {
            [void](Get-XmlDocument $file)
        }
        catch {
            $xmlErrors += "$(Get-RelativePath $file): $($_.Exception.Message)"
        }
    }
    if ($xmlErrors.Count) {
        $xmlErrors | Select-Object -First 100
        Fail "XML parse failed: $($xmlErrors.Count) file(s)"
    }

    Write-Step "About metadata"
    $aboutPath = 'About\About.xml'
    if (-not (Test-Path -LiteralPath $aboutPath)) {
        Fail "About/About.xml is missing"
    }
    $aboutDoc = Get-XmlDocument (Resolve-Path -LiteralPath $aboutPath)
    $aboutErrors = @()
    $packageId = $aboutDoc.ModMetaData.packageId
    if ($packageId -ne 'HAR.MuGirlRace') {
        $aboutErrors += "About.xml :: packageId is '$packageId', expected 'HAR.MuGirlRace'"
    }
    $supportedVersions = @($aboutDoc.ModMetaData.supportedVersions.li | ForEach-Object { $_.'#text' ?? $_.InnerText ?? $_ })
    if (-not ($supportedVersions -contains '1.6')) {
        $aboutErrors += "About.xml :: supportedVersions does not include 1.6"
    }

    $dependencyPackageIds = @($aboutDoc.ModMetaData.modDependencies.li | ForEach-Object { $_.packageId })
    foreach ($requiredDependency in @('brrainz.harmony', 'erdelf.humanoidalienraces')) {
        if (-not ($dependencyPackageIds -contains $requiredDependency)) {
            $aboutErrors += "About.xml :: missing dependency $requiredDependency"
        }
    }

    $loadAfterIds = @($aboutDoc.ModMetaData.loadAfter.li | ForEach-Object { $_.'#text' ?? $_.InnerText ?? $_ })
    foreach ($requiredLoadAfter in @('brrainz.harmony', 'Ludeon.RimWorld', 'erdelf.HumanoidAlienRaces')) {
        if (-not ($loadAfterIds -contains $requiredLoadAfter)) {
            $aboutErrors += "About.xml :: loadAfter missing $requiredLoadAfter"
        }
    }

    $incompatibleIds = @($aboutDoc.ModMetaData.incompatibleWith.li | ForEach-Object { $_.'#text' ?? $_.InnerText ?? $_ })
    if (-not ($incompatibleIds -contains 'Luca.MuGirlFacialAnimation')) {
        $aboutErrors += "About.xml :: incompatibleWith missing Luca.MuGirlFacialAnimation"
    }
    if ($aboutErrors.Count) {
        $aboutErrors | Sort-Object
        Fail "About metadata scan failed: $($aboutErrors.Count) issue(s)"
    }

    Write-Step "LoadFolders"
    $loadFoldersPath = 'LoadFolders.xml'
    $loadFolderEntries = 0
    if (-not (Test-Path -LiteralPath $loadFoldersPath)) {
        Fail "LoadFolders.xml is missing"
    }
    $loadFoldersDoc = Get-XmlDocument (Resolve-Path -LiteralPath $loadFoldersPath)
    $loadFolderPaths = New-Object 'System.Collections.Generic.HashSet[string]'
    $loadFolderErrors = @()
    foreach ($node in $loadFoldersDoc.SelectNodes('/loadFolders/v1.6/li')) {
        $loadFolderEntries++
        $path = $node.InnerText.Trim()
        if ([string]::IsNullOrWhiteSpace($path)) {
            $loadFolderErrors += "LoadFolders.xml :: empty v1.6 li"
            continue
        }
        if (-not $loadFolderPaths.Add($path)) {
            $loadFolderErrors += "LoadFolders.xml :: duplicate path $path"
        }
        if (-not (Test-Path -LiteralPath $path)) {
            $loadFolderErrors += "LoadFolders.xml :: missing path $path"
        }
    }

    $requiredLoadGates = @{
        '1.6/FacialAnimation' = 'Nals.FacialAnimation'
        'Bio_1.6' = 'ludeon.rimworld.biotech'
        'Versions/1.6/Integrations/VCookE' = 'VanillaExpanded.VCookE'
        'Versions/1.6/Integrations/SearchAndDestroy' = 'MemeGoddess.SearchAndDestroy'
    }
    foreach ($entry in $requiredLoadGates.GetEnumerator()) {
        $path = $entry.Key
        $expectedGate = $entry.Value
        $matchingNode = $loadFoldersDoc.SelectSingleNode("/loadFolders/v1.6/li[normalize-space(text())='$path']")
        if (-not $matchingNode) {
            $loadFolderErrors += "LoadFolders.xml :: missing gated path $path"
            continue
        }
        $actualGate = if ($matchingNode.Attributes['IfModActive']) { $matchingNode.Attributes['IfModActive'].Value } else { '' }
        if ($actualGate -ne $expectedGate) {
            $loadFolderErrors += "LoadFolders.xml :: $path gate is '$actualGate', expected '$expectedGate'"
        }
    }
    if ($loadFolderErrors.Count) {
        $loadFolderErrors | Sort-Object
        Fail "LoadFolders scan failed: $($loadFolderErrors.Count) issue(s)"
    }

    Write-Step "Forbidden production patterns"
    $forbidden = 'Apperal|Heiffs|\bHai\b|Gloden|MechanoidWorkControlSettings|allArmorDefs|milking\(V1\)|south \.png|EyeInHead _backpack|Ldloc_S|MooGirlSkinApplied|Analyzer'
    $hits = & rg -n $forbidden @ProductionRoots
    if ($LASTEXITCODE -eq 0) {
        $hits
        Fail "Forbidden production pattern(s) found"
    }
    elseif ($LASTEXITCODE -ne 1) {
        Fail "rg failed during forbidden pattern scan with exit code $LASTEXITCODE"
    }

    Write-Step "Git diff whitespace"
    $diffCheck = & git diff --check 2>&1
    if ($LASTEXITCODE -ne 0) {
        $diffCheck
        Fail "git diff --check failed"
    }

    Write-Step "csproj source references"
    $projPath = '1.6\Source\MooGirlRace.csproj'
    $proj = Get-XmlDocument (Resolve-Path -LiteralPath $projPath)
    $projDir = Resolve-Path -LiteralPath '1.6\Source'
    $compileItems = @($proj.Project.ItemGroup.Compile | ForEach-Object { $_.Include } | Where-Object { $_ })
    $missingCompileItems = @()
    foreach ($include in $compileItems) {
        $path = Join-Path $projDir $include
        if (-not (Test-Path -LiteralPath $path)) {
            $missingCompileItems += $include
        }
    }

    $includeSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($include in $compileItems) {
        [void]$includeSet.Add((Join-Path '1.6\Source' $include).Replace('/', '\'))
    }

    $notIncluded = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\').Replace('/', '\')
            if (-not $includeSet.Contains($relative)) {
                $notIncluded += $relative
            }
        }

    if ($missingCompileItems.Count -or $notIncluded.Count) {
        if ($missingCompileItems.Count) {
            "Missing Compile Include:"
            $missingCompileItems | Sort-Object
        }
        if ($notIncluded.Count) {
            "Source files not in csproj:"
            $notIncluded | Sort-Object
        }
        Fail "csproj source reference scan failed"
    }

    Write-Step "MooGirl XML type references"
    $classNames = New-Object 'System.Collections.Generic.HashSet[string]'
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8
        foreach ($match in [regex]::Matches($text, '\b(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')) {
            [void]$classNames.Add($match.Groups[1].Value)
        }
    }

    $typeFields = @(
        'Class', 'class', 'thingClass', 'compClass', 'driverClass', 'workerClass',
        'workerClassName', 'stateClass', 'incidentWorkerClass', 'questNodeClass',
        'modClass', 'racePartInfoClass', 'worldObjectClass', 'placeWorkerClass',
        'designatorClass', 'gameConditionClass'
    )
    $typeRefs = New-Object 'System.Collections.Generic.HashSet[string]'
    $missingTypeRefs = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6', 'Bio_1.6', 'Versions' -Recurse -Filter '*.xml') {
        $doc = Get-XmlDocument $file.FullName
        foreach ($node in $doc.SelectNodes('//*')) {
            foreach ($field in $typeFields) {
                if ($node.Attributes -and $node.Attributes[$field]) {
                    $value = $node.Attributes[$field].Value.Trim()
                    if ($value.StartsWith('MooGirl.')) {
                        [void]$typeRefs.Add($value)
                        $shortName = $value.Substring('MooGirl.'.Length).Split(',')[0].Trim()
                        if (-not $classNames.Contains($shortName)) {
                            $missingTypeRefs += "$(Get-RelativePath $file.FullName) :: @$field=$value"
                        }
                    }
                }
            }
            if ($typeFields -contains $node.LocalName) {
                $value = $node.InnerText.Trim()
                if ($value.StartsWith('MooGirl.')) {
                    [void]$typeRefs.Add($value)
                    $shortName = $value.Substring('MooGirl.'.Length).Split(',')[0].Trim()
                    if (-not $classNames.Contains($shortName)) {
                        $missingTypeRefs += "$(Get-RelativePath $file.FullName) :: <$($node.LocalName)>$value"
                    }
                }
            }
        }
    }
    if ($missingTypeRefs.Count) {
        $missingTypeRefs | Sort-Object | Select-Object -First 120
        Fail "MooGirl XML type cross-check failed: $($missingTypeRefs.Count) missing reference(s)"
    }

    Write-Step "Def index"
    $defsByType = @{}
    $concreteModDefs = @{}
    $concreteVanillaDefs = @{}
    $modDuplicateDefs = @()
    $modVanillaOverrides = @()
    foreach ($root in Get-DefRoots) {
        $isModRoot = (Get-ModDefRoots | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }) -contains (Resolve-Path -LiteralPath $root).Path
        Get-ChildItem -LiteralPath $root -Recurse -Filter '*.xml' | ForEach-Object {
            $doc = Get-XmlDocument $_.FullName
            if ($null -eq $doc.DocumentElement) {
                return
            }
            foreach ($node in @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })) {
                $xmlType = $node.LocalName
                $defType = if ($DefTypeAliases.ContainsKey($xmlType)) { $DefTypeAliases[$xmlType] } else { $xmlType }
                $defName = $null
                $defNameNode = $node.SelectSingleNode('defName')
                if ($defNameNode -and -not [string]::IsNullOrWhiteSpace($defNameNode.InnerText)) {
                    $defName = $defNameNode.InnerText.Trim()
                }
                elseif ($node.Attributes -and $node.Attributes['Name'] -and $node.Attributes['Abstract'] -and $node.Attributes['Abstract'].Value -eq 'True') {
                    $defName = $node.Attributes['Name'].Value.Trim()
                }
                if ([string]::IsNullOrWhiteSpace($defName)) {
                    continue
                }

                Add-SetValue $defsByType $defType $defName

                $isAbstract = $node.Attributes -and $node.Attributes['Abstract'] -and $node.Attributes['Abstract'].Value -eq 'True'
                if (-not $isAbstract -and $defNameNode) {
                    $key = "$defType::$defName"
                    $relative = Get-RelativePath $_.FullName
                    if ($isModRoot) {
                        if ($concreteModDefs.ContainsKey($key)) {
                            $modDuplicateDefs += "$key :: $($concreteModDefs[$key]) && $relative"
                        }
                        else {
                            $concreteModDefs[$key] = $relative
                        }

                        if ($concreteVanillaDefs.ContainsKey($key)) {
                            $modVanillaOverrides += "$key :: vanilla $($concreteVanillaDefs[$key]) && mod $relative"
                        }
                    }
                    else {
                        if (-not $concreteVanillaDefs.ContainsKey($key)) {
                            $concreteVanillaDefs[$key] = $relative
                        }
                    }
                }
            }
        }
    }

    $requiredIndexedDefs = @(
        @('BackstoryDef', 'MooGirl_ChildSlaveBackStory'),
        @('MentalStateDef', 'MooGirl_BrainWashing'),
        @('ThingDef', 'MooGirl'),
        @('ThingDef', 'CaravanPackingSpot')
    )
    foreach ($pair in $requiredIndexedDefs) {
        if (-not $defsByType.ContainsKey($pair[0]) -or -not $defsByType[$pair[0]].Contains($pair[1])) {
            Fail "Def scanner self-check failed: missing $($pair[0])/$($pair[1])"
        }
    }
    if ($modDuplicateDefs.Count -or $modVanillaOverrides.Count) {
        if ($modDuplicateDefs.Count) {
            "Duplicate mod concrete Defs:"
            $modDuplicateDefs | Sort-Object | Select-Object -First 100
        }
        if ($modVanillaOverrides.Count) {
            "Mod concrete Defs overriding vanilla/DLC Defs:"
            $modVanillaOverrides | Sort-Object | Select-Object -First 100
        }
        Fail "Concrete Def collision scan failed: $($modDuplicateDefs.Count) mod duplicate(s), $($modVanillaOverrides.Count) vanilla override(s)"
    }

    Write-Step "Patch XPath syntax"
    $patchFiles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($patchRoot in @('1.6\Patches', '1.6\FacialAnimation\Patches', 'Versions\1.6')) {
        if (Test-Path -LiteralPath $patchRoot) {
            Get-ChildItem -LiteralPath $patchRoot -Recurse -Filter '*.xml' | ForEach-Object {
                $patchFiles.Add($_.FullName)
            }
        }
    }

    $xpathSyntaxErrors = @()
    $xpathCount = 0
    foreach ($patchFile in $patchFiles) {
        $doc = Get-XmlDocument $patchFile
        foreach ($xpathNode in $doc.SelectNodes('//xpath')) {
            $xpath = $xpathNode.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($xpath)) {
                continue
            }
            $xpathCount++
            try {
                [void][System.Xml.XPath.XPathExpression]::Compile($xpath)
            }
            catch {
                $xpathSyntaxErrors += "$(Get-RelativePath $patchFile) :: $xpath :: $($_.Exception.Message)"
            }
        }
    }
    if ($xpathSyntaxErrors.Count) {
        $xpathSyntaxErrors | Sort-Object | Select-Object -First 120
        Fail "Patch XPath syntax scan failed: $($xpathSyntaxErrors.Count) invalid xpath(s)"
    }

    Write-Step "Direct PatchOperationAdd targets"
    $missingPatchTargets = @()
    $externalPatchTargets = @()
    $checkedPatchTargets = 0
    foreach ($patch in $patchFiles) {
        $doc = Get-XmlDocument $patch
        foreach ($operation in $doc.SelectNodes('//*[@Class="PatchOperationAdd"]')) {
            $xpathNode = $operation.SelectSingleNode('xpath')
            if (-not $xpathNode) {
                continue
            }
            $xpath = $xpathNode.InnerText.Trim()
            if ($xpath -notmatch '^/?Defs/([A-Za-z0-9_.]+)\[defName="([^"]+)"\](/|$)') {
                continue
            }
            $checkedPatchTargets++
            $rawDefType = $matches[1]
            $defType = if ($DefTypeAliases.ContainsKey($rawDefType)) { $DefTypeAliases[$rawDefType] } else { $rawDefType }
            $defName = $matches[2]
            if (-not $defsByType.ContainsKey($defType) -or -not $defsByType[$defType].Contains($defName)) {
                $relativePatch = Get-RelativePath $patch
                if ($relativePatch -match '\\Versions\\1\.6\\Integrations\\') {
                    $externalPatchTargets += "$relativePatch :: $xpath"
                }
                else {
                    $missingPatchTargets += "$relativePatch :: $xpath"
                }
            }
        }
    }
    if ($missingPatchTargets.Count) {
        $missingPatchTargets | Sort-Object
        Fail "Direct PatchOperationAdd target scan failed: $($missingPatchTargets.Count) missing target(s)"
    }

    Write-Step "Keyed translations"
    $directKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    $broadKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    $skipPrefixes = @(
        'MooGirl.Comp', 'MooGirl.Hediff', 'MooGirl.JobDriver', 'MooGirl.Quest',
        'MooGirl.CompProperties', 'MooGirl.Thought', 'MooGirl.Incident',
        'MooGirl.MooGirl', 'MooGirl.SlaveApparel', 'MooGirl.GameComponent',
        'MooGirl.MapComponent', 'MooGirl.PawnRender', 'MooGirl.Command',
        'MooGirl.Building', 'MooGirl.Thing', 'MooGirl.Verb', 'MooGirl.Stat',
        'MooGirl.Work', 'MooGirl.Dialog', 'MooGirl.Mental', 'MooGirl.Pawn'
    )
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8
        foreach ($match in [regex]::Matches($text, '"(MooGirl\.[A-Za-z0-9_.-]+)"\s*\.Translate\s*\(')) {
            [void]$directKeys.Add($match.Groups[1].Value)
        }
        foreach ($match in [regex]::Matches($text, '"(MooGirl\.[A-Za-z0-9_.-]+)"')) {
            $key = $match.Groups[1].Value
            $isType = $false
            foreach ($prefix in $skipPrefixes) {
                if ($key.StartsWith($prefix)) {
                    $isType = $true
                    break
                }
            }
            if (-not $isType) {
                [void]$broadKeys.Add($key)
            }
        }
    }

    $missingKeyed = @()
    foreach ($language in @('English', 'ChineseSimplified')) {
        $languageKeys = New-Object 'System.Collections.Generic.HashSet[string]'
        Get-ChildItem -LiteralPath "1.6\Languages\$language\Keyed" -Recurse -Filter '*.xml' | ForEach-Object {
            $doc = Get-XmlDocument $_.FullName
            foreach ($node in @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })) {
                [void]$languageKeys.Add($node.LocalName)
            }
        }
        foreach ($key in $directKeys) {
            if (-not $languageKeys.Contains($key)) {
                $missingKeyed += "$language direct :: $key"
            }
        }
        foreach ($key in $broadKeys) {
            if (-not $languageKeys.Contains($key)) {
                $missingKeyed += "$language broad :: $key"
            }
        }
    }
    if ($missingKeyed.Count) {
        $missingKeyed | Sort-Object
        Fail "Keyed translation scan failed: $($missingKeyed.Count) missing key(s)"
    }

    Write-Step "DefInjected translations"
    $orphanInjected = @()
    Get-ChildItem -LiteralPath '1.6\Languages' -Recurse -Filter '*.xml' |
        Where-Object { $_.FullName -match '\\DefInjected\\' } |
        ForEach-Object {
            $full = $_.FullName
            $parts = $full -split '[\\/]'
            $index = [Array]::IndexOf($parts, 'DefInjected')
            if ($index -lt 0 -or $index + 1 -ge $parts.Length) {
                return
            }
            $defType = $parts[$index + 1]
            $knownDefs = if ($defsByType.ContainsKey($defType)) { $defsByType[$defType] } else { New-Object 'System.Collections.Generic.HashSet[string]' }
            $doc = Get-XmlDocument $full
            foreach ($node in @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })) {
                $key = $node.LocalName
                $defName = ($key -split '\.')[0]
                if (-not $knownDefs.Contains($defName)) {
                    $orphanInjected += "$(Get-RelativePath $full) :: $key"
                }
            }
        }
    if ($orphanInjected.Count) {
        $orphanInjected | Sort-Object | Select-Object -First 200
        Fail "DefInjected orphan scan failed: $($orphanInjected.Count) candidate(s)"
    }

    Write-Step "Sound clip paths"
    $missingClips = @()
    $clipPathCount = 0
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Defs', 'Bio_1.6\Defs', 'Versions\1.6' -Recurse -Filter '*.xml' -ErrorAction SilentlyContinue) {
        $doc = Get-XmlDocument $file.FullName
        foreach ($node in $doc.SelectNodes('//clipPath')) {
            $path = $node.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }
            $clipPathCount++
            $candidates = @(
                "Sounds\$path.ogg", "Sounds\$path.wav", "Sounds\$path.mp3",
                "1.6\Sounds\$path.ogg", "1.6\Sounds\$path.wav", "1.6\Sounds\$path.mp3"
            )
            $exists = $false
            foreach ($candidate in $candidates) {
                if (Test-Path -LiteralPath $candidate) {
                    $exists = $true
                    break
                }
            }
            if (-not $exists) {
                $missingClips += "$(Get-RelativePath $file.FullName) :: clipPath=$path"
            }
        }
    }
    if ($missingClips.Count) {
        $missingClips | Sort-Object
        Fail "Sound clipPath scan failed: $($missingClips.Count) missing clip(s)"
    }

    Write-Step "Texture paths"
    $textureRoots = @('Textures', '1.6\Textures', 'Bio_1.6\Textures', 'Versions\1.6\Textures') | Where-Object { Test-Path -LiteralPath $_ }
    $vanillaPaths = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($root in @('..\..\Data\Core\Defs', '..\..\Data\Royalty\Defs', '..\..\Data\Ideology\Defs', '..\..\Data\Biotech\Defs', '..\..\Data\Anomaly\Defs', '..\..\Data\Odyssey\Defs') | Where-Object { Test-Path -LiteralPath $_ }) {
        Get-ChildItem -LiteralPath $root -Recurse -Filter '*.xml' | ForEach-Object {
            $doc = Get-XmlDocument $_.FullName
            foreach ($node in $doc.SelectNodes('//texPath|//iconPath|//uiIconPath')) {
                $path = $node.InnerText.Trim()
                if ($path) {
                    [void]$vanillaPaths.Add($path)
                }
            }
        }
    }

    function Test-TexturePath {
        param(
            [string]$Root,
            [string]$Path
        )
        $basePath = Join-Path $Root $Path
        foreach ($extension in @('.png', '.tga', '.jpg', '.jpeg')) {
            if (Test-Path -LiteralPath "$basePath$extension") {
                return $true
            }
        }
        foreach ($suffix in @('_north', '_south', '_east', '_west', '_backpack', '_front', '_side')) {
            foreach ($extension in @('.png', '.tga', '.jpg', '.jpeg')) {
                if (Test-Path -LiteralPath "$basePath$suffix$extension") {
                    return $true
                }
            }
        }
        if (Test-Path -LiteralPath $basePath) {
            $image = Get-ChildItem -LiteralPath $basePath -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in @('.png', '.tga', '.jpg', '.jpeg') } |
                Select-Object -First 1
            if ($image) {
                return $true
            }
        }
        return $false
    }

    $texturePathCount = 0
    $vanillaTextureRefs = New-Object 'System.Collections.Generic.HashSet[string]'
    $missingTextures = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Defs', 'Bio_1.6\Defs', 'Versions\1.6' -Recurse -Filter '*.xml' -ErrorAction SilentlyContinue) {
        $doc = Get-XmlDocument $file.FullName
        foreach ($node in $doc.SelectNodes('//texPath|//iconPath|//uiIconPath')) {
            $path = $node.InnerText.Trim()
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }
            $texturePathCount++
            $local = $false
            foreach ($root in $textureRoots) {
                if (Test-TexturePath $root $path) {
                    $local = $true
                    break
                }
            }
            if ($local) {
                continue
            }
            if ($vanillaPaths.Contains($path)) {
                [void]$vanillaTextureRefs.Add($path)
                continue
            }
            $missingTextures += "$(Get-RelativePath $file.FullName) :: <$($node.LocalName)>$path"
        }
    }
    if ($missingTextures.Count) {
        $missingTextures | Sort-Object | Select-Object -First 200
        Fail "Texture path scan failed: $($missingTextures.Count) missing texture path(s)"
    }

    Write-Step "Publish hygiene"
    $dirtyPublishItems = @()
    Get-ChildItem -LiteralPath $ProductionRoots -Recurse -Force |
        Where-Object { $_.PSIsContainer -and $_.FullName -notmatch '\\1\.6\\Source(\\|$)' -and $_.Name -in @('bin', 'obj') } |
        ForEach-Object { $dirtyPublishItems += "dir $(Get-RelativePath $_.FullName)" }
    Get-ChildItem -LiteralPath $ProductionRoots -Recurse -Force -File |
        Where-Object { $_.FullName -notmatch '\\1\.6\\Source(\\|$)' -and $_.Extension -in @('.pdb', '.tmp', '.bak') } |
        ForEach-Object { $dirtyPublishItems += "file $(Get-RelativePath $_.FullName)" }
    if ($dirtyPublishItems.Count) {
        $dirtyPublishItems | Sort-Object
        Fail "Publish hygiene scan failed: $($dirtyPublishItems.Count) item(s)"
    }

    Write-Host ""
    Write-Host "Phase 6 static validation OK"
    Write-Host "  Compile items: $($compileItems.Count)"
    Write-Host "  LoadFolders v1.6 entries: $loadFolderEntries"
    Write-Host "  MooGirl XML type refs: $($typeRefs.Count)"
    Write-Host "  Patch xpath nodes: $xpathCount"
    Write-Host "  Direct PatchOperationAdd targets: $checkedPatchTargets"
    Write-Host "  Optional external PatchOperationAdd targets: $($externalPatchTargets.Count)"
    Write-Host "  Mod concrete defs: $($concreteModDefs.Count)"
    Write-Host "  Direct keyed keys: $($directKeys.Count)"
    Write-Host "  Broad keyed keys: $($broadKeys.Count)"
    Write-Host "  Def types indexed: $($defsByType.Keys.Count)"
    Write-Host "  clipPath nodes: $clipPathCount"
    Write-Host "  texture paths: $texturePathCount"
    Write-Host "  vanilla texture refs: $($vanillaTextureRefs.Count)"
}
finally {
    Pop-Location
}
