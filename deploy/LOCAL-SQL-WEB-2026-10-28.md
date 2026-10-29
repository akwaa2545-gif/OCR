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
