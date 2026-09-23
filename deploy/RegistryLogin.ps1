function Assert-OcrPrivateRegistryRoot {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    if (-not [IO.Path]::IsPathRooted($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) { throw 'Registry root must be an existing absolute directory.' }
    $directory = Get-Item -LiteralPath $Root -Force
    if ($null -eq $directory.Parent) { throw 'Registry root cannot be a drive root.' }
    while ($null -ne $directory) {
        if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Registry path must not traverse reparse points.' }
        $directory = $directory.Parent
    }
    $allowed = @('S-1-5-18', 'S-1-5-32-544', [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
    $acl = Get-Acl -LiteralPath $Root
    if (-not $acl.AreAccessRulesProtected) { throw 'Registry root ACL must disable inheritance.' }
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $allowed) { throw 'Registry root owner is not trusted.' }
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq [Security.AccessControl.AccessControlType]::Allow -and $rule.IdentityReference.Value -notin $allowed) { throw 'Registry root allows an untrusted identity.' }
    }
}

function New-OcrRegistryConfig {
    [CmdletBinding()]
    param(
        [string]$Root = 'C:\ocr-deploy',
        [Parameter(Mandatory)][string]$Actor,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Token
    )
    if ($Actor -notmatch '^[a-zA-Z0-9\[\]-]{1,100}$') { throw 'Invalid registry actor.' }
    if ([string]::IsNullOrWhiteSpace($Token) -or $Token -match '\s' -or [Text.Encoding]::UTF8.GetByteCount($Token) -gt 2048) { throw 'Invalid registry token shape.' }
    Assert-OcrPrivateRegistryRoot -Root $Root
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($sid)
    foreach ($identity in @('S-1-5-18', 'S-1-5-32-544', $sid.Value)) {
        $principal = New-Object Security.Principal.SecurityIdentifier($identity)
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    $path = Join-Path ([IO.Path]::GetFullPath($Root)) ('ocr-registry-' + [guid]::NewGuid().ToString('N'))
    $created = $false
    try {
        if (Test-Path -LiteralPath $path) { throw 'Registry directory already exists.' }
        # .NET Framework creates the directory with its restrictive DACL atomically.
        $null = [IO.Directory]::CreateDirectory($path, $acl)
        $created = $true
        Assert-OcrPrivateRegistryRoot -Root $path
        $encoding = New-Object Text.UTF8Encoding($false)
        $auth = [Convert]::ToBase64String($encoding.GetBytes($Actor + ':' + $Token))
        $json = @{ auths = @{ 'ghcr.io' = @{ auth = $auth } } } | ConvertTo-Json -Depth 4 -Compress
        [IO.File]::WriteAllText((Join-Path $path 'config.json'), $json, $encoding)
        return $path
    } catch {
        if ($created) {
            try { Remove-OcrRegistryConfig -Root $Root -Path $path }
            catch { Write-Warning 'Failed registry configuration requires private-directory cleanup.' }
        }
        throw 'Private registry configuration could not be created.'
    }
}

function Remove-OcrRegistryConfig {
    [CmdletBinding()]
    param([string]$Root = 'C:\ocr-deploy', [Parameter(Mandatory)][string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ((Split-Path $target -Parent) -ine $rootPath -or (Split-Path $target -Leaf) -notmatch '^ocr-registry-[a-f0-9]{32}$') { throw 'Registry cleanup target is outside the expected private directory.' }
    Assert-OcrPrivateRegistryRoot -Root $rootPath
    if (-not (Test-Path -LiteralPath $target)) { return }
    Assert-OcrPrivateRegistryRoot -Root $target
    $entries = @(Get-ChildItem -LiteralPath $target -Force)
    foreach ($entry in $entries) {
        if ($entry.Name -cne 'config.json' -or $entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Registry cleanup refused unexpected content.' }
    }
    # Only the exact credential file and now-empty GUID directory are removed.
    foreach ($entry in $entries) { Remove-Item -LiteralPath $entry.FullName -Force -ErrorAction Stop }
    Remove-Item -LiteralPath $target -Force -ErrorAction Stop
}
