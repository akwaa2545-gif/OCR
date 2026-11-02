/* Read-only SQL Server 2014 structural capability verification. No contents or
   legacy paths are read/changed. Permission/deployment evidence is separate. */
SET NOCOUNT ON;
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
EXEC sys.sp_executesql N'IF (SELECT COUNT(*) FROM dbo.tblEmployeeFileStorageSchema)<>1
 OR NOT EXISTS(SELECT 1 FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaId=1 AND SchemaVersion=1)
 THROW 51101, ''SQL file schema is not finalized.'', 1;
 SELECT SchemaVersion,PreparedUtc FROM dbo.tblEmployeeFileStorageSchema WHERE SchemaId=1;';
PRINT 'SQL file storage schema verified; no file contents or legacy paths changed.';
