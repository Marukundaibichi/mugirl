param(
    [ValidateSet('Economy', 'Visual', 'WithoutIdeology')][string]$Mode = 'Economy',
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName,
    [switch]$SkipCompact
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
# 含目录联接的测试游戏必须位于所有模组目录之外，避免 RimSort #2450。
$temporaryRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RimWorldModTests\Mugirl'
$stage = Join-Path $temporaryRoot ('CorporateValidation-' + $RunName + '-Game')
$saveRoot = if ($Mode -eq 'Visual') {
    [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ('CorporateValidation-' + $RunName)))
} else {
    Join-Path $stage ('Mods\Mugirl\TMP\CorporateValidation-' + $RunName)
}
if (Test-Path -LiteralPath $saveRoot) { throw 'Use a new RunName so old results cannot be mistaken for this run.' }
if (Test-Path -LiteralPath $stage) { throw 'Use a new RunName so an existing game stage is not overwritten.' }

# Build a separate mod and engine stage. Validation assemblies never replace the
# live mod DLL, even while the player has a normal game process open.
$gameRoot = [System.IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$testMod = Join-Path $stage 'Mods\Mugirl'
$testAssemblies = Join-Path $testMod '1.6\Assemblies'
New-Item -ItemType Directory -Path $testAssemblies -Force | Out-Null
foreach ($directory in @('Data', 'MonoBleedingEdge', 'RimWorldWin64_Data')) {
    New-Item -ItemType Junction -Path (Join-Path $stage $directory) -Target (Join-Path $gameRoot $directory) | Out-Null
}
foreach ($file in @('RimWorldWin64.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $gameRoot $file) -Destination (Join-Path $stage $file)
}
$workshop = [System.IO.Path]::GetFullPath((Join-Path $gameRoot '..\..\workshop\content\294100'))
New-Item -ItemType Junction -Path (Join-Path $stage 'Mods\Harmony') -Target (Join-Path $workshop '2009463077') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $stage 'Mods\AlienRace') -Target (Join-Path $workshop '839005762') | Out-Null
foreach ($directory in @('About', 'Bio_1.6', 'Odyssey_1.6', 'Versions', 'Sounds', 'Textures')) {
    New-Item -ItemType Junction -Path (Join-Path $testMod $directory) -Target (Join-Path $repository $directory) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repository 'LoadFolders.xml') -Destination $testMod
foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $repository '1.6') -Directory) {
    if ($directory.Name -in @('Assemblies', 'Source')) { continue }
    New-Item -ItemType Junction -Path (Join-Path $testMod ('1.6\' + $directory.Name)) -Target $directory.FullName | Out-Null
}
$msbuild = (Get-Command MSBuild.exe -ErrorAction Stop).Source
& $msbuild (Join-Path $repository '1.6\Source\MugirlRace.csproj') /t:Rebuild /p:Configuration=Release /p:EnableCorporateValidation=true "/p:OutputPath=$testAssemblies/" "/p:IntermediateOutputPath=$stage/obj/" /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated validation build failed.' }

New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey')
$knownIds = @($expansions | ForEach-Object { 'ludeon.rimworld.' + $_ })
if ($Mode -eq 'WithoutIdeology') { $expansions = $expansions | Where-Object { $_ -ne 'ideology' } }
$expansionIds = @($expansions | ForEach-Object { 'ludeon.rimworld.' + $_ })
$active = @('brrainz.harmony', 'ludeon.rimworld') + $expansionIds + @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($knownIds | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[System.IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$flag = if ($Mode -eq 'Visual') { '-mugirlCorporateVisual' } else { '-mugirlCorporateChecks' }
$arguments = @('-quicktest', $flag, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
if ($Mode -eq 'Visual' -and -not $SkipCompact) { $arguments += '-mugirlCorporateVisualCompact' }
if ($Mode -eq 'WithoutIdeology') { $arguments += '-mugirlCorporateNoIdeology' }
# Each driver also checks its explicit flag and actual save path inside the game.
$process = Start-Process -FilePath (Join-Path $stage 'RimWorldWin64.exe') -WorkingDirectory $stage -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[PSCustomObject]@{ Mode = $Mode; ProcessId = $process.Id; SaveRoot = $saveRoot; GameStage = $stage }
