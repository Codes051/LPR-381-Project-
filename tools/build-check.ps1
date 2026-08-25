# Builds and runs the project WITHOUT the .NET SDK.
#
# If you have the .NET 8 SDK installed, ignore this script and use `dotnet build` /
# `dotnet run` instead. This exists because Visual Studio Build Tools ships MSBuild and
# the Roslyn compiler but no SDK, and on such a machine `dotnet` does not exist at all.
# It compiles the source tree directly against the .NET Framework reference assemblies,
# which is enough to type-check and run this project - we use no SDK-only APIs.
#
# Usage:
#   .\tools\build-check.ps1                                        build only
#   .\tools\build-check.ps1 -Run                                   build and run interactively
#   .\tools\build-check.ps1 -Run -StdIn "1|samples/lp_max.txt|0"   build and drive the menu
#
# -StdIn takes pipe separated menu keystrokes, which is the quickest way to exercise a
# solver repeatedly without clicking through the menu by hand.

param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [switch]$Run,
    [string]$StdIn = ""
)

$ErrorActionPreference = "Stop"

$out = Join-Path $env:TEMP "lpr_buildcheck"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# The project sets ImplicitUsings, which is an SDK feature the raw compiler knows nothing
# about, so the same set of usings has to be supplied by hand.
@'
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text;
'@ | Set-Content -Path "$out\GlobalUsings.cs" -Encoding utf8

$csc = Get-ChildItem "C:\Program Files (x86)\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe" -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $csc) {
    Write-Host "Could not find csc.exe. Install Visual Studio Build Tools, or install the"
    Write-Host ".NET 8 SDK and use 'dotnet build' instead."
    exit 1
}

$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$exe = "$out\solve.exe"

$files = @(
    Get-ChildItem "$Repo\src\solve" -Recurse -Filter *.cs | Select-Object -ExpandProperty FullName
) + "$out\GlobalUsings.cs"

Remove-Item $exe -ErrorAction SilentlyContinue

& $csc /nologo /langversion:latest /target:exe /out:$exe `
    /r:"$fw\mscorlib.dll" /r:"$fw\System.dll" /r:"$fw\System.Core.dll" /nostdlib+ $files

if (-not (Test-Path $exe)) {
    Write-Host "BUILD FAILED"
    exit 1
}

Write-Host "BUILD OK -> $exe"

if ($Run) {
    Push-Location $Repo
    try {
        if ($StdIn -ne "") { $StdIn -split "\|" | & $exe } else { & $exe }
    }
    finally { Pop-Location }
}
