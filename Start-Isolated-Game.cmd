@echo off
set "pixelGameDir=%PIXELLIVEMOD_GAME_DIR%"
if not defined pixelGameDir set "pixelGameDir=%TEMP%\PixelLiveMod-10MillionPixels-Isolated"
if not exist "%pixelGameDir%\BepInEx\plugins\PixelLiveMod.dll" (
    echo PixelLiveMod.dll not found in the isolated game copy.
    exit /b 1
)
if not exist "%pixelGameDir%\10 Million Pixels Demo.exe" (
    echo Isolated game executable not found.
    exit /b 1
)
start "" /D "%pixelGameDir%" "%pixelGameDir%\10 Million Pixels Demo.exe"
