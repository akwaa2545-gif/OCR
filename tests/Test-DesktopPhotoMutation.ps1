$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\OperatorCertificationRecord\DesktopPhotoCommand.cs'
Add-Type -Path $source -ReferencedAssemblies System.Data
$connection = [System.Data.SqlClient.SqlConnection]::new()
$original = [System.Data.SqlClient.SqlCommand]::new('INSERT INTO tblEmployee(EmpCode,Photo) VALUES(@sEmpCode,@sPhoto)')
[void]$original.Parameters.Add('@sEmpCode', [System.Data.SqlDbType]::Int)
$original.Parameters['@sEmpCode'].Value = '0001600'
[void]$original.Parameters.Add('@sPhoto', [System.Data.SqlDbType]::NVarChar, 50)
$original.Parameters['@sPhoto'].Value = '\\svr120a\PhotoEmp$\0001600.jpg'
$copy = [OperatorTrainingRecord.DesktopPhotoCommand]::ForTransaction($original, $connection, $null, $true)
try {
    if (![object]::ReferenceEquals($copy.Connection, $connection)) { throw 'Mutation must use the supplied transaction connection.' }
    if ($copy.Parameters['@sEmpCode'].SqlDbType -ne 'NVarChar' -or $copy.Parameters['@sEmpCode'].Size -ne 10) { throw 'Employee codes require NVarChar(10).' }
    if ($copy.Parameters['@sEmpCode'].Value -cne '0001600') { throw 'Leading zeros were lost.' }
    if ($copy.Parameters['@sPhoto'].Value -isnot [DBNull]) { throw 'A new SQL-only photo must not invent a physical legacy path.' }
    if ($original.Parameters['@sPhoto'].Value -cne '\\svr120a\PhotoEmp$\0001600.jpg') { throw 'The original legacy command was modified.' }
    if ([object]::ReferenceEquals($original.Parameters[0], $copy.Parameters[0])) { throw 'Parameters must be copied, not shared across commands.' }
} finally { $copy.Dispose(); $original.Dispose(); $connection.Dispose() }
$connection = [System.Data.SqlClient.SqlConnection]::new()
$employeeCode = "'OR'1'='1"
$photo = "photo' with quote.jpg"
$update = [OperatorTrainingRecord.DesktopPhotoCommand]::ForLegacyPhotoUpdate($employeeCode, $photo, $connection, $null)
try {
    if ($update.CommandText.Contains($employeeCode) -or $update.CommandText.Contains($photo)) { throw 'Untrusted values must not enter SQL command text.' }
    if ($update.Parameters['@employeeCode'].Value -cne $employeeCode -or $update.Parameters['@employeeCode'].SqlDbType -ne 'NVarChar' -or $update.Parameters['@employeeCode'].Size -ne 10) { throw 'The exact scoped employee must be a text parameter.' }
    if ($update.Parameters['@legacyPhoto'].Value -cne $photo) { throw 'The photo value must be a parameter.' }
    if (![object]::ReferenceEquals($update.Connection, $connection)) { throw 'Legacy photo update must use the locked connection.' }
} finally { $update.Dispose(); $connection.Dispose() }
$first = [OperatorTrainingRecord.DesktopPhotoCommand]::CreateLegacyPhotoPath('\\svr120a\PhotoEmp$\', '0001600', '.jpg')
$second = [OperatorTrainingRecord.DesktopPhotoCommand]::CreateLegacyPhotoPath('\\svr120a\PhotoEmp$\', '0001600', '.jpg')
if ($first.Length -gt 50 -or $first -eq $second -or [IO.Path]::GetFileName($first) -notlike '0001600-*.jpg') { throw 'Legacy photo paths must be unique, employee scoped and fit the existing column.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ocr-photo-copy-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDirectory)
try {
    $source = Join-Path $testDirectory 'photo.jpg'
    $existing = Join-Path $testDirectory 'other-employee.jpg'
    $created = Join-Path $testDirectory 'new-photo.jpg'
    [IO.File]::WriteAllText($source, 'new employee photo')
    [IO.File]::WriteAllText($existing, 'existing other employee photo')
    try {
        [OperatorTrainingRecord.DesktopPhotoCommand]::CopyNewLegacyPhoto($source, $existing)
        throw 'Existing attachment was overwritten.'
    } catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        if ($cause -isnot [IO.IOException]) { throw }
    }
    if ([IO.File]::ReadAllText($existing) -cne 'existing other employee photo') { throw 'Another employee photo changed.' }
    [OperatorTrainingRecord.DesktopPhotoCommand]::CopyNewLegacyPhoto($source, $created)
    if ([IO.File]::ReadAllText($created) -cne 'new employee photo') { throw 'New photo content was not preserved.' }
} finally {
    foreach ($ownedFile in @($source, $existing, $created)) {
        if ($ownedFile -and [IO.File]::Exists($ownedFile)) { [IO.File]::Delete($ownedFile) }
    }
    [IO.Directory]::Delete($testDirectory, $false)
}
Write-Output 'PASS: Desktop photo mutation keeps transaction scope, parameterized identity and collision-safe legacy copies.'
