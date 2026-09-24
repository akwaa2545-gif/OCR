[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$VerifiedWebRevision,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'

function Assert-CertificateSyncPrivatePath {
    param([Parameter(Mandatory)][string]$Path, [switch]$ProtectedRoot)
    if (!(Test-Path -LiteralPath $Path)) { throw 'Required certificate sync path is missing.' }
    $parent = Get-Item -LiteralPath $Path -Force
    while ($null -ne $parent) {
        if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Certificate sync paths must not traverse reparse points.' }
        if ($parent.PSIsContainer) { $parent = $parent.Parent } else { $parent = $parent.Directory }
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $acl = Get-Acl -LiteralPath $Path
    if ($ProtectedRoot -and !$acl.AreAccessRulesProtected) { throw 'Certificate sync root must disable ACL inheritance.' }
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $allowed) { throw 'Certificate sync path owner is not trusted.' }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and $rule.IdentityReference.Value -notin $allowed) {
            throw 'Certificate sync path grants access to an untrusted identity.'
        }
    }
}

function Assert-CertificateSyncEvidence {
    param([Parameter(Mandatory)]$Web, [Parameter(Mandatory)]$Probe, [Parameter(Mandatory)][string]$Revision)
    if ($Web.Revision -ine $Revision -or $Web.Revision -notmatch '^[a-fA-F0-9]{40}$' -or $Web.Protected -isnot [bool] -or !$Web.Protected -or $Web.ProductionContainerId -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Verified production web protection evidence does not match the requested revision.'
    }
    $verified = [DateTimeOffset]::MinValue
    if (![DateTimeOffset]::TryParse([string]$Web.VerifiedUtc, [ref]$verified)) { throw 'Invalid web protection verification time.' }
    $age = [DateTimeOffset]::UtcNow - $verified.ToUniversalTime()
    if ($age.TotalHours -gt 24 -or $age.TotalMinutes -lt -5) { throw 'Production web protection must have been verified within the last 24 hours.' }
    if ($null -eq $Probe.PSObject.Properties['Error'] -or $null -ne $Probe.Error -or @($Probe.Roots).Count -ne 2) { throw 'Certificate access probe failed or is incomplete.' }
    foreach ($expected in @('C:\uploads', '\\svr120a\Cert$')) {
        $matches = @($Probe.Roots | Where-Object { $_.Root -ieq $expected })
        if ($matches.Count -ne 1 -or $matches[0].Writable -isnot [bool] -or !$matches[0].Writable -or $matches[0].AtomicMove -isnot [bool] -or !$matches[0].AtomicMove) {
            throw 'Both exact production certificate roots require successful read/write and atomic-move access probes.'
        }
    }
}

function Register-CertificateSyncTask {
    $account = 'KEMET\2172172205529'
    $action = New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\ocr-deploy' -Argument '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\ocr-deploy\Sync-LegacyCertificates.ps1"'
    $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
    $logon = New-ScheduledTaskTrigger -AtLogOn -User $account
    # No repetition duration: continue every minute while the user is signed in.
    $minute = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName 'OCR-LegacyCertificateSync' -Action $action -Principal $principal -Trigger @($logon, $minute) -Settings $settings -Description 'Bounded two-way production PDF certificate synchronization under the signed-in Docker user. Conflicts are retained; no delete propagation.' | Out-Null
}

function Get-CertificateProductionIdentity {
    $docker = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
    $endpoint = 'npipe:////./pipe/dockerDesktopLinuxEngine'
    try {
        $identity = & $docker --host $endpoint container inspect --format '{{.Id}}|{{.Image}}|{{.State.Running}}' ocrweb 2>$null
        if ($LASTEXITCODE -ne 0 -or @($identity).Count -ne 1 -or $identity -notmatch '^([a-f0-9]{64})\|(sha256:[a-f0-9]{64})\|true$') { throw 'Unexpected container.' }
        $containerId = $Matches[1]
        $imageId = $Matches[2]
        $labels = & $docker --host $endpoint image inspect --format '{{json .Config.Labels}}' $imageId 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Image inspection failed.' }
        $revision = ($labels | ConvertFrom-Json).'org.opencontainers.image.revision'
        if ($revision -notmatch '^[a-fA-F0-9]{40}$') { throw 'Image revision missing.' }
        [pscustomobject]@{ ContainerId=$containerId; Revision=$revision }
    } catch { throw 'Cannot verify the running production container and its image revision.' }
}

function Assert-CertificateLiveProduction {
    param([Parameter(Mandatory)]$Web)
    $live = Get-CertificateProductionIdentity
    if ($live.ContainerId -cne $Web.ProductionContainerId -or $live.Revision -ine $Web.Revision) { throw 'Production changed since web protection was verified; repeat runtime verification.' }
}

if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'KEMET\2172172205529') { throw 'Run as the existing Docker user.' }
if (Get-ScheduledTask -TaskName 'OCR-LegacyCertificateSync' -ErrorAction SilentlyContinue) { throw 'Certificate sync task already exists; inspect it before changing it.' }
foreach ($directory in @('C:\ocr-deploy', 'C:\ocr-deploy\cert-sync')) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw 'Protected certificate sync directory is missing.' }
    Assert-CertificateSyncPrivatePath -Path $directory -ProtectedRoot
}
foreach ($file in @('C:\ocr-deploy\Sync-LegacyCertificates.ps1', 'C:\ocr-deploy\certificate-access.json', 'C:\ocr-deploy\cert-sync\web-protection.json')) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Certificate sync script or verified safety evidence is missing.' }
    Assert-CertificateSyncPrivatePath -Path $file
}
$webEvidence = Get-Content -LiteralPath 'C:\ocr-deploy\cert-sync\web-protection.json' -Raw | ConvertFrom-Json
Assert-CertificateSyncEvidence -Revision $VerifiedWebRevision -Web $webEvidence -Probe (Get-Content -LiteralPath 'C:\ocr-deploy\certificate-access.json' -Raw | ConvertFrom-Json)
Assert-CertificateLiveProduction -Web $webEvidence
if ($ValidateOnly) { Write-Output 'Certificate sync prerequisites validated; no changes.'; return }
Register-CertificateSyncTask
Write-Output 'Certificate sync task installed; first bounded run starts within one minute. Keep the Docker user signed in; disconnecting RDP is fine.'
