$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'Install-LegacyCertificateSync.ps1'
if (!(Test-Path -LiteralPath $path)) { throw 'Certificate task installer missing.' }
$tokens=$null; $errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Installer parse failed.' }
$script:checks=0
function Assert-Task([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "Certificate task check failed: $Name" }
    $script:checks++; Write-Output "PASS: $Name"
}
foreach ($name in @('Assert-CertificateSyncEvidence','Register-CertificateSyncTask','Assert-CertificateSyncPrivatePath','Assert-CertificateLiveProduction')) {
    $node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
    Assert-Task ($null -ne $node) "$name present"
    . ([scriptblock]::Create($node.Extent.Text))
}
$revision='a'*40
foreach ($scenario in @('Ready','WrongRevision','NotProtected','Stale','Future','BadDate','MissingContainer','ProbeError','WrongRoot','DuplicateRoot','NotWritable','NotAtomic','StringBoolean','MissingError')) {
    $web=[pscustomobject]@{Revision=$revision; Protected=$true; VerifiedUtc=[DateTime]::UtcNow.ToString('o'); ProductionContainerId=('b'*64)}
    $probe=[pscustomobject]@{Error=$null; Roots=@([pscustomobject]@{Root='C:\uploads'; Writable=$true; AtomicMove=$true},[pscustomobject]@{Root='\\svr120a\Cert$'; Writable=$true; AtomicMove=$true})}
    switch ($scenario) {
        'WrongRevision' {$web.Revision='c'*40}
        'NotProtected' {$web.Protected=$false}
        'Stale' {$web.VerifiedUtc=[DateTime]::UtcNow.AddDays(-2).ToString('o')}
        'Future' {$web.VerifiedUtc=[DateTime]::UtcNow.AddHours(1).ToString('o')}
        'BadDate' {$web.VerifiedUtc='bad'}
        'MissingContainer' {$web.ProductionContainerId=$null}
        'ProbeError' {$probe.Error='Failed'}
        'WrongRoot' {$probe.Roots[0].Root='C:\ocr-test\certs'}
        'DuplicateRoot' {$probe.Roots[1].Root='C:\uploads'}
        'NotWritable' {$probe.Roots[0].Writable=$false}
        'NotAtomic' {$probe.Roots[0].AtomicMove=$false}
        'StringBoolean' {$web.Protected='true'}
        'MissingError' {$probe.PSObject.Properties.Remove('Error')}
    }
    $rejected=$false
    try { Assert-CertificateSyncEvidence -Web $web -Probe $probe -Revision $revision } catch {$rejected=$true}
    Assert-Task ($rejected -eq ($scenario -ne 'Ready')) "Evidence guard $scenario"
}
function New-ScheduledTaskAction {param($Execute,$WorkingDirectory,$Argument); [pscustomobject]@{Execute=$Execute;Argument=$Argument}}
function New-ScheduledTaskPrincipal {param($UserId,$LogonType,$RunLevel); [pscustomobject]@{User=$UserId;Logon=$LogonType;Level=$RunLevel}}
function New-ScheduledTaskTrigger {param([switch]$AtLogOn,$User,[switch]$Once,$At,$RepetitionInterval); [pscustomobject]@{Logon=[bool]$AtLogOn;Interval=$RepetitionInterval}}
function New-ScheduledTaskSettingsSet {param($ExecutionTimeLimit,$MultipleInstances,[switch]$AllowStartIfOnBatteries,[switch]$DontStopIfGoingOnBatteries,[switch]$StartWhenAvailable); [pscustomobject]@{Limit=$ExecutionTimeLimit;Instances=$MultipleInstances}}
function Register-ScheduledTask {param($TaskName,$Action,$Principal,$Trigger,$Settings,$Description); $script:task=[pscustomobject]@{Name=$TaskName;Action=$Action;Principal=$Principal;Triggers=$Trigger;Settings=$Settings}}
Register-CertificateSyncTask
Assert-Task ($task.Name -eq 'OCR-LegacyCertificateSync') 'Exact task name'
Assert-Task ($task.Principal.User -eq 'KEMET\2172172205529' -and $task.Principal.Logon -eq 'Interactive' -and $task.Principal.Level -eq 'Limited') 'Limited interactive account'
Assert-Task ($task.Action.Argument -eq '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "C:\ocr-deploy\Sync-LegacyCertificates.ps1"') 'Hidden fixed script, default bounded run'
Assert-Task ($task.Triggers.Count -eq 2 -and $task.Triggers[0].Logon -and $task.Triggers[1].Interval.TotalMinutes -eq 1) 'Logon plus indefinite minute trigger'
Assert-Task ($task.Settings.Limit.TotalMinutes -eq 3 -and $task.Settings.Instances -eq 'IgnoreNew') 'Timeout and overlap prevention'
$source=Get-Content -LiteralPath $path -Raw
Assert-Task ($source -notmatch 'Start-ScheduledTask|Unregister-ScheduledTask|Set-Service|Password|state.json') 'No immediate start, service changes, credentials, or initial state dependency'
Assert-Task ($source.IndexOf('if ($ValidateOnly)') -lt $source.LastIndexOf('Register-CertificateSyncTask')) 'Validation precedes registration'
$original=$env:COMPUTERNAME
try {
    $env:COMPUTERNAME='OFFLINE-NOT-HOST'; $rejected=$false
    try { & $path -ValidateOnly -VerifiedWebRevision $revision } catch {$rejected=$_.Exception.Message -eq 'Unexpected host.'}
    Assert-Task $rejected 'Wrong host refused without task access'
} finally {$env:COMPUTERNAME=$original}
function Test-CertificateAcl([string]$Scenario,[bool]$ExpectedRejection) {
    function Test-Path {$Scenario -ne 'Missing'}
    function Get-Item {
        $flags=[IO.FileAttributes]::Directory
        if ($Scenario -eq 'Reparse') {$flags=$flags -bor [IO.FileAttributes]::ReparsePoint}
        [pscustomobject]@{Attributes=$flags;PSIsContainer=$true;Parent=$null}
    }
    function Get-Acl {
        $acl=[pscustomobject]@{AreAccessRulesProtected=($Scenario -ne 'Inherited')}
        $acl | Add-Member ScriptMethod GetOwner {param($type); [pscustomobject]@{Value=$(if ($Scenario -eq 'Owner') {'S-1-1-0'} else {'S-1-5-18'})}}
        $acl | Add-Member ScriptMethod GetAccessRules {
            param($a,$b,$c)
            [pscustomobject]@{AccessControlType=[Security.AccessControl.AccessControlType]::Allow;IdentityReference=[pscustomobject]@{Value=$(if ($Scenario -eq 'Broad') {'S-1-1-0'} else {'S-1-5-18'})}}
        }
        $acl
    }
    $rejected=$false
    try {Assert-CertificateSyncPrivatePath -Path 'C:\offline' -ProtectedRoot} catch {$rejected=$true}
    Assert-Task ($rejected -eq $ExpectedRejection) "Private ACL guard $Scenario"
}
Test-CertificateAcl 'Trusted' $false
foreach ($scenario in @('Missing','Reparse','Inherited','Owner','Broad')) {Test-CertificateAcl $scenario $true}
foreach ($scenario in @('Matching','ContainerChanged','RevisionChanged','Unavailable')) {
    function Get-CertificateProductionIdentity {
        if ($scenario -eq 'Unavailable') {throw 'Offline fake error'}
        [pscustomobject]@{ContainerId=$(if ($scenario -eq 'ContainerChanged') {'c'*64} else {'b'*64});Revision=$(if ($scenario -eq 'RevisionChanged') {'c'*40} else {'a'*40})}
    }
    $rejected=$false
    try {Assert-CertificateLiveProduction -Web ([pscustomobject]@{ProductionContainerId=('b'*64);Revision=('a'*40)})} catch {$rejected=$true}
    Assert-Task ($rejected -eq ($scenario -ne 'Matching')) "Live production guard $scenario"
}
Write-Output "$checks certificate task checks passed; no host changes."
