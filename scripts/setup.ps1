<#
.SYNOPSIS
    One-time setup after downloading the repo: Python venv, qalab install, and a check that it works.
.DESCRIPTION
    1. Finds Python 3.12+ (the py launcher on Windows, else python3.12 / python3 / python).
    2. Creates .venv in the repo root if it is missing.
    3. Installs qalab with the dev extras (-Gemini adds the Gemini SDK).
    4. Copies .env.example to .env if there is no .env yet (it never prints or edits keys).
    5. Runs qalab validate on the sample run, then the test suite (-SkipTests skips it).
    Then it prints how to activate the venv and the first commands to try.
    Exit code: 0 ready, 1 a step failed (the message says which).
.EXAMPLE
    scripts\setup.ps1
.EXAMPLE
    scripts\setup.ps1 -Gemini -SkipTests
#>
param(
    [switch]$Gemini,
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

function Fail([string]$message) {
    Write-Host "setup: $message" -ForegroundColor Red
    exit 1
}

# 1. A Python that is 3.12 or newer. Each candidate is (command, extra arguments).
$candidates = @(@("py", @("-3.12")), @("python3.12", @()), @("python3", @()), @("python", @()))
$python = $null
foreach ($candidate in $candidates) {
    $command, $extra = $candidate
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { continue }
    $version = & $command @extra -c "import sys; print('%d.%d' % sys.version_info[:2])" 2>$null
    if ($LASTEXITCODE -eq 0 -and $version -and [version]$version -ge [version]"3.12") {
        $python = @($command) + $extra
        break
    }
}
if (-not $python) { Fail "Python 3.12 or newer not found. Install it from python.org (tick 'Add to PATH'), then re-run." }
Write-Host "setup: using $($python -join ' ') ($version)"

# 2. The virtual environment. Its python lives in Scripts\ on Windows and bin/ elsewhere.
$venv = Join-Path $repo ".venv"
if (-not (Test-Path $venv)) {
    & $python[0] @($python | Select-Object -Skip 1) -m venv $venv
    if ($LASTEXITCODE -ne 0) { Fail "could not create $venv" }
}
$venvPython = Join-Path $venv "Scripts\python.exe"
if (-not (Test-Path $venvPython)) { $venvPython = Join-Path $venv "bin/python" }
if (-not (Test-Path $venvPython)) { Fail "no python inside $venv; delete the folder and re-run" }

# 3. qalab itself, editable, so changes to python\src take effect without reinstalling.
$extras = if ($Gemini) { "dev,gemini" } else { "dev" }
& $venvPython -m pip install --quiet --upgrade pip
& $venvPython -m pip install --quiet -e "$(Join-Path $repo 'python')[$extras]"
if ($LASTEXITCODE -ne 0) { Fail "pip install failed (see the messages above)" }

# 4. .env for API keys: created from the template, never printed. Sora fills in keys by hand.
$envFile = Join-Path $repo ".env"
if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $repo ".env.example") $envFile
    Write-Host "setup: created .env from .env.example (add GEMINI_API_KEY there yourself if you use Gemini)"
}

# 5. Does it work? The sample run validates, then the tests (offline, no model, no key).
Push-Location $repo
try {
    & $venvPython -m qalab validate (Join-Path "samples" "sample_run")
    if ($LASTEXITCODE -ne 0) { Fail "qalab validate failed on samples\sample_run" }
    if (-not $SkipTests) {
        & $venvPython -m pytest python -q
        if ($LASTEXITCODE -ne 0) { Fail "some tests failed (see above)" }
    }
} finally {
    Pop-Location
}

$activate = if ($IsWindows -or $env:OS -eq "Windows_NT") { ".\.venv\Scripts\Activate.ps1" } else { ". .venv/bin/activate" }
Write-Host ""
Write-Host "setup: ready. Next:" -ForegroundColor Green
Write-Host "  $activate"
Write-Host "  qalab triage run samples\sample_run --provider fake --docs docs\sandbox_design.md --out out\sample_report"
Write-Host "  start out\sample_report\report.html"
Write-Host "Unity side (once): open unity\QALabSandbox in Unity Hub, then Tools -> QA Lab -> Rebuild Sandbox Scenes."
Write-Host "Full guide: docs\USER_GUIDE.md; every remaining step: docs\progress\PC_CHECKLIST.md"
exit 0
