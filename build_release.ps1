<#
.SYNOPSIS
    Builds release artifacts for Cowtastic Cafe's Archipelago integration.

.DESCRIPTION
    1. Rebuilds the .apworld: cleans __pycache__, deletes the old bundle, and
       re-zips Cowtastic/cowtastic_cafe/ as cowtastic_cafe.apworld (with the
       folder as the zip root, which AP requires).
    2. Packages the Unity build (default: Output/) into a versioned zip.
    3. Collects everything into release/ alongside a generated SETUP.txt.

    The Unity build itself is NOT produced here - build it from the Unity
    Editor (File > Build Settings > Build) into the -BuildDir folder first.
    Run this script afterwards to package it.

.PARAMETER BuildDir
    Folder containing the finished Unity build. Default: ./Output
    The folder's name becomes the root folder inside the game zip, so building
    into a nicely-named folder (e.g. "Cowtastic Cafe") gives a tidier archive.

.PARAMETER SkipGame
    Only rebuild the .apworld; don't touch the Unity build.

.EXAMPLE
    ./build_release.ps1
.EXAMPLE
    ./build_release.ps1 -BuildDir "C:\builds\Cowtastic Cafe"
.EXAMPLE
    ./build_release.ps1 -SkipGame
#>
[CmdletBinding()]
param(
    [string]$BuildDir   = (Join-Path $PSScriptRoot 'Output'),
    [string]$ReleaseDir = (Join-Path $PSScriptRoot 'release'),
    [switch]$SkipGame
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression           # ZipArchive, ZipArchiveMode
Add-Type -AssemblyName System.IO.Compression.FileSystem # ZipFile, ZipFileExtensions

$WorldName  = 'cowtastic_cafe'
$WorldDir   = Join-Path $PSScriptRoot "Cowtastic\$WorldName"
$ApworldOut = Join-Path $PSScriptRoot "Cowtastic\$WorldName.apworld"

function New-Zip {
    # Builds entries manually with forward-slash separators. .NET's
    # CreateFromDirectory uses the OS separator (backslash on Windows), which
    # breaks Python's zipimport - the mechanism Archipelago uses to load
    # apworlds. Forward slashes are also universally safe for the game zip.
    param([string]$SourceDir, [string]$DestZip, [bool]$IncludeRootFolder)
    if (Test-Path $DestZip) { Remove-Item $DestZip -Force }

    $SourceDir = (Resolve-Path $SourceDir).Path.TrimEnd('\')
    $parent    = Split-Path $SourceDir -Parent
    $baseLen   = if ($IncludeRootFolder) { $parent.Length + 1 } else { $SourceDir.Length + 1 }

    $zip = [System.IO.Compression.ZipFile]::Open(
        $DestZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $SourceDir -Recurse -File) {
            $rel = $file.FullName.Substring($baseLen) -replace '\\', '/'
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $rel,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
}

# --- Read version from the manifest --------------------------------------
if (-not (Test-Path $WorldDir)) { throw "World source not found: $WorldDir" }
$manifest = Get-Content (Join-Path $WorldDir 'archipelago.json') -Raw | ConvertFrom-Json
$version  = $manifest.world_version
Write-Host "Cowtastic Cafe release - apworld v$version" -ForegroundColor Cyan

# --- Fresh release dir ---------------------------------------------------
if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir | Out-Null

# --- 1. Build the .apworld ----------------------------------------------
Write-Host "`n[1/3] Building .apworld..." -ForegroundColor Yellow

# Strip compiled-python cruft so it doesn't end up in the bundle.
Get-ChildItem $WorldDir -Recurse -Directory -Filter '__pycache__' -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force
Get-ChildItem $WorldDir -Recurse -File -Filter '*.pyc' -ErrorAction SilentlyContinue |
    Remove-Item -Force

# IncludeRootFolder = $true  ->  zip contains "cowtastic_cafe/..." (required).
$tmpZip = Join-Path $PSScriptRoot "Cowtastic\$WorldName.zip"
New-Zip -SourceDir $WorldDir -DestZip $tmpZip -IncludeRootFolder $true
if (Test-Path $ApworldOut) { Remove-Item $ApworldOut -Force }
Move-Item $tmpZip $ApworldOut
Copy-Item $ApworldOut (Join-Path $ReleaseDir "$WorldName.apworld")
Write-Host "  -> $WorldName.apworld" -ForegroundColor Green

# --- 2. Package the Unity build -----------------------------------------
if (-not $SkipGame) {
    Write-Host "`n[2/3] Packaging Unity build..." -ForegroundColor Yellow
    if (-not (Test-Path $BuildDir) -or
        -not (Get-ChildItem $BuildDir -ErrorAction SilentlyContinue)) {
        Write-Warning "  Build folder is missing or empty: $BuildDir"
        Write-Warning "  Build from the Unity Editor first, then re-run (or use -SkipGame)."
    } else {
        $gameZip = Join-Path $ReleaseDir "CowtasticCafe-Windows-v$version.zip"
        # IncludeRootFolder = $true -> players extract one clean folder.
        New-Zip -SourceDir $BuildDir -DestZip $gameZip -IncludeRootFolder $true
        $sizeMb = [math]::Round((Get-Item $gameZip).Length / 1MB, 1)
        $leaf   = Split-Path $gameZip -Leaf
        Write-Host ("  -> {0} ({1} MB)" -f $leaf, $sizeMb) -ForegroundColor Green
    }
} else {
    Write-Host "`n[2/3] Skipping Unity build (-SkipGame)." -ForegroundColor DarkGray
}

# --- 3. Generate SETUP.txt ----------------------------------------------
Write-Host "`n[3/3] Writing SETUP.txt..." -ForegroundColor Yellow
$setup = @"
Cowtastic Cafe - Archipelago Setup (apworld v$version)
======================================================

WHAT'S IN THIS RELEASE
  $WorldName.apworld              The Archipelago world definition.
  CowtasticCafe-Windows-v$version.zip   The modded game (Windows).

INSTALL THE APWORLD (needed to generate a multiworld)
  1. Install Archipelago: https://github.com/ArchipelagoMW/Archipelago/releases
  2. Double-click $WorldName.apworld  - OR -  copy it into:
       <Archipelago install>\custom_worlds\
  3. Restart the Archipelago Launcher.

MAKE A YAML
  1. In the Launcher, open "Generate Template Options" and grab the
     Cowtastic Cafe template, or write a yaml with these options:
       drinks_per_check:       how many drinks per ingredient = 1 check
       checks_per_ingredient:  how many checks each ingredient grants
       shop_locations:         how many shop-slot checks exist
  2. Put your yaml in the Players/ folder and Generate.

PLAY
  1. Unzip CowtasticCafe-Windows-v$version.zip and run the game.
  2. On the main menu, enter the server host, port, and your slot name,
     then click Connect. The game loads on a successful connection.

GOAL
  Collect all milk-capacity upgrades to reach maximum milk.
"@
Set-Content -Path (Join-Path $ReleaseDir 'SETUP.txt') -Value $setup -Encoding utf8
Write-Host "  -> SETUP.txt" -ForegroundColor Green

Write-Host "`nDone. Release contents in: $ReleaseDir" -ForegroundColor Cyan
foreach ($f in Get-ChildItem $ReleaseDir) {
    $sz = if ($f.PSIsContainer) { '<dir>' }
          else { '{0} KB' -f [math]::Round($f.Length / 1KB, 1) }
    Write-Host ("  {0,-40} {1}" -f $f.Name, $sz)
}
