# Builds vmrdp.dll (the display-only console client) against FreeRDP from vcpkg.
# First run: `vcpkg install` here fetches and compiles FreeRDP 3 (long). Later runs only rebuild the shim.
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot = & $vswhere -latest -products * -property installationPath
if (-not $vsRoot) { throw 'Visual Studio not found' }
# CMake generator for whatever VS is installed: 17 -> 2022, 18 -> 2026 (GitHub runners carry 2022).
$vsMajor = [int]((& $vswhere -latest -products * -property installationVersion) -split '\.')[0]
$generator = switch ($vsMajor) { 17 { 'Visual Studio 17 2022' } 18 { 'Visual Studio 18 2026' } default { throw "Unsupported Visual Studio $vsMajor" } }
# vcpkg: the VS-bundled copy, else VCPKG_ROOT / VCPKG_INSTALLATION_ROOT (GitHub runners).
$vcpkg = Join-Path $vsRoot 'VC\vcpkg\vcpkg.exe'
if (-not (Test-Path $vcpkg)) {
    $root = if ($env:VCPKG_ROOT) { $env:VCPKG_ROOT } else { $env:VCPKG_INSTALLATION_ROOT }
    if (-not $root) { throw 'vcpkg not found' }
    $vcpkg = Join-Path $root 'vcpkg.exe'
}
$cmake = Join-Path $vsRoot 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) { $cmake = 'cmake' }
$env:VCPKG_ROOT = Split-Path $vcpkg

Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'vcpkg_installed\x64-windows\lib\freerdp3.lib')) {
        & $vcpkg install --triplet x64-windows
        if ($LASTEXITCODE) { throw "vcpkg install failed ($LASTEXITCODE)" }
    }
    & $cmake -S . -B build -G $generator -A x64 `
        "-DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_ROOT\scripts\buildsystems\vcpkg.cmake" `
        '-DVCPKG_TARGET_TRIPLET=x64-windows' '-DVCPKG_MANIFEST_MODE=ON' '-DVCPKG_APPLOCAL_DEPS=ON'
    if ($LASTEXITCODE) { throw "cmake configure failed ($LASTEXITCODE)" }
    & $cmake --build build --config $Configuration
    if ($LASTEXITCODE) { throw "cmake build failed ($LASTEXITCODE)" }
    Get-ChildItem "build\$Configuration\*.dll" | Select-Object Name, Length
}
finally { Pop-Location }
