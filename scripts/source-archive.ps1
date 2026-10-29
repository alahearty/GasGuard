$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $env:TEMP ("gasguard-source-" + [Guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $root "artifacts\GasGuard-source.zip"

New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    Get-ChildItem -LiteralPath $root -File | Where-Object {
        $_.Name -notin @("GasGuard-source.zip")
    } | Copy-Item -Destination $stage

    foreach ($directory in @("src", "tests", "docs", "scripts")) {
        $source = Join-Path $root $directory
        if (Test-Path $source) {
            robocopy $source (Join-Path $stage $directory) /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -gt 7) {
                throw "Source copy failed for $directory with robocopy exit code $LASTEXITCODE."
            }
        }
    }

    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    $size = (Get-Item -LiteralPath $zipPath).Length
    "Source archive: $zipPath"
    "Bytes:          $size"
    "SHA256:         $hash"
}
finally {
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
