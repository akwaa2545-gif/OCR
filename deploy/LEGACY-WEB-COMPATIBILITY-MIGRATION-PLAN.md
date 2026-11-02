# Legacy Windows and web compatibility migration plan

Updated: **2 November 2026, Asia/Bangkok**.

## Goal and scope

Keep the Windows application usable while the web application develops, then transfer
each feature to the web application without splitting the employee, skill or file data.
Modify the old application where a shared data contract changes; web interface features
can develop independently. This document is a plan, not an application deployment.

Use one application database and one authority for each shared record. Do not introduce
two writable databases, bidirectional synchronization or separate copies of role membership.
Docker is an optional web hosting choice; it is not required for SQL storage.

## Evidence and status

Distinguish **source implemented**, **locally verified**, **database applied** and
**installed client verified**. A successful build or Git merge proves neither deployment
nor compatibility of an executable already running on another PC.

| Area | Current evidence | Remaining rollout check |
| --- | --- | --- |
| Business data | Windows and web connection aliases were verified against the same SQL database. | Verify each deployed client's actual instance and database. |
| SQL attachments | The dated migration record reports 1,519 photos and 13,097 PDFs imported on 27 October. Shared compatibility code exists. | Verify the installed executable, dependencies and SQL read/write settings. |
| Deferred attachments | Ambiguous, missing, invalid and out-of-scope references were not relinked. | Resolve each owner explicitly; do not assume every physical file is in SQL. |
| Reports | Windows source and a local build were aligned with the main web download rules; full export comparisons are documented. | Verify the installed build and the remaining alternate Section route separately. |
| Access roles | SQL import completed on 2 November at 08:26:51 Bangkok in `SVR120A.OperatorCertificationRecordDB`: 2 Developers and 10 Admins. Web SQL storage and Windows SQL report reader are implemented and tested. | Deploy the compatible clients; old web builds must not keep writing JSON as a separate authority. |
| Web tools | Admin Tools, Skill Recovery and web Activity Log have existing implementations. | Verify current release behavior and preserve their documented scope. |

The [production migration record](PRODUCTION-SQL-MIGRATION-2026-10-27.md) is historical
evidence, not a fresh verification of every installed client. Existing source files and
unresolved legacy attachments remain available; this plan does not authorize their deletion.

The role import preserved both schema-1/revision-1 envelopes, zero pending events, and
the existing two Developer / one Admin consumed-command receipts. Source JSON is an
import source, not a parallel authority after the updated web build starts in SQL mode.
No Docker or mapped-drive deployment is established by that database import.

## Shared feature contracts

| ID / feature | Windows compatibility work | Web dependency and acceptance |
| --- | --- | --- |
| F01 Employee | Preserve textual employee codes, existing short IDs and leading zeros; update only allowed profile fields. | New-employee validation must not invalidate existing IDs. Both apps display the same saved employee. |
| F02 Photos / PDFs | Read exact SQL file IDs for SQL-owned attachments; publish immutable versions through the shared storage service. | Same employee, file kind and skill owner must resolve the same bytes in both apps. |
| F03 Skill lifecycle | Preserve stable skill row IDs, explicit archive columns, status and certificate ownership when changing skills. | Current, Disqualified and Obsolete states must mean the same thing after either app writes. |
| F04 Access roles | Reports read Developer exclusions from shared SQL, using the existing application connection. | Web role edits atomically save membership, audit events and command receipts; Admin cannot manage a Developer target. |
| F05 Downloads | Use the agreed eligibility, date boundaries and export columns. | Preview, CSV, XLSX and Windows exports agree for equivalent inputs and the same data snapshot. |
| F06 Catalog | Refresh Workshop / Process lookup data and preserve historical skill identities. | Moving a process assigns it to the selected workshop; it does not keep both links. Section is not a new linking feature. |
| F07 Concurrent edits | Recheck expected row/state/version before saving; reject stale edits and reload. | A web save and a Windows save must not silently overwrite each other or detach a file. |
| W01 Web interface | No matching Windows redesign required. | Laptop layout, dialogs, date picker and remembered browser sign-in remain web features. |
| W02 Admin Tools | Consume compatible shared business data only. | Bulk updates and Missing Files Center can keep a web-only interface. |
| W03 Developer recovery | Correctly read resulting states and file ownership. | Recovery remains web-only and checks the exact row, state and version before applying. |
| W04 Activity Log | No desktop audit is added by this plan. | Coverage remains web actions only; Windows and direct SQL edits are not represented as fully audited. |

### Rules that must survive every release

- Employee codes are strings. Keep `0402567` unchanged and do not repad an existing
  short code such as `101`. The web's seven-digit new-employee rule applies to creation.
- Reuse the shared `netstandard2.0` file-storage contracts from both .NET Framework
  Windows and .NET 8 web adapters. Preserve `PhotoFileId`, `CertificateFileId`,
  `OcrSkillRowId` and the applicable `FileStorageVersion` concurrency value.
- SQL-owned files remain authoritative. A SQL error must not silently serve an older
  disk file. Approved legacy paths remain valid only for owners not moved to SQL.
- Publish attachment bytes, owner links and business changes in one transaction.
  Replace with a new immutable file version; do not overwrite an existing blob.
- SQL-owned attachment edits require SQL writes. Profile-only changes preserve file
  links. The current write switch is global; the historical pilot employee list is
  not an implemented per-employee rollout mechanism.
- Preserve history when resigning/deactivating an employee. Do not bypass attachment
  foreign keys by cascading deletion of employee files and qualification history.

## Implementation sequence

### Phase 1 — Establish the installed baseline

1. Inventory the executable path, build/version, matching `.exe.config`, dependencies,
   database identity and relevant settings for every supported Windows launch location.
2. Record the web build and hosting configuration separately from the source branch.
3. Maintain a feature release worksheet: feature ID, contract version, compatible
   Windows build, compatible web build, database step, verification and current owner.
4. Identify direct SQL writers and already-running old sessions before changing a
   write contract. A new login screen or hidden button cannot control those writers.

**Exit:** the team can identify which installed clients may read and write each feature.
An unverified mapped-drive executable is not marked compatible because its source changed.

### Phase 2 — Finish the Windows compatibility bridge

1. Verify F01–F03 in the actual Windows interface, including photo binding, PDF download,
   skill replacement and historical/archive views. Reuse implemented adapters first.
2. Close F07 profile concurrency gaps. The current desktop profile update filters by
   employee code alone; add an expected-version contract before allowing unrestricted
   simultaneous profile editing. Reuse an applicable existing rowversion where possible;
   inspect schema before proposing additional concurrency columns.
3. Complete F04 client rollout using the [SQL access-role procedure](SQL-ACCESS-ROLES.md).
   Verify the completed Admin and Developer import, including pending events and
   receipts; do not reimport stale JSON over SQL. Use `tblApplicationRoleStore` and `tblApplicationRoleMember` together;
   runtime SQL failure must not fall back to JSON or an empty Developer list.
4. Verify report reads on a PC without the development source folder. Download users
   do not need a Developer role; the list identifies employees excluded from exports.
5. Align desktop authorization for any business operation that remains writable there.
   Reading Developer membership for reports alone does not give desktop authorization parity.
6. Replace legacy interpolated login SQL with parameterized commands as a scoped fix.
   Coordinate any future password-format change across both supported login clients.

**Exit:** agreed read/write contracts work in both directions, stale saves are rejected,
and the role authority is reachable without a development-PC file path.

### Phase 3 — Develop web features alongside compatible releases

1. Develop W01 interface changes independently when they do not change shared data.
2. For F01–F07 business changes, make the contract change, Windows adapter and web adapter
   part of one reviewed feature branch. Add cross-app acceptance evidence before merging.
3. Expand schemas additively first. Retain columns/read shapes needed by supported old
   clients; remove them only after those clients are retired and checked.
4. Keep W02–W04 web interfaces web-only. A recovery action must still create a state the
   supported Windows reader understands. Do not advertise a universal Undo of old actions.
5. For F06, preserve existing process identities and qualification history. Because some
   old records store process names, renaming/deleting catalog entries needs an explicit
   compatibility mapping. A catalog move must not silently rewrite historical skills.

**Exit:** each shared feature has one defined contract and two tested consumers.

### Phase 4 — Transfer ownership one feature at a time

Use this state sequence independently for each feature:

```text
Windows owns writes
    → Both compatible apps may write
    → Web owns writes; Windows reads
    → Windows feature retired
```

Skip the simultaneous-write stage when enforcing safe coexistence would cost more than
making that Windows feature read-only. Preserve useful Windows reading/exporting while
users learn the replacement. Do not transfer all features merely because one is ready.

Enforce ownership at the command/service or database write boundary. Restrict or retire
unsupported direct-SQL writers; a client-side version flag alone cannot stop an old binary.
Introduce a capability check only with a defined enforcement mechanism and release policy.

**Exit for each transfer:** replacement behavior is accepted, supported clients pass the
feature's tests, operational support is ready, and incompatible writers cannot bypass the gate.

### Phase 5 — Retire the remaining Windows functions

Confirm that every required workflow has a web replacement and no supported process
still writes through the retired Windows feature. Contract schema cleanup is a separate,
reviewed change after this evidence exists. Do not rerun the completed attachment import
as a substitute for client rollout or bulk-import unrelated legacy history into web audit.

## Feature branches and merge graph

This is a **proposed integration topology**, not actual repository history. Use short-lived
feature branches in the existing repository, rather than maintaining divergent old-app and
new-app product branches. A shared feature merges after its contract and both adapters pass.
A merge integrates code; it does not deploy clients, apply schema or transfer data ownership.

```mermaid
gitGraph
    commit id: "Compatible baseline"
    branch web_ui
    commit id: "W01 laptop UI"
    checkout main
    branch compat_core
    commit id: "F01 F02 F03 shared contracts"
    commit id: "Windows and web adapters"
    commit id: "F07 cross-app checks"
    checkout main
    merge compat_core id: "Core compatibility gate"
    merge web_ui id: "Independent UI merge"
    branch shared_roles_reports
    commit id: "F04 SQL roles"
    commit id: "F05 both report adapters"
    checkout main
    merge shared_roles_reports id: "Role and export gate"
    branch shared_catalog
    commit id: "F06 catalog contract and readers"
    checkout main
    merge shared_catalog id: "Catalog gate"
    branch web_tools
    commit id: "W02 W03 W04 web tools"
    checkout main
    merge web_tools id: "Web tools acceptance"
    commit id: "Release candidate for selected features"
```

The graph shows feature dependencies, not instructions to create or merge these branches now.
Existing compatible implementations can satisfy a gate; they do not need to be rewritten.

```mermaid
flowchart LR
    F01["F01 Employee identity"] --> F03["F03 Skill lifecycle"]
    F02["F02 SQL files"] --> F03
    F04["F04 SQL roles"] --> F05["F05 Report exclusions"]
    F06["F06 Workshop / Process"] --> F03
    F03 --> PAIR["Windows + web adapter acceptance"]
    F05 --> PAIR
    F07["F07 Stale-write protection"] --> PAIR
    PAIR --> RELEASE["Compatible release candidate"]
    UI["W01 Web UI"] --> RELEASE
    F03 --> REC["W03 Web recovery"]
    F04 --> TOOLS["W02 Admin Tools / W04 web activity"]
    REC --> RELEASE
    TOOLS --> RELEASE
    RELEASE --> HANDOVER["Separate deployment and feature handover"]
```

## Acceptance and recovery gates

| Check | Required result |
| --- | --- |
| Windows save → web read, then reverse | Same employee fields, skill state and exact attachment owner/bytes. |
| Two editors save the same record | The stale operation is rejected; it cannot silently overwrite the accepted save. |
| Add / update / disqualify / archive / transfer | Correct stable identities, explicit history and certificate ownership; recovery rechecks its selected version. |
| PDF replace / remove; unavailable SQL | Replacement is immutable, removal does not resurrect a legacy reference, failure does not serve a stale disk file. |
| SQL role grant / revoke and replay | Both apps observe current membership; Admin cannot manage Developer users; retries do not duplicate changes/events. |
| Equivalent report filters | Compare full rows and columns, not a fixed historical count. Verify preview and every supported export format. |
| Actual Windows / laptop web interface | Photo binding, navigation, dates and Excel interaction work in the installed clients. Source tests alone do not prove this. |

Inventory and main Section exports allow all grades/shifts under their other current rules.
No-skill, Expiry, Disqualification and Obsoleted use non-DAY shifts and grades 51T/52T;
Resignation has its own eligibility, including a process requirement. Exclude Developers
consistently. Query dates as `>= start.Date` and `< end.Date.AddDays(1)` using typed parameters.
Verify the alternate Section preview/export route separately before claiming every route matches.

Rollback means restoring a **compatible** binary/configuration while preserving committed
data. Do not downgrade SQL-owned file links, restore stale JSON roles or roll back business
records to undo a release. If no previous compatible build exists, pause the affected writes
and fix forward while keeping compatible reads available.

Related evidence: [SQL file-storage runbook](SQL-FILE-STORAGE-RUNBOOK.md),
[download comparison](DOWNLOAD-COUNT-COMPARISON-2026-10-30.md),
[web activity scope](DEV-ACTIVITY-LOG-PLAN.md),
[Skill Recovery scope](DEV-SKILL-RECOVERY-PLAN.md).
