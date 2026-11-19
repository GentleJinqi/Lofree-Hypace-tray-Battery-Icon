# Remakes src\app.ico, HypaceTray.exe's icon, from the photos in photos\ (and build\app-icon-preview.png to look at).
# build.ps1 does not run this: src\app.ico is committed.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc /nologo /codepage:65001 /optimize+ /target:exe /r:System.Drawing.dll /out:"$out\MakeIcon.exe" "$PSScriptRoot\MakeIcon.cs"
if ($LASTEXITCODE -ne 0) { throw "MakeIcon build failed" }
& "$out\MakeIcon.exe" "$root\photos" "$root\src\app.ico" "$out\app-icon-preview.png"
if ($LASTEXITCODE -ne 0) { throw "MakeIcon failed" }
