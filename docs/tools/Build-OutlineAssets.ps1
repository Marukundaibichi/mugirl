param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $modRoot $(if ($ValidateOnly) { 'TMP/OutlineShaderValidation' } else { 'TMP/OutlineShaderBuild' })
$editor = Join-Path $project 'Assets/Editor'
[void][IO.Directory]::CreateDirectory($editor)
[void][IO.Directory]::CreateDirectory((Join-Path $project 'ProjectSettings'))
[void][IO.Directory]::CreateDirectory((Join-Path $project 'Packages'))
[IO.File]::WriteAllText((Join-Path $project 'mod-root.txt'), $modRoot)
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 2022.3.62f3`n")
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{}}')
Copy-Item -LiteralPath (Join-Path $modRoot 'docs/outline/MugirlExtraOutline.shader') -Destination (Join-Path $project 'Assets/MugirlExtraOutline.shader')
Copy-Item -LiteralPath (Join-Path $modRoot 'docs/outline/BuildOutlineAssets.cs') -Destination $editor
if ($ValidateOnly) {
    Copy-Item -LiteralPath (Join-Path $modRoot 'docs/outline/OutlineRenderValidation.cs') -Destination $editor
    Copy-Item -LiteralPath (Join-Path $modRoot '1.6/Source/Features/Appearance/MugirlExtraOutline.cs') -Destination $editor
    $method = 'OutlineRenderValidation.Run'
} else {
    $method = 'BuildOutlineAssets.Build'
}
$log = Join-Path $project 'build.log'
# Keep the helper hidden. GPU validation deliberately does not use -nographics.
$arguments = @('-batchmode', '-quit', '-noUpm', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
# Wait for the editor itself, not a licensing client that can outlive the build.
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 70
    throw "Unity failed with exit code $($process.ExitCode). Log: $log"
}
Select-String -LiteralPath $log -Pattern 'MUGIRL_OUTLINE_'
