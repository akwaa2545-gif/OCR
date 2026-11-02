param([string]$BuildDirectory=(Join-Path $PSScriptRoot '..\OperatorCertificationRecord\bin\Release'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Data
$exe=Join-Path ([IO.Path]::GetFullPath($BuildDirectory)) 'OperatorTrainingRecord.exe'
$assembly=[Reflection.Assembly]::LoadFrom($exe)
$form=[Activator]::CreateInstance($assembly.GetType('OperatorTrainingRecord.frmOperatorTraining',$true))
$failures=New-Object 'System.Collections.Generic.List[string]'
try {
    foreach($gridName in @('gridCurrentSkill','gridDisqualified','gridObsoleted')) {
        $grid=$form.GetType().GetField($gridName,[Reflection.BindingFlags]'Instance,NonPublic').GetValue($form)
        $grid.Parent.Controls.Remove($grid)
        $hostForm=New-Object System.Windows.Forms.Form
        $hostForm.ShowInTaskbar=$false
        $hostForm.StartPosition=[System.Windows.Forms.FormStartPosition]::Manual
        $hostForm.Location=New-Object System.Drawing.Point(-32000,-32000)
        $hostForm.Controls.Add($grid)
        $script:formatErrors=New-Object 'System.Collections.Generic.List[string]'
        $errorHandler=[System.Windows.Forms.DataGridViewDataErrorEventHandler]{param($sender,$eventArgs)
            $script:formatErrors.Add($eventArgs.Exception.GetType().FullName)
            $eventArgs.ThrowException=$false
        }
        $grid.add_DataError($errorHandler)
        try {
            # Show only the empty test host, never the application form: no Load/SQL calls.
            $hostForm.Show()
            foreach($pass in @(1,2)) {
                $table=New-Object System.Data.DataTable
                try {
                    [void]$table.Columns.Add('Process',[string])
                    [void]$table.Columns.Add('OcrSkillRowId',[Guid])
                    [void]$table.Columns.Add('CertificateFileId',[Guid])
                    [void]$table.Columns.Add('FileStorageVersion',[byte[]])
                    $rowId=[Guid]::NewGuid();$fileId=[Guid]::NewGuid();$version=[byte[]](0,0,0,0,0,0,0,$pass)
                    [void]$table.Rows.Add('Test process',$rowId,$fileId,$version)
                    $grid.DataSource=$table
                    [System.Windows.Forms.Application]::DoEvents()
                    foreach($columnName in @('OcrSkillRowId','CertificateFileId','FileStorageVersion')) {
                        if(!$grid.Columns.Contains($columnName) -or $grid.Columns[$columnName].Visible) {
                            $failures.Add($gridName+': metadata not retained and hidden: '+$columnName)
                        }
                    }
                    if($grid.Rows[0].Cells['OcrSkillRowId'].Value -ne $rowId -or
                       $grid.Rows[0].Cells['CertificateFileId'].Value -ne $fileId -or
                       [Convert]::ToBase64String($grid.Rows[0].Cells['FileStorageVersion'].Value) -ne [Convert]::ToBase64String($version)) {
                        $failures.Add($gridName+': bound owner or concurrency metadata changed')
                    }
                    if($grid.Rows[0].Cells['Process'].FormattedValue -ne 'Test process') {
                        $failures.Add($gridName+': business column stopped rendering')
                    }
                    $grid.DataSource=$null
                } finally { $table.Dispose() }
            }
            if($script:formatErrors.Count) { $failures.Add($gridName+': DataError events='+$script:formatErrors.Count) }
            Write-Output ($gridName+': bind/rebind checked; DataError events='+$script:formatErrors.Count)
        } finally {
            $grid.remove_DataError($errorHandler)
            $hostForm.Close();$hostForm.Dispose()
        }
    }
} finally { $form.Dispose() }
if($failures.Count) { $failures | Write-Output; exit 1 }
Write-Output 'PASS: all three actual application grids bind/rebind rowversion metadata without image errors.'
