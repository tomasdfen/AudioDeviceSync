# Compiles AudioDeviceSync, installs it for the current user and registers it to start at logon.
# No admin rights needed. Re-running upgrades the existing install.
$ErrorActionPreference = 'Stop'

$taskName = 'AudioDeviceSync'
$src      = Join-Path $PSScriptRoot 'AudioDeviceSync.cs'
$dir      = Join-Path $env:LOCALAPPDATA 'AudioDeviceSync'
$exe      = Join-Path $dir 'AudioDeviceSync.exe'
$csc      = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "No se encuentra csc.exe ($csc)" }

# Stop a running copy so the exe can be overwritten
Get-Process AudioDeviceSync -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

New-Item -ItemType Directory -Force $dir | Out-Null
& $csc /nologo /target:winexe /optimize+ "/out:$exe" $src
if ($LASTEXITCODE -ne 0) { throw 'La compilacion fallo' }

$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

Start-ScheduledTask -TaskName $taskName
Write-Host "Instalado en $dir y programado al iniciar sesion. Log: $dir\log.txt"
