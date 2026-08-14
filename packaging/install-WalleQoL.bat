@echo off
setlocal EnableDelayedExpansion
title WalleQoL Installer
echo.
echo  ============================================
echo   WalleQoL installer (quality of life)
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

echo  Installing WalleQoL ...
xcopy "%~dp0WalleQoL" "!GAMEDIR!\Mods\WalleQoL\" /E /I /Y >nul
if errorlevel 1 (
    echo  ERROR copying WalleQoL - is the game running? Close it and retry.
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
echo   * Multiplayer: the HOST and EVERY player need
echo     this mod ^(shared chests are server-side^).
echo   * Verify: press F1 in game and look for
echo     [WalleQoL] SharedContainers / QuickDeposit
echo  ============================================
echo.
pause
