<#
.SYNOPSIS
    One command from nothing to report.html (spec 04, M4): build if needed, bot playtests, vision, triage.
.DESCRIPTION
    1. scripts\build_sandbox.ps1, when Builds\Sandbox\QALabSandbox.exe is missing (or -Rebuild).
    2. scripts\run_playtest.ps1: the NavMesh explorer in Sandbox_Level01 for -Duration seconds, then
       (unless -MenuCrawl 0) the UI crawler in Sandbox_Menu for -MenuCrawl seconds. Same seed, -Benchmark.
    3. qalab vision analyze on both runs (method from qalab.toml [vision]); its findings join triage as
       visual bugs. A vision failure is a warning, not a stop: triage still runs on logs and detectors.
    4. qalab triage run on both runs -> reports\<run_id>\ (report.html, report.md, bugs.json, bugs_jira.csv).
    5. -Open opens report.html.
    The provider comes from qalab.toml unless -Provider is given (fake: no model, offline).
    Exit code: 0 done, 3 done and triage found a P1 bug, 1 a step failed (the message says which).
.EXAMPLE
    scripts\run_pipeline.ps1 -Seed 42 -Duration 120 -Open
.EXAMPLE
    scripts\run_pipeline.ps1 -Seed 7 -Duration 60 -Provider fake -MenuCrawl 0
#>
param(
    [int]$Seed = 42,
    [double]$Duration = 120,
    [string]$Provider = "",
    [double]$MenuCrawl = 30,
    [switch]$Rebuild,
    [switch]$Open,
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "runs")
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$player = Join-Path $repo "Builds\Sandbox\QALabSandbox.exe"
$started = Get-Date

function Fail([string]$message) {
    Write-Host "run_pipeline: $message" -ForegroundColor Red
    exit 1
}

# The venv's qalab when there is one (README quick start), else whatever is on PATH.
$qalab = Join-Path $repo ".venv\Scripts\qalab.exe"
if (-not (Test-Path $qalab)) {
    $onPath = Get-Command qalab -ErrorAction SilentlyContinue
    if (-not $onPath) { Fail "qalab not found: create the venv first (README, Quick start)" }
    $qalab = $onPath.Source
}

# 1. Build
if ($Rebuild -or -not (Test-Path $player)) {
    & (Join-Path $PSScriptRoot "build_sandbox.ps1")
    if ($LASTEXITCODE -ne 0) { Fail "build failed (see Builds\build.log)" }
}

# 2. Playtests. Exit 1 (a critical detector fired) is a finding, not a failure; 2 (QA Lab internal
# error) still leaves a run folder worth triaging, so only 3+ (no run folder, timeout) stops here.
$runs = @()
$playtests = @(@{ Adapter = "navmesh_explorer"; Scene = "Sandbox_Level01"; Duration = $Duration })
if ($MenuCrawl -gt 0) { $playtests += @{ Adapter = "ui_crawler"; Scene = "Sandbox_Menu"; Duration = $MenuCrawl } }
foreach ($p in $playtests) {
    $runDir = & (Join-Path $PSScriptRoot "run_playtest.ps1") -Seed $Seed -Duration $p.Duration `
        -Adapter $p.Adapter -Scene $p.Scene -Benchmark -Out $Out | Select-Object -Last 1
    $code = $LASTEXITCODE
    if ($code -ge 3 -or -not $runDir) { Fail "playtest $($p.Adapter) failed (exit $code)" }
    if ($code -eq 2) { Write-Host "run_pipeline: QA Lab reported an internal error in $runDir (results.xml); continuing" -ForegroundColor Yellow }
    $runs += $runDir
}

# 3. Vision (M6). Windows PowerShell 5.1 turns a native command's stderr into a terminating error
# under "Stop", so this step runs with "Continue". Without findings, triage still has logs and detectors.
$ErrorActionPreference = "Continue"
$visionArgs = @("vision", "analyze") + $runs
if ($Provider) { $visionArgs += @("--provider", $Provider) }
& $qalab @visionArgs
$vision = $LASTEXITCODE
$ErrorActionPreference = "Stop"
if ($vision -ne 0) {
    Write-Host "run_pipeline: vision failed (exit $vision); triaging with whatever findings were written" -ForegroundColor Yellow
}

# 4. Triage
$reportDir = Join-Path $repo ("reports\" + (Split-Path $runs[0] -Leaf))
$triageArgs = @("triage", "run") + $runs + @(
    "--out", $reportDir,
    "--docs", (Join-Path $repo "docs\sandbox_design.md"),
    "--repo", (Join-Path $repo "unity\QALabSandbox\Assets\Sandbox\Scripts")
)
if ($Provider) { $triageArgs += @("--provider", $Provider) }
& $qalab @triageArgs
$triage = $LASTEXITCODE
if ($triage -ne 0 -and $triage -ne 3) { Fail "qalab triage run failed (exit $triage)" }

# 5. Report
$report = Join-Path $reportDir "report.html"
if ($Open -and (Test-Path $report)) { Invoke-Item $report }
$seconds = [int]((Get-Date) - $started).TotalSeconds
$p1 = if ($triage -eq 3) { "; P1 bug found" } else { "" }
Write-Host "run_pipeline: done in ${seconds} s: $($runs.Count) run(s) -> $report$p1" -ForegroundColor Green
exit $triage
