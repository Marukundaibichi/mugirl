param([string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe', [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# 只生成普通文件，不创建目录联接。
$project = Join-Path $modRoot 'TMP/LanceMotionBlurShaderBuild'
$editor = Join-Path $project 'Assets/Editor'
[void][IO.Directory]::CreateDirectory($editor)
[void][IO.Directory]::CreateDirectory((Join-Path $project 'ProjectSettings'))
[void][IO.Directory]::CreateDirectory((Join-Path $project 'Packages'))
[IO.File]::WriteAllText((Join-Path $project 'mod-root.txt'), $modRoot)
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 2022.3.62f3`n")
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{}}')
Copy-Item -LiteralPath (Join-Path $modRoot 'docs/lance-motion-blur/MugirlLanceMotionBlur.shader') -Destination (Join-Path $project 'Assets/MugirlLanceMotionBlur.shader')
Copy-Item -LiteralPath (Join-Path $modRoot 'docs/lance-motion-blur/BuildLanceMotionBlur.cs') -Destination $editor
$log = Join-Path $project $(if ($ValidateOnly) { 'validate.log' } else { 'build.log' })
$method = if ($ValidateOnly) { 'BuildLanceMotionBlur.Validate' } else { 'BuildLanceMotionBlur.Build' }
$arguments = @('-batchmode', '-quit', '-noUpm', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', $method, '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Shader build failed. See $log" }
Select-String -LiteralPath $log -Pattern 'MUGIRL_LANCE_BLUR_'
