$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
$env:DOCKER_HOST = 'npipe:////./pipe/dockerDesktopLinuxEngine'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$ErrorActionPreference = 'Continue'
$output = & 'C:\Program Files\Docker\Docker\resources\bin\docker.exe' info --format '{{.OSType}}' 2>&1
$dockerExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$result = [ordered]@{Account=$identity.Name;Elevated=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator);DockerExit=$dockerExit;DockerResult=($output -join "`n");CanReadConfiguration=$false}
try { $stream=[IO.File]::OpenRead('C:\ocr-deploy\test.env'); $stream.Dispose(); $result.CanReadConfiguration=$true } catch { }
[IO.File]::WriteAllText('C:\ocr-deploy\runner-access-check.json',($result|ConvertTo-Json),(New-Object Text.UTF8Encoding($false)))
