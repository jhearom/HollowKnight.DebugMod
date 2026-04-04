@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "BUNDLE_ZIP=%SCRIPT_DIR%DebugMod-1578-windows-bundle.zip"
set "LOG_FILE=%SCRIPT_DIR%install_debugmod_bundle_1578.last.log"
set "GAME_MANAGED=C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight\hollow_knight_Data\Managed"
set "TEMP_ROOT=%TEMP%\DebugModBundleInstall1578"
set "EXTRACT_DIR=%TEMP_ROOT%\extract"
set "MANAGED_SOURCE="
set "BUILD_IDENTITY_FILE="
set "BUILD_UTC="
set "EXIT_CODE=0"

> "%LOG_FILE%" echo [DebugMod 1578 Installer] Starting install.
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Bundle: "%BUNDLE_ZIP%"
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Target: "%GAME_MANAGED%"

echo [DebugMod 1578 Installer] Starting install.
echo [DebugMod 1578 Installer] Bundle: "%BUNDLE_ZIP%"
echo [DebugMod 1578 Installer] Target: "%GAME_MANAGED%"

if not exist "%BUNDLE_ZIP%" (
    echo [DebugMod 1578 Installer] ERROR: Bundle zip not found next to this script.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Bundle zip not found next to this script.
    set "EXIT_CODE=1"
    goto :finish
)

if not exist "%GAME_MANAGED%" (
    echo [DebugMod 1578 Installer] ERROR: Hollow Knight Managed directory not found.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Hollow Knight Managed directory not found.
    set "EXIT_CODE=1"
    goto :finish
)

if exist "%TEMP_ROOT%" rmdir /s /q "%TEMP_ROOT%"
mkdir "%EXTRACT_DIR%" >nul 2>&1
if errorlevel 1 (
    echo [DebugMod 1578 Installer] ERROR: Failed to create temp extraction directory.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Failed to create temp extraction directory.
    set "EXIT_CODE=1"
    goto :finish
)

echo [DebugMod 1578 Installer] Extracting bundle...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%BUNDLE_ZIP%' -DestinationPath '%EXTRACT_DIR%' -Force"
if errorlevel 1 (
    echo [DebugMod 1578 Installer] ERROR: Failed to extract bundle.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Failed to extract bundle.
    set "EXIT_CODE=1"
    goto :finish
)

if exist "%EXTRACT_DIR%\debugmod_bundle_1578\BUILD_IDENTITY.txt" (
    set "BUILD_IDENTITY_FILE=%EXTRACT_DIR%\debugmod_bundle_1578\BUILD_IDENTITY.txt"
) else (
    for /f "usebackq delims=" %%F in (`dir /s /b "%EXTRACT_DIR%\BUILD_IDENTITY.txt" 2^>nul`) do (
        if not defined BUILD_IDENTITY_FILE set "BUILD_IDENTITY_FILE=%%~fF"
    )
)
if defined BUILD_IDENTITY_FILE (
    for /f "usebackq tokens=1,* delims=:" %%A in (`findstr /b /c:"Build UTC:" "!BUILD_IDENTITY_FILE!"`) do (
        set "BUILD_UTC=%%B"
    )
    if defined BUILD_UTC (
        echo [DebugMod 1578 Installer] Bundle Build UTC:!BUILD_UTC!
    ) else (
        echo [DebugMod 1578 Installer] Bundle build identity file found: "!BUILD_IDENTITY_FILE!"
    )
) else (
    echo [DebugMod 1578 Installer] WARNING: BUILD_IDENTITY.txt not found in extracted bundle.
)

if exist "%EXTRACT_DIR%\debugmod_bundle_1578\hollow_knight_Data\Managed\Assembly-CSharp.dll" (
    set "MANAGED_SOURCE=%EXTRACT_DIR%\debugmod_bundle_1578\hollow_knight_Data\Managed"
)

if not defined MANAGED_SOURCE if exist "%EXTRACT_DIR%\hollow_knight_Data\Managed\Assembly-CSharp.dll" (
    set "MANAGED_SOURCE=%EXTRACT_DIR%\hollow_knight_Data\Managed"
)

for /d /r "%EXTRACT_DIR%" %%D in (Managed) do (
    if not defined MANAGED_SOURCE if exist "%%~fD\Assembly-CSharp.dll" (
        set "MANAGED_SOURCE=%%~fD"
    )
)

for /d /r "%EXTRACT_DIR%" %%D in (Managed) do (
    if not defined MANAGED_SOURCE if exist "%%~fD\Mods\DebugMod\DebugMod.dll" (
        set "MANAGED_SOURCE=%%~fD"
    )
)

if not defined MANAGED_SOURCE (
    echo [DebugMod 1578 Installer] ERROR: Could not locate extracted Managed directory in the bundle.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Could not locate extracted Managed directory in the bundle.
    set "EXIT_CODE=1"
    goto :finish
)

echo [DebugMod 1578 Installer] Using extracted Managed directory: "%MANAGED_SOURCE%"
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Using extracted Managed directory: "%MANAGED_SOURCE%"

echo [DebugMod 1578 Installer] Removing stale DebugMod files from the target install...
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Removing stale DebugMod files from the target install...
if exist "%GAME_MANAGED%\Mods\DebugMod.dll" del /f /q "%GAME_MANAGED%\Mods\DebugMod.dll" >nul 2>&1
if exist "%GAME_MANAGED%\Mods\DebugMod.pdb" del /f /q "%GAME_MANAGED%\Mods\DebugMod.pdb" >nul 2>&1
if exist "%GAME_MANAGED%\Mods\DebugMod.xml" del /f /q "%GAME_MANAGED%\Mods\DebugMod.xml" >nul 2>&1
if exist "%GAME_MANAGED%\Mods\DebugMod" rmdir /s /q "%GAME_MANAGED%\Mods\DebugMod" >nul 2>&1

echo [DebugMod 1578 Installer] Copying files into the game install...
robocopy "%MANAGED_SOURCE%" "%GAME_MANAGED%" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP
set "ROBOCOPY_EXIT=%ERRORLEVEL%"

if %ROBOCOPY_EXIT% GEQ 8 (
    echo [DebugMod 1578 Installer] ERROR: robocopy failed with exit code %ROBOCOPY_EXIT%.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: robocopy failed with exit code %ROBOCOPY_EXIT%.
    set "EXIT_CODE=%ROBOCOPY_EXIT%"
    goto :finish
)

if exist "%GAME_MANAGED%\Mods\DebugMod\DebugMod.dll" (
    for %%F in ("%GAME_MANAGED%\Mods\DebugMod\DebugMod.dll") do (
        echo [DebugMod 1578 Installer] Installed DebugMod.dll timestamp: %%~tF size: %%~zF
        >> "%LOG_FILE%" echo [DebugMod 1578 Installer] Installed DebugMod.dll timestamp: %%~tF size: %%~zF
    )
) else (
    echo [DebugMod 1578 Installer] ERROR: Mods\DebugMod\DebugMod.dll was not present after copy.
    >> "%LOG_FILE%" echo [DebugMod 1578 Installer] ERROR: Mods\DebugMod\DebugMod.dll was not present after copy.
    set "EXIT_CODE=1"
    goto :finish
)

echo [DebugMod 1578 Installer] Install completed successfully.
echo [DebugMod 1578 Installer] robocopy exit code: %ROBOCOPY_EXIT%
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Install completed successfully.
>> "%LOG_FILE%" echo [DebugMod 1578 Installer] robocopy exit code: %ROBOCOPY_EXIT%

:finish
echo [DebugMod 1578 Installer] Cleaning up temp files...
rmdir /s /q "%TEMP_ROOT%" >nul 2>&1

>> "%LOG_FILE%" echo [DebugMod 1578 Installer] Exit code: %EXIT_CODE%
echo [DebugMod 1578 Installer] Log file: "%LOG_FILE%"
if not "%EXIT_CODE%"=="0" (
    echo [DebugMod 1578 Installer] Installation failed. Press any key to close this window.
    pause
)
exit /b %EXIT_CODE%
