param([switch]$Stop)
# Local development never takes F13 from the personal v2 instance.
& (Join-Path $PSScriptRoot 'Start-Termlens.ps1') -NoHotkey -Stop:$Stop
