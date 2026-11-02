# Shared SQL access roles

## Purpose

Use the existing application database as the authority for **Admin** and
**Developer** membership. Windows report downloads read Developer exclusions
from this database instead of a JSON path on the development PC. Downloading
does not require the signed-in employee to be a Developer.

The web app retains its existing rules: only Developers can grant or remove
Developer access; Admins can manage ordinary Admin membership but cannot manage
Developer users; the last member of a privileged role cannot be removed.

## Storage contract

| Table | Purpose |
| --- | --- |
| `dbo.tblApplicationRoleStore` | One row per role, containing membership revision, pending audit events and consumed command receipts in the validated role envelope. |
| `dbo.tblApplicationRoleMember` | Normalized role/employee membership for both applications, committed in the same transaction as the envelope. |

Employee codes remain text, including leading zeros and historical short codes.
The schema works with SQL Server 2014; it does not use SQL JSON functions.
SQL role writes use a shared database lock and update the envelope and membership
together. The web audit dispatcher continues delivering queued role events.

## One-time import

1. Finish current role changes and stop the old web writers. Determine the two
   **authoritative** `developers.json` and `admins.json` files. Do not use an
   outdated worktree copy.
2. Use the web app's existing `DefaultConnection` configuration, supplied through
   its normal secret/configuration mechanism. Do not put a password in a command
   or commit it to a configuration file.
3. Run the explicit migration command from the web project's normal working
   directory, with both source paths:

   ```powershell
   dotnet run --project OperatorCertificationRecord.Web -- --migrate-access-roles `
     --DeveloperRoles:StorePath "C:\approved-role-source\developers.json" `
     --AdminRoles:StorePath "C:\approved-role-source\admins.json"
   ```

   Replace the example paths with the actual authoritative files. For a
   published app, run its web DLL with the same arguments and the same normal
   database configuration. The migration exits after reporting the role counts;
   it does not start the web server.
4. Configure the updated web app with `AccessRoles:Storage=Sql`, then start the
   updated web build. In Docker this can be supplied as
   `AccessRoles__Storage=Sql`; Docker is not required to use SQL roles.
5. Start the updated Windows build with its existing application database
   connection. It no longer needs `DeveloperRoles:StorePath` for reports.

The migration validates both source files before touching the database, holds
their existing writer locks, and imports the schema and both roles in one SQL
transaction. Existing membership, pending audit events and duplicate-submit
receipts are preserved. It never creates a fixed default Developer account.
The database principal applying this explicit migration needs schema-creation
permissions; ordinary runtime requests do not create the schema.

Repeating the import checks an already initialized SQL store and does **not**
overwrite newer SQL membership with old JSON files. Both source files must still
be valid for this explicit command. An incomplete SQL initialization, unsupported
schema or mismatch between audited membership and normalized membership stops
the command for investigation.

## Runtime behavior and rollout

The updated web app must use SQL mode and the updated Windows reports must point
to the same SQL database. Confirm the database identity rather than relying on
two differently named connection aliases. The JSON files become import sources;
do not continue running an older web build that writes them as a separate role
authority.

When SQL roles are unavailable, privileged checks deny access and reports stop
with an availability error. Neither application silently substitutes an empty
Developer list or an old JSON snapshot. The initialized-empty Developer store
is distinguishable from missing migration/schema.

Deployment remains a separate operation: editing source or applying the SQL
import does not update a running web process or a Windows executable on a mapped
drive. The executable, dependencies and matching `.exe.config` must come from
the verified release. No fallback to an older file-authoritative binary is safe
after SQL role changes begin; restore a SQL-compatible build or pause role
writes while fixing forward.

## Verification

- Compare imported counts and employee codes with the authoritative source files.
- Open role management as a Developer and as an Admin; verify the existing
  permission boundaries and last-member protection.
- Change one test membership through the web UI and verify a fresh SQL read,
  duplicate-submit protection and its role activity event.
- Run a Windows report from a PC without the development source folder. Verify
  that Developer employees are excluded and ordinary users can still download.
- Run the marked isolated LocalDB migration tests using
  `OCR_ACCESS_ROLES_TEST_CONNECTION`; the fixture must use an
  `OcrAccessRoleTests_` database with `OcrEnvironment=SyntheticLocalFixture`.

## Applied on 2 November 2026

The explicit import completed at **08:26:51 Bangkok time** in
`SVR120A.OperatorCertificationRecordDB`, using the original local web app's
`App_Data` files. SQL readback confirmed:

| Role | Members | Membership revision | Pending events | Preserved command receipts |
| --- | ---: | ---: | ---: | ---: |
| Developer | 2 | 1 | 0 | 2 |
| Admin | 10 | 1 | 0 | 1 |

Developer employee codes are `101` and `2205529`. No employee, skill or attachment
record was changed by this import. The Windows SQL reader read these same two
members from the live database. Its Inventory query returned 4,635 rows with no
Developer employees; this count is a data snapshot, not a fixed expected total.

Updated local releases:

- Windows: `C:\code test\OperatorCertificationRecord\OperatorCertificationRecord\bin\Release-WebReportRules\Operator Training Record.exe`.
- Web: `C:\code test\OperatorCertificationRecord\OperatorCertificationRecord.Web\bin\Release-SqlStorage`.

The Windows release keeps its existing SQL attachment settings and no longer
contains `DeveloperRoles:StorePath`. The local web release uses SQL roles; start
it with its existing `Start-LocalSqlStorageWeb.cmd`. Source/build hashes were
checked after copying the releases. **The mapped-drive executable and Docker
deployment were not updated**, and no running application was restarted.

Validation included 145 focused web role/report tests passing (two unrelated
activity SQL tests skipped), 42 desktop report tests passing including isolated
SQL concurrency checks, and 20 dedicated SQL role storage/migration tests passing
without skips. General and C# reviews approved the changes. SQL migration tests
ran on disposable marked LocalDB databases, not the business database.
