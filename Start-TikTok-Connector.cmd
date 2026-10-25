@echo off
setlocal
set "nodeExe="
for /d %%D in ("%LOCALAPPDATA%\PixelLiveMod\tools\node-*-win-x64") do if exist "%%~fD\node.exe" set "nodeExe=%%~fD\node.exe"
if not defined nodeExe for %%N in (node.exe) do set "nodeExe=%%~$PATH:N"
if not defined nodeExe (
    echo Node.js nao encontrado. Execute Setup-PixelLiveMod.cmd primeiro.
    pause
    exit /b 1
)
if not exist "%~dp0tiktok-connector\dist\index.js" (
    echo Conector nao compilado. Execute Setup-PixelLiveMod.cmd primeiro.
    pause
    exit /b 1
)
pushd "%~dp0tiktok-connector"
"%nodeExe%" dist\index.js
set "result=%errorlevel%"
popd
if not "%result%"=="0" pause
exit /b %result%
