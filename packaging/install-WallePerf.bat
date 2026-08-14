@echo off
setlocal EnableDelayedExpansion
title WallePerf Installer
echo.
echo  ============================================
echo   WallePerf installer (performance patches)
echo  ============================================
echo.

set "GAMEDIR="
set "STEAM="

rem --- 1) default Steam location ---
if exist "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\7DaysToDie.exe" (
    set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
)

rem --- 2) Steam install path from registry ---
if not defined GAMEDIR (
    for /f "usebackq skip=2 tokens=2,*" %%A in (`reg query "HKLM\SOFTWARE\WOW6432Node\Valve\Steam" /v InstallPath 2^>nul`) do set "STEAM=%%B"
    if defined STEAM if exist "!STEAM!\steamapps\common\7 Days To Die\7DaysToDie.exe" (
        set "GAMEDIR=!STEAM!\steamapps\common\7 Days To Die"
    )
)

rem --- 3) additional Steam library folders ---
if not defined GAMEDIR if defined STEAM if exist "!STEAM!\steamapps\libraryfolders.vdf" (
    for /f usebackq^ tokens^=3^ delims^=^" %%P in (`findstr /i /c:"\"path\"" "!STEAM!\steamapps\libraryfolders.vdf"`) do (
        set "LIB=%%P"
        set "LIB=!LIB:\\=\!"
        if not defined GAMEDIR if exist "!LIB!\steamapps\common\7 Days To Die\7DaysToDie.exe" (
            set "GAMEDIR=!LIB!\steamapps\common\7 Days To Die"
        )
    )
)

rem --- 4) ask the user ---
if not defined GAMEDIR (
    echo Could not find 7 Days To Die automatically.
    echo Please paste the full path of your game folder
    echo ^(the folder that contains 7DaysToDie.exe^):
    set /p "GAMEDIR=> "
)

if not exist "!GAMEDIR!\7DaysToDie.exe" (
    echo.
    echo  ERROR: 7DaysToDie.exe not found in:
    echo    "!GAMEDIR!"
    echo  Install aborted - nothing was changed.
    echo.
    pause
    exit /b 1
)

echo.
echo  Game found: !GAMEDIR!
if not exist "!GAMEDIR!\Mods" mkdir "!GAMEDIR!\Mods"

echo  Installing WallePerf ...
xcopy "%~dp0WallePerf" "!GAMEDIR!\Mods\WallePerf\" /E /I /Y >nul
if errorlevel 1 (
    echo  ERROR copying WallePerf - is the game running? Close it and retry.
    pause
    exit /b 1
)

echo.
echo  ============================================
echo   Installed successfully!
echo.
echo   IMPORTANT:
echo   * Launch the game with EasyAntiCheat DISABLED
echo     ^(game launcher -^> untick EasyAntiCheat^).
echo     Code mods do NOT load with EAC on.
echo   * Works in singleplayer and as a client-side
echo     mod; servers can also run it for extra gains.
echo   * Verify: press F1 in game and look for
echo     [WallePerf] ... patches active
echo  ============================================
echo.
pause
