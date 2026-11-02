# Deploy the SQL-storage app from a local Docker image archive

The application still runs in Docker. Migrated photos, certificate PDFs and
Admin/Developer roles live in the existing SQL Server database. **Do not create
a new local folder or Docker SQL container to hold those SQL attachments.**

Use an image built from the updated `SQL-storage` branch. Adding environment
variables to an older image does not add the new SQL reader/writer code.

## Settings added for the new storage

```text
FileStorage__ReadSqlEnabled=true
FileStorage__WriteSqlEnabled=true
AccessRoles__Storage=Sql
```

Keep the same production connection in the protected host file
`C:\ocr-deploy\prod.env`. Its format is one existing connection entry:

```text
ConnectionStrings__DefaultConnection=<production-connection-string>
```

Do not put the real value in scripts, Compose, Git or shell history. The database
name must match `ExpectedDatabase` in the host's `prod.json`. Test deployment uses
its own `test.env`, `test.json` and separately prepared test database.

## Local Windows folders still needed

These are the existing production defaults; the host JSON files are authoritative.

| Host folder | Container path | Purpose |
| --- | --- | --- |
| `C:\uploads` | `/app/wwwroot/certs` | Unmigrated legacy PDFs. |
| `C:\ocr-uploads` | `/app/photo-source` and `/app/wwwroot/uploads` | Existing upload sources and unresolved legacy upload references. |
| `C:\ocr-photos` | `/app/legacy-photos` | Unmigrated legacy employee pictures. |
| `C:\ocr-dataprotection` | `/app/dataprotection` | Persistent encryption/sign-in keys. |
| `C:\ocr-appdata` | `/app/App_Data` | Other persistent settings, templates and schedules. Roles now use SQL. |

Keep the existing contents. If the old container has unmounted `/app/App_Data`,
preserve that data in its approved `AppDataRoot` **before** replacing it; do not
adopt an empty folder over existing settings. SQL-owned files never fall back to
these folders after a SQL error. Keep the legacy folders for unresolved owners.

The dashboard slideshow photos are packaged under `/app/wwwroot/images` in the
new image. No mount for the development PC's `Slideshow` folder is needed.

## Prepare the TAR file

From the updated source checkout on the build PC:

```powershell
docker build --platform linux/amd64 -t ocr-web:sql-storage .
docker save -o C:\temp\ocr-webimagetest.tar ocr-web:sql-storage
```

Transfer the TAR and these two scripts together to the Docker host:

```text
C:\temp\ocr-sql-storage-deploy\
    Deploy-LocalImage.ps1
    Deploy-Ocr.ps1
C:\temp\ocr-webimagetest.tar
```

The scripts read the existing protected `C:\ocr-deploy` configuration; do not
copy its secrets into the transfer folder. Keep both host environment JSON files
available so the existing test/production separation checks can run.

## Validate, then deploy

Run Windows PowerShell on the Docker host with Linux containers running:

```powershell
cd C:\temp\ocr-sql-storage-deploy

# Read-only configuration, existing-container and SQL-structure checks.
.\Deploy-LocalImage.ps1 -Environment prod -ImageTag ocr-web:sql-storage -ValidateOnly

# When ready to replace the selected container:
.\Deploy-LocalImage.ps1 -Environment prod -ImageTag ocr-web:sql-storage
```

`ImageArchive` defaults to `C:\temp\ocr-webimagetest.tar`; use `-ImageArchive`
for another TAR location. `ConfigRoot` defaults to `C:\ocr-deploy`.

The deploy script loads that archive, verifies that it supplied the requested
tag, pins the resulting Linux/amd64 image ID, and tests a candidate before
replacing the configured container. It does not pull another registry image.
The new image must declare both SQL capability labels from the updated Dockerfile;
environment flags alone do not prove an old image understands SQL storage.
Volumes and committed SQL data are not deleted or rolled back. Validation does
not load the TAR or stop containers.

If replacement fails, automatic restart of the previous container requires its
SQL capability labels and SQL read/role settings to be verified. An unverified
legacy image is retained stopped after a failed cutover instead of being started
as an unsafe old writer; its automatic restart policy is disabled as well.
Restore a verified SQL-aware release or fix forward;
container rollback cannot undo the SQL migration.

SQL mode checks the file-storage marker/tables/owner columns and both initialized
role-store rows before changing Docker images or containers. This is structural
readiness, not a full attachment or role-envelope audit. It performs no schema
creation, file import or role seeding. Prepare a test database separately before
using `-Environment test`; production migration does not initialize the test DB.

The GitHub deployment workflow also passes `-EnableSqlStorage` for both test and
production. Each environment must have its own prepared SQL structures and roles;
an unprepared database stops deployment before containers change. Deployment does
not create schemas, import attachments or seed roles.

## Equivalent runtime flags

For comparison with the old `docker run` command, the new container needs these
arguments. The helper above performs the replacement; this command alone does
not preserve an existing container's unmounted settings or manage cutover.

```powershell
docker run -d --name ocrweb -p 8080:80 `
  --env-file C:\ocr-deploy\prod.env `
  --mount type=bind,source=C:\uploads,target=/app/wwwroot/certs `
  --mount type=bind,source=C:\ocr-uploads,target=/app/photo-source `
  --mount type=bind,source=C:\ocr-uploads,target=/app/wwwroot/uploads `
  --mount type=bind,source=C:\ocr-photos,target=/app/legacy-photos `
  --mount type=bind,source=C:\ocr-dataprotection,target=/app/dataprotection `
  --mount type=bind,source=C:\ocr-appdata,target=/app/App_Data `
  -e ASPNETCORE_ENVIRONMENT=Production `
  -e ASPNETCORE_URLS=http://+:80 `
  -e FileStorage__ReadSqlEnabled=true `
  -e FileStorage__WriteSqlEnabled=true `
  -e AccessRoles__Storage=Sql `
  -e CertificatePath=/app/wwwroot/certs `
  -e LocalUploadSubfolder=/app/wwwroot/certs `
  -e PhotoPath=/app/legacy-photos `
  -e PhotoStorage__UploadRoot=/app/photo-source `
  -e PhotoStorage__DatabasePathPrefix=/app/photos `
  -e PhotoStorage__LegacyMirrorRoot=/app/legacy-photos `
  -e PhotoStorage__MaxFileBytes=5242880 `
  --restart unless-stopped `
  ocr-web:sql-storage
```

Container port 80 is explicit because the .NET 8 image otherwise defaults to
8080. See [Microsoft's container-port documentation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/8.0/aspnet-port).
The private env file and bind syntax are documented by [Docker](https://docs.docker.com/reference/cli/docker/container/run/).

`8080:80` is the backend mapping. Production browser access must use HTTPS:
session cookies are Secure, and remembered sign-in also requires the app to see
an HTTPS request. A TLS proxy must use the app's deliberately trusted forwarding
configuration; this script does not add proxy trust or weaken cookie security.
Direct plain HTTP to port 8080 is not a complete Production sign-in setup.

## Compose alternative

The updated repository `docker-compose.yml` uses the same SQL flags and existing
local folders, with the database connection supplied through `OCR_ENV_FILE`
(default `C:/ocr-deploy/prod.env`). Set `OCR_IMAGE` to the loaded tag. Bind sources
must already exist; Compose will not create them. Preserve App_Data before adoption.

Compose does not run the helper's SQL preflight, test/production separation or
candidate cutover checks. Use the helper for the guarded replacement workflow.

Related: [SQL file authority](SQL-FILE-STORAGE-RUNBOOK.md),
[SQL role import](SQL-ACCESS-ROLES.md), [host setup](README.md).
