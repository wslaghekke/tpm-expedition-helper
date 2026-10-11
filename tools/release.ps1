<#
.SYNOPSIS
  Build release zips of Expedition Helper and (with -Publish) upload them as a GitHub release.

.DESCRIPTION
  Builds Release (no dev bridge) against the local game's BepInEx interop assemblies — they're
  generated from the game, so this can't run on a GitHub-hosted runner — and creates in dist/<version>/:

    ExpeditionHelper-<version>.zip               the mod only (for an existing BepInEx install)
    ExpeditionHelper-<version>-with-BepInEx.zip  BepInEx 6 be.788 + config this game needs + the mod

  Both unzip straight into the game folder. With -Publish it also tags v<version>, pushes the tag
  and creates the GitHub release (needs a clean working tree and `gh` logged in).

.EXAMPLE
  tools/release.ps1 -Version 0.1.0             # build zips only
  tools/release.ps1 -Version 0.1.0 -Publish    # build, tag, push tag, create release
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $Publish,
    [string] $GameDir   # defaults to Directory.Build.props / Directory.Build.user.props
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

# BepInEx build bundled in the full zip (the one the mod is developed and tested against).
$bepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'
$bepInExSha256 = 'f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a'
# Everything inside the BepInEx zip, with its upstream repo (license texts are fetched from there).
# Verified 2026-10: all allow redistribution; LGPL parts are shipped unmodified as replaceable DLLs.
$components = [ordered]@{
    'BepInEx'            = @('BepInEx/BepInEx', 'LGPL-2.1', 'BepInEx/core/BepInEx.*')
    'UnityDoorstop'      = @('NeighTools/UnityDoorstop', 'LGPL-2.1', 'winhttp.dll')
    'Il2CppInterop'      = @('BepInEx/Il2CppInterop', 'LGPL-3.0', 'BepInEx/core/Il2CppInterop.*')
    'HarmonyX'           = @('BepInEx/HarmonyX', 'MIT', 'BepInEx/core/0Harmony.dll')
    'MonoMod'            = @('MonoMod/MonoMod', 'MIT', 'BepInEx/core/MonoMod.*')
    'Mono.Cecil'         = @('jbevain/cecil', 'MIT', 'BepInEx/core/Mono.Cecil*')
    'Cpp2IL'             = @('SamboyCoding/Cpp2IL', 'MIT', 'BepInEx/core/Cpp2IL.Core.dll, LibCpp2IL.dll, StableNameDotNet.dll, WasmDisassembler.dll')
    'Disarm'             = @('SamboyCoding/Disarm', 'MIT', 'BepInEx/core/Disarm.dll')
    'AsmResolver'        = @('Washi1337/AsmResolver', 'MIT', 'BepInEx/core/AsmResolver*')
    'AssetRipper.CIL'    = @('AssetRipper/AssetRipper.CIL', 'MIT', 'BepInEx/core/AssetRipper.CIL.dll')
    'AssetRipper.Primitives' = @('AssetRipper/AssetRipper.Primitives', 'MIT', 'BepInEx/core/AssetRipper.Primitives.dll')
    'Iced'               = @('icedland/iced', 'MIT', 'BepInEx/core/Iced.dll')
    'Capstone.NET'       = @('9ee1/Capstone.NET', 'MIT', 'BepInEx/core/Gee.External.Capstone.dll')
    'SemanticVersioning' = @('adamreeve/semver.net', 'MIT', 'BepInEx/core/SemanticVersioning.dll')
    'Dobby'              = @('jmpews/Dobby', 'Apache-2.0', 'BepInEx/core/dobby.dll')
    '.NET runtime'       = @('dotnet/runtime', 'MIT', 'dotnet/ (plus its THIRD-PARTY-NOTICES)')
    'MsQuic'             = @('microsoft/msquic', 'MIT', 'dotnet/msquic.dll')
}

function Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

# Compress-Archive (Windows PowerShell 5.1) stores paths with backslashes, which some extractors
# (Linux/Proton tools, mod managers) turn into file names; write standard '/' entries instead.
function New-Zip($sourceDir, $zipPath) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $sourceDir = (Resolve-Path $sourceDir).Path.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($file in Get-ChildItem $sourceDir -Recurse -File -Force) {
            $entry = $file.FullName.Substring($sourceDir.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, 'Optimal')
        }
    }
    finally { $zip.Dispose() }
}

# ---- checks ---------------------------------------------------------------------------------
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$tag = "v$Version"

$pluginVersion = (Select-String -Path src/ExpeditionHelper.Host/Plugin.cs -Pattern 'const string Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($pluginVersion -ne $Version) { throw "Plugin.Version in src/ExpeditionHelper.Host/Plugin.cs is '$pluginVersion'; set it to '$Version' and commit first." }

if ($Publish) {
    if (git status --porcelain) { throw 'Working tree has uncommitted changes; commit them first.' }
    if (git tag --list $tag) { throw "Tag $tag already exists." }
    if (git ls-remote --tags origin "refs/tags/$tag") { throw "Tag $tag already exists on origin." }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw 'gh is not logged in (run: gh auth login).' }
}

# ---- build ----------------------------------------------------------------------------------
Step "Building Release $Version"
$buildArgs = @('build', 'src/ExpeditionHelper.Logic', '-c', 'Release', '-p:DeployToGame=false', '--nologo', '-v:q')
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$hostDll = 'src/ExpeditionHelper.Host/bin/Release/ExpeditionHelper.Host.dll'
$logicDll = 'src/ExpeditionHelper.Logic/bin/Release/ExpeditionHelper.Logic.dll'

$dist = Join-Path $root "dist/$Version"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
$stageMod = Join-Path $dist 'stage-mod'
$stageFull = Join-Path $dist 'stage-full'

# ---- mod-only zip ---------------------------------------------------------------------------
Step 'Packaging the mod'
$pluginDir = Join-Path $stageMod 'BepInEx/plugins/ExpeditionHelper'
New-Item -ItemType Directory -Force (Join-Path $pluginDir 'reload') | Out-Null
Copy-Item $hostDll $pluginDir
Copy-Item $logicDll (Join-Path $pluginDir 'reload')
Copy-Item LICENSE (Join-Path $pluginDir 'LICENSE.txt')
@"
Expedition Helper $Version for Two Point Museum
https://github.com/wslaghekke/tpm-expedition-helper

This zip contains only the mod. It needs BepInEx 6 (IL2CPP, bleeding edge be.788) installed in
the game folder, with this setting in BepInEx/config/BepInEx.cfg (the game crashes on start
otherwise):

  [Logging]
  UnityLogListening = false

If you don't have BepInEx yet, use ExpeditionHelper-$Version-with-BepInEx.zip instead.

Install: extract this zip into the game folder (the one with TPM.exe), so that
BepInEx/plugins/ExpeditionHelper/ is created. Settings: in game under Settings > Expedition Helper,
or in BepInEx/config/tpm.expeditionhelper.cfg.
"@ | Set-Content -Encoding utf8 (Join-Path $pluginDir 'README.txt')
$modZip = Join-Path $dist "ExpeditionHelper-$Version.zip"
New-Zip $stageMod $modZip

# ---- full zip (BepInEx + mod) ---------------------------------------------------------------
Step 'Fetching BepInEx and license texts'
$cache = Join-Path $root 'dist/cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$bepInExZip = Join-Path $cache 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788.zip'
if (-not (Test-Path $bepInExZip)) { Invoke-WebRequest $bepInExUrl -OutFile $bepInExZip }
$hash = (Get-FileHash $bepInExZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $bepInExSha256) { Remove-Item $bepInExZip; throw "BepInEx download has an unexpected SHA-256 ($hash)." }

# License texts via the GitHub API (follows each repo's default branch and license file name).
function Get-Text($apiPath, $outFile) {
    if (Test-Path $outFile) { return }
    $text = gh api $apiPath -H 'Accept: application/vnd.github.raw'
    if ($LASTEXITCODE -ne 0 -or -not $text) { throw "Couldn't fetch $apiPath" }
    ($text -join "`n") | Set-Content -Encoding utf8 $outFile
}
$licenseCache = Join-Path $cache 'licenses'
New-Item -ItemType Directory -Force $licenseCache | Out-Null
foreach ($name in $components.Keys) {
    Get-Text "repos/$($components[$name][0])/license" (Join-Path $licenseCache "$name-LICENSE.txt")
}
Get-Text 'repos/dotnet/runtime/contents/THIRD-PARTY-NOTICES.TXT' (Join-Path $licenseCache '.NET runtime-THIRD-PARTY-NOTICES.txt')
# LGPL-3.0 is a set of additional permissions on top of the GPL-3.0, whose text must accompany it.
if (-not (Test-Path (Join-Path $licenseCache 'GPL-3.0.txt'))) {
    gh api licenses/gpl-3.0 --jq .body | Set-Content -Encoding utf8 (Join-Path $licenseCache 'GPL-3.0.txt')
}

Step 'Packaging the mod with BepInEx'
Expand-Archive $bepInExZip -DestinationPath $stageFull
Copy-Item (Join-Path $stageMod 'BepInEx') $stageFull -Recurse -Force
# Only the setting this game needs; BepInEx fills in the rest with defaults on first start.
New-Item -ItemType Directory -Force (Join-Path $stageFull 'BepInEx/config') | Out-Null
@"
## Two Point Museum: Unity log listening crashes the game (Denuvo), so it must stay off.
[Logging]
UnityLogListening = false
"@ | Set-Content -Encoding utf8 (Join-Path $stageFull 'BepInEx/config/BepInEx.cfg')

$licenseDir = Join-Path $stageFull 'licenses'
New-Item -ItemType Directory -Force $licenseDir | Out-Null
Copy-Item (Join-Path $licenseCache '*') $licenseDir
$rows = foreach ($name in $components.Keys) {
    $c = $components[$name]
    "  {0,-24} {1,-11} https://github.com/{2}`n  {3,-24} files: {4}" -f $name, $c[1], $c[0], '', $c[2]
}
@"
This package bundles BepInEx 6.0.0-be.788 (IL2CPP, win-x64), unmodified, as published at
$bepInExUrl

It contains the following components. Each one's license text is in this folder
(<name>-LICENSE.txt; GPL-3.0.txt accompanies the LGPL-3.0). Source code is available at the
links. The LGPL-licensed parts are separate, unmodified DLLs and can be replaced with any
compatible build.

$($rows -join "`n")
"@ | Set-Content -Encoding utf8 (Join-Path $licenseDir 'THIRD-PARTY-NOTICES.txt')

@"
Expedition Helper $Version for Two Point Museum, with BepInEx 6 (be.788)
https://github.com/wslaghekke/tpm-expedition-helper

Install:
  1. Open the game folder (Steam: right-click Two Point Museum > Manage > Browse local files).
  2. Extract everything in this zip into that folder (next to TPM.exe).
  3. Start the game. The first start takes a few minutes while BepInEx prepares itself;
     later starts are normal.

Settings: in game under Settings > Expedition Helper, or BepInEx/config/tpm.expeditionhelper.cfg.
Uninstall: delete winhttp.dll (disables all BepInEx mods), or the BepInEx folder, doorstop_config.ini,
.doorstop_version and the dotnet folder.
"@ | Set-Content -Encoding utf8 (Join-Path $stageFull 'ExpeditionHelper-INSTALL.txt')
$fullZip = Join-Path $dist "ExpeditionHelper-$Version-with-BepInEx.zip"
New-Zip $stageFull $fullZip

Remove-Item $stageMod, $stageFull -Recurse -Force
Step 'Built:'
Get-ChildItem $dist -Filter *.zip | ForEach-Object { '  {0}  ({1:N0} KB)' -f $_.FullName, ($_.Length / 1KB) }

# ---- publish --------------------------------------------------------------------------------
if (-not $Publish) { Write-Host "Not published (add -Publish to tag $tag and create the GitHub release)."; return }

Step "Tagging $tag and creating the GitHub release"
$previous = git describe --tags --abbrev=0 2>$null
$range = if ($previous) { "$previous..HEAD" } else { 'HEAD' }
$changes = (git log $range --no-merges --format='- %s') -join "`n"
$notes = @"
## Install
- **New to mods?** Download ``ExpeditionHelper-$Version-with-BepInEx.zip`` and extract it into the game folder (next to ``TPM.exe``). The first start takes a few minutes.
- **Already have BepInEx 6 (IL2CPP be.788)?** Download ``ExpeditionHelper-$Version.zip`` and extract it into the game folder. ``UnityLogListening`` must be ``false`` in ``BepInEx/config/BepInEx.cfg``.

## Changes
$changes
"@
git tag -a $tag -m "Expedition Helper $Version"
git push origin $tag
gh release create $tag $modZip $fullZip --title "Expedition Helper $Version" --notes $notes
if ($LASTEXITCODE -ne 0) { throw 'gh release create failed (the tag was pushed; delete it or retry the release).' }
