param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$type = $assembly.GetType('OperatorTrainingRecord.frmWelcome', $true)
$method = $type.GetMethod(
    'RemoveExcludedNoSkillRows',
    [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
)

$employees = New-Object System.Data.DataTable
[void]$employees.Columns.Add('EmpCode', [string])
[void]$employees.Columns.Add('JobGrade', [string])
[void]$employees.Rows.Add('100', '51T')
[void]$employees.Rows.Add('101', '52T')
[void]$employees.Rows.Add('102', '53T')
[void]$employees.Rows.Add('103', '54T')
[void]$employees.Rows.Add('104', '52T')
[void]$employees.Rows.Add('105', '')

$promotedType = [System.Collections.Generic.HashSet[string]]
$promoted = [Activator]::CreateInstance($promotedType, [System.StringComparer]::OrdinalIgnoreCase)
[void]$promoted.Add('101')

$arguments = New-Object 'object[]' 2
$arguments[0] = $employees.PSObject.BaseObject
$arguments[1] = $promoted.PSObject.BaseObject
[void]$method.Invoke($null, $arguments)

if ($employees.Rows.Count -ne 2 -or
    $employees.Rows[0].EmpCode -ne '100' -or
    $employees.Rows[1].EmpCode -ne '104') {
    throw 'Promoted employees or job grade 53T and above were included in the Operator No Skill export.'
}

Write-Output 'PASS: Operator No Skill includes only unpromoted 51T and 52T employees.'
