param([switch]$Launch, [switch]$StartWithWindows, [switch]$NoDesktopShortcut)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'bin\PapaClock.exe'
if (-not (Test-Path -LiteralPath $source)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$destination = Join-Path $env:LOCALAPPDATA 'Programs\PapaClock'
$executable = Join-Path $destination 'PapaClock.exe'
$running = @(Get-Process -Name PapaClock -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable })
if ($running.Count -gt 0) { throw 'Feche o Papa Clock usando Sair no ícone da bandeja antes de reinstalar. Seus dados serão mantidos.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath $source -Destination $executable -Force
$readme = Join-Path $PSScriptRoot 'README.md'
if (Test-Path -LiteralPath $readme) { Copy-Item -LiteralPath $readme -Destination (Join-Path $destination 'LEIA-ME.md') -Force }
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @((Join-Path ([Environment]::GetFolderPath('Programs')) 'Papa Clock.lnk'))
if (-not $NoDesktopShortcut) { $shortcutPaths += Join-Path ([Environment]::GetFolderPath('Desktop')) 'Papa Clock.lnk' }
foreach ($shortcutPath in $shortcutPaths) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = "$executable,0"
    $shortcut.Description = 'Alarmes e temporizador com execução na bandeja do Windows'
    $shortcut.Save()
}
if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name PapaClock -Value ('"' + $executable + '" --background')
}
Write-Output "Instalado: $executable"
Write-Output 'Atalho criado no menu Iniciar.'
if (-not $NoDesktopShortcut) { Write-Output 'Atalho criado na Área de Trabalho.' }
if ($Launch) { Start-Process -FilePath $executable -WindowStyle Normal }
