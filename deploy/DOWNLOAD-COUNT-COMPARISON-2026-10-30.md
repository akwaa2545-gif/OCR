# Windows and web download count comparison

Inspection date: **30 October 2026, Asia/Bangkok**.

This is a read-only comparison of all seven legacy download choices against the
current web implementations. No application behavior, database records, schema,
roles or deployments were changed for this inspection.

**Follow-up applied locally:** the Windows source and local build now use the
main web download rules. The original comparison below remains the historical
baseline; see the implementation and verification section at the end.

## Evidence and scope

- Database: `OperatorCertificationRecordDB`.
- The connection aliases differ, but read-only SQL instance/database identity
  checks confirmed that the legacy Welcome, Section, Expiry, Disqualification
  and Resignation sources and the web configuration reach the same database.
  No credentials are copied into this report.
- Ten relevant legacy source/Designer files match between the original checkout
  and the managed worktree. Old counts were recomputed from the actual wired
  button queries, including the No-skill post-filter; the Windows executable
  itself was not launched to automate Excel.
- The initial live run was **10:48:27–10:50:27 +07:00**. It exercised **88
  preview/CSV comparisons**, **28 actual Excel exports**, every one of the
  **17 nonblank section names**, and year-to-date filters for **12 departments**.
  Another 17 Excel comparisons checked the separate Section endpoint.
- Actual current web controller/page methods generated CSV/XLSX in memory.
  Excel row counts exclude the column-header row. CSV was parsed as CSV,
  including quoted fields, rather than counted by newline characters.
- Current Developer-role exclusion was loaded from the running application's
  original `App_Data/developers.json`, not a worktree-local file.

## Comparable all-time results

Inventory, Section and No-skill have no legacy date filter. The main web menu's
Inventory, Section and No-skill All time selections are unbounded. For web
Expiry, Disqualification, Obsoleted and Resignation, All time means
**2000-01-01 through 2099-12-31**. The legacy
Obsoleted button returns all its eligible history, including dates outside that
range. That difference is explicitly included in the breakdown below.

| Download choice | Old query rows | Web preview | Web CSV | Web Excel | Difference: web minus old |
|---|---:|---:|---:|---:|---:|
| Skill Inventory, all departments | 4,661 | 4,661 | 4,661 | 4,661 | 0 |
| Section, all 17 selections combined | 4,661 | 4,661 | 4,661 | 4,661 | 0 |
| Operator No-skill | 13 | 13 | 13 | 13 | 0 |
| Expiry Date | 4,661 | 4,638 | 4,638 | 4,638 | -23 |
| Disqualification | 3,695 | 3,650 | 3,650 | 3,650 | -45 |
| Obsoleted, initial capture | 28,657 | 28,186 | 28,186 | 28,186 | -471 |
| Resignation | 8,418 | 6,820 | 6,820 | 6,820 | -1,598 |

These menus overlap; adding their totals would not produce a meaningful
employee or skill total. Most counts are **exported skill/view rows**, not
distinct employees. No-skill currently has one exported row per employee.

The database remained live: a later Obsoleted recheck at **10:57:33** returned
**28,661 old rows versus 28,190 web preview/CSV rows**. The difference remained
471. The table retains the earlier synchronized Excel observation instead of
presenting the later CSV count as a newly verified Excel count.

The earlier conversation's 4,663 Inventory figure was a previous data capture;
this inspection found 4,661 in both implementations.

## Exact explanations for the differences

Exclusion buckets below are evaluated in order, so a row with several reasons
is counted once. The counts reconcile with the observed old-to-web gaps.

### Expiry Date: 23 fewer web rows

- 22 rows belong to DAY-shift employees.
- 1 row belongs to an employee outside grades 51T/52T.

The old Expiry menu has neither restriction. The current web count and download
both apply them. There was no preview/export mismatch.

### Disqualification: 45 fewer web rows

- 36 rows belong to DAY-shift employees.
- 9 rows belong to employees outside grades 51T/52T.

The old Disqualification menu has neither restriction. Again, preview and
download agree with each other while selecting a narrower population.

### Obsoleted: 471 fewer web rows

| Exclusion reason | Rows |
|---|---:|
| Outside the web All-time date range | 3 |
| Current Developer role | 6 |
| Remark contains PROMOTED or RESIGNED | 241 |
| DAY shift | 198 |
| Other job grade | 23 |
| **Total** | **471** |

The three excluded certification years are **2123, 2413 and 2526**, one row
each. The legacy button has no date filter, whereas the web All-time preset
ends in 2099. These dates should be reviewed separately; this audit did not
correct them.

Developer exclusion is an existing user requirement. The other differences
are additional web population rules or preset behavior; they should not be
described as desktop parity without deciding which rules are intended.

### Resignation: 1,598 fewer web rows

All 1,598 excluded rows have **no process**. The old report includes these
resigned employee rows; the web requires `ProcessName IS NOT NULL`.

In the initial capture, the old report represented 3,880 distinct employees and
the web represented 2,282. Their 1,598-person difference matches the no-process
row exclusion. The web report is therefore narrower than the old report, not
just differently formatted.

## Current-year comparison and defaults

Matched range: **2026-01-01 through 2026-10-30**.

| Report | Old rules restricted to this range | Web preview | Web CSV | Web Excel |
|---|---:|---:|---:|---:|
| No-skill | 2 | 2 | 2 | 2 |
| Expiry Date | 2 | 1 | 1 | 1 |
| Disqualification | 521 | 520 | 520 | 520 |
| Obsoleted | 361 | 331 | 331 | 331 |
| Resignation | 1,211 | 950 | 950 | 950 |

The old No-skill and Obsoleted screens have no date controls. Their old-rule
current-year numbers above are analytical comparisons, not selectable legacy
screen totals. Current web date-based menus default to This year through today;
Inventory/Section remain unbounded.

The legacy Expiry, Disqualification and Resignation Designer values default
both date fields to **15 March 2018**. No active Load override was found. Each
of those exact one-day default queries currently returns zero rows. Comparing
those defaults with the web's current-year defaults would be misleading.

Legacy date queries use `BETWEEN` with an end at midnight; the web now includes
the complete selected end day. No end-day timestamp row caused a count
difference in the captured comparisons, but the behaviors remain different.

## All section choices

The main web menu uses **Skill Inventory with a section selection**. It matches
the legacy Section report for every tested section.

| Section | Old rows | Main web preview / CSV / Excel |
|---|---:|---:|
| 1st FA Eng | 0 | 0 |
| 2nd FA Eng | 0 | 0 |
| BOL | 1,806 | 1,806 |
| CQE_Ta | 60 | 60 |
| EHS Center | 0 | 0 |
| Fin & Acct | 0 | 0 |
| HR&GA | 0 | 0 |
| Inspection | 271 | 271 |
| IT Sect | 0 | 0 |
| MOL | 2,068 | 2,068 |
| NNECL | 16 | 16 |
| PEC_Ta | 0 | 0 |
| QMS_Doc.Con | 0 | 0 |
| SC | 0 | 0 |
| SC Eng | 0 | 0 |
| SC Mfg | 440 | 440 |
| Ta Prod Eng | 0 | 0 |

### Remaining bug in the separate Section endpoint

The alternate `/api/download/section/count` and `/Download/Section` pair still
disagrees. This is separate from section selection inside Skill Inventory.

| Section | Alternate preview | Actual alternate Excel | Missing from preview |
|---|---:|---:|---:|
| Inspection | 265 | 271 | 6 |
| MOL | 2,067 | 2,068 | 1 |
| NNECL | 0 | 16 | 16 |

The alternate preview excludes DAY shifts and grades outside 51T/52T while its
export does not. The remaining 14 section choices matched. Dashboard still
renders the separate Section modal/helper, but its visible six download cards
use the main Inventory selection for section downloads.

## Source anchors and remaining definition differences

- Authoritative legacy wiring: `OperatorCertificationRecord/frmWelcome.Designer.cs`,
  lines 245–311. Inventory's actual query is `frmWelcome.cs:311`; the broader
  `btnAllDepartment_Click` query at line 196 is not the pictured button handler.
- Legacy Section: `frmDownloadSection.cs:50`; No-skill: `frmWelcome.cs:504`
  plus the employee-wide promotion post-filter at lines 571–642; Obsoleted:
  `frmWelcome.cs:436`.
- Legacy dated queries: `frmDownloadExpiryDate.cs:39`,
  `frmDownLoadDisqualification.cs:37`, `frmDownloadResign.cs:39`.
- Web counts/exports: `OperatorCertificationRecord.Web/Controllers/DownloadController.cs`;
  Inventory export: `Pages/Download/SkillInventory.cshtml.cs`;
  alternate Section export: `Pages/Download/Section.cshtml.cs`.
- Main Inventory section routes are wired in `Pages/DownloadSection.cshtml`;
  the alternate pair lives in `Pages/Shared/_SectionDownloadModal.cshtml`.
- Current No-skill results match. Its definitions are not identical: old code
  filters `ResignBy`, the web filters `ResignDate`; the old promotion exclusion
  is employee-wide, while the web's Remark exclusion is row-based. Web preview
  counts distinct employee codes, while export DISTINCT includes Notice. None
  of these definition differences produced a current-data mismatch.
- The No-skill Excel formatter skips null JoinDate rows; the current unbounded
  No-skill CSV and Excel both had 13 rows, so that source-level edge case did
  not produce a live discrepancy.

Raw count-only observations and diagnostic employee-code samples are retained
in the ignored task directory's `DownloadCountAudit/results.json` and
`breakdown.json`. They contain no passwords or connection strings.

## Follow-up decisions

1. Fix the alternate Section preview/export mismatch.
2. If desktop parity is the target, align Expiry/Disqualification shift/grade
   rules, Obsoleted population/preset rules, and Resignation no-process handling.
3. Preserve the explicitly requested Developer exclusion when making any parity
   changes.
4. Review the three out-of-range archive dates before changing date data.

This inspection did not apply any of those changes.

## Windows alignment implemented and verified

On the user's subsequent instruction, all seven wired Windows download choices
were changed to use `DesktopReportQuery` and `DesktopReportService`. Inventory
and Section retain all grades/shifts; No-skill, Expiry, Disqualification and
Obsoleted use the web's non-DAY and 51T/52T filters. Current/obsolete remark,
resignation, process and Developer exclusions follow the corresponding main web
exports. Resignation keeps all grades/shifts and requires a process. The old
employee-wide promotion post-filter was removed.

Dates use typed SQL parameters, inclusive start and exclusive next-day end.
Existing dated forms start January 1 through today. No-skill and Obsoleted now
have This year / All time / Custom selection; No-skill All time is unbounded,
while Obsoleted All time is 2000-01-01 through 2099-12-31. Existing Excel column
order, date display and leading-zero employee codes are preserved.

The SELECT-only follow-up ran **11:32:51–11:34:16 +07:00**. All **33 cases**
matched the actual web preview, CSV and Excel totals. Every exported column was
also compared as a multiset of complete rows, normalizing displayed dates and
column order. This included all 17 sections and All time / This year / Today
for the five dated report types. Matching All time totals were:

| Report | Windows / web rows |
|---|---:|
| Inventory | 4,661 |
| Sections combined | 4,661 |
| No-skill | 13 |
| Expiry | 4,638 |
| Disqualification | 3,650 |
| Obsoleted | 28,190 |
| Resignation | 6,820 |

These are snapshots of active data, not fixed expected counts. The ignored
`DownloadCountAudit/aligned-results.json` contains the observations. The alternate
Section preview inconsistency documented above remains outside this change;
Windows Section follows the main Inventory section export.

Validation: full desktop suite **53 passed, 2 existing skips**; focused SQL date
and Developer exclusion tests plus existing boundary checks **16 passed** against
a marked isolated LocalDB fixture. New desktop helper coverage was **86.02% of
lines / 80.15% of branches**. C#, general and security reviews approved. The x86
.NET Framework 4.8 Windows build succeeded; Excel COM interaction itself was not
automated during this change.

Local executable:
`C:\code test\OperatorCertificationRecord\OperatorCertificationRecord\bin\Release-WebReportRules\OperatorTrainingRecord.exe`.
Its config enables SQL reads/writes and points `DeveloperRoles:StorePath` to the
existing authoritative local web `App_Data\developers.json`. No static role copy
is shipped. The reader reloads legacy arrays or schema-1 audited envelopes for
every export; unreadable/invalid role data stops export visibly.

A future deployment to other PCs must configure a readable UNC role-file path
used as the authority by both applications. The checked-in config leaves this
environment-specific path empty. No mapped-drive deployment, Docker change,
business database write/schema change or commit was performed for this task.
