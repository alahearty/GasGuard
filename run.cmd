@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\GasGuard.Desktop\GasGuard.Desktop.csproj -c Debug
endlocal
