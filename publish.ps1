# One self-contained VmView.exe in dist\ — runtime, Avalonia, vmrdp.dll and the FreeRDP/OpenSSL DLLs inside.
# Builds the native shim first when native\build\Release\vmrdp.dll is missing.
param([string]$Out = (Join-Path $PSScriptRoot 'dist'), [string]$Version)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path 'native\build\Release\vmrdp.dll')) { & .\native\build.ps1 }
    $props = @(); if ($Version) { $props += "-p:Version=$Version" }
    dotnet publish VmView.csproj -c Release -nologo @props
    if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }
    $exe = Get-ChildItem 'bin\Release\net10.0-windows\win-x64\publish\VmView.exe'
    New-Item -ItemType Directory -Force $Out | Out-Null
    Copy-Item $exe.FullName (Join-Path $Out 'VmView.exe') -Force
    '{0}  {1:N1} MB' -f (Join-Path $Out 'VmView.exe'), ($exe.Length / 1MB)
}
finally { Pop-Location }
