# Pin to Deck - Development Script
# Usage: .\dev.ps1 <command>
# Commands: build, clean, test, watch, deploy, restart, help

param(
    [Parameter(Position = 0)]
    [ValidateSet("install", "build", "clean", "test", "watch", "deploy", "full", "restart", "exec", "help", "")]
    [string]$Command = "help",
    
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalArgs
)

# === Configuration ===
$PLUGIN_ID = "tw.idv.applepig.pin-to-deck"
$PROJECT_ROOT = $PSScriptRoot
$SRC_PATH = Join-Path $PROJECT_ROOT "src\PinToDeck"
$CSPROJ_PATH = Join-Path $SRC_PATH "PinToDeck.csproj"
$TEST_PROJECT_PATH = Join-Path $PROJECT_ROOT "tests\PinToDeck.Tests\PinToDeck.Tests.csproj"
$PLUGIN_OUTPUT_PATH = "$env:APPDATA\Elgato\StreamDeck\Plugins\$PLUGIN_ID.sdPlugin"

# Auto-detect dotnet.exe
$DOTNET_EXE = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $DOTNET_EXE) {
    $DOTNET_EXE = "C:\Program Files\dotnet\dotnet.exe"
}
if (-not (Test-Path $DOTNET_EXE)) {
    Write-Error ".NET SDK not found. Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download"
    exit 1
}

# Auto-detect Stream Deck installation
$STREAM_DECK_EXE = "C:\Program Files\Elgato\StreamDeck\StreamDeck.exe"
if (-not (Test-Path $STREAM_DECK_EXE)) {
    # Try alternative path
    $ALT_PATH = "${env:ProgramFiles(x86)}\Elgato\StreamDeck\StreamDeck.exe"
    if (Test-Path $ALT_PATH) {
        $STREAM_DECK_EXE = $ALT_PATH
    }
}

# === Helper Functions ===
function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " $Message" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Step, [string]$Message)
    Write-Host ""
    Write-Host "[$Step] $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

function Test-StreamDeckRunning {
    return $null -ne (Get-Process -Name "StreamDeck" -ErrorAction SilentlyContinue)
}

function Stop-StreamDeck {
    # Stop StreamDeck main process
    $process = Get-Process -Name "StreamDeck" -ErrorAction SilentlyContinue
    if ($process) {
        Write-Step "STOP" "Stopping Stream Deck..."
        Stop-Process -Name "StreamDeck" -Force
    }
    
    # Also stop any running plugin processes
    $pluginProcess = Get-Process -Name "PinToDeck" -ErrorAction SilentlyContinue
    if ($pluginProcess) {
        Write-Host "  Stopping plugin process..." -ForegroundColor Gray
        Stop-Process -Name "PinToDeck" -Force -ErrorAction SilentlyContinue
    }
    
    if ($process -or $pluginProcess) {
        Start-Sleep -Seconds 3
        Write-Success "Stream Deck stopped."
    }
}

function Start-StreamDeck {
    if (Test-Path $STREAM_DECK_EXE) {
        Write-Step "START" "Starting Stream Deck..."
        Start-Process $STREAM_DECK_EXE
        Write-Success "Stream Deck started."
    }
    else {
        Write-Error "Stream Deck executable not found at: $STREAM_DECK_EXE"
    }
}

# === Commands ===

function Invoke-Install {
    Write-Header "Installing Development Dependencies"
    
    # Check .NET SDK
    Write-Step "1/4" "Checking .NET SDK..."
    $dotnetVersion = & $DOTNET_EXE --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Success "  ✓ .NET SDK $dotnetVersion found"
        
        # Check if it's .NET 8
        if ($dotnetVersion -like "8.*") {
            Write-Success "  ✓ .NET 8.0 SDK is installed"
        }
        else {
            Write-Host "  ⚠ Current SDK version: $dotnetVersion" -ForegroundColor Yellow
            Write-Host "  ⚠ This project requires .NET 8.0 SDK" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "To install .NET 8.0 SDK, run:" -ForegroundColor Gray
            Write-Host "  winget install Microsoft.DotNet.SDK.8" -ForegroundColor White
            Write-Host ""
        }
    }
    else {
        Write-Error ".NET SDK not found!"
        Write-Host ""
        Write-Host "To install .NET 8.0 SDK, run:" -ForegroundColor Gray
        Write-Host "  winget install Microsoft.DotNet.SDK.8" -ForegroundColor White
        Write-Host "Or download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Gray
        Write-Host ""
        return $false
    }
    
    # Check NuGet sources
    Write-Step "2/4" "Checking NuGet package sources..."
    $sources = & $DOTNET_EXE nuget list source 2>$null
    if ($sources -match "nuget.org") {
        Write-Success "  ✓ nuget.org is configured"
    }
    else {
        Write-Host "  Adding nuget.org source..." -ForegroundColor Yellow
        & $DOTNET_EXE nuget add source https://api.nuget.org/v3/index.json -n nuget.org
        if ($LASTEXITCODE -eq 0) {
            Write-Success "  ✓ nuget.org source added"
        }
        else {
            Write-Error "Failed to add nuget.org source"
        }
    }
    
    # Restore packages
    Write-Step "3/4" "Restoring NuGet packages..."
    & $DOTNET_EXE restore $CSPROJ_PATH
    if ($LASTEXITCODE -eq 0) {
        Write-Success "  ✓ Packages restored successfully"
    }
    else {
        Write-Error "Package restore failed!"
        return $false
    }
    
    # Check Stream Deck installation
    Write-Step "4/4" "Checking Stream Deck installation..."
    if (Test-Path $STREAM_DECK_EXE) {
        Write-Success "  ✓ Stream Deck found at: $STREAM_DECK_EXE"
    }
    else {
        Write-Host "  ⚠ Stream Deck not found at expected location" -ForegroundColor Yellow
        Write-Host "  Expected: $STREAM_DECK_EXE" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Download Stream Deck from:" -ForegroundColor Gray
        Write-Host "  https://www.elgato.com/downloads" -ForegroundColor White
        Write-Host ""
    }
    
    Write-Host ""
    Write-Success "✅ Development environment setup complete!"
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  .\dev.ps1 build    # Build the plugin" -ForegroundColor White
    Write-Host "  .\dev.ps1 full     # Full build + deploy + restart Stream Deck" -ForegroundColor White
    Write-Host ""
    
    return $true
}


function Invoke-Build {
    param(
        [string]$Configuration = "Debug"
    )
    Write-Header "Building Pin to Deck Plugin ($Configuration)"
    

    
    Write-Step "2/4" "Restoring NuGet packages..."
    & $DOTNET_EXE restore $CSPROJ_PATH
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Restore failed!"
        return $false
    }
    
    Write-Step "3/4" "Building project..."
    & $DOTNET_EXE build $CSPROJ_PATH --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed! Check output for errors."
    }
    
    $binDir = Join-Path $SRC_PATH "bin\$Configuration\net8.0-windows"
    
    Write-Step "4/4" "Build output ready"
    Write-Host "  Output: $binDir" -ForegroundColor Gray
    
    Write-Success "Build completed successfully!"
    return $true
}

function Invoke-Clean {
    Write-Header "Cleaning Build Artifacts"
    
    Write-Step "1/3" "Cleaning project..."
    & $DOTNET_EXE clean $CSPROJ_PATH
    
    Write-Step "2/3" "Removing bin/obj folders..."
    $foldersToRemove = @(
        (Join-Path $SRC_PATH "bin"),
        (Join-Path $SRC_PATH "obj")
    )
    foreach ($folder in $foldersToRemove) {
        if (Test-Path $folder) {
            Remove-Item -Path $folder -Recurse -Force
            Write-Host "  Removed: $folder" -ForegroundColor Gray
        }
    }
    
    Write-Step "3/3" "Optionally clean plugin output..."
    if (Test-Path $PLUGIN_OUTPUT_PATH) {
        $response = Read-Host "Remove plugin from Stream Deck folder? (y/N)"
        if ($response -eq "y" -or $response -eq "Y") {
            Remove-Item -Path $PLUGIN_OUTPUT_PATH -Recurse -Force
            Write-Host "  Removed: $PLUGIN_OUTPUT_PATH" -ForegroundColor Gray
        }
    }
    
    Write-Success "Clean completed!"
}

function Invoke-Test {
    Write-Header "Running Tests"
    
    # Check if test project exists
    if (-not (Test-Path $TEST_PROJECT_PATH)) {
        Write-Host ""
        Write-Host "Test project not found at: $TEST_PROJECT_PATH" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To create a test project, run:" -ForegroundColor Gray
        Write-Host "  dotnet new xunit -o tests\PinToDeck.Tests" -ForegroundColor White
        Write-Host "  dotnet add tests\PinToDeck.Tests\PinToDeck.Tests.csproj reference src\PinToDeck\PinToDeck.csproj" -ForegroundColor White
        Write-Host ""
        return $false
    }
    
    Write-Step "1/2" "Building test project..."
    & $DOTNET_EXE build $TEST_PROJECT_PATH
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test build failed!"
        return $false
    }
    
    Write-Step "2/2" "Running tests..."
    & $DOTNET_EXE test $TEST_PROJECT_PATH --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed!"
        return $false
    }
    
    Write-Success "All tests passed!"
    return $true
}

function Invoke-Watch {
    Write-Header "Watching for Changes (Hot Reload)"
    
    Write-Host ""
    Write-Host "Starting file watcher..." -ForegroundColor Gray
    Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
    Write-Host ""
    
    & $DOTNET_EXE watch --project $CSPROJ_PATH build
}

function Remove-DeployedPlugin {
    Write-Step "CLEAN" "Removing deployed plugin files..."
    
    if (Test-Path $PLUGIN_OUTPUT_PATH) {
        Remove-Item -Path $PLUGIN_OUTPUT_PATH -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed: $PLUGIN_OUTPUT_PATH" -ForegroundColor Gray
        
        # Wait a moment for file system to catch up
        Start-Sleep -Milliseconds 500
    }
    else {
        Write-Host "  Plugin folder not found, skipping..." -ForegroundColor Gray
    }
}

function Deploy-Artifacts {
    Write-Step "DEPLOY" "Deploying build artifacts to Stream Deck plugin folder..."
    
    $buildOutput = Join-Path $SRC_PATH "bin\Release\net8.0-windows"
    
    if (-not (Test-Path $buildOutput)) {
        Write-Error "Build output not found at $buildOutput. Did you build Release?"
        return
    }

    Write-Host "  Source: $buildOutput" -ForegroundColor Gray
    Write-Host "  Target: $PLUGIN_OUTPUT_PATH" -ForegroundColor Gray
    
    # Clean deployed plugin first to avoid Copy-Item issues with existing directories
    Remove-DeployedPlugin
    
    # Create fresh plugin directory
    New-Item -ItemType Directory -Force -Path $PLUGIN_OUTPUT_PATH | Out-Null

    # Copy all files and directories from build output
    # This is the ONLY copy operation - build output already has everything we need
    Copy-Item -Path "$buildOutput\*" -Destination $PLUGIN_OUTPUT_PATH -Recurse -Force
    
    Write-Success "  ✓ Build artifacts deployed successfully!"
}

function Invoke-Deploy {
    Write-Header "Deploying Plugin"
    
    # Build first
    if (-not (Invoke-Build -Configuration "Release")) {
        return
    }
    
    Deploy-Artifacts
    
    Write-Host ""
    Write-Host "Deployment completed!" -ForegroundColor Green
    Write-Host ""
    
    # Offer to restart Stream Deck
    if (Test-StreamDeckRunning) {
        $response = Read-Host "Restart Stream Deck to load changes? (Y/n)"
        if ($response -ne "n" -and $response -ne "N") {
            Invoke-Restart
        }
    }
    else {
        $response = Read-Host "Start Stream Deck? (Y/n)"
        if ($response -ne "n" -and $response -ne "N") {
            Start-StreamDeck
        }
    }
}

function Invoke-Full {
    Write-Header "Full Build & Deploy Pipeline"
    
    # Stop Stream Deck if running
    Write-Step "1/4" "Stopping Stream Deck..."
    if (Test-StreamDeckRunning) {
        Stop-StreamDeck
    }
    else {
        Write-Host "Stream Deck not running, skipping..." -ForegroundColor Gray
    }
    
    # Clean first (without prompt)
    Write-Step "2/4" "Cleaning..."
    & $DOTNET_EXE clean $CSPROJ_PATH --nologo --verbosity quiet
    
    # Build Release
    Write-Step "3/4" "Building Release..."
    if (-not (Invoke-Build -Configuration "Release")) {
        Write-Error "Build failed! Aborting."
        return
    }

    # Deploy Artifacts
    Write-Step "3.5/4" "Deploying to Plugin Folder..."
    Deploy-Artifacts
    
    # Start Stream Deck
    Write-Step "4/4" "Starting Stream Deck..."
    Start-StreamDeck
    
    Write-Host ""
    Write-Success "✅ Full pipeline completed!"
}

function Invoke-Restart {
    Write-Header "Restarting Stream Deck"
    
    Stop-StreamDeck
    Start-StreamDeck
    
    Write-Success "Stream Deck restarted!"
}

function Invoke-Exec {
    Write-Host ""
    Write-Host "Executing: " -NoNewline -ForegroundColor Cyan
    Write-Host "dotnet $($AdditionalArgs -join ' ')" -ForegroundColor White
    Write-Host ""
    
    & $DOTNET_EXE @AdditionalArgs
}

function Show-Help {
    Write-Host ""
    Write-Host "Pin to Deck - Development Script" -ForegroundColor Cyan
    Write-Host "=================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Usage: .\dev.ps1 <command>" -ForegroundColor White
    Write-Host ""
    Write-Host "Commands:" -ForegroundColor Yellow
    Write-Host "  install  - Setup development environment (check .NET SDK, NuGet sources)"
    Write-Host "  build    - Build the plugin (Debug configuration)"
    Write-Host "  clean    - Clean build artifacts and optionally remove deployed plugin"
    Write-Host "  test     - Run unit tests"
    Write-Host "  watch    - Watch for file changes and rebuild automatically"
    Write-Host "  deploy   - Build (Release) and optionally restart Stream Deck"
    Write-Host "  full     - Clean → Stop SD → Build Release → Start SD (one-shot)"
    Write-Host "  restart  - Restart Stream Deck application"
    Write-Host "  exec     - Run dotnet command (alias for dotnet CLI)"
    Write-Host "  help     - Show this help message"
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  .\dev.ps1 install   # Setup development environment"
    Write-Host "  .\dev.ps1 build     # Build the plugin"
    Write-Host "  .\dev.ps1 clean     # Clean all build artifacts"
    Write-Host "  .\dev.ps1 test      # Run unit tests"
    Write-Host "  .\dev.ps1 deploy    # Build and deploy for testing"
    Write-Host "  .\dev.ps1 full      # Full pipeline (recommended for testing)"
    Write-Host "  .\dev.ps1 exec test --filter FullyQualifiedName~AppLauncher  # Run specific tests"
    Write-Host ""
}

# === Main Entry Point ===
switch ($Command) {
    "install" { Invoke-Install }
    "build" { Invoke-Build }
    "clean" { Invoke-Clean }
    "test" { Invoke-Test }
    "watch" { Invoke-Watch }
    "deploy" { Invoke-Deploy }
    "full" { Invoke-Full }
    "restart" { Invoke-Restart }
    "exec" { Invoke-Exec }
    "help" { Show-Help }
    default { Show-Help }
}
