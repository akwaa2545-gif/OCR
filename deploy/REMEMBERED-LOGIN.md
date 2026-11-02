# Remembered web sign-in

Installed in the existing local web publication on 29 October 2026. Scope: web
application only; no Docker or mapped-drive deployment performed.

The login form offers an unchecked **Keep me signed in for 30 days** option.
An ordinary sign-in retains the existing 30-minute session behavior. Remembered
sign-in has an absolute 30-day expiry; restoration does not extend it.

The browser receives a host-only, HttpOnly, SameSite=Lax cookie containing a
Data Protection protected random 256-bit token. It contains no employee code,
password, credential hash or permission claims. Production remembered sign-in
requires HTTPS; Development deliberately supports the existing localhost HTTP
launcher. A separately configured `Authentication:AllowHttpSession=true` allows
ordinary, 30-minute manual sessions on an existing Production HTTP site. It does
not enable remembered sign-in over HTTP. The Docker deployment helper enables
that manual-session option only when the selected host JSON explicitly contains
the boolean `"AllowHttpSession": true`; the default remains HTTPS-only sessions.

SQL stores only the token hash, employee code, timestamps and a separately
protected proof of the canonical stored password. The proof is bound to its
employee and token row. The new `dbo.tblWebRememberedLogin` table has no employee
foreign key and does not alter employee, skill, file or role data.

Every dynamic request with a remembered cookie checks expiry, revocation,
current password proof and active employee status before publishing session
identity. Remembered sessions remain bound to that credential; missing or invalid
cookies also clear an already populated remembered session. Admin and Developer
permissions are read through the existing current role services.

Login verifies credentials and completes required storage before setting
`Session.UserCode`. An unchecked successful login revokes an older remembered
credential on that browser. Invalid/tampered cookies are deleted. A temporary
SQL failure denies restoration and clears remembered session identity while
retaining the cookie for retry, without authenticating from a cached profile.

Logout is an antiforgery-protected POST. GET shows confirmation only. Logout
revokes the current SQL token and clears the local cookie and session; failures
must distinguish local sign-out from unconfirmed server revocation. Login POST
and logout routes bypass automatic restoration.

The existing Data Protection application name and persistent key ring are
preserved. Keys must survive application/container restarts; losing them makes
protected cookies unreadable and requires a fresh manual login. Do not copy keys
or credentials into source control or client storage.

Verification covers opt-in/opt-out, browser and app restart, tampering, absolute
expiry, logout/replay/CSRF, account switching, password/status/deletion changes,
warm-session invalidation, transient SQL failure, current roles and failed login
publication. Use only marked synthetic databases for mutation tests. Live rollout
applies the new table/indexes only and performs read-only schema/account checks.

## Installed verification

The full web suite passed 801 tests with 13 unrelated fixture skips. Focused
coverage includes real marked SQL tests for credential proofs, expired/revoked
tokens, employee status/deletion, leading-zero codes, stale authentication,
row-bound ciphertext and both SQL-ID and legacy/binary photo schemas. Core/page
tests cover token privacy, cookie flags, HTTPS policy, fixed expiry, outage and
cancellation, current roles, account switching and trusted session-marker cleanup.

Twenty isolated browser checks passed using actual Login/Logout pages, SQL
stores and middleware, a dedicated synthetic database, and an isolated persistent
key ring. They covered opt-out, browser restart, app restart with a fresh memory
session, tampering, CSRF, GET confirmation, POST logout and replay, missing/corrupt
cookies with warm sessions, password/status/resignation/deletion changes, current
roles and 1280/390px layouts. No real credentials or employee edits were used.

Applied `20261029_remembered_login.sql` only to the inspected
`OperatorCertificationRecordDB` (SQL Server 2014, compatibility 100). The new
table contained zero records after installation and has no employee foreign key.
A read-only account query passed; no live token was issued during verification.

Release publish passed. The local runtime received the compiled DLL/PDB and
restarted through its unchanged launcher, role files, connection and key ring.
Login returns 200 with the 30-day choice and NoStore headers. GET Logout returns
the POST confirmation form. Users should refresh, sign in once and select the
option; subsequent browser/app restarts can restore that remembered sign-in.

## Legacy collation compatibility correction

The host's legacy employee-code column uses `SQL_Latin1_General_CP1_CI_AS`, while
the new registry inherited database-default `Thai_CI_AI`. Directly joining those
columns raised SQL 468 after a successful remembered login. The original fixture
used matching collations and did not expose this difference.

Validation now reads the indexed token first, then uses an employee-code
parameter for the legacy lookup. Both reads share a serializable transaction;
expiry, revocation, active status and the protected credential proof remain
required. No schema, employee records, existing tokens or key-ring changes were
needed. Server diagnostics include the SQL error number without sensitive error
messages or cookie values.

A mixed-collation SQL regression reproduced the exact failure before the fix.
All 56 focused remember-login/activity tests passed after it. Read-only validation
of an existing saved sign-in on the actual SQL Server 2014 host returned true;
it issued no token, browser cookie or authenticated session and changed no rows.
