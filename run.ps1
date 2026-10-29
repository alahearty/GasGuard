$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    dotnet run --project "src\GasGuard.Desktop\GasGuard.Desktop.csproj" -c Debug
}
finally {
    Pop-Location
}
