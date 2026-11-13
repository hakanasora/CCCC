param(
    [string]$FontDirectory = '',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/build'),
    [switch]$CheckAnimation,
    [switch]$TaskbarExperiment = $true,
    [switch]$CheckTaskbar,
    [switch]$CheckQuality,
    [switch]$CheckPrank
)
$ErrorActionPreference = 'Stop'

# Installed Windows fonts are the default. An explicit local font directory
# opts into the original embedded vivo faces; never copy system font binaries.
$embedFonts = -not [string]::IsNullOrWhiteSpace($FontDirectory)
$fontNames = @('vivoSans-Medium.ttf', 'vivoSans-Bold.ttf')
if ($embedFonts) {
    foreach ($font in $fontNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $FontDirectory $font) -PathType Leaf)) {
            throw "Missing explicitly requested local font: $font. See assets/fonts/README.md."
        }
    }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw '.NET Framework 4.8 x64 build components are required.'
}
$framework = Split-Path $compiler
if ($CheckTaskbar -and -not $TaskbarExperiment) { throw '-CheckTaskbar requires -TaskbarExperiment.' }
if ($TaskbarExperiment -and $CheckAnimation) { throw 'Use -CheckTaskbar for the offscreen experiment checks.' }
$references = @('System.dll', 'System.Core.dll', 'System.Numerics.dll', 'System.Runtime.WindowsRuntime.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll',
    'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll')
if ($TaskbarExperiment) { $references += @('WPF/UIAutomationClient.dll', 'WPF/UIAutomationTypes.dll') }
$referenceArguments = @()
foreach ($reference in $references) {
    $path = Join-Path $framework $reference
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing .NET/WPF reference: $reference" }
    $referenceArguments += '/reference:' + $path
}
$metadata = Join-Path $env:WINDIR 'System32/WinMetadata'
if (-not (Test-Path -LiteralPath $metadata -PathType Container)) { throw 'Windows WinMetadata is required.' }
$referenceArguments += Get-ChildItem -LiteralPath $metadata -Filter '*.winmd' | ForEach-Object { '/reference:' + $_.FullName }
foreach ($facade in @('System.Runtime', 'System.Runtime.InteropServices', 'System.ObjectModel', 'System.Numerics.Vectors')) {
    $folder = Join-Path $env:WINDIR ('Microsoft.NET/assembly/GAC_MSIL/' + $facade)
    $assemblies = @(Get-ChildItem -LiteralPath $folder -Recurse -Filter '*.dll')
    if ($assemblies.Count -eq 0) { throw "Missing WinRT facade: $facade" }
    $referenceArguments += $assemblies | ForEach-Object { '/reference:' + $_.FullName }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$fontResources = Join-Path $output 'fonts.g.resources'
if ($embedFonts) {
    $writer = [System.Resources.ResourceWriter]::new($fontResources)
    try {
        foreach ($font in $fontNames) {
            $stream = [System.IO.File]::OpenRead((Join-Path $FontDirectory $font))
            $writer.AddResource(('fonts/' + $font.ToLowerInvariant()), $stream, $true)
        }
        $writer.Generate()
    }
    finally { $writer.Dispose() }
}
function Get-FontResources([string]$assemblyName) {
    if ($embedFonts) { '/resource:' + $fontResources + ',' + $assemblyName + '.g.resources' }
}

$sourceFolder = Join-Path $PSScriptRoot 'src/CCCC'
$sources = @('Core.cs', 'Native.cs', 'Rendering.cs', 'CompositionWindow.cs', 'CompositionBitmap.cs', 'Acrylic.cs', 'Theme.cs',
    'NativeMenuTheme.cs', 'Motion.cs', 'Startup.cs', 'EmbeddedFonts.cs', 'TextPixelWorkspace.cs',
    'SampledText.cs', 'Toast.cs', 'ToastProfile.cs', 'TaskbarPlacement.cs', 'Preferences.cs',
    'PrankPaymentController.cs', 'PrankPlacement.cs', 'PrankBrandMarks.cs', 'PrankQrCode.cs', 'PrankPaymentWindow.cs', 'App.cs') | ForEach-Object { Join-Path $sourceFolder $_ }
$manifest = Join-Path $PSScriptRoot 'assets/app.manifest'
$common = @('/nologo', '/optimize+', '/define:TRACE', '/platform:x64', '/codepage:65001', ('/win32manifest:' + $manifest))
if ($TaskbarExperiment) { $common += '/define:TASKBAR_EXPERIMENT' }
if ($embedFonts) { $common += '/define:EMBED_VIVO_FONTS' }
$common += '/resource:' + (Join-Path $PSScriptRoot 'assets/prank/repository-qr.txt') + ',CCCC.Prank.RepositoryQr'
$arguments = $common + @('/target:winexe', ('/out:' + (Join-Path $output 'CCCC.exe')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets/CCCC.ico'))) + @(Get-FontResources 'CCCC') + $referenceArguments + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'CCCC build failed.' }
Write-Output ('Built ' + (Join-Path $output 'CCCC.exe'))
Write-Output ('Font mode: ' + $(if ($embedFonts) { 'explicit embedded vivo Sans' } else { 'installed Microsoft YaHei UI / Microsoft YaHei / Segoe UI' }))

if ($CheckQuality) {
    $name = 'CCCC.QualityChecks'
    $arguments = $common + @('/target:exe', ('/main:' + $name), ('/out:' + (Join-Path $output ($name + '.exe')))) + @(Get-FontResources $name) + $referenceArguments + $sources + @((Join-Path $PSScriptRoot 'tests/QualityChecks.cs'))
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Quality checks compile failed.' }
    & (Join-Path $output ($name + '.exe'))
    if ($LASTEXITCODE -ne 0) { throw 'Quality checks failed.' }
}

if ($CheckTaskbar) {
    $name = 'CCCC.TaskbarChecks'
    $arguments = $common + @('/target:exe', '/define:TESTS', ('/main:' + $name),
        ('/out:' + (Join-Path $output ($name + '.exe')))) + @(Get-FontResources $name) + $referenceArguments + $sources +
        @((Join-Path $PSScriptRoot 'tests/TaskbarChecks.cs'))
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar diagnostic build failed.' }
    & (Join-Path $output ($name + '.exe'))
    if ($LASTEXITCODE -ne 0) { throw 'Taskbar offscreen checks failed.' }
}

if ($CheckAnimation) {
    $name = 'CCCC.AnimationChecks'
    $arguments = $common + @('/target:exe', '/define:PRESENTATION_CHECKS', ('/main:' + $name),
        ('/out:' + (Join-Path $output ($name + '.exe')))) + @(Get-FontResources $name) + $referenceArguments + $sources +
        @((Join-Path $PSScriptRoot 'tests/AnimationChecks.cs'))
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Animation diagnostic build failed.' }
    & (Join-Path $output ($name + '.exe')) $output
    if ($LASTEXITCODE -ne 0) { throw 'Animation checks failed.' }
}

if ($CheckPrank) {
    & (Join-Path $PSScriptRoot 'tests/build-prank.ps1') -OutputDirectory (Join-Path $output 'prank')
}
