$ErrorActionPreference = 'Stop'
$workflow = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\.github\workflows\ci-cd.yml') -Raw
$deployment = ($workflow -split '(?m)^  deploy:', 2)[1]
$checks = @{
    'Only immutable action references' = ($workflow -notmatch 'uses:\s*[^\s@]+@(?![a-f0-9]{40}\b)')
    'No retained checkout credentials' = (($workflow | Select-String 'persist-credentials: false' -AllMatches).Matches.Count -eq 4)
    'Main-only publication' = ($workflow -match "github.ref == 'refs/heads/main'")
    'Explicit web tests project' = ($workflow -match 'dotnet test tests/OperatorCertificationRecord.Web.Tests/OperatorCertificationRecord.Web.Tests.csproj')
    'No legacy solution build' = ($workflow -notmatch 'dotnet (build|test|restore)\s+\S+\.sln')
    'Host serialization' = ($workflow -match 'group: ocr-host-deploy' -and $workflow -match 'cancel-in-progress: false')
    'Pinned deployment image' = ($workflow -match 'containerimage.digest' -and $workflow -match '@sha256:')
    'No inline database credentials' = ($workflow -notmatch 'ConnectionStrings__|Password=')
    'Private transient registry credentials' = ($deployment -match "New-OcrRegistryConfig -Root 'C:\\ocr-deploy'" -and $deployment -match 'Remove-OcrRegistryConfig')
    'No deployment native credential login or logout' = ($deployment -notmatch 'docker(\.exe)?\s+(login|logout)')
    'Inherited registry override cleared' = ($deployment -match 'Remove-Item Env:DOCKER_AUTH_CONFIG')
    'Token removed before Docker deployment' = ($deployment -match '(?s)Remove-Item Env:GH_TOKEN.*?& ./deploy/Deploy-Ocr.ps1')
    'Windows PowerShell offline checks' = ($workflow -match 'runs-on: windows-latest' -and $workflow -match 'shell: powershell' -and $workflow -match 'test-Deploy-Ocr.ps1')
    'No storage migration in deployment' = ($workflow -notmatch '&\s+./deploy/Isolate-TestStorage.ps1')
}
foreach ($check in $checks.GetEnumerator()) {
    if (-not $check.Value) { throw "Workflow contract failed: $($check.Key)" }
    Write-Output "PASS: $($check.Key)"
}
Write-Output "$($checks.Count) workflow contract checks passed (text contracts, not a YAML parser)."
