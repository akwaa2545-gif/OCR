$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RegistryLogin.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('ocr-auth-test-' + [guid]::NewGuid().ToString('N'))
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$acl.SetOwner($sid)
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
$null = [IO.Directory]::CreateDirectory($root, $acl)
$passed = 0
function Assert-AuthFailure {
    param([scriptblock]$Action)
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Expected authentication configuration rejection.' }
    $script:passed++
}
try {
    $path = New-OcrRegistryConfig -Root $root -Actor 'test-user' -Token 'dummy-token'
    $file = Join-Path $path 'config.json'
    $bytes = [IO.File]::ReadAllBytes($file)
    if ($bytes[0] -eq 239) { throw 'Unexpected UTF8 BOM.' }
    $config = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($config.auths.'ghcr.io'.auth)) -cne 'test-user:dummy-token') { throw 'Incorrect registry auth encoding.' }
    if ($config.PSObject.Properties['credsStore'] -or $config.PSObject.Properties['credHelpers']) { throw 'Native helpers must not be configured.' }
    $passed++
    Assert-OcrPrivateRegistryRoot -Root $path
    $passed++
    Remove-OcrRegistryConfig -Root $root -Path $path
    if (Test-Path -LiteralPath $path) { throw 'Transient config not removed.' }
    $passed++
    Assert-AuthFailure { New-OcrRegistryConfig -Root $root -Actor 'user:bad' -Token 'dummy' }
    Assert-AuthFailure { New-OcrRegistryConfig -Root $root -Actor 'user' -Token '' }
    Assert-AuthFailure { New-OcrRegistryConfig -Root $root -Actor 'user' -Token "dummy`n" }
    Assert-AuthFailure { New-OcrRegistryConfig -Root $root -Actor 'user' -Token ('x' * 2049) }
    Assert-AuthFailure { Remove-OcrRegistryConfig -Root $root -Path $root }
    Assert-AuthFailure { Remove-OcrRegistryConfig -Root $root -Path (Join-Path ([IO.Path]::GetTempPath()) ('ocr-registry-' + [guid]::NewGuid().ToString('N'))) }
    $broad = [IO.Directory]::GetAccessControl($root, [Security.AccessControl.AccessControlSections]::Access)
    $broad.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier('S-1-1-0')), 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    [IO.Directory]::SetAccessControl($root, $broad)
    Assert-AuthFailure { New-OcrRegistryConfig -Root $root -Actor 'user' -Token 'dummy' }
    [IO.Directory]::SetAccessControl($root, $acl)
    $path = New-OcrRegistryConfig -Root $root -Actor 'test-user' -Token 'dummy-token'
    $extra = Join-Path $path 'unexpected'
    $null = New-Item -ItemType Directory -Path $extra
    Assert-AuthFailure { Remove-OcrRegistryConfig -Root $root -Path $path }
    Remove-Item -LiteralPath $extra
    Remove-OcrRegistryConfig -Root $root -Path $path
    Write-Output "$passed registry config security checks passed."
} finally {
    # Exact self-created test root only; no recursive delete on failed tests.
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) { Remove-Item -LiteralPath $root }
    else { Write-Warning 'Test artifacts retained for inspection.' }
}
