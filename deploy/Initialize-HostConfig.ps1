[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected deployment host.' }
$root = 'C:\ocr-deploy'
if (Test-Path -LiteralPath $root) { throw 'Deployment configuration already exists; refusing to overwrite it.' }
$configs = @{
    prod = [ordered]@{ContainerName='ocrweb';Port=8080;CertificatesRoot='C:\uploads';UploadRoot='C:\ocr-uploads';PhotosRoot='C:\ocr-photos';DataProtectionRoot='C:\ocr-dataprotection';AppDataRoot='C:\ocr-appdata';ExpectedDatabase='OperatorCertificationRecordDB'}
    test = [ordered]@{ContainerName='ocrwebtest';Port=5050;CertificatesRoot='C:\ocr-test\certs';UploadRoot='C:\ocr-test\uploads';PhotosRoot='C:\ocr-test\photos';DataProtectionRoot='C:\ocr-test\dataprotection';LegacyTestUploadRoot='C:\ocr-test\uploads-test';AppDataRoot='C:\ocr-test\appdata';ExpectedDatabase='OperatorCertificationRecordDB_Test'}
}
$connections = @{}
foreach ($name in @('prod','test')) {
    $config = $configs[$name]
    $raw = & docker.exe inspect $config.ContainerName 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the existing container.' }
    $container = @($raw | ConvertFrom-Json)[0]
    $entries = @($container.Config.Env | Where-Object { $_ -like 'ConnectionStrings__DefaultConnection=*' })
    if ($entries.Count -ne 1 -or $entries[0] -match "[`r`n]") { throw 'Unexpected connection-string environment entry.' }
    try { $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList (($entries[0] -split '=',2)[1]) }
    catch { throw 'Invalid existing connection configuration.' }
    if ($builder.InitialCatalog -cne $config.ExpectedDatabase) { throw 'Existing container database differs from expected environment.' }
    $connections[$name] = $entries[0]
    foreach ($field in @('CertificatesRoot','UploadRoot','PhotosRoot','DataProtectionRoot','LegacyTestUploadRoot','AppDataRoot')) {
        if (!$config.Contains($field)) { continue }
        $path = $config[$field]
        if (!(Test-Path -LiteralPath $path -PathType Container)) { throw 'A configured storage directory is missing.' }
        $directory = Get-Item -LiteralPath $path
        while ($directory) {
            if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Storage cannot traverse reparse points.' }
            $directory = $directory.Parent
        }
    }
}
if ($ValidateOnly) { Write-Output 'Host configuration inputs validated; no changes.'; return }
# Restrict the new directory before writing credentials; existing containers and
# their environment variables are not modified. No password is printed.
$null = New-Item -ItemType Directory -Path $root
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
foreach ($sid in @('S-1-5-18','S-1-5-32-544',[Security.Principal.WindowsIdentity]::GetCurrent().User.Value)) {
    $identity = New-Object Security.Principal.SecurityIdentifier($sid)
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl',$inheritance,'None','Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $root -AclObject $acl
$encoding = New-Object Text.UTF8Encoding($false)
foreach ($name in @('prod','test')) {
    [IO.File]::WriteAllText((Join-Path $root "$name.json"),($configs[$name] | ConvertTo-Json),$encoding)
    [IO.File]::WriteAllText((Join-Path $root "$name.env"),$connections[$name]+[Environment]::NewLine,$encoding)
}
$connections.Clear()
Write-Output 'Protected host configuration created. Existing container credentials reused locally; no databases or containers changed.'
