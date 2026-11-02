# Dev Tools: Skill Recovery

Status: implemented and applied to the local web app on 2026-10-29. Scope
includes employee-to-employee corrections and undoing incorrect skill
additions/updates. Button labels are Reinstate, Restore, Move, Undo Edit,
Undo Add, Move Tab, Remove PDF, Replace PDF, Preview, Apply, Cancel and Undo. Employee/skill selection
uses the main page; actions and their review/confirmation open in a native popup.

## Purpose

Allow a Developer to correct a mistaken skill operation while preserving the
original qualification details, certificate and an explanation of the correction.
Add Skill Recovery as a submenu of Dev Tools, leaving room for other tools.

## Actions

| Action | Source | Result |
| --- | --- | --- |
| Undo disqualification | One exact disqualified skill | Clear its disqualification fields after recording their previous values in the audit. Keep dates, scores and certificate. |
| Restore obsolete version | One exact archived skill version | Keep the original archive and create a restored version for the same employee. |
| Correct employee assignment | One exact skill version and a selected destination employee | Retain the source history and create a destination version with the correct employee and certificate ownership. |
| Undo incorrect update | The incorrect operational version and an explicitly selected previous version | Archive the incorrect version and restore a new copy of the selected previous version, including its original certificate. |
| Undo incorrect addition | One exact incorrectly added current or obsolete skill | Remove the incorrect entry from skill records without creating an obsolete copy. Keep its snapshot and PDF through Recovery History. |
| Move Tab | One exact skill version for the same employee | Move between Current, Disqualified and Obsolete, preserving dates, results and certificate references. |
| Remove PDF | One exact current or obsolete skill with a linked certificate | Clear both attachment references; retain the original PDF for History Undo and other records. |
| Replace PDF | One exact current or obsolete skill, including one without a PDF | Publish a validated immutable SQL PDF version and attach it to this record only. |

The first version handles one selected record per operation. Selecting an entire
employee or process must never implicitly recover all of their records.

## PDF corrections

PDF actions preserve the skill row GUID, table, dates, results, status and Remark.
They also support promoted records belonging to current employees; promotion
restrictions for qualification corrections remain in place. Both actions require
Developer access and SQL attachment writes. Remove clears CertificateFileId and
Download together, preventing the selected record from falling back to its old
legacy reference. No shared file blob or physical legacy file is deleted.

Replacement uploads use existing PDF signature and size validation and private
immutable staging. A bounded singleton retains stages for at most ten minutes,
bound to the actual Developer, session and exact skill key. Preview contains only
safe file metadata and an opaque handle. A staged PDF can be viewed through the
session-scoped `/api/skill-recovery/upload/{id}/file` endpoint. A retained valid
upload survives Preview POST without asking for the file again. Expiry prompts
the user to choose it again. Multipart requests have a scoped 72 MiB ceiling.

Apply rechecks the protected descriptor, rowversion and upload ownership under an
exclusive lease. The immutable SQL file, attachment-only row update and audit
commit atomically; only then is the stage consumed. Failed writes retain the
stage until expiry. Persisted audit plans contain no session or upload capability.

History Undo restores only the old attachment references, in place in the same
table and row, without creating an obsolete qualification. Earlier PDFs use the
explicit audit-action endpoint, since a live row URL serves its new PDF after
replacement. SQL errors never downgrade to physical fallback.

An employee assignment correction fixes a record entered under the wrong
employee. It does not establish that a different employee completed training and
does not change either employee's department, workshop, grade or employment data.

## User flow

1. Search by employee ID or name. Show their photo, name and ID prominently.
2. Browse All records, Current, Disqualified and Obsolete records. All records
   also exposes expired/failed operational entries so incorrect additions do not
   become impossible to select. Each row shows process, classification,
   certified date, expiry date, result and certificate availability.
3. Select Actions on one version to open the popup, then choose an action. An unavailable action shows
   its reason, such as an ambiguous historical record or promotion archive.
4. For an employee correction, search for and explicitly select the destination.
   Show both employees' names and IDs beside each other. Move Tab instead keeps
   the employee and asks for Current, Disqualified or Obsolete.
5. Show a before/after preview, the selected certificate and any conflicting
   destination skill. Explain whether the resulting version qualifies for the
   Current Skills tab or remains expired, failed or disqualified.
6. Offer an optional recovery reason, initially copied from the selected skill's
   existing Remark. Preserve an intentionally cleared reason on preview. Keep
   the original skill Remark unchanged when editing or clearing the reason.
   Allow an empty reason, with a maximum of 500 characters.
7. Require confirmation through Apply. Keep action labels short: Reinstate,
   Restore, Move, Undo Edit, Undo Add and Move Tab.
8. Show success, profile links and a recovery reference in Recovery History.

Keep employee selection and the skill directory full-width on the main page.
Open the selected action, errors, preview and confirmation inside a labelled
native dialog. Close/Escape restores Actions focus and employee/filter context;
closing a POST-rendered popup navigates to a clean GET to prevent resubmission
on reload. Apply returns to the destination tab for Move Tab.

Move Tab to Current requires a passing, unexpired qualification and clears
disqualification fields. Disqualified records the Developer, apply time and
reason (maximum 300 characters, matching the actual legacy column). Obsolete
archives/removes only the selected operational row. Only a Current destination
may replace an explicitly approved conflicting active version; the other two
destinations preserve other active rows. The PDF stays with the same employee.
Promoted is not a destination. Existing Restore/Reinstate behavior for expired
versions remains available with its eligibility notice.

## Incorrect additions and updates

### Undo incorrect update

Show the incorrect version beside the earlier archived versions. Require the
Developer to choose the exact version to restore; do not infer the previous
version from certification date or process name alone. Display field-by-field
changes for dates, scores, K/S levels, classification, result, verifier, remarks
and certificate. This includes updates where only the uploaded PDF was wrong.

Archive the incorrect operational version and restore a new copy of the chosen
prior snapshot in the same transaction. Restore its original certificate
reference as well as its qualification fields. Keep the incorrect update and
its PDF in history, with a clear Replaced incorrect update recovery event.
Do not undo a past update over subsequent legitimate edits automatically.

If no trustworthy prior version is available, show that limitation. The tool
cannot manufacture previous values or a previous PDF. The Developer may choose
a separate explicit correction through the normal skill editor instead.

### Undo incorrect addition

Preview exactly which employee/process/version will leave its current or obsolete
table and whether any other version will remain. Delete only that exact row with
its rowversion guard, without creating an obsolete copy. Preserve the full
snapshot in the same transaction's recovery audit and retain its certificate:
an incorrect addition is a different correction from a genuine disqualification.

Do not restore an arbitrary old version automatically. If the supposed addition
actually replaced a prior qualification, offer Undo incorrect update so the
Developer can explicitly select that prior version. An incorrect obsolete entry
can also be removed from its archive table. Promotion records remain protected.

Label cancelled additions and replaced incorrect updates in Recovery History.
Retain their complete snapshots and allow guarded Undo this recovery to reinstate
the pre-correction state in its original table. Restored obsolete rows retain
their original GUID and receive a fresh rowversion. Audit scopes without an
explicit table continue to mean the current table for backward compatibility.

Deleted additions' PDFs remain accessible to Developers through
`/api/skill-recovery/history/{actionId}/file`. The endpoint verifies the original
Undo Add audit, exact employee/row/table snapshot and SQL attachment ownership.

## Conflicting versions

When the destination employee already has an active version for the same
process, compare the two versions and offer:

- Cancel, the default.
- Archive the existing destination version and use the selected version.

The second choice requires an additional explicit acknowledgement. Archive the
displaced version and restore/move the selected version in one transaction.
Never silently overwrite it or leave two operationally active versions.
Multiple ambiguous active rows require review before recovery can continue.

Conflict checks must include non-disqualified operational versions even when
expired or failed, because the existing update workflow also addresses those
rows by employee/process. Do not check only the visible Current Skills tab.

## Qualification status and special cases

- Original dates, scores, K/S levels, classification, verifier and result remain
  unchanged. Restoring a record does not renew a certificate or turn a failed
  result into a pass.
- The current query requires a Pass practice result and an unexpired expiry date,
  and excludes promoted/resigned markers. Preview the actual resulting display
  using the same rules as the application.
- Undo disqualification explicitly clears DisQualifiedBy, DisQualifiedDate and
  TheReason. Preserve their original values in the audit.
- A restored or moved version otherwise retains its original disqualification
  status. If reinstatement is also needed, show it as a separate explicit action.
- Recovery writes initially require current source/destination employees. A
  resigned employee's history remains readable; recovery does not reactivate the
  employee.
- Undoing a promotion is a separate future operation because it affects employee
  grade and potentially several skills. Ordinary recovery must identify and
  reject promotion-marked source or conflicting versions, rather than clearing
  promotion fields or hiding promotion history.
- Developer access permits the correction workflow for higher-grade/promoted
  employees where their selected skill is not itself a promotion archive. It
  does not change the employee's grade or promotion event.

## Version and certificate handling

### Same employee

Identify a selected version by source table and OcrSkillRowId, with its
FileStorageVersion for concurrency. Employee/process text is for display and
conflict grouping, not sufficient identity for a mutation.

For an obsolete restore, keep the selected archive unchanged and insert a new
current-table version with a new OcrSkillRowId. Reusing an archived identity in
the current table would collide with the existing unique archive identity when
that version is archived again. A displaced current version is archived first;
if its identity is already archived, verify the identical snapshot or use a new
archive identity recorded in the audit instead of overwriting history.

For the same employee, reuse the existing CertificateFileId. Preserve legacy
Download references for legacy-only files. Recovery does not upload a different
PDF or fall back from a SQL-owned attachment to a guessed legacy filename.
Show missing/unavailable certificates clearly; an unchanged same-employee
reference remains unchanged and is not presented as repaired by recovery.

### Different employee

CertificateFileId is bound to its employee through SQL ownership constraints.
Never attach employee A's file ID directly to employee B.

- Retain the original source skill snapshot and PDF for history.
- For a current/disqualified source version, archive that version and remove its
  operational source row. An already archived source stays unchanged.
- Create a destination-owned skill version with a new OcrSkillRowId.
- For an existing SQL PDF, create a new immutable employee-B-owned file version
  from the verified source content, preserving its hash, filename and MIME type.
  The implementation streams it into private validated staging and inserts the
  destination version within the skill/audit transaction. This supports the
  actual SQL Server 2014 database without server-side large-input hashing. Keep
  the original employee-A file for its historical references.
- For a legacy-only PDF, resolve it through the approved roots, validate and
  stage it, then publish an employee-B-owned SQL version. Never leave the
  destination dependent on a source employee folder or an unscoped alias.
- If a linked PDF cannot be verified/copied, stop the cross-employee move before
  changing either skill. A record that originally has no certificate remains
  explicitly marked as having none; a missing linked file must not be silently
  dropped.
- Archive any explicitly approved destination conflict in the same transaction.

Moving an archived snapshot to another employee does not transfer every newer
source version. The preview must name the exact version being corrected.

## Recovery history and undo

Create an append-only SQL recovery audit. Proposed name:
dbo.tblSkillRecoveryAction. Each event records:

- Unique action ID and UTC time.
- Developer employee code and optional reason.
- Action type and source/destination employee codes.
- For update reversal, both the incorrect version and selected prior version.
- Source, created and displaced version IDs, including source tables.
- Before/after qualification snapshots and certificate IDs.
- An optional ReversesActionId linking an undo to the previous action.

The audit must be committed in the same transaction as the skill/file links.
If the audit fails, the recovery fails too. Do not edit/delete previous events.

Recovery History may offer Undo this recovery for actions created by this tool.
It must preview an inverse operation, offer an optional reason and require confirmation.
Only allow reversal when the affected records, conflict set and references still
match the expected post-action state. Stop if later edits make the inverse
ambiguous. An undo creates another event; it does not erase the original action.

Undoing an employee move archives/removes the created destination version,
restores the original source operational version only when one originally
existed, and restores any displaced destination version. An obsolete-only
source stays obsolete. Reuse the retained original employee-owned PDFs for
these restored snapshots.

For older mistakes without recovery audit entries, select the actual version
and make an explicit correction. Do not claim to reconstruct unrecorded past
values or undo an unknown action automatically.

For reliable future one-click Undo addition/update, record normal skill writes
with operation type, actor, old/new version IDs and snapshots in an append-only
skill-change audit at their transaction boundary. Cover both web and desktop
write paths if the tool is expected to undo actions from either application.
An existing archived snapshot alone is not proof of the exact preceding action.

## Authorization and transaction boundary

- Developer-only hub, page handlers, preview, confirmation and history access.
  Admin-only users cannot perform recovery.
- Fresh persisted Developer membership, antiforgery and session checks; never
  trust a browser role flag, posted original values or hidden process name.
- Provide a scoped Developer-authorized certificate preview/download for the
  selected recovery record. Existing certificate endpoints require Admin
  access, so they cannot provide preview for Developer-only users. Do not
  weaken their global guards or grant Admin membership as a workaround.
- A short-lived protected preview binds the action, actor/session, exact source,
  destination, conflict choice and observed versions. Reject tampering, replay,
  expiration or a changed selection. Include its unique action ID in the audit
  uniqueness constraint so duplicate confirmation/retry cannot apply it twice.
- Re-read and lock the employee rows, exact skill versions and destination
  process conflict set before mutation. Compare row versions and employment,
  promotion and qualification state with the preview. Recheck authorization
  immediately before commit.
- Apply all skill changes, newly owned SQL files and the audit in one SQL
  transaction. On any error, leave both employees' operational state unchanged.
- Validate the archive schema and attachment ownership before issuing writes.
  Use parameterized SQL and a fixed whitelist of source tables.

## Implementation and verification order

1. Add the Developer Tools hub/submenu, recovery search and read-only preview.
2. Add the audit schema and same-employee disqualification/version restoration,
   incorrect-update reversal and incorrect-addition removal.
3. Add cross-employee correction with destination-owned certificates.
4. Add guarded reversal of the tool's own recorded recovery actions.
5. Verify with disposable fixtures: exact-version selection, disqualification,
   archive preservation, incorrect additions, PDF-only/metadata updates, missing
   prior versions, subsequent legitimate edits, conflicting/duplicate versions,
   expired/failed records,
   promotion boundaries, SQL/legacy PDFs, cross-employee ownership, stale state,
   role revocation, invalid previews, duplicate submits and full rollback.
6. Verify browser search, comparison, reason/confirmation and laptop/mobile
   layouts. Apply to the local app only after implementation checks pass.

## Implementation record

Dev Tools now opens a hub with Skill Recovery at `/DevTools/SkillRecovery`.
Developer-only PDF access uses `/api/skill-recovery/file/{employee}/{rowId}`,
with `obsolete=true` for archived versions. Existing Admin certificate routes
retain their original access rules.

The additive audit schema was applied to OperatorCertificationRecordDB. Its
SQL Server major version is 12 and compatibility remains 100. Schema setup
created no recovery events, and verification made no employee/skill changes.
Read-only live queries checked employee search, an existing 42-version employee
workspace and empty recovery history. The local publication was refreshed and
restarted using its existing launcher and role/config stores.

Validation: 466 web tests passed; 13 unrelated fixture tests were skipped.
All 95 recovery tests passed, including 11 real SQL transaction tests in a
separate marked LocalDB fixture at compatibility 100. Recovery-service coverage
was 92.86% of lines and 81.41% of branches. Six JavaScript tests and 15 combined
browser checks passed, including all actions and their guarded reversals,
Developer-only access, PDF ownership, conflict consent, stale/tampered previews,
failed-move rollback and layouts at 1280/390/320px.

The workspace shows up to 300 of the newest versions and indicates when more
exist. Immutable SQL PDF versions can be restored; unversioned legacy references
may point to overwritten content and are explicitly flagged for review.
Historical operations without an audit entry require explicit version selection.
Promotion reversal and recording every ordinary desktop/web skill write remain
separate future work. No sample recovery was applied to a live employee.

The popup/Move Tab refinement passed 514 web tests (13 unrelated fixture skips),
including 28 real SQL cases, plus 14 JavaScript tests and 23 combined browser
checks. Recovery-service coverage is now 93.18% of lines and 82.18% of branches.
The audit action CHECK upgrade `deploy/sql/20261029_skill_recovery_tabs.sql`
was applied without changing skill, employee, attachment or historical audit
rows. Local DLL/PDB and popup assets were refreshed and localhost restarted
with its existing configuration.
