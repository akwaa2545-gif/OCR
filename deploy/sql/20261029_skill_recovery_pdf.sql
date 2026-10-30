-- Extend the existing audit operation CHECK for attachment-only recovery.
-- Employee, qualification, attachment and existing audit records are unchanged.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NULL
    THROW 51000,'Apply the Skill Recovery audit schema before the PDF extension.',1;
IF EXISTS (SELECT 1 FROM dbo.tblSkillRecoveryAction
    WHERE Operation NOT IN (N'reinstate',N'restore',N'move',N'move-tab',N'undo-edit',N'undo-add',N'remove-pdf',N'replace-pdf',N'undo'))
    THROW 51000,'The recovery audit has an unexpected action. Review it before updating the allowlist.',1;
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.tblSkillRecoveryAction') AND name=N'CK_tblSkillRecoveryAction_Operation')
    ALTER TABLE dbo.tblSkillRecoveryAction DROP CONSTRAINT CK_tblSkillRecoveryAction_Operation;
ALTER TABLE dbo.tblSkillRecoveryAction WITH CHECK ADD CONSTRAINT CK_tblSkillRecoveryAction_Operation
    CHECK (Operation IN (N'reinstate',N'restore',N'move',N'move-tab',N'undo-edit',N'undo-add',N'remove-pdf',N'replace-pdf',N'undo'));
ALTER TABLE dbo.tblSkillRecoveryAction CHECK CONSTRAINT CK_tblSkillRecoveryAction_Operation;
COMMIT TRANSACTION;
