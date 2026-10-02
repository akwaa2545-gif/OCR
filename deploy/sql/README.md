# Promotion archive migration

The promotion compatibility release has an expand-before-code requirement:

1. Run `20261002_prepare_web_promotion_archive.sql` against both the test and production databases with `@DryRun = 0`.
2. Deploy the new web image.
3. Run `20261002_archive_web_promoted_skills.sql` against production with `@DryRun = 0`.
4. Verify that `RemainingCurrentPromotedRows` is `0`, `ArchivedRows` equals `BackupRows`, and both web and legacy views show the expected records.

Both scripts are transactional and idempotent for their intended one-time use. Set `@DryRun = 1` only for validation. The archive script creates `dbo.tblQualified_WebPromotionBackup_20261002`; retain it for the agreed rollback window, restrict it like the source tables, then remove it after verification.
