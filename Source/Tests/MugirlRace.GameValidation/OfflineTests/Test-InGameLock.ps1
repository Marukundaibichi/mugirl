param([string] $ModulePath, [string] $Work, [string] $Result, [switch] $Worker)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $ModulePath) { $ModulePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../tools/Mugirl.InGameLock.psm1')) }
Import-Module $ModulePath -Force
$tokenVariable = 'MUGIRL_A5_TEST_LOCK_TOKEN'
if ($Worker) {
    $lease = Enter-MugirlInGameLock -Label 'offline worker' -Path (Join-Path $Work 'test.lock') -TokenVariable $tokenVariable -WaitSeconds 10
    try {
        $start = [DateTime]::UtcNow.Ticks
        Start-Sleep -Milliseconds 350
        @{ owned = $lease.Owned; start = $start; end = [DateTime]::UtcNow.Ticks } | ConvertTo-Json | Set-Content -LiteralPath $Result
    }
    finally { Exit-MugirlInGameLock $lease }
    exit 0
}

$Work = Join-Path ([IO.Path]::GetTempPath()) ('mugirl-lock-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Work | Out-Null
New-Item -ItemType File -Path (Join-Path $Work '.mugirl-lock-test') | Out-Null
$lockPath = Join-Path $Work 'test.lock'
$originalToken = [Environment]::GetEnvironmentVariable($tokenVariable)
$lease = $null
function Start-Worker([string] $Name) {
    $arguments = @('-NoProfile', '-File', $PSCommandPath, '-Worker', '-ModulePath', $ModulePath, '-Work', $Work, '-Result', (Join-Path $Work "$Name.json"))
    $line = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    return Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $line -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $Work "$Name.stdout") -RedirectStandardError (Join-Path $Work "$Name.stderr")
}
function Wait-Worker($Process) {
    if (-not $Process.WaitForExit(20000)) { $Process.Kill($true); throw 'offline lock test worker timed out' }
    if ($Process.ExitCode -ne 0) { throw "offline lock worker failed ($($Process.ExitCode)); evidence $Work" }
}
try {
    [Environment]::SetEnvironmentVariable($tokenVariable, $null)
    $lease = Enter-MugirlInGameLock -Label 'offline parent' -Path $lockPath -TokenVariable $tokenVariable
    $nested = Enter-MugirlInGameLock -Label 'nested' -Path $lockPath -TokenVariable $tokenVariable
    if ($nested.Owned) { throw 'nested call failed to inherit lease' }
    Exit-MugirlInGameLock $nested
    $inherited = Start-Worker 'inherited'
    Wait-Worker $inherited
    if ((Get-Content (Join-Path $Work 'inherited.json') -Raw | ConvertFrom-Json).owned) { throw 'child failed to inherit lease' }
    [Environment]::SetEnvironmentVariable($tokenVariable, $lease.Token.Substring(0, 12))
    $rejected = $false
    try { $null = Enter-MugirlInGameLock -Label 'prefix must fail' -Path $lockPath -TokenVariable $tokenVariable }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'token prefix incorrectly inherited lease' }
    [Environment]::SetEnvironmentVariable($tokenVariable, $lease.Token)
    Exit-MugirlInGameLock $lease
    $lease = $null
    if (Test-Path -LiteralPath $lockPath) { throw 'release left lock file behind' }
    [IO.File]::WriteAllText($lockPath, "pid=2147483647`ntoken=stale`n")
    $first = Start-Worker 'first'
    $second = Start-Worker 'second'
    Wait-Worker $first
    Wait-Worker $second
    $a = Get-Content (Join-Path $Work 'first.json') -Raw | ConvertFrom-Json
    $b = Get-Content (Join-Path $Work 'second.json') -Raw | ConvertFrom-Json
    if (-not $a.owned -or -not $b.owned -or ($a.start -lt $b.end -and $b.start -lt $a.end)) { throw 'stale takeover allowed overlapping owners' }
    if (Test-Path -LiteralPath $lockPath) { throw 'concurrent release left lock file behind' }
    Write-Host "PASS: nested inheritance, child inheritance, exact token, release, concurrent stale takeover; evidence $Work"
}
finally {
    Exit-MugirlInGameLock $lease
    [Environment]::SetEnvironmentVariable($tokenVariable, $originalToken)
}
