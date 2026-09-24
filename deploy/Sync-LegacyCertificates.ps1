[CmdletBinding()]
param([switch]$LibraryOnly, [ValidateRange(1,10000)][int]$MaxFiles=2000,
      [ValidateRange(1048576,2147483647)][long]$MaxBytes=402653184,
      [ValidateRange(1,120)][int]$MaxSeconds=45)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

if (-not ('OcrCertificateFileIdentity' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class OcrCertificateFileIdentity {
    [StructLayout(LayoutKind.Sequential)] struct Info {
        public uint Attr, CreateLow, CreateHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
    public static string Read(SafeFileHandle handle) {
        Info info;
        if (!GetFileInformationByHandle(handle, out info)) throw new System.IO.IOException("File identity unavailable.");
        return String.Format("{0}:{1}:{2}:{3}:{4}:{5}:{6}", info.Volume, info.IndexHigh, info.IndexLow,
            info.SizeHigh, info.SizeLow, info.WriteHigh, info.WriteLow);
    }
}
'@
}

function Assert-CertificatePath {
    param([string]$Path)
    $cursor=[IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item=Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse path rejected.' }
        }
        $parent=[IO.Path]::GetDirectoryName($cursor.TrimEnd('\'))
        if ($parent -eq $cursor) { break }; $cursor=$parent
    }
}

function Resolve-CertificatePath {
    param([string]$Root,[string]$Relative)
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    if ($Relative) {
        if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '[:\x00-\x1f]' ) { throw 'Unsafe relative path.' }
        foreach($part in ($Relative -split '[\\/]')) {
            if ($part -in @('','.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or
                $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') { throw 'Unsafe path component.' }
        }
    }
    $path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
    if ($path -ne $base -and -not $path.StartsWith($base+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped root.' }
    Assert-CertificatePath $path
    return $path
}

function Save-CertificateState {
    param([string]$Path,$Value)
    Assert-CertificatePath $Path
    $temporary="$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary,($Value|ConvertTo-Json -Depth 15),(New-Object Text.UTF8Encoding($false)))
        $flush=[IO.File]::Open($temporary,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        try { $flush.Flush($true) } finally { $flush.Dispose() }
        Assert-CertificatePath $Path
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary,$Path,[NullString]::Value) }
        else { [IO.File]::Move($temporary,$Path) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-CertificateHash {
    param([IO.Stream]$Stream,$Budget)
    $Stream.Position=0; $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $buffer=New-Object byte[] 65536
        while ($Stream.Position -lt $Stream.Length) {
            $remaining=$Budget.Limit-$Budget.ReadBytes
            if ($remaining -le 0) { throw [TimeoutException]::new('Hash byte budget reached.') }
            $read=$Stream.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining))
            if ($read -eq 0) { throw [IO.IOException]::new('Unexpected end of source.') }
            $Budget.ReadBytes+=$read
            if ($Budget.ReadBytes -gt $Budget.Limit -or $Budget.Watch.Elapsed.TotalSeconds -ge $Budget.Seconds) { throw [TimeoutException]::new('Hash budget reached.') }
            [void]$sha.TransformBlock($buffer,0,$read,$buffer,0)
        }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0),0,0)
        return [BitConverter]::ToString($sha.Hash).Replace('-','')
    }
    finally { $sha.Dispose(); $Stream.Position=0 }
}

function Test-CertificatePdf {
    param([IO.Stream]$Stream)
    if ($Stream.Length -lt 12) { return $false }
    $head=New-Object byte[] 5; $Stream.Position=0
    if ($Stream.Read($head,0,5) -ne 5 -or [Text.Encoding]::ASCII.GetString($head) -cne '%PDF-') { return $false }
    $tail=New-Object byte[] ([int][Math]::Min(2048,$Stream.Length))
    $Stream.Position=$Stream.Length-$tail.Length
    $count=$Stream.Read($tail,0,$tail.Length); $Stream.Position=0
    return [Text.Encoding]::ASCII.GetString($tail,0,$count) -match '%%EOF[\x00\x09\x0a\x0c\x0d\x20]*$'
}

function Add-CertificateSeen {
    param($State,[string]$Relative,[string]$Side,[string]$Journal)
    if ($State.Seen[$Relative][$Side]) { return }
    Assert-CertificatePath $Journal
    $bytes=[Text.Encoding]::UTF8.GetBytes((@{Relative=$Relative;Side=$Side}|ConvertTo-Json -Compress)+"`n")
    $stream=[IO.File]::Open($Journal,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    $State.Seen[$Relative][$Side]=$true
}

function Publish-CertificateStage {
    param([string]$Stage,[string]$Destination)
    [IO.File]::Move($Stage,$Destination)
}

function Invoke-LegacyCertificateSync {
    param([string]$LegacyRoot,[string]$WebRoot,[string]$StateRoot,
          [ValidateRange(1,10000)][int]$MaxFiles=2000,
          [ValidateRange(1,2147483647)][long]$MaxBytes=402653184,
          [ValidateRange(1,120)][int]$MaxSeconds=45,[switch]$OneWay,[switch]$PrioritizeRecent)
    $roots=@([IO.Path]::GetFullPath($LegacyRoot).TrimEnd('\'),[IO.Path]::GetFullPath($WebRoot).TrimEnd('\'))
    foreach($p in @($roots[0],$roots[1],$StateRoot)) {
        if (-not [IO.Directory]::Exists($p)) { throw 'Required root unavailable.' }; Assert-CertificatePath $p
    }
    $allRoots=@($roots[0],$roots[1],[IO.Path]::GetFullPath($StateRoot).TrimEnd('\'))
    foreach($x in 0..2) { foreach($y in 0..2) {
        if ($x -ne $y -and ($allRoots[$x] -eq $allRoots[$y] -or $allRoots[$x].StartsWith($allRoots[$y]+'\',[StringComparison]::OrdinalIgnoreCase))) { throw 'Roots must be separate.' }
    } }
    $lock=$null; $iterators=@($null,$null)
    try {
        $lockPath=Resolve-CertificatePath $StateRoot 'sync.lock'
        $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $statePath=Resolve-CertificatePath $StateRoot 'state.json'
        $state=@{Version=1; Roots=$roots; OneWay=[bool]$OneWay; Seen=@{}; Queues=@(@(),@()); Turn=0; Pending=$null}
        if ([IO.File]::Exists($statePath)) {
            $raw=[IO.File]::ReadAllText($statePath)|ConvertFrom-Json
            if ($raw.Version -ne 1 -or $raw.Roots[0] -ne $roots[0] -or $raw.Roots[1] -ne $roots[1] -or [bool]$raw.OneWay -ne [bool]$OneWay) { throw 'State roots/version/mode mismatch.' }
            foreach($p in $raw.Seen.PSObject.Properties) {
                $cached=$null; if ($p.Value.PSObject.Properties.Name -contains 'Cache') { $cached=$p.Value.Cache }
                $state.Seen[$p.Name]=@{A=[bool]$p.Value.A;B=[bool]$p.Value.B;Cache=$cached}
            }
            $state.Queues=@(@($raw.Queues[0]),@($raw.Queues[1])); $state.Turn=[int]$raw.Turn; $state.Pending=$raw.Pending
        }
        if ($state.Turn -notin @(0,1)) { throw 'Invalid state cursor.' }
        $journal=Resolve-CertificatePath $StateRoot 'seen-journal.jsonl'
        if ([IO.File]::Exists($journal)) {
            foreach($line in [IO.File]::ReadLines($journal)) {
                $record=$line|ConvertFrom-Json
                Resolve-CertificatePath $roots[0] ([string]$record.Relative)|Out-Null
                if ($record.Side -notin @('A','B')) { throw 'Invalid observation journal.' }
                if (-not $state.Seen.ContainsKey($record.Relative)) { $state.Seen[$record.Relative]=@{A=$false;B=$false} }
                $state.Seen[$record.Relative][$record.Side]=$true
            }
        }
        # An interrupted publish may have completed and then been deleted. Never
        # guess: remember both sides, so an absent target needs operator review.
        if ($null -ne $state.Pending) {
            $relative=[string]$state.Pending.Relative
            Resolve-CertificatePath $roots[0] $relative | Out-Null
            $state.Seen[$relative]=@{A=$true;B=$true}; $state.Pending=$null
            Save-CertificateState $statePath $state
        }
        $queues=@((New-Object Collections.ArrayList),(New-Object Collections.ArrayList))
        foreach($direction in 0..1) {
            foreach($frame in $state.Queues[$direction]) { if ($null -ne $frame) { [void]$queues[$direction].Add(@{Relative=[string]$frame.Relative;Offset=[long]$frame.Offset}) } }
            if ($OneWay -and $direction -eq 1) { $queues[$direction].Clear() }
            elseif ($queues[$direction].Count -eq 0) { [void]$queues[$direction].Add(@{Relative='';Offset=0}) }
        }
        $result=@{Copied=0;ToLegacy=0;ToWeb=0;Bytes=0L;Examined=0;Conflicts=0;DeletionsDeferred=0;Deferred=0;Oversized=0;Invalid=0;Errors=0;Backlog=0;Details=@();RunUtc=[datetime]::UtcNow.ToString('o')}
        $watch=[Diagnostics.Stopwatch]::StartNew(); $done=@($false,[bool]$OneWay)
        $budget=@{ReadBytes=0L;Limit=$MaxBytes;Watch=$watch;Seconds=$MaxSeconds}
        # Prioritize recently changed employee directories without discarding the
        # durable initial-import cursor. Nested-only changes still use the crawl.
        if ($PrioritizeRecent) {
            foreach($direction in 0..1) {
                if ($OneWay -and $direction -eq 1) { continue }
                $recent=@(); $directoryIterator=$null
                try {
                    $directoryIterator=[IO.Directory]::EnumerateDirectories($roots[$direction]).GetEnumerator()
                    while ($watch.Elapsed.TotalSeconds -lt ($MaxSeconds/4) -and $directoryIterator.MoveNext()) {
                        $candidate=[string]$directoryIterator.Current
                        $modified=[IO.Directory]::GetLastWriteTimeUtc($candidate)
                        if ($modified -ge [datetime]::UtcNow.AddMinutes(-10)) {
                            $recent+=@{Relative=$candidate.Substring($roots[$direction].Length+1);Modified=$modified}
                            $recent=@($recent|Sort-Object -Property @{Expression={$_.Modified};Descending=$true}|Select-Object -First 8)
                        }
                    }
                    foreach($candidate in $recent) {
                        if (@($queues[$direction]|Where-Object { $_.Relative -eq $candidate.Relative }).Count -eq 0) {
                            $queues[$direction].Insert(0,@{Relative=$candidate.Relative;Offset=0})
                        }
                    }
                } catch { $result.Errors++ }
                finally { if ($null -ne $directoryIterator) { $directoryIterator.Dispose() } }
            }
        }
        while ($result.Examined -lt $MaxFiles -and $budget.ReadBytes -lt $MaxBytes -and $watch.Elapsed.TotalSeconds -lt $MaxSeconds -and -not ($done[0] -and $done[1])) {
            $direction=$state.Turn; $state.Turn=1-$direction
            if ($done[$direction]) { continue }
            $queue=$queues[$direction]; $frame=$queue[0]; $relative=$null; $source=$null; $destination=$null; $input=$null; $output=$null; $targetInput=$null; $stage=$null; $published=$false
            try {
                $folder=Resolve-CertificatePath $roots[$direction] $frame.Relative
                if ([long]$frame.Offset -lt 0) { throw 'Invalid enumeration cursor.' }
                if ($null -eq $iterators[$direction]) {
                    $iterators[$direction]=[IO.Directory]::EnumerateFileSystemEntries($folder).GetEnumerator()
                    for($skip=0L;$skip -lt [long]$frame.Offset;$skip++) {
                        if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds) { throw [TimeoutException]::new('Enumeration budget reached.') }
                        if (-not $iterators[$direction].MoveNext()) { break }
                    }
                }
                if (-not $iterators[$direction].MoveNext()) {
                    $iterators[$direction].Dispose(); $iterators[$direction]=$null; $queue.RemoveAt(0)
                    if ($queue.Count -eq 0) { $done[$direction]=$true }; continue
                }
                $source=[string]$iterators[$direction].Current
                $relative=$source.Substring($roots[$direction].Length+1)
                $frame.Offset++; $result.Examined++
                $source=Resolve-CertificatePath $roots[$direction] $relative
                if ([IO.Directory]::Exists($source)) { [void]$queue.Add(@{Relative=$relative;Offset=0}); continue }
                if ([IO.Path]::GetExtension($source) -ine '.pdf') { continue }
                $destination=Resolve-CertificatePath $roots[1-$direction] $relative
                if (-not $state.Seen.ContainsKey($relative)) { $state.Seen[$relative]=@{A=$false;B=$false} }
                $entry=$state.Seen[$relative]; $sourceKey=@('A','B')[$direction]; $targetKey=@('B','A')[$direction]
                Add-CertificateSeen $state $relative $sourceKey $journal
                $exists=[IO.File]::Exists($destination)
                if ($exists) { Add-CertificateSeen $state $relative $targetKey $journal }
                elseif ($entry[$targetKey]) { $result.DeletionsDeferred++; continue }
                if (([datetime]::UtcNow-[IO.File]::GetLastWriteTimeUtc($source)).TotalSeconds -lt 30) { $result.Deferred++; continue }
                $input=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                if (([datetime]::UtcNow-[IO.File]::GetLastWriteTimeUtc($source)).TotalSeconds -lt 30) { $result.Deferred++; continue }
                if (-not (Test-CertificatePdf $input)) { $result.Invalid++; continue }
                if (-not $exists -and $input.Length -gt ($MaxBytes/3)) {
                    $result.Oversized++
                    if ($result.Details.Count -lt 50) { $result.Details+=@{Relative=$relative;Kind='NeedsLargerByteBudget'} }
                    continue
                }
                if ($input.Length -gt ($MaxBytes-$result.Bytes)) { $result.Deferred++; continue }
                if ($exists) {
                    $targetInput=[IO.File]::Open($destination,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                    $sourceStamp=[OcrCertificateFileIdentity]::Read($input.SafeFileHandle)
                    $targetStamp=[OcrCertificateFileIdentity]::Read($targetInput.SafeFileHandle)
                    if ($direction -eq 0) { $fingerprint=$sourceStamp+'|'+$targetStamp } else { $fingerprint=$targetStamp+'|'+$sourceStamp }
                    $equal=$false
                    if ($entry.ContainsKey('Cache') -and $null -ne $entry.Cache -and $entry.Cache.Fingerprint -eq $fingerprint) { $equal=[bool]$entry.Cache.Equal }
                    else {
                        $sourceHash=Get-CertificateHash $input $budget
                        $equal=(Get-CertificateHash $targetInput $budget) -eq $sourceHash
                        $entry.Cache=@{Fingerprint=$fingerprint;Equal=$equal}
                    }
                    if (-not $equal) {
                        $result.Conflicts++
                        if ($result.Details.Count -lt 50) { $result.Details+=@{Relative=$relative;Kind='Conflict'} }
                    }
                    continue
                }
                $sourceHash=Get-CertificateHash $input $budget
                $parent=[IO.Path]::GetDirectoryName($destination)
                Assert-CertificatePath $parent; [IO.Directory]::CreateDirectory($parent)|Out-Null; Assert-CertificatePath $parent
                $stage=Join-Path $parent ('.ocr-cert-'+[guid]::NewGuid().ToString('N')+'.tmp')
                $output=[IO.File]::Open($stage,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
                $buffer=New-Object byte[] 65536
                while ($input.Position -lt $input.Length) {
                    $remaining=$budget.Limit-$budget.ReadBytes
                    if ($remaining -le 0) { throw [TimeoutException]::new('Copy byte budget reached.') }
                    $read=$input.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining))
                    if ($read -eq 0) { throw [IO.IOException]::new('Unexpected end of source.') }
                    if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds) { throw [TimeoutException]::new('Copy budget reached.') }
                    $output.Write($buffer,0,$read)
                    $budget.ReadBytes+=$read
                    if ($budget.ReadBytes -gt $budget.Limit) { throw [TimeoutException]::new('Copy byte budget reached.') }
                }
                $output.Flush($true)
                if ((Get-CertificateHash $output $budget) -ne $sourceHash) { throw 'Stage hash mismatch.' }
                $output.Dispose(); $output=$null
                $state.Pending=@{Relative=$relative}; Save-CertificateState $statePath $state
                Resolve-CertificatePath $roots[1-$direction] $relative|Out-Null; Assert-CertificatePath $stage
                # File.Move is non-overwriting, including when a competing writer won.
                Publish-CertificateStage $stage $destination; $stage=$null
                $entry[$targetKey]=$true; $state.Pending=$null
                $published=$true
                $result.Copied++; $result.Bytes+=$input.Length
                if ($direction -eq 0) { $result.ToWeb++ } else { $result.ToLegacy++ }
            } catch [TimeoutException] { $result.Deferred++; break }
            catch [IO.IOException] {
                $result.Deferred++
                if ($null -ne $state.Pending) { break }
                if ($null -eq $relative) {
                    if ($null -ne $iterators[$direction]) { $iterators[$direction].Dispose();$iterators[$direction]=$null }
                    $queue.RemoveAt(0); if ($queue.Count -eq 0) { $done[$direction]=$true }
                }
            }
            catch {
                $result.Errors++
                if ($result.Details.Count -lt 50) { $result.Details+=@{Relative=$relative;Kind='RejectedOrUnavailable'} }
                if ($null -ne $state.Pending) { break }
                if ($null -eq $relative) {
                    if ($null -ne $iterators[$direction]) { $iterators[$direction].Dispose();$iterators[$direction]=$null }
                    $queue.RemoveAt(0); if ($queue.Count -eq 0) { $done[$direction]=$true }
                }
            } finally {
                foreach($stream in @($output,$input,$targetInput)) { if ($null -ne $stream) { $stream.Dispose() } }
                if ($stage -and [IO.File]::Exists($stage)) { Assert-CertificatePath $stage; [IO.File]::Delete($stage) }
                $state.Queues=@(@($queues[0].ToArray()),@($queues[1].ToArray()))
                if (($result.Examined % 100) -eq 0 -or $null -ne $state.Pending -or $published) { Save-CertificateState $statePath $state }
            }
        }
        $result.Backlog=$queues[0].Count+$queues[1].Count
        $result.ReadBytes=$budget.ReadBytes
        Save-CertificateState $statePath $state
        Assert-CertificatePath $journal
        $journalStream=[IO.File]::Open($journal,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try { $journalStream.Flush($true) } finally { $journalStream.Dispose() }
        Save-CertificateState (Resolve-CertificatePath $StateRoot 'last-run.json') $result
        return [pscustomobject]$result
    } finally {
        foreach($iterator in $iterators) { if ($null -ne $iterator) { $iterator.Dispose() } }
        if ($null -ne $lock) { $lock.Dispose() }
    }
}

if (-not $LibraryOnly) {
    if ($env:COMPUTERNAME -ine 'THBTCDT-CM1XKG2' -or [Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'KEMET\2172172205529') { throw 'Production host/account guard rejected.' }
    $privateRoot='C:\ocr-deploy\cert-sync'
    Assert-CertificatePath $privateRoot
    $allowed=@('S-1-5-18','S-1-5-32-544',[Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $acl=Get-Acl -LiteralPath $privateRoot
    if (-not $acl.AreAccessRulesProtected) { throw 'State directory must have protected ACL.' }
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $allowed) { throw 'State directory owner rejected.' }
    foreach($rule in $acl.Access) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -notin $allowed) { throw 'State ACL contains unexpected principal.' }
    }
    $importState=Resolve-CertificatePath $privateRoot 'import'
    [IO.Directory]::CreateDirectory($importState)|Out-Null
    $importFiles=[Math]::Max(1,[int][Math]::Floor($MaxFiles/3)); $importBytes=[long][Math]::Floor($MaxBytes/3); $importSeconds=[Math]::Max(1,[int][Math]::Floor($MaxSeconds/3))
    Invoke-LegacyCertificateSync -LegacyRoot 'C:\ocr-uploads' -WebRoot 'C:\uploads' -StateRoot $importState -OneWay -PrioritizeRecent -MaxFiles $importFiles -MaxBytes $importBytes -MaxSeconds $importSeconds
    Invoke-LegacyCertificateSync -LegacyRoot '\\svr120a\Cert$' -WebRoot 'C:\uploads' -StateRoot $privateRoot -PrioritizeRecent -MaxFiles ([Math]::Max(1,$MaxFiles-$importFiles)) -MaxBytes ($MaxBytes-$importBytes) -MaxSeconds ([Math]::Max(1,$MaxSeconds-$importSeconds))
}
