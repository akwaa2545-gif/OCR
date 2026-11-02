$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Install-LegacyPhotoSync.ps1'
if (!(Test-Path -LiteralPath $scriptPath)) { throw 'Photo sync installer is missing.' }
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Photo sync installer does not parse.' }
$source = Get-Content -LiteralPath $scriptPath -Raw
$checks = 0
function Assert-SyncTask([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "Photo sync task check failed: $Name" }
    $script:checks++
    Write-Output "PASS: $Name"
}
Assert-SyncTask ($source -match '-LogonType Interactive -RunLevel Limited') 'Limited interactive identity'
Assert-SyncTask ($source -match '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass') 'Hidden process-local execution policy'
Assert-SyncTask ($source -match '-AtLogOn -User \$account') 'Starts on account logon'
Assert-SyncTask ($source -match '-RepetitionInterval \(New-TimeSpan -Minutes 1\)') 'Repeats each minute'
Assert-SyncTask ($source -match '-ExecutionTimeLimit \(New-TimeSpan -Minutes 3\) -MultipleInstances IgnoreNew') 'Bounded non-overlapping runs'
Assert-SyncTask ($source -notmatch 'SeedEmployee|Start-Service|Set-Service|Stop-Service|Password|Unregister-ScheduledTask|Start-ScheduledTask') 'No seed operation, service changes, credentials, task removal or immediate start'
Assert-SyncTask ($source.IndexOf('if ($ValidateOnly)') -lt $source.LastIndexOf('Register-OcrPhotoSyncTask')) 'Validation returns before mutation'
Assert-SyncTask ($source -match 'AreAccessRulesProtected' -and $source -match 'ReparsePoint' -and $source -match 'GetAccessRules' -and $source -match 'GetOwner') 'Protected ACL, ownership and reparse checks'
$originalHost = $env:COMPUTERNAME
try {
    $env:COMPUTERNAME = 'OCR-OFFLINE-WRONG-HOST'
    $refused = $false
    try { & $scriptPath -ValidateOnly } catch { $refused = $_.Exception.Message -eq 'Unexpected host.' }
    Assert-SyncTask $refused 'Wrong host rejected without scheduled task calls'
} finally { $env:COMPUTERNAME = $originalHost }

# Execute the registration function only, with every task API replaced by a fake.
$function = @($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Register-OcrPhotoSyncTask'}, $true))
Assert-SyncTask ($function.Count -eq 1) 'One isolated registration function'
. ([scriptblock]::Create($function[0].Extent.Text))
$script:events = New-Object 'System.Collections.Generic.List[string]'
function New-ScheduledTaskAction { param($Execute,$WorkingDirectory,$Argument); [pscustomobject]@{ Execute=$Execute; WorkingDirectory=$WorkingDirectory; Argument=$Argument } }
function New-ScheduledTaskPrincipal { param($UserId,$LogonType,$RunLevel); [pscustomobject]@{ UserId=$UserId; LogonType=$LogonType; RunLevel=$RunLevel } }
function New-ScheduledTaskTrigger { param([switch]$AtLogOn,$User,[switch]$Once,$At,$RepetitionInterval); [pscustomobject]@{ Logon=[bool]$AtLogOn; User=$User; Once=[bool]$Once; Interval=$RepetitionInterval } }
function New-ScheduledTaskSettingsSet { param($ExecutionTimeLimit,$MultipleInstances,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,[switch]$StartWhenAvailable); [pscustomobject]@{Limit=$ExecutionTimeLimit; Instances=$MultipleInstances} }
function Register-ScheduledTask {
    param($TaskName,$Action,$Principal,$Trigger,$Settings,$Description)
    $script:events.Add($TaskName)
    $script:registered = [pscustomobject]@{ Action=$Action; Principal=$Principal; Trigger=$Trigger; Settings=$Settings }
}
Register-OcrPhotoSyncTask
Assert-SyncTask ($script:events.Count -eq 1 -and $script:events[0] -eq 'OCR-LegacyPhotoSync') 'Registers only the exact sync task'
Assert-SyncTask ($script:registered.Principal.UserId -eq 'KEMET\2172172205529' -and $script:registered.Principal.LogonType -eq 'Interactive' -and $script:registered.Principal.RunLevel -eq 'Limited') 'Actual task account stays unprivileged'
Assert-SyncTask ($script:registered.Action.Execute -eq 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -and $script:registered.Action.Argument -match '-File "C:\\ocr-deploy\\Sync-LegacyPhotos.ps1"$') 'Fixed executable and core script, no arbitrary arguments'
Assert-SyncTask ($script:registered.Trigger.Count -eq 2 -and $script:registered.Trigger[0].Logon -and $script:registered.Trigger[1].Interval.TotalMinutes -eq 1) 'Actual logon and repeating triggers'
Assert-SyncTask ($script:registered.Settings.Limit.TotalMinutes -eq 3 -and $script:registered.Settings.Instances -eq 'IgnoreNew') 'Actual timeout and overlap settings'
$aclFunction = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-OcrPhotoSyncPrivatePath'}, $true)
. ([scriptblock]::Create($aclFunction.Extent.Text))
function Test-SyncAcl([string]$Scenario, [bool]$ShouldReject) {
    function Test-Path { $Scenario -ne 'Missing' }
    function Get-Item {
        $attributes = [IO.FileAttributes]::Directory
        if ($Scenario -eq 'Reparse') { $attributes = $attributes -bor [IO.FileAttributes]::ReparsePoint }
        [pscustomobject]@{ Attributes=$attributes; PSIsContainer=$true; Parent=$null }
    }
    function Get-Acl {
        $fakeAcl = [pscustomobject]@{AreAccessRulesProtected=($Scenario -ne 'InheritedRoot')}
        $fakeAcl | Add-Member ScriptMethod GetOwner { param($type); if ($Scenario -eq 'UntrustedOwner') { [pscustomobject]@{Value='S-1-1-0'} } else { [pscustomobject]@{Value='S-1-5-18'} } }
        $fakeAcl | Add-Member ScriptMethod GetAccessRules {
            param($explicit,$inherited,$type)
            $identity = if ($Scenario -eq 'BroadAccess') { 'S-1-1-0' } else { 'S-1-5-18' }
            [pscustomobject]@{AccessControlType=[Security.AccessControl.AccessControlType]::Allow; IdentityReference=[pscustomobject]@{Value=$identity}}
        }
        $fakeAcl
    }
    $rejected = $false
    try { Assert-OcrPhotoSyncPrivatePath -Path 'C:\offline' -ProtectedRoot } catch { $rejected = $true }
    Assert-SyncTask ($rejected -eq $ShouldReject) "ACL/path guard: $Scenario"
}
Test-SyncAcl 'Trusted' $false
Test-SyncAcl 'Missing' $true
Test-SyncAcl 'Reparse' $true
Test-SyncAcl 'InheritedRoot' $true
Test-SyncAcl 'UntrustedOwner' $true
Test-SyncAcl 'BroadAccess' $true
$stateFunction = $ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-OcrPhotoSyncInitialized'}, $true)
. ([scriptblock]::Create($stateFunction.Extent.Text))
foreach ($scenario in @('Ready','Version','Pending','MissingRepair','BadHash','Errors','Conflicts','MissingCounts')) {
    $state = [pscustomobject]@{Version=1; Pending=$null; Entries=[pscustomobject]@{'102'=[pscustomobject]@{Hash=('a' * 64)}}}
    $lastRun = [pscustomobject]@{Counts=[pscustomobject]@{Errors=0; Conflicts=0}}
    switch ($scenario) {
        'Version' { $state.Version = 2 }
        'Pending' { $state.Pending = 'unfinished' }
        'MissingRepair' { $state.Entries = [pscustomobject]@{} }
        'BadHash' { $state.Entries.'102'.Hash = 'bad' }
        'Errors' { $lastRun.Counts.Errors = 1 }
        'Conflicts' { $lastRun.Counts.Conflicts = 1 }
        'MissingCounts' { $lastRun.Counts = $null }
    }
    $rejected = $false
    try { Assert-OcrPhotoSyncInitialized -State $state -LastRun $lastRun } catch { $rejected = $true }
    Assert-SyncTask ($rejected -eq ($scenario -ne 'Ready')) "Initialization guard: $scenario"
}
Write-Output "$checks photo sync task checks passed (offline only; no host changes)."
