$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot 'Initialize-HostConfig.ps1'
$source = Get-Content -LiteralPath $path -Raw
$tokens = $null
$errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Host configuration script does not parse.' }
$checks = 0
function Assert-HostConfig([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "Host configuration check failed: $Name" }
    $script:checks++
    Write-Output "PASS: $Name"
}
Assert-HostConfig ($source -match 'refusing to overwrite') 'Existing configuration is never overwritten'
Assert-HostConfig ($source -match 'SqlConnectionStringBuilder' -and $source -match 'InitialCatalog -cne') 'Connection database validated'
Assert-HostConfig ($source -match '\$entries.Count -ne 1' -and $source -match '\[`r`n\]') 'Duplicate and newline entries rejected'
Assert-HostConfig ($source -match 'ReparsePoint' -and $source -match '\$directory = \$directory.Parent') 'Storage ancestors checked for redirection'
Assert-HostConfig ($source -match 'SetAccessRuleProtection\(\$true,\$false\)') 'Inherited ACL permissions removed'
Assert-HostConfig ($source -match 'S-1-5-18' -and $source -match 'S-1-5-32-544' -and $source -match 'GetCurrent\(\).User.Value') 'System administrators and current operator receive access'
Assert-HostConfig ($source.IndexOf('Set-Acl -LiteralPath') -lt $source.IndexOf('[IO.File]::WriteAllText')) 'Restrictive ACL applied before credentials written'
Assert-HostConfig ($source.IndexOf('if ($ValidateOnly)') -lt $source.IndexOf('New-Item -ItemType')) 'Validation returns before writes'
Assert-HostConfig ($source -notmatch '(?im)Write-(Output|Host|Verbose|Warning).*\$(connections|entries|raw|builder)') 'Sensitive values are not explicitly logged'
Assert-HostConfig ($source -match "AppDataRoot='C:\\ocr-appdata'" -and $source -match "AppDataRoot='C:\\ocr-test\\appdata'") 'Application data roots separated'

$originalName = $env:COMPUTERNAME
try {
    $env:COMPUTERNAME = 'OCR-OFFLINE-TEST-NOT-THE-HOST'
    $refused = $false
    try { & $path -ValidateOnly } catch { $refused = $_.Exception.Message -eq 'Unexpected deployment host.' }
    Assert-HostConfig $refused 'Actual invocation refuses wrong host before inspecting Docker'
} finally { $env:COMPUTERNAME = $originalName }
Write-Output "$checks host configuration checks passed (AST/text contracts and wrong-host refusal; no host changes)."
