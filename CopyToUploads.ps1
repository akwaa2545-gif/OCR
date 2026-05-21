# Copy Photos and Certificates from Network Share to Local Uploads
# This script copies files from \\svr120a network shares to C:\uploads
# so Docker can serve them (Linux containers can't access Windows UNC paths)

param(
    [string]$Mode = "photos-only",  # Options: "photos-only", "certs-only", "all", "employee"
    [string]$EmployeeCode = ""       # Specific employee code (required for "employee" mode)
)

$PhotoSource = "\\svr120a\PhotoEmp$"
$CertSource = "\\svr120a\Cert$"
$LocalDest = "C:\uploads"

# Create destination if it doesn't exist
if (!(Test-Path $LocalDest)) {
    New-Item -ItemType Directory -Path $LocalDest -Force | Out-Null
    Write-Host "Created directory: $LocalDest" -ForegroundColor Green
}

function Copy-Photos {
    Write-Host "`nCopying photos from $PhotoSource to $LocalDest..." -ForegroundColor Cyan
    
    if (!(Test-Path $PhotoSource)) {
        Write-Host "ERROR: Photo source not accessible: $PhotoSource" -ForegroundColor Red
        return
    }

    $photoFiles = Get-ChildItem -Path $PhotoSource -Filter "*.jpg" -File
    $copied = 0
    $skipped = 0
    
    foreach ($photo in $photoFiles) {
        $destPath = Join-Path $LocalDest $photo.Name
        if (!(Test-Path $destPath)) {
            Copy-Item $photo.FullName -Destination $destPath -Force
            $copied++
            if ($copied % 100 -eq 0) {
                Write-Host "  Copied $copied photos..." -ForegroundColor Yellow
            }
        } else {
            $skipped++
        }
    }
    
    Write-Host "  Total photos copied: $copied" -ForegroundColor Green
    Write-Host "  Skipped (already exist): $skipped" -ForegroundColor Gray
    Write-Host "  Total size: $([Math]::Round((Get-ChildItem $LocalDest -Filter '*.jpg' | Measure-Object -Property Length -Sum).Sum / 1MB, 2)) MB" -ForegroundColor Cyan
}

function Copy-Certificates {
    param([string]$EmpCode = "")
    
    Write-Host "`nCopying certificates from $CertSource to $LocalDest..." -ForegroundColor Cyan
    
    if (!(Test-Path $CertSource)) {
        Write-Host "ERROR: Certificate source not accessible: $CertSource" -ForegroundColor Red
        return
    }

    if ($EmpCode) {
        # Copy for specific employee
        $empDir = Join-Path $CertSource $EmpCode
        if (!(Test-Path $empDir)) {
            Write-Host "  Employee directory not found: $empDir" -ForegroundColor Yellow
            return
        }
        
        $destDir = Join-Path $LocalDest $EmpCode
        if (!(Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        
        $certs = Get-ChildItem -Path $empDir -Filter "*.pdf" -File
        foreach ($cert in $certs) {
            $destPath = Join-Path $destDir $cert.Name
            Copy-Item $cert.FullName -Destination $destPath -Force
        }
        Write-Host "  Copied $($certs.Count) certificates for employee $EmpCode" -ForegroundColor Green
    } else {
        # Copy all employee certificates
        $empDirs = Get-ChildItem -Path $CertSource -Directory
        $totalCopied = 0
        $processedEmployees = 0
        
        foreach ($empDir in $empDirs) {
            $destDir = Join-Path $LocalDest $empDir.Name
            if (!(Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            
            $certs = Get-ChildItem -Path $empDir.FullName -Filter "*.pdf" -File -ErrorAction SilentlyContinue
            foreach ($cert in $certs) {
                $destPath = Join-Path $destDir $cert.Name
                if (!(Test-Path $destPath)) {
                    Copy-Item $cert.FullName -Destination $destPath -Force
                    $totalCopied++
                }
            }
            
            $processedEmployees++
            if ($processedEmployees % 50 -eq 0) {
                Write-Host "  Processed $processedEmployees employees, copied $totalCopied certificates..." -ForegroundColor Yellow
            }
        }
        
        Write-Host "  Total employees processed: $processedEmployees" -ForegroundColor Green
        Write-Host "  Total certificates copied: $totalCopied" -ForegroundColor Green
        Write-Host "  Total size: $([Math]::Round((Get-ChildItem $LocalDest -Filter '*.pdf' -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 2)) MB" -ForegroundColor Cyan
    }
}

# Execute based on mode
Write-Host "=== Copy Files to Docker Uploads Volume ===" -ForegroundColor Magenta
Write-Host "Mode: $Mode" -ForegroundColor Cyan

switch ($Mode.ToLower()) {
    "photos-only" {
        Copy-Photos
    }
    "certs-only" {
        Copy-Certificates
    }
    "all" {
        Copy-Photos
        Copy-Certificates
    }
    "employee" {
        if (!$EmployeeCode) {
            Write-Host "ERROR: EmployeeCode parameter required for 'employee' mode" -ForegroundColor Red
            Write-Host "Example: .\CopyToUploads.ps1 -Mode employee -EmployeeCode 0606543" -ForegroundColor Yellow
            exit 1
        }
        # Copy single employee photo
        $photoFile = Join-Path $PhotoSource "$EmployeeCode.jpg"
        if (Test-Path $photoFile) {
            Copy-Item $photoFile -Destination (Join-Path $LocalDest "$EmployeeCode.jpg") -Force
            Write-Host "  Copied photo for $EmployeeCode" -ForegroundColor Green
        }
        # Copy employee certificates
        Copy-Certificates -EmpCode $EmployeeCode
    }
    default {
        Write-Host "ERROR: Invalid mode '$Mode'" -ForegroundColor Red
        Write-Host "Valid modes: photos-only, certs-only, all, employee" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "`nDone! Files are now accessible by Docker container at C:\uploads" -ForegroundColor Green
