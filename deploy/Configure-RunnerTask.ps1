[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$serviceName = 'actions.runner.akwaa2545-gif-OCR.THBTCDT-CM1XKG2'
$taskName = 'OCR-DeploymentRunner'
$runnerRoot = 'C:\actions-runner'
$account = 'KEMET\2172172205529'
function Get-UserRunnerProcess {
    foreach ($process in @(Get-CimInstance Win32_Process -Filter "Name='Runner.Listener.exe'" | Where-Object { $_.ExecutablePath -ieq "$runnerRoot\bin\Runner.Listener.exe" })) {
        $owner = Invoke-CimMethod -InputObject $process -MethodName GetOwner
        if ("$($owner.Domain)\$($owner.User)" -ieq $account) { $process }
    }
}
if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ine $account) { throw 'Run as the existing Docker user.' }
if (!(Test-Path -LiteralPath "$runnerRoot\run.cmd")) { throw 'Existing runner installation missing.' }
if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) { throw 'Runner task already exists; inspect it before changing it.' }
$service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if (!$service -or $service.StartName -ine 'NT AUTHORITY\NETWORK SERVICE') { throw 'Unexpected runner service configuration.' }
if (@(Get-UserRunnerProcess).Count) { throw 'A runner already runs as the Docker user; inspect it before changing service setup.' }
if ($ValidateOnly) { Write-Output 'Existing runner validated; no changes.'; return }
$registered = $false
try {
    $action = New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -WorkingDirectory $runnerRoot -Argument '-NoProfile -NonInteractive -WindowStyle Hidden -Command "& ''C:\actions-runner\run.cmd''; exit $LASTEXITCODE"'
    $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Limited
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $account
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Trigger $trigger -Settings $settings -Description 'OCR GitHub deployment runner under the signed-in Docker Desktop user. No password or SYSTEM access.' | Out-Null
    $registered = $true
    Stop-Service -Name $serviceName
    Set-Service -Name $serviceName -StartupType Disabled
    Start-ScheduledTask -TaskName $taskName
    $started = $false
    for ($attempt=0; $attempt -lt 15; $attempt++) {
        Start-Sleep -Seconds 2
        $started = @(Get-UserRunnerProcess).Count -eq 1
        if ($started) { break }
    }
    if (!$started) { throw 'Runner did not start as the Docker user.' }
    Write-Output 'Runner task started under the Docker user. Previous service retained but disabled. Keep this user signed in; RDP disconnect is fine.'
} catch {
    $failure = $_
    $recoveryErrors = @()
    try {
        if ($registered) {
            try { Stop-ScheduledTask -TaskName $taskName } catch { $recoveryErrors += 'Task stop failed.' }
            # Only terminate listeners from this installation and newly selected account.
            # Preflight refused any such process that existed before this operation.
            foreach ($process in @(Get-UserRunnerProcess)) {
                try { $null = Invoke-CimMethod -InputObject $process -MethodName Terminate } catch { $recoveryErrors += 'Task listener termination failed.' }
            }
            for ($attempt=0; $attempt -lt 5 -and @(Get-UserRunnerProcess).Count; $attempt++) { Start-Sleep -Seconds 1 }
            try { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false } catch { $recoveryErrors += 'Task removal failed.' }
        }
    } finally {
        try {
            $mode = switch ($service.StartMode) { 'Auto' {'Automatic'} 'Manual' {'Manual'} default {'Disabled'} }
            Set-Service -Name $serviceName -StartupType $mode
            if (@(Get-UserRunnerProcess).Count) { throw 'Task listener remains; refusing duplicate service startup.' }
            if ($service.State -eq 'Running') { Start-Service -Name $serviceName }
        } catch { $recoveryErrors += 'Original runner service restoration needs manual attention.' }
    }
    if ($recoveryErrors.Count) { throw ('Runner migration failed. ' + ($recoveryErrors -join ' ')) }
    throw $failure
}
