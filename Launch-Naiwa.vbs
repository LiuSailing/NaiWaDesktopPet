Option Explicit
Dim shell, fso, folder, scriptPath, powershellPath, command, executable, needsBuild, source, sources, candidate
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
executable = fso.BuildPath(folder, "NaiwaDesktopPet.exe")
needsBuild = Not fso.FileExists(executable)
If Not needsBuild Then
    sources = Array("ApplicationEntry.cs", "NaiwaPet.cs", "AnimationClip.cs", "NativeRendering.cs", "LaughRig.cs", "BlinkRig.cs", "BlinkCache.cs", "RestRig.cs", "AudioPlayback.cs", "SoundOptionSlider.cs", "MouseInteraction.cs", "FlightMotion.cs", "FlightRig.cs", "AboutMenu.cs", "Build-Naiwa.ps1", "assets\naiwa\naiwa-face-rounded.ico")
    For Each source In sources
        candidate = fso.BuildPath(folder, source)
        If fso.FileExists(candidate) Then
            If fso.GetFile(candidate).DateLastModified > fso.GetFile(executable).DateLastModified Then needsBuild = True
        End If
    Next
End If
If Not needsBuild Then
    shell.Run Chr(34) & executable & Chr(34), 1, False
    WScript.Quit 0
End If
scriptPath = fso.BuildPath(folder, "Start-Naiwa.ps1")
powershellPath = fso.BuildPath(shell.ExpandEnvironmentStrings("%SystemRoot%"), "System32\WindowsPowerShell\v1.0\powershell.exe")
command = Chr(34) & powershellPath & Chr(34) & " -NoProfile -STA -ExecutionPolicy Bypass -File " & Chr(34) & scriptPath & Chr(34)
shell.Run command, 0, False
