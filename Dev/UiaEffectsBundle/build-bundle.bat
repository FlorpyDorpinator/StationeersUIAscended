@echo off
REM ============================================================================
REM  build-bundle.bat - build uia_effects.bundle for Stationeers UI Ascended.
REM
REM  Uses the locally-installed 2022.3.62f3 editor on this dedicated mini content
REM  project (NOT the main 2022.3.7f1 game project). Beef's shipped mods prove that
REM  .62-built bundles load in the 7f1 game; HudShaderStore loads fail-soft either way.
REM
REM  Usage:   double-click, or run from a shell:  build-bundle.bat
REM  Output:  %~dp0Build\uia_effects.bundle
REM           (which is exactly the dev path HudShaderStore.DevBundlePath expects)
REM ============================================================================
setlocal

set "UNITY=C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
set "PROJ=%~dp0."
set "LOG=%~dp0build.log"

if not exist "%UNITY%" (
    echo ERROR: Unity editor not found at "%UNITY%".
    echo Install 2022.3.62f3 via Unity Hub, or edit UNITY in this script.
    exit /b 1
)

echo Building UIA effects bundle...
echo   editor : %UNITY%
echo   project: %PROJ%
echo   log    : %LOG%

REM Clean-build ALWAYS: Unity's incremental bundle build can leave a stale output file
REM untouched even after a shader source change (observed 2026-07-14 ??? DONE was reported
REM but the bundle mtime never moved). Moving the output away forces a real rewrite, and the
REM previous bundle is put back if Unity fails (e.g. an expired license, 2026-09-27), so a
REM failed build never leaves the dev path or the package without a bundle.
if exist "%~dp0Build.prev" rd /s /q "%~dp0Build.prev"
if exist "%~dp0Build" move "%~dp0Build" "%~dp0Build.prev" >nul

"%UNITY%" -batchmode -quit -projectPath "%PROJ%" -executeMethod UiaBundleBuilder.Build -logFile "%LOG%"
if errorlevel 1 (
    echo.
    echo Unity build FAILED. See "%LOG%".
    goto restore
)

REM BuildAssetBundles writes into the project-relative "Build" dir, which is %~dp0Build.
REM No copy is needed - the artifact already lands where HudShaderStore looks for it.
if not exist "%~dp0Build\uia_effects.bundle" (
    echo.
    echo WARNING: expected bundle not found at "%~dp0Build\uia_effects.bundle".
    echo Check "%LOG%" for details.
    goto restore
)
if exist "%~dp0Build.prev" rd /s /q "%~dp0Build.prev"
echo.
echo DONE. Bundle at "%~dp0Build\uia_effects.bundle"
endlocal
exit /b 0

:restore
if exist "%~dp0Build.prev" (
    if exist "%~dp0Build" rd /s /q "%~dp0Build"
    move "%~dp0Build.prev" "%~dp0Build" >nul
    echo The previous bundle was put back.
)
endlocal
exit /b 1
