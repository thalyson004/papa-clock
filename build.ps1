param([switch]$Test, [switch]$GuiTest)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Instale o .NET Framework 4.8 para compilar.' }
$output = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Force -Path $output | Out-Null

Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(39,92,65))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235,246,228)),4
$pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$graphics.FillEllipse($brush,2,2,60,60)
$graphics.DrawEllipse($pen,13,13,38,38)
$graphics.DrawLine($pen,32,22,32,33)
$graphics.DrawLine($pen,32,33,42,38)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $projectRoot 'assets\clock.ico'
$stream = [System.IO.File]::Create($iconPath)
try { $icon.Save($stream) } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose(); $brush.Dispose() }

$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll')
& $compiler /nologo /utf8output /target:winexe /platform:anycpu /optimize+ "/win32manifest:$projectRoot\assets\app.manifest" "/win32icon:$iconPath" "/out:$output\PapaClock.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação do aplicativo.' }
Write-Output "Aplicativo compilado: $output\PapaClock.exe"

if ($Test) {
    & $compiler /nologo /utf8output /target:exe "/out:$output\CoreTests.exe" @references (Join-Path $projectRoot 'src\Core.cs') (Join-Path $projectRoot 'tests\CoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação dos testes.' }
    & (Join-Path $output 'CoreTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes.' }
}
if ($GuiTest) {
    & $compiler /nologo /utf8output /target:exe /main:PapaClock.GuiTests "/out:$output\GuiTests.exe" "/win32manifest:$projectRoot\assets\app.manifest" @references @sources (Join-Path $projectRoot 'tests\GuiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação dos testes de interface.' }
    & (Join-Path $output 'GuiTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de interface.' }
}
