param([Parameter(Mandatory)][string]$ExecutablePath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$type = $assembly.GetType('OperatorTrainingRecord.frmOperatorTraining', $true)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
$dialogFactory = $type.GetMethod('CreateCertificateSaveDialog', $flags)
$copy = $type.GetMethod('SaveCertificateCopy', $flags)
if ($null -eq $dialogFactory -or $null -eq $copy) { throw 'Certificate Save dialog/copy helpers are missing.' }

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('ocr-certificate-save-test-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $source = Join-Path $testRoot 'training.pdf'
    $destination = Join-Path $testRoot 'saved.pdf'
    [IO.File]::WriteAllText($source, "%PDF-1.4`nfirst`n%%EOF")

    $dialog = $dialogFactory.Invoke($null, [object[]]@([string]$source))
    try {
        if ($dialog.FileName -ne 'training.pdf' -or !$dialog.InitialDirectory -or
            ![IO.Directory]::Exists($dialog.InitialDirectory) -or !$dialog.OverwritePrompt) {
            throw 'The dialog does not default to a usable PDF save location.'
        }
    } finally { $dialog.Dispose() }

    $copy.Invoke($null, [object[]]@([string]$source, [string]$destination, [bool]$false)) | Out-Null
    if ([IO.File]::ReadAllText($destination) -ne [IO.File]::ReadAllText($source)) { throw 'New PDF was not saved.' }

    [IO.File]::WriteAllText($source, "%PDF-1.4`nsecond`n%%EOF")
    try {
        $copy.Invoke($null, [object[]]@([string]$source, [string]$destination, [bool]$false)) | Out-Null
        throw 'Existing PDF was overwritten without confirmation.'
    } catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
        if ($cause -isnot [IO.IOException]) { throw }
    }
    if (![IO.File]::ReadAllText($destination).Contains('first')) { throw 'Existing PDF changed without confirmation.' }

    $copy.Invoke($null, [object[]]@([string]$source, [string]$destination, [bool]$true)) | Out-Null
    if (![IO.File]::ReadAllText($destination).Contains('second')) { throw 'Confirmed overwrite did not save the PDF.' }

    [IO.File]::WriteAllText($source, 'not a PDF')
    try {
        $copy.Invoke($null, [object[]]@([string]$source, [string]$destination, [bool]$true)) | Out-Null
        throw 'Non-PDF source was accepted.'
    } catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException) { $cause = $cause.InnerException }
        if ($cause -isnot [IO.IOException]) { throw }
    }
    if (![IO.File]::ReadAllText($destination).Contains('second')) { throw 'Invalid source damaged the saved PDF.' }
    if (@(Get-ChildItem -LiteralPath $testRoot -Filter '.ocr-certificate-*.tmp').Count -ne 0) { throw 'Staging file was not cleaned up.' }
    Write-Output 'PASS: Downloads Save dialog, new PDF, confirmed overwrite, rejected overwrite and invalid PDF.'
} finally {
    if ([IO.Directory]::Exists($testRoot)) { [IO.Directory]::Delete($testRoot, $true) }
}
