@echo off
setlocal
cd /d "%~dp0.."
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
dotnet test NEIDFill.sln -c "%CONFIG%" --nologo
exit /b %ERRORLEVEL%
