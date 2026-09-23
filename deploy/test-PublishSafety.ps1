$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$web = Join-Path $root 'OperatorCertificationRecord.Web'
$failures = @()
foreach ($name in @('appsettings.json', 'appsettings.Production.json', 'appsettings.Testing.json')) {
    $config = Get-Content (Join-Path $web $name) -Raw | ConvertFrom-Json
    if ($config.ConnectionStrings.DefaultConnection -ne '') {
        $failures += "$name must receive DefaultConnection at runtime, not from source."
    }
}
$ignore = @(Get-Content (Join-Path $root '.dockerignore'))
foreach ($pattern in @('**/bin', '**/obj', '**/publish', '**/logs', '**/uploads*', '**/photos', '**/certs', '**/.env', '**/.env.*', '**/*.md', '**/.tmp*', '**/App_Data')) {
    if ($ignore -notcontains $pattern) { $failures += "Missing Docker context exclusion: $pattern" }
}
$dockerfile = Get-Content (Join-Path $root 'Dockerfile') -Raw
$dockerCommands = $dockerfile -replace '\\\r?\n\s*', ' '
foreach ($verb in @('restore', 'publish')) {
    if ($dockerCommands -notmatch "(?m)^RUN dotnet $verb [^\r\n]* -r linux-x64(?:\s|$)") {
        $failures += "Docker $verb must use linux-x64 to match the image platform and ReadyToRun assets."
    }
}
if ($dockerCommands -notmatch '(?m)^RUN dotnet publish [^\r\n]* --self-contained false(?:\s|$)') {
    $failures += 'Docker publish must be framework-dependent for the ASP.NET runtime base image.'
}
if ($dockerfile -match '(?m)^COPY OperatorCertificationRecord/') {
    $failures += 'Docker image build must not copy legacy desktop sources or settings.'
}
$project = [xml](Get-Content (Join-Path $web 'OperatorCertificationRecord.Web.csproj') -Raw)
$exclusions = [string]$project.Project.PropertyGroup.DefaultItemExcludes
foreach ($pattern in @('**/publish/**', '**/logs/**', '**/uploads*/**', '**/photos/**', '**/certs/**', '**/App_Data/**')) {
    if (!$exclusions.Contains($pattern)) { $failures += "Missing publish exclusion: $pattern" }
}
# Despite its short name, o contains the tracked application model sources.
if ($ignore -contains '**/o' -or $ignore -contains '**/o/**' -or $exclusions.Contains('**/o/**')) {
    $failures += 'Application model sources under o must remain in the Docker context and compile items.'
}
$compileJson = & dotnet msbuild (Join-Path $web 'OperatorCertificationRecord.Web.csproj') -getItem:Compile,Content -verbosity:quiet
if ($LASTEXITCODE -ne 0) { throw 'Could not evaluate application compile items.' }
$evaluatedItems = ($compileJson -join [Environment]::NewLine | ConvertFrom-Json).Items
$compileItems = $evaluatedItems.Compile
$modelFiles = @(Get-ChildItem (Join-Path $web 'o') -Filter '*.cs' -File)
if ($modelFiles.Count -eq 0) { $failures += 'Application model sources are missing.' }
foreach ($model in $modelFiles) {
    if (@($compileItems | Where-Object { $_.FullPath -eq $model.FullName }).Count -eq 0) {
        $failures += "Model source is excluded from compilation: $($model.Name)"
    }
}
foreach ($content in $evaluatedItems.Content) {
    $relativePath = $content.Identity.Replace('\', '/')
    if ($relativePath -match '(?i)(^|/)(publish|logs|uploads[^/]*|photos|certs|App_Data|dataprotection|photo-source|legacy-photos)(/|$)|(^|/)\.env($|\.)|(^|/)appsettings\.(.*\.)?Local\.json$') {
        $failures += 'Private runtime data or nested publish output is included in application content.'
    }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Output 'PASS: runtime-only database configuration and private build/publish content exclusions.'
