param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/prank-checks'),
    [switch]$Ui,
    [switch]$CompileApplication
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$framework = Split-Path $compiler
$references = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Core.cs', 'Preferences.cs', 'ToastProfile.cs', 'PrankPaymentController.cs', 'PrankPlacement.cs', 'PrankBrandMarks.cs', 'PrankQrCode.cs', 'PrankPaymentWindow.cs') |
    ForEach-Object { Join-Path $root ('src/CCCC/' + $_) }
$resource = '/resource:' + (Join-Path $root 'assets/prank/repository-qr.txt') + ',CCCC.Prank.RepositoryQr'
$common = @('/nologo', '/langversion:5', '/optimize+', '/platform:x64', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $root 'assets/app.manifest')), $resource)
foreach ($profile in @('card', 'taskbar')) {
    $exe = Join-Path $output ('CCCC.PrankChecks-' + $profile + '.exe')
    $arguments = $common + @('/target:exe', '/main:CCCC.PrankChecks', ('/out:' + $exe)) + $references + $sources + (Join-Path $PSScriptRoot 'PrankChecks.cs')
    if ($profile -eq 'taskbar') { $arguments += '/define:TASKBAR_EXPERIMENT' }
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Prank checks compile failed.' }
    if ($Ui) { & $exe '--ui' $output } else { & $exe $output }
    if ($LASTEXITCODE -ne 0) { throw 'Prank checks failed.' }
}
$preview = Join-Path $output 'CCCC.PrankPreview.exe'
& $compiler @common /target:winexe /main:CCCC.PrankPreview "/out:$preview" @references @sources (Join-Path $PSScriptRoot 'PrankPreview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Prank preview compile failed.' }
if ($CompileApplication) {
    # Compile the actual production sources in both profiles using the default
    # installed fonts. These extra executables remain compilation diagnostics.
    $references += @('System.Numerics.dll', 'System.Runtime.WindowsRuntime.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF/UIAutomationClient.dll', 'WPF/UIAutomationTypes.dll') |
        ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'System32/WinMetadata') -Filter '*.winmd' |
        ForEach-Object { '/reference:' + $_.FullName }
    foreach ($facade in @('System.Runtime', 'System.Runtime.InteropServices', 'System.ObjectModel', 'System.Numerics.Vectors')) {
        $references += Get-ChildItem -LiteralPath (Join-Path $env:WINDIR ('Microsoft.NET/assembly/GAC_MSIL/' + $facade)) -Recurse -Filter '*.dll' |
            ForEach-Object { '/reference:' + $_.FullName }
    }
    # Read the production list directly so a missed build integration cannot be
    # hidden by an independent wildcard source list in this diagnostic.
    $buildText = Get-Content -LiteralPath (Join-Path $root 'build.ps1') -Raw
    $sourceMatch = [regex]::Match($buildText, '(?s)\$sources = @\((.*?)\) \| ForEach-Object')
    $allSources = [regex]::Matches($sourceMatch.Groups[1].Value, "'([^']+\.cs)'") |
        ForEach-Object { Join-Path $root ('src/CCCC/' + $_.Groups[1].Value) }
    foreach ($profile in @('card', 'taskbar')) {
        $defines = @(if ($profile -eq 'taskbar') { '/define:TASKBAR_EXPERIMENT' })
        $argsBuild = $common + $defines + @('/target:winexe', ('/out:' + (Join-Path $output ('CCCC.CompileOnly-' + $profile + '.exe')))) + $references + $allSources
        & $compiler @argsBuild
        if ($LASTEXITCODE -ne 0) { throw ('Production-source compile failed: ' + $profile) }
        Write-Output ('PASS: production source compilation (' + $profile + '); installed Windows fonts.')
        $quality = Join-Path $output ('CCCC.QualityChecks-' + $profile + '.exe')
        & $compiler @common @defines /target:exe /main:CCCC.QualityChecks "/out:$quality" @references @allSources (Join-Path $PSScriptRoot 'QualityChecks.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Quality checks compile failed.' }
        & $quality
        if ($LASTEXITCODE -ne 0) { throw 'Quality checks failed.' }
    }
    $taskbarCheck = Join-Path $output 'CCCC.TaskbarChecks.exe'
    & $compiler @common /define:TASKBAR_EXPERIMENT,TESTS /target:exe /main:CCCC.TaskbarChecks "/out:$taskbarCheck" @references @allSources (Join-Path $PSScriptRoot 'TaskbarChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar checks compile failed.' }
    & $taskbarCheck
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar checks failed.' }
}
