# Offline regression tests. All Docker and HTTP operations are replaced before deployment runs.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Deploy-Ocr.ps1')
$script:passed = 0
function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Throws { param([scriptblock]$Action, [string]$Pattern) try { & $Action } catch { if ($_.Exception.Message -like $Pattern) { return }; throw }; throw "Expected failure: $Pattern" }
function Test-Case { param([string]$Name, [scriptblock]$Action) & $Action; $script:passed++; Write-Output "PASS $Name" }
Test-Case 'native exit zero stderr does not fail or contaminate stdout' {
    $result = Invoke-OcrNative -Executable 'cmd.exe' -Arguments @('/d', '/c', 'echo native-ok & echo warning 1>&2 & exit /b 0')
    Assert-True ($result.Trim() -eq 'native-ok') 'Native warning contaminated stdout.'
}
Test-Case 'native nonzero exit fails without exposing stderr' {
    Assert-Throws { Invoke-OcrNative -Executable 'cmd.exe' -Arguments @('/d', '/c', 'echo secret-marker 1>&2 & exit /b 7') } 'Native operation failed (exit 7).'
}
Test-Case 'Docker pull diagnostics classify only fixed safe categories' {
    $cases = @{
        'unauthorized: secret-sentinel'='unauthorized'; 'denied: secret-sentinel'='unauthorized'
        'x509: certificate signed by unknown authority secret-sentinel'='tls'
        'dial tcp: no such host secret-sentinel'='network'
        'failed to register layer: no space left on device secret-sentinel'='storage'
        'write secret-sentinel: access is denied'='storage'
        'unexpected failure secret-sentinel'='unknown'
    }
    foreach ($message in $cases.Keys) {
        Assert-True ((Get-OcrDockerFailureCategory @($message)) -ceq $cases[$message]) 'Unsafe or incorrect Docker category.'
    }
}
Test-Case 'real native failure carries category without raw stderr or stdout' {
    try {
        $null = Invoke-OcrNative -Executable 'cmd.exe' -Arguments @('/d', '/c', 'echo secret-sentinel & echo unauthorized secret-sentinel 1>&2 & exit /b 7')
        throw 'Expected native failure.'
    } catch {
        Assert-True ($_.Exception.Data['OcrFailureCategory'] -eq 'unauthorized') 'Native category lost.'
        Assert-True ($_.Exception.ToString() -notmatch 'secret-sentinel') 'Native details exposed.'
    }
}
Test-Case 'Docker pull exposes only validated category while other failures stay generic' {
    $nativeImplementation = ${function:Invoke-OcrNative}
    try {
        function Invoke-OcrNative {
            param([string]$Executable, [string[]]$Arguments)
            $failure = New-Object InvalidOperationException 'secret-sentinel'
            $failure.Data['OcrFailureCategory'] = 'unauthorized'
            throw $failure
        }
        Assert-Throws { Invoke-OcrDocker @('pull', 'secret-sentinel') } 'Docker pull failed (category: unauthorized). Native details suppressed.'
        Assert-Throws { Invoke-OcrDocker @('inspect', 'secret-sentinel') } "Docker operation 'inspect' could not complete."
        function Invoke-OcrNative {
            $failure = New-Object InvalidOperationException 'secret-sentinel'
            $failure.Data['OcrFailureCategory'] = 'secret-sentinel'
            throw $failure
        }
        Assert-Throws { Invoke-OcrDocker @('pull', 'secret-sentinel') } 'Docker pull failed (category: unknown). Native details suppressed.'
        function Invoke-OcrNative { throw 'secret-sentinel' }
        Assert-Throws { Invoke-OcrDocker @('pull', 'secret-sentinel') } 'Docker pull failed (category: unknown). Native details suppressed.'
    } finally { Set-Item Function:Invoke-OcrNative $nativeImplementation }
}
function New-TestConfig {
    param([string]$Name, [int]$Port)
    return [pscustomobject]@{ ContainerName = "ocr-$Name"; Port = $Port; ExpectedDatabase = "OCR_$Name"; CertificatesRoot = "C:\$Name\certs"; UploadRoot = "C:\$Name\uploads"; PhotosRoot = "C:\$Name\photos"; DataProtectionRoot = "C:\$Name\keys" }
}
$script:testConfig = New-TestConfig 'test' 8081
$script:prodConfig = New-TestConfig 'prod' 8080
Test-Case 'distinct environments accepted' { Assert-OcrSeparation $script:testConfig $script:prodConfig }
Test-Case 'shared database rejected' {
    $copy = New-TestConfig 'test' 8081; $copy.ExpectedDatabase = $script:prodConfig.ExpectedDatabase
    Assert-Throws { Assert-OcrSeparation $copy $script:prodConfig } '*different ExpectedDatabase*'
}
Test-Case 'nested mount roots rejected' {
    $copy = New-TestConfig 'test' 8081; $copy.PhotosRoot = $script:prodConfig.UploadRoot + '\child'
    Assert-Throws { Assert-OcrSeparation $copy $script:prodConfig } '*overlap*'
}
Test-Case 'optional legacy test root cannot overlap production roots' {
    $copy = New-TestConfig 'test' 8081
    $copy | Add-Member LegacyTestUploadRoot ($script:prodConfig.UploadRoot + '\child')
    Assert-Throws { Assert-OcrSeparation $copy $script:prodConfig } '*overlap*'
}
Test-Case 'production cannot configure a legacy test root' {
    $copy = New-TestConfig 'prod' 8080
    $copy | Add-Member LegacyTestUploadRoot 'C:\prod\legacy'
    Assert-Throws { Assert-OcrSeparation $script:testConfig $copy } '*only valid for test*'
}
Test-Case 'app data root must remain separate from production storage' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot $script:prodConfig.PhotosRoot
    Assert-Throws { Assert-OcrSeparation $copy $script:prodConfig } '*overlap*'
}
Test-Case 'connection database parsed without connecting' {
    Assert-True ((Get-OcrDatabase 'Server=unused;Database=OCR_test;Integrated Security=true') -eq 'OCR_test') 'Database parsing failed.'
    Assert-Throws { Get-OcrDatabase 'Server=unused;Integrated Security=true' } '*no explicit database*'
}
Test-Case 'Docker Desktop source paths normalize' {
    Assert-True ((Get-OcrPathKey '/run/desktop/mnt/host/c/ocr-photos') -eq (Get-OcrPathKey 'C:\ocr-photos')) 'Mount normalization failed.'
}

$script:fixture = Join-Path ([IO.Path]::GetTempPath()) ('ocr-deploy-test-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $script:fixture
try {
    $config = New-TestConfig 'test' 8081
    foreach ($field in @('CertificatesRoot', 'UploadRoot', 'PhotosRoot', 'DataProtectionRoot')) {
        $config.$field = Join-Path $script:fixture $field
        $null = New-Item -ItemType Directory -Path $config.$field
    }
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.env'), 'ConnectionStrings__DefaultConnection=Server=unused;Database=OCR_test;Integrated Security=true')
    Test-Case 'host configuration validates offline' { $null = Read-OcrConfiguration $script:fixture 'test' }
    $appDataRoot = Join-Path $script:fixture 'appdata'
    $null = New-Item -ItemType Directory -Path $appDataRoot
    $config | Add-Member AppDataRoot $appDataRoot
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'explicit app data root validates offline' { $null = Read-OcrConfiguration $script:fixture 'test' }
    $config.AppDataRoot = $config.PhotosRoot
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'app data cannot share photo storage' { Assert-Throws { Read-OcrConfiguration $script:fixture 'test' } '*non-nested*' }
    $config.AppDataRoot = ''
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'present but empty app data root rejected' { Assert-Throws { Read-OcrConfiguration $script:fixture 'test' } '*must not be empty*' }
    $config.AppDataRoot = $appDataRoot
    $extraRoot = Join-Path $script:fixture 'legacy-test'
    $null = New-Item -ItemType Directory -Path $extraRoot
    $config | Add-Member LegacyTestUploadRoot $extraRoot
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'explicit optional test root validates offline' { $null = Read-OcrConfiguration $script:fixture 'test' }
    $config.LegacyTestUploadRoot = $config.UploadRoot
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'optional test root cannot alias another test root' { Assert-Throws { Read-OcrConfiguration $script:fixture 'test' } '*non-nested*' }
    $config.LegacyTestUploadRoot = ''
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    Test-Case 'present but empty optional test root rejected' { Assert-Throws { Read-OcrConfiguration $script:fixture 'test' } '*must not be empty*' }
    $config.LegacyTestUploadRoot = $extraRoot
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.json'), ($config | ConvertTo-Json))
    [IO.File]::WriteAllText((Join-Path $script:fixture 'test.env'), 'ConnectionStrings__DefaultConnection=Server=unused;Database=OCR_prod;Integrated Security=true')
    Test-Case 'wrong database in env file rejected' { Assert-Throws { Read-OcrConfiguration $script:fixture 'test' } '*does not match*' }
} finally {
    # Delete only the uniquely named fixture created above, after resolving its boundary.
    $resolvedFixture = [IO.Path]::GetFullPath($script:fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedFixture -Leaf) -like 'ocr-deploy-test-*') {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}

function New-ExistingContainer {
    $mounts = @()
    foreach ($pair in @(@('CertificatesRoot','/app/wwwroot/certs'), @('UploadRoot','/app/wwwroot/uploads'), @('PhotosRoot','/app/wwwroot/photos'), @('DataProtectionRoot','/app/dataprotection'))) {
        $mounts += [pscustomobject]@{ Type='bind'; Source=$script:testConfig.($pair[0]); Destination=$pair[1] }
    }
    return [pscustomobject]@{
        Config=[pscustomobject]@{ Labels=[pscustomobject]@{}; Env=@('ConnectionStrings__DefaultConnection=Server=unused;Database=OCR_test;Integrated Security=true') }
        HostConfig=[pscustomobject]@{ RestartPolicy=[pscustomobject]@{Name='unless-stopped';MaximumRetryCount=0}; PortBindings=[pscustomobject]@{ '80/tcp'=@([pscustomobject]@{HostPort='8081';HostIp=''}) } }
        Mounts=$mounts; State=[pscustomobject]@{Running=$true}
    }
}
Test-Case 'manual container with approved original mounts can be adopted' { Assert-OcrExistingContainer (New-ExistingContainer) $script:testConfig 'test' }
Test-Case 'unlabelled legacy container can adopt exported app data' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot 'C:\test\appdata'
    Assert-OcrExistingContainer (New-ExistingContainer) $copy 'test'
}
Test-Case 'managed container cannot silently omit app data mount' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot 'C:\test\appdata'
    $old = New-ExistingContainer; $old.Config.Labels = [pscustomobject]@{'ocr.environment'='test'}
    Assert-Throws { Assert-OcrExistingContainer $old $copy 'test' } '*approved storage roots*'
}
Test-Case 'empty managed label is not an unlabelled legacy migration' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot 'C:\test\appdata'
    $old = New-ExistingContainer; $old.Config.Labels = [pscustomobject]@{'ocr.environment'=''}
    Assert-Throws { Assert-OcrExistingContainer $old $copy 'test' } '*approved storage roots*'
}
Test-Case 'app data migration does not relax other required mounts' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot 'C:\test\appdata'
    $old = New-ExistingContainer; $old.Mounts = @($old.Mounts | Where-Object { $_.Destination -ne '/app/wwwroot/certs' })
    Assert-Throws { Assert-OcrExistingContainer $old $copy 'test' } '*approved storage roots*'
}
Test-Case 'existing app data mount must match explicit root' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member AppDataRoot 'C:\test\appdata'
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source=$copy.AppDataRoot;Destination='/app/App_Data'}
    Assert-OcrExistingContainer $old $copy 'test'
    $old.Mounts[-1].Source = 'C:\other'
    Assert-Throws { Assert-OcrExistingContainer $old $copy 'test' } '*differs*'
}
Test-Case 'new five mount container with upload alias accepted' {
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source=$script:testConfig.UploadRoot;Destination='/app/photo-source'}
    Assert-OcrExistingContainer $old $script:testConfig 'test'
}
Test-Case 'upload alias with different root rejected' {
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source='C:\other';Destination='/app/photo-source'}
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*differs*'
}
Test-Case 'duplicate exact destination rejected' {
    $old = New-ExistingContainer; $old.Mounts += $old.Mounts[0]
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*duplicate*'
}
Test-Case 'Linux destination case mismatch rejected' {
    $old = New-ExistingContainer; $old.Mounts[0].Destination = '/app/wwwroot/CERTS'
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*unapproved mount*'
}
Test-Case 'isolated uploads-test requires explicit configuration' {
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source='C:\ocr-test\uploads-test';Destination='/app/wwwroot/uploads-test'}
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*unapproved mount*'
}
Test-Case 'explicit fifth test mount and sixth photo alias accepted' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member LegacyTestUploadRoot 'C:\ocr-test\uploads-test'
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source=$copy.LegacyTestUploadRoot;Destination='/app/wwwroot/uploads-test'}
    Assert-OcrExistingContainer $old $copy 'test'
    $old.Mounts += [pscustomobject]@{Type='bind';Source=$copy.UploadRoot;Destination='/app/photo-source'}
    Assert-OcrExistingContainer $old $copy 'test'
}
Test-Case 'explicit optional mount with wrong source rejected' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member LegacyTestUploadRoot 'C:\ocr-test\uploads-test'
    $old = New-ExistingContainer
    $old.Mounts += [pscustomobject]@{Type='bind';Source='C:\other';Destination='/app/wwwroot/uploads-test'}
    Assert-Throws { Assert-OcrExistingContainer $old $copy 'test' } '*differs*'
}
Test-Case 'configured optional mount missing from existing container rejected' {
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member LegacyTestUploadRoot 'C:\ocr-test\uploads-test'
    Assert-Throws { Assert-OcrExistingContainer (New-ExistingContainer) $copy 'test' } '*approved storage roots*'
}
Test-Case 'wrong environment container rejected' {
    $old = New-ExistingContainer; $old.Config.Labels = [pscustomobject]@{'ocr.environment'='prod'}
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*another environment*'
}
Test-Case 'unexpected existing mount rejected' {
    $old = New-ExistingContainer; $old.Mounts[0].Source = 'C:\other'
    Assert-Throws { Assert-OcrExistingContainer $old $script:testConfig 'test' } '*differs*'
}

# Replace boundary functions. No real Docker daemon, database or HTTP calls below.
function Read-OcrConfiguration { param([string]$Root, [string]$Name) if ($Name -eq 'test') { return $script:testConfig }; return $script:prodConfig }
function Get-OcrContainer {
    param([string]$Name)
    if ($Name -like '*-candidate-*') { return [pscustomobject]@{NetworkSettings=[pscustomobject]@{Ports=[pscustomobject]@{'80/tcp'=@([pscustomobject]@{HostPort='49152'})}}} }
    $existing = New-ExistingContainer
    if ($script:failure -eq 'disable-restart-retries') {
        $existing.HostConfig.RestartPolicy.Name = 'on-failure'
        $existing.HostConfig.RestartPolicy.MaximumRetryCount = 3
    }
    return $existing
}
function Invoke-OcrDocker {
    param([string[]]$Arguments)
    $script:operations.Add(($Arguments -join ' '))
    if ($Arguments[0] -eq 'info') { return 'linux' }
    if ($script:failure -eq 'pull' -and $Arguments[0] -eq 'pull') { throw 'simulated pull failure' }
    if ($script:failure -eq 'rename' -and $Arguments[0] -eq 'rename') { throw 'simulated rename failure' }
    if ($script:failure -like 'disable-restart*' -and $Arguments[0] -eq 'update' -and $Arguments[2] -eq 'no') { throw 'simulated update failure' }
    return ''
}
function Wait-OcrReady {
    param([int]$Port, [int]$Attempts=30)
    if (($script:failure -eq 'candidate' -and $Port -eq 49152) -or ($script:failure -eq 'final' -and $Port -eq 8081)) { throw 'simulated readiness failure' }
}
function Reset-Fake { param([string]$Failure='') $script:failure=$Failure; $script:operations=New-Object 'System.Collections.Generic.List[string]' }
$digest = 'ghcr.io/example/ocr@sha256:' + ('a' * 64)
Test-Case 'test and production mount their configured app data' {
    foreach ($target in @('test','prod')) {
        Reset-Fake
        $copy = New-TestConfig $target 8081; $copy | Add-Member AppDataRoot "C:\$target\appdata"
        New-OcrContainer $copy "ocr-$target" '8081:80' $digest $target 'C:\unused'
        Assert-True ([bool]($script:operations -like "create *source=C:\$target\appdata,target=/app/App_Data*")) 'App data persistence missing.'
    }
}
Test-Case 'new test container preserves explicitly configured historical uploads' {
    Reset-Fake
    $copy = New-TestConfig 'test' 8081; $copy | Add-Member LegacyTestUploadRoot 'C:\ocr-test\uploads-test'
    New-OcrContainer $copy 'ocr-test' '8081:80' $digest 'test' 'C:\unused'
    Assert-True ([bool]($script:operations -like 'create *source=C:\ocr-test\uploads-test,target=/app/wwwroot/uploads-test*')) 'Legacy test storage lost.'
    Assert-True ([bool]($script:operations -like 'create *LocalUploadSubfolder=uploads-test*')) 'Legacy certificates not readable.'
}
Test-Case 'new production container has no legacy test upload mount' {
    Reset-Fake
    New-OcrContainer $script:prodConfig 'ocr-prod' '8080:80' $digest 'prod' 'C:\unused'
    Assert-True (-not [bool]($script:operations -like '*uploads-test*')) 'Test mount leaked into production.'
}
Test-Case 'mutable image rejected' { Reset-Fake; Assert-Throws { Invoke-OcrDeployment 'test' 'ghcr.io/example/ocr:latest' 'C:\unused' } '*immutable*'; Assert-True ($script:operations.Count -eq 0) 'Docker called before validation.' }
Test-Case 'validate-only performs no Docker writes' { Reset-Fake; Invoke-OcrDeployment 'test' $digest 'C:\unused' -CheckOnly; Assert-True ($script:operations.Count -eq 1 -and $script:operations[0] -like 'info *') 'Validate-only mutated state.' }
Test-Case 'pull failure leaves original running' { Reset-Fake 'pull'; Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*pull failure*'; Assert-True (-not ($script:operations -like 'stop *')) 'Old container stopped after pull failure.' }
Test-Case 'candidate health failure leaves original running' { Reset-Fake 'candidate'; Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*readiness failure*'; Assert-True (-not ($script:operations -contains 'stop ocr-test')) 'Old container stopped before candidate ready.'; Assert-True ([bool]($script:operations -like 'rm -f ocr-test-candidate-*')) 'Candidate leaked.' }
Test-Case 'final health failure restores original container' {
    Reset-Fake 'final'; Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*readiness failure*'
    Assert-True ($script:operations -contains 'rm -f ocr-test') 'Failed replacement not removed.'
    Assert-True ([bool]($script:operations -like 'rename ocr-test-backup-* ocr-test')) 'Backup not renamed back.'
    Assert-True ($script:operations[$script:operations.Count - 1] -eq 'start ocr-test') 'Original not restarted.'
}
Test-Case 'rename failure restarts stopped original' { Reset-Fake 'rename'; Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*rename failure*'; Assert-True ($script:operations[$script:operations.Count - 1] -eq 'start ocr-test') 'Original not restarted.' }
Test-Case 'successful deployment retains backup and uses private photo mounts' {
    Reset-Fake; Invoke-OcrDeployment 'test' $digest 'C:\unused'
    Assert-True ([bool]($script:operations -like 'rename ocr-test ocr-test-backup-*')) 'Backup missing.'
    Assert-True (-not ($script:operations -like 'rm *backup*')) 'Backup deleted.'
    Assert-True ([bool]($script:operations -like 'create *target=/app/photo-source*')) 'Private photo source mount missing.'
    Assert-True ([bool]($script:operations -like 'create *target=/app/wwwroot/uploads*')) 'Persistent certificate uploads mount missing.'
    Assert-True ([bool]($script:operations -like 'update --restart no ocr-test-backup-*')) 'Backup can restart on reboot.'
}
Test-Case 'backup policy update failure rolls back original restart policy' {
    Reset-Fake 'disable-restart'
    Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*update failure*'
    Assert-True ($script:operations -contains 'update --restart unless-stopped ocr-test') 'Original restart policy not restored.'
    Assert-True ($script:operations[$script:operations.Count - 1] -eq 'start ocr-test') 'Original not restarted.'
}
Test-Case 'rollback preserves on-failure retry limit' {
    Reset-Fake 'disable-restart-retries'
    Assert-Throws { Invoke-OcrDeployment 'test' $digest 'C:\unused' } '*update failure*'
    Assert-True ($script:operations -contains 'update --restart on-failure:3 ocr-test') 'Retry limit lost on rollback.'
}
Write-Output "$script:passed offline deployment tests passed."
