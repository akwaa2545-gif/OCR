[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'

function Assert-OcrPhotoSyncPrivatePath {
    param([Parameter(Mandatory)][string]$Path, [switch]$ProtectedRoot)
    if (!(Test-Path -LiteralPath $Path)) { throw 'Required photo sync file or directory is missing.' }
    $item = Get-Item -LiteralPath $Path -Force
    $parent = $item
    while ($null -ne $parent) {
        if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Photo sync paths must not traverse reparse points.' }
        if ($parent.PSIsContainer) { $parent = $parent.Parent } else { $parent = $parent.Directory }
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $acl = Get-Acl -LiteralPath $Path
    if ($ProtectedRoot -and !$acl.AreAccessRulesProtected) { throw 'Photo sync root must disable ACL inheritance.' }
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $allowed) { throw 'Photo sync path owner is not trusted.' }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and $rule.IdentityReference.Value -notin $allowed) {
            throw 'Photo sync path grants access to an untrusted identity.'
        }
    }
}

function Register-OcrPhotoSyncTask {
    $account = 'KEMET\2172172205529'
    $action = New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory 'C:\ocr-deploy' -Argument '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\ocr-deploy\Sync-LegacyPhotos.ps1"'
    $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
    $logon = New-ScheduledTaskTrigger -AtLogOn -User $account
    # Omitting RepetitionDuration makes this repeat indefinitely while signed in.
    $minute = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 3) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
    Register-ScheduledTask -TaskName 'OCR-LegacyPhotoSync' -Action $action -Principal $principal -Trigger @($logon, $minute) -Settings $settings -Description 'Copies new production employee photos to the legacy share while the existing Docker user remains signed in. Certificates and test photos are excluded.' | Out-Null
}

function Assert-OcrPhotoSyncInitialized {
    param([Parameter(Mandatory)]$State, [Parameter(Mandatory)]$LastRun)
    if ($State.Version -ne 1 -or $null -ne $State.Pending) { throw 'Photo sync baseline is incompatible or has an unfinished operation.' }
    if ($null -eq $State.Entries.'102' -or $State.Entries.'102'.Hash -notmatch '^[a-fA-F0-9]{64}$') { throw 'The requested employee photo repair must complete before installing the recurring task.' }
    if ($null -eq $LastRun.Counts -or $null -eq $LastRun.Counts.Errors -or $null -eq $LastRun.Counts.Conflicts -or $LastRun.Counts.Errors -ne 0 -or $LastRun.Counts.Conflicts -ne 0) {
        throw 'The initial photo sync run must finish without errors or conflicts.'
    }
}

if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'KEMET\2172172205529') { throw 'Run as the existing Docker user.' }
if (Get-ScheduledTask -TaskName 'OCR-LegacyPhotoSync' -ErrorAction SilentlyContinue) { throw 'Photo sync task already exists; inspect it before changing it.' }
foreach ($directory in @('C:\ocr-deploy', 'C:\ocr-deploy\photo-sync')) {
    if (!(Test-Path -LiteralPath $directory -PathType Container)) { throw 'Protected photo sync directory is missing.' }
    Assert-OcrPhotoSyncPrivatePath -Path $directory -ProtectedRoot:($directory -eq 'C:\ocr-deploy')
}
foreach ($file in @('C:\ocr-deploy\Sync-LegacyPhotos.ps1', 'C:\ocr-deploy\prod.env', 'C:\ocr-deploy\photo-sync\state.json', 'C:\ocr-deploy\photo-sync\last-run.json')) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Photo sync initialization or production configuration is missing.' }
    Assert-OcrPhotoSyncPrivatePath -Path $file
}
Assert-OcrPhotoSyncInitialized -State (Get-Content -LiteralPath 'C:\ocr-deploy\photo-sync\state.json' -Raw | ConvertFrom-Json) -LastRun (Get-Content -LiteralPath 'C:\ocr-deploy\photo-sync\last-run.json' -Raw | ConvertFrom-Json)
# Initial baseline and the requested employee repair are performed separately.
# The recurring task never initiates a baseline or bulk migration.
if ($ValidateOnly) { Write-Output 'Photo sync task prerequisites validated; no changes.'; return }
Register-OcrPhotoSyncTask
Write-Output 'Photo sync task installed. It starts within one minute and at logon. Keep the Docker user signed in; disconnecting RDP is fine.'
