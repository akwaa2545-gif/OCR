# OCR deployment

Planning: [legacy Windows / web compatibility and feature handover](LEGACY-WEB-COMPATIBILITY-MIGRATION-PLAN.md),
including the `SQL-storage` baseline, implementation checklist and app compatibility diagram.

The workflow builds on GitHub-hosted Ubuntu, tests deployment scripts on GitHub-hosted Windows PowerShell 5.1, publishes a Linux/amd64 image to GHCR, and deploys its immutable digest on the Windows host. The legacy .NET Framework desktop application is not part of this web deployment.

## Use

- Push to `main`: tests, image publication, and automatic **test** deployment.
- GitHub Actions > **OCR build and deploy** > **Run workflow**: select branch `main`, then `test` or `prod`.
- `prod` is manual only and uses the GitHub `production` environment, configured with a required reviewer. Both `test` and `production` allow deployment from `main` only. Approve the production environment request in GitHub to release a production run.
- Pull requests run checks only on GitHub-hosted runners. They cannot deploy or publish images.
- Dispatching a non-main branch does not publish or deploy.

Both environments share one deployment concurrency group. An active deployment is never canceled by a newer run; GitHub may replace an older pending run with the newest pending run. This is not a FIFO release queue. Manual production runs rebuild the selected main commit; they do not promote a previous test image automatically.

## Host prerequisites

- Host `THBTCDT-CM1XKG2` (`10.249.160.52`), a repository-scoped self-hosted runner with `Windows` and `X64` labels, supported runner version, Windows PowerShell 5.1, Git, and Docker CLI.
- Docker Desktop Linux engine must be running and reachable **under the runner identity**, not just another desktop user. The installed `OCR-DeploymentRunner` hidden scheduled task runs with `Limited` privileges under the signed-in Docker user `kemet\2172172205529`, not NETWORK SERVICE or SYSTEM. The previous runner service is disabled. The workflow explicitly uses the Docker Desktop Linux named pipe.
- After a reboot the Docker user must sign in and Docker Desktop must start before deployments can run. Disconnecting Remote Desktop is fine; signing out stops the interactive runner. No password autologon is configured. This setup is not unattended across reboots; that would require a separately planned always-on Docker host.
- Runner must read `C:\ocr-deploy\test.json`, `prod.json`, `test.env`, and `prod.env`, and access all mount directories. Restrict those files with Windows ACLs; never commit `.env` files or publish Docker inspect output containing connection strings.
- Enable Actions and GitHub Packages for the repository. GHCR uses the workflow's short-lived `GITHUB_TOKEN` (`packages:write` only in publish, `packages:read` only in deploy). Existing packages must grant this repository Actions access. No permanent registry password is needed.
- Protect `main` and require trusted review of workflow/deployment-script changes: repository code executing on the host has access to Docker and host secrets. Do not allow untrusted workflows to use this runner.

Deployment uses the job's read-only `GITHUB_TOKEN` in a private, GUID-named Docker configuration under `C:\ocr-deploy`, then removes the exact temporary credential file and directory. The image pull authenticates directly; deployment does not run `docker login`/`logout` or modify Docker Desktop's shared Windows credential store. The helper rejects unsafe permissions and reparse paths before writing credentials. No database credentials are transferred from GitHub.

## Installed setup (2026-09-23)

The repository default branch is `main`. GitHub `test` and `production` environments exist with main-only deployment policies; production requires reviewer approval. The first end-to-end test deployment succeeded in [run 35837840799](https://github.com/akwaa2545-gif/OCR/actions/runs/35837840799), application revision `a3c0de2`. The host pulled and deployed `ghcr.io/akwaa2545-gif/ocr/ocr-web@sha256:16d34b2afe81e7533da2d84bd2b0d608828e5bd689be24e51c08dd1c872264c5`; test `/health/ready` returned HTTP 200 `Healthy`, and both site roots returned HTTP 200. Production remained on its original container. The prior test container was retained as `ocrwebtest-backup-f56c5f9fcfd4`. Temporary registry credentials were removed, and test certificates remained empty as requested.

`Configure-RunnerTask.ps1` and `Initialize-HostConfig.ps1` are **one-time setup tools**, not regular deployment steps. They have already been applied to this host. Do not rerun them blindly or delete existing state to bypass their refusal checks. Inspect the scheduled task, runner service, and configuration first when repairing or migrating setup. Neither script belongs in the recurring deployment job.

`C:\ocr-deploy` contains the two environment configurations and connection-string files. Its ACL restricts access to SYSTEM, Administrators, and the Docker/deployment user. Database passwords were removed from repository appsettings files; runtime connection strings come from the protected host environment files. Never paste those files, unfiltered container inspection, or passwords into issues, logs, or public documentation. Removing credentials from current files does not revoke previously exposed credentials or erase repository history; rotate exposed credentials separately.

## Host configuration

Each JSON file has these fields (values are installation-specific):

```json
{
  "ContainerName": "ocrwebtest",
  "Port": 5050,
  "CertificatesRoot": "C:\\ocr-test\\certs",
  "UploadRoot": "C:\\ocr-test\\uploads",
  "PhotosRoot": "C:\\ocr-test\\photos",
  "DataProtectionRoot": "C:\\ocr-test\\dataprotection",
  "LegacyTestUploadRoot": "C:\\ocr-test\\uploads-test",
  "AppDataRoot": "C:\\ocr-test\\appdata",
  "ExpectedDatabase": "OperatorCertificationRecordDB_Test"
}
```

Each environment's `.env` contains exactly one `ConnectionStrings__DefaultConnection` entry using its own database, plus optional comments. The deployment script validates names, ports, database names, local mount roots, and cross-environment separation before changing containers. Existing host files, not this example, are authoritative. Production uses `ocrweb`, port 8080, and `OperatorCertificationRecordDB`.

Installed storage mapping:

| Purpose | Production | Test |
| --- | --- | --- |
| Certificates | `C:\uploads` | `C:\ocr-test\certs` |
| Photo upload source / uploads | `C:\ocr-uploads` | `C:\ocr-test\uploads` |
| Legacy photo mirror | `C:\ocr-photos` | `C:\ocr-test\photos` |
| Encryption keys | `C:\ocr-dataprotection` | `C:\ocr-test\dataprotection` |
| Legacy testing uploads | Not configured | `C:\ocr-test\uploads-test` |
| Application data | `C:\ocr-appdata` | `C:\ocr-test\appdata` |

Existing container `/app/App_Data` contents were exported before first adoption (approximately 24 KB production and 20 KB test). Future managed containers bind their environment's `AppDataRoot` to `/app/App_Data`, preserving application data across replacements. These exports are snapshots, not a recurring backup policy.

**Test certificates are intentionally not copied.** Existing certificate records may refer to missing test files until the owner requests a separate copy. The workflow never copies production certificates or runs the storage migration script.

## Legacy photo share synchronization

Docker's `C:\ocr-photos` mirror is not the legacy network share. `Sync-LegacyPhotos.ps1` bridges committed production uploads from `C:\ocr-uploads` to `\\svr120a\PhotoEmp$\<EmpCode>.<extension>` (the legacy F: mapping). It reads only employee codes/photo references from the production database; it does not update employee records, copy certificates, recurse through upload folders, or access test storage.

`Install-LegacyPhotoSync.ps1` registers `OCR-LegacyPhotoSync` under the existing signed-in Docker user with limited privileges, at logon and every minute. No password is stored. RDP disconnection is fine; signing out or rebooting without signing back in pauses this task, just like the Docker runner. The UNC path avoids dependence on a drive letter's session mapping.

Installed on 2026-09-23 after an interactive-account share read/write and atomic-replacement check. Initialization baselined 5,327 photos, copied only employee `102`, and reported zero errors/conflicts. The share's `102.jpg` was independently read from the client PC and matched the current production upload's SHA-256. No certificate files or test photos were copied.

The first run records existing photo versions and destination hashes without bulk-copying historical photos. Initialization explicitly seeds employee `102`; other historical repairs require a separately reviewed `-SeedEmployee` run. Subsequent committed web photo versions are eligible for synchronization. Unexpected changes on the legacy share are treated as conflicts, not overwritten automatically. Existing photos are atomically replaced with retained `.ocr-sync-backup-*.bak` backups; old extensions and unrelated files are not deleted. Both applications must cooperate to eliminate all concurrent-writer races; the bridge's checks and backups mitigate, but do not provide a cross-application transaction.

Runtime scripts and state live under protected `C:\ocr-deploy`; state is in `photo-sync\state.json` and the latest result is `photo-sync\last-run.json`. Never delete state to force a retry or rerun the installer blindly. A nonzero task result or nonzero error/conflict count needs investigation; share or database outages preserve existing photos and are retried by later runs. The task is bounded to three minutes and ignores overlapping starts.

```powershell
Get-ScheduledTaskInfo -TaskName OCR-LegacyPhotoSync
Get-Content -LiteralPath C:\ocr-deploy\photo-sync\last-run.json
```

Sync script updates are host-side operations, separate from web-image deployment. Review and test the scripts, stop the sync task before replacing its installed script, preserve state, then resume and verify its result. The older repository-root `SyncLegacyPhotos.ps1` is a manual backfill tool and must not run alongside this stateful bridge.

## Container deployment safety and limits

`Deploy-Ocr.ps1` pulls the pinned image, starts a loopback-only candidate and checks `/health/ready`, then replaces the selected container and checks readiness again. A failed replacement attempts to restore the retained old container. Other environments are not stopped. Backups are retained; clean them up only after confirming a successful release and identifying the exact backup.

There is a short cutover interruption. Container rollback does not undo database writes or file writes; schema changes must be backward-compatible, and database/storage backup remains a separate responsibility. Readiness is a deployment gate, not continuous monitoring or an end-to-end business test. Job cancellation, host power loss, or forced process termination can interrupt cleanup/rollback and require manual recovery.

## Local checks

```powershell
& ./deploy/test-Deploy-Ocr.ps1
& ./deploy/test-Isolate-TestStorage.ps1
& ./deploy/test-Workflow.ps1
& ./deploy/test-RunnerTask.ps1
& ./deploy/test-HostConfig.ps1
dotnet test tests/OperatorCertificationRecord.Web.Tests/OperatorCertificationRecord.Web.Tests.csproj -c Release
```

The PowerShell tests use fakes and do not change host containers. All deployment PowerShell checks passed during setup, as did all 52 web tests and actionlint workflow validation. Registry security checks cover restrictive ACLs, exact credential encoding, and constrained cleanup. `test-Workflow.ps1` itself checks text contracts, not full YAML/expression syntax. The first live test deployment and host readiness checks passed as recorded above; production deployment and full business-flow testing remain separate checks. The workflow's execution-policy bypass is scoped to its script process; no machine-wide execution policy change is required.

Action pins were resolved from the official [checkout](https://github.com/actions/checkout) and [setup-dotnet](https://github.com/actions/setup-dotnet) v4 refs. Image publication uses the installed Docker CLI and its [build metadata output](https://docs.docker.com/reference/cli/docker/buildx/build/#metadata-file).
