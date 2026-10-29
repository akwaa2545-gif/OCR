param([switch]$StaticOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$preparePath = Join-Path $root 'deploy/sql/20261027_prepare_employee_file_storage.sql'
$verifyPath = Join-Path $root 'deploy/sql/20261027_verify_employee_file_storage.sql'
foreach ($path in @($preparePath, $verifyPath)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Required SQL script missing: $path" }
}
$prepare = [IO.File]::ReadAllText($preparePath)
$verify = [IO.File]::ReadAllText($verifyPath)
if ($prepare -notmatch '@DryRun bit = 1' -or $prepare -notmatch '@PreparationReleaseVerified bit = 0') {
    throw 'Preparation must default to dry-run with an unverified preparation-release gate.'
}
if (!$prepare.Contains('@ExclusiveMigrationWindow bit = 0') -or !$prepare.Contains('@ExpectedServer sysname = NULL')) {
    throw 'Exclusive maintenance must be explicit, default-off and server guarded.'
}
if ($prepare -match '(?i)HASHBYTES\s*\(' -or $prepare -match '(?i)DROP\s+TABLE') {
    throw 'Preparation may not hash large content in SQL or drop legacy tables.'
}
if ($StaticOnly) { Write-Output 'PASS: SQL migration static safety contract'; exit 0 }
$connectionText = [Environment]::GetEnvironmentVariable('OCR_FILE_STORAGE_TEST_CONNECTION')
if ([string]::IsNullOrWhiteSpace($connectionText)) { throw 'Set OCR_FILE_STORAGE_TEST_CONNECTION for a dedicated synthetic local fixture.' }
$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connectionText
$database = $builder.get_InitialCatalog()
if ($database -notmatch '^OcrFileStorageTests_[A-Za-z0-9_]+$') { throw 'Fixture database name must start OcrFileStorageTests_.' }
if ($builder.get_DataSource() -notmatch '^(localhost|127\.0\.0\.1|\(local\)|\.)(,\d+|\\[A-Za-z0-9_]+)?$') { throw 'Fixture SQL server must be local.' }
$builder.set_InitialCatalog('master')
$admin = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
function Invoke-Sql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = $Sql
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
function Scalar([string]$Sql) {
    $command = $script:fixture.CreateCommand()
    $command.CommandText = $Sql
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Prepare([string]$Phase, [bool]$DryRun = $false, [bool]$ReleaseVerified = $true) {
    $sql = $prepare.Replace('@DryRun bit = 1', ('@DryRun bit = ' + [int]$DryRun)).
        Replace("@Phase nvarchar(16) = N'Schema'", "@Phase nvarchar(16) = N'$Phase'").
        Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'$database'").
        Replace("@ExpectedEnvironment nvarchar(128) = NULL", "@ExpectedEnvironment nvarchar(128) = N'SyntheticLocalFixture'").
        Replace('@PreparationReleaseVerified bit = 0', ('@PreparationReleaseVerified bit = ' + [int]$ReleaseVerified)).
        Replace('@KeyBackfillBatchSize int = 500', '@KeyBackfillBatchSize int = 1')
    Invoke-Sql $script:fixture $sql
}
function Must-Fail([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert $failed $Message
}
$fixture = $null
$created = $false
$maintenanceFixture = $null
$maintenanceCreated = $false
$maintenanceDatabase = 'OcrFileStorageTests_Maintenance_' + [Guid]::NewGuid().ToString('N')
try {
    $admin.Open()
    $existsCommand = $admin.CreateCommand()
    $existsCommand.CommandText = 'SELECT DB_ID(@name)'
    [void]$existsCommand.Parameters.Add('@name', [System.Data.SqlDbType]::NVarChar, 128)
    $existsCommand.Parameters['@name'].Value = $database
    try { $existing = $existsCommand.ExecuteScalar() } finally { $existsCommand.Dispose() }
    if ($existing -isnot [DBNull]) { throw 'Fixture refuses to reuse an existing database.' }
    Invoke-Sql $admin "CREATE DATABASE [$database]; ALTER DATABASE [$database] SET COMPATIBILITY_LEVEL = 100;"
    $created = $true
    $builder.set_InitialCatalog($database)
    $fixture = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
    $fixture.Open()
    $legacyFixtureSql = @'
EXEC sys.sp_addextendedproperty @name=N'OcrEnvironment', @value=N'SyntheticLocalFixture';
CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(10) COLLATE Latin1_General_100_CS_AS NOT NULL PRIMARY KEY, Photo nvarchar(50) NULL, ResignDate datetime NULL, StatusWork nvarchar(1) NULL);
CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10) COLLATE Latin1_General_100_CS_AS NULL, Download nvarchar(100) NULL);
CREATE TABLE dbo.tblQualified_Obsoleted(EmpCode nvarchar(10) COLLATE Latin1_General_100_CS_AS NULL, Download nvarchar(100) NULL);
INSERT dbo.tblEmployee(EmpCode,Photo) VALUES(N'00001',N'legacy-photo.jpg'),(N'00002',NULL);
INSERT dbo.tblQualified(EmpCode,Download) VALUES(N'00001',N'old-a.pdf'),(NULL,N'orphan.pdf'),(N'00002',N'old-b.pdf');
INSERT dbo.tblQualified_Obsoleted(EmpCode,Download) VALUES(N'00001',N'history.pdf');
'@
    Invoke-Sql $fixture $legacyFixtureSql
    Prepare 'Schema' $true $false
    Assert ([int](Scalar "SELECT COUNT(*) FROM sys.tables WHERE name='tblEmployeeFile'") -eq 0) 'Dry-run committed DDL.'
    Must-Fail { Prepare 'Schema' $false $false } 'Unverified preparation release was accepted.'
    $badGuard = $prepare.Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'wrongDatabase'")
    Must-Fail { Invoke-Sql $fixture $badGuard } 'Database guard did not reject a mismatch.'
    $badEnvironment = $prepare.Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'$database'").Replace('@ExpectedEnvironment nvarchar(128) = NULL', "@ExpectedEnvironment nvarchar(128) = N'wrongEnvironment'")
    Must-Fail { Invoke-Sql $fixture $badEnvironment } 'Environment guard did not reject a mismatch.'
    Invoke-Sql $fixture 'ALTER TABLE dbo.tblEmployee ADD LegacyVersion rowversion;'
    Must-Fail { Prepare 'Schema' } 'Existing differently named rowversion was accepted.'
    Invoke-Sql $fixture 'ALTER TABLE dbo.tblEmployee DROP COLUMN LegacyVersion;'
    Prepare 'Schema'
    Prepare 'Schema'
    Assert ([int](Scalar 'SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema') -eq 0) 'Capability marker enabled before key finalization.'
    Must-Fail { Prepare 'Finalize' } 'Finalization accepted missing stable keys.'
    Prepare 'Backfill' $true
    Assert ([int](Scalar 'SELECT COUNT(*) FROM dbo.tblQualified WHERE OcrSkillRowId IS NULL') -eq 3) 'Dry-run committed metadata backfill.'
    Prepare 'Backfill'
    Assert ([int](Scalar 'SELECT COUNT(*) FROM dbo.tblQualified WHERE OcrSkillRowId IS NULL') -eq 2) 'Metadata backfill ignored its batch budget.'
    Prepare 'Backfill'
    Prepare 'Backfill'
    Prepare 'Finalize'
    Invoke-Sql $fixture 'UPDATE dbo.tblEmployeeFileStorageSchema SET SchemaVersion=0;'
    # Trusted same-name substitutes must never activate readiness. Every other
    # shape is finalized so verifier failures exercise the substituted contract.
    $tamperedCases = @(
        'ALTER TABLE dbo.tblEmployeeFile DROP CONSTRAINT CK_tblEmployeeFile_Length; ALTER TABLE dbo.tblEmployeeFile WITH CHECK ADD CONSTRAINT CK_tblEmployeeFile_Length CHECK(1=1);',
        'ALTER TABLE dbo.tblEmployeeFile DROP CONSTRAINT CK_tblEmployeeFile_Kind; ALTER TABLE dbo.tblEmployeeFile WITH CHECK ADD CONSTRAINT CK_tblEmployeeFile_Kind CHECK(Kind=N''photo'' OR Kind=N''certificate'');',
        'ALTER TABLE dbo.tblEmployeeFileAlias DROP CONSTRAINT CK_tblEmployeeFileAlias_Kind; ALTER TABLE dbo.tblEmployeeFileAlias WITH CHECK ADD CONSTRAINT CK_tblEmployeeFileAlias_Kind CHECK(Kind=N''photo'' OR Kind=N''certificate'');',
        'ALTER TABLE dbo.tblEmployeeFileMigration DROP CONSTRAINT CK_tblEmployeeFileMigration_Kind; ALTER TABLE dbo.tblEmployeeFileMigration WITH CHECK ADD CONSTRAINT CK_tblEmployeeFileMigration_Kind CHECK(Kind=N''photo'' OR Kind=N''certificate'');',
        'ALTER TABLE dbo.tblEmployeeFileAlias DROP CONSTRAINT PK_tblEmployeeFileAlias; ALTER TABLE dbo.tblEmployeeFileAlias ADD CONSTRAINT PK_tblEmployeeFileAlias PRIMARY KEY(EmpCode,Kind,CanonicalKeyHash,FileId);',
        'ALTER TABLE dbo.tblQualified DROP CONSTRAINT FK_tblQualified_FileOwner; ALTER TABLE dbo.tblQualified WITH CHECK ADD CONSTRAINT FK_tblQualified_FileOwner FOREIGN KEY(CertificateFileId) REFERENCES dbo.tblEmployeeFile(FileId);'
    )
    foreach ($tampering in $tamperedCases) {
        # Preparation explicitly forbids caller transactions; preserve canonical
        # definitions and restore by rerunning the captured DDL after each test.
        $restore = if ($tampering -like '*CK_tblEmployeeFile_Length*') {
            'ALTER TABLE dbo.tblEmployeeFile DROP CONSTRAINT CK_tblEmployeeFile_Length; ALTER TABLE dbo.tblEmployeeFile WITH CHECK ADD CONSTRAINT CK_tblEmployeeFile_Length CHECK(ByteLength>0 AND ByteLength=DATALENGTH(Content));'
        } elseif ($tampering -match 'DROP CONSTRAINT (CK_(tblEmployeeFile(?:Alias|Migration)?)_Kind);') {
            'ALTER TABLE dbo.' + $Matches[2] + ' DROP CONSTRAINT ' + $Matches[1] + '; ALTER TABLE dbo.' + $Matches[2] + ' WITH CHECK ADD CONSTRAINT ' + $Matches[1] + ' CHECK(Kind=N''Photo'' OR Kind=N''Certificate'');'
        } elseif ($tampering -like '*PK_tblEmployeeFileAlias*') {
            'ALTER TABLE dbo.tblEmployeeFileAlias DROP CONSTRAINT PK_tblEmployeeFileAlias; ALTER TABLE dbo.tblEmployeeFileAlias ADD CONSTRAINT PK_tblEmployeeFileAlias PRIMARY KEY(EmpCode,Kind,CanonicalKeyHash);'
        } else {
            'ALTER TABLE dbo.tblQualified DROP CONSTRAINT FK_tblQualified_FileOwner; ALTER TABLE dbo.tblQualified WITH CHECK ADD CONSTRAINT FK_tblQualified_FileOwner FOREIGN KEY(CertificateFileId,EmpCode) REFERENCES dbo.tblEmployeeFile(FileId,EmpCode);'
        }
        try {
            Invoke-Sql $fixture $tampering
            Must-Fail { Prepare 'Finalize' } 'Finalize trusted a same-name incompatible constraint/key.'
            Assert ([int](Scalar 'SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema') -eq 0) 'Incompatible schema activated readiness.'
            Invoke-Sql $fixture 'UPDATE dbo.tblEmployeeFileStorageSchema SET SchemaVersion=1;'
            Must-Fail { Invoke-Sql $fixture $verify } 'Verifier trusted a same-name incompatible constraint/key.'
        } finally {
            Invoke-Sql $fixture $restore
            Invoke-Sql $fixture 'UPDATE dbo.tblEmployeeFileStorageSchema SET SchemaVersion=0;'
        }
    }
    Prepare 'Finalize' $true
    Assert ([int](Scalar 'SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema') -eq 0) 'Dry-run committed capability activation.'
    Prepare 'Finalize'
    Prepare 'Finalize'
    Invoke-Sql $fixture $verify
    Assert ([int](Scalar 'SELECT SchemaVersion FROM dbo.tblEmployeeFileStorageSchema') -eq 1) 'Capability marker missing.'
    Assert ([string](Scalar "SELECT Photo FROM dbo.tblEmployee WHERE EmpCode=N'00001'") -eq 'legacy-photo.jpg') 'Legacy photo changed.'
    Assert ([int](Scalar "SELECT COUNT(*) FROM dbo.tblQualified WHERE Download IN (N'old-a.pdf',N'orphan.pdf',N'old-b.pdf')") -eq 3) 'Legacy downloads changed.'
    Assert ([string](Scalar "SELECT collation_name FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblEmployeeFile') AND name='EmpCode'") -eq 'Latin1_General_100_CS_AS') 'Employee collation not preserved.'
    Invoke-Sql $fixture @'
INSERT dbo.tblEmployeeFile(FileId,EmpCode,Kind,OriginalFileName,ContentType,ByteLength,Sha256,CreatedUtc,CreatedBy,Content)
VALUES('11111111-1111-1111-1111-111111111111',N'00001',N'Photo',N'x.png','image/png',1,CONVERT(binary(32),0x01),SYSUTCDATETIME(),N'fixture',0x01);
UPDATE dbo.tblEmployee SET PhotoFileId='11111111-1111-1111-1111-111111111111' WHERE EmpCode=N'00001';
'@
    Must-Fail { Invoke-Sql $fixture "UPDATE dbo.tblEmployee SET PhotoFileId='11111111-1111-1111-1111-111111111111' WHERE EmpCode=N'00002'" } 'Cross-owner file FK accepted.'
    Must-Fail { Invoke-Sql $fixture "UPDATE dbo.tblQualified SET CertificateFileId='11111111-1111-1111-1111-111111111111' WHERE EmpCode IS NULL" } 'Null employee accepted a file reference.'
    Must-Fail { Invoke-Sql $fixture "UPDATE dbo.tblEmployeeFile SET ByteLength=2 WHERE FileId='11111111-1111-1111-1111-111111111111'" } 'Invalid content length accepted.'
    Must-Fail { Invoke-Sql $fixture "UPDATE dbo.tblEmployeeFile SET Kind=N'Other' WHERE FileId='11111111-1111-1111-1111-111111111111'" } 'Invalid file kind accepted.'
    Must-Fail { Invoke-Sql $fixture "UPDATE dbo.tblEmployeeFile SET Kind=N'photo' WHERE FileId='11111111-1111-1111-1111-111111111111'" } 'Case-variant file kind accepted.'
    Invoke-Sql $fixture @'
INSERT dbo.tblEmployeeFileMigration(EmpCode,Kind,SourceKey,SourceKeyHash,Sha256,FileId,ImportedUtc)
VALUES(N'00001',N'Photo',N'x.png',CONVERT(binary(32),0x01),CONVERT(binary(32),0x01),'11111111-1111-1111-1111-111111111111',SYSUTCDATETIME());
INSERT dbo.tblEmployeeFileAlias(EmpCode,Kind,CanonicalKey,CanonicalKeyHash,FileId)
VALUES(N'00001',N'Photo',N'x.png',CONVERT(binary(32),0x01),'11111111-1111-1111-1111-111111111111');
INSERT dbo.tblQualified_Obsoleted(EmpCode,Download,OcrSkillRowId,CertificateFileId)
SELECT EmpCode,Download,OcrSkillRowId,CertificateFileId FROM dbo.tblQualified WHERE Download=N'old-a.pdf';
'@
    Must-Fail { Invoke-Sql $fixture "INSERT dbo.tblEmployeeFileMigration SELECT EmpCode,Kind,SourceKey,SourceKeyHash,Sha256,FileId,ImportedUtc FROM dbo.tblEmployeeFileMigration" } 'Duplicate import identity accepted.'
    Invoke-Sql $fixture $verify
    # A separate fresh database proves real Schema/Backfill/Finalize without an
    # environment property or a fictional preparation-release assertion.
    Invoke-Sql $admin "CREATE DATABASE [$maintenanceDatabase] COLLATE Latin1_General_100_CS_AS; ALTER DATABASE [$maintenanceDatabase] SET COMPATIBILITY_LEVEL=100;"
    $maintenanceCreated = $true
    $builder.set_InitialCatalog($maintenanceDatabase)
    $maintenanceFixture = New-Object System.Data.SqlClient.SqlConnection $builder.get_ConnectionString()
    $maintenanceFixture.Open()
    $withoutEnvironment = $legacyFixtureSql.Replace("EXEC sys.sp_addextendedproperty @name=N'OcrEnvironment', @value=N'SyntheticLocalFixture';", '')
    Invoke-Sql $maintenanceFixture $withoutEnvironment
    $targetOnly = $prepare.Replace('@ExpectedDatabase sysname = NULL', "@ExpectedDatabase sysname = N'$maintenanceDatabase'")
    Must-Fail { Invoke-Sql $maintenanceFixture $targetOnly } 'Normal mode accepted absent environment/release evidence.'
    $exclusiveNoServer = $targetOnly.Replace('@ExclusiveMigrationWindow bit = 0', '@ExclusiveMigrationWindow bit = 1')
    Must-Fail { Invoke-Sql $maintenanceFixture $exclusiveNoServer } 'Exclusive mode accepted an unset server guard.'
    $serverName = [string](Scalar "SELECT CONVERT(sysname,SERVERPROPERTY('ServerName'))")
    $serverLiteral = $serverName.Replace("'", "''")
    $exclusive = $exclusiveNoServer.Replace('@ExpectedServer sysname = NULL', "@ExpectedServer sysname = N'$serverLiteral'")
    Must-Fail { Invoke-Sql $maintenanceFixture ($exclusive.Replace("@ExpectedServer sysname = N'$serverLiteral'", "@ExpectedServer sysname = N'WrongServer'")) } 'Exclusive mode accepted a mismatched server.'
    Must-Fail { Invoke-Sql $maintenanceFixture ($exclusive.Replace("@ExpectedDatabase sysname = N'$maintenanceDatabase'", "@ExpectedDatabase sysname = N'WrongDatabase'")) } 'Exclusive mode accepted a mismatched database.'
    Invoke-Sql $maintenanceFixture $exclusive
    Invoke-Sql $maintenanceFixture "IF OBJECT_ID(N'dbo.tblEmployeeFile',N'U') IS NOT NULL THROW 52300,'Exclusive dry-run committed DDL.',1;"
    $exclusiveApply = $exclusive.Replace('@DryRun bit = 1', '@DryRun bit = 0')
    foreach ($phase in @('Schema','Backfill','Finalize')) {
        Invoke-Sql $maintenanceFixture ($exclusiveApply.Replace("@Phase nvarchar(16) = N'Schema'", "@Phase nvarchar(16) = N'$phase'"))
    }
    Invoke-Sql $maintenanceFixture $verify
    Invoke-Sql $maintenanceFixture @'
IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment') THROW 52301,'Maintenance fabricated environment evidence.',1;
IF NOT EXISTS(SELECT 1 FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaId=1 AND SchemaVersion=1) THROW 52302,'Maintenance did not finalize.',1;
IF (SELECT Photo FROM dbo.tblEmployee WHERE EmpCode=N'00001')<>N'legacy-photo.jpg' THROW 52303,'Maintenance changed a legacy path.',1;
'@
    Must-Fail { Invoke-Sql $maintenanceFixture ($exclusiveApply.Replace('@ExclusiveMigrationWindow bit = 1', '@ExclusiveMigrationWindow bit = 0')) } 'Maintenance completion weakened the later normal release/environment gate.'
    Write-Output 'PASS: compat100 normal and exclusive-maintenance schema, default-deny/target gates, no fabricated environment/release evidence, idempotency, bounded backfill, FK, exact literal constraints, archive and journal.'
    Write-Output 'Newer-engine compatibility 100 is not actual SQL Server 2014 engine validation.'
} finally {
    if ($maintenanceFixture) { $maintenanceFixture.Dispose() }
    if ($maintenanceCreated -and $admin.State -eq 'Open' -and $maintenanceDatabase -match '^OcrFileStorageTests_Maintenance_[a-f0-9]{32}$') {
        Invoke-Sql $admin "ALTER DATABASE [$maintenanceDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$maintenanceDatabase];"
    }
    if ($fixture) { $fixture.Dispose() }
    if ($created -and $admin.State -eq 'Open') { Invoke-Sql $admin "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];" }
    $admin.Dispose()
}
