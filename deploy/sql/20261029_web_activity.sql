-- Web-only activity history. No business/role/file rows are changed.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.tblWebActivityEvent',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblWebActivityEvent
    (
        EventId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblWebActivityEvent PRIMARY KEY,
        EventUid uniqueidentifier NOT NULL,
        OperationId uniqueidentifier NOT NULL,
        OccurredUtc datetime2(7) NOT NULL,
        RecordedUtc datetime2(7) NOT NULL CONSTRAINT DF_tblWebActivityEvent_Recorded DEFAULT SYSUTCDATETIME(),
        ActorCode nvarchar(20) NOT NULL,
        ActorName nvarchar(200) NULL,
        ActorIsAdmin bit NULL,
        ActorIsDeveloper bit NULL,
        Category nvarchar(30) NOT NULL,
        Action nvarchar(60) NOT NULL,
        Outcome nvarchar(20) NOT NULL,
        Summary nvarchar(1000) NOT NULL,
        RequestId nvarchar(100) NOT NULL,
        Route nvarchar(300) NOT NULL,
        OriginKind nvarchar(30) NOT NULL,
        OriginId uniqueidentifier NULL,
        OriginPart nvarchar(160) NOT NULL,
        RecoveryActionId uniqueidentifier NULL,
        CommandHash char(64) NULL,
        RequestHash char(64) NULL,
        SchemaVersion int NOT NULL CONSTRAINT DF_tblWebActivityEvent_Version DEFAULT 1,
        CONSTRAINT UQ_tblWebActivityEvent_Uid UNIQUE(EventUid),
        CONSTRAINT CK_tblWebActivityEvent_Outcome CHECK(Outcome IN (N'Committed',N'NoChange',N'Viewed',N'Prepared',N'Succeeded',N'Denied',N'Rejected',N'Failed',N'Partial',N'Unknown')),
        CONSTRAINT CK_tblWebActivityEvent_Receipt CHECK(OriginKind<>N'WebCommand' OR (Outcome IN(N'Committed',N'NoChange') AND OriginId IS NOT NULL AND RequestHash IS NOT NULL))
    );
END;
IF COL_LENGTH(N'dbo.tblWebActivityEvent',N'RequestHash') IS NULL
    ALTER TABLE dbo.tblWebActivityEvent ADD RequestHash char(64) NULL;
IF OBJECT_ID(N'dbo.tblWebActivityChange',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblWebActivityChange
    (
        EventId bigint NOT NULL,
        ItemOrdinal int NOT NULL,
        EmployeeCode nvarchar(20) NULL,
        SkillName nvarchar(255) NULL,
        EntityType nvarchar(40) NOT NULL,
        EntityKey nvarchar(300) NOT NULL,
        TableKind nvarchar(30) NULL,
        EntityLabel nvarchar(500) NOT NULL,
        BeforeExists bit NOT NULL,
        AfterExists bit NOT NULL,
        FieldsJson nvarchar(max) NOT NULL,
        CONSTRAINT PK_tblWebActivityChange PRIMARY KEY(EventId,ItemOrdinal),
        CONSTRAINT FK_tblWebActivityChange_Event FOREIGN KEY(EventId) REFERENCES dbo.tblWebActivityEvent(EventId)
    );
END;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'UX_tblWebActivityEvent_Origin')
    CREATE UNIQUE INDEX UX_tblWebActivityEvent_Origin ON dbo.tblWebActivityEvent(OriginKind,OriginId,OriginPart) WHERE OriginId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'IX_tblWebActivityEvent_Time')
    CREATE INDEX IX_tblWebActivityEvent_Time ON dbo.tblWebActivityEvent(OccurredUtc DESC,EventId DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'IX_tblWebActivityEvent_Actor')
    CREATE INDEX IX_tblWebActivityEvent_Actor ON dbo.tblWebActivityEvent(ActorCode,OccurredUtc DESC,EventId DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'IX_tblWebActivityEvent_Action')
    CREATE INDEX IX_tblWebActivityEvent_Action ON dbo.tblWebActivityEvent(Action,Outcome,OccurredUtc DESC);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'IX_tblWebActivityEvent_Operation')
    CREATE INDEX IX_tblWebActivityEvent_Operation ON dbo.tblWebActivityEvent(OperationId);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityEvent') AND name=N'IX_tblWebActivityEvent_Group')
    CREATE INDEX IX_tblWebActivityEvent_Group ON dbo.tblWebActivityEvent(OriginKind,OriginId,EventId) INCLUDE(ActorCode,RequestHash);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebActivityChange') AND name=N'IX_tblWebActivityChange_Employee')
    CREATE INDEX IX_tblWebActivityChange_Employee ON dbo.tblWebActivityChange(EmployeeCode,EventId);
COMMIT TRANSACTION;
