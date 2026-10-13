@echo off
setlocal enabledelayedexpansion
title NetSpeed Monitor - Build Script (.NET 8 Windows x64)

echo =====================================================================
echo           NetSpeed Monitor - Release Build Script (Windows 11 x64)
echo =====================================================================
echo.

:: 1. Verify that dotnet CLI is installed
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERROR] The .NET SDK was not found in your system PATH.
    echo.
    echo Please install the official Microsoft .NET 8.0 SDK:
    echo --^> https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo After installing, restart your terminal and re-run build.bat.
    echo =====================================================================
    pause
    exit /b 1
)

:: Check dotnet SDK version
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set DOTNET_VER=%%v
echo [1/4] Found .NET SDK version: !DOTNET_VER!
echo.

:: 2. Clean previous publish folder if present
if exist ".\publish" (
    echo [2/4] Cleaning previous publish artifacts...
    rmdir /s /q ".\publish" >nul 2>&1
)

:: 3. Restore and Publish Self-Contained Win-x64 Single File
echo [3/4] Publishing self-contained single-file executable for win-x64...
echo       Command: dotnet publish NetSpeedMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o .\publish
echo.

dotnet publish NetSpeedMonitor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o .\publish

if %ERRORLEVEL% neq 0 (
    echo.
    echo =====================================================================
    echo [BUILD FAILED] Dotnet publish exited with error code %ERRORLEVEL%.
    echo Please review the compiler error messages printed above.
    echo =====================================================================
    pause
    exit /b %ERRORLEVEL%
)

:: 4. Verify that final EXE exists and is non-empty
set "OUTPUT_EXE=.\publish\NetSpeedMonitor.exe"

if not exist "!OUTPUT_EXE!" (
    echo.
    echo =====================================================================
    echo [VERIFICATION FAILED] Expected output file not found:
    echo !OUTPUT_EXE!
    echo =====================================================================
    pause
    exit /b 2
)

:: Get file size in bytes and calculate approximate MB
for %%I in ("!OUTPUT_EXE!") do (
    set "FILE_BYTES=%%~zI"
    set "FULL_PATH=%%~fI"
)

if !FILE_BYTES! equ 0 (
    echo.
    echo [VERIFICATION FAILED] Output executable is 0 bytes.
    pause
    exit /b 3
)

set /a FILE_MB=!FILE_BYTES! / 1048576

echo.
echo =====================================================================
echo                    BUILD SUCCESSFUL!
echo =====================================================================
echo Target Architecture : Windows x64 (Windows 11 / 10)
echo Deployment Mode     : Self-contained, single-file EXE
echo Target Executable   : !FULL_PATH!
echo Executable Size     : !FILE_BYTES! bytes (~!FILE_MB! MB)
echo.
echo You can run NetSpeedMonitor.exe immediately on your laptop!
echo No separate .NET runtime installation required.
echo =====================================================================
echo.
pause
