-- Moves web-promoted skills out of tblQualified so the legacy application no
-- longer displays them in its Current Skill tab.
--
-- Run 20261002_prepare_web_promotion_archive.sql first. The backup table is a
-- short-lived rollback aid and should be dropped after the agreed retention
-- period once both applications have been verified.

SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @DryRun bit = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @RowsToArchive int =
    (
        SELECT COUNT(*)
        FROM dbo.tblQualified AS q WITH (UPDLOCK, HOLDLOCK)
        WHERE CHARINDEX('[PROMOTED]', ISNULL(q.Remark, '')) > 0
    );

    IF OBJECT_ID('dbo.tblQualified_WebPromotionBackup_20261002', 'U') IS NULL
    BEGIN
        SELECT q.*
        INTO dbo.tblQualified_WebPromotionBackup_20261002
        FROM dbo.tblQualified AS q
        WHERE CHARINDEX('[PROMOTED]', ISNULL(q.Remark, '')) > 0;
    END
    ELSE IF @RowsToArchive > 0
    BEGIN
        THROW 51000, 'Promotion backup table already exists while current promoted rows remain. Review the prior migration before retrying.', 1;
    END;

    IF @RowsToArchive > 0
    BEGIN
        -- This ID identifies the one-time migration batch, not an individual
        -- employee promotion event. Runtime promotions create one ID per event.
        DECLARE @PromotionId uniqueidentifier = NEWID();
        DECLARE @PromotedAt datetime2(0) = SYSUTCDATETIME();
        DECLARE @PromotedBy nvarchar(128) = N'data-migration-20261002';

        DELETE q
        OUTPUT
            DELETED.EmpCode, DELETED.ProcessName, DELETED.OperatorTraining, DELETED.TheoryTraining,
            DELETED.OJTTraining, DELETED.CertifiedDate, DELETED.FullScore, DELETED.ActualScore,
            DELETED.TestResult, DELETED.JudgmentTheory, DELETED.KnowledgeScore, DELETED.KnowledgeLevel,
            DELETED.SkillScore, DELETED.SkillLevel, DELETED.JudgmentPractice, DELETED.ExpiryDate,
            DELETED.Verifier, DELETED.VerifierDate, DELETED.DisQualifiedDate, DELETED.DisQualifiedBy,
            DELETED.TheReason, DELETED.Remark, DELETED.Download,
            @PromotionId, @PromotedAt, @PromotedBy
        INTO dbo.tblQualified_Obsoleted
            (EmpCode, ProcessName, OperatorTraining, TheoryTraining, OJTTraining, CertifiedDate,
             FullScore, ActualScore, TestResult, JudgmentTheory, KnowledgeScore, KnowledgeLevel,
             SkillScore, SkillLevel, JudgmentPractice, ExpiryDate, Verifier, VerifierDate,
             DisQualifiedDate, DisQualifiedBy, TheReason, Remark, Download,
             PromotionId, PromotedAt, PromotedBy)
        FROM dbo.tblQualified AS q
        WHERE CHARINDEX('[PROMOTED]', ISNULL(q.Remark, '')) > 0;

        IF @@ROWCOUNT <> @RowsToArchive
        BEGIN
            THROW 51001, 'The number of moved promotion rows did not match the locked source count.', 1;
        END;
    END;

    DECLARE @RemainingCurrentPromotedRows int =
    (
        SELECT COUNT(*)
        FROM dbo.tblQualified AS q
        WHERE CHARINDEX('[PROMOTED]', ISNULL(q.Remark, '')) > 0
    );
    DECLARE @TotalArchivedPromotedRows int =
    (
        SELECT COUNT(*)
        FROM dbo.tblQualified_Obsoleted
        WHERE CHARINDEX('[PROMOTED]', ISNULL(Remark, '')) > 0
           OR PromotionId IS NOT NULL
    );
    DECLARE @BackupRows int =
    (
        SELECT COUNT(*)
        FROM dbo.tblQualified_WebPromotionBackup_20261002
    );

    IF @DryRun = 1
        ROLLBACK TRANSACTION;
    ELSE
        COMMIT TRANSACTION;

    SELECT
        @DryRun AS DryRun,
        @RowsToArchive AS ArchivedRows,
        @RemainingCurrentPromotedRows AS RemainingCurrentPromotedRows,
        @TotalArchivedPromotedRows AS TotalArchivedPromotedRows,
        @BackupRows AS BackupRows;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
