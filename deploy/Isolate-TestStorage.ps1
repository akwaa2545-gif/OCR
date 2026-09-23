param([switch]$ValidateOnly, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'

function Normalize-IsolationPath([string]$Path) {
    $value = $Path -replace '^/run/desktop/mnt/host/([a-z])/', '$1:/'
    return $value.Replace('/', '\').TrimEnd('\').ToLowerInvariant()
}
function Invoke-IsolationDocker([string[]]$Arguments) {
    if (!$script:IsolationDockerExe) { throw 'Docker executable was not resolved before execution.' }
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $result = & $script:IsolationDockerExe @Arguments 2>&1
        $exit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $prior }
    if ($exit -ne 0) { throw "Docker operation failed: $($Arguments[0]). Output suppressed to protect configuration secrets." }
    return ($result -join "`n")
}
function Assert-IsolationEquivalent($Before, $After, [string[]]$Ignore) {
    $names = @(@($Before.PSObject.Properties.Name) + @($After.PSObject.Properties.Name) | Sort-Object -Unique)
    $different = @()
    foreach ($name in $names) {
        if ($name -in $Ignore) { continue }
        $left = ConvertTo-Json -InputObject $Before.$name -Depth 30 -Compress
        $right = ConvertTo-Json -InputObject $After.$name -Depth 30 -Compress
        if ($left -cne $right) { $different += $name }
    }
    if ($different.Count) { throw "Container configuration differs at '$($different -join ', ')'; refusing to discard unsupported settings." }
}
function Assert-IsolationHostConfig($Before, $After) {
    $optionalLists = @('Dns','DnsOptions','DnsSearch','ExtraHosts','GroupAdd','Links','CapAdd','CapDrop','Devices','DeviceCgroupRules','DeviceRequests','Ulimits','BlkioWeightDevice','BlkioDeviceReadBps','BlkioDeviceWriteBps','BlkioDeviceReadIOps','BlkioDeviceWriteIOps','SecurityOpt','VolumesFrom')
    $left = [ordered]@{}
    $right = [ordered]@{}
    foreach ($name in @(@($Before.PSObject.Properties.Name) + @($After.PSObject.Properties.Name) | Sort-Object -Unique)) {
        $left[$name]=$Before.$name
        $right[$name]=$After.$name
        if ($name -in $optionalLists) {
            # Docker API versions serialize unused optional lists as either [] or null.
            # Only those two empty representations are equivalent; preserve all values.
            if ($null -eq $Before.$name -or ($Before.$name -is [Array] -and $Before.$name.Count -eq 0)) { $left[$name]=$null }
            if ($null -eq $After.$name -or ($After.$name -is [Array] -and $After.$name.Count -eq 0)) { $right[$name]=$null }
        }
        if ($name -eq 'OomKillDisable') {
            # Unspecified OOM-killer override has the same behavior as false.
            $left[$name]=[bool]$Before.$name
            $right[$name]=[bool]$After.$name
        }
    }
    Assert-IsolationEquivalent ([pscustomobject]$left) ([pscustomobject]$right) @('Binds','Mounts','RestartPolicy','ConsoleSize')
}
function Assert-IsolationCandidateConfig($Before, $After) {
    if ($After.AttachStdin -or $After.OpenStdin -or $After.StdinOnce -or $After.Tty) { throw 'Interactive candidate configuration is unsupported.' }
    # docker create marks output streams attachable; docker start without -a still
    # starts detached. These two client-stream flags do not change app execution.
    Assert-IsolationEquivalent $Before $After @('Hostname','Image','Labels','AttachStdout','AttachStderr','Env')
    Assert-IsolationEnvironmentEquivalent $Before.Env $After.Env
}
function Assert-IsolationEnvironmentEquivalent([string[]]$Before, [string[]]$After) {
    $maps = @()
    foreach ($entries in @(@{Values=$Before}, @{Values=$After})) {
        $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
        foreach ($entry in $entries.Values) {
            $split = $entry.IndexOf('=')
            if ($split -lt 1) { throw 'Invalid environment name in candidate comparison.' }
            $name=$entry.Substring(0,$split)
            if ($map.ContainsKey($name)) { throw 'Duplicate environment name in candidate comparison.' }
            $map.Add($name,$entry.Substring($split+1))
        }
        $maps += ,$map
    }
    if ($maps[0].Count -ne $maps[1].Count) { throw 'Container environment differs.' }
    foreach ($name in $maps[0].Keys) {
        if (!$maps[1].ContainsKey($name) -or ![string]::Equals($maps[0][$name],$maps[1][$name],[StringComparison]::Ordinal)) { throw 'Container environment differs.' }
    }
}
function Assert-IsolationEnvironment([string[]]$Environment) {
    $seen = @{}
    foreach ($entry in $Environment) {
        if ($entry -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$' -or $seen.ContainsKey($Matches[1])) { throw 'Invalid or duplicate environment variable.' }
        $seen[$Matches[1]] = $Matches[2]
    }
    try { $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder($seen['ConnectionStrings__DefaultConnection']) }
    catch { throw 'Invalid database connection configuration.' }
    if ($builder.InitialCatalog -cne 'OperatorCertificationRecordDB_Test') { throw 'Unexpected database; refusing migration.' }
}
function Assert-IsolationTree([string]$Path, [switch]$AncestorsOnly) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point rejected: $cursor" }
        }
        $cursor = Split-Path $cursor -Parent
    }
    if (!$AncestorsOnly -and (Test-Path -LiteralPath $Path)) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($Path)
        while ($pending.Count) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point rejected: $($item.FullName)" }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            }
        }
    }
}
function Test-IsolationPage([int]$Status, [string]$Content) {
    return $Status -eq 200 -and $Content -match '<title>\s*Dashboard - OTD System\s*</title>'
}
function Wait-IsolationPage {
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        try {
            $response = Invoke-WebRequest 'http://127.0.0.1:5050/' -UseBasicParsing -TimeoutSec 5
            if (Test-IsolationPage $response.StatusCode $response.Content) { return }
        } catch { }
        Start-Sleep -Seconds 2
    }
    throw 'Test application dashboard smoke check failed.'
}
function Copy-IsolationData($Pairs) {
    foreach ($pair in $Pairs) {
        if ($pair.Leaf -eq 'dataprotection') { continue } # Never copy production encryption keys.
        if ($pair.Leaf -eq 'certs') { continue } # User requested an empty isolated certificate folder.
        Assert-IsolationTree $pair.Source
        Assert-IsolationTree $pair.Target
        $prior = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & robocopy $pair.Source $pair.Target /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP *> $null
            $exit = $LASTEXITCODE
        } finally { $ErrorActionPreference = $prior }
        if ($exit -ge 8) { throw "Storage copy failed for $($pair.Leaf)." }
    }
}
function Invoke-TestStorageIsolation {
    if ($ValidateOnly -and $PrepareOnly) { throw 'Choose only one mode.' }
    # The image PATH is Linux-specific. Pin the Windows executable before forwarding
    # image environment values into the child process that creates the container.
    $script:IsolationDockerExe = (Get-Command docker.exe -CommandType Application -ErrorAction Stop).Source
    $old = @(ConvertFrom-Json (Invoke-IsolationDocker @('inspect','ocrwebtest')))[0]
    $image = @(ConvertFrom-Json (Invoke-IsolationDocker @('image','inspect',$old.Image)))[0]
    if (!$old.State.Running -or $old.Name -cne '/ocrwebtest' -or $old.HostConfig.NetworkMode -cne 'bridge') { throw 'Expected running test container on bridge network.' }
    if (@($old.NetworkSettings.Networks.PSObject.Properties).Count -ne 1) { throw 'Extra networks are unsupported.' }
    Assert-IsolationEnvironment $old.Config.Env
    $ports = @($old.HostConfig.PortBindings.PSObject.Properties)
    if ($ports.Count -ne 1 -or $ports[0].Name -cne '80/tcp' -or @($ports[0].Value).Count -ne 1 -or $ports[0].Value[0].HostPort -cne '5050' -or $ports[0].Value[0].HostIp -notin @('','0.0.0.0')) { throw 'Unexpected test port binding.' }
    if ($old.Config.AttachStdin -or $old.Config.AttachStdout -or $old.Config.AttachStderr -or $old.Config.OpenStdin -or $old.Config.StdinOnce -or $old.Config.Tty) { throw 'Attached or interactive standard streams are unsupported.' }
    # Image metadata omits many container-only flags; compare executable semantics here.
    # The created candidate is still compared against every existing container field below.
    $semanticFields = @('Cmd','Entrypoint','User','WorkingDir','StopSignal','Healthcheck','Shell','Volumes','OnBuild')
    $imageSemantics = [ordered]@{}
    $containerSemantics = [ordered]@{}
    foreach ($field in $semanticFields) {
        $imageSemantics[$field] = $image.Config.$field
        $containerSemantics[$field] = $old.Config.$field
    }
    # Docker represents the default root user as either missing/null or an empty string.
    $imageSemantics['User'] = [string]$image.Config.User
    $containerSemantics['User'] = [string]$old.Config.User
    Assert-IsolationEquivalent ([pscustomobject]$imageSemantics) ([pscustomobject]$containerSemantics) @()
    $pairs = @(
        @{Source='C:\uploads'; Leaf='certs'; Destination='/app/wwwroot/certs'},
        @{Source='C:\ocr-uploads'; Leaf='uploads'; Destination='/app/wwwroot/uploads'},
        @{Source='C:\ocr-photos'; Leaf='photos'; Destination='/app/wwwroot/photos'},
        @{Source='C:\ocr-dataprotection'; Leaf='dataprotection'; Destination='/app/dataprotection'},
        @{Source='C:\temp\upload(test)'; Leaf='uploads-test'; Destination='/app/wwwroot/uploads-test'}
    )
    if (@($old.Mounts).Count -ne $pairs.Count) { throw 'Unexpected mount count.' }
    foreach ($pair in $pairs) {
        $pair.Target = 'C:\ocr-test\' + $pair.Leaf
        $match = @($old.Mounts | Where-Object { $_.Destination -ceq $pair.Destination })
        if ($match.Count -ne 1 -or $match[0].Type -ne 'bind' -or !$match[0].RW -or (Normalize-IsolationPath $match[0].Source) -cne (Normalize-IsolationPath $pair.Source)) { throw "Unexpected mount: $($pair.Destination)" }
        if (!(Test-Path -LiteralPath $pair.Source -PathType Container)) { throw "Source missing: $($pair.Source)" }
        Assert-IsolationTree $pair.Source -AncestorsOnly:($pair.Leaf -in @('certs','dataprotection'))
    }
    Assert-IsolationTree 'C:\ocr-test'
    $markerPath = 'C:\ocr-test\.ocr-isolation.json'
    if (Test-Path -LiteralPath 'C:\ocr-test') {
        if (!(Test-Path -LiteralPath $markerPath -PathType Leaf)) { throw 'Existing test root has no migration marker.' }
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($marker.ContainerId -cne $old.Id -or $marker.Image -cne $old.Image) { throw 'Migration marker belongs to another container/image.' }
        foreach ($emptyLeaf in @('dataprotection','certs')) {
            if (@(Get-ChildItem (Join-Path 'C:\ocr-test' $emptyLeaf) -Force -ErrorAction SilentlyContinue).Count) { throw "Prepared $emptyLeaf directory must be empty." }
        }
    }
    if ($ValidateOnly) { Write-Output 'Validated expected test database, mounts, paths and image configuration. No changes.'; return }
    if (!(Test-Path -LiteralPath 'C:\ocr-test')) {
        $null = New-Item -ItemType Directory 'C:\ocr-test'
        @{ContainerId=$old.Id; Image=$old.Image} | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
    }
    foreach ($pair in $pairs) { if (!(Test-Path -LiteralPath $pair.Target)) { $null=New-Item -ItemType Directory -Path $pair.Target } }
    Copy-IsolationData $pairs
    if ($PrepareOnly) { Write-Output 'Test storage prepared; certificates and encryption keys intentionally empty. Both running containers unchanged. Rerun without switches for cutover.'; return }
    $suffix = [Guid]::NewGuid().ToString('N').Substring(0,12)
    $candidate = "ocrwebtest-isolated-$suffix"
    $backup = "ocrwebtest-shared-backup-$suffix"
    $created=$false; $stopped=$false; $renamed=$false
    try {
        $args = @('create','--name',$candidate,'--restart','no','--network','bridge','-p','5050:80')
        foreach ($pair in $pairs) { $args += @('--mount',"type=bind,source=$($pair.Target),target=$($pair.Destination)") }
        $expectedLabels = [ordered]@{}
        foreach ($label in $old.Config.Labels.PSObject.Properties) {
            $value = $label.Value
            if ($label.Name -match '^desktop\.docker\.io/binds/(\d+)/Source$') {
                $targetLabel = 'desktop.docker.io/binds/' + $Matches[1] + '/Target'
                $pair = @($pairs | Where-Object { $_.Destination -ceq $old.Config.Labels.$targetLabel })
                if ($pair.Count -ne 1) { throw 'Unknown Docker Desktop bind metadata target.' }
                $value = $pair[0].Target
            }
            $expectedLabels[$label.Name] = $value
            $args += @('--label',"$($label.Name)=$value")
        }
        $saved = @{}
        try {
            foreach ($entry in $old.Config.Env) {
                $split = $entry.IndexOf('='); $name=$entry.Substring(0,$split)
                $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')
                [Environment]::SetEnvironmentVariable($name,$entry.Substring($split+1),'Process')
                $args += @('--env',$name)
            }
            $args += $old.Image
            $null=Invoke-IsolationDocker $args
            $created=$true
        } finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
        $new = @(ConvertFrom-Json (Invoke-IsolationDocker @('inspect',$candidate)))[0]
        Assert-IsolationCandidateConfig $old.Config $new.Config
        Assert-IsolationEquivalent ([pscustomobject]$expectedLabels) $new.Config.Labels @()
        Assert-IsolationHostConfig $old.HostConfig $new.HostConfig
        if (@($new.Mounts).Count -ne $pairs.Count) { throw 'New container mount count differs.' }
        foreach ($pair in $pairs) {
            $mount = @($new.Mounts | Where-Object { $_.Destination -ceq $pair.Destination })
            if ($mount.Count -ne 1 -or $mount[0].Type -ne 'bind' -or !$mount[0].RW -or (Normalize-IsolationPath $mount[0].Source) -cne (Normalize-IsolationPath $pair.Target)) { throw 'New container mount verification failed.' }
        }
        $null=Invoke-IsolationDocker @('stop','--time','30','ocrwebtest'); $stopped=$true
        Copy-IsolationData $pairs
        $null=Invoke-IsolationDocker @('update','--restart','no','ocrwebtest')
        $null=Invoke-IsolationDocker @('rename','ocrwebtest',$backup); $renamed=$true
        $null=Invoke-IsolationDocker @('rename',$candidate,'ocrwebtest'); $candidate='ocrwebtest'
        $null=Invoke-IsolationDocker @('start','ocrwebtest')
        Wait-IsolationPage
        $policy=$old.HostConfig.RestartPolicy.Name
        if ($policy -eq 'on-failure' -and $old.HostConfig.RestartPolicy.MaximumRetryCount) { $policy += ':' + $old.HostConfig.RestartPolicy.MaximumRetryCount }
        $null=Invoke-IsolationDocker @('update','--restart',$policy,'ocrwebtest')
        Write-Output "Test storage isolated; previous stopped container retained as $backup with restart disabled. Certificates were not copied; historical test certificate links will be unavailable. Production unchanged."
    } catch {
        $failure=$_.Exception.Message
        try {
            if ($created) { $null=Invoke-IsolationDocker @('rm','--force',$candidate) }
            if ($renamed) { $null=Invoke-IsolationDocker @('rename',$backup,'ocrwebtest') }
            if ($stopped) {
                $policy=$old.HostConfig.RestartPolicy.Name
                if ($policy -eq 'on-failure' -and $old.HostConfig.RestartPolicy.MaximumRetryCount) { $policy += ':' + $old.HostConfig.RestartPolicy.MaximumRetryCount }
                $null=Invoke-IsolationDocker @('update','--restart',$policy,'ocrwebtest')
                $null=Invoke-IsolationDocker @('start','ocrwebtest')
                Wait-IsolationPage
            }
        } catch { throw "Migration failed and rollback needs manual attention. Original container ID: $($old.Id). Backup name: $backup. No storage was deleted." }
        throw "Migration failed; original test container retained/restored. $failure"
    }
}
if ($MyInvocation.InvocationName -ne '.') { Invoke-TestStorageIsolation }
