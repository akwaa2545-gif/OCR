# Current Employee SQL File Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store current employees' photos and PDF certificates in SQL Server and update both the legacy desktop application and web application to use them safely.

**Architecture:** First deploy a schema-tolerant preparation release to every desktop/web writer, removing positional SQL and SELECT-star archive inserts. Then add immutable SQL file versions and nullable references beside physical path columns, deploy SQL-aware readers to both applications, and only then enable imports/new SQL writes. Records without SQL references continue using the existing share/mirror; bytes, owner reference and business changes commit in the same SQL transaction.

**Tech Stack:** SQL Server Standard `12.0.6449.1` (2014 compatibility), `VARBINARY(MAX)`, .NET Framework 4.8 WinForms/x86, .NET 8 Razor Pages, a new .NET Standard 2.0 shared storage library, existing-compatible `System.Data.SqlClient` 4.8.6.

**Spec:** [Storage inspection and employee-status breakdown](STORAGE-INSPECTION-PLAN-2026-10-26.md), narrowed by the 27 October request to migrate only current employees and modify both applications.

**Status, 27 October:** Local implementation is in branch `codex/current-employee-sql-storage` in the isolated managed worktree. Both application integrations, additive schema scripts and the current-only import tool are implemented. Final local verification is recorded in the [runbook](SQL-FILE-STORAGE-RUNBOOK.md). Production schema, file bytes, scheduled tasks and configuration have not been changed.

The task checklists below include production rollout and operational evidence.
Those steps remain open until actually performed; local tests do not satisfy an
all-client deployment, target SQL2014 engine or production backup/restore gate.
The cross-app Node runner includes the real compiled desktop adapter harness in
place of a separate `Test-LegacySqlFileFlows.ps1` script.

## Global Constraints

- Current means `ResignDate IS NULL AND (StatusWork IS NULL OR StatusWork <> '0')`; include every job grade. This is not the Operator No Skill/51T/52T report filter.
- The inspected population was 1,531 current and 3,915 resigned employees; refresh eligibility during execution rather than hardcoding those totals.
- Bind employee codes as `SqlDbType.NVarChar` with length 10 and preserve leading zeros; do not copy legacy integer-parameter conventions into the new file operations.
- Migrate resolved photos/PDFs belonging to current employees, including retained historical versions. Already-resigned employees and unmatched/ambiguous ownership remain on legacy storage.
- An employee without a photo/PDF needs no fabricated file or placeholder SQL document. Keep missing attachments distinguishable from failed reads.
- After a migrated employee resigns, retain their SQL files and authorized read access. Do not automatically delete, relocate or demote SQL files; reject new SQL uploads/import publication for resigned employees.
- Preserve `tblEmployee.Photo` and both qualification tables' `Download` values as physical legacy references. Never pass a synthetic `sql://...` path to `File.Exists` or `Image.FromFile`.
- SQL-reference existence controls read routing, not present employment status. A SQL error, corrupt file or dangling SQL reference must not silently return an older disk copy.
- A non-null owner FileId also establishes SQL write authority. If that client's SQL writes are disabled or its pilot scope does not allow the owner, reject the file save; never continue through a legacy disk writer. Unrelated business edits may proceed without changing the file reference.
- Use raw binary content, streamed reads/writes, SHA-256 calculated in the application, immutable versions and optimistic concurrency. Do not depend on SQL 2016+ syntax or SQL 2014 `HASHBYTES` for large files.
- Preserve valid historical JPG/JPEG/PNG/BMP/GIF bytes and correct MIME types, including GIF animation. New uploads retain deliberate normalization rules; unsupported/oversized imports are reported, never silently renamed, converted or skipped.
- Keep test/prod databases, directories and credentials isolated. Migration scripts default to dry-run; no production certificates enter test storage without a separately scoped fixture copy.
- No removal of existing files or sync state in this project. No SQL-only writes until every supported desktop client is SQL-aware and the compatibility gate is satisfied.

## Baseline and Scope

| Current-employee holdings, 27 October | Legacy share | Docker host |
| --- | ---: | ---: |
| PDFs | 20,758 / 24.233 GB | 20,755 / 24.682 GB |
| Photos | 1,510 / 132.16 MB | 1,506 / 133.28 MB |

These overlapping live inventories are not a unique-file union or a fixed migration size. Budget roughly 25 GB plus versions, database/log headroom, growth and backups, then replace that estimate with the reconciled manifest. The SQL database currently has approximately 40 MB of allocated row data; inspect capacity on the SQL server itself, not just Docker host drive C:.

The earlier 1,540 differently sized PDFs and other mismatches covered all employment statuses. Recalculate conflicts for the current-only scope; do not assume all 1,540 belong to current employees. Neither share nor mirror is automatically authoritative. Largest observed PDF: 61,054,694 bytes; the desktop is x86, so avoid whole-archive or multi-file byte-array buffering.

## File Structure and Responsibilities

| Files to create or modify | Responsibility |
| --- | --- |
| `deploy/sql/20261027_prepare_employee_file_storage.sql`, `deploy/sql/20261027_verify_employee_file_storage.sql` | Additive schema, preflight, idempotency and verification; no content backfill inside DDL. |
| `OperatorCertificationRecord.FileStorage/OperatorCertificationRecord.FileStorage.csproj` | New .NET Standard 2.0 library; leave path-only `PhotoCompatibility` focused. |
| New library: `FileContracts.cs`, `CurrentEmployeePolicy.cs`, `SqlEmployeeFileRepository.cs`, `EmployeeFileService.cs`, `LegacyFileResolver.cs` | Shared immutable DTOs, eligibility, SQL streaming, transaction-aware writes and approved fallback resolution. |
| `OperatorCertificationRecord.Web/Program.cs`, its `.csproj`, `Dockerfile`, `OperatorCertificationRecord.sln` | Register/reference/build the new library and validated feature options. |
| Web `Services/EmployeePhotoStorageService.cs`, new `Services/EmployeeCertificateStorageService.cs`, `Services/DataServices.cs` | Shared file writes, atomic employee/skill mutations, explicit archive columns and `ResolvePhotoPath` replacement. |
| Web `Controllers/PhotoController.cs`, `Controllers/CertificateController.cs`, `Pages/ViewUser.cshtml.cs` | SQL-aware photo and both certificate download entry points with existing authorization. |
| Web `Pages/AddUser.cshtml.cs`, `UpdateUser.cshtml.cs`, `Settings.cshtml.cs`, `AddSkill.cshtml.cs`, `UpdateSkill.cshtml.cs` | All photo/PDF upload entry points, including skill PDFs uploaded from UpdateUser. |
| Web `o/Employee.cs`, `o/EmployeeSkillRecord.cs`, `o/EmployeeQualifiedInput.cs`; relevant Razor pages | Carry nullable file IDs/owner concurrency tokens and generate SQL-aware links without breaking old URLs. |
| Web Login/Dashboard/Admins/Settings/AddSkill/UpdateSkill/OperatorTraining/UpdateUser/ViewUser `.cshtml.cs`, `Dashboard.cshtml`, `Admins.cshtml`, `Settings.cshtml` and `Pages/Shared/_Layout.cshtml` | Replace filename-only photo/session normalization with FileId-aware URLs, including JavaScript prefix construction. No photo embedding was found in ReportBuilderService/PdfExportService/DownloadController; discovery checks do not authorize speculative rewrites. |
| Desktop `.csproj`, new `DesktopFileService.cs`, `frmAddUser.cs`, `frmUpdateUser.cs`, `frmOperatorTraining.cs`, `frmUpdateSkill.cs`, `frmWelcome.cs` | Shared adapter, every photo/certificate read/write and avatar; preserve UI permissions. |
| New `OperatorCertificationRecord.FileMigration` console project: `Program.cs`, `InventoryCommand.cs`, `ImportCommand.cs`, `MigrationJournal.cs` | Dry-run manifests, current-only reconciliation and restartable imports; no UI or scheduled-task replacement. |
| New `tests/OperatorCertificationRecord.FileStorage.Tests`, `tests/OperatorCertificationRecord.Desktop.Tests`; existing web tests | Shared/storage SQL integration, .NET 4.8 adapter behavior and web authorization/flows. |
| `tests/e2e/current-employee-sql-files.spec.cjs`, `tests/Test-LegacySqlFileFlows.ps1`, `deploy/README.md`, `deploy/sql/README.md` | Cross-app fixture journeys and operating/rollback instructions. |

## Storage Model and Interfaces

- `dbo.tblEmployeeFile`: `FileId uniqueidentifier` primary key; `EmpCode nvarchar(10)`, kind (`Photo`/`Certificate`), original filename, MIME type, byte length, `Sha256 binary(32)`, UTC creation time, creating user, migration source identity and immutable `Content varbinary(max)`. Separate metadata projection from content reads; use the exact live employee-code collation.
- Verified schema: `tblEmployee` has primary key `EmpCode nvarchar(10)` and `Photo nvarchar(50)`; both qualification tables have `EmpCode nvarchar(10)`/`Download nvarchar(100)` and no primary key, identity or rowversion. Preserve those existing column values/types.
- Add `UNIQUE(FileId, EmpCode)` on `tblEmployeeFile` as the candidate key for composite ownership foreign keys; check kind is exactly `Photo` or `Certificate` and positive declared length equals `DATALENGTH(Content)`. Add nullable `PhotoFileId` to `tblEmployee` and nullable `CertificateFileId` to both qualification tables, each linked with its owner EmpCode. Enforce kind inside the guarded linking operation. Do not identify a historical skill solely by employee/process name.
- Add `OcrSkillRowId uniqueidentifier` to BOTH qualification tables, initially nullable with `NEWID()` for future inserts; batch-fill existing rows, verify uniqueness, then enforce non-null/unique IDs. Add `FileStorageVersion rowversion` to both; add/reuse the employee concurrency token after preflight confirms whether one exists. Structural key backfill may cover resigned records but never imports their content.
- `OcrSkillRowId` identifies a qualification version: replacement archives the old row ID + old FileId, then assigns a NEW row ID to the new current version; promotion MOVES the same row ID + FileId. If no replacement attachment was supplied, retain the previous FileId. Generated rowversion values are omitted from archive INSERTs, never copied.
- Add `dbo.tblEmployeeFileAlias` for approved legacy relative path/filename to exact employee/kind/FileId mappings. Its uniqueness is employee + kind + canonical relative key; ambiguous/reused historical paths block activation until reconciled. Do not resolve SQL files by an unscoped filename search.
- Add `dbo.tblEmployeeFileMigration` for source identity/hash, intended owner, imported FileId, checkpoint/outcome and verification time. Its idempotency key includes employee, kind, relative key and source hash; identical retry cannot create a second version.
- Shared immutable `FileOwner` contains owner-table enum, stable row key and employee code. `StoredFileMetadata` includes FileId, employee, kind, length and hash. `StoredFileLease : IDisposable` owns the open SQL reader/connection and exposes a readable `Stream`.
- Repository APIs: `Task<StoredFileLease> OpenReadAsync(Guid fileId, string employeeCode, FileKind kind, CancellationToken ct)`; `Task<StoredFileMetadata> InsertVersionAsync(SqlConnection connection, SqlTransaction transaction, FileWriteRequest request, Stream content, CancellationToken ct)`; `Task LinkOwnerAsync(SqlConnection connection, SqlTransaction transaction, FileOwner owner, Guid fileId, byte[] expectedVersion, CancellationToken ct)`.
- `EmployeeFileService` validates/prepares a bounded stream before acquiring SQL locks, then uses the caller's transaction to recheck current status, insert content, compare-and-swap the owner reference and commit business changes. A stale owner/status causes a conflict and rollback, not a last-writer-wins overwrite.
- `LegacyFileResolver` is called only when an owner has no SQL reference. Web downloads keep certificate admin enforcement and photo session requirements; file ID knowledge alone grants no access. Desktop certificate opening writes a bounded, hash-verified temporary PDF and disposes image streams before cleanup.

## Rollout Controls

- Shared configuration names: `FileStorage:ReadSqlEnabled=false`, `FileStorage:WriteSqlEnabled=false`, and empty `FileStorage:PilotEmployeeCodes`; desktop configuration maps to the same immutable options. Read/write capability and schema version are probed without selecting absent columns.
- Once any SQL reference is activated, disabling SQL reads is not a filesystem rollback. Reject an incompatible configuration visibly; recovery disables new writes/imports while retaining SQL-aware reads.
- Proposed content limits: 67,108,864 bytes per PDF (64 MiB) and 5,242,880 bytes per photo (5 MiB), configurable with explicit oversized-file review. Confirm HTTP/proxy limits and historical manifest compatibility; no truncation. Stage at most one bounded file per worker.
- Import defaults: one worker, at most 10 files / 134,217,728 bytes / 45 seconds per invocation, committing one file at a time. An explicit apply command must also supply the expected database and current employee allowlist.
- The importer runs under an identity proven to read the UNC shares and protected work directory. Host SSH previously lacked share access despite a signed-in desktop session having access; do not rely on that session's mapped drives or change authentication implicitly.

## Review Focus

1. Employee resigns or another client replaces the attachment while an import/upload is in progress: no stale publication; previously committed SQL versions remain readable.
2. Existing SELECT-star archival and positional grid columns: schema additions cannot shift columns or attach an archived record to the replacement version.
3. A legacy filename was overwritten/reused across historical records: an ambiguous physical path is not proof of the historical bytes.
4. SQL-only upload during mixed desktop versions or rollback: no supported client is stranded and disk fallback cannot hide a stale copy.
5. Large streamed requests, missing/corrupt content and forged employee/FileId combinations: bounded memory, visible errors and preserved authorization.

## Task 1: Preflight, Reconcile and Establish the Test Fixture

**Files:** New migration console `InventoryCommand.cs`, `MigrationJournal.cs`; new shared tests `CurrentEmployeePolicyTests.cs`; `deploy/sql/20261027_verify_employee_file_storage.sql`.
**Consumes/produces:** Existing employee/file metadata -> current-only manifest with employee, kind, relative key, source location, length, SHA-256, owner identity, source/reference version and explicit resolution outcome.

- [ ] Create the minimal new shared library, migration-console and shared/desktop test project scaffolds, reference them from the solution, and use `netstandard2.0`, `net8.0`, shared `net48;net8.0` tests and desktop `net48`/x86 tests respectively. Add no SQL-store implementation until its RED task; the following policy tests have concrete projects to target.
- [ ] **RED:** Add `CurrentEmployeePolicyTests`: null date + null/nonzero status is current; non-null date or status `'0'` is excluded; grades `53T`, `55T`, `M2` remain eligible; preserve leading-zero codes. Add `InventoryOwnershipTests` rejecting unknown/mismatched owner IDs and reused-path ambiguity.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.FileStorage.Tests -f net8.0 --filter "CurrentEmployeePolicyTests|InventoryOwnershipTests"`; expect missing-policy/manifest assertions to fail before implementation.
- [ ] Implement the policy and dry-run manifest. Include all resolved physical versions for current employees; select the database-referenced photo/certificate as the active attachment, retain other verified versions without inventing business references. Reconcile share/mirror conflicts by content and owner records; unresolved files remain excluded with reasons.
- [ ] Until Task 3 assigns stable qualification IDs, mark qualification attachment bindings as pending identity. A process name or a row's current result-set position is not a stable identity; refresh these bindings after schema/key backfill.
- [ ] Preflight SQL column types/collations, stable keys, existing rowversions, explicit archive insert shapes, database data/log/backups and restore capacity. Estimate the unique current-only union and retained versions, not the sum of both stores. Persist full manifests outside temporary directories under the protected migration work root.
- [ ] Build a curated test fixture covering current/resigned/unmatched employees, zero-file employees, multiple historical rows sharing process names, reused filenames, a 61,054,694-byte PDF, concurrent status change and a large photo. Keep production files outside test by default.
- [ ] **GREEN:** Re-run policy/manifest tests; assert every eligible file is resolved or explicitly deferred, group totals reconcile and dry-run performs zero database/content writes. Commit the preflight/test fixture work independently.

## Task 2: Ship the Preparation Release BEFORE Schema Changes

**Files:** Web `Services/DataServices.cs`; desktop `frmOperatorTraining.cs`, `frmUpdateSkill.cs` and every archive writer found by search; new `ArchiveSchemaCompatibilityTests.cs` in web/desktop test suites; migration/runbook preflight.
**Consumes/produces:** Task 1 SQL shape inventory -> a deployed, old-schema-compatible application baseline on every supported writer.

- [ ] **RED:** Add fixture tests executing archive/promotion/replacement before and after harmless nullable/generated-column additions. Pin that `INSERT ... SELECT *`, implicit archive column order and positional grid attachment indexes are absent from affected flows.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.Web.Tests --filter ArchiveSchemaCompatibilityTests` and the desktop counterpart on the original fixture schema; expect current SELECT-star behavior to fail the expanded-schema case.
- [ ] Replace every affected archive INSERT with explicit aligned column lists, including `DataServices.AddOrUpdateQualifiedAsync` and promotion DELETE/OUTPUT paths. This preparation build must not unconditionally query new columns; new-schema branches remain capability guarded/off.
- [ ] Audit database views, triggers and procedures for positional INSERT/SELECT assumptions. Resolve or explicitly block incompatible writers before ALTER. A feature flag alone cannot protect an old `INSERT ... SELECT *` from generated rowversion columns.
- [ ] **GREEN:** Test the preparation binaries against both original and expanded fixture schemas. Distribute them to ALL active desktop/web writers and record the version gate; only then allow production Task 3 DDL. No SQL content/reference publication occurs yet.

## Task 3: Expand Schema and Implement the Shared SQL Store

**Files:** Preparation SQL; new library project/contracts/repository/service; shared `SqlEmployeeFileRepositoryTests.cs`, `SqlEmployeeFileConcurrencyTests.cs`, `SqlEmployeeFileStreamingTests.cs`.
**Consumes/produces:** Task 1 schema preflight/fixture and Task 2 deployed preparation baseline -> additive SQL schema and the exact shared interfaces above.

- [ ] **RED:** Test employee/kind ownership constraints, immutable versions, retry idempotency, nullable fallback references, owner CAS failure, failed business mutation rolling back bytes/reference, resigned publication denial and later-resigned SQL reads.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.FileStorage.Tests -f net8.0 --filter "SqlEmployeeFile"`; expect assertions/schema absence to fail on an isolated SQL 2014-compatible fixture database.
- [ ] Implement SQL 2014-compatible, default-dry-run additive migration only after Task 2's preparation-release gate. Separate schema from batched surrogate-key/content backfills; avoid dropping/renaming legacy columns. Retain already-resigned records/bytes on their existing storage.
- [ ] Add explicit stable row IDs/concurrency fields defined above. Stream binary parameters and `SequentialAccess` reads; compute/validate SHA-256 outside SQL. Require trusted connection configuration without adding credentials to source.
- [ ] Refresh manifest attachment bindings using the new stable IDs. Establish application/import permissions separately from schema deployment; ordinary file writers must not update/delete immutable content rows or receive DDL privileges. Test the actual least-privilege credentials, without recording them in reports.
- [ ] Validate/source-stage bytes before acquiring locks. In one bounded transaction lock/recheck the employee's current status, check owner rowversion, insert immutable bytes, apply the business mutation and link the owner. Lock ordering is employee then owner; cancellation/timeouts roll back the complete write.
- [ ] **GREEN:** Run shared unit/integration tests on `net8.0` and `net48`; verify the large-file case with bounded streaming rather than a full-file response array. Re-run preparation SQL twice against the fixture and verify idempotency. Commit schema/store changes.

## Task 4: Deploy SQL-Aware Web Readers with Writes Disabled

**Files:** Web controllers, `Pages/ViewUser.cshtml.cs`, `DataServices.ResolvePhotoPath`, listed model/session/photo-normalization pages, shared layout and `Program.cs`; existing photo/certificate tests plus new `SqlFileReadRoutingTests.cs`.
**Consumes/produces:** Task 3 repository/read leases + nullable owner IDs -> authenticated SQL reads and unchanged legacy fallback for unmigrated records.

- [ ] **RED:** Test SQL-referenced reads for current and later-resigned employees; null-reference legacy reads; missing/corrupt SQL reference and SQL timeout causing visible failure without disk/placeholder fallback; cross-employee/FileId and traversal denial; case/leading-zero handling; historical BMP/GIF MIME and byte preservation; Dashboard/Admins/Settings JavaScript does not double-prefix a complete SQL photo URL.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.Web.Tests --filter "SqlFileReadRoutingTests|PhotoControllerTests|CertificateStorageCompatibilityTests|PrivateUploadAccessTests"`; expect missing SQL-reader integration assertions to fail.
- [ ] Thread IDs/owner tokens through employee/skill DTOs, Login's session `PhotoPath`, every listed page normalizer and shared layout. Resolve old photo/certificate routes through scoped aliases for migrated files; add stable FileId URLs. Retain both certificate download entry points; never synthesize `EmpCode.jpg` for a missing SQL reference target.
- [ ] Keep certificate admin restrictions, photo session checks and private/no-store responses. Dispose SQL leases only after response streaming completes; errors must omit SQL connection details. Do not weaken existing direct `/certs` and private-upload protections.
- [ ] **GREEN:** Pass the focused suite and existing web tests. Deploy this reader-compatible release in test with SQL writes/import activation disabled; legacy behavior must remain unchanged. Commit the web-reader changes.

## Task 5: Update Every Supported Desktop Reader and Archive Path

**Files:** Desktop project reference, `DesktopFileService.cs`, listed forms; `Services/DataServices.cs`; new desktop tests `SqlFileReadTests.cs`, `ArchiveFileReferenceTests.cs`; `tests/Test-LegacySqlFileFlows.ps1`.
**Consumes/produces:** Shared SQL reader + stable owner references -> legacy UI opens SQL photos/PDFs and both applications preserve archived file versions.

- [ ] **RED:** Test photo/avatar reads, current/disqualified/obsolete certificate downloads, null-ID fallback, leading-zero employees, failed SQL reads without silent disk fallback and temporary-file cleanup. Assert replacement leaves the obsolete record pinned to its old FileId.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.Desktop.Tests -f net48 --filter "SqlFileReadTests|ArchiveFileReferenceTests"`; expect missing adapter/reference assertions to fail.
- [ ] Update `frmAddUser`, `frmUpdateUser`, `frmOperatorTraining` and `frmWelcome` photo consumers plus certificate grid reads. Bind file IDs and downloads by column name rather than positional index `13`; keep legacy path resolvers for null-ID records.
- [ ] Extend all web/desktop archival paths to carry `OcrSkillRowId` and `CertificateFileId` using the version protocol above: archive old IDs, assign a new current qualification ID on replacement, move IDs on promotion, omit rowversion. Allocate a new FileId only when attachment bytes change. Preserve promotion/disqualification/resignation business semantics.
- [ ] **GREEN:** Build Release/x86 with Visual Studio MSBuild; run desktop tests and the fixture photo/PDF journey. Distribute the reader-compatible desktop build to every supported client and record versions; do not enable SQL writes/imports while file-only clients can still write.

## Task 6: Make All Photo and Certificate Writes Transactional

**Files:** Web photo/certificate services, `DataServices.cs`, AddUser/UpdateUser/Settings/AddSkill/UpdateSkill handlers; desktop adapter and AddUser/UpdateUser/OperatorTraining/UpdateSkill handlers; new `SqlFileWriteAtomicityTests.cs` in shared/web/desktop suites.
**Consumes/produces:** Shared transaction-aware store + fully deployed readers -> current-only SQL upload/replacement in either application.

- [ ] **RED:** Test each upload entry point, status change before publication, concurrent cross-app replacement, malformed/oversized files, invalid owner, cancellation and failed business commit. Add `SqlAuthoritativeOwnerRejectsLegacyWrite` and `DisabledSqlWritesDoNotWriteDisk`; verify AddUser creates employee + photo reference atomically.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.FileStorage.Tests -f net8.0 --filter SqlFileWriteAtomicityTests` and corresponding web/desktop fixture tests; expect old independent-connection/file-write behavior to fail the atomicity assertions.
- [ ] Refactor affected business operations to share the file write's connection/transaction. Replace `EmployeePhotoStorageService.StoreAndCommitAsync`'s independent-connection callback path; AddUser inserts the employee then checks eligibility/links the photo in the same transaction. Replace desktop `frmUpdateUser.btnUpdatePhoto_Click`'s `Image.ToString()` persistence. Preserve authorization/validation; no-skill current employees remain photo eligible.
- [ ] Use immutable replacement versions and owner CAS. Keep prior FileIds readable for history. Default SQL write flags off and pilot allowlists empty. Check owner authority before the feature flag: a SQL-referenced owner uses the SQL writer when enabled, otherwise its file save is explicitly blocked; SQL errors never redirect to disk. Only an unmigrated owner may retain the legacy write path.
- [ ] At activation, choose the strict gate: all supported clients must be SQL-aware, old writers must be stopped/blocked, and final manifests must be rechecked. During the earlier reader rollout, legacy storage remains authoritative. Do not claim arbitrary old binaries work with SQL-only uploads.
- [ ] **GREEN:** Pass all shared/web/desktop write tests; verify a write from either updated app is immediately readable by the other without file synchronization. Commit writer changes; keep production write flags disabled.

## Task 7: Implement Restartable Current-Only Imports and Pilot Photos

**Files:** Migration console `Program.cs`, `ImportCommand.cs`, `MigrationJournal.cs`; new `CurrentEmployeeImportTests.cs`, `ImportResumeTests.cs`, `ImportConflictTests.cs`; operating docs.
**Consumes/produces:** Resolved Task 1 manifest + deployed readers/writers -> checksum-verified SQL versions and atomically activated owner references.

- [ ] **RED:** Test dry-run zero writes, rerun/resume idempotency, file changed since manifest, stale owner pointer, resignation during staging, unresolved source conflict, unreferenced historical versions and interrupted import before/after commit.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.FileStorage.Tests -f net8.0 --filter "CurrentEmployeeImportTests|ImportResumeTests|ImportConflictTests"`; expect absent importer/journal behavior to fail.
- [ ] Implement `inventory`, `verify-manifest`, `import --dry-run` and explicitly scoped `import --apply` commands. Parameters must include expected database, protected manifest path, employee allowlist, content kind, checkpoint and per-run file/byte/time budgets; never put connection strings in command arguments or logs.
- [ ] Rebuild/finalize the owner-bound manifest AFTER stable-ID backfill and reader rollout: bind exact qualification-version IDs and current owner CAS tokens, recheck employee eligibility and unresolved conflicts, and verify the source fingerprints. Task 1's provisional inventory cannot publish references using pre-key row positions or process names.
- [ ] Re-read and hash each stable source outside SQL locks. Recheck employee eligibility and expected owner reference/version inside publication transaction. Import one bounded file transaction at a time; journal committed IDs for retries. Preserve both physical versions of unresolved conflicts; never choose whichever clock is newest automatically.
- [ ] For resolved unreferenced historical physical files, retain imported immutable versions/journal provenance without assigning them to unrelated skill rows. Missing/ambiguous database attachments remain unresolved; do not infer a historical version solely from `Download` filename.
- [ ] **GREEN:** Run fixtures twice and interrupt/resume them; assert one version per idempotency key and no resigned/unmatched imports. Before publishing pilot pointers, enable the current employee SQL-writer allowlist in the supported clients, or explicitly pause their file saves until enabled. Pilot photos, verify cross-app hashes and expand; certificates follow resolved current-only conflicts. A disabled updated client must block a SQL-authoritative file save, not mutate its old disk copy.

## Task 8: Cross-App Verification, Activation and Recovery

**Files:** E2E/desktop fixture scripts, `deploy/README.md`, `deploy/sql/README.md`, new `deploy/SQL-FILE-STORAGE-RUNBOOK.md`; test/deployment workflow references only where required.
**Consumes/produces:** Verified pilot/import journal -> operational SQL-first current-employee storage with explicit recovery evidence.

- [ ] **RED:** Add E2E cases for desktop-upload -> web-read, web-upload -> desktop-read, replacement -> obsolete-download, later resignation -> retained-read, role denial and one SQL read failure. Include both legacy fallback and SQL routes in the same fixture.
- [ ] Run `dotnet test tests/OperatorCertificationRecord.Web.Tests`, shared tests on both frameworks and desktop Release/x86 checks. Run `node tests/e2e/current-employee-sql-files.spec.cjs` and `powershell -NoProfile -File tests/Test-LegacySqlFileFlows.ps1` against the isolated fixture; failures must identify the cross-app journey.
- [ ] Reconcile imported counts/bytes/hashes and active reference counts against the approved current-only manifest. Record excluded/conflicted/missing files separately; an exit code zero is not evidence that all files migrated.
- [ ] Perform database backup/restore to an isolated recovery instance and verify both applications against restored bytes and references. Record SQL data/log/backup growth, large-file latency, concurrent-reader memory and cancellation behavior.
- [ ] Activate by photo then certificate allowlists, monitor SQL read/write errors and conflicts, and expand only verified employees. Once a file is SQL-authoritative, stale mirror scans and old import paths cannot overwrite its reference; the sync bridge remains for unmigrated/already-resigned records.
- [ ] Recovery stops new SQL writes/import publication while preserving SQL reads, or redeploys the last SQL-aware compatible build. A file-only old binary is not a safe rollback after SQL-only writes. Any rollback requiring those binaries needs a separately tested, version-tracked export of every SQL-only file and restored physical references before use.
- [ ] **GREEN:** Store release versions, manifest/journal IDs, verification totals, recovery evidence and operating ownership in the runbook. Retain source files and state; retiring copies is a later retention project.

## Completion Criteria

- [ ] Only employees current at publication receive newly migrated/uploaded SQL content; already-resigned and unmatched files remain on legacy storage.
- [ ] Both updated applications read existing and new SQL photos/PDFs, retain archived versions and preserve current permissions; later resignation does not erase access.
- [ ] SQL file bytes and owner references are atomic, concurrent writes are detected, retries are idempotent and no SQL failure silently serves stale legacy content.
- [ ] Every enabled employee/file has a reconciled manifest outcome; zero-file employees remain legitimate zero-file cases.
- [ ] Unit, SQL integration and cross-app E2E checks pass with at least 80% coverage for new shared storage/business adapter code; successful backup/restore and compatible rollback are documented.
- [ ] No unsupported legacy writer remains active before SQL-only writes; no source-file deletion or production migration occurs merely by creating this plan.

## Technical References

- [ADO.NET SqlClient streaming](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sqlclient-streaming-support): binary streaming and reader/connection lifetime used by the shared store.
- [SQL Server rowversion](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql): generated concurrency tokens, distinct from timestamps or portable archive values.
- [SQL Server binary and varbinary types](https://learn.microsoft.com/en-us/sql/t-sql/data-types/binary-and-varbinary-transact-sql): plain binary content storage instead of Base64 or filesystem-pointer substitution.
