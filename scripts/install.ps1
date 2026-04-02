# Stardew Valley Dedicated Server - Windows Dependency Check
# Verifies required software is installed

Write-Host "=== Stardew Valley Dedicated Server - Dependency Check ===" -ForegroundColor Cyan
Write-Host ""

$allGood = $true

# Check .NET 6 Runtime
Write-Host "Checking .NET Runtime..." -NoNewline
try {
    $dotnetVersions = dotnet --list-runtimes 2>&1
    if ($dotnetVersions -match "Microsoft\.NETCore\.App 6\.") {
        Write-Host " OK" -ForegroundColor Green
    } else {
        Write-Host " MISSING" -ForegroundColor Red
        Write-Host "  -> Install .NET 6 Runtime from: https://dotnet.microsoft.com/download/dotnet/6.0" -ForegroundColor Yellow
        $allGood = $false
    }
} catch {
    Write-Host " NOT FOUND" -ForegroundColor Red
    Write-Host "  -> Install .NET 6 Runtime from: https://dotnet.microsoft.com/download/dotnet/6.0" -ForegroundColor Yellow
    $allGood = $false
}

# Check if game directory exists (look for common locations)
Write-Host "Checking Stardew Valley installation..." -NoNewline
$commonPaths = @(
    "$env:ProgramFiles (x86)\Steam\steamapps\common\Stardew Valley",
    "$env:ProgramFiles\Steam\steamapps\common\Stardew Valley",
    "C:\GOG Games\Stardew Valley",
    "$env:USERPROFILE\GOG Games\Stardew Valley"
)

$foundGame = $false
foreach ($path in $commonPaths) {
    if (Test-Path "$path\Stardew Valley.exe") {
        Write-Host " OK ($path)" -ForegroundColor Green
        $foundGame = $true
        break
    }
}

if (-not $foundGame) {
    Write-Host " NOT FOUND (not critical for server)" -ForegroundColor Yellow
    Write-Host "  -> Game will be installed via SteamCMD or AMP" -ForegroundColor Yellow
}

# Check SMAPI
Write-Host "Checking SMAPI..." -NoNewline
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameDir = Split-Path -Parent $scriptDir

if (Test-Path "$gameDir\StardewModdingAPI.exe") {
    Write-Host " OK" -ForegroundColor Green
} else {
    Write-Host " NOT INSTALLED" -ForegroundColor Yellow
    Write-Host "  -> Download SMAPI from: https://smapi.io" -ForegroundColor Yellow
    Write-Host "  -> Install it to: $gameDir" -ForegroundColor Yellow
}

# Check mod
Write-Host "Checking Dedicated Server Mod..." -NoNewline
if (Test-Path "$gameDir\Mods\StardewDedicatedServer\StardewDedicatedServer.dll") {
    Write-Host " OK" -ForegroundColor Green
} else {
    Write-Host " NOT INSTALLED" -ForegroundColor Yellow
    Write-Host "  -> Copy StardewDedicatedServer to: $gameDir\Mods\" -ForegroundColor Yellow
}

Write-Host ""
if ($allGood) {
    Write-Host "All dependencies satisfied!" -ForegroundColor Green
} else {
    Write-Host "Some dependencies are missing. Install them before running the server." -ForegroundColor Red
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Install Stardew Valley (via Steam, GOG, or SteamCMD)"
Write-Host "  2. Install SMAPI (https://smapi.io)"
Write-Host "  3. Copy StardewDedicatedServer mod to Mods/ folder"
Write-Host "  4. Run: scripts\start.bat"
Write-Host ""
