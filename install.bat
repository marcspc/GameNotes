@echo off
setlocal
set "DEST=%AppData%\Playnite\Extensions\GameNotes"

tasklist /FI "IMAGENAME eq Playnite.DesktopApp.exe" 2>NUL | find /I "Playnite.DesktopApp.exe" >NUL
if not errorlevel 1 (
    echo Playnite is running. Please close it completely and run this file again.
    pause
    exit /b 1
)

echo Installing Game Notes into:
echo   %DEST%
if not exist "%DEST%" mkdir "%DEST%"
xcopy /Y /Q "%~dp0GameNotes\*" "%DEST%\" >NUL
if errorlevel 1 (
    echo Something went wrong while copying the files.
    pause
    exit /b 1
)

echo.
echo Done! Open Playnite and check Add-ons ^> Extensions ^> Game Notes.
echo Your notes are never touched when you update or reinstall.
pause
