-- Optional remembered sign-ins only. Does not change employee/password/role rows.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.tblWebRememberedLogin',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblWebRememberedLogin
    (
        TokenHash binary(32) NOT NULL CONSTRAINT PK_tblWebRememberedLogin PRIMARY KEY,
        EmpCode nvarchar(20) NOT NULL,
        ProtectedCredentialStamp varbinary(512) NOT NULL,
        IssuedUtc datetime2(7) NOT NULL,
        ExpiresUtc datetime2(7) NOT NULL,
        RevokedUtc datetime2(7) NULL,
        CONSTRAINT CK_tblWebRememberedLogin_Employee CHECK(LEN(EmpCode)>0),
        CONSTRAINT CK_tblWebRememberedLogin_Stamp CHECK(DATALENGTH(ProtectedCredentialStamp) BETWEEN 64 AND 512),
        CONSTRAINT CK_tblWebRememberedLogin_Lifetime CHECK(ExpiresUtc>IssuedUtc AND ExpiresUtc<=DATEADD(day,30,IssuedUtc))
    );
END;
IF COL_LENGTH(N'dbo.tblWebRememberedLogin',N'ProtectedCredentialStamp') IS NULL
    THROW 51000,N'The remembered-login table does not have the expected protected credential column.',1;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebRememberedLogin') AND name=N'IX_tblWebRememberedLogin_Employee')
    CREATE INDEX IX_tblWebRememberedLogin_Employee ON dbo.tblWebRememberedLogin(EmpCode,ExpiresUtc) INCLUDE(RevokedUtc);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.tblWebRememberedLogin') AND name=N'IX_tblWebRememberedLogin_Expiry')
    CREATE INDEX IX_tblWebRememberedLogin_Expiry ON dbo.tblWebRememberedLogin(ExpiresUtc) INCLUDE(RevokedUtc);
COMMIT TRANSACTION;
