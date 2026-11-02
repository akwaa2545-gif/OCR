# Storage inspection summary and improvement plan

**Inspected:** 26 October 2026, approximately 16:06 Asia/Bangkok (09:06 UTC).

**Status:** Inspection complete; reconciliation and storage migration have not started. The future storage design remains undecided.

The stores do not fully match. Reconcile their contents before migration; neither location is established as authoritative.

## Current storage layout

Production Docker host: `THBTCDT-CM1XKG2` (`10.249.160.52`), container `ocrweb`.

| Purpose | Legacy mapped drive | Actual network share | Docker host directory | Container directory |
| --- | --- | --- | --- | --- |
| Certificates | `Z:` | `\\svr120a\Cert$` | `C:\uploads` | `/app/wwwroot/certs` |
| Legacy photos | `F:` | `\\svr120a\PhotoEmp$` | `C:\ocr-photos` | `/app/legacy-photos` |
| Web upload source | None | Replicated where applicable | `C:\ocr-uploads` | `/app/photo-source` and `/app/wwwroot/uploads` |

Mapped drives reference the shares; host directories hold separate copies.

## Verified findings

Sizes use decimal MB/GB. Comparison counts and the earlier host PDF size were collected while synchronization remained active.

| Content | Legacy share | Docker host |
| --- | --- | --- |
| PDFs | 30,494 files; 44.13 GB | 30,472 files; approximately 44.60 GB |
| Photos | 5,999 images; 608.43 MB | 5,993 images; 608.82 MB |

The earlier host inventory counted 30,469 PDFs, increasing to 30,472 by comparison time. Largest PDF: 61,054,694 bytes (61.05 MB). `C:\ocr-uploads` held 47 files totaling 20.66 MB; these overlap with other stores.

| Relative-path comparison | PDFs | Photos |
| --- | ---: | ---: |
| Same path and byte length | 28,932 | 5,988 |
| Same path, different byte length | **1,540** | **3** |
| Legacy files missing at the same host path | **22** | **8** |
| Files found only on host | 0 | 2 |

Enumeration reported zero errors. Matching sizes do not prove identical content; full SHA-256 verification was not performed. Host-only photos may be valid versions, not deletion candidates. Classify unrelated photo-share files before importing.

### Sync and SQL snapshots

- Both scheduled sync tasks use `Interactive` logon and depend on the account remaining signed in.
- Latest certificate sync report: three conflicts and 3,881 queued directories; task exit code zero. The separate import report also showed three conflicts. These are bounded run reports, not cumulative or necessarily unique conflict totals.
- Latest photo sync report: one conflict, 120 rejected records and task exit code one. Rejected records require classification; they do not automatically mean missing photos.
- SQL Server: Standard Edition (64-bit), version `12.0.6449.1`; database `OperatorCertificationRecordDB`.
- Allocated database data: 39,845,888 bytes (39.85 MB). Allocated log: 44,302,336 bytes (44.30 MB). Allocation is not active usage or proof of available migration capacity.

### Scope and limitations

Host directories, mounts, task reports and SQL metadata were inspected through SSH. Legacy shares were enumerated from signed-in PC `THBTCALT-L05543` because the host SSH session received SMB access denied. This does not establish a share outage.

Synchronization continued, so this is not a transactionally consistent snapshot. No source files, configuration, credentials, tasks or database records were changed. This document preserves aggregate findings from the temporary audit JSON without employee filenames or credentials.

## Employee-status breakdown: 27 October 2026

**Inspected:** approximately 10:00-10:01 Asia/Bangkok (03:00-03:01 UTC). This follow-up adds status attribution without replacing the 26 October findings above. It did not repeat the earlier path-to-path discrepancy comparison; changing totals do not establish that those discrepancies have been resolved.

The SQL employee map contained **5,446 distinct employee codes: 1,531 current and 3,915 resigned**, with no duplicate or ambiguous status assignments. An employee was classified as resigned when `ResignDate IS NOT NULL OR StatusWork = '0'`; the two resignation indicators agreed in this snapshot. All other mapped employees were classified as current.

PDF attribution used the first relative employee directory. Photo attribution used the employee filename, versioned employee filename, or first relative employee directory. Files without a matching employee-status assignment remain **Unmatched**. No ambiguous file attribution or enumeration errors were reported.

### PDFs by employee status

| Employee status | Legacy PDFs | Legacy size (GB) | Host PDFs | Host size (GB) |
| --- | ---: | ---: | ---: | ---: |
| Current | 20,758 | 24.233 | 20,755 | 24.682 |
| Resigned | 9,690 | 19.798 | 9,690 | 19.848 |
| Unmatched | 50 | 0.109 | 50 | 0.109 |
| **Total** | **30,498** | **44.141** | **30,495** | **44.640** |

### Photos by employee status

| Employee status | Legacy images | Legacy size (MB) | Host images | Host size (MB) |
| --- | ---: | ---: | ---: | ---: |
| Current | 1,510 | 132.16 | 1,506 | 133.28 |
| Resigned | 3,846 | 389.26 | 3,845 | 388.64 |
| Unmatched | 643 | 87.02 | 642 | 86.90 |
| **Total** | **5,999** | **608.43** | **5,993** | **608.82** |

Sizes are decimal: 1 GB = 1,000,000,000 bytes; 1 MB = 1,000,000 bytes. Totals use unrounded bytes and may differ from the sum of rounded rows.

`C:\ocr-uploads` separately held 25 PDFs (18.72 MB) and 22 images (1.94 MB). These are overlapping staging/version files, not additional unique archive totals.

### Why more resigned employees have fewer PDFs

Employee counts and certificate-file counts measure different things. Current employees have a higher proportion with matched PDFs and substantially more PDFs per employee who has files.

| Measure | Current employees | Resigned employees |
| --- | ---: | ---: |
| Employees in SQL | 1,531 | 3,915 |
| Employees with matched PDFs (count in each store) | 1,395 | 2,262 |
| Employees without matched PDFs | 136 | 1,653 |
| Employees with matched PDFs (% of their status group) | 91.1% | 57.8% |
| PDFs per employee with matched PDFs, approximately | 14.9 | 4.3 |

This explains the numerical result: more resigned employee records do not imply more certificate files. The historical reasons for individual employees having fewer or no matched PDFs have not been established. Absence from these physical holdings is not proof of deletion or loss. The inspected current resignation handlers in the [web service](../OperatorCertificationRecord.Web/Services/DataServices.cs) and [desktop training form](../OperatorCertificationRecord/frmOperatorTraining.cs) update resignation status/remarks without deleting or moving certificate files; that does not establish historical behavior. Resigned employees do account for more photo files and photo bytes, as shown above.

This classification counts **all matching physical files and versions**, not only files currently referenced by database records, and is not content-deduplicated. Unmatched files need investigation before any retention decision. Host metadata came from SSH and legacy metadata from the signed-in PC. The live inventories are neither a synchronized snapshot nor a content-hash verification. No storage, database records or scheduled tasks were changed during this follow-up.

Evidence: `host-status-sizes.json` and `legacy-status-sizes.json` in the inspecting PC's temporary `ocr-storage-status-20261027` report directory.

## Proposed implementation plan

### Phase 1: Establish and reconcile the baseline

- [ ] Preserve recoverable backups of both stores, database references and existing sync state.
- [ ] Generate complete manifests containing relative path, size, modification time and SHA-256, using snapshots or a coordinated quiet period for final verification.
- [ ] Classify the 22 missing PDFs, 1,540 differing PDFs, eight missing photos, three differing photos and two host-only photos.
- [ ] Resolve versions using business ownership, database references and content review. Preserve both versions where ownership is uncertain; do not select winners by timestamps alone.
- [ ] Verify repaired files from both applications. Record exclusions and unresolved conflicts explicitly.

**Completion:** Every discrepancy is reconciled or documented, with a verified baseline and recoverable originals.

### Phase 2: Improve the existing bridge

- [ ] Investigate the photo conflict and rejected records; retain the stateful scripts and their recovery history.
- [ ] Run sync under a dedicated account with verified UNC access using a service or suitable noninteractive task.
- [ ] Monitor last successful run, pending work, oldest pending item, conflicts and share availability.
- [ ] Address Docker Desktop and deployment-runner sign-in dependence separately; changing the sync account alone does not make the entire host unattended.

### Phase 3: Choose shared storage or SQL

- [ ] Size the unique archive, retained versions, annual growth, transaction logs, backups and restore time. Do not count the overlapping copies as an 88 GB archive.
- [ ] Compare continued SMB storage on an unattended Docker host with SQL binary storage. Check capacity on the SQL server itself.
- [ ] Decide whether the web app must continue serving files during share outages and define concurrent-write ownership.

### Phase 4: Pilot SQL only if selected

- [ ] Add a separate file table with `FileId`, employee/document association, filename, content type, byte length, checksum, timestamps and raw `VARBINARY(MAX)` content; avoid Base64.
- [ ] Preserve path columns. Both applications read SQL when a file reference exists; only unmigrated records without a SQL reference use the approved legacy share or web mirror. Report missing/corrupt SQL files instead of serving stale copies.
- [ ] Pilot photos, import with checksum verification, and test existing files, new uploads, replacements, authorization, backup/restore and rollback from both applications.
- [ ] Keep the compatibility bridge until all deployed clients understand SQL files. An unchanged desktop client cannot open new SQL-only files.
- [ ] Retire old copies only after complete reconciliation, client compatibility and recovery verification.

## Related operating documentation

- [Deployment and photo synchronization](README.md)
- [Certificate synchronization behavior and recovery](CERTIFICATE-SYNC.md)
