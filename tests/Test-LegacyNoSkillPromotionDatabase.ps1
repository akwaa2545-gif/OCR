param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [string[]]$EmployeeCodes = @()
)

$ErrorActionPreference = 'Stop'

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$type = $assembly.GetType('OperatorTrainingRecord.frmWelcome', $true)
$form = [Activator]::CreateInstance($type)
try {
    if ($EmployeeCodes.Count -eq 0) {
        $flags = [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic
        $connectionString = $type.GetField('strConnString', $flags).GetValue($form)
        $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
        $connection.Open()
        try {
            $command = $connection.CreateCommand()
            $command.CommandText = @"
SELECT DISTINCT EmpCode
FROM (
    SELECT EmpCode, Remark FROM tblQualified
    UNION ALL
    SELECT EmpCode, Remark FROM tblQualified_Obsoleted
) promotion
WHERE Remark LIKE '%PROMOTED%'
   OR LTRIM(RTRIM(Remark)) = 'PROMOTE TO BE OL1'
"@
            $reader = $command.ExecuteReader()
            $codes = New-Object System.Collections.Generic.List[string]
            while ($reader.Read()) {
                [void]$codes.Add($reader.GetString(0))
            }
            $reader.Close()
            $EmployeeCodes = $codes.ToArray()
        } finally {
            $connection.Close()
        }
    }

    if ($EmployeeCodes.Count -eq 0) {
        throw 'No promotion records were available for the database-backed test.'
    }

    $employees = New-Object System.Data.DataTable
    [void]$employees.Columns.Add('EmpCode', [string])
    foreach ($employeeCode in $EmployeeCodes) {
        [void]$employees.Rows.Add($employeeCode)
    }

    $method = $type.GetMethod(
        'ExcludePromotedEmployees',
        [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic
    )
    $arguments = New-Object 'object[]' 1
    $arguments[0] = $employees.PSObject.BaseObject
    [void]$method.Invoke($form, $arguments)

    if ($employees.Rows.Count -ne 0) {
        throw "$($employees.Rows.Count) promoted employee(s) remained after filtering."
    }

    Write-Output "PASS: database-backed promotion lookup excluded all $($EmployeeCodes.Count) promoted employees."
} finally {
    $form.Dispose()
}
