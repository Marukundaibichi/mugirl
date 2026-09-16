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
            'Odyssey_1.6\Defs',
            'Versions\1.6\Integrations\VCookE\Defs'
        ) | Where-Object { Test-Path -LiteralPath $_ }
    }

    function Get-ModDefRoots {
        return @(
            '1.6\Defs',
            '1.6\FacialAnimation\Defs',
            'Bio_1.6\Defs',
            'Odyssey_1.6\Defs',
            'Versions\1.6\Integrations\VCookE\Defs'
        ) | Where-Object { Test-Path -LiteralPath $_ }
    }

    function Get-LanguageRoots {
        return @(
            '1.6\Languages',
            '1.6\FacialAnimation\Languages',
            'Bio_1.6\Languages',
            'Odyssey_1.6\Languages',
            'Versions\1.6\Integrations\VCookE\Languages',
            'Versions\1.6\Integrations\SearchAndDestroy\Languages'
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

    $ProductionRoots = @('1.6', 'Bio_1.6', 'Odyssey_1.6', 'Versions', 'Textures', 'Sounds') | Where-Object { Test-Path -LiteralPath $_ }
    $DefTypeAliases = @{
        'AlienRace.ThingDef_AlienRace' = 'ThingDef'
        'Mugirl.SlaveApparelDef' = 'ThingDef'
        'AlienRace.AlienBackstoryDef' = 'BackstoryDef'
    }
    function Get-NormalizedDefType {
        param([string]$XmlType)

        if ($DefTypeAliases.ContainsKey($XmlType)) {
            return $DefTypeAliases[$XmlType]
        }
        return $XmlType
    }

    Write-Step "Release build"
    if (-not $SkipBuild) {
        $programFiles = [Environment]::GetFolderPath('ProgramFiles')
        $msbuildCandidates = @(
            (Join-Path $programFiles 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
            (Join-Path $programFiles 'Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'),
            (Join-Path $programFiles 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
            (Join-Path $programFiles 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe')
        )
        $msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (-not $msbuild) {
            $msbuild = 'MSBuild.exe'
        }

        & $msbuild '1.6\Source\MugirlRace.csproj' /p:Configuration=Release /p:Platform=AnyCPU /nologo /v:m
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
    foreach ($root in @('About', '1.6', 'Bio_1.6', 'Odyssey_1.6', 'Versions')) {
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

    Write-Step "Language duplicate keys"
    $languageTranslationNodes = 0
    $languageDuplicateKeys = @()
    foreach ($languageRoot in Get-LanguageRoots) {
        Get-ChildItem -LiteralPath $languageRoot -Recurse -Filter '*.xml' | ForEach-Object {
            $full = $_.FullName
            $doc = Get-XmlDocument $full
            $names = @{}
            foreach ($node in @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })) {
                $languageTranslationNodes++
                $key = $node.LocalName
                if (-not $names.ContainsKey($key)) {
                    $names[$key] = 0
                }
                $names[$key]++
            }

            foreach ($entry in $names.GetEnumerator() | Where-Object { $_.Value -gt 1 }) {
                $languageDuplicateKeys += "$(Get-RelativePath $full) :: $($entry.Key) x$($entry.Value)"
            }
        }
    }
    if ($languageDuplicateKeys.Count) {
        $languageDuplicateKeys | Sort-Object
        Fail "Language duplicate key scan failed: $($languageDuplicateKeys.Count) duplicate key(s)"
    }

    Write-Step "Language load-folder path shadowing"
    $languageRelativeFiles = @()
    foreach ($languageRoot in Get-LanguageRoots) {
        $rootPath = (Resolve-Path -LiteralPath $languageRoot).Path
        Get-ChildItem -LiteralPath $languageRoot -Recurse -Filter '*.xml' | ForEach-Object {
            $languageRelativeFiles += [pscustomobject]@{
                Relative = $_.FullName.Substring($rootPath.Length).TrimStart([char[]]"\/")
                Path = Get-RelativePath $_.FullName
            }
        }
    }

    $languageShadowingIssues = @()
    foreach ($group in $languageRelativeFiles | Group-Object Relative | Where-Object { $_.Count -gt 1 }) {
        $paths = ($group.Group | ForEach-Object { $_.Path } | Sort-Object) -join ', '
        $languageShadowingIssues += "$($group.Name) :: $paths"
    }
    if ($languageShadowingIssues.Count) {
        $languageShadowingIssues | Sort-Object
        Fail "Language load-folder path shadowing scan failed: $($languageShadowingIssues.Count) duplicate relative path(s)"
    }

    Write-Step "Language parity"
    function Get-LanguageNodeMap {
        param([string]$Path)

        $map = @{}
        $doc = Get-XmlDocument $Path
        foreach ($node in @($doc.DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element })) {
            $map[$node.LocalName] = $node.InnerText
        }

        return $map
    }

    function Get-TranslationPlaceholderSet {
        param([string]$Text)

        $set = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($match in [regex]::Matches($Text, '\{\d+\}')) {
            [void]$set.Add($match.Value)
        }

        return ,$set
    }

    $languageParityKeys = 0
    $languageParityIssues = @()
    $englishRoot = Resolve-Path -LiteralPath '1.6\Languages\English'
    $chineseRootPath = '1.6\Languages\ChineseSimplified'
    foreach ($file in Get-ChildItem -LiteralPath $englishRoot -Recurse -Filter '*.xml') {
        $relative = $file.FullName.Substring($englishRoot.Path.Length).TrimStart('\', '/')
        $chineseFile = Join-Path $chineseRootPath $relative
        if (-not (Test-Path -LiteralPath $chineseFile)) {
            $languageParityIssues += "$relative :: missing ChineseSimplified file"
            continue
        }

        $englishKeys = Get-LanguageNodeMap $file.FullName
        $chineseKeys = Get-LanguageNodeMap $chineseFile
        foreach ($key in $englishKeys.Keys) {
            $languageParityKeys++
            if (-not $chineseKeys.ContainsKey($key)) {
                $languageParityIssues += "$relative :: missing ChineseSimplified key $key"
                continue
            }

            $englishPlaceholders = Get-TranslationPlaceholderSet $englishKeys[$key]
            $chinesePlaceholders = Get-TranslationPlaceholderSet $chineseKeys[$key]
            foreach ($placeholder in $englishPlaceholders) {
                if (-not $chinesePlaceholders.Contains($placeholder)) {
                    $languageParityIssues += "$relative :: $key missing placeholder $placeholder"
                }
            }
            foreach ($placeholder in $chinesePlaceholders) {
                if (-not $englishPlaceholders.Contains($placeholder)) {
                    $languageParityIssues += "$relative :: $key extra placeholder $placeholder"
                }
            }
        }
    }
    if ($languageParityIssues.Count) {
        $languageParityIssues | Sort-Object | Select-Object -First 120
        Fail "Language parity scan failed: $($languageParityIssues.Count) issue(s)"
    }

    Write-Step "About metadata"
    $aboutPath = 'About\About.xml'
    if (-not (Test-Path -LiteralPath $aboutPath)) {
        Fail "About/About.xml is missing"
    }
    $aboutDoc = Get-XmlDocument (Resolve-Path -LiteralPath $aboutPath)
    $aboutErrors = @()
    $packageId = $aboutDoc.ModMetaData.packageId
    if ($packageId -ne 'HAR.MugirlRace') {
        $aboutErrors += "About.xml :: packageId is '$packageId', expected 'HAR.MugirlRace'"
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
    if (-not ($incompatibleIds -contains 'Luca.MugirlFacialAnimation')) {
        $aboutErrors += "About.xml :: incompatibleWith missing Luca.MugirlFacialAnimation"
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

    Write-Step "Maintenance documentation"
    $architectureDocumentationChecks = 0
    $architectureDocumentationIssues = @()
    $requiredArchitectureDocs = @(
        'docs\README.md',
        'docs\maintenance-guide.md',
        'docs\content-update-guide.md',
        'docs\compatibility-guide.md',
        'docs\localization-and-comments.md',
        'docs\validation-runbook.md',
        'docs\release-checklist.md',
        'docs\architecture\README.md',
        'docs\architecture\ADR-001-single-dll.md',
        'docs\architecture\ADR-002-harmony-registration.md',
        'docs\architecture\ADR-003-compatibility-scope.md',
        'docs\architecture\ADR-004-root-namespace-markers.md'
    )
    foreach ($docPath in $requiredArchitectureDocs) {
        $architectureDocumentationChecks++
        if (-not (Test-Path -LiteralPath $docPath)) {
            $architectureDocumentationIssues += "$docPath :: required architecture document is missing"
        }
    }

    $architectureDocumentationChecks++
    $runbookPath = 'docs\validation-runbook.md'
    if (Test-Path -LiteralPath $runbookPath) {
        $runbookText = Get-Content -LiteralPath $runbookPath -Encoding utf8 -Raw
        if ($runbookText -notmatch 'Fresh Log 记录模板' -or
            $runbookText -notmatch 'Player\.log LastWriteTime' -or
            $runbookText -notmatch '日志扫描结果') {
            $architectureDocumentationIssues += "$runbookPath :: game validation runbook must keep the fresh log record template"
        }
    }
    else {
        $architectureDocumentationIssues += "$runbookPath :: missing game validation runbook"
    }

    $architectureDocumentationChecks++
    $overviewPath = 'docs\README.md'
    if (Test-Path -LiteralPath $overviewPath) {
        $overviewText = Get-Content -LiteralPath $overviewPath -Encoding utf8 -Raw
        if ($overviewText -notmatch 'maintenance-guide\.md' -or
            $overviewText -notmatch 'validation-runbook\.md' -or
            $overviewText -notmatch 'release-checklist\.md' -or
            $overviewText -notmatch 'architecture/') {
            $architectureDocumentationIssues += "$overviewPath :: maintenance overview must link guide, validation, release and ADR directory"
        }
    }
    else {
        $architectureDocumentationIssues += "$overviewPath :: missing maintenance overview"
    }

    if ($architectureDocumentationIssues.Count) {
        $architectureDocumentationIssues | Sort-Object
        Fail "Maintenance documentation scan failed: $($architectureDocumentationIssues.Count) issue(s)"
    }

    Write-Step "Forbidden production patterns"
    $forbidden = 'Apperal|Heiffs|\bHai\b|Gloden|MechanoidWorkControlSettings|allArmorDefs|milking\(V1\)|south \.png|EyeInHead _backpack|Ldloc_S|MugirlSkinApplied|Analyzer|Mugirl_FactionUtility|AdvancedSlaveApparel\s*\|\|[^\r\n]*BrainWashSlaveApparel|EnsureBikiniOnly|WearBikiniOnly|UnlockEventBikini|Pawn_ApparelTracker_IsLocked_SlaveApparel_Patch'
    $hits = & rg -n $forbidden @ProductionRoots
    if ($LASTEXITCODE -eq 0) {
        $hits
        Fail "Forbidden production pattern(s) found"
    }
    elseif ($LASTEXITCODE -ne 1) {
        Fail "rg failed during forbidden pattern scan with exit code $LASTEXITCODE"
    }

    Write-Step "Direct Verse.Log calls"
    $allowedDirectLogFiles = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$allowedDirectLogFiles.Add('1.6\Source\Core\MugirlLog.cs')
    $directLogCalls = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
        Select-String -LiteralPath $_.FullName -Pattern '(?<!Mugirl)Log\.(ErrorOnce|Error|Warning|Message)\(' | ForEach-Object {
            if (-not $allowedDirectLogFiles.Contains($relative)) {
                $directLogCalls += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
            }
        }
    }
    $logWrapperPath = '1.6\Source\Core\MugirlLog.cs'
    if (Test-Path -LiteralPath $logWrapperPath) {
        $logWrapperText = Get-Content -LiteralPath $logWrapperPath -Encoding utf8 -Raw
        if ($logWrapperText -notmatch 'internal\s+static\s+void\s+DevMessage\(' -or
            $logWrapperText -notmatch 'Prefs\.DevMode' -or
            $logWrapperText -notmatch 'internal\s+static\s+void\s+ResetOnceWarnings\(\)' -or
            $logWrapperText -notmatch 'warnedKeys\.Clear\(\)' -or
            $logWrapperText -notmatch 'private\s+static\s+void\s+Warning\(' -or
            $logWrapperText -notmatch 'using\s+System\.Runtime\.CompilerServices\s*;' -or
            $logWrapperText -notmatch '\[CallerMemberName\]\s*string\s+callerMemberName\s*=\s*null' -or
            $logWrapperText -notmatch '\[CallerLineNumber\]\s*int\s+callerLineNumber\s*=\s*0' -or
            $logWrapperText -notmatch 'string\.IsNullOrWhiteSpace\(key\)' -or
            $logWrapperText -notmatch 'BuildFallbackKey\(callerMemberName,\s*callerLineNumber\)' -or
            $logWrapperText -notmatch 'key\.Trim\(\)' -or
            $logWrapperText -notmatch 'string\.IsNullOrWhiteSpace\(message\)\s*\?\s*"Unspecified warning\."' -or
            $logWrapperText -match 'internal\s+static\s+void\s+(Message|Warning|Error)\(' -or
            $logWrapperText -match 'CallerFilePath' -or
            $logWrapperText -match 'Log\.Error') {
            $directLogCalls += "$logWrapperPath :: informational logging must be gated behind DevMessage, warnings must go through WarningOnce with blank-key/message fallback, red-error wrapper must stay absent and warning-once state must be resettable"
        }
    }
    else {
        $directLogCalls += "$logWrapperPath :: missing log wrapper"
    }

    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
        if ($relative -eq $logWrapperPath) {
            return
        }

        Select-String -LiteralPath $_.FullName -Pattern 'MugirlLog\.Message\(' | ForEach-Object {
            $directLogCalls += ("{0}:{1}: business code must not use MugirlLog.Message: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
        }
        Select-String -LiteralPath $_.FullName -Pattern 'MugirlLog\.Warning\(' | ForEach-Object {
            $directLogCalls += ("{0}:{1}: business code must use MugirlLog.WarningOnce instead of non-limited Warning: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
        }
        Select-String -LiteralPath $_.FullName -Pattern 'MugirlLog\.Error\(' | ForEach-Object {
            $directLogCalls += ("{0}:{1}: business code must not use MugirlLog.Error; use a downgraded WarningOnce/fallback path unless a red error is explicitly justified: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
        }
    }
    if ($directLogCalls.Count) {
        $directLogCalls | Sort-Object
        Fail "Direct Verse.Log call scan failed: $($directLogCalls.Count) call(s)"
    }

    Write-Step "Text formatting safety"
    $textFormattingSafetyChecks = 0
    $textFormattingSafetyIssues = @()

    $textFormattingSafetyChecks++
    $textUtilityPath = '1.6\Source\Core\MugirlText.cs'
    if (Test-Path -LiteralPath $textUtilityPath) {
        $textUtilityText = Get-Content -LiteralPath $textUtilityPath -Encoding utf8 -Raw
        $textFormatFailureSafe = $textUtilityText -match 'catch\s*\(\s*Exception\s+ex\s*\)' `
            -and $textUtilityText -match 'ex\.GetType\(\)\.Name\s*\+\s*": "\s*\+\s*ex\.Message' `
            -and $textUtilityText -match 'MugirlLog\.WarningOnce' `
            -and $textUtilityText -match 'Text\.FormatFailed\.' `
            -and $textUtilityText -match 'Gen\.HashCombineInt\(textOrKey\.GetHashCode\(\),\s*781233517\)' `
            -and $textUtilityText -match 'Mugirl\.Text\.FormatFailedLog' `
            -and $textUtilityText -match 'detail\.Named\("ERROR"\)' `
            -and $textUtilityText -match 'return\s+textOrKey' `
            -and $textUtilityText -notmatch 'Log\.ErrorOnce' `
            -and $textUtilityText -notmatch 'Log\.Error\s*\('
        if (-not $textFormatFailureSafe) {
            $textFormattingSafetyIssues += "$textUtilityPath :: text formatting failures must degrade with MugirlLog.WarningOnce and return the original text"
        }
    }
    else {
        $textFormattingSafetyIssues += "$textUtilityPath :: missing file for text formatting safety"
    }

    if ($textFormattingSafetyIssues.Count) {
        $textFormattingSafetyIssues | Sort-Object
        Fail "Text formatting safety scan failed: $($textFormattingSafetyIssues.Count) issue(s)"
    }

    Write-Step "Tick manager access safety"
    $tickManagerAccessSafetyChecks = 0
    $tickManagerAccessSafetyIssues = @()

    $tickUtilityPath = '1.6\Source\Core\MugirlTickUtility.cs'
    $tickManagerAccessSafetyChecks++
    if (Test-Path -LiteralPath $tickUtilityPath) {
        $tickUtilityText = Get-Content -LiteralPath $tickUtilityPath -Encoding utf8 -Raw
        if ($tickUtilityText -notmatch 'TryGetCurrentGameTick' -or
            $tickUtilityText -notmatch 'CurrentGameTickOrFallback' -or
            $tickUtilityText -notmatch 'Verse\.Current\.ProgramState\s*==\s*Verse\.ProgramState\.Playing' -or
            $tickUtilityText -notmatch 'Verse\.Find\.TickManager\s*!=\s*null') {
            $tickManagerAccessSafetyIssues += "$tickUtilityPath :: tick access helper must guard ProgramState and TickManager availability"
        }
    }
    else {
        $tickManagerAccessSafetyIssues += "$tickUtilityPath :: missing file for TickManager access safety"
    }

    $tickManagerAccessSafetyChecks++
    $directTickManagerAccess = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
            if ($relative -ne $tickUtilityPath) {
                Select-String -LiteralPath $_.FullName -Pattern 'Find\.TickManager' | ForEach-Object {
                    $directTickManagerAccess += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
                }
            }
        }
    if ($directTickManagerAccess.Count) {
        $directTickManagerAccess | Sort-Object
        $tickManagerAccessSafetyIssues += "1.6\Source :: direct Find.TickManager access outside MugirlTickUtility"
    }

    if ($tickManagerAccessSafetyIssues.Count) {
        $tickManagerAccessSafetyIssues | Sort-Object
        Fail "Tick manager access safety scan failed: $($tickManagerAccessSafetyIssues.Count) issue(s)"
    }

    Write-Step "Find access safety"
    $findAccessSafetyChecks = 0
    $findAccessSafetyIssues = @()

    $allowedFindAccessFiles = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$allowedFindAccessFiles.Add('1.6\Source\Core\MugirlGameUtility.cs')
    [void]$allowedFindAccessFiles.Add('1.6\Source\Core\MugirlSelectionUtility.cs')
    [void]$allowedFindAccessFiles.Add('1.6\Source\Core\MugirlTickUtility.cs')

    $findAccessSafetyChecks++
    foreach ($allowedFindAccessFile in $allowedFindAccessFiles) {
        if (-not (Test-Path -LiteralPath $allowedFindAccessFile)) {
            $findAccessSafetyIssues += "$allowedFindAccessFile :: missing allowed Find access helper"
        }
    }

    $findAccessSafetyChecks++
    $directFindAccess = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
            if (-not $allowedFindAccessFiles.Contains($relative)) {
                Select-String -LiteralPath $_.FullName -Pattern '\bFind\.' | ForEach-Object {
                    $directFindAccess += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
                }
            }
        }
    if ($directFindAccess.Count) {
        $directFindAccess | Sort-Object
        $findAccessSafetyIssues += "1.6\Source :: direct Find.* access outside core helpers"
    }

    if ($findAccessSafetyIssues.Count) {
        $findAccessSafetyIssues | Sort-Object
        Fail "Find access safety scan failed: $($findAccessSafetyIssues.Count) issue(s)"
    }

    Write-Step "Current game access safety"
    $currentGameAccessSafetyChecks = 0
    $currentGameAccessSafetyIssues = @()

    $allowedCurrentGameAccessFiles = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$allowedCurrentGameAccessFiles.Add('1.6\Source\Core\MugirlGameUtility.cs')
    [void]$allowedCurrentGameAccessFiles.Add('1.6\Source\Core\MugirlSelectionUtility.cs')
    [void]$allowedCurrentGameAccessFiles.Add('1.6\Source\Core\MugirlTickUtility.cs')

    $currentGameAccessSafetyChecks++
    $gameUtilityPathForCurrent = '1.6\Source\Core\MugirlGameUtility.cs'
    if (Test-Path -LiteralPath $gameUtilityPathForCurrent) {
        $gameUtilityCurrentText = Get-Content -LiteralPath $gameUtilityPathForCurrent -Encoding utf8 -Raw
        if ($gameUtilityCurrentText -notmatch 'internal\s+static\s+bool\s+IsPlaying\(\)' -or
            $gameUtilityCurrentText -notmatch 'internal\s+static\s+bool\s+TryGetGameComponent<T>\(out\s+T\s+component\)\s+where\s+T\s*:\s*GameComponent') {
            $currentGameAccessSafetyIssues += "$gameUtilityPathForCurrent :: game utility must provide centralized Playing and GameComponent helpers"
        }
    }
    else {
        $currentGameAccessSafetyIssues += "$gameUtilityPathForCurrent :: missing file for Current.Game access safety"
    }

    $currentGameAccessSafetyChecks++
    $directCurrentGameAccess = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
            if (-not $allowedCurrentGameAccessFiles.Contains($relative)) {
                Select-String -LiteralPath $_.FullName -Pattern '\b(Current|Verse\.Current)\.(Game|ProgramState)' | ForEach-Object {
                    $directCurrentGameAccess += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
                }
            }
        }
    if ($directCurrentGameAccess.Count) {
        $directCurrentGameAccess | Sort-Object
        $currentGameAccessSafetyIssues += "1.6\Source :: direct Current.Game/ProgramState access outside core helpers"
    }

    $currentGameAccessSafetyChecks++
    $manualGameComponentAdd = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'Current\.Game\.components\.Add|\.components\.Add\(' | ForEach-Object {
                $manualGameComponentAdd += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
            }
        }
    if ($manualGameComponentAdd.Count) {
        $manualGameComponentAdd | Sort-Object
        $currentGameAccessSafetyIssues += "1.6\Source :: runtime GameComponent list mutation must not be used"
    }

    if ($currentGameAccessSafetyIssues.Count) {
        $currentGameAccessSafetyIssues | Sort-Object
        Fail "Current game access safety scan failed: $($currentGameAccessSafetyIssues.Count) issue(s)"
    }

    Write-Step "Def lookup safety"
    $defLookupSafetyChecks = 0
    $defLookupSafetyIssues = @()

    $defLookupSafetyChecks++
    $requiredDefsPath = '1.6\Source\Core\MugirlRequiredDefs.cs'
    if (Test-Path -LiteralPath $requiredDefsPath) {
        $requiredDefsText = Get-Content -LiteralPath $requiredDefsPath -Encoding utf8 -Raw
        $requiredDefsUsesHelper = $requiredDefsText -match 'Required<[^>]+>\('
        $requiredDefsUsesHardGetNamed = $requiredDefsText -match 'DefDatabase<[^>]+>\.GetNamed\('
        if (-not $requiredDefsUsesHelper -or $requiredDefsUsesHardGetNamed) {
            $defLookupSafetyIssues += "$requiredDefsPath :: required Defs must use the Required<T> helper, not bare DefDatabase.GetNamed"
        }
    }
    else {
        $defLookupSafetyIssues += "$requiredDefsPath :: missing file for required Def lookup safety"
    }

    $defLookupSafetyChecks++
    $optionalDefsPath = '1.6\Source\Core\MugirlOptionalDefs.cs'
    if (Test-Path -LiteralPath $optionalDefsPath) {
        $optionalDefsText = Get-Content -LiteralPath $optionalDefsPath -Encoding utf8 -Raw
        if ($optionalDefsText -match 'DefDatabase<[^>]+>\.GetNamed\(') {
            $defLookupSafetyIssues += "$optionalDefsPath :: optional Defs must use GetNamedSilentFail"
        }
    }
    else {
        $defLookupSafetyIssues += "$optionalDefsPath :: missing file for optional Def lookup safety"
    }

    $defLookupSafetyChecks++
    $hardDefLookupCalls = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
            Select-String -LiteralPath $_.FullName -Pattern 'DefDatabase<[^>]+>\.GetNamed\(' | ForEach-Object {
                $hardDefLookupCalls += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
            }
        }
    if ($hardDefLookupCalls.Count) {
        $hardDefLookupCalls | Sort-Object
        $defLookupSafetyIssues += "1.6\Source :: hard DefDatabase.GetNamed call(s) outside controlled helpers"
    }

    if ($defLookupSafetyIssues.Count) {
        $defLookupSafetyIssues | Sort-Object
        Fail "Def lookup safety scan failed: $($defLookupSafetyIssues.Count) issue(s)"
    }

    Write-Step "Dynamic recipe safety"
    $dynamicRecipeSafetyChecks = 0
    $dynamicRecipeSafetyIssues = @()

    $drugAdministerPath = '1.6\Source\Features\Misc\Harmony_DrugAdministerDefs.cs'
    $dynamicRecipeSafetyChecks++
    if (Test-Path -LiteralPath $drugAdministerPath) {
        $drugAdministerText = Get-Content -LiteralPath $drugAdministerPath -Encoding utf8 -Raw
        $drugAdministerResultSafe = $drugAdministerText -match '__result\s*==\s*null' `
            -and $drugAdministerText -match 'new\s+List<RecipeDef>\(__result\)' `
            -and $drugAdministerText -match 'RecipeDef\s+recipe\s*=\s*recipes\[i\]' `
            -and $drugAdministerText -match 'recipe\s*!=\s*null\s*&&\s*recipe\.defName\s*==\s*defName'
        if (-not $drugAdministerResultSafe) {
            $dynamicRecipeSafetyIssues += "$drugAdministerPath :: DrugAdministerDefs postfix must tolerate null result collections and null recipe entries"
        }

        $dynamicRecipeSafetyChecks++
        $drugAdministerIngestibleSafe = $drugAdministerText -match 'milkDef\.ingestible\s*==\s*null' `
            -and $drugAdministerText -match 'MugirlLog\.WarningOnce' `
            -and $drugAdministerText -notmatch 'milkDef\.ingestible\?\.'
        if (-not $drugAdministerIngestibleSafe) {
            $dynamicRecipeSafetyIssues += "$drugAdministerPath :: administer milk recipe must not be generated from a non-ingestible ThingDef fallback"
        }

        $dynamicRecipeSafetyChecks++
        $drugAdministerReloadSafe = $drugAdministerText -match 'ResetGeneratedRecipe\(recipeDef\)' `
            -and $drugAdministerText -match 'DefDatabase<RecipeDef>\.GetNamedSilentFail\(defName\)' `
            -and $drugAdministerText -notmatch 'DefDatabase<RecipeDef>\.GetNamed\(' `
            -and $drugAdministerText -match 'recipeDef\.ingredients\.Clear\(\)' `
            -and $drugAdministerText -match 'recipeDef\.fixedIngredientFilter\s*=\s*new\s+ThingFilter\(\)' `
            -and $drugAdministerText -match 'recipeDef\.defaultIngredientFilter\s*=\s*null' `
            -and $drugAdministerText -match 'recipeDef\.recipeUsers\s*=\s*new\s+List<ThingDef>\(\)'
        if (-not $drugAdministerReloadSafe) {
            $dynamicRecipeSafetyIssues += "$drugAdministerPath :: hotReload recipe reuse must reset ingredients, fixed/default ingredient filters and recipeUsers"
        }

        $dynamicRecipeSafetyChecks++
        $drugAdministerUsersSafe = $drugAdministerText -match 'PopulateRecipeUsers\(recipeDef\)' `
            -and $drugAdministerText -match 'DefDatabase<ThingDef>\.AllDefsListForReading' `
            -and $drugAdministerText -match 'pawnDef\?\.category\s*==\s*ThingCategory\.Pawn' `
            -and $drugAdministerText -match 'pawnDef\.race\s*!=\s*null' `
            -and $drugAdministerText -match 'pawnDef\.race\.IsFlesh' `
            -and $drugAdministerText -notmatch 'MugirlIdentity\.IsMugirlPawnDef\(pawnDef\)' `
            -and $drugAdministerText -notmatch 'Mugirl_DefOf\.MugirlBody' `
            -and $drugAdministerText -notmatch 'DefDatabase<ThingDef>\.AllDefs(?!ListForReading)'
        if (-not $drugAdministerUsersSafe) {
            $dynamicRecipeSafetyIssues += "$drugAdministerPath :: administer milk recipeUsers must follow vanilla drug administer flesh-pawn scanning"
        }
    }
    else {
        $dynamicRecipeSafetyIssues += "$drugAdministerPath :: missing file for dynamic recipe safety"
    }
    if ($dynamicRecipeSafetyIssues.Count) {
        $dynamicRecipeSafetyIssues | Sort-Object
        Fail "Dynamic recipe safety scan failed: $($dynamicRecipeSafetyIssues.Count) issue(s)"
    }

    Write-Step "Mugirl identity safety"
    $mugirlIdentitySafetyChecks = 0
    $mugirlIdentitySafetyIssues = @()

    $mugirlIdentityPath = '1.6\Source\Core\MugirlIdentity.cs'
    $mugirlIdentitySafetyChecks++
    if (Test-Path -LiteralPath $mugirlIdentityPath) {
        $mugirlIdentityText = Get-Content -LiteralPath $mugirlIdentityPath -Encoding utf8 -Raw
        $mugirlIdentitySafe = $mugirlIdentityText -match 'internal\s+static\s+bool\s+IsMugirlDef\(Pawn\s+pawn\)' `
            -and $mugirlIdentityText -match 'internal\s+static\s+bool\s+IsMugirlDef\(ThingDef\s+thingDef\)' `
            -and $mugirlIdentityText -match 'internal\s+static\s+bool\s+HasMugirlBody\(Pawn\s+pawn\)' `
            -and $mugirlIdentityText -match 'internal\s+static\s+bool\s+HasMugirlBody\(ThingDef\s+thingDef\)' `
            -and $mugirlIdentityText -match 'internal\s+static\s+bool\s+IsMugirlPawn\(Pawn\s+pawn\)' `
            -and $mugirlIdentityText -match 'internal\s+static\s+bool\s+IsMugirlPawnDef\(ThingDef\s+thingDef\)' `
            -and $mugirlIdentityText -match 'thingDef\?\.category\s*==\s*ThingCategory\.Pawn'
        if (-not $mugirlIdentitySafe) {
            $mugirlIdentitySafetyIssues += "$mugirlIdentityPath :: Mugirl identity checks must centralize Pawn and ThingDef race/body recognition"
        }
    }
    else {
        $mugirlIdentitySafetyIssues += "$mugirlIdentityPath :: missing file for Mugirl identity safety"
    }

    $mugirlIdentitySafetyChecks++
    $directMugirlBodyChecks = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/')
            if ($relative -ne $mugirlIdentityPath) {
                Select-String -LiteralPath $_.FullName -Pattern 'Mugirl_DefOf\.MugirlBody|body\.defName\s*[=!]=\s*"MugirlBody"' | ForEach-Object {
                    $directMugirlBodyChecks += ("{0}:{1}: {2}" -f $relative, $_.LineNumber, $_.Line.Trim())
                }
            }
        }
    if ($directMugirlBodyChecks.Count) {
        $directMugirlBodyChecks | Sort-Object
        $mugirlIdentitySafetyIssues += "1.6\Source :: direct Mugirl body identity checks outside MugirlIdentity"
    }

    $mugirlIdentitySafetyChecks++
    $identityCallSites = @{
        '1.6\Source\Features\Misc\Thought_MugirlOnly.cs' = 'MugirlIdentity\.HasMugirlBody\(currentPawn\)'
        '1.6\Source\Features\Roping\RopingService.cs' = 'MugirlIdentity\.HasMugirlBody\(pawn\)'
        '1.6\Source\Features\Milk\WorkGiver_GatherBodyResources.cs' = 'MugirlIdentity\.HasMugirlBody\(pawn2\)'
        '1.6\Source\Features\Newborn\LifeStageVisualService.cs' = 'MugirlIdentity\.HasMugirlBody\(pawn\)'
        '1.6\Source\Features\Newborn\Harmony_PawnGenerator_NewbornVisuals.cs' = 'MugirlIdentity\.HasMugirlBody\(__result\)'
    }
    foreach ($entry in $identityCallSites.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath $entry.Key)) {
            $mugirlIdentitySafetyIssues += "$($entry.Key) :: missing file for Mugirl identity call-site safety"
            continue
        }

        $callSiteText = Get-Content -LiteralPath $entry.Key -Encoding utf8 -Raw
        if ($callSiteText -notmatch $entry.Value) {
            $mugirlIdentitySafetyIssues += "$($entry.Key) :: must use centralized MugirlIdentity helper for Mugirl race/body checks"
        }
    }

    if ($mugirlIdentitySafetyIssues.Count) {
        $mugirlIdentitySafetyIssues | Sort-Object
        Fail "Mugirl identity safety scan failed: $($mugirlIdentitySafetyIssues.Count) issue(s)"
    }

    Write-Step "Player faction helper safety"
    $playerFactionHelperSafetyChecks = 0
    $directPlayerFactionEquality = @()
    $playerFactionHelperSafetyChecks++
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern '(Faction\.OfPlayer\s*[=!]=|[=!]=\s*Faction\.OfPlayer)' | ForEach-Object {
                $directPlayerFactionEquality += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
            }
        }
    if ($directPlayerFactionEquality.Count) {
        $directPlayerFactionEquality | Sort-Object
        Fail "Player faction helper safety scan failed: $($directPlayerFactionEquality.Count) direct equality check(s)"
    }

    $directPlayerFactionHostility = @()
    $playerFactionHelperSafetyChecks++
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'HostileTo\(\s*Faction\.OfPlayer\s*\)|\.HostileTo\(\s*Faction\.OfPlayer\s*\)' | ForEach-Object {
                $directPlayerFactionHostility += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
            }
        }
    if ($directPlayerFactionHostility.Count) {
        $directPlayerFactionHostility | Sort-Object
        Fail "Player faction helper safety scan failed: $($directPlayerFactionHostility.Count) direct hostility check(s)"
    }

    $directPlayerFactionHardAccess = @()
    $playerFactionHelperSafetyChecks++
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'Faction\.OfPlayer(?!SilentFail)' | ForEach-Object {
                $directPlayerFactionHardAccess += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
            }
        }
    if ($directPlayerFactionHardAccess.Count) {
        $directPlayerFactionHardAccess | Sort-Object
        Fail "Player faction helper safety scan failed: $($directPlayerFactionHardAccess.Count) direct hard player faction access(es)"
    }

    Write-Step "Restraint target safety"
    $restraintTargetSafetyChecks = 0
    $restraintTargetSafetyIssues = @()
    $restraintTargetSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Restraints\SlaveApparelExtensions.cs'
            Pattern = '\btarget\s*(==|!=)\s*null'
            Description = 'LocalTargetInfo target compared with null'
        },
        @{
            Path = '1.6\Source\Features\Restraints\JobDriver_UseItemOn.cs'
            Pattern = '\((Pawn|Apparel)\)\s*(tar|base\.job\.GetTarget|job\.GetTarget)'
            Description = 'UseItemOn directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Restraints\JobDriver_UnlockSlaveApparelGear.cs'
            Pattern = '\((Pawn|Apparel)\)\s*(targetThing|base\.job\.GetTarget|job\.GetTarget)'
            Description = 'Unlock job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Restraints\JobDriver_CrackBondageGear.cs'
            Pattern = '\((Pawn|Apparel)\)\s*(base\.job\.GetTarget|job\.GetTarget)'
            Description = 'Crack job directly casts job target'
        }
    )

    foreach ($rule in $restraintTargetSafetyRules) {
        $restraintTargetSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $restraintTargetSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $restraintTargetSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $restraintTargetSafetyChecks++
    $targetablePath = '1.6\Source\Features\Restraints\Comps\CompTargetable_SlaveApparel.cs'
    if (Test-Path -LiteralPath $targetablePath) {
        $targetableText = Get-Content -LiteralPath $targetablePath -Encoding utf8 -Raw
        if ($targetableText -match 'yield\s+return\s+targetChosenByPlayer\s*;' -and
            $targetableText -notmatch 'ValidateTarget\(\s*targetChosenByPlayer\s*,\s*false\s*\)') {
            $restraintTargetSafetyIssues += "$targetablePath :: targetable comp returns player target without ValidateTarget(targetChosenByPlayer, false)"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$targetablePath :: missing file for targetable player target validation"
    }

    $restraintTargetSafetyChecks++
    if (Test-Path -LiteralPath $targetablePath) {
        $targetableText = Get-Content -LiteralPath $targetablePath -Encoding utf8 -Raw
        if ($targetableText -notmatch 'Messages\.Message\("Mugirl\.AlreadyCracked"\.Translate\(\),\s*MessageTypeDefOf\.NeutralEvent,\s*historical:\s*false\)' -or
            $targetableText -notmatch 'Messages\.Message\("Mugirl\.InvalidTarget"\.Translate\(\),\s*MessageTypeDefOf\.NeutralEvent,\s*historical:\s*false\)') {
            $restraintTargetSafetyIssues += "$targetablePath :: targetable invalid/already-cracked feedback must not be archived"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$targetablePath :: missing file for targetable feedback archive safety"
    }

    $restraintTargetSafetyChecks++
    $slaveApparelExtensionsPath = '1.6\Source\Features\Restraints\SlaveApparelExtensions.cs'
    if (Test-Path -LiteralPath $slaveApparelExtensionsPath) {
        $slaveApparelExtensionsText = Get-Content -LiteralPath $slaveApparelExtensionsPath -Encoding utf8 -Raw
        if ($slaveApparelExtensionsText -notmatch 'usable\.props\s+as\s+CompProperties_Usable' -or
            $slaveApparelExtensionsText -notmatch 'usableProps\?\.useJob\s*==\s*null') {
            $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: unlock usable props must be safe-cast before creating the unlock job"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: missing file for unlock usable props safety"
    }

    $restraintTargetSafetyChecks++
    if (Test-Path -LiteralPath $slaveApparelExtensionsPath) {
        $slaveApparelExtensionsText = Get-Content -LiteralPath $slaveApparelExtensionsPath -Encoding utf8 -Raw
        if ($slaveApparelExtensionsText -notmatch 'MugirlGameUtility\.TryAddWindow\(\s*new\s+FloatMenu\(options\)\s*\)' -or
            $slaveApparelExtensionsText -match 'Find\.WindowStack') {
            $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: unlock submenu must use MugirlGameUtility.TryAddWindow and avoid direct Find.WindowStack"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: missing file for unlock submenu window safety"
    }

    $restraintTargetSafetyChecks++
    if (Test-Path -LiteralPath $slaveApparelExtensionsPath) {
        $slaveApparelExtensionsText = Get-Content -LiteralPath $slaveApparelExtensionsPath -Encoding utf8 -Raw
        if ($slaveApparelExtensionsText -notmatch 'Messages\.Message\("Mugirl\.NotWearingLockedApparel"\.Translate\(\),\s*MessageTypeDefOf\.RejectInput,\s*historical:\s*false\)') {
            $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: no-locked-apparel reject feedback must not be archived"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$slaveApparelExtensionsPath :: missing file for no-locked-apparel feedback archive safety"
    }

    $restraintTargetSafetyChecks++
    $crackBondageGearEffectPath = '1.6\Source\Features\Restraints\Comps\CompProperties_TargetEffectCrackBondageGear.cs'
    if (Test-Path -LiteralPath $crackBondageGearEffectPath) {
        $crackBondageGearEffectText = Get-Content -LiteralPath $crackBondageGearEffectPath -Encoding utf8 -Raw
        $crackBondageGearFeedbackSafe = $crackBondageGearEffectText -match 'Messages\.Message\("Mugirl\.CrackBondageGear_TargetNotValid"' `
            -and $crackBondageGearEffectText -match 'Messages\.Message\("Mugirl\.CrackBondageGear_CrackBrainwash"' `
            -and $crackBondageGearEffectText -match 'Messages\.Message\("Mugirl\.CrackBondageGear_CrackAdvanced"' `
            -and $crackBondageGearEffectText -match 'Messages\.Message\("Mugirl\.CrackBondageGear_NoCrackableType"' `
            -and $crackBondageGearEffectText -match 'MessageTarget\(Pawn\s+user,\s*Thing\s+fallback\)' `
            -and $crackBondageGearEffectText -match 'MugirlLog\.WarningOnce\("CrackBondageGear\.NoCrackableType"' `
            -and $crackBondageGearEffectText -notmatch 'MugirlLog\.Message' `
            -and $crackBondageGearEffectText -notmatch '(?<!Mugirl)Log\.Message' `
            -and $crackBondageGearEffectText -notmatch '(?<!Mugirl)Log\.Warning'
        if (-not $crackBondageGearFeedbackSafe) {
            $restraintTargetSafetyIssues += "$crackBondageGearEffectPath :: crack bondage gear player feedback must use Messages.Message, keep only WarningOnce for impossible XML type drift and avoid raw log output"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$crackBondageGearEffectPath :: missing file for crack bondage gear feedback safety"
    }

    $restraintTargetSafetyChecks++
    $pawnBoolPath = '1.6\Source\Features\Restraints\PawnBool.cs'
    $pawnSlaveStatusUtilityPath = '1.6\Source\Features\Restraints\PawnSlaveStatusUtility.cs'
    if (Test-Path -LiteralPath $pawnBoolPath) {
        $restraintTargetSafetyIssues += "$pawnBoolPath :: legacy public PawnBool shim must stay removed"
    }

    if (Test-Path -LiteralPath $pawnSlaveStatusUtilityPath) {
        $pawnSlaveStatusUtilityText = Get-Content -LiteralPath $pawnSlaveStatusUtilityPath -Encoding utf8 -Raw
        $pawnSlaveStatusUtilitySafe = $pawnSlaveStatusUtilityText -match 'internal\s+static\s+class\s+PawnSlaveStatusUtility' `
            -and $pawnSlaveStatusUtilityText -match 'internal\s+static\s+string\s+DisplayName\(Pawn\s+pawn\)' `
            -and $pawnSlaveStatusUtilityText -match 'pawn\.Name\?\.ToStringShort\s*\?\?\s*pawn\.Label\s*\?\?\s*"noname"' `
            -and $pawnSlaveStatusUtilityText -match 'internal\s+static\s+bool\s+IsSlave\(Pawn\s+pawn\)' `
            -and $pawnSlaveStatusUtilityText -match 'return\s+pawn\?\.IsSlave\s*==\s*true;' `
            -and $pawnSlaveStatusUtilityText -notmatch 'SimpleSlaveryIsActive|HediffDef\s+Enslaved|is_slave|is_vanillaslave|is_modslave|get_pawnname|public\s+static\s+bool'
        if (-not $pawnSlaveStatusUtilitySafe) {
            $restraintTargetSafetyIssues += "$pawnSlaveStatusUtilityPath :: slave status helper must stay internal, null-safe and free of uninitialized public SimpleSlavery shim state"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$pawnSlaveStatusUtilityPath :: missing file for slave status helper safety"
    }

    $compSlaveApparelGearPath = '1.6\Source\Features\Restraints\Comps\CompSlaveApparelGear.cs'
    $compStampedApparelKeyPath = '1.6\Source\Features\Restraints\Comps\CompStampedApparelKey.cs'
    foreach ($path in @($compSlaveApparelGearPath, $compStampedApparelKeyPath)) {
        if (Test-Path -LiteralPath $path) {
            $text = Get-Content -LiteralPath $path -Encoding utf8 -Raw
            if ($text -match 'PawnBool' -or $text -match '\.is_slave|\.get_pawnname' -or $text -notmatch 'PawnSlaveStatusUtility\.') {
                $restraintTargetSafetyIssues += "$path :: restraint menu code must use PawnSlaveStatusUtility instead of legacy PawnBool helpers"
            }
        }
        else {
            $restraintTargetSafetyIssues += "$path :: missing file for slave status helper callsite safety"
        }
    }

    $restraintTargetSafetyChecks++
    if (Test-Path -LiteralPath $compStampedApparelKeyPath) {
        $compStampedApparelKeyText = Get-Content -LiteralPath $compStampedApparelKeyPath -Encoding utf8 -Raw
        if ($compStampedApparelKeyText -match '\bmake_label\s*\(' -or $compStampedApparelKeyText -notmatch 'MakeLabel\(Pawn\s+pawn,\s*Pawn\s+other\)') {
            $restraintTargetSafetyIssues += "$compStampedApparelKeyPath :: stamped apparel key menu label helper must use PascalCase MakeLabel"
        }
    }
    else {
        $restraintTargetSafetyIssues += "$compStampedApparelKeyPath :: missing file for stamped apparel key helper naming safety"
    }

    if ($restraintTargetSafetyIssues.Count) {
        $restraintTargetSafetyIssues | Sort-Object
        Fail "Restraint target safety scan failed: $($restraintTargetSafetyIssues.Count) issue(s)"
    }

    Write-Step "Restraint hediff state safety"
    $restraintHediffStateSafetyChecks = 0
    $restraintHediffStateSafetyIssues = @()

    $reduceWillEnslavePath = '1.6\Source\Features\Restraints\HediffCompProperties_ReduceWillorEnslave.cs'
    $restraintHediffStateSafetyChecks++
    if (Test-Path -LiteralPath $reduceWillEnslavePath) {
        $reduceWillEnslaveText = Get-Content -LiteralPath $reduceWillEnslavePath -Encoding utf8 -Raw
        if ($reduceWillEnslaveText -notmatch 'private\s+bool\s+triggered' -or
            $reduceWillEnslaveText -notmatch 'Scribe_Values\.Look\(ref\s+triggered,\s*"triggered",\s*false\)' -or
            $reduceWillEnslaveText -notmatch 'props\s+as\s+CompProperties_ReduceWillorEnslave' -or
            $reduceWillEnslaveText -notmatch 'triggered\s+\|\|\s+compProps\s*==\s*null' -or
            $reduceWillEnslaveText -notmatch 'Mathf\.Max\(1,\s*compProps\.triggerTicks\)') {
            $restraintHediffStateSafetyIssues += "$reduceWillEnslavePath :: ReduceWillorEnslave must be a persisted one-shot trigger"
        }
        if ($reduceWillEnslaveText -match 'SetGuestStatus\(\s*Faction\.OfPlayer') {
            $restraintHediffStateSafetyIssues += "$reduceWillEnslavePath :: slave conversion must not hard-access Faction.OfPlayer"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$reduceWillEnslavePath :: missing file for ReduceWillorEnslave state safety"
    }

    $restraintHediffStateSafetyChecks++
    $addTraitPath = '1.6\Source\Features\Restraints\HediffCompProperties_AddTrait.cs'
    if (Test-Path -LiteralPath $addTraitPath) {
        $addTraitText = Get-Content -LiteralPath $addTraitPath -Encoding utf8 -Raw
        if ($addTraitText -notmatch 'props\s+as\s+CompProperties_AddTrait' -or
            $addTraitText -notmatch 'TryGainTrait' -or
            $addTraitText -notmatch 'string\.IsNullOrWhiteSpace\(pawnType\)' -or
            $addTraitText -notmatch 'MugirlWildSlaveUtility\.IsHostileToPlayer\(pawn\)') {
            $restraintHediffStateSafetyIssues += "$addTraitPath :: AddTrait hediff must guard props, missing trait entries and player hostility helper"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$addTraitPath :: missing file for AddTrait safety"
    }

    $restraintHediffStateSafetyChecks++
    $addThoughtPath = '1.6\Source\Features\Restraints\HediffCompProperties_AddThoughts.cs'
    if (Test-Path -LiteralPath $addThoughtPath) {
        $addThoughtText = Get-Content -LiteralPath $addThoughtPath -Encoding utf8 -Raw
        if ($addThoughtText -notmatch 'props\s+as\s+CompProperties_AddThought' -or
            $addThoughtText -notmatch 'compProps\s*==\s*null' -or
            $addThoughtText -notmatch 'triggerTicks\s*=\s*compProps\.triggerTicks\s*<\s*1\s*\?\s*1\s*:\s*compProps\.triggerTicks' -or
            $addThoughtText -notmatch 'thoughts\?\.memories\s*!=\s*null') {
            $restraintHediffStateSafetyIssues += "$addThoughtPath :: AddThought hediff must guard props, trigger interval and mood memory tracker"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$addThoughtPath :: missing file for AddThought safety"
    }

    $restraintHediffStateSafetyChecks++
    $addMentalStatePath = '1.6\Source\Features\Restraints\HediffCompProperties_AddMentalState.cs'
    if (Test-Path -LiteralPath $addMentalStatePath) {
        $addMentalStateText = Get-Content -LiteralPath $addMentalStatePath -Encoding utf8 -Raw
        if ($addMentalStateText -notmatch 'props\s+as\s+CompProperties_AddMentalState' -or
            $addMentalStateText -notmatch 'compProps\s*==\s*null' -or
            $addMentalStateText -notmatch 'mentalStateHandler\s*!=\s*null' -or
            $addMentalStateText -notmatch 'MugirlWildSlaveUtility\.IsPlayerFaction\(pawn\.Faction\)') {
            $restraintHediffStateSafetyIssues += "$addMentalStatePath :: AddMentalState hediff must guard props, mentalState handler and player faction helper"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$addMentalStatePath :: missing file for AddMentalState safety"
    }

    $restraintHediffStateSafetyChecks++
    $suppressionPath = '1.6\Source\Features\Restraints\HediffCompProperties_SuppressionEnhancer.cs'
    if (Test-Path -LiteralPath $suppressionPath) {
        $suppressionText = Get-Content -LiteralPath $suppressionPath -Encoding utf8 -Raw
        if ($suppressionText -notmatch 'props\s+as\s+CompProperties_SuppressionEnhancer' -or
            $suppressionText -notmatch 'Mathf\.Max\(1,\s*compProps\.triggerTicks\)' -or
            $suppressionText -notmatch 'pawn\?\.IsSlaveOfColony\s*==\s*true' -or
            $suppressionText -notmatch 'pawn\.Map\s*!=\s*null') {
            $restraintHediffStateSafetyIssues += "$suppressionPath :: SuppressionEnhancer must guard props, pawn and map before tick-time suppression effects"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$suppressionPath :: missing file for SuppressionEnhancer safety"
    }

    $restraintHediffStateSafetyChecks++
    $eleShockPath = '1.6\Source\Features\Restraints\HediffCompProperties_EleShock.cs'
    if (Test-Path -LiteralPath $eleShockPath) {
        $eleShockText = Get-Content -LiteralPath $eleShockPath -Encoding utf8 -Raw
        if ($eleShockText -notmatch 'props\s+as\s+CompProperties_EleShock' -or
            $eleShockText -notmatch 'ApplyConfiguredHediffs' -or
            $eleShockText -notmatch 'RemoveConfiguredHediffs' -or
            $eleShockText -notmatch 'Scribe_Collections\.Look\(ref\s+filthTimers' -or
            $eleShockText -notmatch 'TryAddJitter' -or
            $eleShockText -notmatch 'hediff\s*==\s*parent' -or
            $eleShockText -notmatch 'entry\?\.filthDef\s*==\s*null' -or
            $eleShockText -notmatch 'tmpStaleFilthDefs' -or
            $eleShockText -notmatch 'IsConfiguredFilthDef' -or
            $eleShockText -notmatch 'JittererField\?\.GetValue\(pawn\.Drawer\)' -or
            $eleShockText -notmatch 'TryGetValue\(entry\.filthDef,\s*out\s+int\s+ticksUntilFilth\)' -or
            $eleShockText -match 'EnsureFilthTimers[\s\S]*new\s+HashSet<ThingDef>' -or
            $eleShockText -match 'EnsureFilthTimers[\s\S]*List<ThingDef>\s+\w+\s*=\s*new\s+List<ThingDef>' -or
            $eleShockText -match 'Traverse\.Create\(pawn\.Drawer\)') {
            $restraintHediffStateSafetyIssues += "$eleShockPath :: EleShock must guard props/list entries, connect configured hediff effects, persist filth timers and avoid tick-time filth/jitter allocation"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$eleShockPath :: missing file for EleShock safety"
    }

    $restraintHediffStateSafetyChecks++
    $performanceEffectPaths = @(
        '1.6\Source\Features\Restraints\HediffCompProperties_PerformanceEffect.cs',
        '1.6\Source\Features\Restraints\BrainwashPerformancePlayer.cs',
        '1.6\Source\Features\Restraints\HediffComp_BrainWashingStar.cs'
    )
    $missingPerformanceEffectPaths = @($performanceEffectPaths | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingPerformanceEffectPaths.Count -eq 0) {
        $performanceEffectText = ($performanceEffectPaths | ForEach-Object { Get-Content -LiteralPath $_ -Encoding utf8 -Raw }) -join "`n"
        if ($performanceEffectText -notmatch 'props\s+as\s+CompProperties_PerformanceEffect' -or
            $performanceEffectText -notmatch 'textParams\s*==\s*null\s*\?\s*-1' -or
            $performanceEffectText -notmatch 's\s*==\s*null\s*\|\|\s*nextSoundTicks\[i\]\s*==\s*-1' -or
            $performanceEffectText -notmatch 'f\s*==\s*null\s*\|\|\s*f\.fleck\s*==\s*null' -or
            $performanceEffectText -notmatch 'stunDurationTicks\s*=\s*Mathf\.Max\(0,\s*props\.stunDurationTicks\)') {
            $restraintHediffStateSafetyIssues += "PerformanceEffect split files :: brainwash performance must guard safe props, null XML list entries and invalid stun duration"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "PerformanceEffect split files :: missing file(s): $($missingPerformanceEffectPaths -join ', ')"
    }

    $restraintHediffStateSafetyChecks++
    $disappearsAddPath = '1.6\Source\Features\Restraints\HediffCompProperties_DisappearsAndAddHediffs.cs'
    if (Test-Path -LiteralPath $disappearsAddPath) {
        $disappearsAddText = Get-Content -LiteralPath $disappearsAddPath -Encoding utf8 -Raw
        if ($disappearsAddText -notmatch 'props\s+as\s+CompProperties_DisappearsAndAddHediffs' -or
            $disappearsAddText -notmatch 'Pawn\?\.health\s*!=\s*null' -or
            $disappearsAddText -notmatch 'parent\?\.Part\s*!=\s*null') {
            $restraintHediffStateSafetyIssues += "$disappearsAddPath :: DisappearsAndAddHediffs must guard props, pawn health and body part before adding a follow-up hediff"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$disappearsAddPath :: missing file for DisappearsAndAddHediffs safety"
    }

    $restraintHediffStateSafetyChecks++
    $brainwashHelmetPath = '1.6\Source\Features\Restraints\Comps\CompProperties_BrainwashHelmet.cs'
    if (Test-Path -LiteralPath $brainwashHelmetPath) {
        $brainwashHelmetText = Get-Content -LiteralPath $brainwashHelmetPath -Encoding utf8 -Raw
        $brainwashHelmetSafe = $brainwashHelmetText -match 'props\s+as\s+CompProperties_BrainwashHelmet' `
            -and $brainwashHelmetText -match 'ManualUseReady' `
            -and $brainwashHelmetText -match 'ManualCooldownPercent' `
            -and $brainwashHelmetText -match 'CurrentGameTickOrFallback' `
            -and $brainwashHelmetText -match 'GetCommandIcon' `
            -and $brainwashHelmetText -match 'currentProps\s*==\s*null' `
            -and $brainwashHelmetText -notmatch 'if\s*\(!canUse\)\s*return'
        if (-not $brainwashHelmetSafe) {
            $restraintHediffStateSafetyIssues += "$brainwashHelmetPath :: brainwash helmet comp must safe-cast props and recheck manual cooldown/current props at click time"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$brainwashHelmetPath :: missing file for brainwash helmet comp safety"
    }

    $restraintHediffStateSafetyChecks++
    $shockCollarPath = '1.6\Source\Features\Restraints\Comps\CompProperties_ShockCollar.cs'
    if (Test-Path -LiteralPath $shockCollarPath) {
        $shockCollarText = Get-Content -LiteralPath $shockCollarPath -Encoding utf8 -Raw
        $shockCollarSafe = $shockCollarText -match 'props\s+as\s+CompProperties_ShockCollar' `
            -and $shockCollarText -match 'ManualUseReady' `
            -and $shockCollarText -match 'ManualCooldownPercent' `
            -and $shockCollarText -match 'System\.Action<Pawn,\s*CompProperties_ShockCollar>' `
            -and $shockCollarText -match 'GetCommandIcon' `
            -and $shockCollarText -notmatch 'if\s*\(!enabled\)\s*return' `
            -and $shockCollarText -notmatch 'ContentFinder<Texture2D>\.Get\(iconPath\)'
        if (-not $shockCollarSafe) {
            $restraintHediffStateSafetyIssues += "$shockCollarPath :: shock collar comp must safe-cast props, use safe icons and recheck current wearer/props/cooldown at click time"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "$shockCollarPath :: missing file for shock collar comp safety"
    }

    $restraintHediffStateSafetyChecks++
    $magneticShacklesPaths = @(
        '1.6\Source\Features\Restraints\Comps\CompProperties_MagneticShackles.cs',
        '1.6\Source\Features\Restraints\Comps\Comp_MagneticShackles.Gizmos.cs'
    )
    $missingMagneticShacklesPaths = @($magneticShacklesPaths | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingMagneticShacklesPaths.Count -eq 0) {
        $magneticShacklesText = ($magneticShacklesPaths | ForEach-Object { Get-Content -LiteralPath $_ -Encoding utf8 -Raw }) -join "`n"
        $magneticShacklesSafe = $magneticShacklesText -match 'props\s+as\s+CompProperties_MagneticShackles' `
            -and $magneticShacklesText -match 'ManualUseReady' `
            -and $magneticShacklesText -match 'ManualCooldownPercent' `
            -and $magneticShacklesText -match 'CurrentGameTickOrFallback\(nextStateTick\)' `
            -and $magneticShacklesText -match 'GetCommandIcon' `
            -and $magneticShacklesText -notmatch 'new\s+List<CompProperties_MagneticShackles\.BindHediffEntry>' `
            -and $magneticShacklesText -notmatch 'if\s*\(!canUse\)\s*return'
        if (-not $magneticShacklesSafe) {
            $restraintHediffStateSafetyIssues += "MagneticShackles split files :: magnetic shackles comp must safe-cast props, avoid hot-path list allocation and recheck manual cooldown at click time"
        }
    }
    else {
        $restraintHediffStateSafetyIssues += "MagneticShackles split files :: missing file(s): $($missingMagneticShacklesPaths -join ', ')"
    }

    if ($restraintHediffStateSafetyIssues.Count) {
        $restraintHediffStateSafetyIssues | Sort-Object
        Fail "Restraint hediff state safety scan failed: $($restraintHediffStateSafetyIssues.Count) issue(s)"
    }

    Write-Step "Roping target safety"
    $ropingTargetSafetyChecks = 0
    $ropingTargetSafetyIssues = @()
    $ropingTargetSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Roping\JobDriver_RopeMoo.cs'
            Pattern = '\((Pawn|Building)\)\s*(this\.)?job\.GetTarget'
            Description = 'Rope job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Roping\JobDriver_RemoveRopeMoo.cs'
            Pattern = '\((Pawn|Building)\)\s*(this\.)?job\.GetTarget'
            Description = 'Unrope job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Roping\JobDriver_RopeToWallRopeHitch.cs'
            Pattern = '\((Pawn|Building)\)\s*job\.GetTarget'
            Description = 'Rope-to-hitch job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Roping\Comp_RopeToBuild.cs'
            Pattern = '\.StartJob\(\s*job\s*\)'
            Description = 'Rope-to-build comp bypasses ordered-job path'
        }
    )

    foreach ($rule in $ropingTargetSafetyRules) {
        $ropingTargetSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $ropingTargetSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $ropingTargetSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $ropingTargetSafetyChecks++
    $ropingServicePath = '1.6\Source\Features\Roping\RopingService.cs'
    if (Test-Path -LiteralPath $ropingServicePath) {
        $ropingServiceText = Get-Content -LiteralPath $ropingServicePath -Encoding utf8 -Raw
        $ropingTargetBounded = $ropingServiceText -match 'public\s+static\s+bool\s+CanStartPawnRope\(Pawn\s+roper,\s*Pawn\s+ropee\)[\s\S]*if\s*\(\s*!IsMugirlRopee\(ropee\)\s*\)\s*\{\s*return\s+false;\s*\}'
        if (-not $ropingTargetBounded) {
            $ropingTargetSafetyIssues += "$ropingServicePath :: CanStartPawnRope must reject non-Mugirl ropees before allowing rope jobs to start"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropingServicePath :: missing file for roping service target boundary safety"
    }

    $ropingTargetSafetyChecks++
    $ropeMooPath = '1.6\Source\Features\Roping\JobDriver_RopeMoo.cs'
    if (Test-Path -LiteralPath $ropeMooPath) {
        $ropeMooText = Get-Content -LiteralPath $ropeMooPath -Encoding utf8 -Raw
        $ropeMooInvasiveBranchesRemoved = $ropeMooText -match 'NotifyRopeAccepted\(pawn\)' `
            -and $ropeMooText -notmatch 'GetAcceptArrestChance|MessageRefusedRope|RopeRejected|MentalStateDefOf\.Berserk|Notify_MemberCaptured|CheckAcceptRope'
        if (-not $ropeMooInvasiveBranchesRemoved) {
            $ropingTargetSafetyIssues += "$ropeMooPath :: rope job must not keep vanilla arrest chance/refusal/berserk branches for non-Mugirl pawns"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropeMooPath :: missing file for rope job target boundary safety"
    }

    $ropingTargetSafetyChecks++
    $floatMenuRopePath = '1.6\Source\Features\Roping\FloatMenuProvider_RopeMoo.cs'
    if (Test-Path -LiteralPath $floatMenuRopePath) {
        $floatMenuRopeText = Get-Content -LiteralPath $floatMenuRopePath -Encoding utf8 -Raw
        $floatMenuRopeBounded = $floatMenuRopeText -match 'TargetPawnValid\(Pawn\s+target,\s*FloatMenuContext\s+context\)[\s\S]*!RopingService\.IsMugirlRopee\(target\)' `
            -and $floatMenuRopeText -match '1f\.ToStringPercent\(\)' `
            -and $floatMenuRopeText -match 'RopingService\.IsRopedByPawn\(target\)' `
            -and $floatMenuRopeText -match '!isPawnRopedByPawn\s*&&\s*!isPawnRopedToThing' `
            -and $floatMenuRopeText -notmatch 'GetAcceptArrestChance|target\.IsPrisonerOfColony|target\.IsSlave'
        if (-not $floatMenuRopeBounded) {
            $ropingTargetSafetyIssues += "$floatMenuRopePath :: rope float menu must expose only Mugirl targets, use tracker-based unrope state and avoid non-Mugirl arrest chance branches"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$floatMenuRopePath :: missing file for rope float menu target boundary safety"
    }

    $ropingTargetSafetyChecks++
    $ropingTickPath = '1.6\Source\Features\Roping\Harmony_RopingTick.cs'
    if (Test-Path -LiteralPath $ropingTickPath) {
        $ropingTickText = Get-Content -LiteralPath $ropingTickPath -Encoding utf8 -Raw
        $ropingTickSafe = $ropingTickText -match 'EndCustomFollowJobs\(owner,\s*tracker\.Ropees\)' `
            -and $ropingTickText -match 'for\s*\(\s*int\s+i\s*=\s*ropees\.Count\s*-\s*1;\s*i\s*>=\s*0;\s*i--\s*\)' `
            -and $ropingTickText -match 'owner\s*==\s*null\s*\|\|\s*ropees\s*==\s*null' `
            -and $ropingTickText -match 'ropee\.jobs\s*!=\s*null' `
            -and $ropingTickText -match 'ropee\.CurJob\?\.targetA\.Thing\s*==\s*owner' `
            -and $ropingTickText -match 'ShouldUseMugirlRopeeTick' `
            -and $ropingTickText -match 'ClearDraftedRopee\(pawn\)' `
            -and $ropingTickText -notmatch 'new\s+List<Pawn>\s*\(\s*tracker\.Ropees\s*\)'
        if (-not $ropingTickSafe) {
            $ropingTargetSafetyIssues += "$ropingTickPath :: roping tick must end custom follow jobs before BreakAllRopes, keep drafted Mugirl ropees from falling through to vanilla break logic and avoid snapshotting tracker.Ropees"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropingTickPath :: missing file for roping tick allocation safety"
    }

    $ropingTargetSafetyChecks++
    $mapRopingIndexPath = '1.6\Source\Features\Roping\MapRopingIndex.cs'
    if (Test-Path -LiteralPath $mapRopingIndexPath) {
        $mapRopingIndexText = Get-Content -LiteralPath $mapRopingIndexPath -Encoding utf8 -Raw
        $mapRopingIndexSafe = $mapRopingIndexText -match 'private\s+int\s+reconcileTickCounter' `
            -and $mapRopingIndexText -match '\+\+reconcileTickCounter\s*>=\s*ReconcileBatchTicks' `
            -and $mapRopingIndexText -match 'ReconcileNextBatch\(\)' `
            -and $mapRopingIndexText -match 'PruneInvalidIndexedRopes\(\)' `
            -and $mapRopingIndexText -notmatch 'Find\.TickManager'
        if (-not $mapRopingIndexSafe) {
            $ropingTargetSafetyIssues += "$mapRopingIndexPath :: roping index rebuild cadence must use a map-local counter instead of direct Find.TickManager modulo checks"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$mapRopingIndexPath :: missing file for roping index tick cadence safety"
    }

    $ropingTargetSafetyChecks++
    if (Test-Path -LiteralPath $mapRopingIndexPath) {
        $mapRopingIndexText = Get-Content -LiteralPath $mapRopingIndexPath -Encoding utf8 -Raw
        $mapRopingPendingSafe = $mapRopingIndexText -match 'tmpPendingSpotRopeRemovals' `
            -and $mapRopingIndexText -match 'PruneInvalidPendingSpotRopes\(\)' `
            -and $mapRopingIndexText -match '!CanIndex\(pawn\)\s*\|\|\s*pawn\.Map\s*!=\s*map\s*\|\|\s*pawn\.roping\?\.IsRopedToSpot\s*==\s*true' `
            -and $mapRopingIndexText -match 'RegisterPawnRope\(Pawn\s+roper,\s*Pawn\s+ropee\)[\s\S]*pendingSpotRope\.Remove\(ropee\)' `
            -and $mapRopingIndexText -match 'RegisterRopedToSpot\(Pawn\s+ropee\)[\s\S]*pendingSpotRope\.Remove\(ropee\)' `
            -and $mapRopingIndexText -match 'pendingSpotRope\.Remove\(tmpPendingSpotRopeRemovals\[i\]\)'
        if (-not $mapRopingPendingSafe) {
            $ropingTargetSafetyIssues += "$mapRopingIndexPath :: pending spot-rope cache must be pruned on rebuild and cleared when real pawn/spot rope state is registered"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$mapRopingIndexPath :: missing file for pending spot-rope cache safety"
    }

    $ropingTargetSafetyChecks++
    $ropeToHitchPath = '1.6\Source\Features\Roping\JobDriver_RopeToWallRopeHitch.cs'
    if (Test-Path -LiteralPath $ropeToHitchPath) {
        $ropeToHitchText = Get-Content -LiteralPath $ropeToHitchPath -Encoding utf8 -Raw
        $ropeToHitchPendingSafe = $ropeToHitchText -match 'private\s+Map\s+pendingSpotRopeMap' `
            -and $ropeToHitchText -match 'AddFinishAction\(_\s*=>\s*ClearPendingSpotRope\(ropee\)\)' `
            -and $ropeToHitchText -match 'pendingSpotRopeMap\s*=\s*ropee\.Map' `
            -and $ropeToHitchText -match 'private\s+void\s+ClearPendingSpotRope\(Pawn\s+ropee\)' `
            -and $ropeToHitchText -match 'RopingService\.ClearPendingSpotRope\(ropee,\s*pendingSpotRopeMap\)' `
            -and $ropeToHitchText -match 'pendingSpotRopeMap\s*=\s*null'
        if (-not $ropeToHitchPendingSafe) {
            $ropingTargetSafetyIssues += "$ropeToHitchPath :: rope-to-hitch job must clear pending spot-rope state against the original map on finish/fail/success"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropeToHitchPath :: missing file for rope-to-hitch pending spot-rope safety"
    }

    $ropingTargetSafetyChecks++
    if ((Test-Path -LiteralPath $ropeToHitchPath) -and (Test-Path -LiteralPath $ropingServicePath)) {
        $ropeToHitchText = Get-Content -LiteralPath $ropeToHitchPath -Encoding utf8 -Raw
        $ropingServiceText = Get-Content -LiteralPath $ropingServicePath -Encoding utf8 -Raw
        $ropeToHitchPreservesOtherRopees = $ropeToHitchText -match 'DropPawnRopeAndNotify\(roper,\s*ropee\)' `
            -and $ropeToHitchText -notmatch 'BreakAllRopesAndNotify\(roper\)' `
            -and $ropingServiceText -match 'public\s+static\s+void\s+DropPawnRopeAndNotify\(Pawn\s+roper,\s*Pawn\s+ropee\)[\s\S]*DropRope\(ropee\)[\s\S]*NotifyPawnNoLongerRopedToTarget\(ropee\)'
        if (-not $ropeToHitchPreservesOtherRopees) {
            $ropingTargetSafetyIssues += "$ropeToHitchPath :: rope-to-hitch job must drop only the selected ropee and preserve the roper's remaining pawn ropes"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropeToHitchPath :: missing file for multi-ropee rope-to-hitch safety"
    }

    $ropingTargetSafetyChecks++
    $followRoperPath = '1.6\Source\Features\Roping\JobDriver_FollowRoper.cs'
    if (Test-Path -LiteralPath $followRoperPath) {
        $followRoperText = Get-Content -LiteralPath $followRoperPath -Encoding utf8 -Raw
        $followRoperRejectsOrphanJob = $followRoperText -match 'pawn\.roping\?\.RopedByPawn\s*!=\s*roper[\s\S]*EndJobWith\(JobCondition\.Incompletable\)'
        if (-not $followRoperRejectsOrphanJob) {
            $ropingTargetSafetyIssues += "$followRoperPath :: follow-roper job must stop when its RopeTracker no longer points to the job target"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$followRoperPath :: missing file for orphan follow-roper job safety"
    }

    if (Test-Path -LiteralPath $ropingServicePath) {
        $ropingServiceText = Get-Content -LiteralPath $ropingServicePath -Encoding utf8 -Raw
        $ropingServicePendingSafe = $ropingServiceText -match 'public\s+static\s+void\s+ClearPendingSpotRope\(Pawn\s+pawn,\s*Map\s+map\)' `
            -and $ropingServiceText -match 'map\?\.GetComponent<MapRopingIndex>\(\)' `
            -and $ropingServiceText -match 'pawn\?\.Map\s*!=\s*null\s*&&\s*pawn\.Map\s*!=\s*map' `
            -and $ropingServiceText -match 'ClearPendingSpotRope\(pawn\)'
        if (-not $ropingServicePendingSafe) {
            $ropingTargetSafetyIssues += "$ropingServicePath :: pending spot-rope clearing must support original-map cleanup plus current-map fallback"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropingServicePath :: missing file for roping service pending spot-rope safety"
    }

    $ropingTargetSafetyChecks++
    $pawnDraftControllerPath = '1.6\Source\Features\Roping\Harmony_PawnDraftController.cs'
    if (Test-Path -LiteralPath $pawnDraftControllerPath) {
        $pawnDraftControllerText = Get-Content -LiteralPath $pawnDraftControllerPath -Encoding utf8 -Raw
        $pawnDraftControllerSafe = $pawnDraftControllerText -match 'Pawn\s+pawn\s*=\s*__instance\?\.pawn' `
            -and $pawnDraftControllerText -match 'DisableDraftGizmo\(__result\)' `
            -and $pawnDraftControllerText -match 'private\s+static\s+IEnumerable<Gizmo>\s+DisableDraftGizmo\(IEnumerable<Gizmo>\s+gizmos\)' `
            -and $pawnDraftControllerText -match 'RopingService\.IsRopedByPawn\(pawn\)' `
            -and $pawnDraftControllerText -match 'RopingService\.IsPendingSpotRope\(pawn\)' `
            -and $pawnDraftControllerText -match 'yield\s+return\s+gizmo\s*;' `
            -and $pawnDraftControllerText -match 'toggle\.Disable\("Mugirl\.DraftDisabledWhileRoped"\.Translate\(\)\)' `
            -and $pawnDraftControllerText -notmatch 'new\s+List<Gizmo>'
        if (-not $pawnDraftControllerSafe) {
            $ropingTargetSafetyIssues += "$pawnDraftControllerPath :: roped pawn draft gizmo patch must use tracker/pending rope state, disable the draft toggle through a lazy iterator and avoid per-gizmo list allocation"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$pawnDraftControllerPath :: missing file for roped draft gizmo safety"
    }

    $ropingTargetSafetyChecks++
    $ropingDrawPath = '1.6\Source\Features\Roping\Harmony_RopingDraw.cs'
    if (Test-Path -LiteralPath $ropingDrawPath) {
        $ropingDrawText = Get-Content -LiteralPath $ropingDrawPath -Encoding utf8 -Raw
        $wallHitchAnchorSafe = $ropingDrawText -match 'WallHitchAnchor\(Building\s+hitch\)' `
            -and $ropingDrawText -match 'case\s+0:[\s\S]*new\s+Vector3\(0f,\s*0f,\s*0\.5f\)' `
            -and $ropingDrawText -match 'case\s+1:[\s\S]*new\s+Vector3\(0\.5f,\s*0f,\s*0f\)' `
            -and $ropingDrawText -match 'case\s+2:[\s\S]*new\s+Vector3\(0f,\s*0f,\s*-0\.5f\)' `
            -and $ropingDrawText -match 'case\s+3:[\s\S]*new\s+Vector3\(-0\.5f,\s*0f,\s*0f\)'
        if (-not $wallHitchAnchorSafe) {
            $ropingTargetSafetyIssues += "$ropingDrawPath :: wall rope hitch draw anchor must use the facing-side cell edge"
        }
    }
    else {
        $ropingTargetSafetyIssues += "$ropingDrawPath :: missing file for wall rope hitch draw anchor safety"
    }

    if ($ropingTargetSafetyIssues.Count) {
        $ropingTargetSafetyIssues | Sort-Object
        Fail "Roping target safety scan failed: $($ropingTargetSafetyIssues.Count) issue(s)"
    }

    Write-Step "Mounting state safety"
    $mountingStateSafetyChecks = 0
    $mountingStateSafetyIssues = @()
    $mountingStateSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Mounting\Comp_MugirlMount.cs'
            Pattern = 'TryDrop\(rider,\s*cell,\s*map,\s*ThingPlaceMode\.Near,\s*out Thing (dropped|_),\s*null,\s*null'
            Description = 'mounted rider drop has no dismount cell validator'
        },
        @{
            Path = '1.6\Source\Features\Mounting\MountedPawnUtility.cs'
            Pattern = 'Find\.Selector\.Select\(carrier\);'
            Description = 'mounted pawn gizmo selects captured carrier'
        },
        @{
            Path = '1.6\Source\Features\Mounting\MountedPawnUtility.cs'
            Pattern = '\bcomp\?\.TryDismount\(\);'
            Description = 'mounted pawn gizmo dismounts captured comp'
        },
        @{
            Path = '1.6\Source\Features\Mounting\MountedPawnCombatTurret.cs'
            Pattern = 'public Thing Thing => comp\.MooPawn;'
            Description = 'mounted combat searcher assumes comp is non-null'
        },
        @{
            Path = '1.6\Source\Features\Mounting\MountedPawnCombatTurret.cs'
            Pattern = 'public (LocalTargetInfo LastAttackedTarget|int LastAttackTargetTick) => comp\.turret'
            Description = 'mounted combat searcher assumes comp turret state is non-null'
        },
        @{
            Path = '1.6\Source\Features\Mounting\FloatMenuProvider_MountMugirl.cs'
            Pattern = '\(\)\s*=>\s*comp\.TryDismount\(\)'
            Description = 'mount float menu dismounts captured comp'
        },
        @{
            Path = '1.6\Source\Features\Mounting\JobDriver_MountMugirl.cs'
            Pattern = 'job\.GetTarget\(MooInd\)\.Pawn'
            Description = 'mount job target uses Pawn shortcut without explicit Thing type check'
        },
        @{
            Path = '1.6\Source\Features\Mounting\JobDriver_DismountMugirl.cs'
            Pattern = 'job\.GetTarget\(MooInd\)\.Pawn'
            Description = 'dismount job target uses Pawn shortcut without explicit Thing type check'
        }
    )

    foreach ($rule in $mountingStateSafetyRules) {
        $mountingStateSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $mountingStateSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $mountingStateSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }
    if ($mountingStateSafetyIssues.Count) {
        $mountingStateSafetyIssues | Sort-Object
        Fail "Mounting state safety scan failed: $($mountingStateSafetyIssues.Count) issue(s)"
    }

    $mountingStateSafetyChecks++
    $mountFloatMenuPath = '1.6\Source\Features\Mounting\FloatMenuProvider_MountMugirl.cs'
    if (Test-Path -LiteralPath $mountFloatMenuPath) {
        $mountFloatMenuText = Get-Content -LiteralPath $mountFloatMenuPath -Encoding utf8 -Raw
        if ($mountFloatMenuText -notmatch 'TryStartMountJob' -or $mountFloatMenuText -notmatch 'TryStartDismountJob' -or $mountFloatMenuText -notmatch 'CanReserveAndReach') {
            $mountingStateSafetyIssues += "$mountFloatMenuPath :: mount float menu actions must recheck mount state, reserve and reach at click time"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountFloatMenuPath :: missing file for mount float menu safety"
    }

    $mountingStateSafetyChecks++
    $mountCompPath = '1.6\Source\Features\Mounting\Comp_MugirlMount.cs'
    if (Test-Path -LiteralPath $mountCompPath) {
        $mountCompText = Get-Content -LiteralPath $mountCompPath -Encoding utf8 -Raw
        $mountCompSafe = $mountCompText -match 'props\s+as\s+CompProperties_MugirlMount' `
            -and $mountCompText -match 'CompProperties_MugirlMount\s+mountProps\s*=\s*Props' `
            -and $mountCompText -match 'mountProps\s*==\s*null' `
            -and $mountCompText -match 'Mathf\.Max\(1,\s*mountProps\.physiologicalTickInterval\)' `
            -and $mountCompText -match 'Mathf\.Max\(1,\s*mountProps\.safetyCheckInterval\)' `
            -and $mountCompText -match 'Mathf\.Max\(1,\s*mountProps\.turretTickInterval\)' `
            -and $mountCompText -notmatch '\(CompProperties_MugirlMount\)props'
        if (-not $mountCompSafe) {
            $mountingStateSafetyIssues += "$mountCompPath :: mount comp must safe-cast props and clamp tick intervals before mounted tick work"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountCompPath :: missing file for mount comp props safety"
    }

    $mountingStateSafetyChecks++
    $mountUtilityPath = '1.6\Source\Features\Mounting\MountedPawnUtility.cs'
    if (Test-Path -LiteralPath $mountUtilityPath) {
        $mountUtilityText = Get-Content -LiteralPath $mountUtilityPath -Encoding utf8 -Raw
        if ($mountUtilityText -notmatch 'if\s*\(\s*props\s*==\s*null\s*\)' -or $mountUtilityText -notmatch 'return\s+Vector3\.zero') {
            $mountingStateSafetyIssues += "$mountUtilityPath :: mounted rider offset helper must tolerate missing comp props"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountUtilityPath :: missing file for mount offset safety"
    }

    $mountingStateSafetyChecks++
    $mountedCombatPath = '1.6\Source\Features\Mounting\MountedPawnCombatTurret.cs'
    if (Test-Path -LiteralPath $mountedCombatPath) {
        $mountedCombatText = Get-Content -LiteralPath $mountedCombatPath -Encoding utf8 -Raw
        if ($mountedCombatText -notmatch 'DefaultTurretTickInterval' -or
            $mountedCombatText -notmatch 'private\s+static\s+int\s+TurretTickInterval' -or
            $mountedCombatText -match 'comp\.Props\.turretTickInterval') {
            $mountingStateSafetyIssues += "$mountedCombatPath :: mounted combat cooldowns must use a safe turret interval helper"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountedCombatPath :: missing file for mounted combat props safety"
    }

    $mountingStateSafetyChecks++
    if (Test-Path -LiteralPath $mountedCombatPath) {
        $mountedCombatText = Get-Content -LiteralPath $mountedCombatPath -Encoding utf8 -Raw
        $mountedCombatTickSafe = $mountedCombatText -match 'MugirlTickUtility\.CurrentGameTickOrFallback\(comp\.turretCastStartTick\)' `
            -and $mountedCombatText -match 'MugirlTickUtility\.CurrentGameTickOrFallback\(comp\.turretLastAttackTargetTick\)' `
            -and $mountedCombatText -notmatch 'Find\.TickManager\.TicksGame'
        if (-not $mountedCombatTickSafe) {
            $mountingStateSafetyIssues += "$mountedCombatPath :: mounted combat cast and last-attack timestamps must use MugirlTickUtility fallback reads"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountedCombatPath :: missing file for mounted combat tick timestamp safety"
    }

    $mountingStateSafetyChecks++
    if (Test-Path -LiteralPath $mountUtilityPath) {
        $mountUtilityText = Get-Content -LiteralPath $mountUtilityPath -Encoding utf8 -Raw
        $mountedPawnUtilityTrackerHelpersSafe = $mountUtilityText -match 'public\s+static\s+bool\s+IsHumanlike\(Pawn\s+pawn\)' `
            -and $mountUtilityText -match 'public\s+static\s+bool\s+HasCapacity\(Pawn\s+pawn,\s*PawnCapacityDef\s+capacity\)' `
            -and $mountUtilityText -match 'public\s+static\s+bool\s+IsAwake\(Pawn\s+pawn\)' `
            -and $mountUtilityText -match 'public\s+static\s+Verb\s+TryGetMeleeVerb\(Pawn\s+pawn,\s*Thing\s+target\)' `
            -and $mountUtilityText -match 'public\s+static\s+BodyPartRecord\s+GetRandomNotMissingPart\(Pawn\s+pawn,\s*DamageDef\s+damageDef,\s*BodyPartHeight\s+height,\s*BodyPartDepth\s+depth\)' `
            -and $mountUtilityText -match 'public\s+static\s+float\s+EquipmentDrawDistanceFactor\(Pawn\s+pawn\)' `
            -and $mountUtilityText -match 'pawn\?\.health\?\.capacities\?\.CapableOf\(capacity\)\s*==\s*true' `
            -and $mountUtilityText -match 'pawn\?\.health\?\.capacities\?\.CanBeAwake\s*!=\s*true' `
            -and $mountUtilityText -match 'pawn\?\.meleeVerbs\?\.TryGetMeleeVerb\(target\)' `
            -and $mountUtilityText -match 'pawn\?\.health\?\.hediffSet\?\.GetRandomNotMissingPart\(damageDef,\s*height,\s*depth\)' `
            -and $mountUtilityText -match 'pawn\?\.ageTracker\?\.CurLifeStage\?\.equipmentDrawDistanceFactor\s*\?\?\s*1f'
        if (-not $mountedPawnUtilityTrackerHelpersSafe) {
            $mountingStateSafetyIssues += "$mountUtilityPath :: mounted pawn tracker/life-stage helpers must centralize null-safe health, melee verb and draw-distance access"
        }
    }
    else {
        $mountingStateSafetyIssues += "$mountUtilityPath :: missing file for mounted pawn tracker helper safety"
    }

    $mountingStateSafetyChecks++
    $directMountedTrackerAccess = @()
    Get-ChildItem -LiteralPath '1.6\Source\Features\Mounting' -Recurse -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\MountedPawnUtility\.cs$' } |
        ForEach-Object {
            Select-String -LiteralPath $_.FullName -Pattern 'ageTracker\??\.CurLifeStage|health\??\.capacities\??\.CapableOf|health\??\.hediffSet\??\.GetRandomNotMissingPart|meleeVerbs\??\.TryGetMeleeVerb|RaceProps\??\.Humanlike|\.Awake\(\)' | ForEach-Object {
                $directMountedTrackerAccess += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
            }
        }
    if ($directMountedTrackerAccess.Count) {
        $directMountedTrackerAccess | Sort-Object
        $mountingStateSafetyIssues += "1.6\Source\Features\Mounting :: mounted pawn tracker/life-stage access must go through MountedPawnUtility helpers"
    }

    $mountingStateSafetyChecks++
    $selectionUtilityPath = '1.6\Source\Core\MugirlSelectionUtility.cs'
    if (Test-Path -LiteralPath $selectionUtilityPath) {
        $selectionUtilityText = Get-Content -LiteralPath $selectionUtilityPath -Encoding utf8 -Raw
        $mountCompSelectionText = ''
        if (Test-Path -LiteralPath $mountCompPath) {
            $mountCompSelectionText = Get-Content -LiteralPath $mountCompPath -Encoding utf8 -Raw
        }

        $mountUtilitySelectionText = ''
        if (Test-Path -LiteralPath $mountUtilityPath) {
            $mountUtilitySelectionText = Get-Content -LiteralPath $mountUtilityPath -Encoding utf8 -Raw
        }

        $mountSelectionSafe = $selectionUtilityText -match 'Current\.ProgramState\s*==\s*ProgramState\.Playing' `
            -and $selectionUtilityText -match 'Find\.Selector\s*!=\s*null' `
            -and $selectionUtilityText -match 'SelectInPlaying' `
            -and $selectionUtilityText -match 'ReselectIfSelectedInPlaying' `
            -and $mountCompSelectionText -match 'MugirlSelectionUtility\.IsSelectedInPlaying' `
            -and $mountCompSelectionText -match 'MugirlSelectionUtility\.SelectInPlaying' `
            -and $mountCompSelectionText -notmatch 'Find\.Selector' `
            -and $mountUtilitySelectionText -match 'MugirlSelectionUtility\.SelectInPlaying\(currentCarrier\)' `
            -and $mountUtilitySelectionText -notmatch 'Find\.Selector'
        if (-not $mountSelectionSafe) {
            $mountingStateSafetyIssues += "$selectionUtilityPath :: mounted selection paths must use guarded MugirlSelectionUtility instead of direct Find.Selector access"
        }
    }
    else {
        $mountingStateSafetyIssues += "$selectionUtilityPath :: missing file for selection UI safety"
    }

    if ($mountingStateSafetyIssues.Count) {
        $mountingStateSafetyIssues | Sort-Object
        Fail "Mounting state safety scan failed: $($mountingStateSafetyIssues.Count) issue(s)"
    }

    Write-Step "Milk interaction safety"
    $milkInteractionSafetyChecks = 0
    $milkInteractionSafetyIssues = @()
    $milkInteractionSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Milk\JobDriver_DrinkMilkFromMugirl.cs'
            Pattern = '\(\s*Pawn\s*\)\s*job\.GetTarget'
            Description = 'drink milk job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Milk\JobDriver_Breastfeed.cs'
            Pattern = '\(\s*Pawn\s*\)\s*job\.GetTarget'
            Description = 'breastfeed job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Milk\JobDriver_FeedMilkToDowned.cs'
            Pattern = '\(\s*Pawn\s*\)\s*job\.GetTarget'
            Description = 'feed milk job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Milk\JobDriver_GatherBodyResources.cs'
            Pattern = '\(\s*Pawn\s*\).*job\.GetTarget'
            Description = 'gather body resources job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Milk\JobDriver_GatherMilk.cs'
            Pattern = '\(\s*Pawn\s*\).*job\.GetTarget'
            Description = 'gather milk job directly casts job target'
        }
    )

    foreach ($rule in $milkInteractionSafetyRules) {
        $milkInteractionSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $milkInteractionSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $milkInteractionSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $milkInteractionSafetyChecks++
    $milkUtilityPath = '1.6\Source\Features\Milk\MugirlMilkInteractionUtility.cs'
    if (Test-Path -LiteralPath $milkUtilityPath) {
        $milkUtilityText = Get-Content -LiteralPath $milkUtilityPath -Encoding utf8 -Raw
        if ($milkUtilityText -match 'internal\s+static\s+void\s+Apply(AdultDrink|Feed|ChildBreastfeed)\s*\(') {
            $milkInteractionSafetyIssues += "$milkUtilityPath :: direct milk Apply methods must return bool and fail when milk cannot be consumed"
        }
        if ($milkUtilityText -notmatch 'CanChildBreastfeedNow' -or $milkUtilityText -notmatch 'ChildBreastfeedConsumption' -or $milkUtilityText -notmatch 'TryConsumeMilk') {
            $milkInteractionSafetyIssues += "$milkUtilityPath :: child breastfeed must use its own consumption threshold and atomic milk consumption"
        }
        if ($milkUtilityText -notmatch 'target\.Downed\s*\|\|\s*target\.IsColonist') {
            $milkInteractionSafetyIssues += "$milkUtilityPath :: direct feeding must support both downed humanlikes and standing colonists"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkUtilityPath :: missing file for atomic direct milk consumption"
    }

    $milkInteractionSafetyChecks++
    $milkFloatMenuPath = '1.6\Source\Features\Milk\Harmony_FloatMenu_Milk.cs'
    if (Test-Path -LiteralPath $milkFloatMenuPath) {
        $milkFloatMenuText = Get-Content -LiteralPath $milkFloatMenuPath -Encoding utf8 -Raw
        if ($milkFloatMenuText -notmatch 'CanReserveAndReach') {
            $milkInteractionSafetyIssues += "$milkFloatMenuPath :: milk float menu actions must recheck reserve and reach at click time"
        }
        if ($milkFloatMenuText -notmatch 'ManagedByDevice') {
            $milkInteractionSafetyIssues += "$milkFloatMenuPath :: gather milk menu must expose milking-device management as a disabled reason"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkFloatMenuPath :: missing file for milk float menu safety"
    }

    $milkInteractionSafetyChecks++
    $feedMilkPath = '1.6\Source\Features\Milk\JobDriver_FeedMilkToDowned.cs'
    if (Test-Path -LiteralPath $feedMilkPath) {
        $feedMilkText = Get-Content -LiteralPath $feedMilkPath -Encoding utf8 -Raw
        $feedMilkSafe = $feedMilkText -match 'ForceMilkInteractionWait' `
            -and $feedMilkText -match 'forcedWaitJobLoadId' `
            -and $feedMilkText -match 'CleanupForcedWait\(\)' `
            -and $feedMilkText -match 'FeedingRecipientFacing'
        if (-not $feedMilkSafe) {
            $milkInteractionSafetyIssues += "$feedMilkPath :: standing feed recipients must be held in a tracked wait job and safely released"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$feedMilkPath :: missing file for standing recipient wait safety"
    }

    $milkInteractionSafetyChecks++
    $gatherMilkPath = '1.6\Source\Features\Milk\JobDriver_GatherMilk.cs'
    if (Test-Path -LiteralPath $gatherMilkPath) {
        $gatherMilkText = Get-Content -LiteralPath $gatherMilkPath -Encoding utf8 -Raw
        if ($gatherMilkText -notmatch 'IsManagedByMilkingDevice') {
            $milkInteractionSafetyIssues += "$gatherMilkPath :: gather milk job must reject milk sources managed by a milking device"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$gatherMilkPath :: missing file for milking-device gather guard"
    }

    $milkInteractionSafetyChecks++
    $lactationPath = '1.6\Source\Features\Milk\HediffComp_Lactation.cs'
    if (Test-Path -LiteralPath $lactationPath) {
        $lactationText = Get-Content -LiteralPath $lactationPath -Encoding utf8 -Raw
        $lactationSafe = $lactationText -match 'props\s+as\s+CompProperties_Lactation' `
            -and $lactationText -match 'MugirlTickUtility\.TryGetCurrentGameTick' `
            -and $lactationText -match 'pawn\?\.needs\?\.food\?\.CurCategory' `
            -and $lactationText -match 'Mathf\.Max\(0f,\s*lactationProps\.baseProductionMultiplier\)' `
            -and $lactationText -match 'mother\.Destroyed' `
            -and $lactationText -match 'lactationHediff\?\.TryGetComp' `
            -and $lactationText -notmatch 'Find\.TickManager\.TicksGame'
        if (-not $lactationSafe) {
            $milkInteractionSafetyIssues += "$lactationPath :: lactation multiplier and birth notification must guard props, tick access, food needs and invalid mothers"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$lactationPath :: missing file for lactation multiplier safety"
    }

    $milkInteractionSafetyChecks++
    $milkablePath = '1.6\Source\Features\Milk\CompMooMilkable.cs'
    if (Test-Path -LiteralPath $milkablePath) {
        $milkableText = Get-Content -LiteralPath $milkablePath -Encoding utf8 -Raw
        $milkableLactationSafe = $milkableText -match 'props\s+as\s+CompProperties_MooMilkable' `
            -and $milkableText -match 'Props\?\.saveKey\s*\?\?\s*"milkFullness"' `
            -and $milkableText -match 'CompProperties_MooMilkable\s+milkProps\s*=\s*Props' `
            -and $milkableText -match 'milkProps\s*!=\s*null' `
            -and $milkableText -match 'pawn\?\.health\?\.hediffSet\s*==\s*null' `
            -and $milkableText -match 'pawn\.ageTracker\?\.CurLifeStage\?\.reproductive\s*==\s*true' `
            -and $milkableText -match 'pawn\.RaceProps\?\.Humanlike\s*==\s*true'
        if (-not $milkableLactationSafe) {
            $milkInteractionSafetyIssues += "$milkablePath :: milk production must safe-cast props, keep save-key fallback and guard health/age/race before lactation checks"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkablePath :: missing file for milkable lactation safety"
    }

    $milkInteractionSafetyChecks++
    $milkingDevicePath = '1.6\Source\Features\Milk\Comp_MilkingDevice.cs'
    if (Test-Path -LiteralPath $milkingDevicePath) {
        $milkingDeviceText = Get-Content -LiteralPath $milkingDevicePath -Encoding utf8 -Raw
        $milkingDeviceSafe = $milkingDeviceText -match 'props\s+as\s+CompProperties_MilkingDevice' `
            -and $milkingDeviceText -match 'Mathf\.Max\(1,\s*deviceProps\.maxCharges\)' `
            -and $milkingDeviceText -match 'Scribe\.mode\s*==\s*LoadSaveMode\.PostLoadInit' `
            -and $milkingDeviceText -match 'storedCharges\s*=\s*Mathf\.Max\(0,\s*storedCharges\)' `
            -and $milkingDeviceText -match 'storedMilkAmount\s*=\s*Mathf\.Max\(0,\s*storedMilkAmount\)' `
            -and $milkingDeviceText -match 'GetCommandIcon' `
            -and $milkingDeviceText -match 'MugirlLog\.WarningOnce' `
            -and $milkingDeviceText -match 'GetReleaseThingDef\(deviceProps\)' `
            -and $milkingDeviceText -match 'GetFilthThingDef\(deviceProps\)' `
            -and $milkingDeviceText -notmatch '\(CompProperties_MilkingDevice\)props'
        if (-not $milkingDeviceSafe) {
            $milkInteractionSafetyIssues += "$milkingDevicePath :: milking device comp must safe-cast props, clamp persisted storage/charges and use safe icon/Def helpers"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkingDevicePath :: missing file for milking device comp safety"
    }

    $milkInteractionSafetyChecks++
    if ((Test-Path -LiteralPath $milkablePath) -and (Test-Path -LiteralPath $milkingDevicePath)) {
        $milkableText = Get-Content -LiteralPath $milkablePath -Encoding utf8 -Raw
        $milkingDeviceText = Get-Content -LiteralPath $milkingDevicePath -Encoding utf8 -Raw
        $milkDevFillRejectFeedbackSafe = $milkableText -match 'Messages\.Message\("Mugirl\.Milk\.DevFill\.Failed"\.Translate\(pawn\.LabelShortCap\),\s*pawn,\s*MessageTypeDefOf\.RejectInput,\s*historical:\s*false\)' `
            -and $milkingDeviceText -match 'Messages\.Message\("Mugirl\.MilkingDevice\.DevFill\.Failed"\.Translate\(wearer\.LabelShortCap\),\s*wearer,\s*MessageTypeDefOf\.RejectInput,\s*historical:\s*false\)'
        if (-not $milkDevFillRejectFeedbackSafe) {
            $milkInteractionSafetyIssues += "$milkablePath / $milkingDevicePath :: DevFill reject feedback must not be archived"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkablePath / $milkingDevicePath :: missing file for DevFill reject feedback archive safety"
    }

    $milkInteractionSafetyChecks++
    $releaseEffectPath = '1.6\Source\Features\Milk\CompProperties_MilkingDeviceReleaseEffect.cs'
    if (Test-Path -LiteralPath $releaseEffectPath) {
        $releaseEffectText = Get-Content -LiteralPath $releaseEffectPath -Encoding utf8 -Raw
        $releaseEffectSafe = $releaseEffectText -match 'props\s+as\s+CompProperties_MilkingDeviceReleaseEffect' `
            -and $releaseEffectText -match 'releaseProps\s*==\s*null' `
            -and $releaseEffectText -match 'textParams\s*==\s*null' `
            -and $releaseEffectText -match 'soundParams\s*==\s*null' `
            -and $releaseEffectText -match 'fleckParams\s*==\s*null' `
            -and $releaseEffectText -match 'Mathf\.Min\(fleckParams\.speedMin,\s*fleckParams\.speedMax\)' `
            -and $releaseEffectText -match 'Mathf\.Max\(0\.01f,\s*fleckParams\.scale\)'
        if (-not $releaseEffectSafe) {
            $milkInteractionSafetyIssues += "$releaseEffectPath :: milking device release effect must guard safe props, null XML list entries and invalid fleck ranges"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$releaseEffectPath :: missing file for milking release effect safety"
    }

    $milkInteractionSafetyChecks++
    $bodyResourcePath = '1.6\Source\Features\Milk\CompMooHasBodyResource.cs'
    if (Test-Path -LiteralPath $bodyResourcePath) {
        $bodyResourceText = Get-Content -LiteralPath $bodyResourcePath -Encoding utf8 -Raw
        $bodyResourceSafe = $bodyResourceText -match 'parent\?\.Faction' `
            -and $bodyResourceText -match 'MugirlGameUtility\.IsPlaying\(\)' `
            -and $bodyResourceText -match 'CurrentGameTickOrFallback' `
            -and $bodyResourceText -match 'ResourceDef\s*==\s*null' `
            -and $bodyResourceText -match 'TryGetGatherContext' `
            -and $bodyResourceText -match 'parentMap\s*!=\s*doerMap' `
            -and $bodyResourceText -match 'WarnInvalidGather'
        if (-not $bodyResourceSafe) {
            $milkInteractionSafetyIssues += "$bodyResourcePath :: body resource comp must guard parent, program state, resource Def, invalid gather callers and cross-map gather context"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$bodyResourcePath :: missing file for body resource comp safety"
    }

    $milkInteractionSafetyChecks++
    $cureFoodEffectPath = '1.6\Source\Features\Milk\HediffComp_CureFoodEffects.cs'
    if (Test-Path -LiteralPath $cureFoodEffectPath) {
        $cureFoodEffectText = Get-Content -LiteralPath $cureFoodEffectPath -Encoding utf8 -Raw
        $cureFoodEffectSafe = $cureFoodEffectText -match 'props\s+as\s+HediffCompProperties_CureFoodEffects' `
            -and $cureFoodEffectText -match 'CureProps\s*==\s*null' `
            -and $cureFoodEffectText -match 'TryGetRemoveHediffs' `
            -and $cureFoodEffectText -match 'Pawn\?\.health\?\.hediffSet\s*!=\s*null'
        if (-not $cureFoodEffectSafe) {
            $milkInteractionSafetyIssues += "$cureFoodEffectPath :: cure food hediff comp must guard props and health/hediffSet before tick-time removals"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$cureFoodEffectPath :: missing file for cure food hediff comp safety"
    }

    $milkInteractionSafetyChecks++
    $foodEffectUtilityPath = '1.6\Source\Features\Milk\MugirlFoodEffectUtility.cs'
    if (Test-Path -LiteralPath $foodEffectUtilityPath) {
        $foodEffectUtilityText = Get-Content -LiteralPath $foodEffectUtilityPath -Encoding utf8 -Raw
        $foodEffectUtilitySafe = $foodEffectUtilityText -match 'pawn\?\.health\?\.hediffSet\s*==\s*null' `
            -and $foodEffectUtilityText -match 'HediffDefCache' `
            -and $foodEffectUtilityText -match 'internal\s+static\s+void\s+ResetDefCache\(\)' `
            -and $foodEffectUtilityText -match 'HediffDefCache\.Clear\(\)'
        if (-not $foodEffectUtilitySafe) {
            $milkInteractionSafetyIssues += "$foodEffectUtilityPath :: food effect utility must guard pawn health and expose a reset hook for static hediff Def cache"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$foodEffectUtilityPath :: missing file for food effect utility safety"
    }

    $milkInteractionSafetyChecks++
    $nurtureUtilityPath = '1.6\Source\Features\Milk\MugirlNurtureUtility.cs'
    if (Test-Path -LiteralPath $nurtureUtilityPath) {
        $nurtureUtilityText = Get-Content -LiteralPath $nurtureUtilityPath -Encoding utf8 -Raw
        $nurtureUtilityDefCacheSafe = $nurtureUtilityText -match 'private\s+static\s+HediffDef\s+motherlyNurtureDef' `
            -and $nurtureUtilityText -match 'private\s+static\s+HediffDef\s+nurtureAfterglowDef' `
            -and $nurtureUtilityText -match 'private\s+static\s+TraitDef\s+nurturedTraitDef' `
            -and $nurtureUtilityText -match 'internal\s+static\s+void\s+ResetDefCache\(\)' `
            -and $nurtureUtilityText -match 'motherlyNurtureDef\s*=\s*null' `
            -and $nurtureUtilityText -match 'nurtureAfterglowDef\s*=\s*null' `
            -and $nurtureUtilityText -match 'nurturedTraitDef\s*=\s*null'
        if (-not $nurtureUtilityDefCacheSafe) {
            $milkInteractionSafetyIssues += "$nurtureUtilityPath :: nurture utility static Def caches must expose a reset hook for Dev reload and same-process validation"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$nurtureUtilityPath :: missing file for nurture utility Def cache safety"
    }

    $milkInteractionSafetyChecks++
    $milkOutputUtilityPath = '1.6\Source\Features\Milk\MugirlMilkOutputUtility.cs'
    if (Test-Path -LiteralPath $milkOutputUtilityPath) {
        $milkOutputUtilityText = Get-Content -LiteralPath $milkOutputUtilityPath -Encoding utf8 -Raw
        $milkOutputUtilitySafe = $milkOutputUtilityText -match 'thingDef\s*==\s*null' `
            -and $milkOutputUtilityText -match 'map\s*==\s*null' `
            -and $milkOutputUtilityText -match '!position\.IsValid' `
            -and $milkOutputUtilityText -match 'Mathf\.Max\(1,\s*thingDef\.stackLimit\)'
        if (-not $milkOutputUtilitySafe) {
            $milkInteractionSafetyIssues += "$milkOutputUtilityPath :: milk output utility must reject invalid spawn inputs and clamp stack limits"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$milkOutputUtilityPath :: missing file for milk output utility safety"
    }

    $milkInteractionSafetyChecks++
    $nurtureProgressPath = '1.6\Source\Features\Milk\HediffComp_MugirlNurtureProgress.cs'
    if (Test-Path -LiteralPath $nurtureProgressPath) {
        $nurtureProgressText = Get-Content -LiteralPath $nurtureProgressPath -Encoding utf8 -Raw
        if ($nurtureProgressText -notmatch 'IsComplete\s*=>\s*parent\?\.def\s*!=\s*null') {
            $milkInteractionSafetyIssues += "$nurtureProgressPath :: nurture progress completion must guard missing parent/def before reading severity limits"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$nurtureProgressPath :: missing file for nurture progress safety"
    }

    $milkInteractionSafetyChecks++
    $babyFeedingPath = '1.6\Source\Features\Milk\Harmony_MugirlBabyFeeding.cs'
    if (Test-Path -LiteralPath $babyFeedingPath) {
        $babyFeedingText = Get-Content -LiteralPath $babyFeedingPath -Encoding utf8 -Raw
        $babyFeedingSafe = $babyFeedingText -match 'ModsConfig\.BiotechActive' `
            -and $babyFeedingText -match 'Mathf\.Max\(0f,\s*milkComp\.Fullness\)' `
            -and $babyFeedingText -match 'Need_Food\s+food\s*=\s*baby\?\.needs\?\.food' `
            -and $babyFeedingText -match 'delta\s*<=\s*0' `
            -and $babyFeedingText -match 'food\.MaxLevel\s*<=\s*0f' `
            -and $babyFeedingText -match 'feeder\.mindState\s*!=\s*null' `
            -and $babyFeedingText -match 'Mathf\.Clamp01\(consumedNutrition\s*/\s*food\.MaxLevel\)' `
            -and $babyFeedingText -match 'feeder\.ideo\s*!=\s*null' `
            -and $babyFeedingText -match 'nutritionPerFullness\s*<=\s*0f'
        if (-not $babyFeedingSafe) {
            $milkInteractionSafetyIssues += "$babyFeedingPath :: Mugirl baby feeding must guard Biotech, food need bounds, milk fullness/nutrition conversion and optional mindState/ideo hooks"
        }
    }
    else {
        $milkInteractionSafetyIssues += "$babyFeedingPath :: missing file for baby feeding safety"
    }

    if ($milkInteractionSafetyIssues.Count) {
        $milkInteractionSafetyIssues | Sort-Object
        Fail "Milk interaction safety scan failed: $($milkInteractionSafetyIssues.Count) issue(s)"
    }

    Write-Step "Incident interaction safety"
    $incidentInteractionSafetyChecks = 0
    $incidentInteractionSafetyIssues = @()
    $incidentInteractionSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Incidents\QuestPart_CourierRaid.cs'
            Pattern = '\(\s*Pawn\s*\)\s*job\.GetTarget'
            Description = 'courier talk job directly casts job target'
        },
        @{
            Path = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_CourierRaid.cs'
            Pattern = 'GenerateQuestAndMakeAvailable\(\s*MugirlContentDefOf\.Mugirl_CourierRaid\s*,\s*points\s*\)'
            Description = 'courier incident generates quest without target map slate'
        },
        @{
            Path = '1.6\Source\Features\Incidents\CompReadableBook.cs'
            Pattern = 'JobMaker\.MakeJob\(\s*MugirlContentDefOf\.Mugirl_ReadCourierDiary\s*,\s*parent\s*\)'
            Description = 'readable book menu dispatches job without click-time validation'
        },
        @{
            Path = '1.6\Source\Features\Incidents\Dialog_ReadBook.cs'
            Pattern = 'private\s+readonly\s+Thing\s+book\s*;'
            Description = 'read book dialog keeps unused book field'
        },
        @{
            Path = '1.6\Source\Features\Incidents\QuestNode_Root_Mugirl_WandererJoin_WalkIn.cs'
            Pattern = '\(\s*ChoiceLetter_AcceptJoiner\s*\)\s*LetterMaker\.MakeLetter'
            Description = 'wanderer join letter directly casts AcceptJoiner letter'
        }
    )

    foreach ($rule in $incidentInteractionSafetyRules) {
        $incidentInteractionSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $incidentInteractionSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $incidentInteractionSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $incidentInteractionSafetyChecks++
    $readableBookPath = '1.6\Source\Features\Incidents\CompReadableBook.cs'
    if (Test-Path -LiteralPath $readableBookPath) {
        $readableBookText = Get-Content -LiteralPath $readableBookPath -Encoding utf8 -Raw
        $readableBookSafe = $readableBookText -match 'props\s+as\s+CompProperties_ReadableBook' `
            -and $readableBookText -match 'BookTitle\s*=>\s*MugirlText\.Resolve\(Props\?\.bookTitle\s*\?\?\s*"Mugirl\.CourierDiary\.Title"\)' `
            -and $readableBookText -match 'selPawn\s*==\s*null\s*\|\|\s*parent\s*==\s*null' `
            -and $readableBookText -match 'comp\.BookTitle' `
            -and $readableBookText -notmatch '\(CompProperties_ReadableBook\)props'
        if (-not $readableBookSafe) {
            $incidentInteractionSafetyIssues += "$readableBookPath :: readable book comp must safe-cast props and use a fallback title through menu/job paths"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$readableBookPath :: missing file for readable book props safety"
    }

    $incidentInteractionSafetyChecks++
    $courierIncidentWorkerPath = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_CourierRaid.cs'
    if (Test-Path -LiteralPath $courierIncidentWorkerPath) {
        $courierIncidentWorkerText = Get-Content -LiteralPath $courierIncidentWorkerPath -Encoding utf8 -Raw
        if ($courierIncidentWorkerText -notmatch 'slate\.Set\(\s*"map"\s*,\s*map\s*\)') {
            $incidentInteractionSafetyIssues += "$courierIncidentWorkerPath :: courier incident must pass the incident target map through quest slate"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$courierIncidentWorkerPath :: missing file for courier incident map slate"
    }

    $incidentInteractionSafetyChecks++
    $storyStatePath = '1.6\Source\Features\Incidents\MugirlStoryState.cs'
    if (Test-Path -LiteralPath $storyStatePath) {
        $storyStateText = Get-Content -LiteralPath $storyStatePath -Encoding utf8 -Raw
        if ($storyStateText -notmatch 'internal\s+static\s+class\s+MugirlStoryService' -or
            $storyStateText -notmatch 'internal\s+static\s+void\s+Tick\(\s*MugirlStoryState\s+state\s*\)' -or
            $storyStateText -match 'public\s+static\s+class\s+MugirlStoryService') {
            $incidentInteractionSafetyIssues += "$storyStatePath :: story service helper must stay internal and not expose public static API surface"
        }

        $storyStateHasMapSlate = $storyStateText -match 'slate\.Set\(\s*"map"\s*,\s*map\s*\)'
        $storyStateUsesSlateQuest = $storyStateText -match 'GenerateQuestAndMakeAvailable\(\s*MugirlContentDefOf\.Mugirl_CourierRaid\s*,\s*slate\s*\)'
        $storyStateUsesOldPointsCall = $storyStateText -match 'GenerateQuestAndMakeAvailable\(\s*MugirlContentDefOf\.Mugirl_CourierRaid\s*,\s*CourierRaidQuestPoints\s*\)'
        if (-not $storyStateHasMapSlate -or -not $storyStateUsesSlateQuest -or $storyStateUsesOldPointsCall) {
            $incidentInteractionSafetyIssues += "$storyStatePath :: courier story timer must pass a resolved target map through quest slate"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$storyStatePath :: missing file for courier story timer safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $storyStatePath) {
        $storyStateText = Get-Content -LiteralPath $storyStatePath -Encoding utf8 -Raw
        $openingStoryHasMapSlate = $storyStateText -match 'slate\.Set\(\s*"map"\s*,\s*map\s*\)' `
            -and $storyStateText -match 'GenerateQuestAndMakeAvailable\(\s*Mugirl_DefOf\.Mugirl_SlaveOpeningPodCrash\s*,\s*slate\s*\)'
        $openingStoryUsesOldPointsCall = $storyStateText -match 'GenerateQuestAndMakeAvailable\(\s*Mugirl_DefOf\.Mugirl_SlaveOpeningPodCrash\s*,\s*OpeningCrashQuestPoints\s*\)'
        if (-not $openingStoryHasMapSlate -or $openingStoryUsesOldPointsCall) {
            $incidentInteractionSafetyIssues += "$storyStatePath :: opening crash story timer must pass a resolved target map through quest slate"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$storyStatePath :: missing file for opening crash story timer safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $storyStatePath) {
        $storyStateText = Get-Content -LiteralPath $storyStatePath -Encoding utf8 -Raw
        $storyStateTransientResetSafe = $storyStateText -match 'ResetTransientRuntimeState\(\)' `
            -and $storyStateText -match 'MugirlLog\.ResetOnceWarnings\(\)' `
            -and $storyStateText -match 'MugirlFoodEffectUtility\.ResetDefCache\(\)' `
            -and $storyStateText -match 'MugirlNurtureUtility\.ResetDefCache\(\)' `
            -and $storyStateText -match 'MugirlMilkingAnimation\.ResetTransientState\(\)' `
            -and $storyStateText -match 'MountedCombatController\.ResetTransientState\(\)'
        if (-not $storyStateTransientResetSafe) {
            $incidentInteractionSafetyIssues += "$storyStatePath :: story game component must reset static warning/cache/transient state on init, new game and load"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$storyStatePath :: missing file for transient runtime reset safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $storyStatePath) {
        $storyStateText = Get-Content -LiteralPath $storyStatePath -Encoding utf8 -Raw
        if ($storyStateText -notmatch 'GhoulRenderingRefreshUtility\.ClearPendingRefreshes\(\)') {
            $incidentInteractionSafetyIssues += "$storyStatePath :: central transient reset must clear ghoul rendering pending refreshes"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$storyStatePath :: missing file for ghoul rendering transient reset safety"
    }

    $incidentInteractionSafetyChecks++
    $generatedPawnUtilityPath = '1.6\Source\Features\Incidents\MugirlGeneratedPawnUtility.cs'
    if (Test-Path -LiteralPath $generatedPawnUtilityPath) {
        $generatedPawnUtilityText = Get-Content -LiteralPath $generatedPawnUtilityPath -Encoding utf8 -Raw
        $generatedPawnUtilitySafe = $generatedPawnUtilityText -match 'TryPassToWorld' `
            -and $generatedPawnUtilityText -match 'public\s+static\s+void\s+Discard\(\s*Pawn\s+pawn\s*\)' `
            -and $generatedPawnUtilityText -match 'pawn\.Spawned' `
            -and $generatedPawnUtilityText -match 'GeneratedPawnDiscardSpawned' `
            -and $generatedPawnUtilityText -match 'MugirlGameUtility\.TryPassToWorld\(pawn\)' `
            -and $generatedPawnUtilityText -match 'MugirlGameUtility\.TryRemoveWorldPawn\(pawn\)' `
            -and $generatedPawnUtilityText -match 'MugirlGameUtility\.TryPassToWorldForDiscard\(pawn\)' `
            -and $generatedPawnUtilityText -notmatch 'Find\.WorldPawns'
        if (-not $generatedPawnUtilitySafe) {
            $incidentInteractionSafetyIssues += "$generatedPawnUtilityPath :: generated pawn world/discard helper must guard null, destroyed and spawned pawns and use MugirlGameUtility for WorldPawns access"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$generatedPawnUtilityPath :: missing file for generated pawn discard safety"
    }

    $incidentInteractionSafetyChecks++
    $ideoUtilityPath = '1.6\Source\Features\Incidents\Mugirl_IdeoUtility.cs'
    if (Test-Path -LiteralPath $ideoUtilityPath) {
        $ideoUtilityText = Get-Content -LiteralPath $ideoUtilityPath -Encoding utf8 -Raw
        if ($ideoUtilityText -notmatch 'internal\s+static\s+class\s+Mugirl_IdeoUtility' -or
            $ideoUtilityText -notmatch 'internal\s+static\s+void\s+AdoptPlayerPrimaryIdeo\(\s*Pawn\s+pawn\s*\)' -or
            $ideoUtilityText -match 'public\s+static\s+class\s+Mugirl_IdeoUtility') {
            $incidentInteractionSafetyIssues += "$ideoUtilityPath :: Ideology helper must stay internal and not expose public static API surface"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$ideoUtilityPath :: missing file for Ideology helper API surface safety"
    }

    $incidentInteractionSafetyChecks++
    $openingPodCrashPath = '1.6\Source\Features\Incidents\QuestNode_Root_Mugirl_OpeningPodCrash.cs'
    if (Test-Path -LiteralPath $openingPodCrashPath) {
        $openingPodCrashText = Get-Content -LiteralPath $openingPodCrashPath -Encoding utf8 -Raw
        $openingPodCrashSafe = $openingPodCrashText -match 'quest\s*==\s*null\s*\|\|\s*slate\s*==\s*null' `
            -and $openingPodCrashText -match 'map\?\.Parent\s*==\s*null' `
            -and $openingPodCrashText -match 'TryAddSpawnPawnsInOnePod' `
            -and $openingPodCrashText -match 'HasUsablePawns' `
            -and $openingPodCrashText -match 'DiscardGeneratedPawns' `
            -and $openingPodCrashText -match 'MugirlGeneratedPawnUtility\.Discard' `
            -and $openingPodCrashText -match 'List<Pawn>\s+validPawns' `
            -and $openingPodCrashText -match 'validPawns\.Count'
        if (-not $openingPodCrashSafe) {
            $incidentInteractionSafetyIssues += "$openingPodCrashPath :: opening pod crash root must guard quest/slate/map/pawns and letter targets"
        }
        $openingPodGenerationFailureSafe = $openingPodCrashText -match 'OpeningPodCrashFactionlessGenerationFailed' `
            -and $openingPodCrashText -match 'Mugirl\.OpeningPodCrash\.Log\.FactionlessGenerationFailed' `
            -and $openingPodCrashText -match 'return\s+null' `
            -and $openingPodCrashText -notmatch 'throw\s+new' `
            -and $openingPodCrashText -notmatch 'MugirlLog\.Error'
        if (-not $openingPodGenerationFailureSafe) {
            $incidentInteractionSafetyIssues += "$openingPodCrashPath :: opening pod crash pawn generation failure must degrade with WarningOnce/null return, not throw or hard-error"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$openingPodCrashPath :: missing file for opening pod crash root safety"
    }

    $incidentInteractionSafetyChecks++
    $courierQuestPartPath = '1.6\Source\Features\Incidents\QuestPart_CourierRaid.cs'
    if (Test-Path -LiteralPath $courierQuestPartPath) {
        $courierQuestPartText = Get-Content -LiteralPath $courierQuestPartPath -Encoding utf8 -Raw
        if ($courierQuestPartText -notmatch 'CanNegotiateWithCourier' -or $courierQuestPartText -notmatch 'TryStartTalkJob') {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: courier talk menu/job/dialog must share current negotiation validation"
        }
        if ($courierQuestPartText -match '\(\)\s*=>\s*(StartFight|DropItemsAndLeave)\(\s*courier\s*\)') {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: courier dialog callbacks must revalidate stale courier state before resolving"
        }
        if ($courierQuestPartText -match 'QuestPart_CourierDemand|CountOnPlayerMaps|ConsumeFromPlayerMaps') {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: unused courier demand resource-confiscation code must not return"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$courierQuestPartPath :: missing file for courier interaction safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $courierQuestPartPath) {
        $courierQuestPartText = Get-Content -LiteralPath $courierQuestPartPath -Encoding utf8 -Raw
        if ($courierQuestPartText -notmatch '!string\.IsNullOrEmpty\(inSignal\)\s*&&\s*signal\.tag\s*==\s*inSignal') {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: courier spawn quest part must ignore empty inSignal values"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$courierQuestPartPath :: missing file for courier spawn signal safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $courierQuestPartPath) {
        $courierQuestPartText = Get-Content -LiteralPath $courierQuestPartPath -Encoding utf8 -Raw
        $courierAlreadyResolvedMessages = [regex]::Matches($courierQuestPartText, 'Messages\.Message\("Mugirl\.CourierAlreadyResolved"\.Translate\(\),\s*MessageTypeDefOf\.RejectInput')
        $courierAlreadyResolvedNonHistorical = [regex]::Matches($courierQuestPartText, 'Messages\.Message\("Mugirl\.CourierAlreadyResolved"\.Translate\(\),\s*MessageTypeDefOf\.RejectInput,\s*historical:\s*false\)')
        if ($courierAlreadyResolvedMessages.Count -eq 0 -or $courierAlreadyResolvedMessages.Count -ne $courierAlreadyResolvedNonHistorical.Count) {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: courier already-resolved reject feedback must not be archived"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$courierQuestPartPath :: missing file for courier already-resolved feedback archive safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $courierQuestPartPath) {
        $courierQuestPartText = Get-Content -LiteralPath $courierQuestPartPath -Encoding utf8 -Raw
        $courierNullClears = [regex]::Matches($courierQuestPartText, 'courier\s*=\s*null').Count
        $courierSpawnSafe = $courierQuestPartText -match 'courier\s*==\s*null\s*\|\|\s*courier\.inventory\?\.innerContainer\s*==\s*null\s*\|\|\s*courier\.mindState\s*==\s*null' `
            -and $courierQuestPartText -match 'MugirlGeneratedPawnUtility\.Discard\(courier\)' `
            -and $courierQuestPartText -match 'CourierGenerationFailed' `
            -and $courierQuestPartText -match 'CourierSpawnFailed' `
            -and $courierNullClears -ge 2
        $courierPayloadSafe = $courierQuestPartText -match 'pawn\?\.inventory\?\.innerContainer\s*==\s*null' `
            -and $courierQuestPartText -match 'ThingDef\s+def\s*=\s*inventory\[i\]\?\.def'
        $courierFightSafe = $courierQuestPartText -match 'courier\.mindState\?\.mentalStateHandler\s*==\s*null'
        if (-not $courierSpawnSafe -or -not $courierPayloadSafe -or -not $courierFightSafe) {
            $incidentInteractionSafetyIssues += "$courierQuestPartPath :: courier spawn, payload and fight paths must guard generated pawn, inventory, spawn success and mindState"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$courierQuestPartPath :: missing file for courier spawn safety"
    }

    $incidentInteractionSafetyChecks++
    $courierDemandLanguageKeys = @()
    if (Test-Path -LiteralPath '1.6\Languages') {
        Get-ChildItem -LiteralPath '1.6\Languages' -Recurse -Filter '*.xml' | Select-String -Pattern 'CourierDemand' | ForEach-Object {
            $courierDemandLanguageKeys += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
        }
    }
    if ($courierDemandLanguageKeys.Count) {
        $incidentInteractionSafetyIssues += "1.6\Languages :: unused courier demand translation keys remain"
        $incidentInteractionSafetyIssues += $courierDemandLanguageKeys
    }

    $incidentInteractionSafetyChecks++
    $slaveStockGeneratorPath = '1.6\Source\Features\Incidents\StockGenerator_Mugirl_Slaves.cs'
    if (Test-Path -LiteralPath $slaveStockGeneratorPath) {
        $slaveStockGeneratorText = Get-Content -LiteralPath $slaveStockGeneratorPath -Encoding utf8 -Raw
        $slaveStockGeneratorSafe = $slaveStockGeneratorText -match 'namespace\s+Mugirl' `
            -and $slaveStockGeneratorText -match 'thingDef\?\.category' `
            -and $slaveStockGeneratorText -match 'thingDef\.race\?\.Humanlike' `
            -and $slaveStockGeneratorText -match 'MugirlGameUtility\.ChildrenAllowedByCurrentDifficulty\(\)'
        if (-not $slaveStockGeneratorSafe) {
            $incidentInteractionSafetyIssues += "$slaveStockGeneratorPath :: slave stock generator must stay namespaced and ThingDef/difficulty null-safe"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$slaveStockGeneratorPath :: missing file for slave stock generator safety"
    }

    $incidentInteractionSafetyChecks++
    $mugirlPatchPath = '1.6\Patches\MugirlPatch.xml'
    if (Test-Path -LiteralPath $mugirlPatchPath) {
        $mugirlPatchText = Get-Content -LiteralPath $mugirlPatchPath -Encoding utf8 -Raw
        if ($mugirlPatchText -notmatch 'Class="Mugirl\.StockGenerator_Mugirl_Slaves"' -or $mugirlPatchText -match 'Class="StockGenerator_Mugirl_Slaves"') {
            $incidentInteractionSafetyIssues += "$mugirlPatchPath :: slave stock generator XML must use the full Mugirl namespace"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$mugirlPatchPath :: missing file for slave stock generator XML safety"
    }

    $incidentInteractionSafetyChecks++
    $rescueJoinUtilityPath = '1.6\Source\Features\Incidents\MugirlRescueJoinUtility.cs'
    if (Test-Path -LiteralPath $rescueJoinUtilityPath) {
        $rescueJoinUtilityText = Get-Content -LiteralPath $rescueJoinUtilityPath -Encoding utf8 -Raw
        $rescueJoinUtilitySafe = $rescueJoinUtilityText -match 'pawn\?\.mindState\s*==\s*null' `
            -and $rescueJoinUtilityText -match 'pawn\.Destroyed' `
            -and $rescueJoinUtilityText -match 'memories\s*!=\s*null'
        if (-not $rescueJoinUtilitySafe) {
            $incidentInteractionSafetyIssues += "$rescueJoinUtilityPath :: rescue join utility must guard destroyed pawns, missing mindState, and missing mood memories"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$rescueJoinUtilityPath :: missing file for rescue join utility safety"
    }

    $incidentInteractionSafetyChecks++
    $rescueJoinPartPath = '1.6\Source\Features\Incidents\QuestPart_MugirlRescueJoin.cs'
    if (Test-Path -LiteralPath $rescueJoinPartPath) {
        $rescueJoinPartText = Get-Content -LiteralPath $rescueJoinPartPath -Encoding utf8 -Raw
        $rescueJoinPartSafe = $rescueJoinPartText -match '!string\.IsNullOrEmpty\(inSignalRescued\)' `
            -and $rescueJoinPartText -match 'pawns\?\.Contains' `
            -and $rescueJoinPartText -match 'pawns\s*==\s*null'
        if (-not $rescueJoinPartSafe) {
            $incidentInteractionSafetyIssues += "$rescueJoinPartPath :: rescue join quest part must guard empty signals and null pawn lists"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$rescueJoinPartPath :: missing file for rescue join quest-part safety"
    }

    $incidentInteractionSafetyChecks++
    if (Test-Path -LiteralPath $rescueJoinPartPath) {
        $rescueJoinPartText = Get-Content -LiteralPath $rescueJoinPartPath -Encoding utf8 -Raw
        $rescueJoinReentrySafe = $rescueJoinPartText -match '(?s)private\s+void\s+TryJoinRescuedPawn\(Pawn\s+pawn\).*?MugirlWildSlaveUtility\.IsPlayerFaction\(pawn\.Faction\).*?return;.*?MugirlRescueJoinUtility\.PrepareRescueJoinPawn\(pawn\)'
        if (-not $rescueJoinReentrySafe) {
            $incidentInteractionSafetyIssues += "$rescueJoinPartPath :: rescue join quest part must skip already-player pawns before setting WillJoinColonyIfRescued"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$rescueJoinPartPath :: missing file for rescue join reentry safety"
    }

    $incidentInteractionSafetyChecks++
    $playerFactionEqualityFiles = @(
        '1.6\Source\Features\Incidents\MugirlRescueJoinUtility.cs',
        '1.6\Source\Features\Incidents\QuestPart_MugirlRescueJoin.cs',
        '1.6\Source\Features\Incidents\QuestPart_CourierRaid.cs',
        '1.6\Source\Features\Incidents\MugirlStoryState.cs'
    )
    foreach ($path in $playerFactionEqualityFiles) {
        if (-not (Test-Path -LiteralPath $path)) {
            $incidentInteractionSafetyIssues += "$path :: missing file for player faction helper safety"
            continue
        }

        Select-String -LiteralPath $path -Pattern '(Faction\.OfPlayer\s*[=!]=|[=!]=\s*Faction\.OfPlayer)' | ForEach-Object {
            $incidentInteractionSafetyIssues += "${path}:$($_.LineNumber) :: player faction equality must use MugirlWildSlaveUtility.IsPlayerFaction: $($_.Line.Trim())"
        }
    }

    $incidentInteractionSafetyChecks++
    $readableBookPath = '1.6\Source\Features\Incidents\CompReadableBook.cs'
    if (Test-Path -LiteralPath $readableBookPath) {
        $readableBookText = Get-Content -LiteralPath $readableBookPath -Encoding utf8 -Raw
        if ($readableBookText -notmatch 'CanReadNow' -or $readableBookText -notmatch 'TryStartReadJob') {
            $incidentInteractionSafetyIssues += "$readableBookPath :: readable book menu and job must share current target validation"
        }
        if ($readableBookText -match 'new\s+Dialog_ReadBook\([^\)]*,\s*diary\s*\)') {
            $incidentInteractionSafetyIssues += "$readableBookPath :: read book dialog should not receive an unused diary object"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$readableBookPath :: missing file for readable book safety"
    }

    $incidentInteractionSafetyChecks++
    $refugeePodCrashPath = '1.6\Source\Features\Incidents\QuestNode_Mugirl_RefugeePodCrash.cs'
    if (Test-Path -LiteralPath $refugeePodCrashPath) {
        $refugeePodCrashText = Get-Content -LiteralPath $refugeePodCrashPath -Encoding utf8 -Raw
        $refugeePodCrashSafe = $refugeePodCrashText -match 'MaxDownedGenerationAttempts' `
            -and $refugeePodCrashText -match 'GenerateDownedPawn' `
            -and $refugeePodCrashText -match 'PawnGenerator\.GeneratePawn\(request\)' `
            -and $refugeePodCrashText -match 'pawn\s*==\s*null' `
            -and $refugeePodCrashText -match 'MugirlGeneratedPawnUtility\.Discard' `
            -and $refugeePodCrashText -match 'MugirlGeneratedPawnUtility\.TryPassToWorld' `
            -and $refugeePodCrashText -match 'RefugeePodDownedGenerationFallback' `
            -and $refugeePodCrashText -match 'RefugeePodCrashLetterMissingPawn' `
            -and $refugeePodCrashText -match 'pawn\.ageTracker\s*!=\s*null'
        if (-not $refugeePodCrashSafe) {
            $incidentInteractionSafetyIssues += "$refugeePodCrashPath :: refugee pod crash pawn generation and letter path must guard null generation, discarded attempts, fallback and invalid letter pawns"
        }
        $refugeePodGenerationFailureSafe = $refugeePodCrashText -match 'protected\s+override\s+void\s+RunInt\(\)' `
            -and $refugeePodCrashText -match 'quest\s*==\s*null\s*\|\|\s*slate\s*==\s*null' `
            -and $refugeePodCrashText -match 'map\?\.Parent\s*==\s*null' `
            -and $refugeePodCrashText -match 'RefugeePodPawnGenerationFailed' `
            -and $refugeePodCrashText -match 'Mugirl\.RefugeePodCrash\.Log\.GenerationFailed' `
            -and $refugeePodCrashText -match 'Mugirl\.RefugeePodCrash\.Log\.MissingQuestContext' `
            -and $refugeePodCrashText -match 'Mugirl\.RefugeePodCrash\.Log\.MissingMap' `
            -and $refugeePodCrashText -match 'return\s+null' `
            -and $refugeePodCrashText -notmatch 'throw\s+new' `
            -and $refugeePodCrashText -notmatch 'MugirlLog\.Error'
        if (-not $refugeePodGenerationFailureSafe) {
            $incidentInteractionSafetyIssues += "$refugeePodCrashPath :: refugee pod crash generation failure must be handled by local RunInt and WarningOnce/null return, not QuestNode exception logging"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$refugeePodCrashPath :: missing file for refugee pod crash generation safety"
    }

    $incidentInteractionSafetyChecks++
    $wandererJoinPath = '1.6\Source\Features\Incidents\QuestNode_Root_Mugirl_WandererJoin_WalkIn.cs'
    if (Test-Path -LiteralPath $wandererJoinPath) {
        $wandererJoinText = Get-Content -LiteralPath $wandererJoinPath -Encoding utf8 -Raw
        $wandererJoinLetterSafe = $wandererJoinText -match 'Letter\s+letter\s*=\s*LetterMaker\.MakeLetter' `
            -and $wandererJoinText -match 'letter\s+as\s+ChoiceLetter_AcceptJoiner' `
            -and $wandererJoinText -match 'choiceLetter_AcceptJoiner\s*==\s*null' `
            -and $wandererJoinText -match 'MugirlLog\.WarningOnce'
        if (-not $wandererJoinLetterSafe) {
            $incidentInteractionSafetyIssues += "$wandererJoinPath :: wanderer join letter must type-check AcceptJoiner and degrade with a limited warning"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$wandererJoinPath :: missing file for wanderer join letter safety"
    }

    $incidentInteractionSafetyChecks++
    $wildManIncidentPath = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_WildManWandersIn.cs'
    if (Test-Path -LiteralPath $wildManIncidentPath) {
        $wildManIncidentText = Get-Content -LiteralPath $wildManIncidentPath -Encoding utf8 -Raw
        $wildManIncidentSafe = $wildManIncidentText -match 'parms\.target\s+is\s+Map\s+map' `
            -and $wildManIncidentText -match 'map\?\.reachability\s*==\s*null' `
            -and $wildManIncidentText -match 'pawn\s*==\s*null' `
            -and $wildManIncidentText -notmatch 'Map\s+map\s*=\s*\(Map\)parms\.target'
        if (-not $wildManIncidentSafe) {
            $incidentInteractionSafetyIssues += "$wildManIncidentPath :: wild slave incident must not directly cast incident target and must guard map reachability/pawn generation"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$wildManIncidentPath :: missing file for wild slave incident safety"
    }

    $incidentInteractionSafetyChecks++
    $gameUtilityPath = '1.6\Source\Core\MugirlGameUtility.cs'
    if (Test-Path -LiteralPath $gameUtilityPath) {
        $gameUtilityText = Get-Content -LiteralPath $gameUtilityPath -Encoding utf8 -Raw
        $gameUtilitySafe = $gameUtilityText -match 'TryAddWindow' `
            -and $gameUtilityText -match 'TryReceiveLetter' `
            -and $gameUtilityText -match 'CanReceiveLetter' `
            -and $gameUtilityText -match 'TrySignalForceNormalSpeedShort' `
            -and $gameUtilityText -match 'ChildrenAllowedByCurrentDifficulty' `
            -and $gameUtilityText -match 'IsCurrentMap' `
            -and $gameUtilityText -match 'TryMarkColonistsDirty' `
            -and $gameUtilityText -match 'TryGetQuestsListForReading' `
            -and $gameUtilityText -match 'TryGetFirstFactionOfDef' `
            -and $gameUtilityText -match 'TryResolvePlayerEventMap' `
            -and $gameUtilityText -match 'TryPassToWorld' `
            -and $gameUtilityText -match 'TryPassToWorldForDiscard' `
            -and $gameUtilityText -match 'TryRemoveWorldPawn' `
            -and $gameUtilityText -match 'Current\.Game\?\.letterStack\s*==\s*null' `
            -and $gameUtilityText -match 'Current\.Game\.tickManager\s*==\s*null' `
            -and $gameUtilityText -match 'Current\.Game\.history\?\.archive\s*==\s*null' `
            -and $gameUtilityText -match 'Current\.Game\?\.World\?\.factionManager' `
            -and $gameUtilityText -match 'Current\.Game\?\.AnyPlayerHomeMap' `
            -and $gameUtilityText -match 'Current\.Game\?\.Maps' `
            -and $gameUtilityText -match 'Current\.Game\?\.World\?\.worldPawns' `
            -and $gameUtilityText -match 'GameUtility\.WindowStackUnavailable' `
            -and $gameUtilityText -match 'GameUtility\.LetterStackUnavailable' `
            -and $gameUtilityText -match 'GameUtility\.WorldPawnsUnavailable'
        if (-not $gameUtilitySafe) {
            $incidentInteractionSafetyIssues += "$gameUtilityPath :: game UI/letter/quest/faction/map/world-pawn helper must guard unavailable game, UI, tick, archive, faction, map and world-pawn state"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$gameUtilityPath :: missing file for game utility safety"
    }

    $incidentInteractionSafetyChecks++
    $fusionInvestmentPath = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_FusionInvestment.cs'
    if (Test-Path -LiteralPath $fusionInvestmentPath) {
        $fusionInvestmentText = Get-Content -LiteralPath $fusionInvestmentPath -Encoding utf8 -Raw
        $fusionRewardSafe = $fusionInvestmentText -match 'Mugirl_DefOf\.Mugirl_WildMugirl' `
            -and $fusionInvestmentText -match 'MugirlWildSlaveUtility\.PlayerFaction' `
            -and $fusionInvestmentText -match 'dontGiveWeapon:\s*true' `
            -and $fusionInvestmentText -match 'request\.ForceNoIdeoGear\s*=\s*true' `
            -and $fusionInvestmentText -notmatch 'Mugirl_DefOf\.Mugirl_Slave\s*,' `
            -and $fusionInvestmentText -notmatch 'EnsureBikiniOnly|WearBikiniOnly'
        if (-not $fusionRewardSafe) {
            $incidentInteractionSafetyIssues += "$fusionInvestmentPath :: fusion rewards must use the bikini-only WildMugirl PawnKind without event-side apparel cleanup"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$fusionInvestmentPath :: missing file for fusion reward PawnKind safety"
    }

    $incidentInteractionSafetyChecks++
    $slavePawnKindsPath = '1.6\Defs\PawnKindDefs\PawnKinds_Slave.xml'
    if (Test-Path -LiteralPath $slavePawnKindsPath) {
        $slavePawnKinds = Get-XmlDocument $slavePawnKindsPath
        $wildMugirlKind = $slavePawnKinds.SelectSingleNode('/Defs/PawnKindDef[defName="Mugirl_WildMugirl"]')
        $requiredApparel = $null
        $apparelMoney = $null
        if ($wildMugirlKind) {
            $requiredApparel = $wildMugirlKind.SelectNodes('apparelRequired/li')
            $apparelMoney = $wildMugirlKind.SelectSingleNode('apparelMoney')
        }
        if ($wildMugirlKind -eq $null `
            -or $apparelMoney -eq $null `
            -or $apparelMoney.InnerText -ne '0~0' `
            -or $requiredApparel -eq $null `
            -or $requiredApparel.Count -ne 1 `
            -or $requiredApparel.Item(0).InnerText -ne 'Mugirl_Bikini') {
            $incidentInteractionSafetyIssues += "$slavePawnKindsPath :: Mugirl_WildMugirl must generate with no apparel budget and require only Mugirl_Bikini"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$slavePawnKindsPath :: missing file for WildMugirl apparel safety"
    }

    $incidentInteractionSafetyChecks++
    $migrationIncidentPath = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_Migration.cs'
    if (Test-Path -LiteralPath $migrationIncidentPath) {
        $migrationIncidentText = Get-Content -LiteralPath $migrationIncidentPath -Encoding utf8 -Raw
        $legacyBikiniMigrationSafe = $migrationIncidentText -match 'CurrentDataVersion\s*=\s*[1-9]\d*' `
            -and $migrationIncidentText -match 'Scribe_Values\.Look\(ref\s+dataVersion,\s*"dataVersion",\s*0\)' `
            -and $migrationIncidentText -match 'ReleaseLegacyBikiniLocks\(\)' `
            -and $migrationIncidentText -match 'UnlockLegacyBikini\(pawn\)' `
            -and $migrationIncidentText -match 'dataVersion\s*<\s*CurrentDataVersion' `
            -and $migrationIncidentText -notmatch 'EnsureBikiniOnly|WearBikiniOnly'
        if (-not $legacyBikiniMigrationSafe) {
            $incidentInteractionSafetyIssues += "$migrationIncidentPath :: active bikini cleanup must stay removed while the versioned one-time legacy unlock remains available for old saves"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$migrationIncidentPath :: missing file for legacy migration bikini-lock safety"
    }

    $incidentInteractionSafetyChecks++
    $mugirlEventUtilityPath = '1.6\Source\Features\Incidents\MugirlEventUtility.cs'
    if (Test-Path -LiteralPath $mugirlEventUtilityPath) {
        $mugirlEventUtilityText = Get-Content -LiteralPath $mugirlEventUtilityPath -Encoding utf8 -Raw
        $migrationApparelDropGuardSafe = $mugirlEventUtilityText -match 'HarmonyPatch\(typeof\(JobGiver_DropRandomGearOrApparel\),\s*"TryGiveJob"\)' `
            -and $mugirlEventUtilityText -match 'class\s+JobGiver_DropRandomGearOrApparel_MugirlMigration_Patch' `
            -and $mugirlEventUtilityText -match 'if\s*\(\s*!MugirlEventUtility\.IsMigrationPawn\(pawn\)\s*\)\s*\{\s*return\s+true\s*;\s*\}' `
            -and $mugirlEventUtilityText -match '__result\s*=\s*null\s*;\s*return\s+false\s*;'
        if (-not $migrationApparelDropGuardSafe) {
            $incidentInteractionSafetyIssues += "$mugirlEventUtilityPath :: migration pawns must suppress the vanilla wild-man random gear/apparel drop job"
        }
    }
    else {
        $incidentInteractionSafetyIssues += "$mugirlEventUtilityPath :: missing file for migration apparel-drop guard safety"
    }

    $incidentInteractionSafetyChecks++
    $directIncidentGlobalAccess = @()
    if (Test-Path -LiteralPath '1.6\Source\Features\Incidents') {
        Get-ChildItem -LiteralPath '1.6\Source\Features\Incidents' -Recurse -Filter '*.cs' | Select-String -Pattern 'Find\.(WindowStack|LetterStack|QuestManager|TickManager|WorldPawns|FactionManager|AnyPlayerHomeMap|Maps)|WindowStack\.Add' | ForEach-Object {
            $directIncidentGlobalAccess += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: incident global UI/game access must use MugirlGameUtility: $($_.Line.Trim())"
        }
    }
    if ($directIncidentGlobalAccess.Count) {
        $incidentInteractionSafetyIssues += $directIncidentGlobalAccess
    }

    if ($incidentInteractionSafetyIssues.Count) {
        $incidentInteractionSafetyIssues | Sort-Object
        Fail "Incident interaction safety scan failed: $($incidentInteractionSafetyIssues.Count) issue(s)"
    }

    Write-Step "Harmony bootstrap safety"
    $harmonyBootstrapSafetyChecks = 0
    $harmonyBootstrapSafetyIssues = @()

    $harmonyBootstrapSafetyChecks++
    $bootstrapPath = '1.6\Source\Core\MugirlBootstrap.cs'
    if (Test-Path -LiteralPath $bootstrapPath) {
        $bootstrapText = Get-Content -LiteralPath $bootstrapPath -Encoding utf8 -Raw
        $bootstrapSafe = $bootstrapText -match 'RegisterAttributePatches\(Harmony\)' `
            -and $bootstrapText -match 'GetHarmonyPatchTypes\(\)' `
            -and $bootstrapText -match 'HasHarmonyPatchAttribute\(type\)' `
            -and $bootstrapText -match 'TryPatchClass\(harmony,\s*patchTypes\[i\]\)' `
            -and $bootstrapText -match 'harmony\.CreateClassProcessor\(patchClass\)\.Patch\(\)' `
            -and $bootstrapText -match 'catch\s*\(\s*ReflectionTypeLoadException\s+ex\s*\)' `
            -and $bootstrapText -match 'types\s*=\s*ex\.Types\s*\?\?\s*new\s+Type\[0\]' `
            -and $bootstrapText -match 'catch\s*\(\s*Exception\s+ex\s*\)' `
            -and $bootstrapText -match 'Bootstrap\.PatchClassDiscoveryPartial' `
            -and $bootstrapText -match 'Bootstrap\.PatchClassDiscoveryFailed' `
            -and $bootstrapText -match 'Bootstrap\.PatchClassAttributeFailed\.' `
            -and $bootstrapText -match 'Bootstrap\.PatchClassFailed\.' `
            -and $bootstrapText -match 'Mugirl\.Bootstrap\.HarmonyPatchFailed' `
            -and $bootstrapText -match 'patchedClassNames\.Add\(patchClassName\)' `
            -and $bootstrapText -notmatch '\.PatchAll\s*\('
        if (-not $bootstrapSafe) {
            $harmonyBootstrapSafetyIssues += "$bootstrapPath :: Harmony bootstrap must register attribute patches per class, downgrade type discovery/attribute/patch failures and never use coarse PatchAll"
        }
    }
    else {
        $harmonyBootstrapSafetyIssues += "$bootstrapPath :: missing file for Harmony bootstrap safety"
    }

    $harmonyBootstrapSafetyChecks++
    $englishGameplayLanguagePath = '1.6\Languages\English\Keyed\Misc_Gameplay.xml'
    $chineseGameplayLanguagePath = '1.6\Languages\ChineseSimplified\Keyed\Misc_Gameplay.xml'
    if ((Test-Path -LiteralPath $englishGameplayLanguagePath) -and (Test-Path -LiteralPath $chineseGameplayLanguagePath)) {
        $englishGameplayLanguageText = Get-Content -LiteralPath $englishGameplayLanguagePath -Encoding utf8 -Raw
        $chineseGameplayLanguageText = Get-Content -LiteralPath $chineseGameplayLanguagePath -Encoding utf8 -Raw
        $harmonyBootstrapTextSafe = $englishGameplayLanguageText -match '<Mugirl\.Bootstrap\.HarmonyPatchFailed>Harmony patch skipped: \{0\}\. \{1\}</Mugirl\.Bootstrap\.HarmonyPatchFailed>' `
            -and $chineseGameplayLanguageText -match '<Mugirl\.Bootstrap\.HarmonyPatchFailed>Harmony 补丁已跳过：\{0\}。\{1\}</Mugirl\.Bootstrap\.HarmonyPatchFailed>'
        if (-not $harmonyBootstrapTextSafe) {
            $harmonyBootstrapSafetyIssues += "$englishGameplayLanguagePath / $chineseGameplayLanguagePath :: Harmony bootstrap failure warning text must keep English/Chinese key parity and placeholder parity"
        }
    }
    else {
        $harmonyBootstrapSafetyIssues += "$englishGameplayLanguagePath / $chineseGameplayLanguagePath :: missing language files for Harmony bootstrap safety"
    }

    $harmonyBootstrapSafetyChecks++
    $modEntryPath = '1.6\Source\Core\MugirlMod.cs'
    if (Test-Path -LiteralPath $modEntryPath) {
        $modEntryText = Get-Content -LiteralPath $modEntryPath -Encoding utf8 -Raw
        $modEntryStateSafe = $modEntryText -match 'private\s+static\s+MugirlSettings\s+settings' `
            -and $modEntryText -match 'internal\s+static\s+MugirlSettings\s+Settings\s*=>\s*settings' `
            -and $modEntryText -match 'MugirlBootstrap\.Initialize\(\)' `
            -and $modEntryText -notmatch 'using\s+HarmonyLib' `
            -and $modEntryText -notmatch 'public\s+static\s+Harmony\s+harmony|public\s+static\s+MugirlSettings\s+settings|MugirlBootstrap\.Harmony'
        if (-not $modEntryStateSafe) {
            $harmonyBootstrapSafetyIssues += "$modEntryPath :: mod entry point must not expose public mutable Harmony/settings fields and should use bootstrap-owned Harmony"
        }
    }
    else {
        $harmonyBootstrapSafetyIssues += "$modEntryPath :: missing file for mod entry state safety"
    }

    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | Select-String -Pattern 'MugirlMod\.(settings|harmony)' -CaseSensitive | ForEach-Object {
        $harmonyBootstrapSafetyIssues += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: callers must use MugirlMod.Settings and never access legacy public settings/harmony fields: $($_.Line.Trim())"
    }

    if ($harmonyBootstrapSafetyIssues.Count) {
        $harmonyBootstrapSafetyIssues | Sort-Object
        Fail "Harmony bootstrap safety scan failed: $($harmonyBootstrapSafetyIssues.Count) issue(s)"
    }

    Write-Step "Harmony boundary safety"
    $harmonyBoundarySafetyChecks = 0
    $harmonyBoundarySafetyIssues = @()

    $harmonyBoundarySafetyChecks++
    $nurturedSkillPath = '1.6\Source\Features\Milk\Harmony_MugirlNurturedSkillLearnCap.cs'
    if (Test-Path -LiteralPath $nurturedSkillPath) {
        $nurturedSkillText = Get-Content -LiteralPath $nurturedSkillPath -Encoding utf8 -Raw
        if ($nurturedSkillText -notmatch 'nurturedTrait\s*==\s*null' -or $nurturedSkillText -notmatch 'HasTrait\(nurturedTrait\)' -or $nurturedSkillText -notmatch 'xpSinceMidnightValue\s+is\s+float' -or $nurturedSkillText -notmatch 'baseCapValue\s+is\s+int') {
            $harmonyBoundarySafetyIssues += "$nurturedSkillPath :: global SkillRecord.Learn patch must guard missing nurtured trait def"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$nurturedSkillPath :: missing file for nurtured skill patch safety"
    }

    $harmonyBoundarySafetyChecks++
    $nurtureGrowthPath = '1.6\Source\Features\Milk\Harmony_MugirlNurtureGrowthPoints.cs'
    if (Test-Path -LiteralPath $nurtureGrowthPath) {
        $nurtureGrowthText = Get-Content -LiteralPath $nurtureGrowthPath -Encoding utf8 -Raw
        if ($nurtureGrowthText -notmatch 'afterglowDef\s*!=\s*null' -or $nurtureGrowthText -notmatch 'nurtureDef\s*==\s*null' -or $nurtureGrowthText -notmatch 'HediffSet\s+hediffSet') {
            $harmonyBoundarySafetyIssues += "$nurtureGrowthPath :: global growth-points patch must guard missing hediff defs and pawn hediff set"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$nurtureGrowthPath :: missing file for nurture growth patch safety"
    }

    $harmonyBoundarySafetyChecks++
    $ropingTickPath = '1.6\Source\Features\Roping\Harmony_RopingTick.cs'
    if (Test-Path -LiteralPath $ropingTickPath) {
        $ropingTickText = Get-Content -LiteralPath $ropingTickPath -Encoding utf8 -Raw
        if ($ropingTickText -notmatch 'pawnField\s*==\s*null' -or $ropingTickText -notmatch 'as\s+Pawn') {
            $harmonyBoundarySafetyIssues += "$ropingTickPath :: roping tick reflection helper must fall back when private pawn field is unavailable"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$ropingTickPath :: missing file for roping tick reflection safety"
    }

    $harmonyBoundarySafetyChecks++
    $ropingDrawPath = '1.6\Source\Features\Roping\Harmony_RopingDraw.cs'
    if (Test-Path -LiteralPath $ropingDrawPath) {
        $ropingDrawText = Get-Content -LiteralPath $ropingDrawPath -Encoding utf8 -Raw
        if ($ropingDrawText -notmatch '__instance\?\.IsRopedToSpot\s*!=\s*true' -or $ropingDrawText -notmatch 'Pawn\s+___pawn' -or $ropingDrawText -notmatch 'fieldRopeLineMat\s*==\s*null' -or $ropingDrawText -notmatch 'ropeLineMat\s*==\s*null' -or $ropingDrawText -notmatch 'as\s+Material') {
            $harmonyBoundarySafetyIssues += "$ropingDrawPath :: roping draw patch must use the unroped fast path, injected pawn field and cached rope material with reflection fallback"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$ropingDrawPath :: missing file for roping draw reflection safety"
    }

    $harmonyBoundarySafetyChecks++
    $milkingAnimationPaths = @(
        '1.6\Source\Features\Milk\MugirlMilkingAnimation.cs',
        '1.6\Source\Features\Milk\MugirlMilkingAnimation.Interaction.cs',
        '1.6\Source\Features\Milk\MugirlMilkingAnimation.State.cs',
        '1.6\Source\Features\Milk\MugirlMilkingAnimationWorker.cs',
        '1.6\Source\Features\Milk\Harmony_MugirlMilkingAnimation.cs',
        '1.6\Defs\AnimationDefs\Mugirl_Milking.xml'
    )
    $missingMilkingAnimationPaths = @($milkingAnimationPaths | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingMilkingAnimationPaths.Count -eq 0) {
        $milkingAnimationText = ($milkingAnimationPaths | ForEach-Object { Get-Content -LiteralPath $_ -Encoding utf8 -Raw }) -join "`n"
        $milkingAnimationSafe = $milkingAnimationText -match 'class\s+MugirlMilkingAnimationWorker\s*:\s*BaseAnimationWorker' `
            -and $milkingAnimationText -match 'renderer\.CurAnimation\s*==\s*null\s*\|\|\s*IsMugirlMilkingAnimation' `
            -and $milkingAnimationText -match 'renderer\.SetAnimation\(desired\)' `
            -and $milkingAnimationText -match 'IsMugirlMilkingAnimation\(renderer\.CurAnimation\)' `
            -and $milkingAnimationText -match '<key>Root</key>' `
            -and $milkingAnimationText -match '<key>Head</key>' `
            -and $milkingAnimationText -notmatch 'Harmony_MugirlMilkingAnimation_NodeTransform' `
            -and $milkingAnimationText -notmatch 'Harmony_MugirlMilkingAnimation_DisableCachedPawnRender' `
            -and $milkingAnimationText -match 'MugirlTickUtility\.TryGetCurrentGameTick' `
            -and $milkingAnimationText -match 'MugirlTickUtility\.CurrentGameTickOrFallback' `
            -and $milkingAnimationText -notmatch 'Find\.TickManager' `
            -and $milkingAnimationText -match 'FallbackHeadRegionFraction\s*=\s*0\.3f' `
            -and $milkingAnimationText -match 'PawnRenderNodeTagDefOf\.Head' `
            -and $milkingAnimationText -match 'StartFeeding' `
            -and $milkingAnimationText -match 'StartDrinking' `
            -and $milkingAnimationText -match 'source\.Rotation\s*=\s*Rot4\.South' `
            -and $milkingAnimationText -match 'recipient\.Rotation\s*=\s*FeedingRecipientFacing' `
            -and $milkingAnimationText -match 'source\.rotationTracker\?\.FaceTarget\(recipient\)' `
            -and $milkingAnimationText -match 'states\.Clear\(\)' `
            -and $milkingAnimationText -match 'tmpStateKeysToRemove' `
            -and $milkingAnimationText -match 'state\.pawn\s*!=\s*pawn\s*\|\|\s*state\.role\s*!=\s*role' `
            -and $milkingAnimationText -match 'state\.pawn\s*!=\s*pawn' `
            -and $milkingAnimationText -match 'state\.pawn\s*==\s*pawn\s*&&\s*state\.partner\s*==\s*expectedPartner' `
            -and $milkingAnimationText -match 'StateNeedsPartner\(state\)\s*&&\s*!Valid\(state\.partner\)' `
            -and $milkingAnimationText -match 'RemoveIfMatches\(state\.partner,\s*state\.pawn\)' `
            -and $milkingAnimationText -match 'tmpStateKeysToRemove\.Clear\(\)'
        if (-not $milkingAnimationSafe) {
            $harmonyBoundarySafetyIssues += "MugirlMilkingAnimation files :: native animation must yield to foreign CurAnimation values and preserve centralized tick access plus stale/cross-pawn/partner cleanup"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "MugirlMilkingAnimation split files :: missing file(s): $($missingMilkingAnimationPaths -join ', ')"
    }

    $harmonyBoundarySafetyChecks++
    $ghoulRenderingPath = '1.6\Source\Features\Misc\Harmony_GhoulRenderingRefresh.cs'
    if (Test-Path -LiteralPath $ghoulRenderingPath) {
        $ghoulRenderingText = Get-Content -LiteralPath $ghoulRenderingPath -Encoding utf8 -Raw
        $ghoulRenderingSafe = $ghoulRenderingText -match 'ClearPendingRefreshes' `
            -and $ghoulRenderingText -match 'MugirlGameUtility\.IsPlaying\(\)' `
            -and $ghoulRenderingText -match 'MugirlTickUtility\.TryGetCurrentGameTick' `
            -and $ghoulRenderingText -notmatch 'Find\.TickManager' `
            -and $ghoulRenderingText -match 'pendingRefreshes\.Clear\(\)' `
            -and $ghoulRenderingText -match 'tmpPawnsToProcess\.Clear\(\)' `
            -and $ghoulRenderingText -match 'pawn\.Destroyed'
        if (-not $ghoulRenderingSafe) {
            $harmonyBoundarySafetyIssues += "$ghoulRenderingPath :: ghoul rendering refresh must clear static pawn caches and use centralized tick access"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$ghoulRenderingPath :: missing file for ghoul rendering refresh safety"
    }

    $harmonyBoundarySafetyChecks++
    $wildManPath = '1.6\Source\Features\Incidents\Harmony_IsWildMan_WildManUtility.cs'
    if (Test-Path -LiteralPath $wildManPath) {
        $wildManText = Get-Content -LiteralPath $wildManPath -Encoding utf8 -Raw
        if ($wildManText -notmatch 'IsNonPlayerWildMugirl\(p\)' `
            -or $wildManText -notmatch 'IsPlayerFaction\(faction\)' `
            -or $wildManText -notmatch 'CleanupAfterJoiningPlayer\(pawn\)' `
            -or $wildManText -match 'ChangeKind\(') {
            $harmonyBoundarySafetyIssues += "$wildManPath :: wild-slave global patches must use player-faction behavior gating and must not convert PawnKind"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$wildManPath :: missing file for wild slave patch safety"
    }

    $harmonyBoundarySafetyChecks++
    $designatorTamePath = '1.6\Source\Features\Incidents\Harmony_DesignatorTame.cs'
    if (Test-Path -LiteralPath $designatorTamePath) {
        $designatorTameText = Get-Content -LiteralPath $designatorTamePath -Encoding utf8 -Raw
        if ($designatorTameText -notmatch 'IsNonPlayerWildMugirl\(pawn\)' -or $designatorTameText -match 'Faction\.OfPlayer') {
            $harmonyBoundarySafetyIssues += "$designatorTamePath :: tame designator patches must share wild-slave player-faction validation"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$designatorTamePath :: missing file for tame designator safety"
    }

    $harmonyBoundarySafetyChecks++
    $wildSlaveUtilityPath = '1.6\Source\Features\Incidents\MugirlWildSlaveUtility.cs'
    $wildThinkNodePath = '1.6\Source\Features\Incidents\ThinkNode_ConditionalNonPlayerWildMugirl.cs'
    $mugirlThinkTreePath = '1.6\Defs\ThinkTreeDefs\Mugirl_ThinkTreeDefs.xml'
    $mugirlStoryStatePath = '1.6\Source\Features\Incidents\MugirlStoryState.cs'
    $fusionInvestmentPath = '1.6\Source\Features\Incidents\IncidentWorker_Mugirl_FusionInvestment.cs'
    if ((Test-Path -LiteralPath $wildSlaveUtilityPath) `
        -and (Test-Path -LiteralPath $wildThinkNodePath) `
        -and (Test-Path -LiteralPath $mugirlThinkTreePath) `
        -and (Test-Path -LiteralPath $mugirlStoryStatePath) `
        -and (Test-Path -LiteralPath $fusionInvestmentPath)) {
        $wildSlaveUtilityText = Get-Content -LiteralPath $wildSlaveUtilityPath -Encoding utf8 -Raw
        $wildThinkNodeText = Get-Content -LiteralPath $wildThinkNodePath -Encoding utf8 -Raw
        $mugirlThinkTreeText = Get-Content -LiteralPath $mugirlThinkTreePath -Encoding utf8 -Raw
        $mugirlStoryStateText = Get-Content -LiteralPath $mugirlStoryStatePath -Encoding utf8 -Raw
        $fusionInvestmentText = Get-Content -LiteralPath $fusionInvestmentPath -Encoding utf8 -Raw
        $wildBehaviorGateCount = ([regex]::Matches($mugirlThinkTreeText, 'Class="Mugirl\.ThinkNode_ConditionalNonPlayerWildMugirl"')).Count
        if ($wildSlaveUtilityText -notmatch 'return IsWildMugirl\(pawn\)\s*&&\s*!IsPlayerFaction\(pawn\.Faction\)' `
            -or $wildSlaveUtilityText -notmatch 'pawn\.Faction\?\.def\?\.basicMemberKind' `
            -or $wildSlaveUtilityText -notmatch 'pawn\.ChangeKind\(targetKind\)' `
            -or $wildSlaveUtilityText -match 'ChangeKind\(Mugirl_DefOf\.Mugirl_PreEscapeWildSlave\)' `
            -or $wildSlaveUtilityText -match 'PawnGenerator_GeneratePawn_MugirlWildSlaveBirth_Patch' `
            -or $wildThinkNodeText -notmatch 'IsNonPlayerWildMugirl\(pawn\)' `
            -or $wildBehaviorGateCount -ne 2 `
            -or $mugirlThinkTreeText -match '<pawnKind>Mugirl_(EscapeWildSlave|WildMugirl)</pawnKind>' `
            -or $mugirlStoryStateText -notmatch 'LoadedGame\(\)[\s\S]*NormalizeLoadedPlayerPawnKinds\(\)' `
            -or $fusionInvestmentText -notmatch 'PawnGenerator\.GeneratePawn\(request\)[\s\S]*CleanupAfterJoiningPlayer\(pawn\)') {
            $harmonyBoundarySafetyIssues += "$wildSlaveUtilityPath :: wild Mugirl behavior must be faction-gated, while joined and legacy player pawns normalize only to the player faction basic member kind"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$wildSlaveUtilityPath :: missing wild Mugirl behavior-gate, load migration, reward, or think-tree source file"
    }

    $harmonyBoundarySafetyChecks++
    $newbornVisualPath = '1.6\Source\Features\Newborn\Harmony_PawnGenerator_NewbornVisuals.cs'
    if (Test-Path -LiteralPath $newbornVisualPath) {
        $newbornVisualText = Get-Content -LiteralPath $newbornVisualPath -Encoding utf8 -Raw
        if ($newbornVisualText -notmatch 'MugirlIdentity\.HasMugirlBody\(__result\)' -or $newbornVisualText -notmatch 'ageTracker\?' -or $newbornVisualText -notmatch 'story\s*!=\s*null') {
            $harmonyBoundarySafetyIssues += "$newbornVisualPath :: newborn visual patch must guard generated pawn race, age and story trackers"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$newbornVisualPath :: missing file for newborn visual patch safety"
    }

    $harmonyBoundarySafetyChecks++
    $xenotypeFixPath = '1.6\Source\Features\Genes\Mugirl_XenotypeFix_GameComp.cs'
    if (Test-Path -LiteralPath $xenotypeFixPath) {
        $xenotypeFixText = Get-Content -LiteralPath $xenotypeFixPath -Encoding utf8 -Raw
        $xenotypeFixSafe = $xenotypeFixText -match 'internal\s+static\s+class\s+MugirlXenotypeService' `
            -and $xenotypeFixText -match 'internal\s+static\s+void\s+ForceFemaleMugirlXenotypeIfNeeded' `
            -and $xenotypeFixText -match 'internal\s+static\s+void\s+FixLoadedPawnIfSafe' `
            -and $xenotypeFixText -match 'private\s+static\s+readonly\s+List<Pawn>\s+tmpParents\s*=' `
            -and $xenotypeFixText -match 'List<Pawn>\s+parents\s*=\s*tmpParents' `
            -and $xenotypeFixText -match 'ref\s+parents' `
            -and $xenotypeFixText -match 'parents\?\.Clear\(\)' `
            -and $xenotypeFixText -match 'tmpParents\.Clear\(\)' `
            -and $xenotypeFixText -match 'targetXenotype\?\.genes\s*==\s*null' `
            -and $xenotypeFixText -match 'geneDef\s*==\s*null' `
            -and $xenotypeFixText -match 'ModsConfig\.BiotechActive' `
            -and $xenotypeFixText -notmatch 'public\s+static\s+class\s+MugirlXenotypeService'
        if (-not $xenotypeFixSafe) {
            $harmonyBoundarySafetyIssues += "$xenotypeFixPath :: xenotype birth/load helper must stay internal, keep parent scratch data readonly, clear it after use and guard missing gene lists"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$xenotypeFixPath :: missing file for xenotype birth/load safety"
    }

    $harmonyBoundarySafetyChecks++
    $thoughtHediffPath = '1.6\Source\Features\Milk\HediffCompProperties_WhileHavingThoughts.cs'
    if (Test-Path -LiteralPath $thoughtHediffPath) {
        $thoughtHediffText = Get-Content -LiteralPath $thoughtHediffPath -Encoding utf8 -Raw
        if ($thoughtHediffText -match 'HediffDef\.Named|DefDatabase<HediffDef>\.GetNamed\([^,]+,\s*false\)' -or
            $thoughtHediffText -notmatch 'GetNamedSilentFail\((?:Props|thoughtProps)\.hediffReduction\)' -or
            $thoughtHediffText -notmatch 'props\s+as\s+HediffCompProperties_WhileHavingThoughts' -or
            $thoughtHediffText -notmatch 'base\.CompExposeData\(\)' -or
            $thoughtHediffText -notmatch 'Scribe_Values\.Look<int>\(ref this\.checkingCounter' -or
            $thoughtHediffText -notmatch 'memories\s*==\s*null' -or
            $thoughtHediffText -notmatch 'Thought_Memory\s+memory' -or
            $thoughtHediffText -notmatch 'RemoveSelf\(\)') {
            $harmonyBoundarySafetyIssues += "$thoughtHediffPath :: thought hediff comp must avoid hard hediff lookup, use safe props/base expose, persist interval state and guard missing mood memory tracker"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$thoughtHediffPath :: missing file for thought hediff safety"
    }

    $harmonyBoundarySafetyChecks++
    $mountedMeleePath = '1.6\Source\Features\Mounting\MountedPawnMeleeSupport.cs'
    if (Test-Path -LiteralPath $mountedMeleePath) {
        $mountedMeleeText = Get-Content -LiteralPath $mountedMeleePath -Encoding utf8 -Raw
        if ($mountedMeleeText -notmatch '原版私有近战辅助方法只是兼容调用' -or
            $mountedMeleeText -notmatch '私有音效辅助方法缺失时' -or
            $mountedMeleeText -notmatch '私有冷却字段缺失时') {
            $harmonyBoundarySafetyIssues += "$mountedMeleePath :: mounted melee reflection helpers must fall back without throwing in tick"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$mountedMeleePath :: missing file for mounted melee reflection safety"
    }

    $harmonyBoundarySafetyChecks++
    $meleeAnimationCompatPath = '1.6\Source\Compatibility\MeleeAnimation\MeleeAnimationCompat.cs'
    if (Test-Path -LiteralPath $meleeAnimationCompatPath) {
        $meleeAnimationCompatText = Get-Content -LiteralPath $meleeAnimationCompatPath -Encoding utf8 -Raw
        if ($meleeAnimationCompatText -notmatch 'AnimateAtIdleField\?\.FieldType\s*==\s*typeof\(bool\)' -or
            $meleeAnimationCompatText -notmatch 'FieldRefAccess<object,\s*bool>' -or
            $meleeAnimationCompatText -notmatch 'ref\s+MeleeAnimationCompat\.IdleAnimationState\s+__state') {
            $harmonyBoundarySafetyIssues += "$meleeAnimationCompatPath :: melee animation compat must validate the bool field before binding and restore shared state by reference"
        }
    }
    else {
        $harmonyBoundarySafetyIssues += "$meleeAnimationCompatPath :: missing file for melee animation compatibility safety"
    }

    $harmonyBoundarySafetyChecks++
    $directReflectionCasts = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        Select-String -LiteralPath $_.FullName -Pattern '\((float|int|bool|Pawn)\).*GetValue' | ForEach-Object {
            $directReflectionCasts += "$(Get-RelativePath $_.Path):$($_.LineNumber) :: $($_.Line.Trim())"
        }
    }
    if ($directReflectionCasts.Count) {
        $harmonyBoundarySafetyIssues += "Direct reflection value cast(s) must be replaced with type-checked fallbacks:"
        $harmonyBoundarySafetyIssues += $directReflectionCasts
    }

    if ($harmonyBoundarySafetyIssues.Count) {
        $harmonyBoundarySafetyIssues | Sort-Object
        Fail "Harmony boundary safety scan failed: $($harmonyBoundarySafetyIssues.Count) issue(s)"
    }

    Write-Step "Compatibility boundary safety"
    $compatibilityBoundarySafetyChecks = 0
    $compatibilityBoundarySafetyIssues = @()

    $compatibilityBoundarySafetyChecks++
    $directThirdPartyReflection = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/').Replace('/', '\')
        if ($relative -like '1.6\Source\Compatibility\*') {
            return
        }

        Select-String -LiteralPath $_.FullName -Pattern 'AccessTools\.TypeByName' | ForEach-Object {
            $directThirdPartyReflection += "${relative}:$($_.LineNumber) :: $($_.Line.Trim())"
        }
    }
    if ($directThirdPartyReflection.Count) {
        $compatibilityBoundarySafetyIssues += "Third-party TypeByName reflection must stay under 1.6\Source\Compatibility:"
        $compatibilityBoundarySafetyIssues += $directThirdPartyReflection
    }

    $compatibilityBoundarySafetyChecks++
    $patchRegistryPath = '1.6\Source\Core\MugirlPatchRegistry.cs'
    if (Test-Path -LiteralPath $patchRegistryPath) {
        $patchRegistryText = Get-Content -LiteralPath $patchRegistryPath -Encoding utf8 -Raw
        if ($patchRegistryText -match 'AlienRaceCompatibility|AlienPawnRenderNode_Swaddle|TryGetSwaddleGraphicForTarget|AlienRaceSwaddleGraphicFor|PatchAlienRaceSwaddleGraphicFor' -or
            $patchRegistryText -match 'AccessTools\.TypeByName') {
            $compatibilityBoundarySafetyIssues += "$patchRegistryPath :: HAR swaddle rendering must be configured through race XML, not a manual reflection patch"
        }
    }
    else {
        $compatibilityBoundarySafetyIssues += "$patchRegistryPath :: missing patch registry for Compatibility boundary safety"
    }

    $compatibilityBoundarySafetyChecks++
    $raceXmlPath = '1.6\Defs\ThingDefs_Races\Mugirl_Race.xml'
    if (Test-Path -LiteralPath $raceXmlPath) {
        $raceXmlText = Get-Content -LiteralPath $raceXmlPath -Encoding utf8 -Raw
        if ($raceXmlText -notmatch '<graphicPaths>[\s\S]*<swaddle>\s*Mugirl/Bodies/SwaddledBaby/Swaddled_Child\s*</swaddle>[\s\S]*</graphicPaths>') {
            $compatibilityBoundarySafetyIssues += "$raceXmlPath :: HAR swaddle path must be configured in graphicPaths.swaddle"
        }
    }
    else {
        $compatibilityBoundarySafetyIssues += "$raceXmlPath :: missing race XML for HAR swaddle configuration"
    }

    $compatibilityBoundarySafetyChecks++
    $removedSwaddlePatchPaths = @(
        '1.6\Source\Compatibility\AlienRace\AlienRaceCompatibility.cs',
        '1.6\Source\Compatibility\AlienRace\Harmony_AlienPawnRenderNode_Swaddle_GraphicFor.cs'
    )
    foreach ($removedSwaddlePatchPath in $removedSwaddlePatchPaths) {
        if (Test-Path -LiteralPath $removedSwaddlePatchPath) {
            $compatibilityBoundarySafetyIssues += "$removedSwaddlePatchPath :: HAR swaddle reflection patch should stay removed; use graphicPaths.swaddle"
        }
    }

    $compatibilityBoundarySafetyChecks++
    $externalBreastPath = '1.6\Source\Compatibility\OtherMods\ExternalBreastHediffDefs.cs'
    $externalBreastHediffLiteralPattern = '"(?:HugeBreasts|BionicBreasts|SlimeBreasts|GR_MuffaloMammaries|Breasts|HydraulicBreasts|SmallBreasts|LargeBreasts|ArchotechBreasts|FlatBreasts)"'
    if (Test-Path -LiteralPath $externalBreastPath) {
        $externalBreastText = Get-Content -LiteralPath $externalBreastPath -Encoding utf8 -Raw
        if ($externalBreastText -notmatch 'StaticCacheLifecycle: 进程级外部乳房 HediffDef 缓存' -or
            $externalBreastText -match 'Mugirl_Lactation') {
            $compatibilityBoundarySafetyIssues += "$externalBreastPath :: external breast cache must document lifecycle and must not carry internal Mugirl hediff defs"
        }
    }
    else {
        $compatibilityBoundarySafetyIssues += "$externalBreastPath :: missing external breast hediff compatibility cache"
    }

    $compatibilityBoundarySafetyChecks++
    $externalBreastLiteralHits = @()
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $relative = (Get-RelativePath $_.FullName).TrimStart('.', '\', '/').Replace('/', '\')
        if ($relative -eq $externalBreastPath) {
            return
        }

        Select-String -LiteralPath $_.FullName -Pattern $externalBreastHediffLiteralPattern | ForEach-Object {
            $externalBreastLiteralHits += "${relative}:$($_.LineNumber) :: $($_.Line.Trim())"
        }
    }
    if ($externalBreastLiteralHits.Count) {
        $compatibilityBoundarySafetyIssues += "External breast hediff defName literals must stay in ExternalBreastHediffDefs:"
        $compatibilityBoundarySafetyIssues += $externalBreastLiteralHits
    }

    $compatibilityBoundarySafetyChecks++
    $optionalDefsPath = '1.6\Source\Core\MugirlOptionalDefs.cs'
    if (Test-Path -LiteralPath $optionalDefsPath) {
        $optionalDefsText = Get-Content -LiteralPath $optionalDefsPath -Encoding utf8 -Raw
        if ($optionalDefsText -match 'internal\s+static\s+class\s+Hediffs') {
            $compatibilityBoundarySafetyIssues += "$optionalDefsPath :: optional external hediff defs must not drift back into MugirlOptionalDefs.Hediffs"
        }
    }
    else {
        $compatibilityBoundarySafetyIssues += "$optionalDefsPath :: missing optional defs file for Compatibility boundary safety"
    }

    $compatibilityBoundarySafetyChecks++
    $requiredDefsPath = '1.6\Source\Core\MugirlRequiredDefs.cs'
    if (Test-Path -LiteralPath $requiredDefsPath) {
        $requiredDefsText = Get-Content -LiteralPath $requiredDefsPath -Encoding utf8 -Raw
        if ($requiredDefsText -notmatch 'MugirlLactation\s*=\s*Required<HediffDef>\("Mugirl_Lactation"\)') {
            $compatibilityBoundarySafetyIssues += "$requiredDefsPath :: internal Mugirl lactation hediff must stay in required Def cache, not in external compatibility caches"
        }
    }
    else {
        $compatibilityBoundarySafetyIssues += "$requiredDefsPath :: missing required defs file for internal lactation cache safety"
    }

    if ($compatibilityBoundarySafetyIssues.Count) {
        $compatibilityBoundarySafetyIssues | Sort-Object
        Fail "Compatibility boundary safety scan failed: $($compatibilityBoundarySafetyIssues.Count) issue(s)"
    }

    Write-Step "Ability job safety"
    $abilityJobSafetyChecks = 0
    $abilityJobSafetyIssues = @()
    $abilityJobSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Genes\JobDriver_CastCharge.cs'
            Pattern = 'return\s+pawn\.Reserve\(job\.targetA,\s*job\)'
            Description = 'charge job reserves TargetA without validating current pawn target'
        },
        @{
            Path = '1.6\Source\Features\Genes\JobDriver_CastCharge.cs'
            Pattern = 'Pawn\s+target\s*=\s*job\.targetA\.Thing\s+as\s+Pawn'
            Description = 'charge job captures stale target at toil construction time'
        },
        @{
            Path = '1.6\Source\Features\Misc\Harmony_MugirlGrazeChain.cs'
            Pattern = '9999f'
            Description = 'graze chain searches the whole map for the next plant'
        }
    )

    foreach ($rule in $abilityJobSafetyRules) {
        $abilityJobSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $abilityJobSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $abilityJobSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $abilityJobSafetyChecks++
    $chargeDriverPath = '1.6\Source\Features\Genes\JobDriver_CastCharge.cs'
    if (Test-Path -LiteralPath $chargeDriverPath) {
        $chargeDriverText = Get-Content -LiteralPath $chargeDriverPath -Encoding utf8 -Raw
        if ($chargeDriverText -notmatch 'CanChargeTarget' -or $chargeDriverText -notmatch 'AddFinishAction' -or $chargeDriverText -notmatch 'EnsureChargeHediff' -or $chargeDriverText -notmatch 'target\s*!=\s*pawn') {
            $abilityJobSafetyIssues += "$chargeDriverPath :: charge job must validate current target and clean its speed hediff on every finish path"
        }
    }
    else {
        $abilityJobSafetyIssues += "$chargeDriverPath :: missing file for charge job safety"
    }

    $abilityJobSafetyChecks++
    if (Test-Path -LiteralPath $chargeDriverPath) {
        $chargeDriverText = Get-Content -LiteralPath $chargeDriverPath -Encoding utf8 -Raw
        if ($chargeDriverText -notmatch 'MugirlSelectionUtility\.ReselectIfSelectedInPlaying\(pawn,\s*playSound:\s*false,\s*forceDesignatorDeselect:\s*false\)' -or
            $chargeDriverText -match 'Find\.Selector') {
            $abilityJobSafetyIssues += "$chargeDriverPath :: charge jump reselect must use guarded MugirlSelectionUtility instead of direct Find.Selector access"
        }
    }
    else {
        $abilityJobSafetyIssues += "$chargeDriverPath :: missing file for charge selection safety"
    }

    $abilityJobSafetyChecks++
    $forceJobPath = '1.6\Source\Features\Genes\CompProperties_AbilityEffect_ForceJob.cs'
    if (Test-Path -LiteralPath $forceJobPath) {
        $forceJobText = Get-Content -LiteralPath $forceJobPath -Encoding utf8 -Raw
        if ($forceJobText -notmatch 'props\s+as\s+CompProperties_AbilityEffect_ForceJob' -or
            $forceJobText -notmatch 'forceProps\?\.jobDef\s*==\s*null' -or
            $forceJobText -notmatch 'CanForceJobOn' -or
            $forceJobText -notmatch 'StartForcedJob\(p,\s*caster,\s*forceProps\)' -or
            $forceJobText -notmatch 'target\.jobs\s*!=\s*null' -or
            $forceJobText -notmatch 'DistanceToSquared' -or
            $forceJobText -notmatch 'mindState\s*!=\s*null') {
            $abilityJobSafetyIssues += "$forceJobPath :: forced ability job must safe-cast props, share target validation and avoid unbounded distance checks"
        }
    }
    else {
        $abilityJobSafetyIssues += "$forceJobPath :: missing file for force-job ability safety"
    }

    $abilityJobSafetyChecks++
    $checkJobHediffPath = '1.6\Source\Features\Genes\HediffCompProperties_CheckJobOrRemove.cs'
    if (Test-Path -LiteralPath $checkJobHediffPath) {
        $checkJobHediffText = Get-Content -LiteralPath $checkJobHediffPath -Encoding utf8 -Raw
        $checkJobHediffSafe = $checkJobHediffText -match 'props\s+as\s+HediffCompProperties_CheckJobOrRemove' `
            -and $checkJobHediffText -match 'parent\?\.pawn' `
            -and $checkJobHediffText -match 'checkProps\?\.requiredJob\s*==\s*null' `
            -and $checkJobHediffText -match 'pawn\.health\?\.RemoveHediff\(parent\)' `
            -and $checkJobHediffText -notmatch '\(HediffCompProperties_CheckJobOrRemove\)this\.props'
        if (-not $checkJobHediffSafe) {
            $abilityJobSafetyIssues += "$checkJobHediffPath :: check-job hediff must safe-cast props and remove itself through guarded pawn health"
        }
    }
    else {
        $abilityJobSafetyIssues += "$checkJobHediffPath :: missing file for check-job hediff safety"
    }

    $abilityJobSafetyChecks++
    $grazeChainPath = '1.6\Source\Features\Misc\Harmony_MugirlGrazeChain.cs'
    if (Test-Path -LiteralPath $grazeChainPath) {
        $grazeChainText = Get-Content -LiteralPath $grazeChainPath -Encoding utf8 -Raw
        if ($grazeChainText -notmatch 'MaxGrazeSearchRadius' -or $grazeChainText -notmatch 'job\.playerForced' -or $grazeChainText -notmatch 'pawn\.Drafted' -or $grazeChainText -notmatch 'jobQueue\.Count') {
            $abilityJobSafetyIssues += "$grazeChainPath :: graze chain must stay bounded and avoid overriding player-forced or queued work"
        }
    }
    else {
        $abilityJobSafetyIssues += "$grazeChainPath :: missing file for graze-chain safety"
    }

    if ($abilityJobSafetyIssues.Count) {
        $abilityJobSafetyIssues | Sort-Object
        Fail "Ability job safety scan failed: $($abilityJobSafetyIssues.Count) issue(s)"
    }

    Write-Step "Misc job safety"
    $miscJobSafetyChecks = 0
    $miscJobSafetyIssues = @()
    $miscJobSafetyRules = @(
        @{
            Path = '1.6\Source\Features\Misc\JobDriver_Nuzzle.cs'
            Pattern = '\(\s*Pawn\s*\).*CurJob\.targetA'
            Description = 'nuzzle job directly casts current job target'
        }
    )

    foreach ($rule in $miscJobSafetyRules) {
        $miscJobSafetyChecks++
        if (-not (Test-Path -LiteralPath $rule.Path)) {
            $miscJobSafetyIssues += "$($rule.Path) :: missing file for $($rule.Description)"
            continue
        }

        Select-String -LiteralPath $rule.Path -Pattern $rule.Pattern | ForEach-Object {
            $miscJobSafetyIssues += "$($rule.Path):$($_.LineNumber) :: $($rule.Description): $($_.Line.Trim())"
        }
    }

    $miscJobSafetyChecks++
    $nuzzleDriverPath = '1.6\Source\Features\Misc\JobDriver_Nuzzle.cs'
    if (Test-Path -LiteralPath $nuzzleDriverPath) {
        $nuzzleDriverText = Get-Content -LiteralPath $nuzzleDriverPath -Encoding utf8 -Raw
        if ($nuzzleDriverText -notmatch 'CanNuzzle' -or $nuzzleDriverText -notmatch 'pawn\.Reserve') {
            $miscJobSafetyIssues += "$nuzzleDriverPath :: nuzzle job must reserve and revalidate its recipient"
        }
    }
    else {
        $miscJobSafetyIssues += "$nuzzleDriverPath :: missing file for nuzzle job safety"
    }

    $miscJobSafetyChecks++
    $nuzzleGiverPath = '1.6\Source\Features\Misc\JobGiver_Nuzzle.cs'
    if (Test-Path -LiteralPath $nuzzleGiverPath) {
        $nuzzleGiverText = Get-Content -LiteralPath $nuzzleGiverPath -Encoding utf8 -Raw
        if ($nuzzleGiverText -notmatch 'CanReserve\(\s*candidate\s*\)') {
            $miscJobSafetyIssues += "$nuzzleGiverPath :: nuzzle target selection must skip unreservable candidates"
        }
    }
    else {
        $miscJobSafetyIssues += "$nuzzleGiverPath :: missing file for nuzzle target safety"
    }

    if ($miscJobSafetyIssues.Count) {
        $miscJobSafetyIssues | Sort-Object
        Fail "Misc job safety scan failed: $($miscJobSafetyIssues.Count) issue(s)"
    }

    Write-Step "Apparel generation safety"
    $apparelGenerationSafetyChecks = 0
    $apparelGenerationSafetyIssues = @()
    $apparelTagUtilityPath = '1.6\Source\Features\Apparel\MugirlApparelTagUtility.cs'
    $apparelGenerationSafetyChecks++
    if (Test-Path -LiteralPath $apparelTagUtilityPath) {
        $apparelTagUtilityText = Get-Content -LiteralPath $apparelTagUtilityPath -Encoding utf8 -Raw
        $apparelTagSelectionSafe = $apparelTagUtilityText -match 'tag\.NullOrEmpty\(\)' `
            -and $apparelTagUtilityText -match 'ThingDef\s+chosenDef\s*=\s*null' `
            -and $apparelTagUtilityText -match 'int\s+matchingCandidates\s*=\s*0' `
            -and $apparelTagUtilityText -match 'Rand\.Range\(0,\s*matchingCandidates\)\s*==\s*0' `
            -and $apparelTagUtilityText -notmatch 'new\s+List<ThingDef>\s+candidates' `
            -and $apparelTagUtilityText -notmatch 'candidates\.RandomElement\(\)'
        if (-not $apparelTagSelectionSafe) {
            $apparelGenerationSafetyIssues += "$apparelTagUtilityPath :: apparel tag selection must guard empty tags and use zero-list-allocation reservoir sampling"
        }
    }
    else {
        $apparelGenerationSafetyIssues += "$apparelTagUtilityPath :: missing file for apparel generation safety"
    }
    if ($apparelGenerationSafetyIssues.Count) {
        $apparelGenerationSafetyIssues | Sort-Object
        Fail "Apparel generation safety scan failed: $($apparelGenerationSafetyIssues.Count) issue(s)"
    }

    Write-Step "Lifecycle base calls"
    $lifecycleOverrideCount = 0
    $lifecycleBaseCallIssues = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs') {
        $lines = Get-Content -LiteralPath $file.FullName -Encoding utf8
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            if ($lines[$lineIndex] -notmatch 'override\s+void\s+(Notify_Equipped|Notify_Unequipped)\s*\(') {
                continue
            }

            $methodName = $matches[1]
            $lifecycleOverrideCount++
            $body = New-Object System.Text.StringBuilder
            $depth = 0
            $seenBrace = $false
            for ($bodyLineIndex = $lineIndex; $bodyLineIndex -lt $lines.Count; $bodyLineIndex++) {
                $line = $lines[$bodyLineIndex]
                [void]$body.AppendLine($line)

                $openCount = [regex]::Matches($line, '\{').Count
                $closeCount = [regex]::Matches($line, '\}').Count
                if ($openCount -gt 0) {
                    $seenBrace = $true
                }
                if ($seenBrace) {
                    $depth += $openCount - $closeCount
                    if ($depth -le 0) {
                        break
                    }
                }
            }

            if ($body.ToString() -notmatch [regex]::Escape("base.$methodName(")) {
                $lifecycleBaseCallIssues += "$(Get-RelativePath $file.FullName):$($lineIndex + 1) :: $methodName override does not call base.$methodName"
            }
        }
    }
    if ($lifecycleBaseCallIssues.Count) {
        $lifecycleBaseCallIssues | Sort-Object
        Fail "Lifecycle base call scan failed: $($lifecycleBaseCallIssues.Count) missing base call(s)"
    }

    Write-Step "Apparel lifecycle safety"
    $apparelLifecycleSafetyChecks = 0
    $apparelLifecycleSafetyIssues = @()
    $slaveApparelDefPath = '1.6\Source\Features\Restraints\SlaveApparelDef.cs'
    $apparelLifecycleSafetyChecks++
    if (Test-Path -LiteralPath $slaveApparelDefPath) {
        $slaveApparelDefText = Get-Content -LiteralPath $slaveApparelDefPath -Encoding utf8 -Raw
        $slaveApparelLifecycleSafe = $slaveApparelDefText -match 'pawn\?\.apparel\s*==\s*null' `
            -and $slaveApparelDefText -match 'pawn\.health\?\.hediffSet\s*!=\s*null' `
            -and $slaveApparelDefText -match 'pawn\.RaceProps\?\.body\?\.AllParts\s*!=\s*null' `
            -and $slaveApparelDefText -match '!pawn\.health\.hediffSet\.HasHediff\(def\.equipped_hediff,\s*bodyPart\)' `
            -and $slaveApparelDefText -match 'for\s*\(\s*int\s+i\s*=\s*hediffs\.Count\s*-\s*1;\s*i\s*>=\s*0;\s*i--\s*\)' `
            -and $slaveApparelDefText -match 'CanAutoWearNextStage' `
            -and $slaveApparelDefText -match 'nextDef\.IsApparel' `
            -and $slaveApparelDefText -match 'ApparelUtility\.HasPartsToWear\(pawn,\s*nextDef\)' `
            -and $slaveApparelDefText -match 'ThingMaker\.MakeThing\(nextDef,\s*stuff\)\s+is\s+Apparel\s+nextApparel' `
            -and $slaveApparelDefText -match 'Wear\(nextApparel,\s*locked:\s*true\)' `
            -and $slaveApparelDefText -match 'MugirlSelectionUtility\.SelectInPlaying\(wearer\)' `
            -and $slaveApparelDefText -notmatch 'new\s+List<Hediff>\s+hediffsToRemove' `
            -and $slaveApparelDefText -notmatch 'Find\.Selector'
        if (-not $slaveApparelLifecycleSafe) {
            $apparelLifecycleSafetyIssues += "$slaveApparelDefPath :: slave apparel lifecycle must guard pawn health/body/apparel, avoid duplicate equipped hediffs, remove hediffs in-place and gate next-stage wear/selector calls"
        }
    }
    else {
        $apparelLifecycleSafetyIssues += "$slaveApparelDefPath :: missing file for slave apparel lifecycle safety"
    }
    if ($apparelLifecycleSafetyIssues.Count) {
        $apparelLifecycleSafetyIssues | Sort-Object
        Fail "Apparel lifecycle safety scan failed: $($apparelLifecycleSafetyIssues.Count) issue(s)"
    }

    Write-Step "Generated apparel lock safety"
    $generatedApparelLockSafetyChecks = 0
    $generatedApparelLockSafetyIssues = @()

    $pawnGeneratorPatchPath = '1.6\Source\Features\Restraints\Harmony_PawnGenerator_Patch.cs'
    $generatedApparelLockSafetyChecks++
    if (Test-Path -LiteralPath $pawnGeneratorPatchPath) {
        $pawnGeneratorPatchText = Get-Content -LiteralPath $pawnGeneratorPatchPath -Encoding utf8 -Raw
        $pawnGeneratorPatchSafe = $pawnGeneratorPatchText -match 'PawnGenerator_GeneratePawn_Patch' `
            -and $pawnGeneratorPatchText -match '__result\.EnsureWornSlaveApparelLocks\(\)' `
            -and $pawnGeneratorPatchText -notmatch '__result\.apparel\.Lock' `
            -and $pawnGeneratorPatchText -notmatch '__result\.apparel\.WornApparel'
        if (-not $pawnGeneratorPatchSafe) {
            $generatedApparelLockSafetyIssues += "$pawnGeneratorPatchPath :: PawnGenerator postfix must delegate generated slave-apparel locking to the shared helper"
        }
    }
    else {
        $generatedApparelLockSafetyIssues += "$pawnGeneratorPatchPath :: missing file for generated apparel lock safety"
    }

    $generatedApparelLockSafetyChecks++
    $slaveApparelExtensionsPath = '1.6\Source\Features\Restraints\SlaveApparelExtensions.cs'
    if (Test-Path -LiteralPath $slaveApparelExtensionsPath) {
        $slaveApparelExtensionsText = Get-Content -LiteralPath $slaveApparelExtensionsPath -Encoding utf8 -Raw
        $generatedLockHelperSafe = $slaveApparelExtensionsText -match 'EnsureWornSlaveApparelLocks\(this\s+Pawn\s+pawn\)' `
            -and $slaveApparelExtensionsText -match 'pawn\?\.apparel\?\.WornApparel\s*==\s*null' `
            -and $slaveApparelExtensionsText -match 'List<Apparel>\s+wornApparel\s*=\s*pawn\.apparel\.WornApparel' `
            -and $slaveApparelExtensionsText -match 'for\s*\(\s*int\s+i\s*=\s*0;\s*i\s*<\s*wornApparel\.Count;\s*i\+\+\s*\)' `
            -and $slaveApparelExtensionsText -match 'Apparel\s+apparel\s*=\s*wornApparel\[i\]' `
            -and $slaveApparelExtensionsText -match 'apparel\.IsLockedSlaveApparel\(\)\s+&&\s+!pawn\.apparel\.IsLocked\(apparel\)' `
            -and $slaveApparelExtensionsText -match 'pawn\.apparel\.Lock\(apparel\)'
        if (-not $generatedLockHelperSafe) {
            $generatedApparelLockSafetyIssues += "$slaveApparelExtensionsPath :: generated slave-apparel lock helper must guard pawn/apparel state, iterate by index and avoid duplicate locked-apparel entries"
        }
    }
    else {
        $generatedApparelLockSafetyIssues += "$slaveApparelExtensionsPath :: missing file for generated apparel lock helper safety"
    }

    $generatedApparelLockSafetyChecks++
    $apparelTrackerPatchPath = '1.6\Source\Features\Restraints\Harmony_DropThingTooltip.cs'
    if (Test-Path -LiteralPath $apparelTrackerPatchPath) {
        $apparelTrackerPatchText = Get-Content -LiteralPath $apparelTrackerPatchPath -Encoding utf8 -Raw
        $apparelTrackerLockBoundarySafe = $apparelTrackerPatchText -notmatch 'nameof\(Pawn_ApparelTracker\.IsLocked\)' `
            -and $apparelTrackerPatchText -match 'nameof\(Pawn_ApparelTracker\.Unlock\)' `
            -and $apparelTrackerPatchText -match 'return\s+!apparel\.IsLockedSlaveApparel\(\)' `
            -and $apparelTrackerPatchText -match 'WornApparel\?\.Contains\(apparel\)\s*!=\s*true' `
            -and $apparelTrackerPatchText -match 'nameof\(Pawn_ApparelTracker\.ExposeData\)' `
            -and $apparelTrackerPatchText -match 'Scribe\.mode\s*==\s*LoadSaveMode\.PostLoadInit' `
            -and $apparelTrackerPatchText -match 'EnsureWornSlaveApparelLocks\(\)'
        if (-not $apparelTrackerLockBoundarySafe) {
            $generatedApparelLockSafetyIssues += "$apparelTrackerPatchPath :: slave apparel must preserve base IsLocked semantics, block invalid worn unlocks and restore native locks after loading"
        }
    }
    else {
        $generatedApparelLockSafetyIssues += "$apparelTrackerPatchPath :: missing file for slave-apparel tracker lock boundary safety"
    }

    $generatedApparelLockSafetyChecks++
    $patchRegistryPath = '1.6\Source\Core\MugirlPatchRegistry.cs'
    if (Test-Path -LiteralPath $patchRegistryPath) {
        $patchRegistryText = Get-Content -LiteralPath $patchRegistryPath -Encoding utf8 -Raw
        $manualPatchSafe = $patchRegistryText -match 'PatchPawnGeneratorGeneratePawn\(harmony\)' `
            -and $patchRegistryText -match 'AccessTools\.Method\(typeof\(PawnGenerator\),\s*nameof\(PawnGenerator\.GeneratePawn\)' `
            -and $patchRegistryText -match 'typeof\(PawnGenerator_GeneratePawn_Patch\)' `
            -and $patchRegistryText -match 'nameof\(PawnGenerator_GeneratePawn_Patch\.Postfix\)' `
            -and $patchRegistryText -match 'Mugirl\.PatchRegistry\.PawnGeneratorGeneratePawn'
        if (-not $manualPatchSafe) {
            $generatedApparelLockSafetyIssues += "$patchRegistryPath :: PawnGenerator manual patch registration must stay explicit and auditable"
        }
    }
    else {
        $generatedApparelLockSafetyIssues += "$patchRegistryPath :: missing file for manual PawnGenerator patch registration safety"
    }

    if ($generatedApparelLockSafetyIssues.Count) {
        $generatedApparelLockSafetyIssues | Sort-Object
        Fail "Generated apparel lock safety scan failed: $($generatedApparelLockSafetyIssues.Count) issue(s)"
    }

    Write-Step "Manual patch registry safety"
    $manualPatchRegistrySafetyChecks = 0
    $manualPatchRegistrySafetyIssues = @()

    $manualPatchRegistrySafetyChecks++
    $patchRegistryPath = '1.6\Source\Core\MugirlPatchRegistry.cs'
    if (Test-Path -LiteralPath $patchRegistryPath) {
        $patchRegistryText = Get-Content -LiteralPath $patchRegistryPath -Encoding utf8 -Raw
        $manualPatchFailureSafe = $patchRegistryText -match 'using\s+System;' `
            -and $patchRegistryText -match 'catch\s*\(\s*Exception\s+ex\s*\)' `
            -and $patchRegistryText -match 'ex\.GetType\(\)\.Name\s*\+\s*": "\s*\+\s*ex\.Message' `
            -and $patchRegistryText -match 'Mugirl\.PatchRegistry\.PatchFailed' `
            -and $patchRegistryText -match 'PatchRegistry\.PatchFailed\.\s*"\s*\+\s*nameKey' `
            -and $patchRegistryText -match '(?s)try\s*\{.*harmony\.Patch\s*\(.*manualPatchNames\.Add\(nameKey\);.*\}\s*catch\s*\(\s*Exception\s+ex\s*\)'
        if (-not $manualPatchFailureSafe) {
            $manualPatchRegistrySafetyIssues += "$patchRegistryPath :: manual Harmony patch registration must catch patch failures, log a localized WarningOnce and record patch names only after successful Patch calls"
        }
    }
    else {
        $manualPatchRegistrySafetyIssues += "$patchRegistryPath :: missing file for manual patch registry safety"
    }

    $manualPatchRegistrySafetyChecks++
    $englishGameplayLanguagePath = '1.6\Languages\English\Keyed\Misc_Gameplay.xml'
    $chineseGameplayLanguagePath = '1.6\Languages\ChineseSimplified\Keyed\Misc_Gameplay.xml'
    if ((Test-Path -LiteralPath $englishGameplayLanguagePath) -and (Test-Path -LiteralPath $chineseGameplayLanguagePath)) {
        $englishGameplayLanguageText = Get-Content -LiteralPath $englishGameplayLanguagePath -Encoding utf8 -Raw
        $chineseGameplayLanguageText = Get-Content -LiteralPath $chineseGameplayLanguagePath -Encoding utf8 -Raw
        $manualPatchFailureTextSafe = $englishGameplayLanguageText -match '<Mugirl\.PatchRegistry\.PatchFailed>Manual patch failed: \{0\}\. \{1\}</Mugirl\.PatchRegistry\.PatchFailed>' `
            -and $chineseGameplayLanguageText -match '<Mugirl\.PatchRegistry\.PatchFailed>手动补丁应用失败：\{0\}。\{1\}</Mugirl\.PatchRegistry\.PatchFailed>'
        if (-not $manualPatchFailureTextSafe) {
            $manualPatchRegistrySafetyIssues += "$englishGameplayLanguagePath / $chineseGameplayLanguagePath :: manual patch failure warning text must keep English/Chinese key parity and placeholder parity"
        }
    }
    else {
        $manualPatchRegistrySafetyIssues += "$englishGameplayLanguagePath / $chineseGameplayLanguagePath :: missing language files for manual patch registry safety"
    }

    if ($manualPatchRegistrySafetyIssues.Count) {
        $manualPatchRegistrySafetyIssues | Sort-Object
        Fail "Manual patch registry safety scan failed: $($manualPatchRegistrySafetyIssues.Count) issue(s)"
    }

    Write-Step "Patch metadata audit"
    $patchMetadataAuditChecks = 0
    $patchMetadataAuditIssues = @()
    $patchMetadataAuditEntries = 0

    $patchInfoPath = '1.6\Source\Core\MugirlPatchInfo.cs'
    if (Test-Path -LiteralPath $patchInfoPath) {
        $patchInfoText = Get-Content -LiteralPath $patchInfoPath -Encoding utf8 -Raw

        $patchMetadataAuditChecks++
        $patchInfoShapeSafe = $patchInfoText -match 'internal\s+sealed\s+class\s+MugirlPatchInfo' `
            -and $patchInfoText -match 'internal\s+enum\s+MugirlPatchRiskLevel' `
            -and $patchInfoText -match 'ModuleName\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'PatchClassName\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'PatchClassFullName\s*=>' `
            -and $patchInfoText -match 'TargetTypeName\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'TargetMethodName\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'PatchKind\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'MaySkipOriginal\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'CompatibilityRisk\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'FailureBehavior\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'RegistryNameKey\s*\{\s*get;\s*\}' `
            -and $patchInfoText -match 'HighRiskPatchInfos' `
            -and $patchInfoText -match 'LogDevSummary'
        if (-not $patchInfoShapeSafe) {
            $patchMetadataAuditIssues += "$patchInfoPath :: patch metadata must expose module, class, target, kind, skip-original flag, risk level, failure behavior, manual registry key and DevMode summary"
        }

        $registeredPatchClassMatches = [regex]::Matches($patchInfoText, 'patchClassName:\s*"([^"]+)"')
        $patchMetadataAuditEntries = $registeredPatchClassMatches.Count
        $registeredPatchClasses = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($match in $registeredPatchClassMatches) {
            [void]$registeredPatchClasses.Add('Mugirl.' + $match.Groups[1].Value)
        }

        $patchMetadataAuditChecks++
        if ($registeredPatchClasses.Count -ne $registeredPatchClassMatches.Count) {
            $patchMetadataAuditIssues += "$patchInfoPath :: duplicate patchClassFullName entries found in patch metadata"
        }

        $registeredManualPatchKeys = New-Object 'System.Collections.Generic.HashSet[string]'
        [regex]::Matches($patchInfoText, 'registryNameKey:\s*"([^"]+)"') | ForEach-Object {
            [void]$registeredManualPatchKeys.Add($_.Groups[1].Value)
        }

        $actualPatchClasses = New-Object 'System.Collections.Generic.HashSet[string]'
        Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
            $sourceText = Get-Content -LiteralPath $_.FullName -Encoding utf8 -Raw
            [regex]::Matches($sourceText, '(?m)(?:^\s*\[\s*HarmonyPatch[^\r\n]*\]\s*\r?\n)+(?:\s*(?:public|internal)\s+static\s+class\s+([A-Za-z0-9_]+))') | ForEach-Object {
                [void]$actualPatchClasses.Add('Mugirl.' + $_.Groups[1].Value)
            }
        }

        foreach ($patchClass in ($actualPatchClasses | Sort-Object)) {
            $patchMetadataAuditChecks++
            if (-not $registeredPatchClasses.Contains($patchClass)) {
                $patchMetadataAuditIssues += "$patchInfoPath :: missing MugirlPatchInfo for Harmony patch class $patchClass"
            }
        }

        $manualPatchExpectations = @(
            @{
                Class = 'Mugirl.PawnGenerator_GeneratePawn_Patch'
                Key = 'Mugirl.PatchRegistry.PawnGeneratorGeneratePawn'
            }
        )
        foreach ($expectedManualPatch in $manualPatchExpectations) {
            $patchMetadataAuditChecks++
            if (-not $registeredPatchClasses.Contains($expectedManualPatch.Class)) {
                $patchMetadataAuditIssues += "$patchInfoPath :: missing MugirlPatchInfo for manual patch class $($expectedManualPatch.Class)"
            }

            if (-not $registeredManualPatchKeys.Contains($expectedManualPatch.Key)) {
                $patchMetadataAuditIssues += "$patchInfoPath :: missing registryNameKey metadata for manual patch $($expectedManualPatch.Key)"
            }

            [void]$actualPatchClasses.Add($expectedManualPatch.Class)
        }

        foreach ($registeredPatchClass in ($registeredPatchClasses | Sort-Object)) {
            $patchMetadataAuditChecks++
            if (-not $actualPatchClasses.Contains($registeredPatchClass)) {
                $patchMetadataAuditIssues += "$patchInfoPath :: patch metadata entry has no matching Harmony or manual patch class: $registeredPatchClass"
            }
        }

        $patchMetadataAuditChecks++
        if ($patchInfoText -notmatch 'compatibilityRisk:\s*MugirlPatchRiskLevel\.(High|Critical)') {
            $patchMetadataAuditIssues += "$patchInfoPath :: patch metadata must include high-risk or critical entries"
        }
    }
    else {
        $patchMetadataAuditIssues += "$patchInfoPath :: missing patch metadata file"
    }

    $patchMetadataAuditChecks++
    $bootstrapPath = '1.6\Source\Core\MugirlBootstrap.cs'
    if (Test-Path -LiteralPath $bootstrapPath) {
        $bootstrapText = Get-Content -LiteralPath $bootstrapPath -Encoding utf8 -Raw
        if ($bootstrapText -notmatch 'MugirlPatchCatalog\.LogDevSummary\(patchedClassNames,\s*MugirlPatchRegistry\.ManualPatchNames\)') {
            $patchMetadataAuditIssues += "$bootstrapPath :: bootstrap must emit the patch metadata DevMode summary after attribute and manual patch registration"
        }
    }
    else {
        $patchMetadataAuditIssues += "$bootstrapPath :: missing file for patch metadata DevMode summary"
    }

    if ($patchMetadataAuditIssues.Count) {
        $patchMetadataAuditIssues | Sort-Object
        Fail "Patch metadata audit failed: $($patchMetadataAuditIssues.Count) issue(s)"
    }

    Write-Step "Static cache lifecycle"
    $staticCacheLifecycleChecks = 0
    $staticCacheLifecycleFields = 0
    $staticCacheLifecycleIssues = @()

    $staticCacheFieldPatterns = @(
        'private\s+static\s+(?:readonly\s+)?(?:Dictionary|HashSet|List)<[^>]+>\s+\w+\s*(?:=|;)',
        'private\s+static\s+readonly\s+(?:FieldInfo|MethodInfo)\s+\w+\s*(?:=|;)',
        'private\s+static\s+(?:HediffDef|TraitDef)\s+\w+\s*(?:=|;)'
    )

    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $path = $_.FullName
        $relative = Get-RelativePath $path
        $lines = Get-Content -LiteralPath $path -Encoding utf8
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $isStaticCacheField = $false
            foreach ($pattern in $staticCacheFieldPatterns) {
                if ($line -match $pattern) {
                    $isStaticCacheField = $true
                    break
                }
            }

            if (-not $isStaticCacheField) {
                continue
            }

            $staticCacheLifecycleFields++
            $staticCacheLifecycleChecks++
            $hasLifecycleComment = $false
            $start = [Math]::Max(0, $i - 8)
            for ($j = $start; $j -lt $i; $j++) {
                if ($lines[$j] -match 'StaticCacheLifecycle:') {
                    $hasLifecycleComment = $true
                    break
                }
            }

            if (-not $hasLifecycleComment) {
                $staticCacheLifecycleIssues += "${relative}:$($i + 1) :: static cache field must declare StaticCacheLifecycle: $($line.Trim())"
            }
        }
    }

    $staticCacheLifecycleChecks++
    $optionalDefsPath = '1.6\Source\Core\MugirlOptionalDefs.cs'
    if (Test-Path -LiteralPath $optionalDefsPath) {
        $optionalDefsText = Get-Content -LiteralPath $optionalDefsPath -Encoding utf8 -Raw
        if ($optionalDefsText -notmatch 'StaticCacheLifecycle: 进程级可选 Def 缓存') {
            $staticCacheLifecycleIssues += "$optionalDefsPath :: optional Def cache must document process-level lifecycle and nullable optional entries"
        }
    }
    else {
        $staticCacheLifecycleIssues += "$optionalDefsPath :: missing optional Def cache file"
    }

    $staticCacheLifecycleChecks++
    $requiredDefsPath = '1.6\Source\Core\MugirlRequiredDefs.cs'
    if (Test-Path -LiteralPath $requiredDefsPath) {
        $requiredDefsText = Get-Content -LiteralPath $requiredDefsPath -Encoding utf8 -Raw
        if ($requiredDefsText -notmatch 'StaticCacheLifecycle: 进程级必需 Def 缓存') {
            $staticCacheLifecycleIssues += "$requiredDefsPath :: required Def cache must document process-level lifecycle and fail-fast behavior"
        }
    }
    else {
        $staticCacheLifecycleIssues += "$requiredDefsPath :: missing required Def cache file"
    }

    $staticCacheLifecycleChecks++
    $storyStatePath = '1.6\Source\Features\Incidents\MugirlStoryState.cs'
    if (Test-Path -LiteralPath $storyStatePath) {
        $storyStateText = Get-Content -LiteralPath $storyStatePath -Encoding utf8 -Raw
        $storyStateResetsSafe = $storyStateText -match 'FinalizeInit\(\)' `
            -and $storyStateText -match 'StartedNewGame\(\)' `
            -and $storyStateText -match 'LoadedGame\(\)' `
            -and $storyStateText -match 'MugirlLog\.ResetOnceWarnings\(\)' `
            -and $storyStateText -match 'MugirlFoodEffectUtility\.ResetDefCache\(\)' `
            -and $storyStateText -match 'MugirlNurtureUtility\.ResetDefCache\(\)' `
            -and $storyStateText -match 'MugirlMilkingAnimation\.ResetTransientState\(\)' `
            -and $storyStateText -match 'MountedCombatController\.ResetTransientState\(\)' `
            -and $storyStateText -match 'GhoulRenderingRefreshUtility\.ClearPendingRefreshes\(\)'
        if (-not $storyStateResetsSafe) {
            $staticCacheLifecycleIssues += "$storyStatePath :: game init/new-game/load must reset once warnings, Def caches and transient pawn/verb render state"
        }
    }
    else {
        $staticCacheLifecycleIssues += "$storyStatePath :: missing story state reset file"
    }

    if ($staticCacheLifecycleIssues.Count) {
        $staticCacheLifecycleIssues | Sort-Object
        Fail "Static cache lifecycle scan failed: $($staticCacheLifecycleIssues.Count) issue(s)"
    }

    Write-Step "Scribe state defaults"
    $requiredScribeDefaultFields = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($field in @(
        'currentTicksToChange',
        'currentCycleInterval',
        'currentBindDurationTicks',
        'nextStateTick',
        'lockCount',
        'spawnCell'
    )) {
        [void]$requiredScribeDefaultFields.Add($field)
    }

    $scribeStateDefaultCount = 0
    $missingScribeDefaults = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs') {
        Select-String -LiteralPath $file.FullName -Pattern 'Scribe_Values\.Look' | ForEach-Object {
            $line = $_.Line.Trim()
            if ($line -match 'Scribe_Values\.Look(?:<[^>]+>)?\(\s*ref\s+(?:this\.)?([A-Za-z_][A-Za-z0-9_]*)\s*,\s*"([^"]+)"\s*\)') {
                $fieldName = $matches[1]
                if ($requiredScribeDefaultFields.Contains($fieldName)) {
                    $scribeStateDefaultCount++
                    $missingScribeDefaults += "$(Get-RelativePath $file.FullName):$($_.LineNumber) :: $line"
                }
            }
        }
    }
    if ($missingScribeDefaults.Count) {
        $missingScribeDefaults | Sort-Object
        Fail "Scribe state default scan failed: $($missingScribeDefaults.Count) missing default value(s)"
    }

    Write-Step "Git diff whitespace"
    $diffCheck = & git diff --check 2>&1
    if ($LASTEXITCODE -ne 0) {
        $diffCheck
        Fail "git diff --check failed"
    }

    Write-Step "csproj source references"
    $projPath = '1.6\Source\MugirlRace.csproj'
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

    Write-Step "Feature module size report"
    $featureModuleSizeChecks = 0
    $featureModuleLargeFileCount = 0
    $featureModuleStats = @{}
    foreach ($moduleName in @('Milk', 'Mounting', 'Restraints')) {
        $featureModuleSizeChecks++
        $modulePath = "1.6\Source\Features\$moduleName"
        if (-not (Test-Path -LiteralPath $modulePath)) {
            Fail "Feature module size report failed: missing $modulePath"
        }

        $moduleFiles = @(Get-ChildItem -LiteralPath $modulePath -Recurse -Filter '*.cs')
        $moduleLineCount = 0
        $moduleMaxFileLines = 0
        foreach ($moduleFile in $moduleFiles) {
            $lineCount = (Get-Content -LiteralPath $moduleFile.FullName -Encoding utf8).Count
            $moduleLineCount += $lineCount
            if ($lineCount -gt $moduleMaxFileLines) {
                $moduleMaxFileLines = $lineCount
            }
            if ($lineCount -gt 400) {
                $featureModuleLargeFileCount++
            }
        }

        $featureModuleStats[$moduleName] = [pscustomobject]@{
            Files = $moduleFiles.Count
            Lines = $moduleLineCount
            MaxFileLines = $moduleMaxFileLines
        }
    }

    Write-Step "C# empty production types"
    $allowedEmptyProductionTypes = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($typeName in @(
        'BrainWashSlaveApparel',
        'CompAdultContentControl'
    )) {
        [void]$allowedEmptyProductionTypes.Add($typeName)
    }

    $emptyProductionTypePattern = '(?s)(?:^|[\s;{}])(?:\[[^\]]+\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|new)\s+)*(class|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*)[^{;]*\{\s*\}'
    $emptyProductionTypeCount = 0
    $unexpectedEmptyProductionTypes = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs') {
        if ($file.FullName -match '\\bin\\|\\obj\\') {
            continue
        }

        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
        $scanText = [regex]::Replace($text, '(?s)/\*.*?\*/|//[^\r\n]*', '')
        foreach ($match in [regex]::Matches($scanText, $emptyProductionTypePattern)) {
            $emptyProductionTypeCount++
            $typeName = $match.Groups[2].Value
            if (-not $allowedEmptyProductionTypes.Contains($typeName)) {
                $unexpectedEmptyProductionTypes += "$(Get-RelativePath $file.FullName) :: empty $($match.Groups[1].Value) $typeName"
            }
        }
    }
    if ($unexpectedEmptyProductionTypes.Count) {
        $unexpectedEmptyProductionTypes | Sort-Object
        Fail "C# empty production type scan failed: $($unexpectedEmptyProductionTypes.Count) unexpected empty type(s)"
    }

    Write-Step "Mugirl XML type references"
    $classNames = New-Object 'System.Collections.Generic.HashSet[string]'
    $qualifiedClassNames = New-Object 'System.Collections.Generic.HashSet[string]'
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8
        $sourceNamespace = [regex]::Match($text, '(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)').Groups[1].Value
        foreach ($match in [regex]::Matches($text, '\b(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')) {
            [void]$classNames.Add($match.Groups[1].Value)
            [void]$qualifiedClassNames.Add("$sourceNamespace.$($match.Groups[1].Value)")
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
    $unqualifiedMugirlTypeRefs = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6', 'Bio_1.6', 'Versions' -Recurse -Filter '*.xml') {
        $doc = Get-XmlDocument $file.FullName
        foreach ($node in $doc.SelectNodes('//*')) {
            foreach ($field in $typeFields) {
                if ($node.Attributes -and $node.Attributes[$field]) {
                    $value = $node.Attributes[$field].Value.Trim()
                    if ($value.StartsWith('Mugirl.')) {
                        [void]$typeRefs.Add($value)
                        $qualifiedName = $value.Split(',')[0].Trim()
                        if (-not $qualifiedClassNames.Contains($qualifiedName)) {
                            $missingTypeRefs += "$(Get-RelativePath $file.FullName) :: @$field=$value"
                        }
                    }
                    elseif (-not $value.Contains('.') -and $classNames.Contains($value)) {
                        $unqualifiedMugirlTypeRefs += "$(Get-RelativePath $file.FullName) :: @$field=$value"
                    }
                }
            }
            if ($typeFields -contains $node.LocalName) {
                $value = $node.InnerText.Trim()
                if ($value.StartsWith('Mugirl.')) {
                    [void]$typeRefs.Add($value)
                    $qualifiedName = $value.Split(',')[0].Trim()
                    if (-not $qualifiedClassNames.Contains($qualifiedName)) {
                        $missingTypeRefs += "$(Get-RelativePath $file.FullName) :: <$($node.LocalName)>$value"
                    }
                }
                elseif (-not $value.Contains('.') -and $classNames.Contains($value)) {
                    $unqualifiedMugirlTypeRefs += "$(Get-RelativePath $file.FullName) :: <$($node.LocalName)>$value"
                }
            }
        }
    }
    if ($missingTypeRefs.Count -or $unqualifiedMugirlTypeRefs.Count) {
        $missingTypeRefs | Sort-Object | Select-Object -First 120
        if ($unqualifiedMugirlTypeRefs.Count) {
            "Unqualified Mugirl type references:"
            $unqualifiedMugirlTypeRefs | Sort-Object | Select-Object -First 120
        }
        Fail "Mugirl XML type cross-check failed: $($missingTypeRefs.Count) missing reference(s), $($unqualifiedMugirlTypeRefs.Count) unqualified reference(s)"
    }

    Write-Step "Namespace boundary report"
    $namespaceBoundaryChecks = 0
    $namespaceRootTypeCount = 0
    $namespaceRootXmlReferencedTypeCount = 0
    $namespaceRootLegacyTypeCount = 0
    $namespaceSpecificTypeCount = 0
    $namespaceNonMugirlTypeCount = 0
    $namespaceSpecificNames = New-Object 'System.Collections.Generic.HashSet[string]'
    $xmlReferencedTypeNames = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($typeRef in $typeRefs) {
        $shortName = $typeRef.Substring('Mugirl.'.Length).Split(',')[0].Trim()
        if (-not [string]::IsNullOrWhiteSpace($shortName)) {
            [void]$xmlReferencedTypeNames.Add($shortName)
        }
    }

    $namespaceBoundaryChecks++
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs') {
        if ($file.FullName -match '\\bin\\|\\obj\\') {
            continue
        }

        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
        $namespaceMatch = [regex]::Match($text, '(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)')
        if (-not $namespaceMatch.Success) {
            continue
        }

        $namespaceName = $namespaceMatch.Groups[1].Value
        $typeMatches = [regex]::Matches($text, '(?m)^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|new)\s+)*(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')
        foreach ($match in $typeMatches) {
            $typeName = $match.Groups[1].Value
            if ($namespaceName -eq 'Mugirl') {
                $namespaceRootTypeCount++
                if ($xmlReferencedTypeNames.Contains($typeName)) {
                    $namespaceRootXmlReferencedTypeCount++
                }
                else {
                    $namespaceRootLegacyTypeCount++
                }
            }
            elseif ($namespaceName.StartsWith('Mugirl.')) {
                $namespaceSpecificTypeCount++
                [void]$namespaceSpecificNames.Add($namespaceName)
            }
            else {
                $namespaceNonMugirlTypeCount++
            }
        }
    }

    Write-Step "Def index"
    $defsByType = @{}
    $modMayRequireDefsByType = @{}
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
                $defType = Get-NormalizedDefType $xmlType
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
                if ($isModRoot -and $node.Attributes -and ($node.Attributes['MayRequire'] -or $node.Attributes['MayRequireAnyOf'])) {
                    Add-SetValue $modMayRequireDefsByType $defType $defName
                }

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
        @('BackstoryDef', 'Mugirl_ChildSlaveBackStory'),
        @('MentalStateDef', 'Mugirl_BrainWashing'),
        @('ThingDef', 'Mugirl'),
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

    Write-Step "Optional DLC Def gates"
    function Get-DefNameSetFromRoot {
        param([string]$Root)

        $set = New-Object 'System.Collections.Generic.HashSet[string]'
        if (-not (Test-Path -LiteralPath $Root)) {
            return $set
        }

        Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.xml' | ForEach-Object {
            $doc = Get-XmlDocument $_.FullName
            foreach ($node in $doc.SelectNodes('//defName')) {
                $value = $node.InnerText.Trim()
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    [void]$set.Add($value)
                }
            }
        }

        return $set
    }

    function Test-XmlNodeHasLoadGate {
        param([System.Xml.XmlNode]$Node)

        $current = $Node
        while ($current -ne $null -and $current.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            if ($current.Attributes['MayRequire'] -or $current.Attributes['MayRequireAnyOf']) {
                return $true
            }
            $current = $current.ParentNode
        }

        return $false
    }

    $coreDefNames = Get-DefNameSetFromRoot '..\..\Data\Core\Defs'
    $modDefNames = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($root in Get-ModDefRoots) {
        $rootDefs = Get-DefNameSetFromRoot $root
        foreach ($defName in $rootDefs) {
            [void]$modDefNames.Add($defName)
        }
    }

    $dlcOnlyDefNames = @{}
    foreach ($entry in @{
        'Royalty' = '..\..\Data\Royalty\Defs'
        'Ideology' = '..\..\Data\Ideology\Defs'
        'Biotech' = '..\..\Data\Biotech\Defs'
        'Anomaly' = '..\..\Data\Anomaly\Defs'
        'Odyssey' = '..\..\Data\Odyssey\Defs'
    }.GetEnumerator()) {
        $dlcDefs = Get-DefNameSetFromRoot $entry.Value
        foreach ($defName in $dlcDefs) {
            if (-not $coreDefNames.Contains($defName) -and -not $modDefNames.Contains($defName)) {
                if (-not $dlcOnlyDefNames.ContainsKey($defName)) {
                    $dlcOnlyDefNames[$defName] = New-Object 'System.Collections.Generic.HashSet[string]'
                }
                [void]$dlcOnlyDefNames[$defName].Add($entry.Key)
            }
        }
    }

    $highConfidenceRefNodes = @(
        '//researchPrerequisite',
        '//researchPrerequisites/li',
        '//unfinishedThingDef',
        '//soundCast',
        '//soundCastTail',
        '//defaultProjectile',
        '//thingCategories/li',
        '//recipeUsers/li',
        '//costList/*',
        '//statBases/*',
        '//statOffsets/*',
        '//statFactors/*',
        '//equippedStatOffsets/*'
    )
    $optionalDlcRefCount = 0
    $ungatedDlcRefs = @()
    foreach ($file in Get-ChildItem -LiteralPath '1.6\Defs', '1.6\Patches' -Recurse -Filter '*.xml' -ErrorAction SilentlyContinue) {
        $doc = Get-XmlDocument $file.FullName
        foreach ($node in $doc.SelectNodes('//*[@ParentName]')) {
            $value = $node.Attributes['ParentName'].Value.Trim()
            if ($dlcOnlyDefNames.ContainsKey($value)) {
                $optionalDlcRefCount++
                if (-not (Test-XmlNodeHasLoadGate $node)) {
                    $ungatedDlcRefs += "$(Get-RelativePath $file.FullName) :: ParentName=$value [$([string]::Join(',', $dlcOnlyDefNames[$value]))]"
                }
            }
        }

        foreach ($xpath in $highConfidenceRefNodes) {
            foreach ($node in $doc.SelectNodes($xpath)) {
                $value = if ($xpath -in @('//costList/*', '//statBases/*', '//statOffsets/*', '//statFactors/*', '//equippedStatOffsets/*')) { $node.LocalName } else { $node.InnerText.Trim() }
                if ([string]::IsNullOrWhiteSpace($value)) {
                    continue
                }
                if ($dlcOnlyDefNames.ContainsKey($value)) {
                    $optionalDlcRefCount++
                    if (-not (Test-XmlNodeHasLoadGate $node)) {
                        $ungatedDlcRefs += "$(Get-RelativePath $file.FullName) :: <$($node.LocalName)>$value [$([string]::Join(',', $dlcOnlyDefNames[$value]))]"
                    }
                }
            }
        }
    }
    if ($ungatedDlcRefs.Count) {
        $ungatedDlcRefs | Sort-Object | Select-Object -First 120
        Fail "Optional DLC Def gate scan failed: $($ungatedDlcRefs.Count) ungated reference(s)"
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

    Write-Step "XML patch governance"
    $xmlPatchGovernanceChecks = 0
    $xmlPatchGovernanceFiles = 0
    $xmlPatchGovernanceIssues = @()

    foreach ($patchFile in $patchFiles) {
        $doc = Get-XmlDocument $patchFile
        if ($doc.DocumentElement -eq $null -or $doc.DocumentElement.LocalName -ne 'Patch') {
            continue
        }

        $xmlPatchGovernanceFiles++
        $xmlPatchGovernanceChecks++
        $patchText = Get-Content -LiteralPath $patchFile -Encoding utf8 -Raw
        $hasGovernanceComment = $patchText -match '(?s)<Patch>\s*<!--\s*PatchGovernance:.*Targets:.*Scope:.*Duplicate guard:.*Failure:.*-->'
        if (-not $hasGovernanceComment) {
            $xmlPatchGovernanceIssues += "$(Get-RelativePath $patchFile) :: patch file must start with PatchGovernance comment containing Targets, Scope, Duplicate guard and Failure"
        }
    }

    if ($xmlPatchGovernanceIssues.Count) {
        $xmlPatchGovernanceIssues | Sort-Object
        Fail "XML patch governance scan failed: $($xmlPatchGovernanceIssues.Count) issue(s)"
    }

    function Test-PatchOperationHasConditionalAncestor {
        param([System.Xml.XmlNode]$Node)

        $current = $Node.ParentNode
        while ($current -ne $null) {
            if ($current.NodeType -eq [System.Xml.XmlNodeType]::Element -and
                $current.Attributes -and
                $current.Attributes['Class'] -and
                $current.Attributes['Class'].Value -eq 'PatchOperationConditional') {
                return $true
            }

            $current = $current.ParentNode
        }

        return $false
    }

    function Test-PatchFileIsOptionalIntegration {
        param([string]$RelativePatch)

        $normalized = $RelativePatch.TrimStart('.', '\', '/').Replace('/', '\')
        return $normalized -match '^1\.6\\FacialAnimation\\Patches\\' -or
            $normalized -match '^Versions\\1\.6\\Integrations\\'
    }

    Write-Step "Direct PatchOperationAdd targets"
    $missingPatchTargets = @()
    $externalPatchTargets = @()
    $checkedPatchTargets = 0
    $directPatchAddGuardedTargets = 0
    $directPatchAddUnguardedTargets = 0
    $directPatchAddVanillaTargets = 0
    $directPatchAddModTargets = 0
    $directPatchAddOptionalIntegrationTargets = 0
    $directPatchAddUnclassifiedTargets = @()
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
            $defType = Get-NormalizedDefType $rawDefType
            $defName = $matches[2]
            $targetKey = "$defType::$defName"
            $relativePatch = (Get-RelativePath $patch).TrimStart('.', '\', '/').Replace('/', '\')
            if (Test-PatchOperationHasConditionalAncestor $operation) {
                $directPatchAddGuardedTargets++
            }
            else {
                $directPatchAddUnguardedTargets++
            }

            if (Test-PatchFileIsOptionalIntegration $relativePatch) {
                $directPatchAddOptionalIntegrationTargets++
            }
            elseif ($concreteVanillaDefs.ContainsKey($targetKey)) {
                $directPatchAddVanillaTargets++
            }
            elseif ($concreteModDefs.ContainsKey($targetKey)) {
                $directPatchAddModTargets++
            }
            else {
                $directPatchAddUnclassifiedTargets += "$relativePatch :: $xpath"
            }

            if (-not $defsByType.ContainsKey($defType) -or -not $defsByType[$defType].Contains($defName)) {
                if ($relativePatch -match '(^|\\)Versions\\1\.6\\Integrations\\') {
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
    if ($directPatchAddUnclassifiedTargets.Count) {
        $directPatchAddUnclassifiedTargets | Sort-Object
        Fail "Direct PatchOperationAdd classification failed: $($directPatchAddUnclassifiedTargets.Count) unclassified target(s)"
    }

    Write-Step "Keyed translations"
    $directKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    $broadKeys = New-Object 'System.Collections.Generic.HashSet[string]'
    # Exact historical class names in the save migration are not translation keys.
    $legacySerializedTypes = @('Mugirl.CorporateRuntimeValidation', 'Mugirl.CorporateVisualValidation')
    $skipPrefixes = @(
        'Mugirl.Comp', 'Mugirl.Hediff', 'Mugirl.JobDriver', 'Mugirl.Quest',
        'Mugirl.CompProperties', 'Mugirl.Thought', 'Mugirl.Incident',
        'Mugirl.Mugirl', 'Mugirl.SlaveApparel', 'Mugirl.GameComponent',
        'Mugirl.MapComponent', 'Mugirl.PawnRender', 'Mugirl.Command',
        'Mugirl.Building', 'Mugirl.Thing', 'Mugirl.Verb', 'Mugirl.Stat',
        'Mugirl.Work', 'Mugirl.Dialog', 'Mugirl.Mental', 'Mugirl.Pawn'
    )
    Get-ChildItem -LiteralPath '1.6\Source' -Recurse -Filter '*.cs' | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding utf8
        foreach ($match in [regex]::Matches($text, '"(Mugirl\.[A-Za-z0-9_.-]+)"\s*\.Translate\s*\(')) {
            [void]$directKeys.Add($match.Groups[1].Value)
        }
        foreach ($match in [regex]::Matches($text, '"(Mugirl\.[A-Za-z0-9_.-]+)"')) {
            $key = $match.Groups[1].Value
            $isType = $key -in $legacySerializedTypes
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
            # A trailing dot denotes a dynamically composed namespace, not a complete key.
            # Require the namespace to exist; direct Translate calls still require exact keys.
            if ($key.EndsWith('.') -and @($languageKeys | Where-Object { $_.StartsWith($key) }).Count -gt 0) {
                continue
            }
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
    $defInjectedIssues = @()
    foreach ($languageRoot in Get-LanguageRoots) {
        Get-ChildItem -LiteralPath $languageRoot -Recurse -Filter '*.xml' |
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
                    $defInjectedIssues += "$(Get-RelativePath $full) :: orphan $key"
                }

                if ($key -match '\.rulesStrings$') {
                    $listItems = @($node.ChildNodes | Where-Object { $_.NodeType -eq [System.Xml.XmlNodeType]::Element -and $_.LocalName -eq 'li' })
                    if ($listItems.Count -eq 0) {
                        $defInjectedIssues += "$(Get-RelativePath $full) :: $key must translate rulesStrings as <li> list nodes"
                    }
                }

                $relativeFull = Get-RelativePath $full
                if ($relativeFull -match '^[.\\\/]*1\.6[\\\/]Languages[\\\/]' -and
                    $modMayRequireDefsByType.ContainsKey($defType) -and
                    $modMayRequireDefsByType[$defType].Contains($defName)) {
                    $defInjectedIssues += "$relativeFull :: $key translates MayRequire-gated Def in root 1.6 language folder"
                }
            }
        }
    }
    if ($defInjectedIssues.Count) {
        $defInjectedIssues | Sort-Object | Select-Object -First 200
        Fail "DefInjected translation scan failed: $($defInjectedIssues.Count) issue(s)"
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
    $textureRoots = @('Textures', '1.6\Textures', '1.6\FacialAnimation\Textures', 'Bio_1.6\Textures', 'Odyssey_1.6\Textures', 'Versions\1.6\Textures') | Where-Object { Test-Path -LiteralPath $_ }
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
    Write-Host "  Feature module size rules: $featureModuleSizeChecks"
    foreach ($moduleName in @('Milk', 'Mounting', 'Restraints')) {
        $moduleStats = $featureModuleStats[$moduleName]
        Write-Host "  $moduleName feature files/lines/max: $($moduleStats.Files)/$($moduleStats.Lines)/$($moduleStats.MaxFileLines)"
    }
    Write-Host "  Feature module files over 400 lines: $featureModuleLargeFileCount"
    Write-Host "  Empty production marker types: $emptyProductionTypeCount"
    Write-Host "  LoadFolders v1.6 entries: $loadFolderEntries"
    Write-Host "  Mugirl XML type refs: $($typeRefs.Count)"
    Write-Host "  Unqualified Mugirl XML type refs: $($unqualifiedMugirlTypeRefs.Count)"
    Write-Host "  Namespace boundary rules: $namespaceBoundaryChecks"
    Write-Host "  Root namespace types: $namespaceRootTypeCount"
    Write-Host "  Root namespace XML-referenced types: $namespaceRootXmlReferencedTypeCount"
    Write-Host "  Root namespace legacy/non-XML types: $namespaceRootLegacyTypeCount"
    Write-Host "  Specific Mugirl namespace types: $namespaceSpecificTypeCount"
    Write-Host "  Specific Mugirl namespaces: $($namespaceSpecificNames.Count)"
    Write-Host "  Non-Mugirl namespace types: $namespaceNonMugirlTypeCount"
    Write-Host "  Patch xpath nodes: $xpathCount"
    Write-Host "  XML patch governance files: $xmlPatchGovernanceFiles"
    Write-Host "  XML patch governance rules: $xmlPatchGovernanceChecks"
    Write-Host "  Direct PatchOperationAdd targets: $checkedPatchTargets"
    Write-Host "  Direct PatchOperationAdd guarded targets: $directPatchAddGuardedTargets"
    Write-Host "  Direct PatchOperationAdd unguarded targets: $directPatchAddUnguardedTargets"
    Write-Host "  Direct PatchOperationAdd vanilla targets: $directPatchAddVanillaTargets"
    Write-Host "  Direct PatchOperationAdd mod targets: $directPatchAddModTargets"
    Write-Host "  Direct PatchOperationAdd optional integration targets: $directPatchAddOptionalIntegrationTargets"
    Write-Host "  Optional external PatchOperationAdd targets: $($externalPatchTargets.Count)"
    Write-Host "  Mod concrete defs: $($concreteModDefs.Count)"
    Write-Host "  Direct keyed keys: $($directKeys.Count)"
    Write-Host "  Broad keyed keys: $($broadKeys.Count)"
    Write-Host "  Language translation nodes: $languageTranslationNodes"
    Write-Host "  English to Chinese parity keys: $languageParityKeys"
    Write-Host "  Maintenance documentation rules: $architectureDocumentationChecks"
    Write-Host "  Text formatting safety rules: $textFormattingSafetyChecks"
    Write-Host "  Tick manager access safety rules: $tickManagerAccessSafetyChecks"
    Write-Host "  Find access safety rules: $findAccessSafetyChecks"
    Write-Host "  Current game access safety rules: $currentGameAccessSafetyChecks"
    Write-Host "  Def lookup safety rules: $defLookupSafetyChecks"
    Write-Host "  Dynamic recipe safety rules: $dynamicRecipeSafetyChecks"
    Write-Host "  Mugirl identity safety rules: $mugirlIdentitySafetyChecks"
    Write-Host "  Player faction helper safety rules: $playerFactionHelperSafetyChecks"
    Write-Host "  Restraint target safety rules: $restraintTargetSafetyChecks"
    Write-Host "  Restraint hediff state safety rules: $restraintHediffStateSafetyChecks"
    Write-Host "  Roping target safety rules: $ropingTargetSafetyChecks"
    Write-Host "  Mounting state safety rules: $mountingStateSafetyChecks"
    Write-Host "  Milk interaction safety rules: $milkInteractionSafetyChecks"
    Write-Host "  Incident interaction safety rules: $incidentInteractionSafetyChecks"
    Write-Host "  Harmony bootstrap safety rules: $harmonyBootstrapSafetyChecks"
    Write-Host "  Harmony boundary safety rules: $harmonyBoundarySafetyChecks"
    Write-Host "  Compatibility boundary safety rules: $compatibilityBoundarySafetyChecks"
    Write-Host "  Ability job safety rules: $abilityJobSafetyChecks"
    Write-Host "  Misc job safety rules: $miscJobSafetyChecks"
    Write-Host "  Apparel generation safety rules: $apparelGenerationSafetyChecks"
    Write-Host "  Lifecycle overrides checked: $lifecycleOverrideCount"
    Write-Host "  Apparel lifecycle safety rules: $apparelLifecycleSafetyChecks"
    Write-Host "  Generated apparel lock safety rules: $generatedApparelLockSafetyChecks"
    Write-Host "  Manual patch registry safety rules: $manualPatchRegistrySafetyChecks"
    Write-Host "  Patch metadata audit entries: $patchMetadataAuditEntries"
    Write-Host "  Patch metadata audit rules: $patchMetadataAuditChecks"
    Write-Host "  Static cache lifecycle fields: $staticCacheLifecycleFields"
    Write-Host "  Static cache lifecycle rules: $staticCacheLifecycleChecks"
    Write-Host "  High-risk no-default Scribe hits: $scribeStateDefaultCount"
    Write-Host "  Def types indexed: $($defsByType.Keys.Count)"
    Write-Host "  Optional DLC Def refs: $optionalDlcRefCount"
    Write-Host "  clipPath nodes: $clipPathCount"
    & (Join-Path $PSScriptRoot 'Invoke-TextureAssetValidation.ps1')
    Write-Host "  texture paths: $texturePathCount"
    Write-Host "  vanilla texture refs: $($vanillaTextureRefs.Count)"
}
finally {
    Pop-Location
}
