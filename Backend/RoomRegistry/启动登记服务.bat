@echo off
cd /d "%~dp0"
set "EXE=bin\Release\net10.0\RoomRegistry.exe"

REM Database connection string for the account module.
REM Commented out means the built-in local default is used
REM   (byb_app / blowyourbrother, see Db.cs).
REM set MYSQL_CONN=Server=127.0.0.1;Port=3306;User ID=byb_app;Password=BybApp9x7K2q;Database=blowyourbrother;

echo ==========================================
echo  Room registry + account service  (default port 5000)
echo   room servers / clients must point here:
echo       -lobbyserver http://127.0.0.1:5000
echo   change port: set ROOM_REGISTRY_PORT first
echo   account DB : MySQL (see Db.cs, env MYSQL_CONN)
echo   close this window to stop the service
echo ==========================================
echo.

if not exist "%EXE%" goto build

"%EXE%"
goto done

:build
echo [INFO] build output not found - building and running with the SDK...
"C:\Program Files\dotnet\dotnet.exe" run -c Release

:done
echo.
echo Service exited. Press any key to close this window.
pause
