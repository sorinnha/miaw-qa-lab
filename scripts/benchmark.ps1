<#
.SYNOPSIS
    Records the seeded benchmark (spec 04, M5): one bot playtest per seed, with ground truth.
.DESCRIPTION
    For every seed: scripts\run_playtest.ps1 -Benchmark with the NavMesh explorer in Sandbox_Level01
    (-Duration s), then, unless -MenuCrawl 0, the UI crawler in Sandbox_Menu (-MenuCrawl s). Runs land
    in <Out>\<run_id>\. Then writes <Out>\manifest.json: seeds, durations, git sha, machine and every
    run with its exit code. Builds the player first if it's missing.
    Re-running some seeds into the same -Out replaces those seeds' manifest entries and keeps the
    others. A timed-out playtest's partial run folder is moved to <Out>\_failed\ (eval ignores it).
    Evaluate with:  qalab eval triage <Out>
    Exit code: 0 every playtest left a run folder, 1 one or more didn't (they're listed in the manifest).
.EXAMPLE
    scripts\benchmark.ps1 -Seeds (1..20) -Duration 120
.EXAMPLE
    scripts\benchmark.ps1 -Seeds 1,2,3 -Duration 60 -Out benchmarks\smoke -MenuCrawl 0
#>
param(
    [int[]]$Seeds = (1..20),
    [double]$Duration = 120,
    [double]$MenuCrawl = 30,
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) "benchmarks\seeded_v1")
)
$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$player = Join-Path $repo "Builds\Sandbox\QALabSandbox.exe"
$started = Get-Date

if (-not (Test-Path $player)) {
    & (Join-Path $PSScriptRoot "build_sandbox.ps1")
    if ($LASTEXITCODE -ne 0) {
        Write-Host "benchmark: build failed (see Builds\build.log)" -ForegroundColor Red
        exit 1
    }
}

$playtests = @(@{ Adapter = "navmesh_explorer"; Scene = "Sandbox_Level01"; Duration = $Duration })
if ($MenuCrawl -gt 0) { $playtests += @{ Adapter = "ui_crawler"; Scene = "Sandbox_Menu"; Duration = $MenuCrawl } }

$runs = @()
$failed = 0
foreach ($seed in $Seeds) {
    foreach ($p in $playtests) {
        $runDir = $null
        $code = $null
        $problem = $null
        try {
            # The last output line is the run folder; $LASTEXITCODE still holds the script's exit code.
            $runDir = & (Join-Path $PSScriptRoot "run_playtest.ps1") -Seed $seed -Duration $p.Duration `
                -Adapter $p.Adapter -Scene $p.Scene -Benchmark -Out $Out | Select-Object -Last 1
            $code = $LASTEXITCODE
        } catch {
            # One broken playtest must not stop the other seeds or lose the manifest.
            $problem = $_.Exception.Message
            Write-Host "benchmark: seed $seed, $($p.Adapter): $problem" -ForegroundColor Red
        }
        $runId = $null
        if ($null -eq $problem -and $code -lt 3 -and $runDir) {
            $runId = Split-Path $runDir -Leaf
        } else {
            $failed++
            if ($runDir -and (Test-Path $runDir -PathType Container)) {
                # A partial run has no labels.json; set it aside so eval doesn't cluster it.
                $failedDir = Join-Path $Out "_failed"
                New-Item -ItemType Directory -Force -Path $failedDir | Out-Null
                Move-Item -Force -Path $runDir -Destination $failedDir
            }
        }
        $runs += [ordered]@{ seed = $seed; adapter = $p.Adapter; scene = $p.Scene; duration_s = $p.Duration; run_id = $runId; exit_code = $code; error = $problem }
    }
}

# Merge with an earlier manifest: re-run seeds replace their entries, other seeds stay.
$manifestPath = Join-Path $Out "manifest.json"
$createdAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$allRuns = @($runs)
if (Test-Path $manifestPath) {
    try {
        $previous = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
        if ($previous.created_at) { $createdAt = $previous.created_at }
        $allRuns = @($previous.runs | Where-Object { $Seeds -notcontains [int]$_.seed }) + $runs
    } catch {
        Write-Host "benchmark: could not read the old manifest.json, writing a new one ($($_.Exception.Message))" -ForegroundColor Yellow
    }
}
$allSeeds = @($allRuns | ForEach-Object { [int]$_.seed } | Sort-Object -Unique)

$sha = $null
try { $sha = (git -C $repo rev-parse HEAD 2>$null).Trim() } catch { $sha = $null }
# Machine facts are nice to have: a missing cmdlet (pwsh outside Windows) or CIM error must not lose the manifest.
$cpu = $null
$ramGb = $null
try {
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
} catch {
    Write-Host "benchmark: no CPU/RAM info for the manifest ($($_.Exception.Message))" -ForegroundColor Yellow
}
$manifest = [ordered]@{
    name = Split-Path $Out -Leaf
    created_at = $createdAt
    updated_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    seeds = $allSeeds
    duration_s = $Duration
    menu_crawl_s = $MenuCrawl
    git_sha = $sha
    machine = [ordered]@{
        os = [Environment]::OSVersion.VersionString
        cpu = $cpu
        ram_gb = $ramGb
    }
    runs = $allRuns
}
$json = $manifest | ConvertTo-Json -Depth 5
# UTF-8 without BOM (Windows PowerShell's Set-Content -Encoding utf8 would add one).
[IO.File]::WriteAllText($manifestPath, $json + "`n", (New-Object Text.UTF8Encoding $false))

$minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
$ok = $runs.Count - $failed
$color = if ($failed -eq 0) { "Green" } else { "Red" }
Write-Host "benchmark: $ok/$($runs.Count) playtests recorded in $minutes min -> $Out (manifest.json); evaluate with: qalab eval triage `"$Out`"" -ForegroundColor $color
exit $(if ($failed -eq 0) { 0 } else { 1 })
