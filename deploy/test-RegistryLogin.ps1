$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RegistryLogin.ps1')
$shell = (Get-Command powershell.exe -CommandType Application).Source
function Invoke-TestChild {
    param([string]$Command, [string]$InputText, [int]$TimeoutSeconds = 10)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Command))
    Invoke-OcrSecretStdinProcess -FileName $shell -Arguments "-NoProfile -NonInteractive -EncodedCommand $encoded" -InputText $InputText -TimeoutSeconds $TimeoutSeconds
}
$sample = 'dummy_ascii_token_' + [char]0x00e9
$result = Invoke-TestChild -InputText $sample -Command '$s=[Console]::OpenStandardInput(); $m=New-Object IO.MemoryStream; $s.CopyTo($m); [Console]::Write([Convert]::ToBase64String($m.ToArray())); exit 0'
if ($result.ExitCode -ne 0 -or [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($result.StandardOutput)) -cne $sample) { throw 'Stdin must be exact UTF8 without BOM or newline.' }
$result = Invoke-TestChild -InputText 'dummy' -Command '[Console]::Out.Write("success"); [Console]::Error.Write("warning"); exit 0'
if ($result.ExitCode -ne 0 -or $result.StandardOutput -ne 'success' -or $result.StandardError -ne 'warning') { throw 'Successful stderr warning capture failed.' }
$result = Invoke-TestChild -InputText 'dummy' -Command '[Console]::Error.Write("denied"); exit 7'
if ($result.ExitCode -ne 7 -or $result.StandardError -ne 'denied') { throw 'Native exit code lost.' }
$result = Invoke-TestChild -InputText 'dummy' -Command '[Console]::Out.Write(("o" * 100000)); [Console]::Error.Write(("e" * 100000)); exit 0'
if ($result.StandardOutput.Length -ne 100000 -or $result.StandardError.Length -ne 100000) { throw 'Concurrent stream drain failed.' }
$timedOut = $false
try { Invoke-TestChild -InputText 'dummy' -TimeoutSeconds 1 -Command 'Start-Sleep -Seconds 30' | Out-Null }
catch { $timedOut = $_.Exception.Message -eq 'Registry client process timed out.' }
if (-not $timedOut) { throw 'Bounded process timeout failed.' }
$failedSafely = $false
try { Invoke-OcrSecretStdinProcess -FileName 'missing-dummy-client.exe' -Arguments '' -InputText 'secret-sentinel' | Out-Null }
catch { $failedSafely = $_.Exception.Message -eq 'Registry client process could not complete.' }
if (-not $failedSafely) { throw 'Process launch error must not expose input.' }
$helper = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RegistryLogin.ps1') -Raw
if ($helper -notmatch "https://api.github.com/repos/akwaa2545-gif/OCR" -or $helper -notmatch 'AllowAutoRedirect = \$false') { throw 'Repository diagnostic endpoint is not pinned.' }
Write-Output '7 registry transport checks passed.'
