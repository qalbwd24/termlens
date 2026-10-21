param([string]$OutputName = 'TermLens.exe',[switch]$Share)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Net.Http.dll', 'System.Web.Extensions.dll', 'System.Security.dll', 'System.Speech.dll', 'Microsoft.CSharp.dll', 'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll')
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName) { throw 'OutputName must be a file name within the app folder.' }
$argsForCompiler = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + (Join-Path $taskRoot $OutputName)))
$argsForCompiler += '/win32icon:' + (Join-Path $taskRoot 'assets\TermLens.ico')
foreach ($reference in $refs) {
    $referencePath = Join-Path $framework $reference
    if ($reference -eq 'System.Speech.dll' -and -not (Test-Path -LiteralPath $referencePath)) {
        $referencePath = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Recurse -Filter 'System.Speech.dll' | Select-Object -First 1 -ExpandProperty FullName
    }
    $argsForCompiler += '/reference:' + $referencePath
}
$sources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
foreach ($mathDll in @('WpfMath.dll','XamlMath.Shared.dll')) {
    $argsForCompiler += '/reference:' + (Join-Path $taskRoot ('lib\' + $mathDll))
    Copy-Item -LiteralPath (Join-Path $taskRoot ('lib\' + $mathDll)) -Destination $taskRoot -Force
}
if ($Share) { $argsForCompiler += '/define:SHARE'; $sources = $sources | Where-Object { $_ -notlike '*Tests.cs' } }
& $compiler @argsForCompiler @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-FileHash -LiteralPath (Join-Path $taskRoot $OutputName) -Algorithm SHA256
