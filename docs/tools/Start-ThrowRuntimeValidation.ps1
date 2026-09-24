param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+$')][string]$RunName
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$externalRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'RimWorldModTests\Mugirl'))
$stage = [IO.Path]::GetFullPath((Join-Path $externalRoot ('ThrowValidation-' + $RunName + '-Game')))
if (-not $stage.StartsWith($externalRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid external test game path.'
}
if (Test-Path -LiteralPath $stage) { throw 'Use a fresh RunName.' }

$gameRoot = [IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$workshop = [IO.Path]::GetFullPath((Join-Path $gameRoot '..\..\workshop\content\294100'))
$testMod = Join-Path $stage 'Mods\Mugirl'
$assemblies = Join-Path $testMod '1.6\Assemblies'
$saveRoot = Join-Path $testMod ('TMP\ThrowValidation-' + $RunName)

# 含联接的游戏环境仅位于 RimSort 扫描目录之外。独立输出和 obj 都留在该测试环境。
New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
foreach ($directory in @('Data', 'MonoBleedingEdge', 'RimWorldWin64_Data')) {
    New-Item -ItemType Junction -Path (Join-Path $stage $directory) -Target (Join-Path $gameRoot $directory) | Out-Null
}
foreach ($file in @('RimWorldWin64.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $gameRoot $file) -Destination (Join-Path $stage $file)
}
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
& $msbuild (Join-Path $repository '1.6\Source\MugirlRace.csproj') /t:Rebuild /p:Configuration=Release /p:EnableThrowValidation=true "/p:OutputPath=$assemblies/" "/p:IntermediateOutputPath=$stage/obj/" /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Isolated throw validation build failed.' }

New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$expansions = @('royalty', 'ideology', 'biotech', 'anomaly', 'odyssey') | ForEach-Object { 'ludeon.rimworld.' + $_ }
$active = @('brrainz.harmony', 'ludeon.rimworld') + $expansions + @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$knownXml = ($expansions | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'),
    ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions>' + $knownXml + '</knownExpansions></ModsConfigData>'), $encoding)
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'),
    '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$arguments = @('-quicktest', '-mugirlThrowChecks', '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
    ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
$process = Start-Process -FilePath (Join-Path $stage 'RimWorldWin64.exe') -WorkingDirectory $stage -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.Id | Set-Content -LiteralPath (Join-Path $saveRoot 'process-id.txt')
[pscustomobject]@{ ProcessId = $process.Id; SaveRoot = $saveRoot; Stage = $stage }
