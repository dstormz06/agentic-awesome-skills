@echo off
rem ANDA Companion (Notula) - portable demo launcher.
rem Keeps every setting inside the "data" folder next to this file, so the demo
rem never touches your profile and removing it is just deleting this folder.
setlocal
cd /d "%~dp0"
set "ANDA_COMPANION_HOME=%~dp0data"
start "" "%~dp0AndaCompanion.exe"
start "" notepad.exe "%~dp0sample-memo.txt"
echo ANDA Companion (Notula) started. Look for its icon near the clock.
echo Windows 11 may hide it under the ^^ arrow - drag it onto the taskbar.
timeout /t 6 >nul
