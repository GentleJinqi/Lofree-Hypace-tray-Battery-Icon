# Builds HypaceTray.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = $PSScriptRoot
$out = Join-Path $root 'build'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc /nologo /codepage:65001 /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /out:"$out\icontests.exe" "$root\src\BatteryIcon.cs" "$root\src\TaskbarBadge.cs" "$root\src\UiText.cs" "$root\src\MouseLook.cs" "$root\src\RealMouse.cs" "$root\src\PhotoStore.cs" "$root\tests\IconTests.cs"
if ($LASTEXITCODE -ne 0) { throw "icontests build failed" }

& $csc /nologo /codepage:65001 /optimize+ /target:winexe /platform:anycpu `
    /win32manifest:"$root\src\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /out:"$out\HypaceTray.exe" "$root\src\LofreeHid.cs" "$root\src\BatteryIcon.cs" "$root\src\TaskbarBadge.cs" "$root\src\UiText.cs" "$root\src\MouseLook.cs" "$root\src\RealMouse.cs" "$root\src\PhotoStore.cs" "$root\src\TrayApp.cs"
if ($LASTEXITCODE -ne 0) { throw "HypaceTray build failed" }

& $csc /nologo /codepage:65001 /target:exe /out:"$out\probe.exe" "$root\src\LofreeHid.cs" "$root\src\Probe.cs"
if ($LASTEXITCODE -ne 0) { throw "probe build failed" }

Write-Host "Built $out\HypaceTray.exe"
