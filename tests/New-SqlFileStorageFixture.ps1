<#
.SYNOPSIS
Creates a new retained local SQL fixture from synthetic data only.
.DESCRIPTION
Reads OCR_FILE_STORAGE_TEST_CONNECTION privately. The Initial Catalog must be
a NEW OcrFileStorageTests_* database on localhost/127.0.0.1. Existing databases
are refused. There is no database deletion or production-data copy operation.
Use -ValidateOnly to check inputs/scripts without connecting to SQL Server.
#>
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$preparePath = Join-Path $root 'deploy/sql/20261027_prepare_employee_file_storage.sql'
$verifyPath = Join-Path $root 'deploy/sql/20261027_verify_employee_file_storage.sql'
foreach ($path in @($preparePath, $verifyPath)) {
    if (!(Test-Path -LiteralPath $path)) { throw 'Required file-storage schema script is absent.' }
}
$prepare = [IO.File]::ReadAllText($preparePath)
$verify = [IO.File]::ReadAllText($verifyPath)
$connectionText = [Environment]::GetEnvironmentVariable('OCR_FILE_STORAGE_TEST_CONNECTION')
if ([string]::IsNullOrWhiteSpace($connectionText)) {
    throw 'Set OCR_FILE_STORAGE_TEST_CONNECTION privately before creating a fixture.'
}
try { $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionText }
catch { throw 'OCR_FILE_STORAGE_TEST_CONNECTION cannot be parsed.' }
$database = $builder.get_InitialCatalog()
if ($database.Length -gt 128 -or $database -notmatch '^OcrFileStorageTests_[A-Za-z0-9_]+$') {
    throw 'Initial Catalog must be a new OcrFileStorageTests_* database name.'
}
if ($builder.get_DataSource() -notmatch '^(localhost|127\.0\.0\.1)(,\d+|\\[A-Za-z0-9_]+)?$') {
    throw 'Fixture SQL Server must be localhost or 127.0.0.1.'
}
if (![string]::IsNullOrEmpty($builder.get_AttachDBFilename())) {
    throw 'Attached database files are not supported by this fixture creator.'
}
foreach ($declaration in @('@DryRun bit = 1', '@PreparationReleaseVerified bit = 0',
    '@ExpectedDatabase sysname = NULL', '@ExpectedEnvironment nvarchar(128) = NULL',
    "@Phase nvarchar(16) = N'Schema'")) {
    if (!$prepare.Contains($declaration)) { throw 'Preparation script declaration contract changed; review fixture creator before running.' }
}
if ($ValidateOnly) {
    Write-Output 'PASS: local-only new-database fixture inputs and schema declarations validated; no SQL connection opened.'
    exit 0
}
function Invoke-FixtureSql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = $Sql
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
$builder.set_InitialCatalog('master')
$admin = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
$fixture = $null
$created = $false
try {
    $admin.Open()
    $exists = $admin.CreateCommand()
    $exists.CommandText = 'SELECT DB_ID(@database)'
    [void]$exists.Parameters.Add('@database', [System.Data.SqlDbType]::NVarChar, 128)
    $exists.Parameters['@database'].Value = $database
    try { $existing = $exists.ExecuteScalar() } finally { $exists.Dispose() }
    if ($existing -isnot [DBNull]) { throw 'Fixture creator refuses every existing database; choose a new name.' }
    # The name is allowlisted above. A case-sensitive database default also keeps
    # Verifier/DisQualifiedBy/PromotedBy and all other employee references aligned
    # with EmpCode in real application joins, rather than inheriting server CI.
    Invoke-FixtureSql $admin "CREATE DATABASE [$database] COLLATE Latin1_General_100_CS_AS;"
    $created = $true
    Invoke-FixtureSql $admin "ALTER DATABASE [$database] SET COMPATIBILITY_LEVEL = 120;"
    $builder.set_InitialCatalog($database)
    $fixture = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
    $fixture.Open()
    Invoke-FixtureSql $fixture @'
EXEC sys.sp_addextendedproperty @name=N'OcrEnvironment', @value=N'SyntheticLocalFixture';
CREATE TABLE dbo.tblEmployee(
 EmpCode nvarchar(10) NOT NULL CONSTRAINT PK_tblEmployee PRIMARY KEY,
 EmpPassword nvarchar(128) NULL, JoinDate datetime NULL, JobGrade nvarchar(20) NULL, HEng nvarchar(50) NULL,
 PersonFnameEng nvarchar(100) NULL, PersonLnameEng nvarchar(100) NULL, HThai nvarchar(50) NULL,
 PersonFnameThai nvarchar(100) NULL, PersonLnameThai nvarchar(100) NULL,
 DeptID nvarchar(20) NULL, SectID nvarchar(20) NULL, WorkshopID nvarchar(20) NULL, Shift nvarchar(20) NULL,
 Photo nvarchar(50) NULL, Notice nvarchar(500) NULL, ResignBy nvarchar(128) NULL, ResignDate datetime NULL,
 StatusWork nvarchar(1) NULL, TransferBy nvarchar(128) NULL, TransferDate datetime NULL);
CREATE TABLE dbo.tblQualified(
 EmpCode nvarchar(10) NULL, ProcessName nvarchar(100) NULL, OperatorTraining nvarchar(100) NULL,
 TheoryTraining datetime NULL, OJTTraining datetime NULL, CertifiedDate datetime NULL,
 FullScore nvarchar(50) NULL, ActualScore nvarchar(50) NULL, TestResult nvarchar(50) NULL, JudgmentTheory nvarchar(50) NULL,
 KnowledgeScore nvarchar(50) NULL, KnowledgeLevel nvarchar(50) NULL, SkillScore nvarchar(50) NULL, SkillLevel nvarchar(50) NULL,
 JudgmentPractice nvarchar(50) NULL, ExpiryDate datetime NULL, Verifier nvarchar(128) NULL, VerifierDate datetime NULL,
 DisQualifiedDate datetime NULL, DisQualifiedBy nvarchar(128) NULL, TheReason nvarchar(500) NULL, Remark nvarchar(500) NULL,
 Download nvarchar(100) NULL, PromotionId uniqueidentifier NULL, PromotedAt datetime2(0) NULL, PromotedBy nvarchar(128) NULL);
SELECT TOP(0) EmpCode,ProcessName,OperatorTraining,TheoryTraining,OJTTraining,CertifiedDate,FullScore,ActualScore,
 TestResult,JudgmentTheory,KnowledgeScore,KnowledgeLevel,SkillScore,SkillLevel,JudgmentPractice,ExpiryDate,Verifier,
 VerifierDate,DisQualifiedDate,DisQualifiedBy,TheReason,Remark,Download,PromotionId,PromotedAt,PromotedBy
INTO dbo.tblQualified_Obsoleted FROM dbo.tblQualified;
INSERT dbo.tblEmployee(EmpCode,Photo,StatusWork,JobGrade,PersonFnameEng,PersonLnameEng)
VALUES(N'00001',N'fixture-old.jpg',N'1',N'53T',N'Synthetic',N'One'),(N'00002',NULL,N'1',N'55T',N'Synthetic',N'Two');
'@
    $apply = $prepare.Replace('@DryRun bit = 1', '@DryRun bit = 0').
        Replace('@PreparationReleaseVerified bit = 0', '@PreparationReleaseVerified bit = 1').
        Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'$database'").
        Replace('@ExpectedEnvironment nvarchar(128) = NULL', "@ExpectedEnvironment nvarchar(128) = N'SyntheticLocalFixture'")
    # Release verification is true only for this script's newly created synthetic
    # fixture. This does not assert that any deployed real client is compatible.
    foreach ($phase in @('Schema', 'Backfill', 'Finalize')) {
        Invoke-FixtureSql $fixture ($apply.Replace("@Phase nvarchar(16) = N'Schema'", "@Phase nvarchar(16) = N'$phase'"))
    }
    Invoke-FixtureSql $fixture $verify
    Write-Output "READY: retained synthetic local database $database; schema version 1 verified; no production data copied."
    Write-Output 'Compatibility level 120 on a newer SQL engine is not actual SQL Server 2014 validation.'
} catch {
    if ($created) { Write-Warning 'The newly created synthetic fixture is retained for inspection; this script never deletes databases. Use a new database name after resolving the failure.' }
    throw
} finally {
    if ($fixture) { $fixture.Dispose() }
    $admin.Dispose()
}
