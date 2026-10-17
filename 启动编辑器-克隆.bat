@echo off
REM ================================================================
REM  Same as "?????-???.bat" but for the ParrelSync clone,
REM  so you can run two editor clients side by side.
REM  Close the clone's editor first if it is already running.
REM ================================================================
set UNITY=D:\unity_hub_editor\2022.3.62f3c1\Editor\Unity.exe
set PROJECT=D:\unitydemo\Blow up your brother_clone_0
set LOBBY=http://127.0.0.1:5000

echo Launching editor (clone):
echo   project : %PROJECT%
echo   lobby   : %LOBBY%
echo Press Play inside Unity, then the public mode is on.
start "" "%UNITY%" -projectPath "%PROJECT%" -lobbyserver %LOBBY%
