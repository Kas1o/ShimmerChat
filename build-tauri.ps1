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
    Write-Host "`n[1/5] Publishing .NET backend..." -ForegroundColor Yellow
    
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
    Write-Host "`n[1/5] Skipping .NET publish (--SkipPublish)" -ForegroundColor Gray
}

# Step 2: Sync version from Nerdbank.GitVersioning into Tauri config
# version.json 是唯一版本源；tauri.conf.json / Cargo.toml 里的 version 在构建时被覆盖
$TauriConf = Join-Path $Root "src-tauri/tauri.conf.json"
$CargoToml = Join-Path $Root "src-tauri/Cargo.toml"
$VersionSynced = $false

if (Test-Path (Join-Path $Root "version.json")) {
    Write-Host "`n[2/5] Syncing version from Nerdbank.GitVersioning..." -ForegroundColor Yellow

    # 从 nbgv 获取版本号（需提交 version.json 后才生效）
    $nbgvOutput = nbgv get-version -v AssemblyInformationalVersion 2>&1
    if ($LASTEXITCODE -eq 0 -and $nbgvOutput) {
        # 去掉 build metadata（+ 后面的 commit hash），得到干净的 semver
        $semver = ($nbgvOutput.Trim() -split '\+')[0]
        Write-Host "  Nerdbank version: $semver" -ForegroundColor Gray

        # 注入 tauri.conf.json
        $conf = Get-Content $TauriConf -Raw | ConvertFrom-Json
        $conf.version = $semver
        $conf | ConvertTo-Json -Depth 10 | Set-Content $TauriConf -NoNewline
        Write-Host "  tauri.conf.json version -> $semver" -ForegroundColor Gray

        # 注入 Cargo.toml
        $cargo = Get-Content $CargoToml -Raw
        $cargo = $cargo -replace '(?m)^version\s*=\s*".*"', "version = `"$semver`""
        Set-Content $CargoToml $cargo -NoNewline
        Write-Host "  Cargo.toml version -> $semver" -ForegroundColor Gray

        $VersionSynced = $true
        Write-Host "  Version synced" -ForegroundColor Green
    }
    else {
        Write-Host "  nbgv not available, using version from config as-is" -ForegroundColor Yellow
    }
}
else {
    Write-Host "`n[2/5] No version.json found, skipping version sync" -ForegroundColor Gray
}

# Step 3: Generate Tauri icons from favicon (if needed)
$IconDir = Join-Path $Root "src-tauri/icons"
if (-not (Test-Path (Join-Path $IconDir "icon.ico"))) {
    Write-Host "`n[3/5] Generating icons..." -ForegroundColor Yellow
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
    Write-Host "`n[3/5] Icons already exist, skipping" -ForegroundColor Gray
}

# Step 4: Build Tauri
if (-not $SkipTauri) {
    Write-Host "`n[4/5] Building Tauri app..." -ForegroundColor Yellow
    Push-Location (Join-Path $Root "src-tauri")
    try {
        npx @tauri-apps/cli build --bundles nsis 2>&1
        if ($LASTEXITCODE -ne 0) { throw "tauri build failed" }
    }
    finally {
        Pop-Location
    }
    Write-Host "`n  Build complete!" -ForegroundColor Green
    Write-Host "  Output: src-tauri/target/release/" -ForegroundColor Green
}
else {
    Write-Host "`n[4/5] Skipping Tauri build (--SkipTauri)" -ForegroundColor Gray
}

# Step 5: 还原被脚本覆盖的 tauri.conf.json / Cargo.toml，保持工作区干净
if ($VersionSynced) {
    git checkout -- $TauriConf $CargoToml 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n[5/5] Restored tauri.conf.json and Cargo.toml to repo version" -ForegroundColor Gray
    }
    else {
        Write-Host "`n[5/5] Warning: could not restore tauri.conf.json / Cargo.toml (not in git?)" -ForegroundColor Yellow
    }
}

Write-Host "`nDone!" -ForegroundColor Cyan
