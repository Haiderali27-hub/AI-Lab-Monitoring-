# SmartExam Startup Script for PowerShell
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "      SmartExam System Launcher" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script will spin up all three components in separate windows:"
Write-Host "1. Backend API (http://localhost:5050)"
Write-Host "2. Admin/Teacher Web Panel (Vite Dev Server)"
Write-Host "3. Student WPF Desktop App"
Write-Host ""

$PSScriptRoot = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
if ([string]::IsNullOrEmpty($PSScriptRoot)) {
    $PSScriptRoot = Get-Location
}

Write-Host "[1/3] Starting Backend API..." -ForegroundColor Green
Start-Process cmd -ArgumentList "/k cd /d `"$PSScriptRoot\SmartExam_Root\Backend_API`" && dotnet run --urls http://localhost:5050"

Write-Host "[2/3] Starting Admin Web Panel..." -ForegroundColor Green
Start-Process cmd -ArgumentList "/k cd /d `"$PSScriptRoot\SmartExam_Root\Admin_Web_Panel`" && npm run dev"

Write-Host "[3/3] Starting Student Desktop App..." -ForegroundColor Green
Start-Process cmd -ArgumentList "/k cd /d `"$PSScriptRoot\SmartExam_Root\Student_Desktop_App`" && dotnet run"

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "All components triggered successfully!" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
