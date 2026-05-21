-- BACKUP your database before running any destructive command.
-- 1) Add unique constraint on EmpCode to prevent future duplicates.
-- Run this once. If EmpCode already has duplicates, the statement will fail.
-- Option A: create unique index
-- CREATE UNIQUE INDEX IX_tblEmployee_EmpCode ON dbo.tblEmployee(EmpCode);

-- Option B: add unique constraint
-- ALTER TABLE dbo.tblEmployee ADD CONSTRAINT UQ_tblEmployee_EmpCode UNIQUE (EmpCode);

-- 2) If duplicates already exist, run the dedupe script below.
-- This script keeps the row with the earliest JoinDate for each EmpCode (adjust ORDER BY as needed).
-- Make a backup first.

-- Example dedupe (keeps one row per EmpCode):
-- WITH CTE AS (
--   SELECT *, ROW_NUMBER() OVER (PARTITION BY EmpCode ORDER BY JoinDate ASC) AS rn
--   FROM dbo.tblEmployee
-- )
-- DELETE FROM CTE WHERE rn > 1;

-- If there is no JoinDate or you prefer to keep the lowest primary key, replace ORDER BY JoinDate ASC
-- with ORDER BY [PrimaryKeyColumn] ASC (replace [PrimaryKeyColumn] with the actual PK).

-- After deduplication, create the unique index/constraint from step 1.
