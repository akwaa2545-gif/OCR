$ErrorActionPreference = 'Stop'
$workflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\.github\workflows\ci-cd.yml') -Raw
$checks = @{
    'Only immutable action references' = ($workflow -notmatch 'uses:\s*[^\s@]+@(?![a-f0-9]{40}\b)')
    'No retained checkout credentials' = (($workflow | Select-String 'persist-credentials: false' -AllMatches).Matches.Count -eq 4)
    'Main-only publication' = ($workflow -match "github.ref == 'refs/heads/main'")
    'Explicit web tests project' = ($workflow -match 'dotnet test tests/OperatorCertificationRecord.Web.Tests/OperatorCertificationRecord.Web.Tests.csproj')
    'No legacy solution build' = ($workflow -notmatch 'dotnet (build|test|restore)\s+\S+\.sln')
    'Host serialization' = ($workflow -match 'group: ocr-host-deploy' -and $workflow -match 'cancel-in-progress: false')
    'Pinned deployment image' = ($workflow -match 'containerimage.digest' -and $workflow -match '@sha256:')
    'No inline database credentials' = ($workflow -notmatch 'ConnectionStrings__|Password=')
    'Transient registry credentials' = ($workflow -match 'DOCKER_CONFIG' -and $workflow -match 'docker logout ghcr.io')
    'Windows PowerShell offline checks' = ($workflow -match 'runs-on: windows-latest' -and $workflow -match 'shell: powershell' -and $workflow -match 'test-Deploy-Ocr.ps1')
    'No storage migration in deployment' = ($workflow -notmatch '&\s+./deploy/Isolate-TestStorage.ps1')
}
foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw "Workflow contract failed: $($check.Key)" }
    Write-Output "PASS: $($check.Key)"
}
Write-Output "$($checks.Count) workflow contract checks passed (text contracts, not a YAML parser)."

# Execute only the isolated, pure classifier function, never the workflow body.
$classifier = [regex]::Match($workflow, '(?ms)^ {10}function Get-OcrRegistryFailure \{.*?^ {10}\}')
if (-not $classifier.Success) { throw 'Registry failure classifier missing.' }
. ([scriptblock]::Create(($classifier.Value -replace '(?m)^ {10}', '')))
$registryCases = @(
    @('error saving credentials: error storing credentials - err: exit status 1', 'credential-storage'),
    @('docker-credential-wincred unavailable', 'credential-storage'),
    @('open //./pipe/dockerDesktopLinuxEngine: Access is denied.', 'docker-engine'),
    @('Cannot connect to the Docker daemon', 'docker-engine'),
    @('unauthorized: authentication required token-secret-sentinel', 'unauthorized'),
    @('denied: permission denied', 'unauthorized'),
    @('x509: certificate signed by unknown authority', 'tls'),
    @('TLS handshake timeout', 'tls'),
    @('dial tcp: lookup ghcr.io: no such host', 'network'),
    @('connection refused', 'network'),
    @('context deadline exceeded', 'network'),
    @('token-secret-sentinel unexplained error', 'unknown'),
    @('', 'unknown')
)
foreach ($case in $registryCases) {
    $actual = Get-OcrRegistryFailure -OutputLines @($case[0])
    if ($actual -cne $case[1]) { throw 'Registry diagnostic classification contract failed.' }
    if ($actual -match 'token-secret-sentinel') { throw 'Registry classifier leaked input.' }
}
$errorRecord = New-Object System.Management.Automation.ErrorRecord ([Exception]::new('error storing credentials token-secret-sentinel')), 'NativeCommandError', 'NotSpecified', $null
if ((Get-OcrRegistryFailure -OutputLines @($errorRecord)) -ne 'credential-storage') { throw 'Native stderr classification failed.' }
Write-Output '14 registry diagnostic tests passed; only fixed categories returned.'
