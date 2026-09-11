[CmdletBinding()]
param(
    # Set this when Steam is installed in a non-standard location or when the
    # automatic Steam library search finds more than one Valheim installation.
    [string]$GamePath,

    # Build the DLL and create the ZIP, but do not copy anything into Valheim.
    [switch]$NoInstall,

    # Do not create a ZIP for sending to another player.
    [switch]$NoPackage
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\ValheimRandomizer.csproj"
$sourcePlugin = Join-Path $repoRoot "mod\BepInEx\plugins\ValheimRandomizer"
$configuration = "Release"

function Normalize-Path([string]$Path) {
    $Path = $Path.Trim().Trim('"')
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-ValheimDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $path = Normalize-Path $Path
    return (Test-Path (Join-Path $path "valheim.exe")) -and
           (Test-Path (Join-Path $path "valheim_Data"))
}

function Add-UniquePath([System.Collections.Generic.List[string]]$List, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try { $normalized = Normalize-Path $Path } catch { return }
    # Windows paths are case-insensitive, so avoid listing the same Steam
    # library twice when registry and VDF use different capitalization.
    if (-not ($List -contains $normalized)) {
        $null = $List.Add($normalized)
    }
}

function Add-Candidate([System.Collections.Generic.List[string]]$List, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try { $normalized = Normalize-Path $Path } catch { return }
    if (Test-ValheimDirectory $normalized) {
        Add-UniquePath $List $normalized
    }
}

function Get-RegistryValue([string]$Key, [string]$Name) {
    try {
        $value = Get-ItemPropertyValue -Path $Key -Name $Name -ErrorAction Stop
        if ($value) { return [string]$value }
    } catch {
        # The key is absent on some Steam installations. That is expected.
    }
    return $null
}

function Get-SteamRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $registryKeys = @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam"
    )

    foreach ($key in $registryKeys) {
        Add-UniquePath $roots (Get-RegistryValue $key "SteamPath")
        Add-UniquePath $roots (Get-RegistryValue $key "InstallPath")
    }

    if ($env:ProgramFiles) {
        Add-UniquePath $roots (Join-Path $env:ProgramFiles "Steam")
    }
    if (${env:ProgramFiles(x86)}) {
        Add-UniquePath $roots (Join-Path ${env:ProgramFiles(x86)} "Steam")
    }

    return $roots
}

function Get-SteamLibraryPaths([string]$SteamRoot) {
    $paths = New-Object System.Collections.Generic.List[string]
    Add-UniquePath $paths $SteamRoot

    $vdf = Join-Path $SteamRoot "steamapps\libraryfolders.vdf"
    if (-not (Test-Path $vdf)) { return $paths }

    # We only need the library paths. This deliberately avoids depending on a
    # VDF parser so the script can run on a clean Windows installation.
    foreach ($line in Get-Content -LiteralPath $vdf) {
        if ($line -match '"path"\s+"([^"]+)"') {
            $library = $Matches[1].Replace('\\', '\')
            Add-UniquePath $paths $library
        }
    }

    return $paths
}

function Find-ValheimInstallations {
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($steamRoot in Get-SteamRoots) {
        foreach ($library in Get-SteamLibraryPaths $steamRoot) {
            Add-Candidate $candidates (Join-Path $library "steamapps\common\Valheim")
        }
    }

    return $candidates
}

function Select-GamePath {
    if (-not [string]::IsNullOrWhiteSpace($GamePath)) {
        $explicit = Normalize-Path $GamePath
        if (-not (Test-ValheimDirectory $explicit)) {
            throw "Valheim was not found at: $explicit"
        }
        return $explicit
    }

    $found = @(Find-ValheimInstallations)
    if ($found.Count -eq 1) {
        return $found[0]
    }

    if ($found.Count -gt 1) {
        Write-Host "Multiple Valheim installations found:" -ForegroundColor Yellow
        for ($i = 0; $i -lt $found.Count; $i++) {
            Write-Host "[$($i + 1)] $($found[$i])"
        }
        $choice = Read-Host "Choose an installation number"
        $number = 0
        if (-not [int]::TryParse($choice, [ref]$number) -or
            $number -lt 1 -or $number -gt $found.Count) {
            throw "Invalid installation number. Use -GamePath to specify the path manually."
        }
        return $found[$number - 1]
    }

    $entered = Read-Host "Valheim was not found automatically. Enter its path manually"
    $entered = Normalize-Path $entered
    if (-not (Test-ValheimDirectory $entered)) {
        throw "Valheim was not found at: $entered"
    }
    return $entered
}

function Find-JotunnDll([string]$ValheimPath) {
    $expected = Join-Path $ValheimPath "BepInEx\plugins\Jotunn.dll"
    if (Test-Path $expected) { return (Normalize-Path $expected) }

    $plugins = Join-Path $ValheimPath "BepInEx\plugins"
    if (Test-Path $plugins) {
        $found = @(Get-ChildItem -LiteralPath $plugins -Filter "Jotunn.dll" -File -Recurse)
        if ($found.Count -gt 0) { return $found[0].FullName }
    }

    throw "Jotunn.dll was not found in $plugins. Install Jotunn before building."
}

function Assert-RequiredFiles([string]$ValheimPath, [string]$JotunnPath) {
    $required = @(
        (Join-Path $ValheimPath "BepInEx\core\0Harmony.dll"),
        (Join-Path $ValheimPath "BepInEx\core\BepInEx.dll"),
        (Join-Path $ValheimPath "BepInEx\core\BepInEx.Harmony.dll"),
        (Join-Path $ValheimPath "BepInEx\core\BepInEx.Preloader.dll"),
        (Join-Path $ValheimPath "valheim_Data\Managed\Assembly-CSharp.dll"),
        (Join-Path $ValheimPath "valheim_Data\Managed\assembly_guiutils.dll"),
        (Join-Path $ValheimPath "valheim_Data\Managed\assembly_utils.dll"),
        (Join-Path $ValheimPath "valheim_Data\Managed\assembly_valheim.dll"),
        (Join-Path $ValheimPath "valheim_Data\Managed\UnityEngine.dll"),
        $JotunnPath
    )

    $missing = @($required | Where-Object { -not (Test-Path $_) })
    if ($missing.Count -gt 0) {
        Write-Host "Required build files are missing:" -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "  $_" }
        throw "Check BepInEx, Jotunn, and the Valheim files."
    }
}

function Invoke-DotNet([string[]]$Arguments) {
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

function Backup-File([string]$Path, [string]$Stamp) {
    if (Test-Path $Path) {
        $backupDir = Join-Path $repoRoot "build\backups"
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        $backup = Join-Path $backupDir "ValheimRandomizer.dll.$Stamp.bak"
        Copy-Item -LiteralPath $Path -Destination $backup -Force
        Write-Host "Backup: $backup" -ForegroundColor DarkGray
    }
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet was not found. Install .NET SDK 8 or newer and try again."
    }

    if (-not (Test-Path $project)) { throw "Project was not found: $project" }
    if (-not (Test-Path $sourcePlugin)) { throw "Repository dependencies were not found: $sourcePlugin" }

    $selectedGamePath = Select-GamePath
    $jotunnPath = Find-JotunnDll $selectedGamePath
    Assert-RequiredFiles $selectedGamePath $jotunnPath

    Write-Host "Game:       $selectedGamePath" -ForegroundColor Green
    Write-Host "Jotunn:     $jotunnPath" -ForegroundColor Green
    Write-Host "Repository: $repoRoot" -ForegroundColor Green

    $buildProperties = @(
        "-p:ValheimInstallDir=$selectedGamePath",
        "-p:JotunnDllPath=$jotunnPath"
    )

    $restoreArguments = @("restore", $project) + $buildProperties
    $buildArguments = @("build", $project, "-c", $configuration, "--no-restore") + $buildProperties
    Invoke-DotNet $restoreArguments
    Invoke-DotNet $buildArguments

    $builtDll = Join-Path $repoRoot "src\bin\$configuration\netstandard2.1\ValheimRandomizer.dll"
    if (-not (Test-Path $builtDll)) { throw "The built DLL was not found: $builtDll" }

    $pluginDir = Join-Path $selectedGamePath "BepInEx\plugins\ValheimRandomizer"
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $installedDll = Join-Path $pluginDir "ValheimRandomizer.dll"

    if (-not $NoInstall) {
        $valheimProcess = Get-Process -Name "valheim" -ErrorAction SilentlyContinue
        if ($valheimProcess) {
            throw "Valheim is running. Close the game and try again."
        }

        Backup-File $installedDll $stamp
        Copy-Item -LiteralPath $builtDll -Destination $installedDll -Force
        Write-Host "New DLL installed: $installedDll" -ForegroundColor Green

        # Do not overwrite existing TSV files: they may contain custom content
        # required by the current Archipelago run. Copy them only on a fresh
        # installation.
        foreach ($name in @("Archipelago.MultiClient.Net.dll", "Newtonsoft.Json.dll", "research.tsv", "trophies.tsv")) {
            $destination = Join-Path $pluginDir $name
            $source = Join-Path $sourcePlugin $name
            if (-not (Test-Path $destination) -and (Test-Path $source)) {
                Copy-Item -LiteralPath $source -Destination $destination
                Write-Host "Added missing file: $name" -ForegroundColor DarkGray
            }
        }
    } else {
        Write-Host "Installation skipped because of -NoInstall." -ForegroundColor Yellow
    }

    if (-not $NoPackage) {
        $versionText = "0.0.0"
        $versionLine = Get-Content (Join-Path $repoRoot "src\ValheimRandomizer.cs") | Where-Object { $_.Contains("public const string ModVersion") } | Select-Object -First 1
        if ($versionLine) {
            $firstQuote = $versionLine.IndexOf('"')
            $secondQuote = $versionLine.IndexOf('"', $firstQuote + 1)
            if ($firstQuote -ge 0 -and $secondQuote -gt $firstQuote) {
                $versionText = $versionLine.Substring($firstQuote + 1, $secondQuote - $firstQuote - 1)
            }
        }

        $packageRoot = Join-Path $repoRoot "build\ValheimRandomizer-chat"
        $packagePlugin = Join-Path $packageRoot "BepInEx\plugins\ValheimRandomizer"
        $packageZip = Join-Path $repoRoot "build\ValheimRandomizer-chat-$versionText.zip"

        Remove-Item $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $packageZip -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $packagePlugin -Force | Out-Null

        # Package only the files belonging to this mod. For TSV files, prefer
        # the files from the current installation so custom content is kept.
        $packageNames = @(
            "ValheimRandomizer.dll",
            "Archipelago.MultiClient.Net.dll",
            "Newtonsoft.Json.dll",
            "research.tsv",
            "trophies.tsv"
        )
        foreach ($name in $packageNames) {
            $destination = Join-Path $packagePlugin $name
            if ($name -eq "ValheimRandomizer.dll") {
                Copy-Item -LiteralPath $builtDll -Destination $destination -Force
                continue
            }

            $installed = Join-Path $pluginDir $name
            $source = Join-Path $sourcePlugin $name
            if (Test-Path $installed) {
                Copy-Item -LiteralPath $installed -Destination $destination -Force
            } elseif (Test-Path $source) {
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
        }

        Compress-Archive -Path (Join-Path $packageRoot "BepInEx") -DestinationPath $packageZip -Force
        Write-Host "Archive for friend: $packageZip" -ForegroundColor Green
    }

    Write-Host "Done. Restart Valheim and connect to the same Archipelago room." -ForegroundColor Green
} catch {
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
