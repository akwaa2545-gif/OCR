-- Prepares the legacy skill tables for atomic promotion archival.
-- The destination date type is widened to preserve VerifierDate time values,
-- and EmpCode indexes keep the serializable transaction narrowly scoped.

SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @DryRun bit = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS
    (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = 'dbo'
          AND TABLE_NAME = 'tblQualified_Obsoleted'
          AND COLUMN_NAME = 'VerifierDate'
          AND DATA_TYPE = 'date'
    )
    BEGIN
        ALTER TABLE dbo.tblQualified_Obsoleted
            ALTER COLUMN VerifierDate datetime NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified', 'PromotionId') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified ADD PromotionId uniqueidentifier NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified', 'PromotedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified ADD PromotedAt datetime2(0) NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified', 'PromotedBy') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified ADD PromotedBy nvarchar(128) NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified_Obsoleted', 'PromotionId') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified_Obsoleted ADD PromotionId uniqueidentifier NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified_Obsoleted', 'PromotedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified_Obsoleted ADD PromotedAt datetime2(0) NULL;
    END;

    IF COL_LENGTH('dbo.tblQualified_Obsoleted', 'PromotedBy') IS NULL
    BEGIN
        ALTER TABLE dbo.tblQualified_Obsoleted ADD PromotedBy nvarchar(128) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.tblQualified')
          AND name = 'IX_tblQualified_EmpCode'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_tblQualified_EmpCode
            ON dbo.tblQualified (EmpCode);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID('dbo.tblQualified_Obsoleted')
          AND name = 'IX_tblQualified_Obsoleted_EmpCode'
    )
    BEGIN
        CREATE NONCLUSTERED INDEX IX_tblQualified_Obsoleted_EmpCode
            ON dbo.tblQualified_Obsoleted (EmpCode);
    END;

    IF @DryRun = 1
        ROLLBACK TRANSACTION;
    ELSE
        COMMIT TRANSACTION;

    SELECT @DryRun AS DryRun;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
    END;

    THROW;
END CATCH;
