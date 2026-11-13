param([string]$Repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference = 'Stop'
$output = Join-Path $Repository 'artifacts/material-checks'
& (Join-Path $Repository 'build.ps1') -TaskbarExperiment:$false -OutputDirectory $output
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework = Split-Path $compiler
$references = @('System.dll', 'System.Core.dll', 'System.Numerics.dll', 'System.Runtime.WindowsRuntime.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll',
    'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'System32/WinMetadata') -Filter '*.winmd' |
    ForEach-Object { '/reference:' + $_.FullName }
foreach ($facade in @('System.Runtime', 'System.Runtime.InteropServices', 'System.ObjectModel', 'System.Numerics.Vectors')) {
    $references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET/assembly/GAC_MSIL/' + $facade)) -Recurse -Filter '*.dll' |
        ForEach-Object { '/reference:' + $_.FullName }
}
$sources = Get-ChildItem -LiteralPath (Join-Path $Repository 'src/CCCC') -Filter '*.cs' | ForEach-Object { $_.FullName }
$arguments = @('/nologo', '/optimize+', '/define:TRACE,PRESENTATION_CHECKS', '/platform:x64', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $Repository 'assets/app.manifest')), '/target:exe', '/main:CCCC.MaterialChecks',
    ('/out:' + (Join-Path $output 'CCCC.MaterialChecks.exe')),
    ('/resource:' + (Join-Path $Repository 'assets/prank/repository-qr.txt') + ',CCCC.Prank.RepositoryQr')) + $references + $sources +
    @((Join-Path $PSScriptRoot 'MaterialChecks.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Material check compilation failed.' }
Write-Output (Join-Path $output 'CCCC.MaterialChecks.exe')
