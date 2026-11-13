param([string]$PreviewDirectory = (Join-Path $env:TEMP 'NaiwaDesktopPet-checks'))
$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, Accessibility
$sources = @('NaiwaPet.cs', 'AnimationClip.cs', 'NativeRendering.cs', 'LaughRig.cs', 'BlinkRig.cs', 'BlinkCache.cs', 'RestRig.cs', 'AudioPlayback.cs', 'SoundOptionSlider.cs', 'MouseInteraction.cs', 'FlightMotion.cs', 'FlightRig.cs') | ForEach-Object { Join-Path $petRoot $_ }
Add-Type -Path ($sources + (Join-Path $PSScriptRoot 'FlightRegression.cs')) -ReferencedAssemblies @('System.Windows.Forms', 'System.Drawing', 'Accessibility')
[FlightRegression]::Run((Join-Path $petRoot 'assets\naiwa'), $PreviewDirectory)
