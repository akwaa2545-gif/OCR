[CmdletBinding()]
param(
    [ValidateSet('test', 'prod')][string]$Environment,
    [string]$Image,
    [string]$ConfigRoot = 'C:\ocr-deploy',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-OcrNative {
    param([string]$Executable, [string[]]$Arguments)
    try {
        $command = Get-Command -Name $Executable -CommandType Application -ErrorAction Stop
        # PS5.1 wraps stderr as ErrorRecord even on exit zero. Capture it without
        # terminating, then use the native exit status and return stdout only.
        $ErrorActionPreference = 'Continue'
        $PSNativeCommandUseErrorActionPreference = $false
        $result = @(& $command.Source @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } catch { throw 'Native operation could not complete.' }
    if ($null -eq $exitCode -or $exitCode -ne 0) { throw "Native operation failed (exit $exitCode)." }
    return (($result | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n")
}

function Invoke-OcrDocker {
    param([string[]]$Arguments)
    # Never print native output on error: inspect/env-file errors may contain secrets.
    try { return Invoke-OcrNative -Executable 'docker.exe' -Arguments $Arguments }
    catch { throw "Docker operation '$($Arguments[0])' could not complete." }
}

function Get-OcrPathKey {
    param([string]$Path)
    $value = $Path.Replace('\', '/').TrimEnd('/').ToLowerInvariant()
    $value = $value -replace '^/(run/desktop/mnt/host|host_mnt)/([a-z])/', '$2:/'
    return $value
}

function Get-OcrDatabase {
    param([string]$ConnectionString)
    try {
        $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder -ArgumentList $ConnectionString
        if ([string]::IsNullOrWhiteSpace($builder.InitialCatalog)) { throw 'Missing database.' }
        return $builder.InitialCatalog
    } catch { throw 'Connection string is invalid or has no explicit database.' }
}

function Get-OcrRootFields {
    param($Config, [string]$Name)
    $fields = @('CertificatesRoot', 'UploadRoot', 'PhotosRoot', 'DataProtectionRoot')
    if ($Config.PSObject.Properties['AppDataRoot']) {
        if ([string]::IsNullOrWhiteSpace([string]$Config.AppDataRoot)) { throw 'AppDataRoot must not be empty when configured.' }
        $fields += 'AppDataRoot'
    }
    if ($Config.PSObject.Properties['LegacyTestUploadRoot']) {
        if ($Name -ne 'test') { throw 'LegacyTestUploadRoot is only valid for test.' }
        if ([string]::IsNullOrWhiteSpace([string]$Config.LegacyTestUploadRoot)) { throw 'LegacyTestUploadRoot must not be empty when configured.' }
        $fields += 'LegacyTestUploadRoot'
    }
    return $fields
}

function Read-OcrConfiguration {
    param([string]$Root, [string]$Name)
    $config = Get-Content -LiteralPath (Join-Path $Root "$Name.json") -Raw | ConvertFrom-Json
    foreach ($field in @('ContainerName', 'Port', 'CertificatesRoot', 'UploadRoot', 'PhotosRoot', 'DataProtectionRoot', 'ExpectedDatabase')) {
        if (-not $config.PSObject.Properties[$field] -or [string]::IsNullOrWhiteSpace([string]$config.$field)) {
            throw "$Name configuration is missing $field."
        }
    }
    if ($config.ContainerName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,62}$') { throw "$Name container name is invalid." }
    $portNumber = 0
    if (-not [int]::TryParse([string]$config.Port, [ref]$portNumber) -or $portNumber -lt 1 -or $portNumber -gt 65535) { throw "$Name port is invalid." }
    $roots = @()
    foreach ($field in (Get-OcrRootFields $config $Name)) {
        $path = [string]$config.$field
        if ($path -notmatch '^[A-Za-z]:[\\/].+' -or $path.Contains(',') -or $path.Contains('"') -or $path.Contains("`n")) { throw "$Name $field must be a local absolute directory." }
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "$Name $field directory does not exist." }
        $resolved = (Get-Item -LiteralPath $path).FullName
        $directory = Get-Item -LiteralPath $path
        while ($null -ne $directory) {
            if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$Name $field must not traverse a junction or symbolic link." }
            $directory = $directory.Parent
        }
        $config.$field = $resolved
        $roots += Get-OcrPathKey $resolved
    }
    for ($i = 0; $i -lt $roots.Count; $i++) {
        for ($j = $i + 1; $j -lt $roots.Count; $j++) {
            if ($roots[$i] -eq $roots[$j] -or $roots[$i].StartsWith($roots[$j] + '/') -or $roots[$j].StartsWith($roots[$i] + '/')) { throw "$Name mount roots must be separate, non-nested directories." }
        }
    }
    $envPath = Join-Path $Root "$Name.env"
    $connection = $null
    foreach ($line in Get-Content -LiteralPath $envPath) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
        if ($line -notmatch '^ConnectionStrings__DefaultConnection=(.+)$' -or $null -ne $connection) {
            throw "$Name.env must contain only one ConnectionStrings__DefaultConnection entry (and optional comments)."
        }
        $connection = $Matches[1]
    }
    if ($null -eq $connection -or (Get-OcrDatabase $connection) -ne $config.ExpectedDatabase) { throw "$Name connection database does not match ExpectedDatabase." }
    return $config
}

function Assert-OcrSeparation {
    param($TestConfig, $ProdConfig)
    foreach ($field in @('ContainerName', 'Port', 'ExpectedDatabase')) {
        if ([string]$TestConfig.$field -eq [string]$ProdConfig.$field) { throw "Test and production must have different $field values." }
    }
    foreach ($left in (Get-OcrRootFields $TestConfig 'test')) {
        foreach ($right in (Get-OcrRootFields $ProdConfig 'prod')) {
            $a = Get-OcrPathKey $TestConfig.$left
            $b = Get-OcrPathKey $ProdConfig.$right
            if ($a -eq $b -or $a.StartsWith($b + '/') -or $b.StartsWith($a + '/')) { throw 'Test and production mount roots overlap.' }
        }
    }
}

function Get-OcrContainer {
    param([string]$Name)
    $names = Invoke-OcrDocker @('container', 'ls', '-a', '--format', '{{.Names}}')
    if (($names -split "`r?`n") -notcontains $Name) { return $null }
    return @((Invoke-OcrDocker @('container', 'inspect', $Name) | ConvertFrom-Json))[0]
}

function Assert-OcrExistingContainer {
    param($Container, $Config, [string]$Name)
    $rootFields = @(Get-OcrRootFields $Config $Name)
    if ($null -eq $Container) { return }
    $label = $null
    $hasEnvironmentLabel = $null -ne $Container.Config.Labels -and $Container.Config.Labels.PSObject.Properties['ocr.environment']
    if ($hasEnvironmentLabel) { $label = $Container.Config.Labels.'ocr.environment' }
    if ($label -and $label -ne $Name) { throw 'Existing container belongs to another environment.' }
    if (-not $Container.HostConfig.PortBindings.PSObject.Properties['80/tcp']) { throw 'Existing container port mapping does not match host configuration.' }
    $ports = @($Container.HostConfig.PortBindings.'80/tcp')
    if ($ports.Count -ne 1 -or [string]$ports[0].HostPort -ne [string]$Config.Port -or @($Container.HostConfig.PortBindings.PSObject.Properties).Count -gt 1 -or $ports[0].HostIp -notin @('', '0.0.0.0')) { throw 'Existing container port mapping does not match host configuration.' }
    $destinations = @{
        '/app/wwwroot/certs' = 'CertificatesRoot'; '/app/dataprotection' = 'DataProtectionRoot'
        '/app/photo-source' = 'UploadRoot'; '/app/wwwroot/uploads' = 'UploadRoot'
        '/app/legacy-photos' = 'PhotosRoot'; '/app/wwwroot/photos' = 'PhotosRoot'
    }
    if ($rootFields -contains 'LegacyTestUploadRoot') {
        $destinations['/app/wwwroot/uploads-test'] = 'LegacyTestUploadRoot'
    }
    if ($rootFields -contains 'AppDataRoot') { $destinations['/app/App_Data'] = 'AppDataRoot' }
    $seen = @()
    $seenDestinations = @()
    foreach ($mount in $Container.Mounts) {
        if ($mount.Type -ne 'bind' -or $destinations.Keys -cnotcontains $mount.Destination) { throw 'Existing container has an unapproved mount.' }
        if ($seenDestinations -contains $mount.Destination) { throw 'Existing container has a duplicate mount destination.' }
        $field = $destinations[$mount.Destination]
        if ((Get-OcrPathKey $mount.Source) -ne (Get-OcrPathKey $Config.$field)) { throw 'Existing container mount differs from host configuration.' }
        $seen += $field
        $seenDestinations += $mount.Destination
    }
    # Manual legacy containers keep App_Data in their writable layer. The operator
    # must export it to AppDataRoot before adoption. Managed releases require the bind.
    $requiredRoots = $rootFields
    $hasReleaseLabel = $null -ne $Container.Config.Labels -and $Container.Config.Labels.PSObject.Properties['ocr.release']
    if (-not $hasEnvironmentLabel -and -not $hasReleaseLabel) { $requiredRoots = @($rootFields | Where-Object { $_ -ne 'AppDataRoot' }) }
    foreach ($requiredRoot in $requiredRoots) {
        if ($seen -notcontains $requiredRoot) { throw 'Existing container must have all configured approved storage roots.' }
    }
    $connections = @($Container.Config.Env | Where-Object { $_ -like 'ConnectionStrings__DefaultConnection=*' })
    if ($connections.Count -ne 1 -or (Get-OcrDatabase ($connections[0] -split '=', 2)[1]) -ne $Config.ExpectedDatabase) { throw 'Existing container database does not match host configuration.' }
}

function New-OcrContainer {
    param($Config, [string]$Name, [string]$Publish, [string]$Release, [string]$Target, [string]$Root)
    $rootFields = @(Get-OcrRootFields $Config $Target)
    $arguments = @('create', '--name', $Name, '--restart', 'unless-stopped', '--publish', $Publish,
        '--label', "ocr.environment=$Target", '--label', "ocr.release=$Release", '--env-file', (Join-Path $Root "$Target.env"),
        '--env', 'ASPNETCORE_ENVIRONMENT=Production', '--env', 'ASPNETCORE_URLS=http://+:80',
        '--env', 'PhotoStorage__UploadRoot=/app/photo-source', '--env', 'PhotoStorage__LegacyMirrorRoot=/app/legacy-photos',
        '--env', 'PhotoStorage__DatabasePathPrefix=/app/photos', '--env', 'PhotoPath=/app/legacy-photos',
        '--env', 'CertificatePath=/app/wwwroot/certs')
    foreach ($mount in @(@('CertificatesRoot', '/app/wwwroot/certs'), @('UploadRoot', '/app/photo-source'), @('UploadRoot', '/app/wwwroot/uploads'), @('PhotosRoot', '/app/legacy-photos'), @('DataProtectionRoot', '/app/dataprotection'))) {
        $arguments += @('--mount', "type=bind,source=$($Config.($mount[0])),target=$($mount[1])")
    }
    if ($rootFields -contains 'LegacyTestUploadRoot') {
        $arguments += @('--mount', "type=bind,source=$($Config.LegacyTestUploadRoot),target=/app/wwwroot/uploads-test",
            '--env', 'LocalUploadSubfolder=uploads-test')
    }
    if ($rootFields -contains 'AppDataRoot') {
        $arguments += @('--mount', "type=bind,source=$($Config.AppDataRoot),target=/app/App_Data")
    }
    $arguments += $Release
    $null = Invoke-OcrDocker $arguments
}

function Wait-OcrReady {
    param([int]$Port, [int]$Attempts = 30)
    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health/ready" -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 0
            if ($response.StatusCode -eq 200 -and $response.Content.Trim() -eq 'Healthy') { return }
        } catch { }
        if ($attempt -lt $Attempts - 1) { Start-Sleep -Seconds 2 }
    }
    throw 'Container readiness check failed.'
}

function Invoke-OcrDeployment {
    param([string]$Target, [string]$Release, [string]$Root, [switch]$CheckOnly)
    if ($Target -notin @('test', 'prod')) { throw 'Environment must be test or prod.' }
    if ($Release -cnotmatch '^ghcr\.io/[a-z0-9][a-z0-9._/-]*@sha256:[a-f0-9]{64}$') { throw 'Image must be an immutable ghcr.io image digest.' }
    $test = Read-OcrConfiguration $Root 'test'
    $prod = Read-OcrConfiguration $Root 'prod'
    Assert-OcrSeparation $test $prod
    $config = if ($Target -eq 'test') { $test } else { $prod }
    if ((Invoke-OcrDocker @('info', '--format', '{{.OSType}}')).Trim() -ne 'linux') { throw 'Docker must be running Linux containers.' }
    $old = Get-OcrContainer $config.ContainerName
    Assert-OcrExistingContainer $old $config $Target
    if ($CheckOnly) { Write-Output "$Target configuration and existing container validated; no changes made."; return }

    $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $candidate = "$($config.ContainerName)-candidate-$suffix"
    $backup = "$($config.ContainerName)-backup-$suffix"
    $oldRenamed = $false
    $oldStopped = $false
    $finalCreated = $false
    $candidateCreated = $false
    $backupPolicyTouched = $false
    $oldRestartPolicy = $null
    if ($null -ne $old) {
        $oldRestartPolicy = [string]$old.HostConfig.RestartPolicy.Name
        if ($oldRestartPolicy -eq 'on-failure' -and $old.HostConfig.RestartPolicy.MaximumRetryCount -gt 0) {
            $oldRestartPolicy += ':' + $old.HostConfig.RestartPolicy.MaximumRetryCount
        }
    }
    $wasRunning = $null -ne $old -and $old.State.Running
    $null = Invoke-OcrDocker @('pull', $Release)
    try {
        New-OcrContainer $config $candidate '127.0.0.1::80' $Release $Target $Root
        $candidateCreated = $true
        $null = Invoke-OcrDocker @('start', $candidate)
        $details = Get-OcrContainer $candidate
        Wait-OcrReady ([int]$details.NetworkSettings.Ports.'80/tcp'[0].HostPort)
        $null = Invoke-OcrDocker @('stop', $candidate)
        $null = Invoke-OcrDocker @('rm', $candidate)
        $candidateCreated = $false
        if ($null -ne $old) {
            $null = Invoke-OcrDocker @('stop', $config.ContainerName)
            $oldStopped = $true
            $null = Invoke-OcrDocker @('rename', $config.ContainerName, $backup)
            $oldRenamed = $true
        }
        New-OcrContainer $config $config.ContainerName "$($config.Port):80" $Release $Target $Root
        $finalCreated = $true
        $null = Invoke-OcrDocker @('start', $config.ContainerName)
        Wait-OcrReady ([int]$config.Port)
        if ($oldRenamed) {
            $backupPolicyTouched = $true
            $null = Invoke-OcrDocker @('update', '--restart', 'no', $backup)
        }
        Write-Output "Deployment to $Target succeeded: $Release"
        if ($oldRenamed) { Write-Output "Previous container retained as $backup." }
    } catch {
        $failure = $_
        try {
            if ($finalCreated) { $null = Invoke-OcrDocker @('rm', '-f', $config.ContainerName) }
            if ($oldRenamed) { $null = Invoke-OcrDocker @('rename', $backup, $config.ContainerName) }
            if ($backupPolicyTouched) { $null = Invoke-OcrDocker @('update', '--restart', $oldRestartPolicy, $config.ContainerName) }
            if ($oldStopped -and $wasRunning) { $null = Invoke-OcrDocker @('start', $config.ContainerName) }
        } catch { throw "Deployment failed and automatic rollback needs operator intervention. Previous container: $backup. No volumes were removed." }
        throw $failure
    } finally {
        if ($candidateCreated) {
            try { $null = Invoke-OcrDocker @('rm', '-f', $candidate) } catch { Write-Warning "Candidate cleanup failed: $candidate" }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-OcrDeployment -Target $Environment -Release $Image -Root $ConfigRoot -CheckOnly:$ValidateOnly
}
