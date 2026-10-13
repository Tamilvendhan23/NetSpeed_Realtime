<#
.SYNOPSIS
    Builds and publishes NetSpeed Monitor for Windows 11 x64.
.DESCRIPTION
    Compiles the project as a self-contained, single-file executable using the .NET 8 SDK.
    Supports optional framework-dependent build if requested via -FrameworkDependent.
.PARAMETER FrameworkDependent
    If specified, creates a lightweight framework-dependent build requiring .NET 8 Desktop Runtime.
#>
[CmdletBinding()]
param(
    [switch]$FrameworkDependent = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "           NetSpeed Monitor - Release Build Script (PowerShell)      " -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Check for dotnet CLI
try {
    $dotnetCmd = Get-Command "dotnet" -ErrorAction Stop
    $sdkVersion = (dotnet --version).Trim()
    Write-Host "[1/4] Found .NET SDK: $sdkVersion ($($dotnetCmd.Source))" -ForegroundColor Green
}
catch {
    Write-Host "[ERROR] .NET SDK is not installed or not in system PATH." -ForegroundColor Red
    Write-Host ""
    Write-Host "Please download and install the official .NET 8.0 SDK:" -ForegroundColor Yellow
    Write-Host "--> https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Press Enter to exit..."
    exit 1
}

$publishDir = Join-Path $PSScriptRoot "publish"
$projectFile = Join-Path $PSScriptRoot "NetSpeedMonitor.csproj"

if (-not (Test-Path $projectFile)) {
    Write-Host "[ERROR] Project file not found: $projectFile" -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit 1
}

# 2. Clean previous publish folder
if (Test-Path $publishDir) {
    Write-Host "[2/4] Cleaning previous publish directory..." -ForegroundColor Gray
    Remove-Item -Path $publishDir -Recurse -Force
}

# 3. Publish
$isSelfContained = (-not $FrameworkDependent)
$modeDescription = if ($isSelfContained) { "Self-Contained Single-File (win-x64)" } else { "Framework-Dependent" }

Write-Host "[3/4] Publishing $modeDescription..." -ForegroundColor Cyan

$publishArgs = @(
    "publish",
    $projectFile,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", ($isSelfContained.ToString().ToLower()),
    "-p:PublishSingleFile=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-o", $publishDir
)

Write-Host "Executing: dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet $publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "=====================================================================" -ForegroundColor Red
    Write-Host "[BUILD FAILED] Compilation failed with exit code $LASTEXITCODE." -ForegroundColor Red
    Write-Host "=====================================================================" -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit $LASTEXITCODE
}

# 4. Verification
$outputExe = Join-Path $publishDir "NetSpeedMonitor.exe"
if (-not (Test-Path $outputExe)) {
    Write-Host ""
    Write-Host "[VERIFICATION FAILED] Expected output executable not found: $outputExe" -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit 2
}

$fileInfo = Get-Item $outputExe
$fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)

if ($fileInfo.Length -eq 0) {
    Write-Host "[VERIFICATION FAILED] Output executable is 0 bytes." -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit 3
}

Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "                    BUILD SUCCESSFUL!                                " -ForegroundColor Green
Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "Executable Path    : $($fileInfo.FullName)" -ForegroundColor White
Write-Host "File Size          : $fileSizeMB MB ($($fileInfo.Length.ToString('N0')) bytes)" -ForegroundColor White
Write-Host "Deployment Type    : $modeDescription" -ForegroundColor White
Write-Host "Target OS          : Windows 11 / 10 x64" -ForegroundColor White
Write-Host "DPI Awareness      : PerMonitorV2 (Optimized for Acer Swift OLED displays)" -ForegroundColor White
Write-Host "=====================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Ready to run! Double click NetSpeedMonitor.exe or move it to your desired folder." -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to finish..."
