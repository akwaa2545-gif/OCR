-- Migration: Add Transfer tracking columns to tblEmployee
-- Run once against the target database before deploying the Transfer feature.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblEmployee') AND name = 'TransferBy')
    ALTER TABLE tblEmployee ADD TransferBy NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('tblEmployee') AND name = 'TransferDate')
    ALTER TABLE tblEmployee ADD TransferDate DATETIME NULL;
