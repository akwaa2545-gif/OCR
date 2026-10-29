param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$type = $assembly.GetType('OperatorTrainingRecord.frmUpdateSkill', $true)
$form = [Activator]::CreateInstance($type)
try {
    $flags = [Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic
    $selectedProcess = $type.GetField('ProcessSelect', $flags)
    $bindingProcesses = $type.GetField('bindingProcesses', $flags)
    $combo = $type.GetField('lblProcess', $flags).GetValue($form)

    $table = New-Object System.Data.DataTable
    [void]$table.Columns.Add('ProcessName', [string])
    [void]$table.Rows.Add('Appearance inspection of PSL B')
    [void]$table.Rows.Add('Terminal connection (2HD)')

    $selectedProcess.SetValue($form, 'Terminal connection (2HD)')
    $bindingProcesses.SetValue($form, $true)
    $combo.DisplayMember = 'ProcessName'
    $combo.ValueMember = 'ProcessName'
    $combo.DataSource = $table
    if ($selectedProcess.GetValue($form) -ne 'Terminal connection (2HD)') {
        throw 'Binding the dropdown replaced the checked process with its first item.'
    }

    $combo.SelectedValue = $selectedProcess.GetValue($form)
    $bindingProcesses.SetValue($form, $false)
    if ($combo.Text -ne 'Terminal connection (2HD)' -or
        $selectedProcess.GetValue($form) -ne $combo.SelectedValue) {
        throw 'The update form does not show the checked process.'
    }
    Write-Output 'PASS: dropdown binding preserves the selected skill, even when it is not the first item.'
} finally {
    $form.Dispose()
}
