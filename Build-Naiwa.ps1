param([switch]$Force, [switch]$Quiet)
$ErrorActionPreference = 'Stop'
$sources = @('ApplicationEntry.cs', 'NaiwaPet.cs', 'AnimationClip.cs', 'NativeRendering.cs', 'LaughRig.cs', 'BlinkRig.cs', 'BlinkCache.cs', 'RestRig.cs', 'AudioPlayback.cs', 'SoundOptionSlider.cs', 'MouseInteraction.cs', 'FlightMotion.cs', 'FlightRig.cs')
$inputs = @($sources | ForEach-Object { Join-Path $PSScriptRoot $_ })
$icon = Join-Path $PSScriptRoot 'assets\naiwa\naiwa-face-rounded.ico'
$executable = Join-Path $PSScriptRoot 'NaiwaDesktopPet.exe'
$requiresBuild = $Force -or !(Test-Path -LiteralPath $executable)
if (!$requiresBuild) {
    $builtAt = [IO.File]::GetLastWriteTimeUtc($executable)
    foreach ($file in ($inputs + @($icon, $PSCommandPath))) {
        if ([IO.File]::GetLastWriteTimeUtc($file) -gt $builtAt) { $requiresBuild = $true; break }
    }
}
if (!$requiresBuild) { if (!$Quiet) { Write-Output $executable }; return }
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler)) { throw '找不到 Windows 内置的 .NET Framework C# 编译器。' }
$temporaryExe = Join-Path $PSScriptRoot ('NaiwaDesktopPet-build-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    $arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', ('/out:' + $temporaryExe),
        ('/win32icon:' + $icon), '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:Accessibility.dll') + $inputs
    $messages = & $compiler @arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($messages -join [Environment]::NewLine) }
    Move-Item -LiteralPath $temporaryExe -Destination $executable -Force
}
finally { if (Test-Path -LiteralPath $temporaryExe) { Remove-Item -LiteralPath $temporaryExe } }
if (!$Quiet) { Write-Output $executable }
