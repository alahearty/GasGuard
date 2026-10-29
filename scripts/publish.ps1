param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $arguments = @(
        "publish",
        "src\GasGuard.Desktop\GasGuard.Desktop.csproj",
        "-c", "Release",
        "-r", "win-x64",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    if ($SelfContained) {
        $arguments += @("--self-contained", "true", "-o", "artifacts\publish\win-x64-selfcontained")
    }
    else {
        $arguments += @("--self-contained", "false", "-o", "artifacts\publish\win-x64")
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
