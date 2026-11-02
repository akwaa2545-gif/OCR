[CmdletBinding()]
param([string[]]$SeedEmployee = @(), [switch]$LibraryOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-PhotoPath {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse path rejected.' }
        if ($item -is [IO.FileInfo]) { $item=$item.Directory } else { $item=$item.Parent }
    }
}

function Get-PhotoHash {
    param([string]$Path)
    if (-not [IO.File]::Exists($Path)) { return '' }
    Assert-PhotoPath $Path
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Save-PhotoState {
    param([string]$Path, $Value)
    $temp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temp, ($Value | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temp, $Path, "$Path.previous") }
        else { [IO.File]::Move($temp, $Path) }
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}

function Read-PhotoState {
    param([string]$Path)
    if (-not [IO.File]::Exists($Path)) { return @{Version=1; Entries=@{}; Pending=$null} }
    Assert-PhotoPath $Path
    $raw = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($raw.Version -ne 1) { throw 'Unsupported state.' }
    $entries=@{}
    foreach ($p in $raw.Entries.PSObject.Properties) { $entries[$p.Name]=$p.Value }
    return @{Version=1; Entries=$entries; Pending=$raw.Pending}
}

function Get-PhotoDescriptor {
    param($Row)
    $code=[string]$Row.EmpCode; $photo=[string]$Row.Photo
    if ($code -cnotmatch '^[A-Za-z0-9-]{1,32}$') { return $null }
    $pattern='^/app/photos/' + [regex]::Escape($code) + '_[0-9]{1,24}\.(jpg|jpeg|png)$'
    if ($photo -cnotmatch $pattern) { return $null }
    return @{Code=$code; Photo=$photo; File=$photo.Substring(12); Target=($code+'.'+$Matches[1])}
}

function Get-LegacyPhotoDescriptor {
    param($Row)
    $code=[string]$Row.EmpCode; $photo=[string]$Row.Photo
    if ($code -cnotmatch '^[A-Za-z0-9-]{1,32}$') { return $null }
    $pattern='^(?:' + [regex]::Escape('\\svr120a\PhotoEmp$\') + '|' + [regex]::Escape('F:\') + ')' + [regex]::Escape($code) + '\.(jpg|jpeg|png)$'
    if ($photo -notmatch $pattern) { return $null }
    return @{Code=$code; Photo=$photo; Target=($code+'.'+$Matches[1].ToLowerInvariant())}
}

function Assert-PhotoImage {
    param([string]$Path)
    Assert-PhotoPath $Path
    if ((Get-Item -LiteralPath $Path).Length -gt 5242880) { throw 'Image size rejected.' }
    $bytes=[IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -gt 5242880 -or $bytes.Length -lt 4) { throw 'Image size rejected.' }
    if ([IO.Path]::GetExtension($Path) -eq '.png') {
        if ($bytes.Length -lt 8 -or [BitConverter]::ToString($bytes,0,8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Image signature rejected.' }
    } elseif ($bytes[0] -ne 255 -or $bytes[1] -ne 216 -or $bytes[2] -ne 255) { throw 'Image signature rejected.' }
}

function Publish-PhotoFile {
    param([string]$Stage,[string]$Target,[string]$Backup)
    if ([IO.File]::Exists($Target)) { [IO.File]::Replace($Stage,$Target,$Backup) }
    else { [IO.File]::Move($Stage,$Target) }
}

function Invoke-LegacyPhotoSync {
    param([string]$SourceRoot,[string]$DestinationRoot,[string]$StateRoot,
          [scriptblock]$GetRows,[scriptblock]$GetCurrent,[string[]]$SeedEmployee=@())
    foreach ($path in @($SourceRoot,$DestinationRoot,$StateRoot)) {
        if (-not [IO.Directory]::Exists($path)) { throw 'Required directory unavailable.' }
        Assert-PhotoPath $path
    }
    $lock=$null
    try {
        $lockPath=Join-Path $StateRoot 'sync.lock'
        if ([IO.File]::Exists($lockPath)) { Assert-PhotoPath $lockPath }
        $lock=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $statePath=Join-Path $StateRoot 'state.json'; $first=-not [IO.File]::Exists($statePath)
        $state=Read-PhotoState $statePath
        $result=@{Baselined=0; Copied=0; Conflicts=0; Rejected=0; Errors=0; Recovered=0}
        if ($null -ne $state.Pending) {
            $pending=$state.Pending
            if ($pending.PSObject.Properties.Name -contains 'Conflict' -and $pending.Conflict) { throw 'Pending publication requires conflict review.' }
            $descriptor=Get-PhotoDescriptor @{EmpCode=$pending.Code;Photo=$pending.Photo}
            if ($null -eq $descriptor -or $descriptor.Target -cne $pending.Target) { throw 'Invalid pending state.' }
            $actual=Get-PhotoHash (Join-Path $DestinationRoot $pending.Target)
            if ($actual -eq $pending.Hash) {
                if ($pending.Before -ne '') {
                    if ($pending.Backup -cnotmatch '^\.ocr-sync-backup-[a-f0-9]{32}\.bak$' -or (Get-PhotoHash (Join-Path $DestinationRoot $pending.Backup)) -ne $pending.Before) { throw 'Pending replacement backup requires review.' }
                }
                $state.Entries[$pending.Code]=@{Photo=$pending.Photo;Target=$pending.Target;Hash=$pending.Hash}
                $result.Recovered++
            } elseif ($actual -ne $pending.Before) { throw 'Pending publication conflict.' }
            $state.Pending=$null; Save-PhotoState $statePath $state
        }
        $rows=@(& $GetRows)
        if ($first) {
            foreach ($row in $rows) {
                $d=Get-PhotoDescriptor $row
                if ($null -eq $d) { $d=Get-LegacyPhotoDescriptor $row }
                if ($null -eq $d) { continue }
                if ($state.Entries.ContainsKey($d.Code)) { throw 'Duplicate employee reference.' }
                $state.Entries[$d.Code]=@{Photo=$d.Photo;Target=$d.Target;Hash=(Get-PhotoHash (Join-Path $DestinationRoot $d.Target))}
                $result.Baselined++
            }
            # Baseline must be durable before the first explicitly selected repair.
            Save-PhotoState $statePath $state
        }
        foreach ($row in $rows) {
            $d=Get-PhotoDescriptor $row
            if ($null -eq $d) {
                $legacy=Get-LegacyPhotoDescriptor $row
                if ($null -ne $legacy -and -not $state.Entries.ContainsKey($legacy.Code)) {
                    $state.Entries[$legacy.Code]=@{Photo=$legacy.Photo;Target=$legacy.Target;Hash=(Get-PhotoHash (Join-Path $DestinationRoot $legacy.Target))}
                    Save-PhotoState $statePath $state
                    $result.Baselined++
                } elseif ($null -eq $legacy) { $result.Rejected++ }
                continue
            }
            $target=Join-Path $DestinationRoot $d.Target
            $stage=$null; $destinationGuard=$null
            try {
                $old=$state.Entries[$d.Code]; $hash=Get-PhotoHash $target
                if ($null -ne $old -and $old.Photo -ceq $d.Photo -and $SeedEmployee -cnotcontains $d.Code) { continue }
                $expected=''
                if ($null -ne $old -and $old.Target -ceq $d.Target) { $expected=$old.Hash }
                if ($hash -ne $expected) { $result.Conflicts++; continue }
                $source=Join-Path $SourceRoot $d.File
                Assert-PhotoImage $source
                $sourceHash=Get-PhotoHash $source
                $stage=Join-Path $DestinationRoot ('.ocr-sync-'+[guid]::NewGuid().ToString('N')+'.tmp')
                [IO.File]::Copy($source,$stage,$false)
                if ((Get-PhotoHash $stage) -ne $sourceHash) { throw 'Staging verification failed.' }
                if ([IO.File]::Exists($target)) {
                    # Deny ordinary legacy writes while validating and replacing. Delete
                    # sharing is required by File.Replace; an independent rename-style
                    # writer still needs coordination with this task.
                    $destinationGuard=[IO.File]::Open($target,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::Read -bor [IO.FileShare]::Delete))
                }
                if ((& $GetCurrent $d.Code) -cne $d.Photo) { throw 'Database photo changed.' }
                if ((Get-PhotoHash $target) -ne $expected) { $result.Conflicts++; continue }
                $backupLeaf='.ocr-sync-backup-'+[guid]::NewGuid().ToString('N')+'.bak'
                $state.Pending=@{Code=$d.Code;Photo=$d.Photo;Target=$d.Target;Hash=$sourceHash;Before=$expected;Backup=$backupLeaf;Conflict=$false}
                Save-PhotoState $statePath $state
                $backup=Join-Path $DestinationRoot $backupLeaf
                Publish-PhotoFile $stage $target $backup
                if ($expected -ne '' -and (Get-PhotoHash $backup) -ne $expected) {
                    # An uncoordinated rename-style writer raced publication. Retain
                    # both backup and intent for operator review, never auto-restore.
                    $state.Pending.Conflict=$true
                    Save-PhotoState $statePath $state
                    throw 'Replacement backup changed during publication.'
                }
                $state.Entries[$d.Code]=@{Photo=$d.Photo;Target=$d.Target;Hash=$sourceHash}
                $state.Pending=$null; Save-PhotoState $statePath $state
                $result.Copied++
            } catch {
                $result.Errors++
                # Stop after an uncertain publication; persisted intent is recovered next run.
                if ($null -ne $state.Pending -or [IO.File]::Exists("$statePath.previous")) {
                    $disk=Read-PhotoState $statePath
                    if ($null -ne $disk.Pending) { break }
                }
            } finally {
                if ($null -ne $destinationGuard) { $destinationGuard.Dispose() }
                if ($null -ne $stage -and [IO.File]::Exists($stage)) { [IO.File]::Delete($stage) }
            }
        }
        if ($result.Errors -eq 0) { Save-PhotoState $statePath $state }
        Save-PhotoState (Join-Path $StateRoot 'last-run.json') @{Utc=[DateTime]::UtcNow.ToString('o');Counts=$result}
        return [pscustomobject]$result
    } finally { if ($null -ne $lock) { $lock.Dispose() } }
}

function Get-ProductionPhotoRows {
    param([string]$ConnectionString,[string]$Employee)
    $connection=New-Object Data.SqlClient.SqlConnection($ConnectionString)
    try {
        $connection.Open(); $command=$connection.CreateCommand(); $command.CommandTimeout=10
        $command.CommandText='SELECT EmpCode, Photo FROM dbo.tblEmployee'
        if ($Employee) {
            $command.CommandText+=' WHERE EmpCode = @code'
            [void]$command.Parameters.Add('@code',[Data.SqlDbType]::VarChar,32); $command.Parameters['@code'].Value=$Employee
        }
        $reader=$command.ExecuteReader()
        try { while ($reader.Read()) { @{EmpCode=[string]$reader['EmpCode'];Photo=[string]$reader['Photo']} } }
        finally { $reader.Dispose(); $command.Dispose() }
    } finally { $connection.Dispose() }
}

if (-not $LibraryOnly -and $MyInvocation.InvocationName -ne '.') {
    try {
        if ($env:COMPUTERNAME -cne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
        $lines=@(Get-Content -LiteralPath 'C:\ocr-deploy\prod.env' | Where-Object { $_.StartsWith('ConnectionStrings__DefaultConnection=') })
        if ($lines.Count -ne 1) { throw 'Production configuration invalid.' }
        $builder=New-Object Data.SqlClient.SqlConnectionStringBuilder($lines[0].Substring('ConnectionStrings__DefaultConnection='.Length))
        if ($builder.InitialCatalog -cne 'OperatorCertificationRecordDB') { throw 'Production database mismatch.' }
        $builder['Connect Timeout']=10
        $cs=$builder.ConnectionString
        $result=Invoke-LegacyPhotoSync -SourceRoot 'C:\ocr-uploads' -DestinationRoot '\\svr120a\PhotoEmp$' -StateRoot 'C:\ocr-deploy\photo-sync' -SeedEmployee $SeedEmployee -GetRows { Get-ProductionPhotoRows $cs } -GetCurrent { param($code) $row=@(Get-ProductionPhotoRows $cs $code); if ($row.Count -eq 1) { $row[0].Photo } else { '' } }
        $result | ConvertTo-Json -Compress
        if ($result.Errors -gt 0 -or $result.Conflicts -gt 0) { exit 1 }
    } catch { Write-Error 'Legacy photo synchronization failed; check host, share, SQL access and protected state.'; exit 1 }
}
