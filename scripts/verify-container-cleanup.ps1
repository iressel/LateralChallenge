[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-CheckedCapture {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A cleanup-verification command failed."
    }

    return ($output -join [Environment]::NewLine)
}

function Test-DockerResourcesRemain {
    param(
        [Parameter(Mandatory)]
        [string] $Label
    )

    $containers = Invoke-CheckedCapture -Command "docker" -Arguments @(
        "ps",
        "--all",
        "--quiet",
        "--filter",
        "label=$Label"
    )
    $volumes = Invoke-CheckedCapture -Command "docker" -Arguments @(
        "volume",
        "ls",
        "--quiet",
        "--filter",
        "label=$Label"
    )

    return ![string]::IsNullOrWhiteSpace($containers) -or
        ![string]::IsNullOrWhiteSpace($volumes)
}

function Wait-ForDockerResourcesToDisappear {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [ValidateRange(1, 300)]
        [int] $TimeoutSeconds,

        [Parameter(Mandatory)]
        [ValidateRange(1, 30)]
        [int] $PollIntervalSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (Test-DockerResourcesRemain -Label $Label) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            return $false
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return $true
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$repositoryState = Invoke-CheckedCapture -Command "git" -Arguments @(
    "-C",
    $repositoryRoot,
    "status",
    "--porcelain=v1",
    "--untracked-files=all"
)
if (![string]::IsNullOrWhiteSpace($repositoryState)) {
    throw "Repository cleanup verification found an unexpected worktree or index change."
}

$composeProjects = @("cms-sync", "cmssync-t015-validation")
foreach ($composeProject in $composeProjects) {
    if (Test-DockerResourcesRemain -Label "com.docker.compose.project=$composeProject") {
        throw "A repository Compose container or volume remained after validation."
    }
}

# Testcontainers disposal is asynchronous, so allow its resource reaper a bounded grace period.
$testcontainersCleanupTimeoutSeconds = 30
$testcontainersCleanupPollIntervalSeconds = 1
$testcontainersCleanupCompleted = Wait-ForDockerResourcesToDisappear `
    -Label "org.testcontainers=true" `
    -TimeoutSeconds $testcontainersCleanupTimeoutSeconds `
    -PollIntervalSeconds $testcontainersCleanupPollIntervalSeconds
if (!$testcontainersCleanupCompleted) {
    throw "A Testcontainers resource remained after validation."
}

Write-Output "Repository and container cleanup verification passed."
