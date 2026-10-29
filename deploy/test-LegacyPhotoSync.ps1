$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Sync-LegacyPhotos.ps1" -LibraryOnly
$root = Join-Path ([IO.Path]::GetTempPath()) ('ocr-photo-test-' + [guid]::NewGuid().ToString('N'))
$source = New-Item -ItemType Directory (Join-Path $root 'source')
$dest = New-Item -ItemType Directory (Join-Path $root 'dest')
$state = New-Item -ItemType Directory (Join-Path $root 'state')
$script:rows = @(@{EmpCode='102';Photo='/app/photos/102_123.jpg'})
$script:current = '/app/photos/102_123.jpg'
$passed = 0
function Assert($value, $name) { if (-not $value) { throw "FAIL: $name" }; $script:passed++; Write-Host "PASS: $name" }
function Run($seed) { Invoke-LegacyPhotoSync -SourceRoot $source.FullName -DestinationRoot $dest.FullName -StateRoot $state.FullName -GetRows { $script:rows } -GetCurrent { param($code) $script:current } -SeedEmployee $seed }
function Jpeg($name, $tail) { [IO.File]::WriteAllBytes((Join-Path $source $name), [byte[]](255,216,255,$tail)) }
try {
    Jpeg '102_123.jpg' 1
    $r = Run @()
    Assert ($r.Baselined -eq 1 -and -not (Test-Path "$dest/102.jpg")) 'baseline does not bulk copy'
    $r = Run @('102')
    Assert ($r.Copied -eq 1 -and (Test-Path "$dest/102.jpg")) 'explicit 102 repair'
    $r = Run @()
    Assert ($r.Copied -eq 0) 'idempotent'
    [IO.File]::WriteAllBytes("$dest/102.jpg", [byte[]](255,216,255,9))
    $r = Run @()
    Assert ($r.Copied -eq 0 -and [IO.File]::ReadAllBytes("$dest/102.jpg")[3] -eq 9) 'unchanged DB leaves legacy edit'
    Jpeg '102_124.jpg' 2
    $script:rows[0].Photo = '/app/photos/102_124.jpg'; $script:current = $script:rows[0].Photo
    $r = Run @()
    Assert ($r.Conflicts -eq 1 -and $r.Copied -eq 0) 'new DB version conflicts with edited legacy'
    $script:rows = @(@{EmpCode='103';Photo='/app/photos/103_1.jpg'})
    Jpeg '103_1.jpg' 3; $script:current = '/app/photos/103_2.jpg'
    $r = Run @()
    Assert ($r.Errors -eq 1 -and -not (Test-Path "$dest/103.jpg")) 'DB recheck rejects stale photo'
    $script:current = '/app/photos/103_1.jpg'; $r = Run @()
    Assert ($r.Copied -eq 1) 'new employee copies'
    $script:rows = @(@{EmpCode='104';Photo='/app/photos/../104_1.jpg'})
    $r = Run @()
    Assert ($r.Rejected -eq 1) 'traversal rejected'
    $script:rows = @(@{EmpCode='104';Photo='/app/photos/105_1.jpg'})
    $r = Run @(); Assert ($r.Rejected -eq 1) 'employee mismatch rejected'
    $script:rows = @(@{EmpCode='104';Photo='/app/photos/104_1.png'})
    [IO.File]::WriteAllBytes("$source/104_1.png", [byte[]](1,2,3,4)); $script:current=$script:rows[0].Photo
    $r=Run @(); Assert ($r.Errors -eq 1) 'invalid image signature rejected'
    $failed=$false; try { Invoke-LegacyPhotoSync -SourceRoot $source -DestinationRoot "$root/missing" -StateRoot $state -GetRows { @() } -GetCurrent { '' } } catch { $failed=$true }
    Assert $failed 'unavailable share fails without creating it'
    $script:rows = @(@{EmpCode='105';Photo='/app/photos/105_1.jpg'})
    Jpeg '105_1.jpg' 5; $script:current=$script:rows[0].Photo
    $publish = ${function:Publish-PhotoFile}
    function Publish-PhotoFile { throw 'Injected atomic publication failure' }
    $r=Run @(); Assert ($r.Errors -eq 1 -and -not (Test-Path "$dest/105.jpg")) 'atomic failure retains old target'
    Set-Item Function:Publish-PhotoFile $publish
    $r=Run @(); Assert ($r.Copied -eq 1) 'failed atomic publication retries'
    $script:rows = @(@{EmpCode='106';Photo='/app/photos/106_1.jpg'})
    Jpeg '106_1.jpg' 6; $script:current=$script:rows[0].Photo
    $save = ${function:Save-PhotoState}
    function Save-PhotoState {
        param($Path,$Value)
        if ($Path.EndsWith('state.json') -and $null -ne $Value.Pending) { throw 'Injected intent failure' }
        & $save $Path $Value
    }
    $r=Run @(); Assert ($r.Errors -eq 1 -and -not (Test-Path "$dest/106.jpg")) 'intent persistence failure prevents publication'
    Set-Item Function:Save-PhotoState $save
    $r=Run @(); Assert ($r.Copied -eq 1) 'intent failure retries'
    $script:rows = @(@{EmpCode='107';Photo='/app/photos/107_1.jpg'})
    Jpeg '107_1.jpg' 7; $script:current=$script:rows[0].Photo
    function Save-PhotoState {
        param($Path,$Value)
        if ($Path.EndsWith('state.json') -and $null -eq $Value.Pending -and $Value.Entries.ContainsKey('107')) { throw 'Injected acknowledgement failure' }
        & $save $Path $Value
    }
    $r=Run @(); Assert ($r.Errors -eq 1 -and (Test-Path "$dest/107.jpg")) 'postpublish state failure preserves pending intent'
    Set-Item Function:Save-PhotoState $save
    $r=Run @(); Assert ($r.Recovered -eq 1 -and $r.Copied -eq 0) 'postpublish recovery acknowledges matching hash'
    $script:rows = @(@{EmpCode='108';Photo='/app/photos/108_1.jpg'})
    Jpeg '108_1.jpg' 8; $script:current=$script:rows[0].Photo
    [IO.File]::WriteAllBytes("$dest/108.jpg", [byte[]](255,216,255,9))
    $r=Run @(); Assert ($r.Conflicts -eq 1 -and $r.Copied -eq 0) 'untracked existing legacy image conflicts'
    $script:rows = @(@{EmpCode='105';Photo='/app/photos/105_2.jpg'})
    Jpeg '105_2.jpg' 9; $script:current=$script:rows[0].Photo
    $r=Run @(); Assert ($r.Copied -eq 1 -and [IO.File]::ReadAllBytes("$dest/105.jpg")[3] -eq 9) 'existing target atomically replaced'
    $backups=@(Get-ChildItem -LiteralPath $dest -Force -Filter '.ocr-sync-backup-*.bak')
    Assert ($backups.Count -gt 0 -and @($backups | Where-Object { [IO.File]::ReadAllBytes($_.FullName)[3] -eq 5 }).Count -eq 1) 'replacement retains original backup'
    $state=New-Item -ItemType Directory (Join-Path $root 'legacy-state')
    $script:rows = @(@{EmpCode='108';Photo='\\svr120a\PhotoEmp$\108.jpg'})
    $r=Run @(); Assert ($r.Baselined -eq 1 -and $r.Copied -eq 0) 'legacy UNC reference baseline retained'
    $script:rows[0].Photo='/app/photos/108_1.jpg'; $script:current=$script:rows[0].Photo
    $r=Run @(); Assert ($r.Copied -eq 1) 'first web upload replaces unchanged legacy image'
    Assert ($null -ne (Get-LegacyPhotoDescriptor @{EmpCode='108';Photo='F:\108.jpg'})) 'mapped drive baseline supported'
    Assert ($null -eq (Get-LegacyPhotoDescriptor @{EmpCode='108';Photo='F:\109.jpg'})) 'legacy employee mismatch rejected'
    $script:rows[0].Photo='/app/photos/108_2.jpg'; $script:current=$script:rows[0].Photo
    Jpeg '108_2.jpg' 7
    $script:writerBlocked=$false
    function Publish-PhotoFile {
        param($Stage,$Target,$Backup)
        try { [IO.File]::WriteAllBytes($Target,[byte[]](255,216,255,6)) } catch { $script:writerBlocked=$true }
        & $publish $Stage $Target $Backup
        $tamper="$Backup.tamper"
        [IO.File]::WriteAllBytes($tamper,[byte[]](255,216,255,4))
        [IO.File]::Replace($tamper,$Backup,"$Backup.original")
    }
    $r=Run @(); Assert $script:writerBlocked 'destination guard denies concurrent in-place writer'
    Assert ($r.Errors -eq 1) 'unexpected replacement backup requires review'
    Set-Item Function:Publish-PhotoFile $publish
    $failed=$false; try { Run @() } catch { $failed=$true }
    Assert $failed 'replacement conflict remains blocked on next run'
    [IO.File]::WriteAllText("$state/state.json", 'not-json')
    $failed=$false; try { Run @() } catch { $failed=$true }
    Assert $failed 'malformed state fails closed'
    Write-Host "$passed tests passed."
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    $prefix=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\ocr-photo-test-'
    if (-not $resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^ocr-photo-test-[a-f0-9]{32}$') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
