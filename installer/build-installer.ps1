# Builds the BsxtBatch Windows installer.
#
#   1. Publishes the WPF app as a self-contained single-file win-x64 exe.
#   2. Compiles installer\BsxtBatch.iss with Inno Setup (ISCC.exe).
#   3. Prints the resulting setup exe path.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.2.0

param(
    [string]$Version = '1.0.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $root 'src\BsxtBatch.App\BsxtBatch.App.csproj'
$publish  = Join-Path $root 'publish\BsxtBatch'
$iss      = Join-Path $PSScriptRoot 'BsxtBatch.iss'
$issSuffix = (Get-Date).ToString('yyyyMMdd-HHmm')

# --- locate ISCC.exe -------------------------------------------------------
$isccCandidates = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw @'
Inno Setup compiler (ISCC.exe) was not found.
Install it with one of:
    winget install --id JRSoftware.InnoSetup -e
    choco install innosetup
'@
}
Write-Host "Inno Setup compiler : $iscc"

# --- 1. publish ------------------------------------------------------------
if ($SkipPublish) {
    Write-Host 'Skipping publish (-SkipPublish).'
} else {
    Write-Host "Publishing $proj ..."
    & dotnet publish $proj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version `
        -o $publish --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
}

$exe = Join-Path $publish 'BsxtBatch.App.exe'
if (-not (Test-Path $exe)) { throw "Expected publish output missing: $exe" }

# --- 2. compile installer --------------------------------------------------
Write-Host "Compiling $iss ..."
& $iscc "/DMyAppVersion=$Version" "/DOutputDir=Output" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)." }

# --- 3. report -------------------------------------------------------------
$out = Join-Path $PSScriptRoot "Output\BsxtBatch-Setup-$Version.exe"
if (Test-Path $out) {
    $sizeMb = [math]::Round((Get-Item $out).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Installer built: $out ($sizeMb MB)"
} else {
    Write-Warning 'ISCC reported success but the expected output file was not found.'
}
