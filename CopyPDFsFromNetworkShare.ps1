# Script to copy PDF certificates from network share to local uploads directory
# This allows the Docker container to access PDF files via bind mounts

param(
    [string]$SourcePath = "\\svr120a\Cert$",
    [string]$DestinationPath = "C:\code test\OperatorCertificationRecord\OperatorCertificationRecord.Web\wwwroot\uploads",
    [switch]$WhatIf
)

Write-Host "`n=== PDF Migration Tool ===" -ForegroundColor Cyan
Write-Host "Source: $SourcePath" -ForegroundColor Yellow
Write-Host "Destination: $DestinationPath" -ForegroundColor Yellow
Write-Host ""

# Check if source exists
if (-not (Test-Path $SourcePath)) {
    Write-Host "ERROR: Source path not accessible: $SourcePath" -ForegroundColor Red
    Write-Host "Please ensure you have access to the network share." -ForegroundColor Red
    exit 1
}

# Create destination if it doesn't exist
if (-not (Test-Path $DestinationPath)) {
    Write-Host "Creating destination directory: $DestinationPath" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
}

# Get all employee folders from network share
$employeeFolders = Get-ChildItem -Path $SourcePath -Directory -ErrorAction SilentlyContinue

if ($employeeFolders.Count -eq 0) {
    Write-Host "No employee folders found in $SourcePath" -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($employeeFolders.Count) employee folders" -ForegroundColor Green
Write-Host ""

$copiedCount = 0
$skippedCount = 0
$errorCount = 0

foreach ($empFolder in $employeeFolders) {
    $empCode = $empFolder.Name
    $sourceFolderPath = $empFolder.FullName
    $destFolderPath = Join-Path $DestinationPath $empCode
    
    # Get all PDF files in this employee folder
    $pdfFiles = Get-ChildItem -Path $sourceFolderPath -Filter "*.pdf" -File -ErrorAction SilentlyContinue
    
    if ($pdfFiles.Count -eq 0) {
        continue
    }
    
    # Create employee folder in destination if it doesn't exist
    if (-not (Test-Path $destFolderPath)) {
        if (-not $WhatIf) {
            New-Item -ItemType Directory -Path $destFolderPath -Force | Out-Null
        }
    }
    
    foreach ($pdfFile in $pdfFiles) {
        $destFilePath = Join-Path $destFolderPath $pdfFile.Name
        
        # Check if file already exists
        if (Test-Path $destFilePath) {
            $existingFile = Get-Item $destFilePath
            # Skip if same size (already copied)
            if ($existingFile.Length -eq $pdfFile.Length) {
                $skippedCount++
                Write-Host "  SKIP: $empCode\$($pdfFile.Name) (already exists)" -ForegroundColor DarkGray
                continue
            }
        }
        
        try {
            if ($WhatIf) {
                Write-Host "  WOULD COPY: $empCode\$($pdfFile.Name) ($([math]::Round($pdfFile.Length/1KB, 2)) KB)" -ForegroundColor Cyan
            } else {
                Copy-Item -Path $pdfFile.FullName -Destination $destFilePath -Force
                Write-Host "  COPIED: $empCode\$($pdfFile.Name) ($([math]::Round($pdfFile.Length/1KB, 2)) KB)" -ForegroundColor Green
            }
            $copiedCount++
        } catch {
            Write-Host "  ERROR: $empCode\$($pdfFile.Name) - $($_.Exception.Message)" -ForegroundColor Red
            $errorCount++
        }
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
if ($WhatIf) {
    Write-Host "Would copy: $copiedCount PDFs" -ForegroundColor Yellow
} else {
    Write-Host "Copied: $copiedCount PDFs" -ForegroundColor Green
}
Write-Host "Skipped: $skippedCount PDFs (already exist)" -ForegroundColor DarkGray
if ($errorCount -gt 0) {
    Write-Host "Errors: $errorCount PDFs" -ForegroundColor Red
}
Write-Host ""
Write-Host "TIP: Run with -WhatIf to preview changes without copying" -ForegroundColor Yellow
Write-Host "Example: .\CopyPDFsFromNetworkShare.ps1 -WhatIf" -ForegroundColor Yellow
