param(
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dist = Join-Path $root 'dist'
$projDir = Join-Path $root 'src/BIMRadar.Warnings'

# tfm folder -> Revit year whose build output is used
$builds = [ordered]@{ 'net48' = '2022'; 'net8' = '2025'; 'net10' = '2027' }
$files = @('BIMRadar.Warnings.dll', 'BIMRadar.Warnings.pdb')

function Get-BuildDir([string]$year) {
    $dir = Join-Path $projDir "bin/$Configuration/$year"
    if (-not (Test-Path (Join-Path $dir 'BIMRadar.Warnings.dll'))) {
        throw "Build output missing: $dir/BIMRadar.Warnings.dll. Build RevitVersion=$year first."
    }
    return $dir
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

# ---- pyRevit ----
$pyDir = Join-Path $dist 'pyRevit'
$extDir = Join-Path $pyDir 'BIMRadar-Warnings.extension'
New-Item -ItemType Directory -Path $pyDir | Out-Null
Copy-Item (Join-Path $root 'extension/BIMRadar-Warnings.extension') $extDir -Recurse
foreach ($tfm in $builds.Keys) {
    $src = Get-BuildDir $builds[$tfm]
    $dst = Join-Path $extDir "bin/$tfm"
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    foreach ($f in $files) {
        $p = Join-Path $src $f
        if (Test-Path $p) { Copy-Item $p $dst }
    }
}

# ---- Standalone ----
$saDir = Join-Path $dist 'Standalone'
New-Item -ItemType Directory -Path $saDir | Out-Null
Copy-Item (Join-Path $root 'installer/BIMRadar.Warnings.addin') $saDir
Copy-Item (Join-Path $root 'installer/install.bat') $saDir
Copy-Item (Join-Path $root 'installer/uninstall.bat') $saDir
$readme = @(
    'BIMRadar Warnings Manager - standalone install',
    '1. Close Revit, unzip this folder anywhere.',
    '2. Double-click install.bat (per user, Revit 2022-2027; no admin needed).',
    '3. Start Revit: BIMRadar tab > Health panel. Hidden if the pyRevit extension is registered.',
    '4. To remove: double-click uninstall.bat.',
    'If Revit says the add-in can''t be loaded: right-click the zip > Properties > Unblock, then install again.'
)
Set-Content -Path (Join-Path $saDir 'README.txt') -Value $readme -Encoding ASCII
foreach ($tfm in $builds.Keys) {
    $src = Get-BuildDir $builds[$tfm]
    $dst = Join-Path $saDir $tfm
    New-Item -ItemType Directory -Path $dst | Out-Null
    foreach ($f in $files) {
        $p = Join-Path $src $f
        if (Test-Path $p) { Copy-Item $p $dst }
    }
}

# ---- Zips ----
Compress-Archive -Path $extDir -DestinationPath (Join-Path $dist 'BIMRadar-Warnings-pyRevit.zip') -Force
Compress-Archive -Path (Join-Path $saDir '*') -DestinationPath (Join-Path $dist 'BIMRadar-Warnings-Standalone.zip') -Force

Get-ChildItem $dist -Filter *.zip | ForEach-Object { Write-Host ("{0}  {1} bytes" -f $_.Name, $_.Length) }
