$ErrorActionPreference = 'SilentlyContinue'
Unregister-ScheduledTask -TaskName 'AudioDeviceSync' -Confirm:$false
Get-Process AudioDeviceSync | Stop-Process -Force
Start-Sleep -Milliseconds 300
Remove-Item (Join-Path $env:LOCALAPPDATA 'AudioDeviceSync') -Recurse -Force
Write-Host 'Desinstalado.'
