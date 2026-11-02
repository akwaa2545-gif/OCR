# Current employee SQL file storage

Implementation plan: [current-employee SQL storage](CURRENT-EMPLOYEE-SQL-STORAGE-PLAN-2026-10-27.md).
Inventory and sizing: [storage inspection](STORAGE-INSPECTION-PLAN-2026-10-26.md).

Current production authorization and execution status:
[27 October real migration record](PRODUCTION-SQL-MIGRATION-2026-10-27.md).
The operator selected an exclusive no-user window, no backup and no deployment.
The REFERENCES permission block was resolved. Schema version 1 and all 1,519
resolved current-employee photos have committed to the final production tables.
All 13,097 eligible certificates also committed. Essential verification passed
at 18:50:48 Bangkok time on 27 October 2026. Deferred references remain on
existing storage; see the migration record for counts and verification limits.
Old writers must remain unused until compatible applications are deployed.

## Scope and authority

Eligibility for a new import or SQL upload is `ResignDate IS NULL AND
(StatusWork IS NULL OR StatusWork <> '0')`, across all job grades. This is
independent of the Operator No Skill report. Preserve employee codes as text,
including leading zeros. Already-resigned, unknown and unresolved files stay on
their existing storage. No empty document is created for an employee without files.

An owner with a SQL FileId reads that exact SQL attachment. Employment changes do
not remove the reference. A SQL error never means "try an old disk copy". A SQL
owner cannot save its file through a legacy writer when SQL writes are disabled;
the save is blocked instead. Unrelated employee edits can preserve the attachment.
Unmigrated owners continue using existing paths. Keep all original files and sync
state during rollout and recovery verification.

Legacy and Docker are overlapping migration sources. Identical copies of one
employee/kind/path are imported once; conflicting content requires resolution.
The current-employee baseline is approximately 25 GB, not the sum of both copies.
Final capacity includes the resolved union, retained versions, SQL log, backups
and future growth. Check capacity on the database server itself.

## Release order

1. Verify recoverable SQL and filesystem backups. Inventory the supported desktop
   executables, running clients, web image and any other database writers.
2. Deploy the preparation release to **every writer**. It removes positional
   archive INSERT/SELECT operations and works before the new schema exists.
   Feature flags cannot protect an older `INSERT ... SELECT *` from new columns.
3. Apply and verify the additive schema and metadata-key backfill on the isolated
   test database first. DDL and binary imports are separate operations.
4. Deploy the SQL-aware web and desktop releases with SQL writes disabled. Verify
   old files still open, new references are understood, certificate permissions
   remain enforced and SQL failures cannot return stale files.
5. Rebuild the owner-bound manifest after stable row IDs exist. Reconcile current
   employees' conflicts before activating their references.
6. Enable SQL reads and writes in **both updated apps**. The write switch applies
   to all current employees without an employee allowlist. Keep file saves paused
   until compatible SQL writers are enabled for migrated attachments.
7. Test cross-app upload/download, replacement/archive, resignation, rollback and
   backup/restore. Resigned employees remain excluded from new SQL publications.

The implementation itself does not establish that installed production clients
have been upgraded. Record that operational evidence before enabling DDL/apply.

## Schema preparation

Use [the preparation script](sql/20261027_prepare_employee_file_storage.sql) and
[the read-only verifier](sql/20261027_verify_employee_file_storage.sql).
The preparation script defaults to rollback and an unverified release gate.

For each invocation, deliberately set these declarations in a reviewed private
copy of the script:

| Declaration | Required value |
| --- | --- |
| `@ExpectedDatabase` | Exact approved target database name |
| `@ExpectedEnvironment` | Its independently verified `OcrEnvironment` property |
| `@PreparationReleaseVerified` | `1` only after the all-writer release gate |
| `@Phase` | `Schema`, then bounded `Backfill` invocations, then `Finalize` |
| `@DryRun` | Start with `1`; `0` is an explicit apply |
| `@KeyBackfillBatchSize` | Default 500; allowed range 1 to 10,000 |

The DBA-managed database environment property is evidence about the selected
database. Do not create or change it merely to bypass a mismatch. Do not run
preparation inside another transaction. Check each invocation's result, complete
the bounded key backfill and run verification before finalization. SchemaVersion
1 is available only after the full metadata contract has been validated.

Structural stable-ID backfill can touch existing qualification metadata for
resigned employees; it does **not** copy their document bytes into SQL.

## Application configuration

SQL storage does not require Docker. Both applications connect to the existing
SQL Server. The local Docker SQL container is an isolated synthetic test fixture;
it is not a production migration target or an additional production dependency.

Defaults in both apps keep SQL storage off. Configure these nonsecret settings:

| Setting | Initial value |
| --- | --- |
| `FileStorage:ReadSqlEnabled` | `false` |
| `FileStorage:WriteSqlEnabled` | `false` |
| `FileStorage:PilotEmployeeCodes` | Legacy setting; no longer restricts writes |

Enable SQL reads and writes once for normal operation. SQL writes then cover all
current employees in both updated apps; individual employee codes are not needed.
Existing pilot lists are retained for configuration compatibility and do not
restrict this global switch. Publication transactions still reject resigned
employees, and existing application permissions and concurrency checks remain.
Web environment names are `FileStorage__ReadSqlEnabled` and
`FileStorage__WriteSqlEnabled`. Desktop `app.config` uses the colon-separated
keys shown above. The config filename must match the executable exactly:
`Operator Training Record.exe` requires `Operator Training Record.exe.config`.

The existing protected deployment `.env` validator accepts a connection entry,
not arbitrary feature settings. Do not append unsupported settings to that file.
Any managed production activation must explicitly carry the reviewed nonsecret
settings into the container configuration and preserve them on later releases.
Restart the appropriate clients/container to load the selected settings.

Before activation, provision and test least-privilege application/import accounts
separately from the DDL account. Ordinary file writers need read/insert access to
immutable file content and the required business/owner mutations, but must be
unable to update/delete stored file versions or alter the schema. Restrict alias
and journal publication to the approved import workflow. Exercise those exact
accounts in the target-engine and recovery checks; tests using a fixture
administrator do not establish these permissions. Existing embedded legacy
credentials must be replaced with protected configuration before distributing
the production release. Do not put credentials into these instructions.

Do not disable SQL reads after activating references. To pause the migration,
disable new writes/imports while keeping SQL reads available. Disabled SQL writers
block file changes for migrated owners instead of modifying obsolete disk copies.

## Inventory and import

The [migration console guide](../OperatorCertificationRecord.FileMigration/README.md)
documents exact CLI options and manifest ownership mapping. Use a persistent,
private work directory and an account with verified UNC access. The prior SSH
session could not access the shares; a signed-in user's F:/Z: mappings are not
proof that a service/import account can read them.

Supply the connection through a private process environment variable named
`OCR_FILE_MIGRATION_CONNECTION`, or select another private variable with
`--connection-env`. Never put a connection string in command arguments, manifests
or logs. Begin with `inventory`, `verify-manifest` and `import --dry-run`.

An apply invocation requires all of `--apply`, `--expected-database`,
`--clients-sql-aware`, `--employee-kind` and an explicit `--employees` allowlist.
The default budgets are 10 files, 128 MiB read/import bytes and 45 seconds per run.
The content limits are 64 MiB per PDF and 5 MiB per photo; oversized content is an
explicit review outcome. Valid historical GIF/BMP bytes and MIME types are retained.

Rerun the same reviewed manifest and scope to resume. The SQL journal establishes
which files committed, including a crash after commit but before checkpoint
output. Do not remove state or edit the manifest/checksum to force a retry.
Unreferenced historical files need the separate reviewed ownership mapping;
importing a file must not create duplicate qualification records.

## Verification and recovery

- Reconcile imported file counts, bytes and SHA-256 against the approved manifest;
  record missing, conflicted, unsupported and excluded files separately.
- Verify web-to-desktop and desktop-to-web photo/PDF writes and reads, current and
  archived certificate versions, permission denial and visible SQL failures.
- Verify a later resignation retains existing SQL access but blocks new imports.
- Restore a backup into an isolated recovery instance and test both applications.
- Record database/log/backup growth and large-file response/memory behavior.

Rollback uses the last SQL-aware application build and retains the SQL schema and
bytes. A file-only old executable is not safe once SQL-only uploads exist. Such a
rollback first requires a separately tested, version-tracked export of every
SQL-only attachment and consistent restoration of physical references. No source
deletion or copy-retirement operation is included in this release.

Unmigrated desktop photo saves use unique employee-prefixed filenames and
non-overwriting creation under the employee lock. A complete new copy can remain
unreferenced after a database failure; retain it for reconciliation. Automatically
deleting that copy could break a reference if a commit acknowledgement was lost.

## Local validation boundary

Create a reusable local fixture with
[`tests/New-SqlFileStorageFixture.ps1`](../tests/New-SqlFileStorageFixture.ps1).
Privately set `OCR_FILE_STORAGE_TEST_CONNECTION` to localhost/127.0.0.1 with a
**new** `OcrFileStorageTests_<unique>` database name and a local identity allowed
to create/alter that database. Run with `-ValidateOnly` first, then without the
switch. The creator seeds only two synthetic employees, applies/verifies the
schema and retains the fixture. It refuses every existing database and never
copies production data or deletes databases. Its case-sensitive default keeps
employee references and name expressions compatible in actual application joins.

The automated schema fixture creates only a newly named localhost
`OcrFileStorageTests_*` database and refuses an existing database. Repository,
desktop and web SQL tests likewise require a dedicated local test connection.
Use a different fresh name for `Test-SqlFileStorageSchema.ps1`: that adversarial
test owns creation/cleanup of its temporary database and cannot reuse the retained
fixture created above.
Actual fixture execution on SQL Server 2022 at compatibility level 120 is useful
evidence, but it does not establish execution on the production SQL Server 2014
engine. Confirm the target-engine check before production apply.

The cross-app runner is `tests/e2e/current-employee-sql-files.spec.cjs`. It uses
Playwright/Edge and an embedded PowerShell harness calling the compiled .NET
Framework desktop adapter. It exercises both photo/PDF directions and both web
certificate entry points. This replaces the planned separate
`Test-LegacySqlFileFlows.ps1`; it does not claim interactive WinForms UI testing.

For desktop coverage, use the tracked
`tests/OperatorCertificationRecord.Desktop.Tests/coverage.runsettings` with
`dotnet test --collect:"XPlat Code Coverage" --settings <that-path>`. It selects
x86 and includes linked production helpers from the test assembly. Report helper
source coverage separately from test source; default collection would omit those
helpers entirely. SQL tests require the protected local fixture connection in
`OCR_FILE_STORAGE_TEST_CONNECTION`; an unset variable skips those cases.

### Local results, 27 October 2026

| Area | Executed checks | Measured source line coverage |
| --- | --- | ---: |
| Shared storage | net48: 54 passed; net8: 54 passed, 1 POSIX test skipped on Windows; all 3 SQL cases passed on each framework | 92.35% |
| Desktop adapters | 22 passed on x86, including 2 SQL fixtures | 88.59% across 4 production helpers |
| Web | 152 passed, no skips, including archive and file-mutation SQL fixtures | 91.62% across 7 storage services; qualification transaction adapter 100% |
| Migration console | 26 passed, no skips, including public CLI and SQL apply/retry/rollback | 92.55% of entire module |
| Actual WinForms app | Release/x86 build succeeded; 3 existing certificate/selection/promotion regression scripts passed | Not a UI coverage claim |
| Native Linux probes | Unprivileged UID 1654: staging modes 0700/0600 before source reads; migration state 0600, trust rejection and case-sensitive containment passed | Separate native execution evidence |
| Reusable fixture creator | Fresh database/schema/collations/seeds verified; existing database refused without mutation; 3 shared SQL smoke cases passed against the new fixture | Test infrastructure |
| Cross-app browser + compiled desktop adapter | 13/13 passed, process exit 0, 176 seconds; bidirectional photo/PDF transfers, history, roles, legacy fallback, resignation and real SQL timeout/recovery | End-to-end behavior |

Coverage above means executable source lines, not branch coverage. The shared
and migration branch figures were 75.44% and 79.03%, respectively. The net8 POSIX
test was skipped on Windows; a separate actual Linux runtime probe passed.
Tests used SQL Server 2022 `16.0.4305.5` at compatibility level 120 and generated
synthetic employees/files. They did not execute on the production 2014 engine.
Local coverage, traces and probe outputs are retained in the ignored worktree
test artifacts; protected connection files are excluded from reports.

The final cross-app evidence is under
`.superpowers/sdd/CURRENT-EMPLOYEE-SQL-STORAGE-PLAN-2026-10-27/browser-e2e/run-2026-10-27T06-29-37-705Z/`:
JSON, JUnit, HTML, screenshots, trace and row-lock proof. An independent connection
confirmed SQL lock timeout 1222; the application returned the required generic
503, then served the correct BMP bytes after rollback. The helper exited 0,
browser/web processes closed and port 5091 was released. The minimal fixture
omits the optional `ViewEmpQualified_All` dashboard-cache view; these checks cover
authentication, Settings and file routes, rather than the entire dashboard.

After verification, only the labeled `ocr-sql-storage-tests` container was
stopped. The container and synthetic databases are retained for another run.
The implementation remains uncommitted in branch
`codex/current-employee-sql-storage`; the original dirty checkout is preserved.

Production apply is still pending the release gates above, including all-writer
rollout, target-engine checks, protected least-privilege accounts, capacity and
an isolated backup/restore drill. No production schema/content migration,
deployment, source retirement, commit or push was performed by these tests.
