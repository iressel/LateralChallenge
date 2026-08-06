[CmdletBinding()]
param(
    [string] $AppHostPath = "apphost.cs",

    [string] $SqlDataVolumeName = "cms-sync-aspire-sql-data",

    [switch] $RemoveData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedAppHostPath = $null
if ([IO.Path]::IsPathRooted($AppHostPath)) {
    $resolvedAppHostPath = [IO.Path]::GetFullPath($AppHostPath)
}
else {
    $resolvedAppHostPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $AppHostPath))
}

if (!(Test-Path -Path $resolvedAppHostPath -PathType Leaf)) {
    throw "The AppHost file was not found at '$resolvedAppHostPath'."
}

if ([string]::IsNullOrWhiteSpace($SqlDataVolumeName)) {
    throw "SqlDataVolumeName must be provided."
}

$allowedSqlImageRepositories = @(
    "mcr.microsoft.com/mssql/server",
    "mssql/server"
)

$sqlVolumeMountDestination = "/var/opt/mssql"
$requiredHostPorts = @(14333, 8080)

function Invoke-CheckedCapture {
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $SafeOperationDescription,

        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [switch] $AllowFailure
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $output = & $Command @Arguments 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if (!$AllowFailure -and $exitCode -ne 0) {
        throw "Operation '$SafeOperationDescription' failed for '$Command' with exit code $exitCode."
    }

    $global:LASTEXITCODE = $exitCode
    return ($output -join [Environment]::NewLine)
}

function Test-IsExpectedSqlImage {
    param(
        [Parameter(Mandatory)]
        [string] $Image,

        [Parameter(Mandatory)]
        [string[]] $AllowedRepositories
    )

    foreach ($repository in $AllowedRepositories) {
        if ($Image.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
            $Image.StartsWith("${repository}:", [StringComparison]::OrdinalIgnoreCase) -or
            $Image.StartsWith("${repository}@", [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-ContainerInspections {
    $containerIdsRaw = Invoke-CheckedCapture `
        -SafeOperationDescription "list Docker containers" `
        -Command "docker" `
        -Arguments @("ps", "-a", "--quiet")

    $containerIds = @(
        $containerIdsRaw -split "`r?`n" |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() }
    )

    $inspections = [Collections.Generic.List[object]]::new()
    foreach ($containerId in $containerIds) {
        $inspectText = Invoke-CheckedCapture `
            -SafeOperationDescription "inspect Docker container" `
            -Command "docker" `
            -Arguments @("inspect", $containerId)

        $inspect = $inspectText | ConvertFrom-Json
        if ($null -eq $inspect -or $inspect.Count -eq 0) {
            continue
        }

        $inspections.Add($inspect[0])
    }

    return @($inspections)
}

function Get-SqlContainerCandidates {
    param(
        [Parameter(Mandatory)]
        [string] $VolumeName,

        [Parameter(Mandatory)]
        [string] $VolumeMountDestination,

        [Parameter(Mandatory)]
        [string[]] $AllowedRepositories
    )

    $validCandidates = [Collections.Generic.List[object]]::new()
    $unexpectedCandidates = [Collections.Generic.List[object]]::new()

    foreach ($container in Get-ContainerInspections) {
        $name = ([string] $container.Name).TrimStart('/')
        $mounts = @($container.Mounts)
        $matchingVolumeMounts = @(
            $mounts | Where-Object {
                $_.Type -eq "volume" -and $_.Name -eq $VolumeName
            }
        )

        if ($matchingVolumeMounts.Count -eq 0) {
            continue
        }

        $expectedDestinationMounts = @(
            $matchingVolumeMounts | Where-Object {
                $_.Destination -eq $VolumeMountDestination
            }
        )

        $image = [string] $container.Config.Image
        $hasExpectedImage = Test-IsExpectedSqlImage -Image $image -AllowedRepositories $AllowedRepositories
        $hasSingleExpectedMount =
            $matchingVolumeMounts.Count -eq 1 -and
            $expectedDestinationMounts.Count -eq 1

        if ($hasSingleExpectedMount -and $hasExpectedImage) {
            $validCandidates.Add([pscustomobject]@{
                Id = [string] $container.Id
                Name = $name
                Image = $image
            })
            continue
        }

        $unexpectedCandidates.Add([pscustomobject]@{
            Id = [string] $container.Id
            Name = $name
            Image = $image
            Destinations = @($matchingVolumeMounts | ForEach-Object { [string] $_.Destination })
        })
    }

    return [pscustomobject]@{
        Valid = @($validCandidates)
        Unexpected = @($unexpectedCandidates)
    }
}

function Test-ExactDockerVolumeExists {
    param(
        [Parameter(Mandatory)]
        [string] $VolumeName
    )

    $volumeNames = Invoke-CheckedCapture `
        -SafeOperationDescription "list Docker volumes" `
        -Command "docker" `
        -Arguments @("volume", "ls", "--format", "{{.Name}}")

    return @($volumeNames -split "`r?`n" | Where-Object { $_ -eq $VolumeName }).Count -gt 0
}

function Try-GetAppHostResourceNames {
    param(
        [Parameter(Mandatory)]
        [string] $ResolvedAppHostPath
    )

    $describeText = Invoke-CheckedCapture `
        -SafeOperationDescription "describe AppHost resources" `
        -Command "aspire" `
        -Arguments @("describe", "--apphost", $ResolvedAppHostPath, "--format", "Json", "--non-interactive") `
        -AllowFailure

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($describeText)) {
        return @()
    }

    try {
        $describeDocument = $describeText | ConvertFrom-Json
    }
    catch {
        return @()
    }

    return @(
        $describeDocument.resources |
            ForEach-Object { $_.name } |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
}

function Test-ContainerUsesSqlVolume {
    param(
        [Parameter(Mandatory)]
        [psobject] $Container,

        [Parameter(Mandatory)]
        [string] $VolumeName,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    $mounts = @($Container.Mounts)
    return @(
        $mounts | Where-Object {
            $_.Type -eq "volume" -and $_.Name -eq $VolumeName -and $_.Destination -eq $Destination
        }
    ).Count -eq 1
}

function Test-IsAppHostContainer {
    param(
        [Parameter(Mandatory)]
        [psobject] $Container,

        [Parameter()]
        [AllowNull()]
        [string[]] $AppHostResourceNames,

        [Parameter(Mandatory)]
        [string] $VolumeName,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    $name = ([string] $Container.Name).TrimStart('/')
    if ($AppHostResourceNames -contains $name) {
        return $true
    }

    if (Test-ContainerUsesSqlVolume -Container $Container -VolumeName $VolumeName -Destination $Destination) {
        return $true
    }

    $labels = @($Container.Config.Labels.PSObject.Properties.Name)
    return @($labels | Where-Object { $_ -like "*aspire*" }).Count -gt 0
}

function Assert-RequiredPortsNotOwnedByContainersOrProcesses {
    param(
        [Parameter(Mandatory)]
        [int[]] $Ports,

        [Parameter()]
        [AllowNull()]
        [string[]] $AppHostResourceNames,

        [Parameter(Mandatory)]
        [string] $VolumeName,

        [Parameter(Mandatory)]
        [string] $Destination
    )

    $runningContainerIdsRaw = Invoke-CheckedCapture `
        -SafeOperationDescription "list running Docker containers" `
        -Command "docker" `
        -Arguments @("ps", "--quiet")

    $runningContainerIds = @(
        $runningContainerIdsRaw -split "`r?`n" |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() }
    )

    foreach ($containerId in $runningContainerIds) {
        $inspectText = Invoke-CheckedCapture `
            -SafeOperationDescription "inspect running Docker container" `
            -Command "docker" `
            -Arguments @("inspect", $containerId)

        $inspect = $inspectText | ConvertFrom-Json
        if ($null -eq $inspect -or $inspect.Count -eq 0) {
            continue
        }

        $container = $inspect[0]
        $containerName = ([string] $container.Name).TrimStart('/')
        $portMappings = $container.NetworkSettings.Ports
        if ($null -eq $portMappings) {
            continue
        }

        foreach ($property in $portMappings.PSObject.Properties) {
            $bindings = @($property.Value)
            foreach ($binding in $bindings) {
                if ($null -eq $binding) {
                    continue
                }

                $hostIp = [string] $binding.HostIp
                $hostPortValue = [string] $binding.HostPort
                $hostPort = 0
                if (!([int]::TryParse($hostPortValue, [ref] $hostPort))) {
                    continue
                }

                if ($hostIp -ne "127.0.0.1" -or $hostPort -notin $Ports) {
                    continue
                }

                if (Test-IsAppHostContainer `
                        -Container $container `
                        -AppHostResourceNames $AppHostResourceNames `
                        -VolumeName $VolumeName `
                        -Destination $Destination) {
                    throw "Cleanup incomplete: AppHost container '$containerName' is still publishing 127.0.0.1:$hostPort."
                }

                throw "Port 127.0.0.1:$hostPort is owned by unrelated Docker container '$containerName'. Stop that container before running this workflow."
            }
        }
    }

    if (!(Get-Command -Name Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        return
    }

    foreach ($port in $Ports) {
        $listeners = @(
            Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.LocalAddress -in @("127.0.0.1", "0.0.0.0", "::", "::1")
                }
        )

        if ($listeners.Count -eq 0) {
            continue
        }

        $details = @(
            $listeners |
                Select-Object -ExpandProperty OwningProcess -Unique |
                ForEach-Object {
                    $processId = $_
                    $processName = "unknown"
                    try {
                        $processName = (Get-Process -Id $processId -ErrorAction Stop).ProcessName
                    }
                    catch {
                        $processName = "unknown"
                    }

                    "PID $processId ($processName)"
                }
        ) -join ", "

        throw "Port 127.0.0.1:$port is owned by a non-container process: $details."
    }
}

$appHostResourceNames = @(Try-GetAppHostResourceNames -ResolvedAppHostPath $resolvedAppHostPath)
if ($null -eq $appHostResourceNames) {
    $appHostResourceNames = @()
}

Invoke-CheckedCapture `
    -SafeOperationDescription "stop Aspire AppHost" `
    -Command "aspire" `
    -Arguments @("stop", "--apphost", $resolvedAppHostPath, "--non-interactive") | Out-Null

$candidates = Get-SqlContainerCandidates `
    -VolumeName $SqlDataVolumeName `
    -VolumeMountDestination $sqlVolumeMountDestination `
    -AllowedRepositories $allowedSqlImageRepositories

if ($candidates.Unexpected.Count -gt 0) {
    $details = $candidates.Unexpected |
        ForEach-Object {
            "Name=$($_.Name);Image=$($_.Image);Destinations=$([string]::Join(',', $_.Destinations))"
        } |
        Sort-Object -Unique

    $messagePrefix = if ($candidates.Unexpected.Count -gt 1) {
        "More than one unexpected container"
    }
    else {
        "Unexpected container"
    }

    throw "${messagePrefix} mounts volume '$SqlDataVolumeName'. Safe cleanup aborted. $($details -join '; ')"
}

if ($candidates.Valid.Count -gt 1) {
    $names = $candidates.Valid | ForEach-Object { $_.Name } | Sort-Object
    throw "More than one validated SQL container mounts volume '$SqlDataVolumeName': $($names -join ', '). Safe cleanup aborted."
}

if ($candidates.Valid.Count -eq 1) {
    $sqlContainer = $candidates.Valid[0]

    Invoke-CheckedCapture `
        -SafeOperationDescription "remove validated SQL container" `
        -Command "docker" `
        -Arguments @("rm", "-f", $sqlContainer.Id) | Out-Null

    $containerStillExists = Invoke-CheckedCapture `
        -SafeOperationDescription "verify SQL container removal" `
        -Command "docker" `
        -Arguments @("ps", "-a", "--quiet", "--filter", "id=$($sqlContainer.Id)")

    if (!([string]::IsNullOrWhiteSpace($containerStillExists))) {
        throw "SQL container '$($sqlContainer.Name)' still exists after removal."
    }

    Write-Output "Removed SQL container '$($sqlContainer.Name)' for volume '$SqlDataVolumeName'."
}
else {
    Write-Output "No SQL container mounted to '$SqlDataVolumeName' was found."
}

if ($RemoveData) {
    if (Test-ExactDockerVolumeExists -VolumeName $SqlDataVolumeName) {
        Invoke-CheckedCapture `
            -SafeOperationDescription "remove SQL data volume" `
            -Command "docker" `
            -Arguments @("volume", "rm", $SqlDataVolumeName) | Out-Null
    }

    if (Test-ExactDockerVolumeExists -VolumeName $SqlDataVolumeName) {
        throw "Volume '$SqlDataVolumeName' still exists after removal."
    }

    Write-Output "Removed SQL data volume '$SqlDataVolumeName'."
}
else {
    Write-Output "Retained SQL data volume '$SqlDataVolumeName'."
}

Assert-RequiredPortsNotOwnedByContainersOrProcesses `
    -Ports $requiredHostPorts `
    -AppHostResourceNames $appHostResourceNames `
    -VolumeName $SqlDataVolumeName `
    -Destination $sqlVolumeMountDestination

Write-Output "Aspire AppHost resources were stopped safely."
