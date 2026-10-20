@echo off
:: SmartExam Startup Script for Windows
TITLE SmartExam Launcher

echo ==========================================
echo       SmartExam System Launcher
echo ==========================================
echo.
echo Starting all three components in separate windows...
echo.

echo [1/3] Starting Backend API...
start "SmartExam Backend API" cmd /k "cd /d "%~dp0SmartExam_Root\Backend_API" && dotnet run --urls http://localhost:5050"

echo [2/3] Starting Admin Web Panel...
start "SmartExam Admin Web Panel" cmd /k "cd /d "%~dp0SmartExam_Root\Admin_Web_Panel" && npm run dev"

echo [3/3] Starting Student Desktop App...
start "SmartExam Student Desktop App" cmd /k "cd /d "%~dp0SmartExam_Root\Student_Desktop_App" && dotnet run --project Student_Desktop_App.csproj"

echo.
echo ==========================================
echo All components triggered successfully!
echo You can view logs in each opened terminal.
echo ==========================================
ping 127.0.0.1 -n 4 >nul
