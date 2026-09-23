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
foreach ($pattern in @('**/bin', '**/obj', '**/publish', '**/o', '**/logs', '**/uploads*', '**/photos', '**/certs', '**/.env', '**/.env.*', '**/*.md', '**/.tmp*', '**/App_Data')) {
    if ($ignore -notcontains $pattern) { $failures += "Missing Docker context exclusion: $pattern" }
}
$dockerfile = Get-Content (Join-Path $root 'Dockerfile') -Raw
if ($dockerfile -match '(?m)^COPY OperatorCertificationRecord/') {
    $failures += 'Docker image build must not copy legacy desktop sources or settings.'
}
$project = [xml](Get-Content (Join-Path $web 'OperatorCertificationRecord.Web.csproj') -Raw)
$exclusions = [string]$project.Project.PropertyGroup.DefaultItemExcludes
foreach ($pattern in @('**/publish/**', '**/o/**', '**/logs/**', '**/uploads*/**', '**/photos/**', '**/certs/**', '**/App_Data/**')) {
    if (!$exclusions.Contains($pattern)) { $failures += "Missing publish exclusion: $pattern" }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Output 'PASS: runtime-only database configuration and private build/publish content exclusions.'
