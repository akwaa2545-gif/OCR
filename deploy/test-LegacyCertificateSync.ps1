$ErrorActionPreference='Stop'
. "$PSScriptRoot/Sync-LegacyCertificates.ps1" -LibraryOnly
$script:passed=0
function Check($condition,$message) { if (-not $condition) { throw $message }; $script:passed++ }
function Pdf($path,$text='one') {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path,"%PDF-1.4`n$text`n%%EOF`n")
    [IO.File]::SetLastWriteTimeUtc($path,[datetime]::UtcNow.AddMinutes(-2))
}
$root=Join-Path ([IO.Path]::GetTempPath()) ('ocr-cert-test-'+[guid]::NewGuid().ToString('N'))
try {
    $a=Join-Path $root 'a'; $b=Join-Path $root 'b'; $s=Join-Path $root 'state'
    foreach($p in @($a,$b,$s)) { [IO.Directory]::CreateDirectory($p)|Out-Null }
    Pdf "$a/102/nested/a.pdf"; Pdf "$b/103/b.pdf" 'two'
    $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s
    Check ([IO.File]::Exists("$b/102/nested/a.pdf")) 'Nested legacy certificate was not imported'
    Check ([IO.File]::Exists("$a/103/b.pdf")) 'Web certificate was not exported'
    Check ($r.Copied -eq 2) 'Wrong copy count'
    $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s
    Check ($r.Copied -eq 0) 'Second run was not idempotent'
    Pdf "$b/102/nested/a.pdf" 'different'
    $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s
    Check ($r.Conflicts -gt 0) 'Conflict not reported'
    Check (([IO.File]::ReadAllText("$a/102/nested/a.pdf")) -notmatch 'different') 'Conflict overwrote source'
    [IO.File]::Delete("$b/103/b.pdf")
    $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s
    Check (-not [IO.File]::Exists("$b/103/b.pdf")) 'Deleted certificate was resurrected'
    Check ($r.DeletionsDeferred -gt 0) 'Deletion was not reported'
    [IO.File]::WriteAllText("$a/partial.pdf",'%PDF-1.4 partial')
    [IO.File]::SetLastWriteTimeUtc("$a/partial.pdf",[datetime]::UtcNow.AddMinutes(-2))
    Pdf "$a/new.pdf"; [IO.File]::SetLastWriteTimeUtc("$a/new.pdf",[datetime]::UtcNow)
    Pdf "$a/busy.pdf"
    $busy=[IO.File]::Open("$a/busy.pdf",[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try { $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s }
    finally { $busy.Dispose() }
    Check (-not [IO.File]::Exists("$b/partial.pdf")) 'Partial PDF copied'
    Check (-not [IO.File]::Exists("$b/new.pdf")) 'Unstable PDF copied'
    Check (-not [IO.File]::Exists("$b/busy.pdf")) 'Busy PDF copied'
    Check ($r.Invalid -gt 0 -and $r.Deferred -gt 0) 'Partial/busy status missing'
    [IO.File]::WriteAllText("$a/ignore.tmp",'not a pdf')
    for($i=0;$i -lt 8;$i++) { Pdf "$a/batch/$i.pdf" }
    $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s -MaxFiles 2
    Check ($r.Examined -le 2) 'File count bound exceeded'
    Check ($r.Backlog -gt 0) 'Durable backlog missing'
    for($i=0;$i -lt 40 -and -not [IO.File]::Exists("$b/batch/7.pdf");$i++) {
        $r=Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s -MaxFiles 3
    }
    Check ([IO.File]::Exists("$b/batch/7.pdf")) 'Batch cursor did not progress'
    Check (-not [IO.File]::Exists("$b/ignore.tmp")) 'Unsupported file copied'
    $thrown=$false; try { Resolve-CertificatePath $a '../escape.pdf' | Out-Null } catch { $thrown=$true }
    Check $thrown 'Traversal accepted'
    $thrown=$false; try { Resolve-CertificatePath $a 'file.pdf:stream' | Out-Null } catch { $thrown=$true }
    Check $thrown 'Alternate stream accepted'
    $held=[IO.File]::Open("$s/sync.lock",[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $thrown=$false; try { Invoke-LegacyCertificateSync -LegacyRoot $a -WebRoot $b -StateRoot $s | Out-Null } catch { $thrown=$true }
        Check $thrown 'Concurrent process lock not enforced'
    } finally { $held.Dispose() }
    $x=Join-Path $root 'import-source'; $y=Join-Path $root 'import-target'; $z=Join-Path $root 'import-state'
    foreach($p in @($x,$y,$z)) { [IO.Directory]::CreateDirectory($p)|Out-Null }
    Pdf "$x/from-old.pdf"; Pdf "$y/never-reverse.pdf"
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay
    Check ([IO.File]::Exists("$y/from-old.pdf")) 'One way import failed'
    Check (-not [IO.File]::Exists("$x/never-reverse.pdf")) 'One way mode copied backwards'
    [IO.File]::SetLastWriteTimeUtc("$y/from-old.pdf",[datetime]::UtcNow.AddMinutes(-2))
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay
    Check ($r.ReadBytes -eq 0) 'Unchanged pair was rehashed'
    Pdf "$x/over-budget.pdf"
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay -MaxBytes 1
    Check (-not [IO.File]::Exists("$y/over-budget.pdf")) 'Copy exceeded byte budget'
    Check ($r.ReadBytes -le 1 -and $r.Oversized -gt 0) 'Oversized file/budget reporting incorrect'
    $state=[IO.File]::ReadAllText("$z/state.json")|ConvertFrom-Json
    $state.Pending=[pscustomobject]@{Relative='over-budget.pdf'}
    Save-CertificateState "$z/state.json" $state
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay
    Check (-not [IO.File]::Exists("$y/over-budget.pdf")) 'Uncertain pending publication resurrected missing target'
    Check ($r.DeletionsDeferred -gt 0) 'Uncertain pending state not reported'
    $junction=Join-Path $x 'escape'
    New-Item -ItemType Junction -Path $junction -Target $y|Out-Null
    try {
        $thrown=$false; try { Resolve-CertificatePath $x 'escape/from-old.pdf'|Out-Null } catch { $thrown=$true }
        Check $thrown 'Reparse path accepted'
    } finally { [IO.Directory]::Delete($junction) }
    Pdf "$x/journal.pdf"
    [IO.File]::WriteAllText("$z/seen-journal.jsonl",'{"Relative":"journal.pdf","Side":"B"}'+"`n")
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay
    Check (-not [IO.File]::Exists("$y/journal.pdf")) 'Crash observation journal lost deletion protection'
    Pdf "$x/recent/new.pdf"
    $r=Invoke-LegacyCertificateSync -LegacyRoot $x -WebRoot $y -StateRoot $z -OneWay -PrioritizeRecent -MaxFiles 1
    Check ([IO.File]::Exists("$y/recent/new.pdf")) 'Recent employee folder did not get priority'
    $raceA=Join-Path $root 'race-a'; $raceB=Join-Path $root 'race-b'; $raceState=Join-Path $root 'race-state'
    foreach($p in @($raceA,$raceB,$raceState)) { [IO.Directory]::CreateDirectory($p)|Out-Null }
    Pdf "$raceA/first.pdf"; Pdf "$raceA/second.pdf"
    $publish=(Get-Item Function:Publish-CertificateStage).ScriptBlock
    Set-Item Function:Publish-CertificateStage -Value {
        param($Stage,$Destination)
        [IO.File]::WriteAllText($Destination,"%PDF-1.4`nracing writer`n%%EOF`n")
        throw [IO.IOException]::new('Simulated destination race')
    }
    try { $r=Invoke-LegacyCertificateSync -LegacyRoot $raceA -WebRoot $raceB -StateRoot $raceState -OneWay }
    finally { Set-Item Function:Publish-CertificateStage -Value $publish }
    $pending=([IO.File]::ReadAllText("$raceState/state.json")|ConvertFrom-Json).Pending
    Check ($null -ne $pending) 'Uncertain publication intent was lost'
    Check (@([IO.Directory]::GetFiles($raceB,'*.pdf')).Count -eq 1) 'Batch continued after uncertain publication'
    Check (([IO.File]::ReadAllText((Join-Path $raceB $pending.Relative))) -match 'racing writer') 'Racing destination was overwritten'
    "PASS: $script:passed certificate sync checks"
} finally {
    $resolved=[IO.Path]::GetFullPath($root)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -match '^ocr-cert-test-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
