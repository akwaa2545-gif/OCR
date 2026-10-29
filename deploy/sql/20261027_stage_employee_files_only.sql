/* SQL Server 2014 staging only. No content import or application activation.
   This migration creates two independent append-only staging tables. It never
   adds owner columns, legacy FKs, aliases, triggers or the final readiness marker.
   Existing applications continue reading/writing their original physical paths.
   Declare the independently inspected server/database and staging-only intent.
   Dry-run rolls back all new DDL. Schema and binary batches are separate steps.
   Recovery retains staged copies; no destructive down migration is supplied.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
SET DEADLOCK_PRIORITY LOW;
DECLARE @DryRun bit = 1;
DECLARE @StagingOnly bit = 0;
DECLARE @ExpectedServer sysname = NULL;
DECLARE @ExpectedDatabase sysname = NULL;

IF @ExpectedServer IS NULL OR @ExpectedServer<>CONVERT(sysname,SERVERPROPERTY('ServerName'))
 THROW 52100, 'Expected server guard is unset or mismatched.', 1;
IF @ExpectedDatabase IS NULL OR @ExpectedDatabase<>DB_NAME()
 THROW 52101, 'Expected database guard is unset or mismatched.', 1;
IF @StagingOnly<>1 THROW 52102, 'Explicit staging-only intent is required.', 1;
IF @@TRANCOUNT<>0 THROW 52103, 'Run staging preparation outside an existing transaction.', 1;
IF OBJECT_ID(N'dbo.tblEmployee',N'U') IS NULL OR OBJECT_ID(N'dbo.tblQualified',N'U') IS NULL
 OR OBJECT_ID(N'dbo.tblQualified_Obsoleted',N'U') IS NULL
 THROW 52104, 'Required legacy owner tables are absent.', 1;
DECLARE @EmployeeCollation sysname;
SELECT @EmployeeCollation=collation_name FROM sys.columns
 WHERE object_id=OBJECT_ID(N'dbo.tblEmployee') AND name=N'EmpCode'
 AND system_type_id=231 AND user_type_id=231 AND max_length=20 AND is_nullable=0;
IF @EmployeeCollation IS NULL OR NOT EXISTS(SELECT 1 FROM sys.fn_helpcollations() WHERE name=@EmployeeCollation)
 OR @EmployeeCollation COLLATE Latin1_General_100_BIN2 LIKE N'%[^A-Za-z0-9_]%'
 THROW 52105, 'Expected employee nvarchar(10) non-null key/collation is unavailable.', 1;
IF (OBJECT_ID(N'dbo.OcrFileStageBlob') IS NOT NULL AND OBJECT_ID(N'dbo.OcrFileStageBlob',N'U') IS NULL)
 OR (OBJECT_ID(N'dbo.OcrFileStageJournal') IS NOT NULL AND OBJECT_ID(N'dbo.OcrFileStageJournal',N'U') IS NULL)
 THROW 52106, 'A non-table object already uses a staging name.', 1;

DECLARE @BeforeColumns nvarchar(max),@AfterColumns nvarchar(max),@Sql nvarchar(max);
SELECT @BeforeColumns=CONVERT(nvarchar(max),(
 SELECT c.object_id,c.column_id,c.name,c.system_type_id,c.user_type_id,c.max_length,c.precision,c.scale,
 c.is_nullable,c.is_identity,c.is_computed,c.collation_name,c.default_object_id,c.rule_object_id
 FROM sys.columns c WHERE c.object_id IN(OBJECT_ID(N'dbo.tblEmployee'),OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted'))
 ORDER BY c.object_id,c.column_id FOR XML RAW('column'),ROOT('legacy'),TYPE));
BEGIN TRY
 BEGIN TRANSACTION;
 IF OBJECT_ID(N'dbo.OcrFileStageBlob',N'U') IS NULL
 BEGIN
  SET @Sql=N'CREATE TABLE dbo.OcrFileStageBlob(
   FileId uniqueidentifier NOT NULL CONSTRAINT PK_OcrFileStageBlob PRIMARY KEY,
   EmpCode nvarchar(10) COLLATE '+@EmployeeCollation+N' NOT NULL,
   Kind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
   OriginalFileName nvarchar(512) NOT NULL, ContentType nvarchar(128) NOT NULL,
   ByteLength bigint NOT NULL, Sha256 binary(32) NOT NULL,
   CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_OcrFileStageBlob_CreatedUtc DEFAULT SYSUTCDATETIME(),
   CreatedBy nvarchar(128) NOT NULL, Content varbinary(max) NOT NULL,
   CONSTRAINT UQ_OcrFileStageBlob_Owner UNIQUE(FileId,EmpCode,Kind),
   CONSTRAINT CK_OcrFileStageBlob_Kind CHECK(Kind=N''Photo'' OR Kind=N''Certificate''),
   CONSTRAINT CK_OcrFileStageBlob_Length CHECK(ByteLength>0 AND ByteLength=DATALENGTH(Content)),
   CONSTRAINT CK_OcrFileStageBlob_Labels CHECK(LEN(OriginalFileName)>0 AND LEN(ContentType)>0 AND LEN(CreatedBy)>0 AND LEN(EmpCode)>0));';
  EXEC sys.sp_executesql @Sql;
 END;
 IF OBJECT_ID(N'dbo.OcrFileStageJournal',N'U') IS NULL
 BEGIN
  SET @Sql=N'CREATE TABLE dbo.OcrFileStageJournal(
   StageId uniqueidentifier NOT NULL CONSTRAINT PK_OcrFileStageJournal PRIMARY KEY,
   FileId uniqueidentifier NOT NULL, EmpCode nvarchar(10) COLLATE '+@EmployeeCollation+N' NOT NULL,
   Kind nvarchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
   CanonicalKey nvarchar(1024) NOT NULL, CanonicalKeyHash binary(32) NOT NULL, Sha256 binary(32) NOT NULL,
   LegacyReference nvarchar(1024) NOT NULL, SourcePath nvarchar(2048) NOT NULL, SourceModifiedUtc datetime2(7) NOT NULL,
   StagedUtc datetime2(7) NOT NULL CONSTRAINT DF_OcrFileStageJournal_StagedUtc DEFAULT SYSUTCDATETIME(),
   ManifestId uniqueidentifier NOT NULL,
   CONSTRAINT UQ_OcrFileStageJournal_Identity UNIQUE(EmpCode,Kind,CanonicalKeyHash,Sha256),
   CONSTRAINT CK_OcrFileStageJournal_Kind CHECK(Kind=N''Photo'' OR Kind=N''Certificate''),
   CONSTRAINT CK_OcrFileStageJournal_Provenance CHECK(LEN(CanonicalKey)>0 AND LEN(LegacyReference)>0 AND LEN(SourcePath)>0 AND LEN(EmpCode)>0),
   CONSTRAINT FK_OcrFileStageJournal_Blob FOREIGN KEY(FileId,EmpCode,Kind) REFERENCES dbo.OcrFileStageBlob(FileId,EmpCode,Kind));';
  EXEC sys.sp_executesql @Sql;
 END;

 /* Require the complete version-one shape on retries; no same-name substitute
    may silently become a staging destination. Hashes are calculated/verified in
    the bounded streaming client, not SQL2014 HASHBYTES over large file content. */
 DECLARE @Columns TABLE(TableName sysname,Ordinal int,ColumnName sysname,TypeId int,MaxLength int);
 INSERT @Columns VALUES
 (N'OcrFileStageBlob',1,N'FileId',36,16),(N'OcrFileStageBlob',2,N'EmpCode',231,20),
 (N'OcrFileStageBlob',3,N'Kind',231,32),(N'OcrFileStageBlob',4,N'OriginalFileName',231,1024),
 (N'OcrFileStageBlob',5,N'ContentType',231,256),(N'OcrFileStageBlob',6,N'ByteLength',127,8),
 (N'OcrFileStageBlob',7,N'Sha256',173,32),(N'OcrFileStageBlob',8,N'CreatedUtc',42,8),
 (N'OcrFileStageBlob',9,N'CreatedBy',231,256),(N'OcrFileStageBlob',10,N'Content',165,-1),
 (N'OcrFileStageJournal',1,N'StageId',36,16),(N'OcrFileStageJournal',2,N'FileId',36,16),
 (N'OcrFileStageJournal',3,N'EmpCode',231,20),(N'OcrFileStageJournal',4,N'Kind',231,32),
 (N'OcrFileStageJournal',5,N'CanonicalKey',231,2048),(N'OcrFileStageJournal',6,N'CanonicalKeyHash',173,32),
 (N'OcrFileStageJournal',7,N'Sha256',173,32),(N'OcrFileStageJournal',8,N'LegacyReference',231,2048),
 (N'OcrFileStageJournal',9,N'SourcePath',231,4096),(N'OcrFileStageJournal',10,N'SourceModifiedUtc',42,8),
 (N'OcrFileStageJournal',11,N'StagedUtc',42,8),(N'OcrFileStageJournal',12,N'ManifestId',36,16);
 IF EXISTS(SELECT 1 FROM @Columns e WHERE NOT EXISTS(
  SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U') AND c.column_id=e.Ordinal
  AND c.name=e.ColumnName AND c.system_type_id=e.TypeId AND c.user_type_id=c.system_type_id
  AND c.max_length=e.MaxLength AND c.is_nullable=0 AND c.is_computed=0 AND c.is_identity=0))
 OR EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id IN(OBJECT_ID(N'dbo.OcrFileStageBlob'),OBJECT_ID(N'dbo.OcrFileStageJournal'))
  AND NOT EXISTS(SELECT 1 FROM @Columns e WHERE OBJECT_ID(N'dbo.'+e.TableName)=c.object_id AND e.Ordinal=c.column_id))
  THROW 52107, 'Staging column contract is absent or incompatible.', 1;
 IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id IN(OBJECT_ID(N'dbo.OcrFileStageBlob'),OBJECT_ID(N'dbo.OcrFileStageJournal'))
  AND ((name=N'EmpCode' AND collation_name<>@EmployeeCollation) OR (name=N'Kind' AND collation_name<>N'Latin1_General_100_BIN2')
  OR (system_type_id=42 AND scale<>7)))
  THROW 52108, 'Staging key collation or timestamp precision differs.', 1;
 DECLARE @Checks TABLE(TableName sysname,ConstraintName sysname,Expression nvarchar(1000),Alternative nvarchar(1000));
 INSERT @Checks VALUES
 (N'OcrFileStageBlob',N'CK_OcrFileStageBlob_Kind',N'kind=n''photo''orkind=n''certificate''',N'kind=n''certificate''orkind=n''photo'''),
 (N'OcrFileStageBlob',N'CK_OcrFileStageBlob_Length',N'bytelength>0andbytelength=datalengthcontent',NULL),
 (N'OcrFileStageBlob',N'CK_OcrFileStageBlob_Labels',N'lenoriginalfilename>0andlencontenttype>0andlencreatedby>0andlenempcode>0',NULL),
 (N'OcrFileStageJournal',N'CK_OcrFileStageJournal_Provenance',N'lencanonicalkey>0andlenlegacyreference>0andlensourcepath>0andlenempcode>0',NULL),
 (N'OcrFileStageJournal',N'CK_OcrFileStageJournal_Kind',N'kind=n''photo''orkind=n''certificate''',N'kind=n''certificate''orkind=n''photo''');
 IF EXISTS(SELECT 1 FROM @Checks e WHERE NOT EXISTS(
  SELECT 1 FROM sys.check_constraints c CROSS APPLY(SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(c.definition,N'[',N''),N']',N''),N'(',N''),N')',N''),N' ',N''),NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')) AS Definition) n
  WHERE c.object_id=OBJECT_ID(N'dbo.'+e.ConstraintName,N'C') AND c.parent_object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U')
  AND c.is_disabled=0 AND c.is_not_trusted=0 AND c.is_not_for_replication=0 AND (n.Definition=e.Expression OR n.Definition=e.Alternative)
  /* Preserve literal case separately: LOWER above is safe for parser keywords,
     but Photo/Certificate are binary-exact values, not case-folded identifiers. */
  AND (e.ConstraintName NOT IN(N'CK_OcrFileStageBlob_Kind',N'CK_OcrFileStageJournal_Kind')
   OR (CHARINDEX(N'''Photo''',c.definition COLLATE Latin1_General_100_BIN2)>0
    AND CHARINDEX(N'''Certificate''',c.definition COLLATE Latin1_General_100_BIN2)>0))))
  THROW 52109, 'Staging CHECK definition is absent, disabled or incompatible.', 1;
 DECLARE @Keys TABLE(TableName sysname,IndexName sysname,Column1 sysname,Column2 sysname NULL,Column3 sysname NULL,Column4 sysname NULL,IsPrimary bit);
 INSERT @Keys VALUES
 (N'OcrFileStageBlob',N'PK_OcrFileStageBlob',N'FileId',NULL,NULL,NULL,1),
 (N'OcrFileStageBlob',N'UQ_OcrFileStageBlob_Owner',N'FileId',N'EmpCode',N'Kind',NULL,0),
 (N'OcrFileStageJournal',N'PK_OcrFileStageJournal',N'StageId',NULL,NULL,NULL,1),
 (N'OcrFileStageJournal',N'UQ_OcrFileStageJournal_Identity',N'EmpCode',N'Kind',N'CanonicalKeyHash',N'Sha256',0);
 IF EXISTS(SELECT 1 FROM @Keys e WHERE NOT EXISTS(
  SELECT 1 FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N'dbo.'+e.TableName,N'U') AND i.name=e.IndexName
  AND i.is_unique=1 AND i.is_primary_key=e.IsPrimary AND i.is_disabled=0 AND i.is_hypothetical=0 AND i.has_filter=0
  AND i.ignore_dup_key=0 AND i.type IN(1,2)
  AND (SELECT COUNT(*) FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0)=CASE WHEN e.Column4 IS NOT NULL THEN 4 WHEN e.Column3 IS NOT NULL THEN 3 WHEN e.Column2 IS NOT NULL THEN 2 ELSE 1 END
  AND NOT EXISTS(SELECT 1 FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id AND x.key_ordinal>0
   AND (x.is_descending_key=1 OR COL_NAME(x.object_id,x.column_id)<>CASE x.key_ordinal WHEN 1 THEN e.Column1 WHEN 2 THEN e.Column2 WHEN 3 THEN e.Column3 WHEN 4 THEN e.Column4 END))))
  THROW 52110, 'Staging identity/idempotency key differs.', 1;
 IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys f WHERE f.object_id=OBJECT_ID(N'dbo.FK_OcrFileStageJournal_Blob',N'F')
  AND f.parent_object_id=OBJECT_ID(N'dbo.OcrFileStageJournal') AND f.referenced_object_id=OBJECT_ID(N'dbo.OcrFileStageBlob')
  AND f.is_disabled=0 AND f.is_not_trusted=0 AND f.is_not_for_replication=0 AND f.delete_referential_action=0 AND f.update_referential_action=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns k WHERE k.constraint_object_id=f.object_id)=3
  AND NOT EXISTS(SELECT 1 FROM sys.foreign_key_columns k WHERE k.constraint_object_id=f.object_id
   AND (COL_NAME(k.parent_object_id,k.parent_column_id)<>CASE k.constraint_column_id WHEN 1 THEN N'FileId' WHEN 2 THEN N'EmpCode' WHEN 3 THEN N'Kind' END
    OR COL_NAME(k.referenced_object_id,k.referenced_column_id)<>CASE k.constraint_column_id WHEN 1 THEN N'FileId' WHEN 2 THEN N'EmpCode' WHEN 3 THEN N'Kind' END)))
  OR EXISTS(SELECT 1 FROM sys.foreign_keys WHERE (parent_object_id IN(OBJECT_ID(N'dbo.OcrFileStageBlob'),OBJECT_ID(N'dbo.OcrFileStageJournal'))
   OR referenced_object_id IN(OBJECT_ID(N'dbo.OcrFileStageBlob'),OBJECT_ID(N'dbo.OcrFileStageJournal'))) AND object_id<>OBJECT_ID(N'dbo.FK_OcrFileStageJournal_Blob',N'F'))
  THROW 52111, 'Only the exact staging-to-staging ownership FK is permitted.', 1;
 IF EXISTS(SELECT 1 FROM (VALUES(N'OcrFileStageBlob',N'CreatedUtc'),(N'OcrFileStageJournal',N'StagedUtc')) e(TableName,ColumnName)
  WHERE NOT EXISTS(SELECT 1 FROM sys.default_constraints d JOIN sys.columns c ON c.object_id=d.parent_object_id AND c.column_id=d.parent_column_id
   WHERE d.parent_object_id=OBJECT_ID(N'dbo.'+e.TableName) AND c.name=e.ColumnName
   AND LOWER(REPLACE(REPLACE(REPLACE(d.definition,N'(',N''),N')',N''),N' ',N''))=N'sysutcdatetime'))
  THROW 52112, 'Staging UTC default differs.', 1;
 IF EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id IN(OBJECT_ID(N'dbo.OcrFileStageBlob'),OBJECT_ID(N'dbo.OcrFileStageJournal')))
  THROW 52113, 'Staging tables must not have triggers.', 1;
 SELECT @AfterColumns=CONVERT(nvarchar(max),(
  SELECT c.object_id,c.column_id,c.name,c.system_type_id,c.user_type_id,c.max_length,c.precision,c.scale,
  c.is_nullable,c.is_identity,c.is_computed,c.collation_name,c.default_object_id,c.rule_object_id
  FROM sys.columns c WHERE c.object_id IN(OBJECT_ID(N'dbo.tblEmployee'),OBJECT_ID(N'dbo.tblQualified'),OBJECT_ID(N'dbo.tblQualified_Obsoleted'))
  ORDER BY c.object_id,c.column_id FOR XML RAW('column'),ROOT('legacy'),TYPE));
 IF @BeforeColumns COLLATE Latin1_General_100_BIN2<>@AfterColumns COLLATE Latin1_General_100_BIN2
  THROW 52114, 'Legacy column metadata changed during staging preparation.', 1;
 IF @DryRun=1 ROLLBACK TRANSACTION; ELSE COMMIT TRANSACTION;
 SELECT @DryRun AS DryRun,CAST(1 AS bit) AS StagingOnly,CAST(0 AS bit) AS ApplicationActivationReady,
  DB_NAME() AS DatabaseName,N'Legacy paths/columns untouched; staged bytes are not application authority.' AS Result;
END TRY
BEGIN CATCH
 IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
 THROW;
END CATCH;
