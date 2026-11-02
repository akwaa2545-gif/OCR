$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($identity.Name -ine 'KEMET\2172172205529') { throw 'Unexpected account.' }
$root = '\\svr120a\PhotoEmp$'
$result = [ordered]@{ Account = $identity.Name; ShareReadable = $false; ShareWritable = $false; AtomicReplaceSupported = $false; ProbeRemoved = $false; Employee102Exists = $false; Employee102Hash = $null; ErrorCategory = $null }
$probe = Join-Path $root ('.ocr-access-probe-' + [guid]::NewGuid().ToString('N') + '.tmp')
$stage = $probe + '.stage'
$backup = $probe + '.backup'
$created = $false
$stageCreated = $false
$replacementAttempted = $false
try {
    $null = Get-Item -LiteralPath $root -ErrorAction Stop
    $result.ShareReadable = $true
    $stream = [IO.File]::Open($probe, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $created = $true
    try { $stream.WriteByte(0); $stream.Flush() } finally { $stream.Dispose() }
    $result.ShareWritable = $true
    if ([IO.File]::Exists($stage) -or [IO.File]::Exists($backup)) { throw 'Probe staging collision.' }
    $stream = [IO.File]::Open($stage, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $stageCreated = $true
    try { $stream.WriteByte(1); $stream.Flush() } finally { $stream.Dispose() }
    $guard = [IO.File]::Open($probe, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::Read -bor [IO.FileShare]::Delete))
    try {
        $replacementAttempted = $true
        [IO.File]::Replace($stage, $probe, $backup)
    } finally { $guard.Dispose() }
    $result.AtomicReplaceSupported = ([IO.File]::ReadAllBytes($probe)[0] -eq 1 -and [IO.File]::ReadAllBytes($backup)[0] -eq 0)
    $photo = Join-Path $root '102.jpg'
    if (Test-Path -LiteralPath $photo -PathType Leaf) {
        $result.Employee102Exists = $true
        $result.Employee102Hash = (Get-FileHash -LiteralPath $photo -Algorithm SHA256 -ErrorAction Stop).Hash
    }
} catch {
    $result.ErrorCategory = $_.CategoryInfo.Category.ToString()
} finally {
    if ($created) {
        try {
            if ($stageCreated -and [IO.File]::Exists($stage)) { Remove-Item -LiteralPath $stage -Force -ErrorAction Stop }
            if ($replacementAttempted -and [IO.File]::Exists($backup)) { Remove-Item -LiteralPath $backup -Force -ErrorAction Stop }
            Remove-Item -LiteralPath $probe -Force -ErrorAction Stop
            $result.ProbeRemoved = $true
        }
        catch { $result.ErrorCategory = 'ProbeCleanupFailed' }
    }
    [IO.File]::WriteAllText('C:\ocr-deploy\legacy-share-access.json', ($result | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}
if (-not $result.ShareWritable -or -not $result.AtomicReplaceSupported -or -not $result.ProbeRemoved) { exit 1 }
