$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Isolate-TestStorage.ps1')
function Expect-Failure([scriptblock]$Action) { $failed = $false; try { & $Action } catch { $failed = $true }; if (!$failed) { throw 'Expected refusal.' } }
function Check([bool]$Condition) { if (!$Condition) { throw 'Assertion failed.' } }
Check ((Normalize-IsolationPath '/run/desktop/mnt/host/c/uploads') -eq 'c:\uploads')
Check ((Normalize-IsolationPath 'C:\uploads\') -eq 'c:\uploads')
$before = [pscustomobject]@{ A=1; B=@('x'); Binds=@('old'); RestartPolicy=[pscustomobject]@{Name='unless-stopped'} }
$after = [pscustomobject]@{ A=1; B=@('x'); Binds=@('new'); RestartPolicy=[pscustomobject]@{Name='no'} }
Assert-IsolationEquivalent $before $after @('Binds','RestartPolicy')
$after.A=2
Expect-Failure { Assert-IsolationEquivalent $before $after @('Binds','RestartPolicy') }
Check (Test-IsolationPage 200 '<title>Dashboard - OTD System</title>')
Check (!(Test-IsolationPage 200 '<title>Other</title>'))
Check (!(Test-IsolationPage 500 '<title>Dashboard - OTD System</title>'))
Expect-Failure { Assert-IsolationEnvironment @('ConnectionStrings__DefaultConnection=Server=unused;Database=production;Integrated Security=true') }
Assert-IsolationEnvironment @('ConnectionStrings__DefaultConnection=Server=unused;Database=OperatorCertificationRecordDB_Test;Integrated Security=true')
Expect-Failure { Assert-IsolationEnvironment @('bad') }
function Assert-IsolationTree { throw 'Excluded source tree must never be enumerated.' }
function robocopy { throw 'Excluded directory must never be copied.' }
Copy-IsolationData @(@{Leaf='certs';Source='C:\uploads';Target='C:\ocr-test\certs'}, @{Leaf='dataprotection';Source='C:\ocr-dataprotection';Target='C:\ocr-test\dataprotection'})
$priorPath = $env:PATH
try {
    # Exercise the actual native-child boundary with a harmless Windows executable,
    # while PATH matches a Linux image. Docker itself is never called by this test.
    $script:IsolationDockerExe = Join-Path $env:SystemRoot 'System32\cmd.exe'
    $env:PATH = '/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin'
    $nativeResult = Invoke-IsolationDocker @('/d','/c','echo','pinned-native-child-ok')
    Check ($nativeResult.Trim() -eq 'pinned-native-child-ok')
} finally {
    $env:PATH = $priorPath
    $script:IsolationDockerExe = $null
}
$oldStreams = [pscustomobject]@{ AttachStdin=$false; AttachStdout=$false; AttachStderr=$false; OpenStdin=$false; StdinOnce=$false; Tty=$false; Cmd=@('unchanged') }
$newStreams = [pscustomobject]@{ AttachStdin=$false; AttachStdout=$true; AttachStderr=$true; OpenStdin=$false; StdinOnce=$false; Tty=$false; Cmd=@('unchanged') }
Assert-IsolationCandidateConfig $oldStreams $newStreams
$newStreams.AttachStdin=$true
Expect-Failure { Assert-IsolationCandidateConfig $oldStreams $newStreams }
$newStreams.AttachStdin=$false
$newStreams.Cmd=@('changed')
Expect-Failure { Assert-IsolationCandidateConfig $oldStreams $newStreams }
Assert-IsolationEnvironmentEquivalent @('A=one','B=two') @('B=two','A=one')
Expect-Failure { Assert-IsolationEnvironmentEquivalent @('A=one') @('A=one','A=one') }
Expect-Failure { Assert-IsolationEnvironmentEquivalent @('A=one') @('A=ONE') }
Assert-IsolationHostConfig ([pscustomobject]@{Dns=@(); CapAdd=$null}) ([pscustomobject]@{Dns=$null;CapAdd=@()})
Expect-Failure { Assert-IsolationHostConfig ([pscustomobject]@{Dns=@('1.2.3.4')}) ([pscustomobject]@{Dns=$null}) }
Expect-Failure { Assert-IsolationEquivalent ([pscustomobject]@{Cmd=@()}) ([pscustomobject]@{Cmd=$null}) @() }
$differenceMessage = ''
try { Assert-IsolationEquivalent ([pscustomobject]@{First=1;Second=2}) ([pscustomobject]@{First=3;Second=4}) @() } catch { $differenceMessage=$_.Exception.Message }
Check ($differenceMessage -match 'First' -and $differenceMessage -match 'Second')
Assert-IsolationHostConfig ([pscustomobject]@{OomKillDisable=$null}) ([pscustomobject]@{OomKillDisable=$false})
Expect-Failure { Assert-IsolationHostConfig ([pscustomobject]@{OomKillDisable=$null}) ([pscustomobject]@{OomKillDisable=$true}) }
Write-Output 'PASS 24 offline safety assertions (no Docker or remote operations).'
