# Local Windows SQL web build

The old local Debug process runs from the original checkout and has no SQL
attachment reader. The updated web code is published locally to:

`C:\code test\OperatorCertificationRecord\OperatorCertificationRecord.Web\bin\Release-SqlStorage`

Stop the old local web process using Ctrl+C in its terminal. Run
`Start-LocalSqlStorageWeb.cmd` in the published folder and open
`http://localhost:5000`. This launcher is for Windows local use, not Docker.

The launcher enables global SQL reads/writes, uses the existing development user
secrets for the database connection, preserves the original project's App_Data
admin store via its working directory, and loads published content from the new
build. No credentials are embedded or copied into the launcher. Original project
source files and Docker deployments are unchanged.

Unmigrated PDFs use the approved original `\\svr120a\Cert$` root through
`LocalUploadSubfolder`; unmigrated photos use `\\svr120a\PhotoEmp$`. The signed-in
Windows user must retain access to those shares. SQL-owned files remain
authoritative and never fall back to a disk copy after a SQL error.

For employee `0402567`, `0402567_App TA.pdf` has an active SQL file reference.
`0402567 CAN.pdf` has no SQL reference because current/obsolete rows reuse that
physical path; it remains on the original share. Both original files were
confirmed present during diagnosis. No ambiguous historical row was relinked.

Validation: 33 focused SQL routing/certificate compatibility tests passed, zero
skips or failures, and framework-dependent Release publish completed. The
original SQL metadata/content state was inspected read-only; no production
record was changed for this fix.

## Laptop ViewUser layout

The refreshed local UI is published separately to
`OperatorCertificationRecord.Web\bin\Release-SqlStorage-Laptop` under the original
checkout. Use its `Start-LocalSqlStorageWeb.cmd` after stopping the previous local
web process. The original source checkout and Docker deployment remain unchanged.

The existing `bin\Release-SqlStorage` publication was subsequently refreshed with
the same updated web DLL/PDB, and its local server was restarted on port 5000.
This preserves the user's existing launcher path. The actual local employee
`0402567` page was reloaded and confirmed to contain `vu-employee-details`, no
`vu-left-col`, and matching full-width skills/content regions. A restart clears
the in-memory session; sign in again for authenticated photos and admin actions.

Employee information is merged into the profile header: English/Thai names,
employee code/status, department, section, workshop, shift, job grade, employment
date, and conditional resignation details. The separate left information card
is removed, and certification tables occupy the full content width. Details use
12px labels and 14px values with responsive wrapping. Table/action/form IDs,
photo handling, SQL download links and permission checks are preserved.

Verification used an isolated local preview of the actual Razor view with
synthetic records and no database connection. At a measured 1280 x 720 CSS
viewport, the skills region occupied the full 951px content width with no
page-wide horizontal overflow. At 390px, details reflowed to one column without
page-wide overflow. Selection controls and tab switching worked; resigned/reader
views retained resignation details and hid write links. Release publish and
targeted code review passed. No production data was edited by these UI checks.

## Skill table scrolling

Skill tables now scroll within a viewport-bounded area, keeping the horizontal
scrollbar reachable without scrolling through every row. Column headings remain
fixed while rows scroll. Hidden tabs defer column measurements until visible,
preventing zero-width columns; narrow K/S columns, resizing and Fit mode are
preserved across tab switches.

The updated web DLL/PDB and `wwwroot/js/viewuser-skill-scroll.js` were copied into
the existing `bin/Release-SqlStorage` publication, and localhost was restarted
with its existing launcher. Verification covered an isolated 80-row preview,
six focused JavaScript tests, code review and Release publish. The actual local
employee `0402567` Obsoleted tab showed all 36 rows within an internally scrolling
area, with its bottom scrollbar inside the viewport. Docker was not deployed.

## Update Employee return navigation

`Go Back` now links to the loaded employee's profile (Dashboard when no employee
is loaded), avoiding form/history loops. The shared layout resets outgoing page
animation and cancels pending progress callbacks on `pageshow`, including browser
history restoration. Five behavioral JavaScript tests and targeted reviews
passed; Release publish succeeded. The isolated actual-Razor browser check
confirmed Go Back opens the profile and browser Back restores a visible editor.
The existing local publication was refreshed and restarted; sign in again if
needed. No database records or Docker deployment were changed.

## Shared date picker

The shared layout now loads pinned Flatpickr 4.6.13 assets locally and initializes
date fields through `wwwroot/js/global-date-picker.js`. ReportBuilder retains its
range picker and uses the same library instead of loading another CDN version.
Built-in month/year controls replace detached menus. Original field IDs/names and
ISO values remain available to existing page handlers; display labels, validation,
required/readonly/disabled state, limits and calendar buttons are synchronized.

Manual input accepts `DD Mon YYYY`, ISO `YYYY-MM-DD`, or `DD/MM/YYYY` and rejects
invalid dates instead of rolling them into another month. The adapter owns manual
commit handling so Flatpickr's outside-click parser cannot erase invalid text or
skip change events. Reset restores the original date baseline, including after
programmatic date assignments. Dialog calendars remain inside their focus scope
and compensate for transformed ancestors. Dynamic inputs are initialized/cleaned
up, and history restoration closes stale calendars.

Verification: 11 focused date-picker tests and 25 related existing JavaScript
checks passed. Browser checks covered invalid/leap dates, one change event per
selection, month/year editing, programmatic values/reset, limits, readonly fields,
dynamic fields, Escape and positioning inside a translated dialog. Release publish
passed; the existing local publication, new script and vendor assets were refreshed
and restarted. Local page/assets returned HTTP 200. No Docker deployment or data
changes were performed.

### Year dropdown and report defaults

The year is now a native dropdown beside the month, including ReportBuilder's
range calendar. Changing the browsed year does not change a saved date until a
day is selected. Blank date fields open in the current year.

Report date filters initialize to 1 January of the current local year through
today, with **This year** active. **All time** remains an explicit choice.
Reopening a report preserves manually selected dates. Shared presets are scoped
to their own modal. Pages supplying their own Operator No-skill modal suppress
the layout's duplicate copy, preventing the visible presets from targeting hidden
fields. The range helper loads in the document head before inline partial scripts.

Validation: 28 focused JavaScript tests passed. Actual Razor Download Reports
preview showed one no-skill modal, current-year dates, both month/year dropdowns,
and manual All time persisting after reopening. Browsing another year on Update
Employee left the saved date unchanged. Release publish and targeted reviews
passed; localhost was restarted with matching DLL/static assets and returned HTTP
200. Sign in again after the restart if needed.

### Thai labels and Sunday-first calendar

Calendar months/weekdays use the locally bundled Thai locale. The grid starts
with Sunday and ends with Saturday, while years remain Gregorian (for example,
2026). Thai abbreviated/full month input is accepted alongside English and ISO
dates; canonical form values stay `YYYY-MM-DD`. ReportBuilder uses the same Thai
locale and Sunday-first setting.

Validation: 15 date-picker and four report tests passed. The browser showed
`อา จ อ พ พฤ ศ ส`, October 2026, and the 28th under Wednesday. Release publish
passed and localhost was refreshed with the Thai locale asset and updated code.

### Sidebar default

Every page starts with the sidebar collapsed, including history restoration.
The toggle can open it for the current page; its expanded state is no longer
carried through local storage to other pages. Initial server markup is collapsed
to avoid briefly displaying the full panel during loading.
Browser verification covered manual opening, navigation from ViewUser to Update
Employee, and browser Back: both destination and restored page were collapsed.
Publish passed and localhost returned the updated collapsed markup with HTTP 200.

## Developer and Admin tools/access

Employee `101` now has Developer access in the local app and retains existing
Admin access. The existing nine-admin store was preserved unchanged.

| Access flag | Tool page | Change access roles | Edit employee profile |
| --- | --- | --- | --- |
| Admin only | Admin Tools | Admin access for non-Developer targets through Admin Management | Yes |
| Developer only | Dev Tools | Admin and Developer | Read/search only |
| Admin and Developer | Both | Admin and Developer | Yes |

Both tool pages intentionally have empty content areas. Direct page access is
checked server-side. Only Developers see the two separate access selectors on
**Update Employee**, beside the displayed Job Grade. Each selector has an explicit
Save button and changes only its corresponding access flag. Job Grade is not an
application role. Admin Management is available to Admins and Developers. Admins
can grant/remove Admin access for non-Developer employees; Developer targets are
protected. Only Developers can assign Developer access or manage Developer targets.

Admin membership is persisted in the original web working directory's
`App_Data/admins.json`; Developer membership uses `App_Data/developers.json`.
Developer `101` was explicitly provisioned once in that local store. Missing or
corrupt files deny access and are never automatically seeded from configuration.
These runtime files are excluded from published content; preserve the existing
working directory/role data when launching the app. No Docker deployment or SQL
employee data changes were performed.

Role POSTs use antiforgery validation, exact role/value allowlists and an existing
employee lookup. Fresh role authorization and Developer-target protection are held through role persistence;
writes are atomic and removal of the final Admin/Developer is blocked. A
Developer without Admin access can read/search Update Employee, but its profile
fields are disabled/inert and forged profile-update POSTs return 403.

Validation: 82 focused authorization tests passed, with 94.3% line/87.3% branch
coverage across the changed authorization services/handlers/filters. Actual HTTP
tests confirmed missing-token POST 400, Admin role POST 403, Developer-only
profile mutation 403, and ordinary-user profile access 403. Browser tests granted
both flags independently in isolated stores while retaining Job Grade `52T`, and
confirmed both tool pages were blank. Release publish/reviews passed; localhost
was restarted. Anonymous `/DevTools` redirects to Login; sign in again as `101`
after the restart to use the new controls.

### Update Employee UI polish

Application access now uses separate Admin and Developer cards with persisted
Granted/Not granted status, capability descriptions, visible native dropdown
arrows and individual Save buttons. A compact Job Grade tile distinguishes the
employee's grade from application access. Form labels and values are larger;
cards stack on narrow screens and retain visible keyboard focus.

Validation: browser checks at 1280px and 390px found no horizontal overflow.
An isolated Developer role save persisted while Job Grade stayed `52T`; an
Admin-only actor saw no role controls. Accessibility/code reviews and Release
publish passed. Localhost was updated; existing runtime role stores were preserved.

### Admin and Developer directory

Admin Management now lists the distinct union of Admins and Developers, with
independent badges, separate role totals and a Manage access link to Update
Employee. Developer-only employees are included; employees holding both roles
appear once; Developer takes precedence as their displayed badge. Both underlying
access flags and the independent role totals are preserved. Remove Admin only appears for current Admins and
still changes Admin membership only. Admins can open the directory and grant or
remove Admin access for non-Developer employees. Developer rows are marked
Protected account for Admin-only viewers, with no manage/remove controls.

The Developer membership listing returns a fresh, sorted, detached snapshot and
fails closed for missing, corrupt or unreadable role data. Missing employee
display information retains the employee code and existing role flags.
Validation: six new directory tests and existing role-service regressions passed
(40 cases total); C# and UI accessibility/code reviews passed.
Browser checks confirmed all three role combinations, correct separate counts
and Manage access navigation. Layouts at 1280px and 390px
retain page width, with narrow-screen table scrolling contained inside the card.
Release publish passed and localhost was updated without changing role data.

### Admin delegation with protected Developer targets

Admin Management admits current Admins or Developers and is visible in either
role's sidebar. Admin-only viewers can add/remove Admin access for non-Developer
employees. Developer rows display Protected account and expose no access-management
or removal controls to Admin-only viewers. Developer assignment and the Update
Employee access selectors remain Developer-only.

The final Admin mutation holds a required, readable Developer snapshot before
taking the Admin lock. It rechecks current actor membership and rejects every
Developer target for Admin-only actors, including unchanged grants or removals.
Missing, invalid or unreadable Developer stores deny writes. The Developer-only
AccessRole handler retains Developer authorization through its final Admin write.

Validation: 67 focused tests passed for delegation, protected targets, fresh
revocations, forged session flags, role-store failures and roles changing during
employee lookup. Security, C# and code reviews passed.
Actual HTTP and browser checks confirmed Admin page entry and real Add/Remove
forms for a non-Developer employee. Crafted Admin changes for both Developer-only
and dual-role targets returned 403, as did Developer-role assignment by an Admin.
Ordinary readers were denied; Developers retained management controls. Release
publish passed and localhost was updated while preserving runtime role stores.

### Developer skill overrides and report exclusions

Signed-in Developers can add/update skills and disqualify selected skills for
promoted employees, including higher-grade employees such as `54T`. Add and
Update Skill admit Admins or Developers; promotion overrides use fresh persisted
Developer membership. Required fields, valid certificates, date checks,
resigned/current-employee storage rules and concurrency checks still apply.
Admin-only users retain the existing promotion restrictions.

Download Reports exclude employees assigned Developer access, including dual-role
employees, regardless of whether they have skills. The exclusion covers No Skill,
count/preview endpoints, expiry, resigned/disqualified/obsoleted reports, section,
inventory and skill-matrix data and their supported export formats. XML SQL
parameters filter employee codes before aggregation or TOP limits, preserving
leading zeros. Developer verifiers remain identified on ordinary employees' rows.
Missing/corrupt/unreadable Developer membership stops report generation.

Validation: 67 focused tests passed. A read-only SQL check of synthetic skilled
and No Skill rows verified XML exclusion and leading-zero identity. Security,
C# and JavaScript reviews passed; Release publish passed.
Browser checks verified promoted `54T` skill forms and selected-row actions for
a Developer-only actor, promotion locks for an Admin-only actor, and retained
resignation restrictions. Localhost was updated without changing role assignments.

### Skill Inventory modal download fix

Removed an unmatched JavaScript closure from the shared Dashboard Skill Inventory
modal. The syntax error prevented all preview/download handlers from registering.
Three regression tests reproduce the original failure and now pass. Browser
checks completed All Departments preview/Excel download and section preview/CSV
download with no page script errors. The actual local backend returned count
HTTP 200 and a valid Excel attachment. Release publish passed and localhost was
updated with the corrected modal.

### Admin Tools: Workshops and processes

Admin Tools opens a general Overview hub with internal tool navigation.
Workshops & Processes is a submenu at `/AdminTools?tool=workshops`, leaving room
for future tools without tying the hub to the catalog. The hub does not query
catalog data. Catalog saves and validation errors remain in the selected tool;
unknown tools return 404 after authorization.
The hub/submenu update passed 28 page tests and browser navigation/save checks
at 1280/390/320px. Catalog forms and JavaScript load only in the submenu; saves
retain its query route. Release publish and local hub stylesheet update passed.

Admin Tools now has two simple forms: enter a new Workshop name; select a
destination Workshop and create or move a Process. Section linking was removed
at the user's request. Moving requires explicit confirmation and removes the
old workshop association while preserving the process name, ID and training
history. An expandable Workshop → Process overview shows every workshop and
includes processes awaiting assignment.

The initially added, empty `dbo.tblWorkshopSection` mapping was removed after
checking its rows and dependencies in one locked transaction; its migration was
also removed. Existing `dbo.tblSection` records remain unchanged.
The feature now uses only the existing workshop and process master tables.

Mutations use current Admin authority, antiforgery, parameterized SQL and
serializable transactions. Nullable/nonunique legacy Process IDs are handled
using exact source identity and an affected-row check; stale or ambiguous moves
fail without changes. New IDs are allocated under table locks.

Validation: 41 unit/page tests passed; five SQL-write integration tests require a
disposable `OCR_CatalogTest_*` database and were skipped because the available
login cannot create a test database. A read-only check against the actual catalog
verified exact-source SQL matching. Browser fixtures completed creation
and moves with responsive checks at 1280/390/320px, missing-token HTTP 400 and
non-Admin denial. Security, C#, JavaScript and accessibility reviews passed.
Release publish and local runtime/assets update completed. No Docker deployment
or live catalog sample entries were made.

### Workshop directory refresh

Workshops & Processes now places a searchable workshop directory beside the
process editor on laptops. Search includes workshop names, process names and
process IDs, opens matching workshops, and restores the previous open/closed
state when cleared. Unassigned processes have their own group without increasing
the workshop total. Expand/collapse controls apply to visible groups.

Contextual Add and Move buttons select the appropriate editor fields. Changing
the process, destination or editor mode clears move confirmation. Add Workshop
is a separate expandable form. The Admin Tools overview and submenu remain
available through compact tool navigation. Sections are not linked.

Validation: 45 catalog service/page tests and 19 JavaScript regression tests
passed. Synthetic browser checks covered creation, moves, search, confirmation,
and layouts at 1366/1280/390/320px without horizontal page overflow. No live
catalog entries were created by these checks.
Release publish passed, and the existing local publication was refreshed with
the new web assembly and three Admin Tools assets. Localhost was restarted;
Login and the updated assets returned HTTP 200. Sign in again after the local
restart. The directory was subsequently enlarged to 65% of viewport height
(360–760px on desktop), with tighter header, footer and workshop-row spacing so
more workshops fit in its scroll area. Mobile retains its 430px limit. This
stylesheet-only follow-up does not require restarting the application.

### Bulk employee updates and Missing Files Center

The Admin Tools overview and shared navigation include two additional pages:
`/AdminTools/BulkEmployees` and `/AdminTools/MissingFiles`. Both require current
Admin membership. The overview remains independent of catalog/database reads.

Bulk Employee Updates searches current employees in pages of 50. Select employees
on a page or paste their IDs, with at most 100 employees per batch. Workshop,
Shift and Job Grade each default to keeping the current value. Preview shows the
before/after values; applying requires explicit confirmation. The protected
preview expires after ten minutes and is bound to the actor and session.

Apply rechecks authorization, Developer target protection, lookup choices,
employment status, promotion state and employee values under a serializable
transaction. Any stale or ineligible row stops the entire batch. Promoted
employees retain their existing grade lock. Only the requested employee columns
are updated; the service records the actor and before/after fields in structured
logs. No schema changes are required.

Missing Files Center checks up to 25 attachment records per page for current
employees and their current skill records, including disqualified skills.
Search accepts employee IDs, English/Thai names and process names. File-type
filters apply before pagination; status filters apply to the checked page.
All counts are page-scoped. SQL checks validate scoped ownership and stored
content length without downloading the attachment. SQL-linked records never
fall back to legacy copies. Approved legacy roots are checked separately.

Missing, Not linked and Unavailable are distinct states; a disabled reader,
unreachable share, denied access or inconsistent file length is not reported as
a missing file. Open file uses existing attachment routes. Replace photo/PDF
opens the existing employee or skill form, preserving its validation and save
behavior. Duplicate or disqualified skill records open the profile for review.
The center itself performs no writes and does not scan obsolete skill history.

Validation: 81 focused C# tests and 16 JavaScript regression tests passed. Four
bulk SQL write integration tests require a marked disposable localhost fixture
and were skipped. Read-only checks of the actual database verified bulk search,
lookup and selected-row queries, plus photo/certificate metadata queries. No
live employee updates or attachment replacements were performed during checks.
Thirteen synthetic browser scenarios verified preview/apply, stale/tampered
tokens, confirmation, protected accounts, grade locks, file states and paging.
Nine page/viewport checks covered the hub and both pages at 1280/390/320px with
no page overflow or JavaScript errors. A positioned table scroll container keeps
screen-reader action labels from causing mobile overflow.

Release publish passed. The existing local publication received the new web
DLL/PDB and five Admin Tools styles/scripts, and localhost was restarted with
its existing SQL-storage launcher. Login/assets return HTTP 200; unauthenticated
requests to both new tools redirect to AccessDenied. Runtime role stores and
connection settings were preserved. Sign in again after the restart.

### Developer Skill Recovery

Dev Tools opens a general hub with Skill Recovery at `/DevTools/SkillRecovery`.
Its short actions are Reinstate, Restore, Move, Undo Edit and Undo Add. Each
requires a reason, a protected before/after preview and explicit Apply
confirmation. History offers Undo while affected records remain unchanged.
Admin-only users cannot access the tool or its scoped PDF endpoint; persisted
Developer membership is checked again before commit.

Exact skill versions and qualification details are preserved. Replacements and
incorrect additions are archived; obsolete originals remain available. Conflicts
require explicit Replace consent. Moves create destination-owned SQL PDFs from
validated staging, preserving the source history. Preview/apply compares file
hashes and row versions, and audit events commit atomically with the changes.
Recovery does not renew dates or turn failed qualifications into passes.

The actual SQL Server is 2014, compatibility 100. Recovery parses history JSON
in C# and validates/stages PDFs in the application, so no upgrade or compatibility
change was needed. `deploy/sql/20261029_skill_recovery.sql` added only the audit
table/indexes; its event count was zero after installation. Read-only live
queries verified search, a 42-version workspace and history.

Validation: 466 web tests passed (13 unrelated fixture tests skipped); all 95
recovery cases passed, including 11 real SQL tests in an isolated marked LocalDB
fixture at compatibility 100. Recovery-service line/branch coverage was
92.86%/81.41%. Six JavaScript tests and 15 combined browser checks passed for
all actions/Undo, role enforcement, PDFs, stale/tampered previews, rollback and
1280/390/320px layouts. Unversioned legacy PDFs carry an explicit review notice.

Release publish passed. The existing local web DLL/PDB and four supporting
styles/scripts were refreshed, and localhost restarted with its existing SQL
launcher. Login/assets return HTTP 200; unauthenticated Dev Tools requests
redirect to Login. Runtime role stores and database settings were preserved.
No recovery was applied to a live employee during verification. Sign in again.

### Recovery popup and same-employee tab moves

Skill Recovery now uses a full-width employee/skill directory. Actions opens a
native popup containing the correction, errors, preview and Apply confirmation.
Close/Escape returns focus and employee/filter context. Closing a POST-rendered
popup uses a clean GET, avoiding form resubmission on reload.

Move Tab keeps the employee and PDF references while moving the selected version
to Current, Disqualified or Obsolete. Current requires a passing, unexpired
qualification; Disqualified records the actor/date and a reason of up to 300
characters, checked against the actual legacy column capacity. Obsolete archives
only the selected row. Other active skills remain unchanged unless a Current
destination conflict is explicitly approved for replacement. All transitions
retain the protected preview, row-version guards, atomic audit and guarded Undo.
Successful Apply opens the destination tab.

The audit CHECK allowlist upgrade `20261029_skill_recovery_tabs.sql` was applied
transactionally on SQL Server 2014, compatibility 100. Audit events remained zero
after setup, and read-only live queries passed against the 42-version profile.
No sample tab move or recovery was applied to a live employee.

Validation: 514 web tests passed, including 28 real SQL cases; 13 unrelated
fixture tests were skipped. Fourteen JavaScript tests and 23 combined browser
checks passed for the transition/Undo matrix, original actions, conflicts,
certificate preservation, Current eligibility, focus/Close/Escape, POST-close
reload and 1280/390/320px layouts. Recovery-service coverage is 93.18% of lines
and 82.18% of branches. Release publish passed; the existing local publication
received the new DLL/PDB and two popup assets and was restarted with the same
launcher, connection and role stores. Sign in again after the restart.

### Optional recovery reason

Reason is optional for corrections and Undo. Opening a selected skill copies its
existing Remark into the Reason box; editing or clearing this box preserves the
original Remark. Preview retains an intentionally cleared value. The limit stays
500 characters, or 300 for Move Tab to Disqualified, where the note also becomes
the separate disqualification reason (`TheReason`).

Applied `20261029_skill_recovery_optional_reason.sql` to the same SQL Server 2014
database, changing only the audit Reason CHECK. Existing audit events, skills and
attachments were preserved. Validation passed: 169 recovery tests, including 37
real SQL integration cases with no skips, and 14 JavaScript tests. Release publish
passed. Local DLL/PDB and the reason help script were refreshed, the existing
launcher restarted, and the route/script HTTP checks passed. Sign in again.

The browser check caught MVC's implicit required rule on the nonnullable Reason
property. Reason is now nullable at binding and normalized to an empty string
before preview/audit. A real MVC metadata regression and the page/service tests
passed (114 cases). Nine isolated browser checks passed through blank Preview,
Apply and Undo with the original Remark preserved; no live skill was changed.

### Undo Add removal and status colors

Skill Recovery tabs now use navy (All), green (Current), red (Disqualified) and
amber (Obsolete), with matching counts, explicit labels and active underlines.
Cards use their status color, including purple for Promoted. Text contrast is at
least 5.74:1; the existing focus indicator is preserved.

Undo Add removes the exact selected Current or Obsolete row without creating an
obsolete copy. It preserves PDF storage and complete audit snapshots. Guarded
History Undo restores the original table; obsolete restoration retains the old
row GUID with a fresh rowversion. Old audit scopes without a table still mean
Current. Removed-entry PDFs use a Developer-only audit-action endpoint with
strict snapshot and attachment ownership checks.

Validation passed: 196 recovery tests including the real SQL fixture, a separate
cross-connection maintenance-lock test, 54 page tests after the final copy change,
and 14 JavaScript tests. Browser checks covered colors, exact removal from both
tables, PDF access while removed, Undo restoration, unrelated-row preservation,
and 390px layout. Release publish and local route/assets/auth smoke checks passed.

Corrected only the two inspected, unchanged Undo Add archive artifacts for
employee `101`, process `Aging of FPS A`, through normal protected Preview/Apply.
The existing original audits remain intact. New correction audit references:
`14cdee36-0e25-46ec-93ee-7c0c0054546a` and
`71604d56-2d28-497d-b16f-60042f10dc13`. Every unrelated skill row was checked
unchanged after each correction. No PDFs or role files were modified.

The local web publication received the new DLL/PDB and Skill Recovery CSS/JS,
then restarted through its existing launcher and configuration. Sign in again.

### Remove PDF and Replace PDF

The Actions popup now supports Remove PDF and Replace PDF for exact Current or
Obsolete rows. Promoted records allow these attachment-only corrections while
qualification actions remain restricted. All skill fields, Remark, status, table
and row GUID stay unchanged. Remove clears both CertificateFileId and Download;
Replace links a new immutable SQL PDF version. Prior PDFs remain available in
Recovery History for attachment-only Undo in the same row, without obsolete
qualification copies or shared-file deletion.

Replacement uses validated private staging for up to ten minutes, bound to the
actual Developer, session and source row. The user can open the staged PDF before
Apply and retain it through Preview without selecting it again. Apply publishes
the SQL file, link and audit atomically, then consumes the stage. Expired uploads
return to the selected record with a prompt to choose the file again. The stage
store has per-session/global limits, byte bounds and safe lease/expiry cleanup.

Applied `20261029_skill_recovery_pdf.sql` to extend only the audit operation CHECK
on SQL Server 2014, compatibility 100. Existing audit events were preserved; no
employee PDF was removed or replaced during verification. Local publication
DLL/PDB and popup CSS/JS were refreshed through the existing launcher and config.
Route/assets return HTTP 200 and private PDF endpoints deny anonymous requests.

Validation: 239 recovery tests passed with no skips, including actual SQL
attachment update/rollback/Undo cases and private staging ownership/capacity/race
checks. Recovery-service coverage is 91.11% of lines and 80.39% of branches.
Seventeen JavaScript tests and fourteen isolated browser checks passed, covering
multipart uploads, staged bytes, no publication before Apply, session isolation,
Current/Obsolete removal/replacement, in-place Undo, old-PDF access, Promoted
permissions, validation, and 390px layout. Release publish passed. Sign in again.

### Web Activity Log

Added **Dev Tools → Activity Log**, available only to current Developers. The
table shows Who, Action, Employee ID, Skill / item, Date & time, and Result, with
filters and read-only before/after details. Changes, page/report activity, and
unsuccessful attempts have separate views. Bangkok dates default to this year
through today. Existing Recovery History links retain the original source
employee. Admins produce history without gaining access to this viewer.

Employee/profile, skill, transfer, promotion/resignation, file, catalog, bulk,
role and recovery changes are recorded at their actual write boundaries.
SQL changes and their audit commit together; retries use signed actor/session/form
commands and durable receipts. Multi-item operations retain honest partial
results. Role files use atomic membership/outbox publication, bounded durable
receipts and idempotent SQL delivery, with delivery health visible in the log.
Existing role memberships and configuration were preserved. Audited file writes
require SQL mode, already enabled by the existing local launcher.

Applied `20261029_web_activity.sql` to `OperatorCertificationRecordDB` on SQL
Server 2014, compatibility 100. Setup changed only audit tables/indexes. Imported
seven original recovery actions into seven references/nine affected-item records;
a repeated import added zero. Existing recovery audits remain intact. Ordinary
past edits cannot be reconstructed; other web history starts with this install.
Windows app edits, direct SQL and external file/role edits remain outside scope.

Validation passed: full web suite 717 tests, 13 unrelated fixture skips; focused
feature suite 155 tests; final command/recovery-link checks 27 tests; JavaScript
six tests. SQL-backed isolated browser checks covered real add/update diffs,
catalog and role delivery, retry protection, filters, permissions, dialog focus
and 1280/390px layouts. Browser skill/report forms were fixture shells using real
services; no live employee, skill, PDF or role was edited for verification.
Activity/role service line coverage was 86.08%; branch coverage was 63.44%.

Release publish passed. The existing local publication received the DLL/PDB and
Activity Log CSS/JS and restarted through its unchanged launcher and role/config
stores. Both assets return 200; anonymous list/details access redirects to Login.
Refresh and sign in again. Detailed design: `DEV-ACTIVITY-LOG-PLAN.md`.

### Employee history view

Activity Log now opens **Employee history** first. Enter an exact Employee code
such as `101` to see changes to that employee, newest first. **Changed by** is a
separate optional actor filter. Leading zeros are preserved. Bulk/move results
display the employee and skill that match the filters, even when another item
appears first in the original event. Details retain the complete event.

The employee view includes committed, partial and recorded no-change outcomes
only when an actual employee item was captured. Page/report/sign-in observations
are excluded; routine page opens no longer generate new events. All changes
retains nonemployee catalog history, and Attempts retains unsuccessful actions.
Existing audit data is preserved; this update needs no database migration.

Validation passed: 58 focused C#/SQL/page tests, six JavaScript tests and nine
isolated browser checks, including employee101 versus actor103, time ordering,
bulk matching, details/focus, combined filters and 1280/390px layouts. Published
DLL/PDB and Activity Log CSS were copied into the existing local publication and
restarted with its unchanged configuration. Anonymous employee-history access
redirects to Login; the stylesheet returns 200. Refresh and sign in again.

### Dashboard bug fixes

Department KPI buttons now use SQL-backed department snapshots instead of
hardcoded sample numbers. Department buttons and process cards share a single
selection; delayed count-up animations can no longer overwrite the selected
totals. Department expiry eligibility matches the existing All Data cutoff.

Employee search supports trimmed full names, keeps exact employee codes first,
and prevents cleared or superseded requests from restoring old results. Failed
queries can retry the same text; HTTP/session failures display safe errors.
Guest pages safely skip missing search controls and do not perform GET/POST
employee searches. POST initialization preserves the same identity and cached
dashboard state as GET.

Expiry Load More fetches beyond the cached 20 rows, uses one in-flight request,
preserves rows on failure and reports loaded results without inventing a total.
It loads progressively up to 1,000 rows and points to View full list for more.
Employee/name/process text is rendered with DOM text nodes, closing stored HTML
injection paths. Local photo URLs are validated before assignment. The employee
expiry popup now has real markup and switches Bootstrap modals so closing it
returns to the list with focus and scroll locking restored. Bangkok calendar-day
badges handle Today, critical and soon dates consistently.

Cold dashboard readers now wait for an in-progress refresh instead of receiving
an empty snapshot. Failed loads show an unavailable banner and placeholder
totals. Resignation periods use exclusive next-day bounds, avoiding SQL datetime
rounding that counted midnight in adjacent periods.

Validation: 754 web tests passed, with 13 unrelated fixture skips; 25 focused
JavaScript tests passed. Marked synthetic SQL fixtures cover department totals
and exact-midnight resignation boundaries. Read-only checks of the two new/fixed
queries passed on the existing SQL Server 2014 database, compatibility 100.
Existing guest expiry/report visibility was preserved. No database migration or
live employee, skill, file or role edits are part of this dashboard update.

Final isolated browser checks passed for all reproduced search/KPI defects and
the real Expiring soon button, progressive 20/40/45-row loading, literal hostile
text, Today/15-day badges and 1280/390px popups. Child/parent modal transitions
preserve one backdrop, scroll lock and row focus; closing the final list releases
all locks. Load More transfers focus before disabling its button so Escape keeps
working. Shared backdrop cleanup preserves a newly opening fade modal while
`body.modal-open` is set. Browser search/preview responses were synthetic; actual
SQL query behavior was verified separately as described above.

Release publish passed. The existing local runtime received the DLL/PDB and
three dashboard scripts through its unchanged launcher/configuration. Refresh
and sign in again after the restart.

### Remembered sign-in

Login now offers an optional **Keep me signed in for 30 days** checkbox, off by
default. A protected opaque cookie can restore identity after browser and app
restarts. It stores no employee identity, password or permissions. Fixed expiry,
SQL revocation and current credential/active-employee checks run for remembered
requests; roles remain current. Logout is an antiforgery POST and revokes the
browser's remembered token, including a trusted session marker when its cookie
is absent or corrupt. Unchecked login retains the existing 30-minute session.

Applied only the new `tblWebRememberedLogin` table/indexes on SQL Server 2014,
compatibility 100; no employee foreign key or business-row changes. Initial token
count was zero. Existing role files and persistent Data Protection keys were
preserved. Production requires HTTPS; the current local Development launcher
continues to support HTTP. Details: `REMEMBERED-LOGIN.md`.

Validation passed: 801 web tests, 13 unrelated fixture skips, and 20 isolated
browser checks including browser/app restarts, logout/replay, CSRF, warm-session
revocation, password/account changes, fresh roles and laptop/mobile layout.
Release publish passed and local DLL/PDB were refreshed. Login/Logout smoke
checks passed. Refresh and sign in once with the checkbox selected.

Remembered-login restoration initially hit SQL 468 because the registry's Thai
default collation differs from the legacy employee-code column's SQL Latin
collation. Replaced that join with two parameterized reads inside a serializable
transaction. A mixed-collation SQL test reproduced the failure; 56 focused tests
then passed, and read-only validation of an existing host sign-in returned true.
No saved sign-ins, employee data, schema or Data Protection keys were reset.

### Skill Inventory preview count

Read-only comparison found 4,663 old-app rows, 4,663 actual web-export rows and
only 4,640 preview rows. The preview alone excluded DAY shifts and grades outside
51T/52T: 22 DAY skill rows and one additional O1/shift-C row. The user confirmed
the old-app population should be retained. Removed only those extra preview
restrictions and aligned its disqualification predicate with the existing export.
Developer, promotion/resignation, section, department and date filters remain.
The export implementation and database records were not changed.

Six actual SQL cases compare preview counts with actual generated Excel IDs,
including DAY/other grades, Developer exclusions and representative optional
filters. All 18 focused inventory/exclusion/verifier tests passed. Release
publish passed; the local DLL/PDB were updated through the existing launcher.
Live HTTP verification confirms preview 4,663 and actual CSV download 4,663.
Refresh and run Preview again.

Separate existing issue observed during testing: the inventory date endpoints
both use an inclusive SQL datetime end built with AddTicks(-1), which can round
to next-day midnight. It is outside this preview-only correction; date behavior
was deliberately left unchanged. The parity fixture uses a July 2 date so it
does not turn this scope into a separate date-boundary behavior change.
