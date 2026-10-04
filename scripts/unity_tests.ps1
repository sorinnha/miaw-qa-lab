<#
.SYNOPSIS
    Runs the sandbox's Unity tests in batch mode and prints pass/fail counts (spec 04).
.DESCRIPTION
    Unity.exe -runTests -batchmode -projectPath unity\QALabSandbox -testPlatform <p>
              -testResults out\<p>.xml -logFile out\<p>.log
    No -quit: with -runTests Unity quits by itself when the tests finish.
    YOU WRITE tests (category YouWrite) are skipped unless -IncludeYouWrite is given.
    Exit code: 0 all passed, 2 test failures (Unity's code), anything else = Unity could not run.
.EXAMPLE
    scripts\unity_tests.ps1 -Platform EditMode
.EXAMPLE
    scripts\unity_tests.ps1 -Platform PlayMode -IncludeYouWrite
#>
param(
    [ValidateSet("EditMode", "PlayMode")]
    [string]$Platform = "EditMode",
    [switch]$IncludeYouWrite
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo "unity\QALabSandbox"
$outDir = Join-Path $repo "out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$results = Join-Path $outDir "$Platform.xml"
$log = Join-Path $outDir "$Platform.log"
Remove-Item -ErrorAction SilentlyContinue $results

# find_unity prints only the path on its output stream; on failure it prints nothing (and says why).
$unity = & (Join-Path $PSScriptRoot "find_unity.ps1")
if (-not $unity) {
    Write-Host "unity_tests: $Platform not run (Unity editor not found)" -ForegroundColor Red
    exit 1
}

$unityArgs = @(
    "-runTests", "-batchmode",
    "-projectPath", "`"$project`"",
    "-testPlatform", $Platform,
    "-testResults", "`"$results`"",
    "-logFile", "`"$log`""
)
if (-not $IncludeYouWrite) {
    $unityArgs += @("-testCategory", "`"!YouWrite`"")
}

Write-Host "unity_tests: running $Platform tests (log: $log)"
# Not Start-Process -Wait: it also waits for every child process, and Unity can leave its licensing
# client running after it exits, which would hang this script. Wait for Unity itself instead.
$process = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -NoNewWindow
$null = $process.Handle   # keep a handle open, or ExitCode can come back empty after the exit
$process.WaitForExit()
$code = $process.ExitCode

if (-not (Test-Path $results)) {
    # Write-Host, not Write-Error: under "Stop" Write-Error would end the script and lose Unity's code.
    Write-Host "unity_tests: $Platform no results file ($results); Unity exited with $code, see $log" -ForegroundColor Red
    exit $(if ($code -ne 0) { $code } else { 1 })
}
[xml]$xml = Get-Content -Raw -Path $results
$run = $xml.'test-run'
$summary = "unity_tests: $Platform total=$($run.total) passed=$($run.passed) failed=$($run.failed) skipped=$($run.skipped) result=$($run.result)"
if ($code -eq 0) {
    Write-Host $summary -ForegroundColor Green
} else {
    Write-Host $summary -ForegroundColor Red
}
exit $code
