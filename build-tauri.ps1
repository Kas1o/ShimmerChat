# Build script: Publish ShimmerChat .NET backend, then package with Tauri
param(
    [string]$Target = "win-x64",
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$SkipTauri
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServerOutput = Join-Path $Root "src-tauri/binaries/shimmer-server"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " ShimmerChat Tauri Build" -ForegroundColor Cyan
Write-Host " Target: $Target" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Publish .NET backend
if (-not $SkipPublish) {
    Write-Host "`n[1/3] Publishing .NET backend..." -ForegroundColor Yellow
    
    if (Test-Path $ServerOutput) {
        Write-Host "  Cleaning previous publish..." -ForegroundColor Gray
        Remove-Item -Recurse -Force $ServerOutput
    }
    
    dotnet publish "$Root/ShimmerChat/ShimmerChat.csproj" `
        -c $Configuration `
        -r $Target `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $ServerOutput
    
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Write-Host "  .NET backend published to $ServerOutput" -ForegroundColor Green
}
else {
    Write-Host "`n[1/3] Skipping .NET publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 2: Generate Tauri icons from favicon (if needed)
$IconDir = Join-Path $Root "src-tauri/icons"
if (-not (Test-Path (Join-Path $IconDir "icon.ico"))) {
    Write-Host "`n[2/3] Generating icons..." -ForegroundColor Yellow
    $Favicon = Join-Path $Root "ShimmerChat/wwwroot/favicon.png"
    if (Test-Path $Favicon) {
        # Use the Tauri CLI icon generator
        Push-Location (Join-Path $Root "src-tauri")
        try {
            npx @tauri-apps/cli icon $Favicon 2>$null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  Tauri icon generation failed, copying favicon as fallback..." -ForegroundColor Yellow
                Copy-Item $Favicon (Join-Path $IconDir "icon.png") -Force
            }
        }
        finally {
            Pop-Location
        }
        Write-Host "  Icons generated" -ForegroundColor Green
    }
    else {
        Write-Host "  No favicon found, skipping icon generation" -ForegroundColor Yellow
        # Create a minimal placeholder icon
        # Tauri needs at least icon.ico to build
        if (-not (Test-Path (Join-Path $IconDir "icon.ico"))) {
            Write-Host "  Creating placeholder icon..." -ForegroundColor Yellow
            # Use a simple 1-pixel PNG as placeholder (won't look good but allows build)
            $PlaceholderPng = Join-Path $IconDir "icon.png"
            if (-not (Test-Path $PlaceholderPng)) {
                # Generate a minimal valid PNG (1x1 purple pixel)
                # This is a base64 of the smallest valid PNG
                $minimalPng = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==")
                [IO.File]::WriteAllBytes($PlaceholderPng, $minimalPng)
            }
        }
    }
}
else {
    Write-Host "`n[2/3] Icons already exist, skipping" -ForegroundColor Gray
}

# Step 3: Build Tauri
if (-not $SkipTauri) {
    Write-Host "`n[3/3] Building Tauri app..." -ForegroundColor Yellow
    Push-Location (Join-Path $Root "src-tauri")
    try {
        npx @tauri-apps/cli build 2>&1
        if ($LASTEXITCODE -ne 0) { throw "tauri build failed" }
    }
    finally {
        Pop-Location
    }
    Write-Host "`n  Build complete!" -ForegroundColor Green
    Write-Host "  Output: src-tauri/target/release/" -ForegroundColor Green
}
else {
    Write-Host "`n[3/3] Skipping Tauri build (--SkipTauri)" -ForegroundColor Gray
}

Write-Host "`nDone!" -ForegroundColor Cyan
