/* SQL Server 2014 additive preparation. No content import; no legacy column changes.
   Run Schema, bounded Backfill invocations, then Finalize. Each invocation is one
   transaction and defaults to rollback. Replace only the declarations below.
   Normal apply requires all-client preparation release evidence plus verified
   database/environment guards. An explicit exclusive migration window is an
   alternative only while every legacy writer is kept unused until SQL-aware
   deployment. That path requires the exact inspected server AND database and
   makes no assertion that a preparation release has been deployed.
   OcrEnvironment is a DBA-managed property; this script never fabricates it.
   DDL requires deployment privileges; do not grant these to application writers.
   Alias/source SHA-256 values are computed and validated by the application.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @DryRun bit = 1;
DECLARE @PreparationReleaseVerified bit = 0;
DECLARE @ExpectedDatabase sysname = NULL;
DECLARE @ExpectedEnvironment nvarchar(128) = NULL;
DECLARE @ExclusiveMigrationWindow bit = 0;
DECLARE @ExpectedServer sysname = NULL;
DECLARE @Phase nvarchar(16) = N'Schema';
DECLARE @KeyBackfillBatchSize int = 500;

IF @ExpectedDatabase IS NULL OR @ExpectedDatabase <> DB_NAME()
    THROW 51000, 'Expected database guard is unset or mismatched.', 1;
IF @DryRun IS NULL OR @PreparationReleaseVerified IS NULL OR @ExclusiveMigrationWindow IS NULL
    THROW 51023, 'Migration flags must be explicitly zero or one.', 1;
IF @ExclusiveMigrationWindow=1 AND (@ExpectedServer IS NULL OR @ExpectedServer<>CONVERT(sysname,SERVERPROPERTY('ServerName')))
    THROW 51024, 'Exclusive migration requires the exact inspected server.', 1;
IF (@ExclusiveMigrationWindow=0 OR @ExpectedEnvironment IS NOT NULL) AND (@ExpectedEnvironment IS NULL OR NOT EXISTS
    (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'
     AND CONVERT(nvarchar(128),value)=@ExpectedEnvironment))
    THROW 51001, 'Expected OcrEnvironment database property is unset or mismatched.', 1;
IF @DryRun=0 AND @PreparationReleaseVerified=0 AND @ExclusiveMigrationWindow=0
    THROW 51002, 'Apply requires verified preparation release or an explicit exclusive migration window.', 1;
IF @Phase NOT IN(N'Schema',N'Backfill',N'Finalize')
    THROW 51003, 'Phase must be Schema, Backfill or Finalize.', 1;
IF @KeyBackfillBatchSize < 1 OR @KeyBackfillBatchSize > 10000
    THROW 51004, 'Key backfill batch size must be between 1 and 10000.', 1;
IF @@TRANCOUNT<>0 THROW 51005, 'Run preparation outside an existing transaction.', 1;
IF OBJECT_ID(N'dbo.tblEmployee',N'U') IS NULL
 OR OBJECT_ID(N'dbo.tblQualified',N'U') IS NULL
 OR OBJECT_ID(N'dbo.tblQualified_Obsoleted',N'U') IS NULL
    THROW 51006, 'Required legacy owner tables are absent.', 1;

DECLARE @EmployeeCollation sysname;
SELECT @EmployeeCollation=collation_name FROM sys.columns
WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'EmpCode'
  AND system_type_id=231 AND max_length=20 AND is_nullable=0;
IF @EmployeeCollation IS NULL OR NOT EXISTS(SELECT 1 FROM sys.fn_helpcollations() WHERE name=@EmployeeCollation) OR NOT EXISTS
 (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
  JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
  WHERE i.object_id=OBJECT_ID(N'dbo.tblEmployee') AND i.is_primary_key=1 AND c.name=N'EmpCode'
  AND ic.key_ordinal=1 AND 1=(SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0))
    THROW 51007, 'Expected employee nvarchar(10) non-null single-column primary key.', 1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.tblEmployee'),OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted'))
           AND system_type_id=189 AND name<>N'FileStorageVersion')
    THROW 51008, 'Existing rowversion has another name; resolve concurrency contract before preparation.', 1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted'))
           AND name=N'EmpCode' AND (system_type_id<>231 OR max_length<>20 OR collation_name<>@EmployeeCollation))
    THROW 51009, 'Qualification employee code type/collation differs from employee key.', 1;
IF (SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted')) AND name=N'EmpCode')<>2
    THROW 51010, 'Qualification employee code column is missing.', 1;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'Photo' AND (system_type_id<>231 OR max_length<>100))
 OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'Photo')
 OR (SELECT COUNT(*) FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted')) AND name=N'Download' AND system_type_id=231 AND max_length=200)<>2
    THROW 51011, 'Legacy Photo/Download shape differs from the inspected schema.', 1;

DECLARE @Sql nvarchar(max), @Owner sysname, @Reference sysname;
BEGIN TRY
 BEGIN TRANSACTION;
 IF @Phase=N'Schema'
 BEGIN
  IF OBJECT_ID(N'dbo.tblEmployeeFile',N'U') IS NULL
  BEGIN
   SET @Sql=N'CREATE TABLE dbo.tblEmployeeFile(
    FileId uniqueidentifier NOT NULL CONSTRAINT PK_tblEmployeeFile PRIMARY KEY,
    EmpCode nvarchar(10) COLLATE '+@EmployeeCollation+N' NOT NULL,
    Kind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    OriginalFileName nvarchar(255) NOT NULL, ContentType varchar(100) NOT NULL,
    ByteLength bigint NOT NULL, Sha256 binary(32) NOT NULL,
    CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_tblEmployeeFile_CreatedUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy nvarchar(128) NOT NULL, SourceKey nvarchar(512) NULL, Content varbinary(max) NOT NULL,
    CONSTRAINT UQ_tblEmployeeFile_FileId_EmpCode UNIQUE(FileId,EmpCode),
    CONSTRAINT CK_tblEmployeeFile_Kind CHECK(Kind IN(N''Photo'',N''Certificate'')),
    CONSTRAINT CK_tblEmployeeFile_Length CHECK(ByteLength>0 AND ByteLength=DATALENGTH(Content)),
    CONSTRAINT FK_tblEmployeeFile_Employee FOREIGN KEY(EmpCode) REFERENCES dbo.tblEmployee(EmpCode));';
   EXEC sys.sp_executesql @Sql;
  END;
  IF OBJECT_ID(N'dbo.tblEmployeeFileAlias',N'U') IS NULL
  BEGIN
   SET @Sql=N'CREATE TABLE dbo.tblEmployeeFileAlias(
    EmpCode nvarchar(10) COLLATE '+@EmployeeCollation+N' NOT NULL,
    Kind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    CanonicalKey nvarchar(512) NOT NULL, CanonicalKeyHash binary(32) NOT NULL,
    FileId uniqueidentifier NOT NULL, FileStorageVersion rowversion NOT NULL,
    CONSTRAINT PK_tblEmployeeFileAlias PRIMARY KEY(EmpCode,Kind,CanonicalKeyHash),
    CONSTRAINT CK_tblEmployeeFileAlias_Kind CHECK(Kind IN(N''Photo'',N''Certificate'')),
    CONSTRAINT FK_tblEmployeeFileAlias_Owner FOREIGN KEY(FileId,EmpCode) REFERENCES dbo.tblEmployeeFile(FileId,EmpCode));';
   EXEC sys.sp_executesql @Sql;
  END;
  IF OBJECT_ID(N'dbo.tblEmployeeFileMigration',N'U') IS NULL
  BEGIN
   SET @Sql=N'CREATE TABLE dbo.tblEmployeeFileMigration(
    EmpCode nvarchar(10) COLLATE '+@EmployeeCollation+N' NOT NULL,
    Kind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    SourceKey nvarchar(512) NOT NULL, SourceKeyHash binary(32) NOT NULL, Sha256 binary(32) NOT NULL,
    FileId uniqueidentifier NOT NULL, ImportedUtc datetime2(7) NOT NULL,
    CONSTRAINT PK_tblEmployeeFileMigration PRIMARY KEY(EmpCode,Kind,SourceKeyHash,Sha256),
    CONSTRAINT CK_tblEmployeeFileMigration_Kind CHECK(Kind IN(N''Photo'',N''Certificate'')),
    CONSTRAINT FK_tblEmployeeFileMigration_Owner FOREIGN KEY(FileId,EmpCode) REFERENCES dbo.tblEmployeeFile(FileId,EmpCode));';
   EXEC sys.sp_executesql @Sql;
  END;
  IF OBJECT_ID(N'dbo.tblEmployeeFileStorageSchema',N'U') IS NULL
  BEGIN
   CREATE TABLE dbo.tblEmployeeFileStorageSchema(
    SchemaId tinyint NOT NULL CONSTRAINT PK_tblEmployeeFileStorageSchema PRIMARY KEY,
    SchemaVersion int NOT NULL, PreparedUtc datetime2(7) NOT NULL,
    CONSTRAINT CK_tblEmployeeFileStorageSchema_Id CHECK(SchemaId=1));
   INSERT dbo.tblEmployeeFileStorageSchema(SchemaId,SchemaVersion,PreparedUtc) VALUES(1,0,SYSUTCDATETIME());
  END;
  DECLARE owners CURSOR LOCAL FAST_FORWARD FOR
   SELECT N'tblEmployee',N'PhotoFileId' UNION ALL SELECT N'tblQualified',N'CertificateFileId' UNION ALL SELECT N'tblQualified_Obsoleted',N'CertificateFileId';
  OPEN owners;
  FETCH NEXT FROM owners INTO @Owner,@Reference;
  WHILE @@FETCH_STATUS=0
  BEGIN
   IF COL_LENGTH(N'dbo.'+@Owner,@Reference) IS NULL
   BEGIN
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ADD '+QUOTENAME(@Reference)+N' uniqueidentifier NULL;';
    EXEC sys.sp_executesql @Sql;
   END;
   IF COL_LENGTH(N'dbo.'+@Owner,N'FileStorageVersion') IS NULL
   BEGIN
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ADD FileStorageVersion rowversion NOT NULL;';
    EXEC sys.sp_executesql @Sql;
   END;
   IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.'+@Owner) AND
      ((name=@Reference AND (system_type_id<>36 OR is_nullable<>1)) OR (name=N'FileStorageVersion' AND system_type_id<>189)))
    THROW 51012, 'Existing owner file reference/concurrency column has an incompatible type.', 1;
   IF OBJECT_ID(N'dbo.FK_'+@Owner+N'_FileOwner',N'F') IS NULL
   BEGIN
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(N'FK_'+@Owner+N'_FileOwner')+
     N' FOREIGN KEY('+QUOTENAME(@Reference)+N',EmpCode) REFERENCES dbo.tblEmployeeFile(FileId,EmpCode);';
    EXEC sys.sp_executesql @Sql;
   END;
   IF OBJECT_ID(N'dbo.CK_'+@Owner+N'_FileHasEmployee',N'C') IS NULL
   BEGIN
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(N'CK_'+@Owner+N'_FileHasEmployee')+
     N' CHECK('+QUOTENAME(@Reference)+N' IS NULL OR EmpCode IS NOT NULL);';
    EXEC sys.sp_executesql @Sql;
   END;
   IF @Owner<>N'tblEmployee'
   BEGIN
    IF COL_LENGTH(N'dbo.'+@Owner,N'OcrSkillRowId') IS NULL
    BEGIN
     SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ADD OcrSkillRowId uniqueidentifier NULL CONSTRAINT '+QUOTENAME(N'DF_'+@Owner+N'_OcrSkillRowId')+N' DEFAULT NEWID();';
     EXEC sys.sp_executesql @Sql;
    END;
    IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.'+@Owner) AND name=N'OcrSkillRowId' AND system_type_id<>36)
     THROW 51013, 'Existing skill row identity has an incompatible type.', 1;
    IF NOT EXISTS(SELECT 1 FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
                  WHERE d.parent_object_id=OBJECT_ID(N'dbo.'+@Owner) AND c.name=N'OcrSkillRowId')
    BEGIN
     SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ADD CONSTRAINT '+QUOTENAME(N'DF_'+@Owner+N'_OcrSkillRowId')+N' DEFAULT NEWID() FOR OcrSkillRowId;';
     EXEC sys.sp_executesql @Sql;
    END;
   END;
   FETCH NEXT FROM owners INTO @Owner,@Reference;
  END;
  CLOSE owners;
  DEALLOCATE owners;
 END
 ELSE
 BEGIN
  IF OBJECT_ID(N'dbo.tblEmployeeFileStorageSchema',N'U') IS NULL
   THROW 51014, 'Run Schema phase first.', 1;
  IF @Phase=N'Backfill'
  BEGIN
   /* One bounded batch PER TABLE. Repeat explicitly until both remaining counts
      are zero. Structural IDs include resigned/orphan rows; no bytes are read. */
   EXEC sys.sp_executesql N'UPDATE TOP(@BatchSize) dbo.tblQualified SET OcrSkillRowId=NEWID() WHERE OcrSkillRowId IS NULL;
      UPDATE TOP(@BatchSize) dbo.tblQualified_Obsoleted SET OcrSkillRowId=NEWID() WHERE OcrSkillRowId IS NULL;',N'@BatchSize int',@KeyBackfillBatchSize;
  END
  ELSE
  BEGIN
   EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.tblQualified WHERE OcrSkillRowId IS NULL)
      OR EXISTS(SELECT 1 FROM dbo.tblQualified_Obsoleted WHERE OcrSkillRowId IS NULL)
      THROW 51015, ''Run bounded key backfill until no null skill row IDs remain.'', 1;
      IF EXISTS(SELECT OcrSkillRowId FROM dbo.tblQualified GROUP BY OcrSkillRowId HAVING COUNT(*)>1)
      OR EXISTS(SELECT OcrSkillRowId FROM dbo.tblQualified_Obsoleted GROUP BY OcrSkillRowId HAVING COUNT(*)>1)
      THROW 51016, ''Duplicate skill row IDs prevent finalization.'', 1;';
   DECLARE finalizeOwners CURSOR LOCAL FAST_FORWARD FOR SELECT N'tblQualified' UNION ALL SELECT N'tblQualified_Obsoleted';
   OPEN finalizeOwners;
   FETCH NEXT FROM finalizeOwners INTO @Owner;
   WHILE @@FETCH_STATUS=0
   BEGIN
    IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.'+@Owner) AND name=N'OcrSkillRowId' AND is_nullable=1)
    BEGIN
     SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ALTER COLUMN OcrSkillRowId uniqueidentifier NOT NULL;';
     EXEC sys.sp_executesql @Sql;
    END;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@Owner) AND name=N'UQ_'+@Owner+N'_OcrSkillRowId')
    BEGIN
     SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Owner)+N' ADD CONSTRAINT '+QUOTENAME(N'UQ_'+@Owner+N'_OcrSkillRowId')+N' UNIQUE(OcrSkillRowId);';
     EXEC sys.sp_executesql @Sql;
    END;
    FETCH NEXT FROM finalizeOwners INTO @Owner;
   END;
   CLOSE finalizeOwners;
   DEALLOCATE finalizeOwners;
   /* BEGIN EXACT FILE STORAGE CONTRACT: keep identical in prepare and verify. */
DECLARE @ContractEmployeeCollation sysname=(SELECT collation_name FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'EmpCode');
IF @ContractEmployeeCollation IS NULL THROW 51200, 'Employee key metadata is unavailable.', 1;
IF EXISTS(SELECT 1 FROM (VALUES
 (N'tblEmployee',N'PhotoFileId',36,16,1),(N'tblEmployee',N'FileStorageVersion',189,8,0),
 (N'tblQualified',N'CertificateFileId',36,16,1),(N'tblQualified',N'FileStorageVersion',189,8,0),(N'tblQualified',N'OcrSkillRowId',36,16,0),
 (N'tblQualified_Obsoleted',N'CertificateFileId',36,16,1),(N'tblQualified_Obsoleted',N'FileStorageVersion',189,8,0),(N'tblQualified_Obsoleted',N'OcrSkillRowId',36,16,0),
 (N'tblEmployeeFile',N'FileId',36,16,0),(N'tblEmployeeFile',N'EmpCode',231,20,0),(N'tblEmployeeFile',N'Kind',231,32,0),
 (N'tblEmployeeFile',N'OriginalFileName',231,510,0),(N'tblEmployeeFile',N'ContentType',167,100,0),(N'tblEmployeeFile',N'ByteLength',127,8,0),
 (N'tblEmployeeFile',N'Sha256',173,32,0),(N'tblEmployeeFile',N'CreatedUtc',42,8,0),(N'tblEmployeeFile',N'CreatedBy',231,256,0),
 (N'tblEmployeeFile',N'SourceKey',231,1024,1),(N'tblEmployeeFile',N'Content',165,-1,0),
 (N'tblEmployeeFileAlias',N'EmpCode',231,20,0),(N'tblEmployeeFileAlias',N'Kind',231,32,0),(N'tblEmployeeFileAlias',N'CanonicalKey',231,1024,0),
 (N'tblEmployeeFileAlias',N'CanonicalKeyHash',173,32,0),(N'tblEmployeeFileAlias',N'FileId',36,16,0),(N'tblEmployeeFileAlias',N'FileStorageVersion',189,8,0),
 (N'tblEmployeeFileMigration',N'EmpCode',231,20,0),(N'tblEmployeeFileMigration',N'Kind',231,32,0),(N'tblEmployeeFileMigration',N'SourceKey',231,1024,0),
 (N'tblEmployeeFileMigration',N'SourceKeyHash',173,32,0),(N'tblEmployeeFileMigration',N'Sha256',173,32,0),(N'tblEmployeeFileMigration',N'FileId',36,16,0),(N'tblEmployeeFileMigration',N'ImportedUtc',42,8,0),
 (N'tblEmployeeFileStorageSchema',N'SchemaId',48,1,0),(N'tblEmployeeFileStorageSchema',N'SchemaVersion',56,4,0),(N'tblEmployeeFileStorageSchema',N'PreparedUtc',42,8,0)
 ) e(TableName,ColumnName,TypeId,MaxLength,Nullable)
 WHERE NOT EXISTS(SELECT 1 FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
 WHERE t.object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U') AND c.name=e.ColumnName AND c.system_type_id=e.TypeId
 AND c.user_type_id=c.system_type_id AND c.max_length=e.MaxLength AND c.is_nullable=e.Nullable AND c.is_computed=0))
 THROW 51201, 'Required file/owner/marker column shape is incompatible.', 1;
IF EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id IN(OBJECT_ID(N'dbo.tblEmployeeFile'),OBJECT_ID(N'dbo.tblEmployeeFileAlias'),OBJECT_ID(N'dbo.tblEmployeeFileMigration'))
 AND ((c.name=N'EmpCode' AND c.collation_name<>@ContractEmployeeCollation) OR (c.name=N'Kind' AND c.collation_name<>N'Latin1_General_100_BIN2')
 OR (c.system_type_id=42 AND c.scale<>7)))
 THROW 51202, 'File key collation or UTC precision is incompatible.', 1;
/* Compare complete CHECK predicates after removing SQL parser decoration. This
   deliberately fails closed on logically-equivalent but unapproved definitions. */
DECLARE @ExpectedChecks TABLE(TableName sysname,ConstraintName sysname,Expression nvarchar(1000),Alternative nvarchar(1000));
INSERT @ExpectedChecks VALUES
 (N'tblEmployeeFile',N'CK_tblEmployeeFile_Length',N'bytelength>0andbytelength=datalengthcontent',NULL),
 (N'tblEmployeeFile',N'CK_tblEmployeeFile_Kind',N'kind=n''photo''orkind=n''certificate''',N'kind=n''certificate''orkind=n''photo'''),
 (N'tblEmployeeFileAlias',N'CK_tblEmployeeFileAlias_Kind',N'kind=n''photo''orkind=n''certificate''',N'kind=n''certificate''orkind=n''photo'''),
 (N'tblEmployeeFileMigration',N'CK_tblEmployeeFileMigration_Kind',N'kind=n''photo''orkind=n''certificate''',N'kind=n''certificate''orkind=n''photo'''),
 (N'tblEmployee',N'CK_tblEmployee_FileHasEmployee',N'photofileidisnullorempcodeisnotnull',NULL),
 (N'tblQualified',N'CK_tblQualified_FileHasEmployee',N'certificatefileidisnullorempcodeisnotnull',NULL),
 (N'tblQualified_Obsoleted',N'CK_tblQualified_Obsoleted_FileHasEmployee',N'certificatefileidisnullorempcodeisnotnull',NULL),
 (N'tblEmployeeFileStorageSchema',N'CK_tblEmployeeFileStorageSchema_Id',N'schemaid=1',NULL);
IF EXISTS(SELECT 1 FROM @ExpectedChecks e WHERE NOT EXISTS(
 SELECT 1 FROM sys.check_constraints c CROSS APPLY(SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N''),NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')) AS Definition) normalized
 WHERE c.object_id=OBJECT_ID(N'dbo.'+e.ConstraintName,N'C') AND c.parent_object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U')
 AND c.is_disabled=0 AND c.is_not_trusted=0 AND c.is_not_for_replication=0
 AND (normalized.Definition=e.Expression OR normalized.Definition=e.Alternative)
 /* Parser keywords are normalized above; binary-exact string literals must
    retain their approved case rather than being normalized with identifiers. */
 AND (e.ConstraintName NOT IN(N'CK_tblEmployeeFile_Kind',N'CK_tblEmployeeFileAlias_Kind',N'CK_tblEmployeeFileMigration_Kind')
  OR (CHARINDEX(N'''Photo''',c.definition COLLATE Latin1_General_100_BIN2)>0
   AND CHARINDEX(N'''Certificate''',c.definition COLLATE Latin1_General_100_BIN2)>0))))
 THROW 51203, 'CHECK predicate is absent or differs from the approved contract.', 1;
DECLARE @ExpectedForeignKeys TABLE(ConstraintName sysname,ParentTable sysname,ParentColumn1 sysname,ParentColumn2 sysname NULL,ReferencedTable sysname,ReferencedColumn1 sysname,ReferencedColumn2 sysname NULL);
INSERT @ExpectedForeignKeys VALUES
 (N'FK_tblEmployeeFile_Employee',N'tblEmployeeFile',N'EmpCode',NULL,N'tblEmployee',N'EmpCode',NULL),
 (N'FK_tblEmployee_FileOwner',N'tblEmployee',N'PhotoFileId',N'EmpCode',N'tblEmployeeFile',N'FileId',N'EmpCode'),
 (N'FK_tblQualified_FileOwner',N'tblQualified',N'CertificateFileId',N'EmpCode',N'tblEmployeeFile',N'FileId',N'EmpCode'),
 (N'FK_tblQualified_Obsoleted_FileOwner',N'tblQualified_Obsoleted',N'CertificateFileId',N'EmpCode',N'tblEmployeeFile',N'FileId',N'EmpCode'),
 (N'FK_tblEmployeeFileAlias_Owner',N'tblEmployeeFileAlias',N'FileId',N'EmpCode',N'tblEmployeeFile',N'FileId',N'EmpCode'),
 (N'FK_tblEmployeeFileMigration_Owner',N'tblEmployeeFileMigration',N'FileId',N'EmpCode',N'tblEmployeeFile',N'FileId',N'EmpCode');
IF EXISTS(SELECT 1 FROM @ExpectedForeignKeys e WHERE NOT EXISTS(
 SELECT 1 FROM sys.foreign_keys f WHERE f.object_id=OBJECT_ID(N'dbo.'+e.ConstraintName,N'F')
 AND f.parent_object_id=OBJECT_ID(N'dbo.'+e.ParentTable,N'U') AND f.referenced_object_id=OBJECT_ID(N'dbo.'+e.ReferencedTable,N'U')
 AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0 AND f.delete_referential_action=0 AND f.update_referential_action=0
 AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id)=CASE WHEN e.ParentColumn2 IS NULL THEN 1 ELSE 2 END
 AND EXISTS(SELECT 1 FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id AND x.constraint_column_id=1
 AND COL_NAME(x.parent_object_id,x.parent_column_id)=e.ParentColumn1 AND COL_NAME(x.referenced_object_id,x.referenced_column_id)=e.ReferencedColumn1)
 AND (e.ParentColumn2 IS NULL OR EXISTS(SELECT 1 FROM sys.foreign_key_columns x WHERE x.constraint_object_id=f.object_id AND x.constraint_column_id=2
 AND COL_NAME(x.parent_object_id,x.parent_column_id)=e.ParentColumn2 AND COL_NAME(x.referenced_object_id,x.referenced_column_id)=e.ReferencedColumn2))))
 THROW 51204, 'Foreign key ownership columns/order/actions differ from the approved contract.', 1;
DECLARE @ExpectedKeys TABLE(TableName sysname,IndexName sysname,Column1 sysname,Column2 sysname NULL,Column3 sysname NULL,Column4 sysname NULL,IsPrimary bit);
INSERT @ExpectedKeys VALUES
 (N'tblEmployeeFile',N'PK_tblEmployeeFile',N'FileId',NULL,NULL,NULL,1),
 (N'tblEmployeeFile',N'UQ_tblEmployeeFile_FileId_EmpCode',N'FileId',N'EmpCode',NULL,NULL,0),
 (N'tblEmployeeFileAlias',N'PK_tblEmployeeFileAlias',N'EmpCode',N'Kind',N'CanonicalKeyHash',NULL,1),
 (N'tblEmployeeFileMigration',N'PK_tblEmployeeFileMigration',N'EmpCode',N'Kind',N'SourceKeyHash',N'Sha256',1),
 (N'tblQualified',N'UQ_tblQualified_OcrSkillRowId',N'OcrSkillRowId',NULL,NULL,NULL,0),
 (N'tblQualified_Obsoleted',N'UQ_tblQualified_Obsoleted_OcrSkillRowId',N'OcrSkillRowId',NULL,NULL,NULL,0),
 (N'tblEmployeeFileStorageSchema',N'PK_tblEmployeeFileStorageSchema',N'SchemaId',NULL,NULL,NULL,1);
IF EXISTS(SELECT 1 FROM @ExpectedKeys e WHERE NOT EXISTS(
 SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U') AND i.name=e.IndexName
 AND i.is_unique=1 AND i.is_primary_key=e.IsPrimary AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0 AND i.type IN(1,2)
 AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0)=CASE WHEN e.Column4 IS NOT NULL THEN 4 WHEN e.Column3 IS NOT NULL THEN 3 WHEN e.Column2 IS NOT NULL THEN 2 ELSE 1 END
 AND NOT EXISTS(SELECT 1 FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0
 AND (x.is_descending_key=1 OR COL_NAME(x.object_id,x.column_id)<>CASE x.key_ordinal WHEN 1 THEN e.Column1 WHEN 2 THEN e.Column2 WHEN 3 THEN e.Column3 WHEN 4 THEN e.Column4 END))))
 THROW 51205, 'Unique identity/idempotency key columns/order/filter differ from the approved contract.', 1;
IF EXISTS(SELECT 1 FROM (VALUES(N'tblQualified'),(N'tblQualified_Obsoleted')) e(TableName) WHERE NOT EXISTS(
 SELECT 1 FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
 WHERE d.parent_object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U') AND c.name=N'OcrSkillRowId'
 AND LOWER(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''))=N'newid'))
 THROW 51206, 'Stable row identity default differs from NEWID().', 1;
/* END EXACT FILE STORAGE CONTRACT. */
   EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaVersion NOT IN(0,1))
    THROW 51022, ''Unsupported schema marker version; do not downgrade it.'', 1;
    UPDATE dbo.tblEmployeeFileStorageSchema SET SchemaVersion=1,PreparedUtc=SYSUTCDATETIME() WHERE SchemaId=1;
    IF @@ROWCOUNT<>1 THROW 51021, ''Schema marker singleton is absent.'', 1;';
  END;
 END;
 IF @DryRun=1
 BEGIN
  ROLLBACK TRANSACTION;
  PRINT 'DRY RUN: all preparation changes rolled back.';
 END
 ELSE
 BEGIN
  COMMIT TRANSACTION;
  PRINT 'Preparation phase committed. No file bytes were imported.';
  IF @ExclusiveMigrationWindow=1
   PRINT 'Exclusive migration: keep legacy clients unused until SQL-aware builds are deployed. No preparation-release deployment was asserted.';
 END;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
