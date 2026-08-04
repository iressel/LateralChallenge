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
    $containers = Invoke-CheckedCapture -Command "docker" -Arguments @(
        "ps",
        "--all",
        "--quiet",
        "--filter",
        "label=com.docker.compose.project=$composeProject"
    )
    $volumes = Invoke-CheckedCapture -Command "docker" -Arguments @(
        "volume",
        "ls",
        "--quiet",
        "--filter",
        "label=com.docker.compose.project=$composeProject"
    )

    if (![string]::IsNullOrWhiteSpace($containers) -or
        ![string]::IsNullOrWhiteSpace($volumes)) {
        throw "A repository Compose container or volume remained after validation."
    }
}

$testcontainersContainers = Invoke-CheckedCapture -Command "docker" -Arguments @(
    "ps",
    "--all",
    "--quiet",
    "--filter",
    "label=org.testcontainers=true"
)
$testcontainersVolumes = Invoke-CheckedCapture -Command "docker" -Arguments @(
    "volume",
    "ls",
    "--quiet",
    "--filter",
    "label=org.testcontainers=true"
)

if (![string]::IsNullOrWhiteSpace($testcontainersContainers) -or
    ![string]::IsNullOrWhiteSpace($testcontainersVolumes)) {
    throw "A Testcontainers resource remained after validation."
}

Write-Output "Repository and container cleanup verification passed."
