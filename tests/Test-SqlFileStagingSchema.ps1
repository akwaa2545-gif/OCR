param([switch]$StaticOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$migrationPath = Join-Path $root 'deploy/sql/20261027_stage_employee_files_only.sql'
if (!(Test-Path -LiteralPath $migrationPath)) { throw 'Staging-only migration is missing.' }
$migration = [IO.File]::ReadAllText($migrationPath)
foreach ($declaration in @('@DryRun bit = 1', '@StagingOnly bit = 0', '@ExpectedServer sysname = NULL', '@ExpectedDatabase sysname = NULL')) {
    if (!$migration.Contains($declaration)) { throw "Required staging guard missing: $declaration" }
}
if ($migration -match '(?i)(ALTER\s+TABLE|REFERENCES|TRIGGER\s+ON)\s+(?:\[?dbo\]?\.)?\[?tbl(?:Employee|Qualified|Qualified_Obsoleted)\]?\b' -or
    $migration -match '(?i)\b(?:INSERT|UPDATE|DELETE|MERGE)\s+(?:INTO\s+|FROM\s+)?(?:dbo\.)?tbl(?:Employee|Qualified|Qualified_Obsoleted)\b' -or
    $migration -match '(?i)\b(?:CREATE|ALTER)\s+TABLE\s+(?:dbo\.)?tblEmployeeFile(?:Alias|Migration|StorageSchema)?\b') {
    throw 'Staging migration must not change legacy tables or final storage/activation objects.'
}
if ($StaticOnly) { Write-Output 'PASS: staging-only migration static isolation and default guards'; exit 0 }
$connectionText = [Environment]::GetEnvironmentVariable('OCR_FILE_STORAGE_TEST_CONNECTION')
if ([string]::IsNullOrWhiteSpace($connectionText)) { throw 'Set a private OCR_FILE_STORAGE_TEST_CONNECTION for local synthetic SQL.' }
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionText
if ($builder.get_DataSource() -notmatch '^(localhost|127\.0\.0\.1)(,\d+|\\[A-Za-z0-9_]+)?$' -or
    $builder.get_InitialCatalog() -notmatch '^OcrFileStorageTests_[A-Za-z0-9_]+$') { throw 'Only a dedicated local SQL fixture is allowed.' }
$database = 'OcrFileStorageTests_StageSchema_' + [Guid]::NewGuid().ToString('N')
$builder.set_InitialCatalog('master')
$admin = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
$fixture = $null; $created = $false
function Invoke-Sql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
    $q = $Connection.CreateCommand(); $q.CommandTimeout = 120; $q.CommandText = $Sql
    try { [void]$q.ExecuteNonQuery() } finally { $q.Dispose() }
}
function Scalar([string]$Sql) {
    $q = $script:fixture.CreateCommand(); $q.CommandText = $Sql
    try { return $q.ExecuteScalar() } finally { $q.Dispose() }
}
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Must-Fail([scriptblock]$Action, [string]$Message) {
    $failed = $false; try { & $Action } catch { $failed = $true }; Assert $failed $Message
}
function Stage([bool]$DryRun = $false) {
    $sql = $migration.Replace('@DryRun bit = 1', ('@DryRun bit = ' + [int]$DryRun)).
        Replace('@StagingOnly bit = 0', '@StagingOnly bit = 1').
        Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'$database'").
        Replace('@ExpectedServer sysname = NULL', "@ExpectedServer sysname = CONVERT(sysname,SERVERPROPERTY('ServerName'))")
    Invoke-Sql $script:fixture $sql
}
try {
    $admin.Open()
    $q = $admin.CreateCommand(); $q.CommandText = 'SELECT DB_ID(@name)'
    [void]$q.Parameters.Add('@name', [System.Data.SqlDbType]::NVarChar, 128); $q.Parameters['@name'].Value = $database
    try { $exists = $q.ExecuteScalar() } finally { $q.Dispose() }
    if ($exists -isnot [DBNull]) { throw 'Synthetic fixture name already exists; refusing reuse.' }
    Invoke-Sql $admin "CREATE DATABASE [$database] COLLATE Latin1_General_100_CS_AS;"
    $created = $true
    Invoke-Sql $admin "ALTER DATABASE [$database] SET COMPATIBILITY_LEVEL=100;"
    $builder.set_InitialCatalog($database); $fixture = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString(); $fixture.Open()
    Invoke-Sql $fixture @'
CREATE TABLE dbo.tblEmployee(
 EmpCode nvarchar(10) NOT NULL PRIMARY KEY, EmpPassword nvarchar(128) NULL, JoinDate datetime NULL, JobGrade nvarchar(20) NULL, HEng nvarchar(50) NULL,
 PersonFnameEng nvarchar(100) NULL, PersonLnameEng nvarchar(100) NULL, HThai nvarchar(50) NULL, PersonFnameThai nvarchar(100) NULL, PersonLnameThai nvarchar(100) NULL,
 DeptID nvarchar(20) NULL, SectID nvarchar(20) NULL, WorkshopID nvarchar(20) NULL, Shift nvarchar(20) NULL, Photo nvarchar(50) NULL, Notice nvarchar(500) NULL,
 ResignBy nvarchar(128) NULL, ResignDate datetime NULL, StatusWork nvarchar(1) NULL, TransferBy nvarchar(128) NULL, TransferDate datetime NULL);
CREATE TABLE dbo.tblQualified(
 EmpCode nvarchar(10) NULL, ProcessName nvarchar(100) NULL, OperatorTraining nvarchar(100) NULL, TheoryTraining datetime NULL, OJTTraining datetime NULL,
 CertifiedDate datetime NULL, FullScore nvarchar(50) NULL, ActualScore nvarchar(50) NULL, TestResult nvarchar(50) NULL, JudgmentTheory nvarchar(50) NULL,
 KnowledgeScore nvarchar(50) NULL, KnowledgeLevel nvarchar(50) NULL, SkillScore nvarchar(50) NULL, SkillLevel nvarchar(50) NULL, JudgmentPractice nvarchar(50) NULL,
 ExpiryDate datetime NULL, Verifier nvarchar(128) NULL, VerifierDate datetime NULL, DisQualifiedDate datetime NULL, DisQualifiedBy nvarchar(128) NULL,
 TheReason nvarchar(500) NULL, Remark nvarchar(500) NULL, Download nvarchar(100) NULL, PromotionId uniqueidentifier NULL, PromotedAt datetime2(0) NULL, PromotedBy nvarchar(128) NULL);
SELECT TOP(0) * INTO dbo.tblQualified_Obsoleted FROM dbo.tblQualified;
INSERT dbo.tblEmployee(EmpCode,Photo,StatusWork) VALUES(N'00001',N'F:\00001.jpg',N'1'),(N'00002',N'F:\00002.jpg',N'1');
INSERT dbo.tblQualified(EmpCode,ProcessName,Download) VALUES(N'00001',N'Fixture process',N'Z:\00001\legacy.pdf');
INSERT dbo.tblQualified_Obsoleted SELECT * FROM dbo.tblQualified;
'@
    Assert ((Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblEmployee')") -eq 21) 'Old employee shape must be 21 columns.'
    Assert ((Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblQualified')") -eq 26) 'Old qualification shape must be 26 columns.'
    Must-Fail { Invoke-Sql $fixture $migration } 'Unset target/staging guards must fail.'
    Stage $true
    Assert ((Scalar "SELECT COUNT(*) FROM sys.tables WHERE name IN('OcrFileStageBlob','OcrFileStageJournal')") -eq 0) 'Dry-run leaked staging tables.'
    Stage; Stage
    Assert ((Scalar "SELECT COUNT(*) FROM sys.tables WHERE name IN('OcrFileStageBlob','OcrFileStageJournal')") -eq 2) 'Apply/repeat must create exactly two staging tables.'
    Assert ((Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblEmployee')") -eq 21) 'Employee shape changed.'
    Assert ((Scalar "SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID('dbo.tblQualified'),OBJECT_ID('dbo.tblQualified_Obsoleted'))") -eq 52) 'Qualification shape changed.'
    Assert ((Scalar "SELECT Photo FROM dbo.tblEmployee WHERE EmpCode=N'00001'") -eq 'F:\00001.jpg') 'Existing photo path changed.'
    Invoke-Sql $fixture 'INSERT dbo.tblQualified_Obsoleted SELECT * FROM dbo.tblQualified;'
    Assert ((Scalar 'SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted') -eq 2) 'Old positional archive failed after staging DDL.'
    Assert ((Scalar "SELECT COUNT(*) FROM sys.foreign_keys WHERE referenced_object_id IN(OBJECT_ID('dbo.tblEmployee'),OBJECT_ID('dbo.tblQualified'),OBJECT_ID('dbo.tblQualified_Obsoleted'))") -eq 0) 'Staging introduced a legacy-table FK.'
    Assert ((Scalar "SELECT COUNT(*) FROM sys.objects WHERE name IN('tblEmployeeFile','tblEmployeeFileAlias','tblEmployeeFileMigration','tblEmployeeFileStorageSchema')") -eq 0) 'Staging exposed final activation objects.'
    Invoke-Sql $fixture "ALTER TABLE dbo.OcrFileStageBlob DROP CONSTRAINT CK_OcrFileStageBlob_Kind; ALTER TABLE dbo.OcrFileStageBlob WITH CHECK ADD CONSTRAINT CK_OcrFileStageBlob_Kind CHECK(Kind=N'photo' OR Kind=N'certificate');"
    Must-Fail { Stage } 'Lowercase-literal CHECK was accepted for a binary-exact kind column.'
    Invoke-Sql $fixture "ALTER TABLE dbo.OcrFileStageBlob DROP CONSTRAINT CK_OcrFileStageBlob_Kind; ALTER TABLE dbo.OcrFileStageBlob WITH CHECK ADD CONSTRAINT CK_OcrFileStageBlob_Kind CHECK(Kind=N'Photo' OR Kind=N'Certificate');"
    $insert = "INSERT dbo.OcrFileStageBlob(FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedBy,Content) VALUES(NEWID(),N'00002',N'Photo',N'delete-proof.png',N'image/png',2,CONVERT(binary(32),0x11),N'Fixture',0x0102);"
    Invoke-Sql $fixture $insert
    Invoke-Sql $fixture "DELETE dbo.tblEmployee WHERE EmpCode=N'00002';"
    Assert ((Scalar "SELECT COUNT(*) FROM dbo.OcrFileStageBlob WHERE EmpCode=N'00002'") -eq 1) 'Legacy delete changed staged-copy retention.'
    $journal = "INSERT dbo.OcrFileStageJournal(StageId,FileId,EmpCode,Kind,CanonicalKey,CanonicalKeyHash,Sha256,LegacyReference,SourcePath,SourceModifiedUtc,ManifestId) SELECT NEWID(),FileId,N'00002',N'Photo',N'00002/delete-proof.png',CONVERT(binary(32),0x22),Sha256,N'F:\00002.jpg',N'C:\fixture\00002.jpg',SYSUTCDATETIME(),NEWID() FROM dbo.OcrFileStageBlob WHERE EmpCode=N'00002';"
    Invoke-Sql $fixture $journal
    Must-Fail { Invoke-Sql $fixture $journal } 'Duplicate idempotency key was accepted.'
    Must-Fail { Invoke-Sql $fixture ($journal.Replace("FileId,N'00002',N'Photo'", "FileId,N'00001',N'Photo'")) } 'Journal accepted a different blob employee.'
    Must-Fail { Invoke-Sql $fixture ($journal.Replace("FileId,N'00002',N'Photo'", "FileId,N'00002',N'Certificate'")) } 'Journal accepted a different blob kind.'
    Must-Fail { Invoke-Sql $fixture ($insert.Replace("N'00002',N'Photo'", "N'00001',N'photo'")) } 'Case-variant kind was accepted.'
    Must-Fail { Invoke-Sql $fixture ($insert.Replace(',2,CONVERT(binary', ',3,CONVERT(binary')) } 'Wrong declared content length was accepted.'
    Must-Fail { Invoke-Sql $fixture ($insert.Replace("N'delete-proof.png'", "N''")) } 'Empty filename metadata was accepted.'
    Invoke-Sql $fixture "ALTER TABLE dbo.OcrFileStageBlob DROP CONSTRAINT CK_OcrFileStageBlob_Length; ALTER TABLE dbo.OcrFileStageBlob WITH CHECK ADD CONSTRAINT CK_OcrFileStageBlob_Length CHECK(1=1);"
    Must-Fail { Stage } 'A trusted-name but incorrect constraint was accepted on repeat.'
    Write-Output 'PASS: staging dry-run/apply/repeat, old positional archive/path/delete compatibility, exact constraints and no activation objects (compatibility 100).'
} finally {
    if ($fixture) { $fixture.Dispose() }
    if ($created -and $database -match '^OcrFileStorageTests_StageSchema_[a-f0-9]{32}$') {
        Invoke-Sql $admin "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];"
    }
    $admin.Dispose()
}
