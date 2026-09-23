$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Configure-RunnerTask.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Runner configuration script does not parse.' }
$source = Get-Content -LiteralPath $scriptPath -Raw
$checks = 0
function Assert-RunnerCheck([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "Runner safety check failed: $Name" }
    $script:checks++
    Write-Output "PASS: $Name"
}
Assert-RunnerCheck ($source -match '-LogonType Interactive -RunLevel Limited') 'Limited interactive identity'
Assert-RunnerCheck ($source -match '-WindowStyle Hidden') 'Hidden runner wrapper'
Assert-RunnerCheck ($source -match '-ExecutionTimeLimit \(\[TimeSpan\]::Zero\)') 'Long-lived runner task'
Assert-RunnerCheck ($source -match 'exit \$LASTEXITCODE') 'Wrapper preserves runner exit status'
Assert-RunnerCheck ($source -match '\$_.ExecutablePath -ieq' -and $source -match '\$owner.Domain' -and $source -match '\$owner.User') 'Listener constrained by executable and owner'
$guard = $source.IndexOf("throw 'A runner already runs as the Docker user")
Assert-RunnerCheck ($guard -ge 0 -and $guard -lt $source.IndexOf('Register-ScheduledTask -TaskName')) 'Existing user listener rejected before mutation'

# This actual invocation must stop at the first guard, before any host API.
$originalHostName = $env:COMPUTERNAME
try {
    $env:COMPUTERNAME = 'OCR-OFFLINE-TEST-NOT-THE-HOST'
    $refused = $false
    try { & $scriptPath -ValidateOnly } catch { $refused = $_.Exception.Message -eq 'Unexpected host.' }
    Assert-RunnerCheck $refused 'Wrong host refused before accessing services/tasks'
} finally { $env:COMPUTERNAME = $originalHostName }

# Execute only the migration try/catch AST with all operating-system commands
# replaced by local fakes. Identity/preflight checks are verified above.
$migration = @($ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.TryStatementAst] })
Assert-RunnerCheck ($migration.Count -eq 1) 'One migration transaction'
$transaction = [scriptblock]::Create($migration[0].Extent.Text)
function Test-Migration([string]$Scenario) {
    $script:events = New-Object 'System.Collections.Generic.List[string]'
    $serviceName = 'offline-service'
    $taskName = 'offline-task'
    $runnerRoot = 'C:\offline-runner'
    $account = 'OFFLINE\user'
    $registered = $false
    $service = [pscustomobject]@{ StartMode='Auto'; State='Running' }
    function New-ScheduledTaskAction { [pscustomobject]@{Fake='Action'} }
    function New-ScheduledTaskPrincipal { [pscustomobject]@{Fake='Principal'} }
    function New-ScheduledTaskTrigger { [pscustomobject]@{Fake='Trigger'} }
    function New-ScheduledTaskSettingsSet { [pscustomobject]@{Fake='Settings'} }
    function Register-ScheduledTask { $script:events.Add('Register') }
    function Stop-Service { $script:events.Add('StopService') }
    function Set-Service { param($Name,$StartupType); $script:events.Add("Mode:$StartupType") }
    function Start-Service { $script:events.Add('StartService') }
    function Start-ScheduledTask {
        $script:events.Add('StartTask')
        if ($Scenario -ne 'Success') { throw 'Simulated task launch failure.' }
    }
    function Stop-ScheduledTask { $script:events.Add('StopTask') }
    function Unregister-ScheduledTask {
        param($TaskName,$Confirm)
        $script:events.Add('Unregister')
        if ($Scenario -eq 'RemovalFailure') { throw 'Simulated unregister failure.' }
    }
    function Get-UserRunnerProcess {
        if ($Scenario -in @('Success','RemainingListener')) { [pscustomobject]@{ProcessId=123} }
    }
    function Invoke-CimMethod { $script:events.Add('TerminateListener') }
    function Start-Sleep { }
    $caught = $false
    try { & $transaction | Out-Null } catch { $caught = $true }
    if ($Scenario -eq 'Success') {
        Assert-RunnerCheck (!$caught -and $script:events.Contains('Mode:Disabled') -and !$script:events.Contains('StartService')) 'Successful migration retains disabled service'
    } else {
        Assert-RunnerCheck $caught "$Scenario reports failure"
        Assert-RunnerCheck ($script:events.Contains('Mode:Automatic')) "$Scenario restores original startup mode"
        if ($Scenario -eq 'RemainingListener') {
            Assert-RunnerCheck ($script:events.Contains('TerminateListener') -and !$script:events.Contains('StartService')) 'Remaining listener prevents duplicate service startup'
        } else {
            Assert-RunnerCheck ($script:events.Contains('StartService')) "$Scenario restores running service despite cleanup failure"
        }
    }
}
Test-Migration 'Success'
Test-Migration 'LaunchFailure'
Test-Migration 'RemovalFailure'
Test-Migration 'RemainingListener'
Write-Output "$checks runner safety checks passed (offline mocks; no host changes)."
