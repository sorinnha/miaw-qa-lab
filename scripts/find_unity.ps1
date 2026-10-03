<#
.SYNOPSIS
    Prints the path of the Unity editor for unity/QALabSandbox (spec 04).
.DESCRIPTION
    Uses $env:UNITY_EXE when set; otherwise Unity Hub's default editor folder for the version in
    unity/QALabSandbox/ProjectSettings/ProjectVersion.txt. The path is the only thing written to the
    output, so other scripts can do:  $unity = & "$PSScriptRoot\find_unity.ps1"
.EXAMPLE
    scripts\find_unity.ps1
#>
param(
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) "unity\QALabSandbox"),
    [string]$HubEditorRoot = "C:\Program Files\Unity\Hub\Editor"
)
$ErrorActionPreference = "Stop"

if ($env:UNITY_EXE) {
    if (-not (Test-Path $env:UNITY_EXE)) {
        Write-Error "UNITY_EXE points to a missing file: $env:UNITY_EXE"
        exit 1
    }
    Write-Host "find_unity: using UNITY_EXE"
    Write-Output $env:UNITY_EXE
    exit 0
}

$versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
if (-not (Test-Path $versionFile)) {
    Write-Error "No $versionFile. Create the sandbox project in Unity Hub first (PLAN.md, M1), or set UNITY_EXE."
    exit 1
}
# "m_EditorVersion: 6000.0.23f1" -> "6000.0.23f1"
$line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
if (-not $line) {
    Write-Error "Could not read m_EditorVersion from $versionFile"
    exit 1
}
$version = $line.Matches[0].Groups[1].Value
$unity = Join-Path $HubEditorRoot "$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) {
    Write-Error "Unity $version is not installed at $unity. Install it with Unity Hub, or set UNITY_EXE."
    exit 1
}
Write-Host "find_unity: Unity $version"
Write-Output $unity
