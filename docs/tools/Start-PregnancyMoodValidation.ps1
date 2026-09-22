param([switch]$WithoutBiotech)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runName = (Get-Date -Format 'yyyyMMdd-HHmmss') + $(if ($WithoutBiotech) { '-NoBiotech' } else { '-Biotech' })
$stage = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('RimWorldModTests\Mugirl\PregnancyMoodValidation-' + $runName + '-Game')
$saveRoot = Join-Path $repository ('TMP\PregnancyMoodValidation-' + $runName)
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $saveRoot)) { throw 'Unique paths required.' }
$gameRoot = [IO.Path]::GetFullPath((Join-Path $repository '..\..'))
$workshop = [IO.Path]::GetFullPath((Join-Path $gameRoot '..\..\workshop\content\294100'))
$testMod = Join-Path $stage 'Mods\Mugirl'
$assemblies = Join-Path $testMod '1.6\Assemblies'
New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
foreach ($dir in @('Data', 'MonoBleedingEdge', 'RimWorldWin64_Data')) {
    New-Item -ItemType Junction -Path (Join-Path $stage $dir) -Target (Join-Path $gameRoot $dir) | Out-Null
}
foreach ($file in @('RimWorldWin64.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'steam_appid.txt')) {
    Copy-Item -LiteralPath (Join-Path $gameRoot $file) -Destination (Join-Path $stage $file)
}
New-Item -ItemType Junction -Path (Join-Path $stage 'Mods\Harmony') -Target (Join-Path $workshop '2009463077') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $stage 'Mods\AlienRace') -Target (Join-Path $workshop '839005762') | Out-Null
foreach ($dir in @('About', 'Bio_1.6', 'Odyssey_1.6', 'Versions', 'Sounds', 'Textures')) {
    New-Item -ItemType Junction -Path (Join-Path $testMod $dir) -Target (Join-Path $repository $dir) | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repository 'LoadFolders.xml') -Destination $testMod
foreach ($dir in Get-ChildItem -LiteralPath (Join-Path $repository '1.6') -Directory) {
    if ($dir.Name -in @('Assemblies', 'Source')) { continue }
    New-Item -ItemType Junction -Path (Join-Path $testMod ('1.6\' + $dir.Name)) -Target $dir.FullName | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repository '1.6\Assemblies\MugirlRace.dll') -Destination $assemblies
$managed = Join-Path $gameRoot 'RimWorldWin64_Data\Managed'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
& $csc /nologo /target:library /langversion:7.2 ("/out:$assemblies\PregnancyMoodProbe.dll") ("/reference:$assemblies\MugirlRace.dll") ("/reference:$managed\Assembly-CSharp.dll") ("/reference:$managed\UnityEngine.CoreModule.dll") ("/reference:$managed\netstandard.dll") ("/reference:$workshop\2009463077\Current\Assemblies\0Harmony.dll") (Join-Path $PSScriptRoot 'PregnancyMoodProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
New-Item -ItemType Directory -Path (Join-Path $saveRoot 'Config') -Force | Out-Null
$dlc = if ($WithoutBiotech) { @() } else { @('ludeon.rimworld.biotech') }
$active = @('brrainz.harmony', 'ludeon.rimworld') + $dlc + @('erdelf.humanoidalienraces', 'har.mugirlrace')
$activeXml = ($active | ForEach-Object { '<li>' + $_ + '</li>' }) -join ''
$encoding = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\ModsConfig.xml'), ('<ModsConfigData><version>1.6.4871 rev591</version><activeMods>' + $activeXml + '</activeMods><knownExpansions><li>ludeon.rimworld.biotech</li></knownExpansions></ModsConfigData>'), $encoding)
[IO.File]::WriteAllText((Join-Path $saveRoot 'Config\Prefs.xml'), '<PrefsData><langFolderName>ChineseSimplified (简体中文)</langFolderName><runInBackground>true</runInBackground><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><fullscreen>false</fullscreen></PrefsData>', $encoding)
$arguments = @('-mugirlPregnancyMoodProbe', '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '480', ('-savedatafolder="' + $saveRoot + '"'), ('-logFile "' + (Join-Path $saveRoot 'Player.log') + '"'))
if ($WithoutBiotech) { $arguments += '-mugirlPregnancyNoBiotech' }
$process = Start-Process -FilePath (Join-Path $stage 'RimWorldWin64.exe') -WorkingDirectory $stage -ArgumentList $arguments -WindowStyle Hidden -PassThru
[PSCustomObject]@{ ProcessId = $process.Id; SaveRoot = $saveRoot; Stage = $stage } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $saveRoot 'run.json')
