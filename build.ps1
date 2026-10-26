param(
    [Parameter(Mandatory=$true)][ValidateSet('Mono','IL2CPP')][string]$Target,
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$ManagedDirectory,
    [string]$RuntimeDirectory,
    [string]$CompilerPath
)
$ErrorActionPreference='Stop'
$GameRoot=(Resolve-Path -LiteralPath $GameRoot).Path
$outputDirectory=Join-Path $PSScriptRoot ('bin\'+$Target)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$core=Join-Path $GameRoot 'BepInEx\core'
$sources=@('TextSourceInspector.cs','DialogueContextBridge.cs','SakuraTraceLog.cs') |
    ForEach-Object {Join-Path $PSScriptRoot ('src\'+$_)}
$options=@('/nologo','/target:library','/optimize+')
if($Target -eq 'Mono') {
    if(!$CompilerPath) {$CompilerPath=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'}
    if(!$ManagedDirectory) {
        $candidates=@(Get-ChildItem -LiteralPath $GameRoot -Directory -Filter '*_Data' |
            ForEach-Object {Join-Path $_.FullName 'Managed'} | Where-Object {Test-Path -LiteralPath $_})
        if($candidates.Count -ne 1) {throw 'Specify -ManagedDirectory for this environment.'}
        $ManagedDirectory=$candidates[0]
    }
    $references=@((Join-Path $core 'BepInEx.dll'),(Join-Path $core '0Harmony.dll'))
    $references+=@('UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll',
        'UnityEngine.TextRenderingModule.dll','netstandard.dll') |
        ForEach-Object {Join-Path $ManagedDirectory $_}
    $dllName='TextSourceInspector.dll'
} else {
    if(!$CompilerPath) {throw 'Specify a Roslyn csc.exe with -CompilerPath.'}
    if(!$RuntimeDirectory) {$RuntimeDirectory=Join-Path $GameRoot 'dotnet'}
    $options+=@('/noconfig','/nostdlib+','/define:IL2CPP')
    $references=@('System.Private.CoreLib.dll','System.Runtime.dll','System.Collections.dll',
        'System.IO.FileSystem.dll','System.Runtime.Extensions.dll','System.Runtime.InteropServices.dll',
        'System.Threading.dll','System.Reflection.dll','System.Diagnostics.TraceSource.dll',
        'System.Diagnostics.Debug.dll','System.Diagnostics.StackTrace.dll','System.Diagnostics.Tools.dll',
        'System.Linq.dll','netstandard.dll') | ForEach-Object {Join-Path $RuntimeDirectory $_}
    $references+=@('BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','Il2CppInterop.Runtime.dll','0Harmony.dll') |
        ForEach-Object {Join-Path $core $_}
    $references+=@('UnityEngine.CoreModule.dll','UnityEngine.TextRenderingModule.dll',
        'UnityEngine.IMGUIModule.dll','Il2Cppmscorlib.dll') |
        ForEach-Object {Join-Path $GameRoot ('BepInEx\interop\'+$_)}
    $sources+=Join-Path $PSScriptRoot 'src\TextSourceInspectorIL2CPP.cs'
    $dllName='TextSourceInspectorIL2CPP.dll'
}
foreach($file in @($CompilerPath)+@($references)+@($sources)) {
    if(!(Test-Path -LiteralPath $file -PathType Leaf)) {throw ('Missing dependency: '+$file)}
}
$destination=Join-Path $outputDirectory $dllName
$compilerArguments=$options+@('/out:'+$destination)+@($references | ForEach-Object {'/reference:'+$_})+@($sources)
& $CompilerPath $compilerArguments
if($LASTEXITCODE -ne 0) {throw ('Compilation failed: '+$LASTEXITCODE)}
Write-Output ('Built: '+$destination)
