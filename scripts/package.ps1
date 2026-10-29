param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish.ps1"
$suffix = if ($SelfContained) { "-selfcontained" } else { "" }
$publishDir = if ($SelfContained) {
    Join-Path $root "artifacts\publish\win-x64-selfcontained"
} else {
    Join-Path $root "artifacts\publish\win-x64"
}
$packageName = "GasGuard-Desktop-win-x64$suffix"
$packageDir = Join-Path $root "artifacts\package\$packageName"
$zipPath = Join-Path $root "artifacts\$packageName.zip"

if ($SelfContained) {
    & $publishScript -SelfContained
}
else {
    & $publishScript
}

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path $publishDir)) {
    throw "Publish directory was not created: $publishDir"
}

if (Test-Path $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $packageDir -Force
Copy-Item -LiteralPath (Join-Path $root "docs") -Destination $packageDir -Recurse -Force

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $zipPath).Length
"Package: $zipPath"
"Bytes:   $size"
"SHA256:  $hash"
