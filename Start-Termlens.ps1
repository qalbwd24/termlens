[CmdletBinding()]
param([switch]$NoHotkey,[switch]$Tray,[switch]$Settings,[switch]$Stop)
$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'TermLens.exe'
if(-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Build TermLens.exe first.' }
$arguments=@()
if($Stop) { $arguments=@('--exit','--quiet') }
else {
    if($NoHotkey) { $arguments+='--no-hotkey' }
    if($Tray) { $arguments+='--tray' }
    if($Settings) { $arguments+='--settings' }
}
$launch=@{FilePath=$exe; WorkingDirectory=$PSScriptRoot; WindowStyle='Hidden'; PassThru=$true}
if($arguments.Count) { $launch.ArgumentList=$arguments }
$process=Start-Process @launch
if($Stop) {
    if(-not $process.WaitForExit(25000) -or $process.ExitCode -ne 0) { throw 'Normal exit failed; inspect launcher.log.' }
} elseif($process.WaitForExit(1200) -and $process.ExitCode -ne 0) { throw 'Launch failed; inspect launcher.log.' }
