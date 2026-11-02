[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('test', 'prod')][string]$Environment,
    [Parameter(Mandatory = $true)][string]$ImageTag,
    [string]$ImageArchive = 'C:\temp\ocr-webimagetest.tar',
    [string]$ConfigRoot = 'C:\ocr-deploy',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Capture arguments before dot-sourcing the shared script's parameter scope.
$localImageOptions = @{
    Target = $Environment
    Release = $ImageTag
    Root = $ConfigRoot
    ImageArchive = $ImageArchive
    EnableSqlStorage = $true
    CheckOnly = $ValidateOnly
}
. (Join-Path $PSScriptRoot 'Deploy-Ocr.ps1')
Invoke-OcrDeployment @localImageOptions
