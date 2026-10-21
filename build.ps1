param(
    [string]$FontDirectory = (Join-Path $PSScriptRoot 'assets/fonts'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts/build'),
    [switch]$CheckAnimation
)
$ErrorActionPreference = 'Stop'

# Keep the two original faces and the original WPF resource names. Fonts are
# local build inputs, never downloaded, installed or substituted by this script.
$fontNames = @('vivoSans-Medium.ttf', 'vivoSans-Bold.ttf')
foreach ($font in $fontNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $FontDirectory $font) -PathType Leaf)) {
        throw "Missing local font: $font. See assets/fonts/README.md or pass -FontDirectory."
    }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw '.NET Framework 4.8 x64 build components are required.'
}
$framework = Split-Path $compiler
$references = @('System.dll', 'System.Core.dll', 'System.Numerics.dll', 'System.Runtime.WindowsRuntime.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll',
    'WPF/WindowsBase.dll', 'WPF/PresentationCore.dll', 'WPF/PresentationFramework.dll')
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
$writer = [System.Resources.ResourceWriter]::new($fontResources)
try {
    foreach ($font in $fontNames) {
        $stream = [System.IO.File]::OpenRead((Join-Path $FontDirectory $font))
        $writer.AddResource(('fonts/' + $font.ToLowerInvariant()), $stream, $true)
    }
    $writer.Generate()
}
finally { $writer.Dispose() }

$sourceFolder = Join-Path $PSScriptRoot 'src/CCCC'
$sources = @('Core.cs', 'Native.cs', 'Rendering.cs', 'CompositionWindow.cs', 'Acrylic.cs', 'Theme.cs',
    'NativeMenuTheme.cs', 'Motion.cs', 'Startup.cs', 'EmbeddedFonts.cs', 'TextPixelWorkspace.cs',
    'SampledText.cs', 'Toast.cs', 'App.cs') | ForEach-Object { Join-Path $sourceFolder $_ }
$manifest = Join-Path $PSScriptRoot 'assets/app.manifest'
$common = @('/nologo', '/optimize+', '/platform:x64', '/codepage:65001', ('/win32manifest:' + $manifest))
$arguments = $common + @('/target:winexe', ('/out:' + (Join-Path $output 'CCCC.exe')),
    ('/win32icon:' + (Join-Path $PSScriptRoot 'assets/CCCC.ico')),
    ('/resource:' + $fontResources + ',CCCC.g.resources')) + $referenceArguments + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'CCCC build failed.' }
Write-Output ('Built ' + (Join-Path $output 'CCCC.exe'))

if ($CheckAnimation) {
    $name = 'CCCC.AnimationChecks'
    $arguments = $common + @('/target:exe', '/define:PRESENTATION_CHECKS', ('/main:' + $name),
        ('/out:' + (Join-Path $output ($name + '.exe'))),
        ('/resource:' + $fontResources + ',' + $name + '.g.resources')) + $referenceArguments + $sources +
        @((Join-Path $PSScriptRoot 'tests/AnimationChecks.cs'))
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Animation diagnostic build failed.' }
    & (Join-Path $output ($name + '.exe')) $output
    if ($LASTEXITCODE -ne 0) { throw 'Animation checks failed.' }
}
