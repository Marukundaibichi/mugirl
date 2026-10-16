Set-StrictMode -Version Latest

# 整个运行期保留写句柄，只共享读取：子脚本能验证令牌，其他等待者不能改写或删除锁。
function Read-MugirlLockText([string] $Path) {
    try {
        $reader = [System.IO.StreamReader]::new([System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite))
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    catch [System.IO.IOException] { return $null }
    catch [System.UnauthorizedAccessException] { return $null }
}

function Test-MugirlLockToken([string] $Text, [string] $Token) {
    return $Text -and $Text -cmatch ('(?m)^token=' + [regex]::Escape($Token) + '\r?$')
}

function Enter-MugirlInGameLock {
    param(
        [Parameter(Mandatory)][string] $Label,
        [ValidateRange(0, 86400)][int] $WaitSeconds = 0,
        [string] $Path = (Join-Path ([System.IO.Path]::GetTempPath()) 'mugirl-ingame.lock'),
        [string] $TokenVariable = 'MUGIRL_INGAME_LOCK_TOKEN'
    )
    $previousToken = [Environment]::GetEnvironmentVariable($TokenVariable)
    if ($previousToken -and (Test-MugirlLockToken (Read-MugirlLockText $Path) $previousToken)) {
        return @{ Owned = $false; Token = $previousToken }
    }
    $token = [guid]::NewGuid().ToString('N')
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes("label=$Label`npid=$PID`ntoken=$token`nstarted=$((Get-Date).ToString('o'))`n")
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    $announced = $false
    while ($true) {
        $stream = $null
        $content = $null
        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)
        }
        catch [System.IO.IOException] { }
        catch [System.UnauthorizedAccessException] { }
        if ($null -eq $stream) {
            $content = Read-MugirlLockText $Path
            $holder = if ($content -cmatch '(?m)^pid=(\d+)\r?$') { [int]$Matches[1] } else { $null }
            if ($null -ne $holder -and -not (Get-Process -Id $holder -ErrorAction SilentlyContinue)) {
                try {
                    # 两个等待者只能有一个取得写权限；读取和比较均在同一把文件锁内，接管后也不释放句柄。
                    $candidate = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)
                    $reader = [System.IO.StreamReader]::new($candidate, [System.Text.Encoding]::UTF8, $true, 4096, $true)
                    try { $current = $reader.ReadToEnd() } finally { $reader.Dispose() }
                    if ($current -ceq $content) { $stream = $candidate; $stream.Position = 0; $stream.SetLength(0) }
                    else { $candidate.Dispose() }
                }
                catch [System.IO.IOException] { }
                catch [System.UnauthorizedAccessException] { }
            }
        }
        if ($null -ne $stream) {
            try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
            catch { $stream.Dispose(); throw }
            [Environment]::SetEnvironmentVariable($TokenVariable, $token)
            return @{ Owned = $true; Token = $token; Stream = $stream; Path = $Path; TokenVariable = $TokenVariable; PreviousToken = $previousToken }
        }
        $shown = if ($content) { $content.Trim() -replace "`r?`n", '; ' } else { 'unreadable' }
        if ([DateTime]::UtcNow -ge $deadline) { throw "the in-game lock $Path is still held after $WaitSeconds s: $shown" }
        if (-not $announced) { Write-Host "waiting for the in-game lock $Path ($shown)" -ForegroundColor DarkYellow; $announced = $true }
        Start-Sleep -Milliseconds ([Math]::Min(1000, [Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)))
    }
}

function Exit-MugirlInGameLock($Lock) {
    if ($null -eq $Lock -or -not $Lock.Owned) { return }
    $Lock.Stream.Dispose()
    # 当前进程仍存活，等待者不能把刚释放句柄的文件当成残留锁接管。
    if (Test-MugirlLockToken (Read-MugirlLockText $Lock.Path) $Lock.Token) {
        try { [System.IO.File]::Delete($Lock.Path) }
        catch { Write-Warning "could not release the in-game lock $($Lock.Path): $_" }
    }
    if ([Environment]::GetEnvironmentVariable($Lock.TokenVariable) -ceq $Lock.Token) {
        [Environment]::SetEnvironmentVariable($Lock.TokenVariable, $Lock.PreviousToken)
    }
}

Export-ModuleMember -Function Enter-MugirlInGameLock, Exit-MugirlInGameLock
