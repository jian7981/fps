@echo off
REM ================================================================
REM  Launch the Unity editor WITH the lobby registry argument.
REM  Why: the editor's own command line is what the game reads in
REM  Play mode (Environment.GetCommandLineArgs), so the argument
REM  must be given to the EDITOR process, not typed in game.
REM
REM  IMPORTANT: Unity keeps one instance per project. Close the
REM  editor for this project first, otherwise this does nothing.
REM ================================================================
set UNITY=D:\unity_hub_editor\2022.3.62f3c1\Editor\Unity.exe
set PROJECT=D:\unitydemo\Blow up your brother
set LOBBY=http://127.0.0.1:5000

echo Launching editor:
echo   project : %PROJECT%
echo   lobby   : %LOBBY%
echo Press Play inside Unity, then the public mode is on.
start "" "%UNITY%" -projectPath "%PROJECT%" -lobbyserver %LOBBY%
