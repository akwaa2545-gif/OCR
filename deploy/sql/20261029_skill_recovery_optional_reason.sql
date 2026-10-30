-- Make recovery explanations optional while retaining their existing size bound.
-- Employee/skill/attachment rows and existing audit events remain unchanged.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NULL
    THROW 51000,'Apply the Skill Recovery audit schema before the optional-reason extension.',1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction')
    AND name=N'Reason' AND system_type_id=231 AND max_length=1000 AND is_nullable=0)
    THROW 51000,'Recovery reason storage needs review before changing its constraint.',1;
IF EXISTS (SELECT 1 FROM dbo.tblSkillRecoveryAction WHERE Reason IS NULL OR DATALENGTH(Reason)>1000)
    THROW 51000,'Existing recovery reasons need review before changing their constraint.',1;
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'CK_tblSkillRecoveryAction_Reason')
    ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Reason;
ALTER TABLE dbo.tblSkillRecoveryAction WITH CHECK ADD CONSTRAINT CK_tblSkillRecoveryAction_Reason
    CHECK (DATALENGTH(Reason)<=1000);
ALTER TABLE dbo.tblSkillRecoveryAction CHECK CONSTRAINT CK_tblSkillRecoveryAction_Reason;
COMMIT TRANSACTION;
