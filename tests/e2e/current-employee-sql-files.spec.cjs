/* Synthetic, localhost-only cross-app integration runner. Run with:
 * OCR_FILE_STORAGE_TEST_CONNECTION=<private process environment>
 * OCR_E2E_WEB_DLL=<already-built web dll> node tests/e2e/current-employee-sql-files.spec.cjs
 * Optional OCR_E2E_DESKTOP_DIR points to compiled net48 desktop test artifacts.
 * Never pass a connection string on the command line. Does not build or deploy applications.
 */
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const net = require('node:net');
const { spawn, spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
let playwright;
try { playwright = require('playwright'); }
catch { playwright = require(path.join(process.env.OCR_E2E_PLAYWRIGHT_ROOT || 'C:/code test/OperatorCertificationRecord/node_modules', 'playwright')); }
const actor = 'E2EFILE01', normal = 'E2EFILE02';
const base = 'http://127.0.0.1:5091';
const scratch = path.join(root, '.superpowers/sdd/CURRENT-EMPLOYEE-SQL-STORAGE-PLAN-2026-10-27/browser-e2e', `run-${new Date().toISOString().replace(/[:.]/g,'-')}`);
fs.mkdirSync(scratch, { recursive: true });
const webDll = path.resolve(process.env.OCR_E2E_WEB_DLL || path.join(root, '.superpowers/sdd/CURRENT-EMPLOYEE-SQL-STORAGE-PLAN-2026-10-27/build-web/bin/OperatorCertificationRecord.Web/debug/OperatorCertificationRecord.Web.dll'));
const desktopDir = path.resolve(process.env.OCR_E2E_DESKTOP_DIR || path.join(root, 'tests/OperatorCertificationRecord.Desktop.Tests/bin/Debug/net48'));
const password = crypto.randomBytes(8).toString('hex');
const helper = path.join(scratch, 'fixture.ps1');
fs.writeFileSync(helper, String.raw`
param([string]$Action,[string]$InputPath,[string]$RowId,[string]$FileId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Data
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($env:OCR_FILE_STORAGE_TEST_CONNECTION)
if($builder.DataSource -notmatch '^(127\.0\.0\.1|localhost|\.)(,|\\|$)' -or $builder.InitialCatalog -notlike 'OcrFileStorageTests_*'){throw 'Synthetic localhost fixture required'}
$connection=New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
$connection.Open()
function Command([string]$Sql){$q=$connection.CreateCommand();$q.CommandText=$Sql;return $q}
$guard=Command "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment' AND CONVERT(nvarchar(50),value)=N'SyntheticLocalFixture'"
if([int]$guard.ExecuteScalar() -ne 1){throw 'Synthetic fixture marker required'}
function Run([string]$Sql){$q=Command $Sql;try{[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}}
function Rows([string]$Sql){$q=Command $Sql;$r=$q.ExecuteReader();$list=@();try{while($r.Read()){$item=@{};for($i=0;$i -lt $r.FieldCount;$i++){$v=$r.GetValue($i);$item[$r.GetName($i)]=if($v -is [DBNull]){$null}else{$v}};$list+=,$item}}finally{$r.Dispose();$q.Dispose()};return $list}
if($Action -eq 'setup'){
 if([string]::IsNullOrWhiteSpace($env:OCR_E2E_FIXTURE_PASSWORD)){throw 'Fixture password required'}
 $setup=Command @'
IF OBJECT_ID('dbo.tblDepartment') IS NULL CREATE TABLE dbo.tblDepartment(DeptID nvarchar(10),DeptName nvarchar(100));
IF OBJECT_ID('dbo.tblSection') IS NULL CREATE TABLE dbo.tblSection(SectID nvarchar(10),SectName nvarchar(100),DeptID nvarchar(10));
IF OBJECT_ID('dbo.tblWorkshop') IS NULL CREATE TABLE dbo.tblWorkshop(WorkshopID nvarchar(10),WorkshopName nvarchar(100));
IF OBJECT_ID('dbo.tblProcess') IS NULL CREATE TABLE dbo.tblProcess(ProcessID nvarchar(10),ProcessName nvarchar(100),WorkshopID nvarchar(10));
IF OBJECT_ID('dbo.tblOperatorTraining') IS NULL CREATE TABLE dbo.tblOperatorTraining(OperatorTrainingID nvarchar(10),OperatorTrainingName nvarchar(100));
IF NOT EXISTS(SELECT 1 FROM dbo.tblDepartment WHERE DeptID=N'E2E') INSERT dbo.tblDepartment VALUES(N'E2E',N'QA');
IF NOT EXISTS(SELECT 1 FROM dbo.tblWorkshop WHERE WorkshopID=N'E2E') INSERT dbo.tblWorkshop VALUES(N'E2E',N'DAY');
IF EXISTS(SELECT 1 FROM dbo.tblEmployee WHERE EmpCode IN(N'E2EFILE01',N'E2EFILE02') AND ISNULL(PersonFnameEng,N'')<>N'SyntheticE2E') THROW 51000,'Fixture actor belongs to another test',1;
IF NOT EXISTS(SELECT 1 FROM dbo.tblEmployee WHERE EmpCode=N'E2EFILE01') INSERT dbo.tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,PersonFnameEng,PersonLnameEng,DeptID,WorkshopID,StatusWork,Photo) VALUES(N'E2EFILE01',@fixturePassword,'20200101',N'53T',N'SyntheticE2E',N'Admin',N'E2E',N'E2E',N'1',N'E2EFILE01.png');
IF NOT EXISTS(SELECT 1 FROM dbo.tblEmployee WHERE EmpCode=N'E2EFILE02') INSERT dbo.tblEmployee(EmpCode,EmpPassword,JoinDate,JobGrade,PersonFnameEng,PersonLnameEng,DeptID,WorkshopID,StatusWork,Photo) VALUES(N'E2EFILE02',@fixturePassword,'20200101',N'53T',N'SyntheticE2E',N'Normal',N'E2E',N'E2E',N'1',N'E2EFILE02.png');
UPDATE dbo.tblEmployee SET ResignDate=NULL,StatusWork=N'1',EmpPassword=@fixturePassword WHERE EmpCode IN(N'E2EFILE01',N'E2EFILE02') AND PersonFnameEng=N'SyntheticE2E';
'@
 try{
  [void]$setup.Parameters.Add('@fixturePassword',[System.Data.SqlDbType]::NVarChar,16)
  $setup.Parameters['@fixturePassword'].Value=$env:OCR_E2E_FIXTURE_PASSWORD
  [void]$setup.ExecuteNonQuery()
 }finally{$setup.Dispose()}
 # Reference-name fields in the synthetic legacy tables must use the employee key's collation.
 # Preserve their original type, length and nullability; no table/data/constraint replacement.
 $employeeCollation=@(Rows "SELECT collation_name Collation FROM sys.columns WHERE object_id=OBJECT_ID('dbo.tblEmployee') AND name=N'EmpCode'")[0].Collation
 if($employeeCollation -notmatch '^[A-Za-z0-9_]+$'){throw 'Invalid fixture key collation'}
 $references=@(Rows "SELECT OBJECT_NAME(object_id) TableName,name ColumnName,TYPE_NAME(system_type_id) TypeName,max_length MaxLength,is_nullable Nullable,collation_name Collation FROM sys.columns WHERE (object_id IN(OBJECT_ID('dbo.tblQualified'),OBJECT_ID('dbo.tblQualified_Obsoleted')) AND name=N'Verifier') OR (object_id=OBJECT_ID('dbo.tblEmployee') AND name IN(N'PersonFnameEng',N'PersonLnameEng'))")
 foreach($reference in $references){
  if($reference.Collation -eq $employeeCollation){continue}
  if($reference.TypeName -notin @('nvarchar','varchar','nchar','char')){throw 'Unexpected fixture verifier column type'}
  $length=if($reference.MaxLength -eq -1){'max'}elseif($reference.TypeName.StartsWith('n')){[string]($reference.MaxLength/2)}else{[string]$reference.MaxLength}
  $nullable=if($reference.Nullable){'NULL'}else{'NOT NULL'}
  Run ('ALTER TABLE dbo.['+$reference.TableName+'] ALTER COLUMN ['+$reference.ColumnName+'] '+$reference.TypeName+'('+$length+') COLLATE '+$employeeCollation+' '+$nullable)
 }
 $view=@(Rows "SELECT CAST(CASE WHEN OBJECT_ID('dbo.ViewEmpQualified_All',N'V') IS NULL THEN 0 ELSE 1 END AS bit) Present")[0].Present
 @{employeeReferenceCollation=$employeeCollation;optionalDashboardViewPresent=$view}|ConvertTo-Json -Compress
} elseif($Action -eq 'state'){
 $out=@{employee=@(Rows "SELECT CONVERT(nvarchar(36),PhotoFileId) FileId FROM dbo.tblEmployee WHERE EmpCode=N'E2EFILE01'");current=@(Rows "SELECT CONVERT(nvarchar(36),OcrSkillRowId) RowId,CONVERT(nvarchar(36),CertificateFileId) FileId FROM dbo.tblQualified WHERE EmpCode=N'E2EFILE01' AND ProcessName=N'SyntheticE2E'");obsolete=@(Rows "SELECT CONVERT(nvarchar(36),OcrSkillRowId) RowId,CONVERT(nvarchar(36),CertificateFileId) FileId FROM dbo.tblQualified_Obsoleted WHERE EmpCode=N'E2EFILE01' AND ProcessName=N'SyntheticE2E'");files=@(Rows "SELECT CONVERT(nvarchar(36),FileId) FileId,CONVERT(varchar(64),Sha256,2) Hash,ByteLength FROM dbo.tblEmployeeFile WHERE EmpCode=N'E2EFILE01'")};$out|ConvertTo-Json -Depth 5 -Compress
} elseif($Action -eq 'resign'){Run "UPDATE dbo.tblEmployee SET ResignDate=GETDATE(),StatusWork=N'0' WHERE EmpCode=N'E2EFILE01' AND PersonFnameEng=N'SyntheticE2E'"}
elseif($Action -eq 'restore'){Run "UPDATE dbo.tblEmployee SET ResignDate=NULL,StatusWork=N'1' WHERE EmpCode=N'E2EFILE01' AND PersonFnameEng=N'SyntheticE2E'"}
elseif($Action -eq 'lock-owner'){
 $transaction=$connection.BeginTransaction()
 try{$q=Command "UPDATE dbo.tblEmployee WITH (ROWLOCK) SET Notice=Notice WHERE EmpCode=N'E2EFILE01' AND PersonFnameEng=N'SyntheticE2E';SELECT @@ROWCOUNT";$q.Transaction=$transaction
  if($q.ExecuteScalar() -ne 1){throw 'Own fixture row not found'}
  Write-Output ('Own row data lock acquired '+[DateTime]::UtcNow.ToString('o'))
  [IO.File]::WriteAllText($InputPath+'.ready','locked');$deadline=[DateTime]::UtcNow.AddSeconds(55)
  $probe=[System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
  try{$probe.Open();$read=$probe.CreateCommand();$read.CommandTimeout=5;$read.CommandText="SET LOCK_TIMEOUT 1000;SELECT PhotoFileId FROM dbo.tblEmployee WHERE EmpCode=N'E2EFILE01'"
   try{[void]$read.ExecuteScalar();throw 'Fault fixture did not block an independent owner read'}
   catch {$sqlError=$_.Exception;while($sqlError -isnot [System.Data.SqlClient.SqlException] -and $null -ne $sqlError.InnerException){$sqlError=$sqlError.InnerException};if($sqlError -isnot [System.Data.SqlClient.SqlException] -or $sqlError.Number -ne 1222){throw};Write-Output 'Independent owner read blocked with SQL lock timeout 1222';[IO.File]::WriteAllText($InputPath+'.probe','blocked-1222')}
   finally{$read.Dispose()}
  }finally{$probe.Dispose()}
  while(-not [IO.File]::Exists($InputPath+'.release') -and [DateTime]::UtcNow -lt $deadline){[Threading.Thread]::Sleep(100)}
 }finally{$transaction.Rollback();$transaction.Dispose();Write-Output ('Own row transaction rolled back '+[DateTime]::UtcNow.ToString('o'))}
}
elseif($Action -like 'desktop-*'){
 Add-Type -AssemblyName System.Windows.Forms
 Add-Type -AssemblyName System.Drawing
 foreach($name in @('OperatorCertificationRecord.PhotoCompatibility.dll','OperatorCertificationRecord.FileStorage.dll','OperatorCertificationRecord.Desktop.Tests.dll')){[void][Reflection.Assembly]::LoadFrom((Join-Path $env:OCR_E2E_DESKTOP_DIR $name))}
 $factory=[Func[System.Data.SqlClient.SqlConnection]]{New-Object System.Data.SqlClient.SqlConnection($env:OCR_FILE_STORAGE_TEST_CONNECTION)}
 $repo=[OperatorCertificationRecord.FileStorage.SqlEmployeeFileRepository]::new($factory)
 $options=[OperatorCertificationRecord.FileStorage.FileStorageOptions]::new($true,$true,[string[]]@('E2EFILE01'),5242880,67108864)
 $svc=[OperatorTrainingRecord.DesktopFileService]::new($repo,$factory,$options)
 if($Action -eq 'desktop-upload-photo'){if(-not $svc.TrySavePhoto('E2EFILE01',$InputPath,$null,$false)){throw 'Desktop SQL writer not selected'}}
 elseif($Action -eq 'desktop-upload-pdf'){
  $script:uploadedRow=[Guid]::Empty
  $mutation=[Action[System.Data.SqlClient.SqlConnection,System.Data.SqlClient.SqlTransaction,Guid]]{
   param($c,$t,$row)
   $script:uploadedRow=$row
   $q=[System.Data.SqlClient.SqlCommand]::new("INSERT dbo.tblQualified(EmpCode,OcrSkillRowId,ProcessName,Download) VALUES(N'E2EFILE01',@row,N'DesktopE2E-'+LEFT(CONVERT(nvarchar(36),@row),8),N'desktop-legacy.pdf')",$c,$t)
   try{[void]$q.Parameters.Add('@row',[System.Data.SqlDbType]::UniqueIdentifier);$q.Parameters['@row'].Value=$row;[void]$q.ExecuteNonQuery()}finally{$q.Dispose()}
  }
  if(-not $svc.TrySaveCertificate('E2EFILE01',$null,$InputPath,$mutation)){throw 'Desktop SQL certificate writer not selected'}
  $owner=[OperatorCertificationRecord.FileStorage.FileOwner]::new([OperatorCertificationRecord.FileStorage.FileOwnerTable]::Qualified,'E2EFILE01',$script:uploadedRow)
  $saved=$svc.GetOwner($owner)
  @{RowId=$script:uploadedRow.ToString();FileId=$saved.FileId.ToString()}|ConvertTo-Json -Compress
 }
 elseif($Action -eq 'desktop-read-photo'){$box=New-Object System.Windows.Forms.PictureBox;try{$svc.LoadPhoto($box,'E2EFILE01','intentionally-missing-legacy.png');if($null -eq $box.Image){throw 'Desktop image missing'};@{width=$box.Image.Width;height=$box.Image.Height}|ConvertTo-Json -Compress}finally{$box.Dispose()}}
 elseif($Action -eq 'desktop-read-pdf'){$owner=[OperatorCertificationRecord.FileStorage.FileOwner]::new([OperatorCertificationRecord.FileStorage.FileOwnerTable]::Qualified,'E2EFILE01',[Guid]$RowId);$svc.CopyCertificate($owner,'intentionally-missing-legacy.pdf',$InputPath,[Guid]$FileId);(Get-FileHash -LiteralPath $InputPath -Algorithm SHA256).Hash|ConvertTo-Json -Compress}
}
$connection.Dispose()
`);
const helperEnv = { ...process.env, OCR_E2E_DESKTOP_DIR: desktopDir, OCR_E2E_FIXTURE_PASSWORD: password };
function fixture(action, extras = []) {
 const p = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File', helper, '-Action', action, ...extras], { env: helperEnv, encoding: 'utf8', timeout: 90000, windowsHide: true });
 if (p.status !== 0) throw new Error(`Fixture ${action} failed: ${(p.stderr || p.error?.message || 'unknown error').slice(0, 1800)}`);
 return p.stdout.trim() ? JSON.parse(p.stdout.trim()) : undefined;
}
const hash = b => crypto.createHash('sha256').update(b).digest('hex').toUpperCase();
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNxkAAAAASUVORK5CYII=', 'base64');
const desktopPhoto = Buffer.from('Qk06AAAAAAAAADYAAAAoAAAAAQAAAAEAAAABABgAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAD/AA==','base64');
const pdf = Buffer.from('%PDF-1.4\n% synthetic web cross-app fixture v1\n%%EOF\n');
const replacement = Buffer.from('%PDF-1.4\n% synthetic web cross-app fixture v2\n%%EOF\n');
let checks = [];
let artifactWarnings=[];
let fixtureInfo;
const startedAt = Date.now();
const escape = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
function report(error) {
 const result = { mode:process.env.OCR_E2E_ONLY_FAULT==='1'?'fault-only':'full',checks, artifactWarnings,fixture:fixtureInfo, failure: error?.message, durationSeconds: (Date.now()-startedAt)/1000, completedAt: new Date().toISOString() };
 fs.writeFileSync(path.join(scratch,'result.json'),JSON.stringify(result,null,2));
 const testcases=checks.map(name=>`<testcase name="${escape(name)}"/>`).join('');
 const failure=error?`<testcase name="incomplete journey"><failure message="${escape(error.message)}"/></testcase>`:'';
 fs.writeFileSync(path.join(scratch,'junit.xml'),`<?xml version="1.0" encoding="UTF-8"?><testsuite name="current employee SQL files" tests="${checks.length+(error?1:0)}" failures="${error?1:0}" time="${result.durationSeconds}">${testcases}${failure}</testsuite>`);
 const fixtureNote=fixtureInfo?.optionalDashboardViewPresent===false?'<p>Fixture limitation: optional ViewEmpQualified_All dashboard-cache view is absent. Authentication, Settings and all asserted file routes use the real application and synthetic SQL records.</p>':'';
 fs.writeFileSync(path.join(scratch,'report.html'),`<!doctype html><html lang="en"><meta charset="utf-8"><title>SQL file cross-app fixture results</title><h1>${error?'Failed':'Passed'} synthetic cross-app journey</h1><p>${checks.length} checks passed. ${escape(result.completedAt)}</p>${fixtureNote}<ul>${checks.map(name=>`<li>PASS ${escape(name)}</li>`).join('')}</ul>${error?`<p>FAIL ${escape(error.message)}</p>`:''}</html>`);
 console.log(`Artifacts: ${scratch}`);
}
async function check(name, work) { try { await work(); } catch(error) { throw new Error(`${name}: ${error.message}`); } checks=[...checks,name]; console.log(`PASS ${name}`); }
async function login(context, employee) {
 const page = await context.newPage(); await page.goto(`${base}/Login`);
 await page.getByLabel('Employee ID',{exact:true}).fill(employee); await page.getByLabel('Password',{exact:true}).fill(password);
 await Promise.all([page.waitForURL(/Dashboard/), page.locator('button[type=submit]').click()]);
 return page;
}
async function bytes(context, route, expected) {
 const response = await context.request.get(base + route);
 assert.equal(response.status(), 200, route); assert.match(response.headers()['cache-control'], /no-store/);
 assert.equal(hash(await response.body()), hash(expected), route);
}
async function readFailureCheck(admin,legacyRoot){
 const id=fixture('state').employee[0].FileId;assert.ok(id,'Existing synthetic SQL photo is required for fault probe');
 const signal=path.join(scratch,`rowlock-${crypto.randomUUID()}`);
 fs.writeFileSync(path.join(legacyRoot,actor+'.png'),png);
 const lock=spawn('powershell.exe',['-NoProfile','-NonInteractive','-File',helper,'-Action','lock-owner','-InputPath',signal],{env:helperEnv,windowsHide:true,timeout:110000});
 let lockDetails='';for(const stream of [lock.stdout,lock.stderr])stream.on('data',chunk=>{lockDetails=(lockDetails+chunk.toString()).slice(-4000);});
 let lockError;lock.once('error',error=>{lockError=error;});
 try{
  for(let i=0;!fs.existsSync(signal+'.ready')||!fs.existsSync(signal+'.probe');i++){if(lockError)throw new Error(`Own-row lock launch failed: ${lockError.message}`);if(i>=300||lock.exitCode!==null)throw new Error(`Own-row lock failed to become ready (exit ${lock.exitCode}): ${lockDetails}`);await new Promise(r=>setTimeout(r,100));}
  const response=await admin.request.get(`${base}/api/photo/employee/${actor}/${id}`,{timeout:45000});assert.equal(response.status(),503);assert.equal(await response.text(),'Photo storage is unavailable.');
 }finally{fs.writeFileSync(signal+'.release','release');if(lock.pid&&lock.exitCode===null&&lock.signalCode===null)await new Promise(r=>lock.once('exit',r));fs.writeFileSync(path.join(scratch,'rowlock.log'),`exit=${lock.exitCode}\n${lockDetails}`);}
 assert.equal(lock.exitCode,0,lockDetails);
 await bytes(admin,`/api/photo/employee/${actor}/${id}`,desktopPhoto);
}
async function main() {
 assert.ok(process.env.OCR_FILE_STORAGE_TEST_CONNECTION, 'Private process environment OCR_FILE_STORAGE_TEST_CONNECTION is required');
 assert.ok(fs.existsSync(webDll), 'Build web artifact first');
 await new Promise((resolve,reject)=>{const probe=net.createServer();probe.once('error',()=>reject(new Error('Local E2E port 5091 must be unused')));probe.listen(5091,'127.0.0.1',()=>probe.close(resolve));});
 fixtureInfo=fixture('setup');
 const legacyRoot = path.join(scratch, 'legacy'); fs.mkdirSync(path.join(legacyRoot, normal), { recursive: true });
 fs.writeFileSync(path.join(legacyRoot, normal + '.png'), png); fs.writeFileSync(path.join(legacyRoot, normal, 'legacy.pdf'), pdf);
 const web = spawn('dotnet', [webDll, '--urls', base, '--contentRoot', path.join(root, 'OperatorCertificationRecord.Web')], {
  cwd: scratch, windowsHide: true, env: { ...process.env, ConnectionStrings__DefaultConnection: process.env.OCR_FILE_STORAGE_TEST_CONNECTION,
   ASPNETCORE_ENVIRONMENT: 'Development', FileStorage__ReadSqlEnabled: 'true', FileStorage__WriteSqlEnabled: 'true', FileStorage__PilotEmployeeCodes__0: actor,
   Admins__0: actor, PhotoStorage__UploadRoot: legacyRoot, PhotoStorage__LegacyMirrorRoot: legacyRoot, LocalUploadSubfolder: legacyRoot }
 });
 let webError;web.once('error',error=>{webError=error;});
 const log = fs.createWriteStream(path.join(scratch, 'web.log')); web.stdout.pipe(log); web.stderr.pipe(log);
 let browser, admin, user, anonymous;
 try {
  for (let i=0;i<120;i++) { try { if ((await fetch(base+'/Login')).ok) break; } catch {} if(webError)throw new Error(`Web launch failed: ${webError.message}`);if (web.exitCode !== null) throw new Error('Web exited before readiness'); await new Promise(r=>setTimeout(r,500)); if(i===119) throw new Error('Web did not become ready'); }
  browser = await playwright.chromium.launch({ channel: 'msedge', headless: true });
  admin = await browser.newContext(); user = await browser.newContext(); anonymous = await browser.newContext();
  for(const context of [admin,user,anonymous])await context.route('**/*',route=>new URL(route.request().url()).origin===base?route.continue():route.abort());
  await admin.tracing.start({ screenshots: true, snapshots: true, sources: false });
  const page = await login(admin, actor);
  await check('browser authenticated admin settings', async()=>{ await page.goto(base+'/Settings'); await page.locator('#stgPhotoForm').waitFor({ state:'attached' }); await page.screenshot({ path:path.join(scratch,'settings-before.png'), fullPage:true }); });
  if(process.env.OCR_E2E_ONLY_FAULT==='1'){await check('SQL owner read timeout returns 503 without legacy fallback',()=>readFailureCheck(admin,legacyRoot));return;}
  await check('web photo upload -> SQL bytes -> desktop image',async()=>{
   await page.locator('#stgPhotoFile').setInputFiles({name:'synthetic.png',mimeType:'image/png',buffer:png});
   await Promise.all([page.waitForNavigation(),page.locator('#stgPhotoForm').evaluate(f=>f.submit())]);
   assert.match(await page.textContent('body'),/Profile picture updated/);
   const state=fixture('state'); const id=state.employee[0].FileId; assert.ok(id); assert.ok(state.files.some(f=>f.FileId===id&&f.Hash===hash(png)));
   await bytes(admin,`/api/photo/employee/${actor}/${id}`,png); assert.equal(fixture('desktop-read-photo').width,1);
  });
  async function uploadCertificate(content){
   await page.goto(base+'/Settings'); const token=await page.locator('input[name=__RequestVerificationToken]').first().inputValue();
   const result=await admin.request.post(base+'/AddSkill',{maxRedirects:0,multipart:{__RequestVerificationToken:token,EmpCode:actor,Skill_WorkshopID:'E2E',Skill_ProcessName:'SyntheticE2E',Skill_OperatorTraining:'SyntheticE2E',Skill_CertifiedDate:'2026-10-27',Skill_ExpiryDate:'2027-10-27',Skill_FullScore:'100',Skill_ActualScore:'90',Skill_KnowledgeScore:'30',Skill_SkillScore:'60',Skill_TestResult:'Pass',SkillFile:{name:'synthetic.pdf',mimeType:'application/pdf',buffer:content}}});
   assert.equal(result.status(),302,`AddSkill HTTP ${result.status()}`); assert.match(result.headers().location,/ViewUser/);const state=fixture('state'); const row=state.current.at(-1); assert.ok(row?.FileId,'AddSkill did not publish SQL certificate'); assert.ok(state.files.some(f=>f.FileId===row.FileId&&f.Hash===hash(content))); return row;
  }
  let first, current;
  await check('web PDF upload -> SQL download -> desktop hash',async()=>{first=await uploadCertificate(pdf);await bytes(admin,`/api/certificate/owner/${actor}/${first.RowId}/${first.FileId}`,pdf);assert.equal(fixture('desktop-read-pdf',['-RowId',first.RowId,'-FileId',first.FileId,'-InputPath',path.join(scratch,'desktop.pdf')]),hash(pdf));});
  await check('replacement retains obsolete certificate bytes',async()=>{current=await uploadCertificate(replacement);assert.notEqual(first.RowId,current.RowId);assert.notEqual(first.FileId,current.FileId);assert.ok(fixture('state').obsolete.some(r=>r.RowId===first.RowId&&r.FileId===first.FileId));await bytes(admin,`/api/certificate/owner/${actor}/${first.RowId}/${first.FileId}?obsolete=true`,pdf);await bytes(admin,`/api/certificate/owner/${actor}/${current.RowId}/${current.FileId}`,replacement);});
  await check('Razor certificate download entry redirects to SQL bytes',async()=>{await bytes(admin,`/ViewUser?handler=DownloadFile&empCode=${actor}&process=SyntheticE2E`,replacement);});
  await check('desktop BMP upload -> web photo download',async()=>{const file=path.join(scratch,'desktop-photo.bmp');fs.writeFileSync(file,desktopPhoto);const before=fixture('state').employee[0].FileId;fixture('desktop-upload-photo',['-InputPath',file]);const id=fixture('state').employee[0].FileId;assert.notEqual(id,before);await bytes(admin,`/api/photo/employee/${actor}/${id}`,desktopPhoto);});
  await check('active browser avatars follow desktop photo replacement',async()=>{await page.goto(base+'/Settings');const sources=await page.locator('img[src*="/api/photo/"]').evaluateAll(images=>images.map(image=>image.getAttribute('src')));assert.ok(sources.length>0);for(const source of sources){const url=new URL(source,base);assert.equal(url.origin,base);await bytes(admin,url.pathname+url.search,desktopPhoto);}});
  await check('desktop PDF upload -> SQL bytes -> web certificate download',async()=>{const content=Buffer.from('%PDF-1.4\n% synthetic desktop cross-app upload\n%%EOF\n');const file=path.join(scratch,'desktop-upload.pdf');fs.writeFileSync(file,content);const saved=fixture('desktop-upload-pdf',['-InputPath',file]);assert.ok(saved.RowId&&saved.FileId);assert.ok(fixture('state').files.some(f=>f.FileId.toLowerCase()===saved.FileId.toLowerCase()&&f.Hash===hash(content)));await bytes(admin,`/api/certificate/owner/${actor}/${saved.RowId}/${saved.FileId}`,content);});
  await check('anonymous, nonadmin and forged-owner denials',async()=>{const id=fixture('state').employee[0].FileId;assert.equal((await anonymous.request.get(`${base}/api/photo/employee/${actor}/${id}`)).status(),401);assert.equal((await anonymous.request.get(`${base}/api/certificate/owner/${actor}/${current.RowId}/${current.FileId}`)).status(),403);await login(user,normal);assert.equal((await user.request.get(`${base}/api/certificate/owner/${actor}/${current.RowId}/${current.FileId}`)).status(),403);assert.equal((await admin.request.get(`${base}/api/photo/employee/${normal}/${id}`)).status(),404);assert.equal((await admin.request.get(`${base}/api/certificate/owner/${normal}/${current.RowId}/${current.FileId}`)).status(),404);});
  await check('same browser admin-to-user login removes certificate permission',async()=>{await login(admin,normal);for(const route of [`/api/certificate/owner/${actor}/${current.RowId}/${current.FileId}`,`/api/certificate/owner/${actor}/${first.RowId}/${first.FileId}?obsolete=true`,`/api/certificate/${normal}/legacy.pdf`,`/ViewUser?handler=DownloadFile&empCode=${actor}&process=SyntheticE2E`])assert.equal((await admin.request.get(base+route)).status(),403,route);await login(admin,actor);});
  await check('legacy photo and certificate fallback remain readable',async()=>{await bytes(admin,`/api/photo/${normal}.png`,png);const r=await admin.request.get(`${base}/api/certificate/${normal}/legacy.pdf`);assert.equal(r.status(),200);assert.equal(hash(await r.body()),hash(pdf));});
  await check('later resignation retains SQL photo and certificate reads',async()=>{fixture('resign');try{await bytes(admin,`/api/photo/employee/${actor}/${fixture('state').employee[0].FileId}`,desktopPhoto);await bytes(admin,`/api/certificate/owner/${actor}/${first.RowId}/${first.FileId}?obsolete=true`,pdf);}finally{fixture('restore');}});
  await check('SQL owner read timeout returns 503 without legacy fallback',()=>readFailureCheck(admin,legacyRoot));
  try{await page.screenshot({path:path.join(scratch,'settings-sql-after.png'),fullPage:true,timeout:5000});}
  catch(error){const warning=`Optional final screenshot was not saved: ${error.message}`;artifactWarnings=[...artifactWarnings,warning];console.warn(warning);}
 } finally {
  try{try{if(admin)await admin.tracing.stop({path:path.join(scratch,'trace.zip')});}finally{if(browser)await browser.close();}}
  finally{if(web.pid&&web.exitCode===null&&web.signalCode===null){web.kill();await new Promise(resolve=>web.once('exit',resolve));}log.end();}
 }
}
main().then(()=>report()).catch(error=>{report(error);console.error(error.message);process.exitCode=1;});
