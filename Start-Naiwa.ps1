param(
    [switch]$SmokeTest,
    [switch]$AudioTest,
    [switch]$AudioSettings,
    [switch]$Benchmark,
    [ValidateSet('idle','listen','laugh','rest')][string]$BenchmarkAnimation = 'idle'
)

$ErrorActionPreference = 'Stop'
if (!$SmokeTest -and !$AudioTest -and !$Benchmark) {
    & (Join-Path $PSScriptRoot 'Build-Naiwa.ps1') -Quiet
    $arguments = @()
    if ($AudioSettings) { $arguments += '--audio-settings' }
    $launch = @{ FilePath = (Join-Path $PSScriptRoot 'NaiwaDesktopPet.exe'); WorkingDirectory = $PSScriptRoot }
    if ($arguments.Count -gt 0) { $launch.ArgumentList = $arguments }
    Start-Process @launch
    return
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, Accessibility
$sources = @('NaiwaPet.cs', 'AnimationClip.cs', 'NativeRendering.cs', 'LaughRig.cs', 'BlinkRig.cs', 'BlinkCache.cs', 'RestRig.cs', 'AudioPlayback.cs', 'SoundOptionSlider.cs', 'MouseInteraction.cs', 'FlightMotion.cs', 'FlightRig.cs', 'AboutMenu.cs')
Add-Type -Path @($sources | ForEach-Object { Join-Path $PSScriptRoot $_ }) -ReferencedAssemblies @('System.Windows.Forms', 'System.Drawing', 'Accessibility')

$assetDir = Join-Path $PSScriptRoot 'assets\naiwa'
[NaiwaDesktopPet.DisplayQuality]::Enable()
[System.Windows.Forms.Application]::SetUnhandledExceptionMode([System.Windows.Forms.UnhandledExceptionMode]::ThrowException)
[System.Windows.Forms.Application]::EnableVisualStyles()
if ($AudioTest) { [NaiwaDesktopPet.Pet]::AudioSmokeTest($assetDir); return }
if ($SmokeTest) { [NaiwaDesktopPet.Pet]::SmokeTest($assetDir); return }
if ($Benchmark) {
    $reportDir = Join-Path ([System.IO.Path]::GetTempPath()) 'NaiwaDesktopPet-checks'
    [System.IO.Directory]::CreateDirectory($reportDir) | Out-Null
    [NaiwaDesktopPet.Pet]::Benchmark($assetDir, (Join-Path $reportDir ('performance-' + $BenchmarkAnimation + '.json')), $BenchmarkAnimation)
    return
}
