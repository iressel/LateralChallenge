[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-CheckedCapture {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $SafeOperationDescription,

        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & $Command @Arguments 2>$null
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Cleanup verification operation '$SafeOperationDescription' failed for executable '$Command' with exit code $exitCode."
    }

    return ($output -join [Environment]::NewLine)
}

function Test-DockerResourcesRemain {
    param(
        [Parameter(Mandatory)]
        [string] $Label,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $ContainerOperationDescription,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $VolumeOperationDescription
    )

    $containers = Invoke-CheckedCapture `
        -SafeOperationDescription $ContainerOperationDescription `
        -Command "docker" `
        -Arguments @(
        "ps",
        "--all",
        "--quiet",
        "--filter",
        "label=$Label"
    )
    $volumes = Invoke-CheckedCapture `
        -SafeOperationDescription $VolumeOperationDescription `
        -Command "docker" `
        -Arguments @(
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
    while (Test-DockerResourcesRemain `
            -Label $Label `
            -ContainerOperationDescription "list Testcontainers containers" `
            -VolumeOperationDescription "list Testcontainers volumes") {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            return $false
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return $true
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$repositoryState = Invoke-CheckedCapture `
    -SafeOperationDescription "read repository status" `
    -Command "git" `
    -Arguments @(
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
    if (Test-DockerResourcesRemain `
            -Label "com.docker.compose.project=$composeProject" `
            -ContainerOperationDescription "list Compose containers" `
            -VolumeOperationDescription "list Compose volumes") {
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
