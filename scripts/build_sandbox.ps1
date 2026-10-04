<#
.SYNOPSIS
    Builds the sandbox's windowed development player (spec 04): Builds\Sandbox\QALabSandbox.exe.
.DESCRIPTION
    Unity.exe -batchmode -quit -projectPath unity\QALabSandbox
              -executeMethod MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer
              -qalabBuildOut <Out> -logFile Builds\build.log
    Development build, Mono, the scenes in Build Settings (Tools > QA Lab > Rebuild Sandbox Scenes sets
    them). The commit is stamped into the build so run.json records it. Close the sandbox in the Unity
    editor first: a project can be open in one editor at a time.
    Exit code: 0 built, 1 build failed or Unity not found (see the log).
.EXAMPLE
    scripts\build_sandbox.ps1
#>
param(
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "Builds\Sandbox\QALabSandbox.exe")
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo "unity\QALabSandbox"
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
$buildsDir = Join-Path $repo "Builds"
New-Item -ItemType Directory -Force -Path $buildsDir | Out-Null
$log = Join-Path $buildsDir "build.log"

$unity = & (Join-Path $PSScriptRoot "find_unity.ps1")
if (-not $unity) {
    Write-Host "build_sandbox: not built (Unity editor not found)" -ForegroundColor Red
    exit 1
}

$unityArgs = @(
    "-batchmode", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer",
    "-qalabBuildOut", "`"$Out`"",
    "-logFile", "`"$log`""
)
Write-Host "build_sandbox: building $Out (log: $log)"
$started = Get-Date
# Wait for Unity itself, not Start-Process -Wait (which also waits for Unity's child processes).
$process = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -NoNewWindow
$null = $process.Handle   # keep a handle open, or ExitCode can come back empty after the exit
$process.WaitForExit()
$code = $process.ExitCode
$seconds = [int]((Get-Date) - $started).TotalSeconds

if ($code -ne 0 -or -not (Test-Path $Out)) {
    Write-Host "build_sandbox: FAILED (Unity exit $code, ${seconds} s); see $log" -ForegroundColor Red
    exit 1
}
Write-Host "build_sandbox: built $Out in ${seconds} s" -ForegroundColor Green
exit 0
