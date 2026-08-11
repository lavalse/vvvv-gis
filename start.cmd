@echo off
rem Double-clickable wrapper for start.ps1, so getting to work does not require a terminal.
rem
rem -ExecutionPolicy Bypass because the default policy blocks unsigned local scripts, and
rem -NoProfile so a slow or opinionated user profile cannot interfere.
rem
rem The script shows a menu and reads a choice, so it must keep this console and its stdin.
rem Pausing only on failure means a normal run leaves no window behind.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start.ps1" %*
if errorlevel 1 pause
