<#
.SYNOPSIS
    Runs one bot playtest in the built sandbox player and prints the run folder (spec 04).
.DESCRIPTION
    QALabSandbox.exe -qalab -qalabOut <Out> -qalabSeed <Seed> -qalabDuration <Duration>
                     -qalabAdapter <Adapter> [-qalabScene <Scene>] [-qalabBenchmark] -qalabQuitOnEnd
                     -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile <log>
    A windowed development player: screenshots then include the UI (spec 01). The player writes
    <Out>\<run_id>\ (run.json, events.jsonl, shots\, results.xml, labels.json with -Benchmark); its log
    is moved there as player.log. Several playtests may run at once: each finds its own run folder in
    its own log. The last line printed is the run folder, so other scripts can capture it.
    Exit code: the player's (0 clean, 1 a blocker/critical detector fired, 2 QA Lab internal error);
    3 no run folder (the player crashed or QA Lab didn't start); 4 timed out (the player was stopped).
.EXAMPLE
    scripts\run_playtest.ps1 -Seed 42 -Duration 120
.EXAMPLE
    scripts\run_playtest.ps1 -Seed 7 -Duration 30 -Adapter ui_crawler -Scene Sandbox_Menu -Benchmark
#>
param(
    [int]$Seed = 42,
    [double]$Duration = 120,
    [string]$Adapter = "navmesh_explorer",
    [string]$Scene = "Sandbox_Level01",
    [switch]$Benchmark,
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "runs"),
    [string]$Seeds = "all",
    [double]$ShotEvery = 5,
    [string]$Player = (Join-Path (Split-Path $PSScriptRoot -Parent) "Builds\Sandbox\QALabSandbox.exe"),
    [int]$TimeoutS = 0
)
$ErrorActionPreference = "Stop"

$inv = [Globalization.CultureInfo]::InvariantCulture
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force -Path $Out | Out-Null
if (-not (Test-Path $Player)) {
    Write-Host "run_playtest: no player at $Player (run scripts\build_sandbox.ps1 first)" -ForegroundColor Red
    exit 3
}
if ($TimeoutS -le 0) { $TimeoutS = [int]$Duration + 180 }   # loading, the run itself, closing
$log = Join-Path $Out ("player-" + [guid]::NewGuid().ToString("N") + ".log")

$playerArgs = @(
    "-qalab",
    "-qalabOut", "`"$Out`"",
    "-qalabSeed", $Seed.ToString($inv),
    "-qalabDuration", $Duration.ToString($inv),
    "-qalabAdapter", $Adapter,
    "-qalabShotEvery", $ShotEvery.ToString($inv),
    "-qalabSeeds", $Seeds,
    "-qalabQuitOnEnd",
    "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720",
    "-logFile", "`"$log`""
)
if ($Scene) { $playerArgs += @("-qalabScene", $Scene) }
if ($Benchmark) { $playerArgs += "-qalabBenchmark" }

Write-Host "run_playtest: seed $Seed, $Duration s, adapter $Adapter, scene $Scene"
$process = Start-Process -FilePath $Player -ArgumentList $playerArgs -PassThru
$null = $process.Handle   # keep a handle open, or ExitCode can come back empty after the exit
if (-not $process.WaitForExit($TimeoutS * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Write-Host "run_playtest: timed out after $TimeoutS s; the player was stopped (log: $log)" -ForegroundColor Red
    exit 4
}
$code = $process.ExitCode

# QA Lab logs "[QALab] recording run <run_id> to <run folder> for <n> s, ..." when it starts.
$runDir = $null
if (Test-Path $log) {
    $match = Select-String -Path $log -Pattern '\[QALab\] recording run (\S+) to (.+) for [0-9.]+ s' | Select-Object -First 1
    if ($match) { $runDir = $match.Matches[0].Groups[2].Value.Trim() }
}
if (-not $runDir -or -not (Test-Path $runDir)) {
    Write-Host "run_playtest: no run folder (player exit $code); see $log" -ForegroundColor Red
    exit 3
}
Move-Item -Force -Path $log -Destination (Join-Path $runDir "player.log")

$run = Get-Content -Raw -Path (Join-Path $runDir "run.json") | ConvertFrom-Json
$actions = (Select-String -Path (Join-Path $runDir "events.jsonl") -Pattern '"kind":"action"').Count
$detectors = (Select-String -Path (Join-Path $runDir "events.jsonl") -Pattern '"kind":"detector"').Count
$shots = @(Get-ChildItem -Path (Join-Path $runDir "shots") -Filter *.png -ErrorAction SilentlyContinue).Count
$color = if ($code -eq 0) { "Green" } elseif ($code -eq 1) { "Yellow" } else { "Red" }
Write-Host ("run_playtest: exit $code ($($run.exit_reason)), $actions actions, $detectors detector events, $shots screenshots") -ForegroundColor $color
Write-Output $runDir
exit $code
