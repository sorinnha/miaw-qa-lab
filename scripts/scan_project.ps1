<#
.SYNOPSIS
    Runs QA Lab's ProjectScanner on a Unity project in batch mode (spec 01, M7) and prints a summary.
.DESCRIPTION
    Unity.exe -batchmode -projectPath <p> -executeMethod MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine
              -qalabScanOut <Out> -logFile <Out without .json>.log
    The project must have the QA Lab package installed (docs/GAME_INTEGRATION.md). The Unity version comes
    from the project's ProjectSettings/ProjectVersion.txt (or $env:UNITY_EXE).
    Exit code: 0 no errors, 1 errors found, 2 the scan didn't run or wrote no scan.json (Unity not found,
    project compile errors, project already open in another editor; see the .log next to -Out).
.EXAMPLE
    scripts\scan_project.ps1
.EXAMPLE
    scripts\scan_project.ps1 -ProjectPath D:\Games\CrimsonTactics -Out out\crimson_scan.json
#>
param(
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "unity\QALabSandbox"),
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "out\scan.json")
)
$ErrorActionPreference = "Stop"

$ProjectPath = (Resolve-Path $ProjectPath).Path
# Relative to the current PowerShell location ([IO.Path]::GetFullPath would use the process's directory).
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
$log = [IO.Path]::ChangeExtension($Out, ".log")
Remove-Item -ErrorAction SilentlyContinue $Out

$unity = & (Join-Path $PSScriptRoot "find_unity.ps1") -ProjectPath $ProjectPath
if (-not $unity) {
    Write-Host "scan_project: not run (Unity editor not found)" -ForegroundColor Red
    exit 2
}

$unityArgs = @(
    "-batchmode",
    "-projectPath", "`"$ProjectPath`"",
    "-executeMethod", "MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine",
    "-qalabScanOut", "`"$Out`"",
    "-logFile", "`"$log`""
)
Write-Host "scan_project: scanning $ProjectPath (log: $log)"
# Wait for Unity itself, not Start-Process -Wait (which also waits for Unity's child processes).
$process = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -NoNewWindow
$null = $process.Handle   # keep a handle open, or ExitCode can come back empty after the exit
$process.WaitForExit()
$code = $process.ExitCode

if (-not (Test-Path $Out)) {
    Write-Host "scan_project: no scan.json ($Out); Unity exited with $code, see $log" -ForegroundColor Red
    exit 2
}
$scan = Get-Content -Raw -Path $Out | ConvertFrom-Json
$summary = "scan_project: errors=$($scan.summary.errors) info=$($scan.summary.info) scenes=$($scan.scanned.scenes) prefabs=$($scan.scanned.prefabs) -> $Out"
if ($code -eq 0) {
    Write-Host $summary -ForegroundColor Green
} else {
    Write-Host $summary -ForegroundColor Red
}
exit $code
