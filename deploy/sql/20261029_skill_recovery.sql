-- Additive audit storage for Developer-only Skill Recovery.
-- Does not alter employee, qualification or attachment rows.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.tblSkillRecoveryAction', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblSkillRecoveryAction
    (
        ActionId uniqueidentifier NOT NULL CONSTRAINT PK_tblSkillRecoveryAction PRIMARY KEY,
        Operation nvarchar(20) NOT NULL,
        Actor nvarchar(10) NOT NULL,
        Reason nvarchar(500) NOT NULL,
        CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_tblSkillRecoveryAction_CreatedUtc DEFAULT SYSUTCDATETIME(),
        SourceEmployee nvarchar(10) NOT NULL,
        TargetEmployee nvarchar(10) NULL,
        SourceRowId uniqueidentifier NOT NULL,
        CreatedRowId uniqueidentifier NULL,
        ReversesActionId uniqueidentifier NULL,
        BeforeJson nvarchar(max) NOT NULL,
        AfterJson nvarchar(max) NOT NULL,
        PlanJson nvarchar(max) NOT NULL,
        CONSTRAINT CK_tblSkillRecoveryAction_Operation CHECK (Operation IN (N'reinstate',N'restore',N'move',N'move-tab',N'undo-edit',N'undo-add',N'remove-pdf',N'replace-pdf',N'undo')),
        CONSTRAINT CK_tblSkillRecoveryAction_Reason CHECK (DATALENGTH(Reason) <= 1000),
        CONSTRAINT FK_tblSkillRecoveryAction_Reverses FOREIGN KEY (ReversesActionId) REFERENCES dbo.tblSkillRecoveryAction(ActionId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'UX_tblSkillRecoveryAction_Reverses')
    CREATE UNIQUE INDEX UX_tblSkillRecoveryAction_Reverses ON dbo.tblSkillRecoveryAction(ReversesActionId) WHERE ReversesActionId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'IX_tblSkillRecoveryAction_Source')
    CREATE INDEX IX_tblSkillRecoveryAction_Source ON dbo.tblSkillRecoveryAction(SourceEmployee,CreatedUtc DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'IX_tblSkillRecoveryAction_Target')
    CREATE INDEX IX_tblSkillRecoveryAction_Target ON dbo.tblSkillRecoveryAction(TargetEmployee,CreatedUtc DESC);
COMMIT TRANSACTION;
