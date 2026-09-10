<# Build ValheimRandomizer.dll
   Usage:
     .\build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\Valheim" `
                 -ProfileDir "C:\Users\YOU\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\arta" `
                 -Install
   -GameDir    : Steam game folder (contains valheim.exe and valheim_Data). Asks if omitted.
   -ProfileDir : r2modman profile folder (contains BepInEx). Omit for classic BepInEx installs.
   -Install    : also copy the built DLL (+ archipelago libs, + TSVs if missing) into the profile.
#>
param(
    [string]$GameDir = "",
    [string]$ProfileDir = "",
    [switch]$Install
)
$ErrorActionPreference = 'Stop'

if (-not $GameDir) { $GameDir = Read-Host 'Path to Valheim game folder (with valheim.exe)' }
if (-not (Test-Path (Join-Path $GameDir 'valheim_Data/Managed/Assembly-CSharp.dll'))) {
    throw "Not a Valheim game folder (missing valheim_Data/Managed/Assembly-CSharp.dll): $GameDir"
}
$env:ValheimInstallDir = $GameDir

if ($ProfileDir) {
    if (-not (Test-Path (Join-Path $ProfileDir 'BepInEx/core/BepInEx.dll'))) {
        throw "Not an r2modman profile folder (missing BepInEx/core/BepInEx.dll): $ProfileDir"
    }
    $env:BepInExDir = $ProfileDir
    # Jotunn location differs between installs: find it instead of guessing.
    $jotunn = Get-ChildItem (Join-Path $ProfileDir 'BepInEx/plugins') -Filter 'Jotunn.dll' -Recurse -Depth 3 -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $jotunn) { throw "Jotunn.dll not found in profile. Install Jotunn in r2modman first." }
    $env:JotunnDll = $jotunn.FullName
    Write-Host "Jotunn: $($jotunn.FullName)"
}

$csproj = Join-Path $PSScriptRoot 'src/ValheimRandomizer.csproj'
dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }

$dll = Join-Path $PSScriptRoot 'src/bin/Release/netstandard2.1/ValheimRandomizer.dll'
Write-Host ""
Write-Host "Built: $dll" -ForegroundColor Green

if ($Install) {
    if (-not $ProfileDir) { throw "-Install needs -ProfileDir" }
    $dest = Join-Path $ProfileDir 'BepInEx/plugins/ValheimRandomizer'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $outDir = Split-Path $dll
    foreach ($lib in @('ValheimRandomizer.dll', 'Archipelago.MultiClient.Net.dll', 'Newtonsoft.Json.dll')) {
        $src = Join-Path $outDir $lib
        if (Test-Path $src) { Copy-Item $src $dest -Force; Write-Host "Copied: $lib" }
    }
    foreach ($tsv in @('research.tsv', 'trophies.tsv')) {
        $target = Join-Path $dest $tsv
        if (-not (Test-Path $target)) {
            Copy-Item (Join-Path $PSScriptRoot "mod/BepInEx/plugins/ValheimRandomizer/$tsv") $dest
            Write-Host "Copied: $tsv (was missing)"
        }
    }
    Write-Host ""
    Write-Host "Installed to: $dest" -ForegroundColor Green
    Write-Host "Disable the old ValheimRandomizer mod in r2modman if you had one, then launch the game."
}
