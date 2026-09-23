[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [string]$Source = 'C:\ocr-uploads',

    [Parameter()]
    [string]$Destination = '\\svr120a\PhotoEmp$'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Photo source directory does not exist: $Source"
}

if (-not (Test-Path -LiteralPath $Destination -PathType Container)) {
    throw "Legacy photo directory does not exist: $Destination"
}

$photos = Get-ChildItem -LiteralPath $Source -File |
    Where-Object { $_.Extension -match '^\.(jpg|jpeg|png)$' } |
    ForEach-Object {
        if ($_.BaseName -match '^([A-Za-z0-9-]{1,32})_\d+$') {
            [PSCustomObject]@{
                EmployeeCode = $Matches[1]
                File = $_
            }
        }
    }

$photos |
    Group-Object EmployeeCode |
    ForEach-Object {
        $latest = $_.Group |
            Sort-Object { $_.File.LastWriteTimeUtc } -Descending |
            Select-Object -First 1

        $sourceFile = $latest.File
        $targetName = $latest.EmployeeCode + $sourceFile.Extension.ToLowerInvariant()
        $targetPath = Join-Path -Path $Destination -ChildPath $targetName

        if ($PSCmdlet.ShouldProcess($targetPath, "Copy $($sourceFile.FullName)")) {
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $targetPath -Force
        }
    }
