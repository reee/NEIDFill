@echo off
setlocal
cd /d "%~dp0.."
dotnet build NEIDFill.sln -c Release --nologo
if errorlevel 1 exit /b 1
dotnet test NEIDFill.sln -c Release --nologo --no-build
if errorlevel 1 exit /b 1
echo CI OK
exit /b 0
