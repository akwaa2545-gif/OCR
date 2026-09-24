# Production certificate synchronization

`OCR-LegacyCertificateSync` runs on `THBTCDT-CM1XKG2` under the existing signed-in Docker account. The Limited interactive task starts at logon and every minute, ignores overlapping invocations, and has a three-minute execution limit. Disconnecting RDP is fine; signing out prevents runs.

## Scope

- Two-way missing-file PDF replication: `\\svr120a\Cert$` and `C:\uploads`.
- One-way import of existing PDFs from `C:\ocr-uploads` into `C:\uploads`; photo files are excluded.
- Relative employee subfolders are retained. Only complete-looking PDFs with the expected header/end marker are accepted.
- No database writes, overwrites, deletion propagation, or test-environment copies.
- A previously observed file disappearing is deferred for review rather than restored automatically. Differing files at the same relative path remain unchanged and are reported as conflicts.

This is additive replication, not a replacement for backup. In-place edits made by an old client do not overwrite the other side automatically. New web uploads use unique PDF filenames, so replacements become new files that can replicate safely.

## Prerequisites

Deploy the certificate compatibility release before importing anything. Direct `/certs` static access must be blocked; downloads remain behind the authorized certificate controller. All web certificate writers must use `CertificatePath=/app/wwwroot/certs`. Old clients need the certificate-path-compatible desktop executable to open new web-style database paths.

The installer requires:

1. Protected trusted ACLs on `C:\ocr-deploy` and `C:\ocr-deploy\cert-sync` and no reparse traversal.
2. Successful `Test-CertificateSyncAccess.ps1` results in `C:\ocr-deploy\certificate-access.json`.
3. Recent runtime proof in `C:\ocr-deploy\cert-sync\web-protection.json`: `Revision` (40 hex), `Protected: true`, UTC `VerifiedUtc`, and full 64-hex `ProductionContainerId`.
4. The proof must match the currently running production container and its immutable image revision. It is not enough to build the code locally.

After deploying and verifying the exact production revision:

```powershell
.\Install-LegacyCertificateSync.ps1 -VerifiedWebRevision <verified-40-character-revision> -ValidateOnly
.\Install-LegacyCertificateSync.ps1 -VerifiedWebRevision <verified-40-character-revision>
```

Do not manufacture proof before checking the live container and denied direct access to an existing PDF. The installer refuses an existing task so that changes require inspection.

## Operation and progress

The core script is `C:\ocr-deploy\Sync-LegacyCertificates.ps1`. State and reports are private:

- `C:\ocr-deploy\cert-sync\state.json` and the first-observation journal: resumable two-way inventory and deletion/conflict protection.
- `C:\ocr-deploy\cert-sync\last-run.json`: latest two-way result, including copied files/bytes, conflicts, deferred files, errors, and queued directories.
- `C:\ocr-deploy\cert-sync\import\`: independent state and result for the old local-upload import.

Defaults are 2,000 examined entries, a 384 MiB combined read budget, and 45 seconds of cooperative work per invocation. The one-way import receives a portion of those budgets. File reads, hashes, and staging consume the byte budget; it is not simply the number of copied bytes. Busy/recent files and budget exhaustion are deferred. Recent employee directories are prioritized, but initial backfill is eventual, not a promise that every file appears within one minute. `Backlog` counts queued directories, not remaining files.

Task exit zero alone does not prove all files synchronized: inspect both result reports. Do not remove state to clear a conflict; that also removes deletion history. Any `NeedsLargerByteBudget` result needs an explicitly reviewed larger budget or a separate controlled import. The initial inventory's largest PDF was 61,054,694 bytes, which fits the configured main-sync budget.

Sources are opened denying concurrent writes/deletes, staged files are hash-verified, and publication uses non-overwriting same-directory moves. The source may still represent an application-paused write after its last close; atomic writers provide stronger guarantees. Native file identity, size, and modification time cache equal existing pairs; edits preserving all metadata may not be noticed without a deliberate full comparison. SMB calls can outlast cooperative deadlines; the task timeout is the final bound. Trusted share/directory permissions remain necessary because path checks cannot prevent a hostile concurrent directory replacement.

## Verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\test-LegacyCertificateSync.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\test-LegacyCertificateSyncTask.ps1
dotnet test .\tests\OperatorCertificationRecord.Web.Tests\OperatorCertificationRecord.Web.Tests.csproj
```

Monitor reports while the initial missing-file import runs. Existing certificates are never removed as cleanup. Temporary access-probe files are removed only by the probe that created them.
