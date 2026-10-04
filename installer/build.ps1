<#
.SYNOPSIS
    Builds, tests and packages NOVA into a Windows installer (Setup.exe) and a portable zip using Velopack.

.EXAMPLE
    ./installer/build.ps1 -Version 1.0.0
    Produces artifacts/releases/NOVA-win-Setup.exe, a portable zip and update packages.
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $publish = Join-Path $root "artifacts/publish"
    $releases = Join-Path $root "artifacts/releases"
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    # Rebuilding the same version replaces it (Velopack refuses to overwrite an existing release).
    if (Test-Path (Join-Path $releases "NOVA-$Version-full.nupkg")) { Remove-Item $releases -Recurse -Force }

    Write-Host "> Restoring tools" -ForegroundColor Cyan
    dotnet tool restore | Out-Host

    if (-not $SkipTests) {
        Write-Host "> Running tests" -ForegroundColor Cyan
        # Integration tests touch real Windows APIs; they run in the normal test pass on a dev machine.
        dotnet test tests/Nova.Tests -c Release --filter "Category!=Integration" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
    }

    Write-Host "> Publishing self-contained win-x64 ($Version)" -ForegroundColor Cyan
    dotnet publish src/Nova.App -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:PublishReadyToRun=true -p:DebugType=none `
        -o $publish | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

    Write-Host "> Packaging with Velopack" -ForegroundColor Cyan
    dotnet vpk pack `
        --packId NOVA `
        --packVersion $Version `
        --runtime win-x64 `
        --packDir $publish `
        --mainExe NOVA.exe `
        --packTitle "NOVA" `
        --packAuthors "NOVA" `
        --icon src/Nova.App/Assets/nova.ico `
        --outputDir $releases | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Packaging failed" }

    Write-Host "`nOK: Done. Installer and portable build:" -ForegroundColor Green
    Get-ChildItem $releases | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table | Out-Host
}
finally {
    Pop-Location
}
