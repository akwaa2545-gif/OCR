# Production SQL migration — 27 October 2026

## Authorized scope

The operator requested a real migration to the final SQL file tables, stated that
the database is not being used during the migration window, and explicitly
requested **no backup and no application deployment**. The earlier staging-only
approach is not selected. No source-file deletion is authorized.

Use the explicit exclusive migration window controls instead of falsely claiming
that unchanged clients have received a preparation release. Keep all old writers
unused until compatible builds are deployed and SQL access is configured.

## Verified target and capacity

- Server: `SVR120A`; database: `OperatorCertificationRecordDB`.
- SQL Server 2014 Standard `12.0.6449.1`, compatibility level 100, SIMPLE recovery.
- Current employees: 1,530; resigned: 3,916; total: 5,446 at preflight.
- Legacy column counts: employee 21, qualified 26, obsolete 26.
- Employee key: `nvarchar(10)`; Photo: `nvarchar(50)`; Download: `nvarchar(100)`;
  collation: `SQL_Latin1_General_CP1_CI_AS`.
- The operator reported D: free 328.49 GB of 400 GB on the SQL server.
- Existing application SQL credentials can access the target from the signed-in
  PC; that PC can read both original UNC shares. The SSH host can read its mirrors
  but cannot enumerate the original shares in that SSH context.
- Prefer original database-referenced UNC sources for the normal import; do not
  relabel staging manifests or guess ownership from employee-like filenames.

## Current status: resolved current-employee migration complete

Completed **27 October 2026 at 18:50:48 Bangkok time (11:50:48 UTC)**.
All eligible entries in the reviewed photo and certificate manifests committed
to the final production tables; no staging table was used.

| Content | Imported files | Stored bytes |
| --- | ---: | ---: |
| Photos | 1,519 | 136,470,367 |
| Certificates (PDF) | 13,097 | 19,348,590,084 |
| Total | 14,616 | 19,485,060,451 |

The operator supplied the REFERENCES grant. The repeated real-server dry-run
passed, then Schema, bounded Backfill and Finalize committed successfully on
`SVR120A.OperatorCertificationRecordDB`. The strict verifier passed at schema
version 1. Stable IDs were assigned to 52,864 qualification records in 76 batches;
the tables contain 15,191 qualified and 37,673 obsolete records. Their new column
counts are 23/29/29. No backup or application deployment was performed.

All **1,519 resolved current-employee photos** are imported into the final
`dbo.tblEmployeeFile` table: **136,470,367 bytes**, with 1,519 owner links, aliases
and migration journal records. Independent pilot readback verified all 10 initial
files/160,564 bytes against SHA-256 and length. Final photo metadata checks show
zero invalid/dangling links, aliases or journals and zero unlinked files. Nine
missing photo references and two ambiguous shared-path cases were left unchanged.

Protected production work root:
`%LOCALAPPDATA%\OCR-SQL-Migration\20261027-real`.
Photo manifest: `photo-20261027T075020Z.json`. It is a genuine format-1 stable-key
manifest; the import used `--exclusive-migration-window`, not a false deployment
attestation. One transient file/state access error was safely resumed through the
SQL journal without duplicate imports.

Certificate inventory completed from the original `\\svr120a\Cert$` references.
The old read-only scanner was stopped and replaced with an indexed, cached-
metadata scanner after SMB metadata round trips proved slow. The narrow speedup
passed 39 offline tests and review; fresh source/hash/reparse/publication checks
are unchanged. Old tool binaries remain untouched for the completed photo run.

After two chat-bound scans stopped without a manifest, the remaining phases
were launched in a hidden background PowerShell process at 09:02:49 UTC.
Controller PID: 21256; initial inventory worker PID: 22424. The reviewed
`production-continue.ps1` completed inventory, bounded final-table certificate
import, then metadata verification and readback of ten certificates, sequentially.
It stops on any failed phase without automatic retries. Logs are in the protected
work root: `continue-20261027T090247Z.log` and
`continue-20261027T090247Z.err.log`. The controller exited after completion;
the error log contains zero bytes. No restart is required.

Certificate manifest: `certificate-20261027T090251Z.json`; 13,097 eligible
attachments across 1,282 explicit employee codes. The importer committed all
13,097 in 1,364 bounded invocations, leaving **zero eligible current entries**.
The final SQL metadata verification confirmed 13,097 owner links, aliases and
journals; zero invalid links/aliases/journals, missing employees, missing migration
journals/aliases or unlinked files. Independent readback checked ten PDFs totaling
9,373,822 bytes: zero SHA-256 or length failures. This was sampled content
readback plus complete metadata verification, not a second full content read.

### Deferred certificate references

| Manifest outcome | References | Treatment |
| --- | ---: | --- |
| AmbiguousOwner | 23,558 | Existing path is shared by multiple business versions; no guessed attachment |
| NotCurrent | 15,559 | Excluded because the employee is not current |
| UnknownEmployee | 31 | No matching employee; left on existing storage |
| Unsupported | 3 | Failed PDF signature/size validation; not imported |
| Missing | 29 | Source reference could not be resolved; not imported |

These are manifest reference counts and may include several rows for one physical
file; they must not be presented as unique file counts or added to infer storage
size. Unowned physical files are also left untouched. Completion covers the
resolved, eligible manifest entries, not every physical current-employee PDF.

Keep old clients unused until compatible builds are deployed/configured. Source
files and existing Photo/Download paths are retained.

## Earlier permission block (resolved)

The reviewed full-schema script was executed as a **dry-run on the real target**.
It passed target/metadata guards, then SQL Server returned:

```text
229: REFERENCES permission denied on dbo.tblEmployee.
1750: Could not create constraint or index.
```

The transaction rolled back. Post-checks still show 21/26/26 legacy columns and
no new final storage tables. No file bytes or active references were imported.
No backup was created and neither application was deployed.

The actual database principal is `TETUSR`; schema REFERENCES and CONTROL are both
absent. A DBA must grant the required migration permission before retrying:

```sql
USE [OperatorCertificationRecordDB];
GRANT REFERENCES ON SCHEMA::[dbo] TO [TETUSR];
```

Do not work around the permission by removing ownership foreign keys or using a
different target. After the migration, have the DBA remove the temporary grant
if it is not part of the normal application role.

## Prepared execution controls

The full preparation script now supports default-off
`@ExclusiveMigrationWindow`, requiring exact `@ExpectedServer` and
`@ExpectedDatabase`. This path does not create an `OcrEnvironment` property or
set `@PreparationReleaseVerified=1`. Normal deployment/environment gates remain.
Local compatibility-100 schema tests and adversarial contract tests passed;
review approved the change.

The normal importer supports `--exclusive-migration-window` as an explicit
alternative to the SQL-aware-client attestation. Format 1, stable owner keys,
exact database, explicit employee allowlist, budgets, current-status rechecks,
source hashes and owner CAS remain required. Its offline regression suite passed
36/36; code review approved. The separate staging commands remain unused.

After the grant: repeat the real dry-run, apply Schema, run bounded metadata-key
Backfill, Finalize and verify. Build fresh protected manifests from original UNC
references. Import a small current-employee photo batch first and verify SQL
bytes/hash; expand only resolved current files. Report ambiguous/missing/conflict
outcomes separately and preserve all originals. Existing paths stay unchanged.
