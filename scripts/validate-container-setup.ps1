[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$sqlServerImage = "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89"
$migrationId = "20260802142305_InitialCmsPersistence"
$projectName = "cmssync-t015-validation"
$managedEnvironmentVariables = @(
    "COMPOSE_PROJECT_NAME",
    "SQL_SERVER_IMAGE",
    "SQL_SERVER_PORT",
    "CMS_API_PORT",
    "MSSQL_SA_PASSWORD",
    "MIGRATION_SQL_PASSWORD",
    "WRITE_SQL_PASSWORD",
    "READ_SQL_PASSWORD",
    "Authentication__Credentials__Cms__Username",
    "Authentication__Credentials__Cms__Password",
    "Authentication__Credentials__Consumer__Username",
    "Authentication__Credentials__Consumer__Password",
    "Authentication__Credentials__Administrator__Username",
    "Authentication__Credentials__Administrator__Password"
)

$originalEnvironment = @{}
foreach ($variableName in $managedEnvironmentVariables) {
    $originalEnvironment[$variableName] = [Environment]::GetEnvironmentVariable(
        $variableName,
        [EnvironmentVariableTarget]::Process)
}

function Invoke-DockerCommand {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A Docker command failed with exit code $LASTEXITCODE."
    }
}

function Invoke-DockerCapture {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A Docker inspection command failed with exit code $LASTEXITCODE."
    }

    return ($output -join [Environment]::NewLine)
}

function New-SqlPassword {
    $randomBytes = [byte[]]::new(24)
    $randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomNumberGenerator.GetBytes($randomBytes)
        $randomText = [BitConverter]::ToString($randomBytes).Replace("-", "").ToLowerInvariant()
        return "Aa9!${randomText}z"
    }
    finally {
        [Array]::Clear($randomBytes, 0, $randomBytes.Length)
        $randomNumberGenerator.Dispose()
    }
}

function Get-AvailableTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint] $listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function New-BasicAuthenticationValue {
    param(
        [Parameter(Mandatory)]
        [string] $Username,

        [Parameter(Mandatory)]
        [string] $Password
    )

    $credentialBytes = [Text.Encoding]::UTF8.GetBytes("${Username}:${Password}")
    try {
        return [Convert]::ToBase64String($credentialBytes)
    }
    finally {
        [Array]::Clear($credentialBytes, 0, $credentialBytes.Length)
    }
}

function Invoke-AuthenticatedRequest {
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient] $Client,

        [Parameter(Mandatory)]
        [Net.Http.HttpMethod] $Method,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Username,

        [Parameter(Mandatory)]
        [string] $Password,

        [string] $JsonBody
    )

    $request = [Net.Http.HttpRequestMessage]::new($Method, $Path)
    try {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new(
            "Basic",
            (New-BasicAuthenticationValue -Username $Username -Password $Password))

        if ($PSBoundParameters.ContainsKey("JsonBody")) {
            $request.Content = [Net.Http.StringContent]::new(
                $JsonBody,
                [Text.Encoding]::UTF8,
                "application/json")
        }

        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            return [pscustomobject]@{
                StatusCode = [int] $response.StatusCode
                Content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $request.Dispose()
    }
}

function Assert-HttpStatus {
    param(
        [Parameter(Mandatory)]
        [string] $Uri,

        [Parameter(Mandatory)]
        [int] $ExpectedStatus
    )

    $client = [Net.Http.HttpClient]::new()
    try {
        $response = $client.GetAsync($Uri).GetAwaiter().GetResult()
        try {
            if ([int] $response.StatusCode -ne $ExpectedStatus) {
                throw "An HTTP health probe returned an unexpected status."
            }
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}

function Assert-ServiceState {
    $serviceJson = Invoke-DockerCapture -Arguments @(
        "compose",
        "ps",
        "--all",
        "--format",
        "json"
    )
    $services = @(
        $serviceJson -split "`r?`n" |
            Where-Object { ![string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json }
    )
    $servicesByName = @{}
    foreach ($service in $services) {
        $servicesByName[$service.Service] = $service
    }

    foreach ($requiredService in @("sql", "db-init", "migration", "api")) {
        if (!$servicesByName.ContainsKey($requiredService)) {
            throw "Compose did not report every required service."
        }
    }

    if ($servicesByName["sql"].State -ne "running" -or
        $servicesByName["sql"].Health -ne "healthy" -or
        $servicesByName["api"].State -ne "running" -or
        $servicesByName["api"].Health -ne "healthy" -or
        $servicesByName["db-init"].State -ne "exited" -or
        [int] $servicesByName["db-init"].ExitCode -ne 0 -or
        $servicesByName["migration"].State -ne "exited" -or
        [int] $servicesByName["migration"].ExitCode -ne 0) {
        throw "Compose services did not reach the required healthy/completed state."
    }
}

function Assert-MigrationHistory {
    Invoke-DockerCommand -Arguments @(
        "compose",
        "run",
        "--rm",
        "--no-deps",
        "--entrypoint",
        "/bin/bash",
        "db-init",
        "/opt/cms-sync/verify-migration.sh"
    )
}

function Assert-ApiSmoke {
    param(
        [Parameter(Mandatory)]
        [int] $ApiPort,

        [Parameter(Mandatory)]
        [string] $CmsUsername,

        [Parameter(Mandatory)]
        [string] $CmsPassword,

        [Parameter(Mandatory)]
        [string] $ConsumerUsername,

        [Parameter(Mandatory)]
        [string] $ConsumerPassword
    )

    $client = [Net.Http.HttpClient]::new()
    $client.BaseAddress = [Uri]::new("http://127.0.0.1:$ApiPort")
    try {
        $listRequest = @{
            Client = $client
            Method = [Net.Http.HttpMethod]::Get
            Path = "/api/entities"
            Username = $ConsumerUsername
            Password = $ConsumerPassword
        }
        $listResponse = Invoke-AuthenticatedRequest @listRequest
        if ($listResponse.StatusCode -ne 200) {
            throw "The authenticated consumer list smoke request failed."
        }

        $entityId = "container-smoke-$([Guid]::NewGuid().ToString('N'))"
        $eventId = "container-event-$([Guid]::NewGuid().ToString('N'))"
        $timestamp = [DateTimeOffset]::UtcNow.ToString(
            "O",
            [Globalization.CultureInfo]::InvariantCulture)
        $eventBody = [ordered]@{
            eventId = $eventId
            type = "publish"
            id = $entityId
            version = 1
            timestamp = $timestamp
            payload = [ordered]@{
                source = "container-smoke"
            }
        } | ConvertTo-Json -Depth 4 -Compress
        $body = "[${eventBody}]"

        $webhookRequest = @{
            Client = $client
            Method = [Net.Http.HttpMethod]::Post
            Path = "/cms/events"
            Username = $CmsUsername
            Password = $CmsPassword
            JsonBody = $body
        }
        $webhookResponse = Invoke-AuthenticatedRequest @webhookRequest
        if ($webhookResponse.StatusCode -ne 200) {
            throw "The webhook write smoke request failed."
        }

        $webhookResult = $webhookResponse.Content | ConvertFrom-Json
        if ($webhookResult.results.Count -ne 1 -or
            $webhookResult.results[0].outcome -ne "applied") {
            throw "The webhook write smoke request returned an unexpected result."
        }

        $detailRequest = @{
            Client = $client
            Method = [Net.Http.HttpMethod]::Get
            Path = "/api/entities/$entityId"
            Username = $ConsumerUsername
            Password = $ConsumerPassword
        }
        $detailResponse = Invoke-AuthenticatedRequest @detailRequest
        if ($detailResponse.StatusCode -ne 200) {
            throw "The consumer could not read the entity persisted by the webhook."
        }
    }
    finally {
        $client.Dispose()
    }
}

function Assert-NoProjectResources {
    $containers = Invoke-DockerCapture -Arguments @(
        "ps",
        "--all",
        "--filter",
        "label=com.docker.compose.project=$projectName",
        "--format",
        "{{.ID}}"
    )
    $volumes = Invoke-DockerCapture -Arguments @(
        "volume",
        "ls",
        "--filter",
        "label=com.docker.compose.project=$projectName",
        "--quiet"
    )

    if (![string]::IsNullOrWhiteSpace($containers) -or
        ![string]::IsNullOrWhiteSpace($volumes)) {
        throw "A project container or volume remained after cleanup."
    }
}

$composeStarted = $false
try {
    Invoke-DockerCommand -Arguments @("version", "--format", "{{.Server.Version}}")
    Invoke-DockerCommand -Arguments @("compose", "version", "--short")
    $architecture = (Invoke-DockerCapture -Arguments @(
        "info",
        "--format",
        "{{.Architecture}}"
    )).Trim().ToLowerInvariant()
    if ($architecture -notin @("amd64", "x86_64")) {
        throw "The supported Compose smoke test requires an x86-64 Docker host."
    }

    $sqlPort = Get-AvailableTcpPort
    $apiPort = Get-AvailableTcpPort
    $cmsUsername = "cms-service"
    $consumerUsername = "normal-consumer"
    $administratorUsername = "administrator"
    $cmsPassword = [Guid]::NewGuid().ToString("D")
    $consumerPassword = [Guid]::NewGuid().ToString("D")
    $administratorPassword = [Guid]::NewGuid().ToString("D")

    [Environment]::SetEnvironmentVariable("COMPOSE_PROJECT_NAME", $projectName, "Process")
    [Environment]::SetEnvironmentVariable("SQL_SERVER_IMAGE", $sqlServerImage, "Process")
    [Environment]::SetEnvironmentVariable("SQL_SERVER_PORT", $sqlPort.ToString(), "Process")
    [Environment]::SetEnvironmentVariable("CMS_API_PORT", $apiPort.ToString(), "Process")
    [Environment]::SetEnvironmentVariable("MSSQL_SA_PASSWORD", (New-SqlPassword), "Process")
    [Environment]::SetEnvironmentVariable("MIGRATION_SQL_PASSWORD", (New-SqlPassword), "Process")
    [Environment]::SetEnvironmentVariable("WRITE_SQL_PASSWORD", (New-SqlPassword), "Process")
    [Environment]::SetEnvironmentVariable("READ_SQL_PASSWORD", (New-SqlPassword), "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Cms__Username", $cmsUsername, "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Cms__Password", $cmsPassword, "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Consumer__Username", $consumerUsername, "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Consumer__Password", $consumerPassword, "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Administrator__Username", $administratorUsername, "Process")
    [Environment]::SetEnvironmentVariable("Authentication__Credentials__Administrator__Password", $administratorPassword, "Process")

    Invoke-DockerCommand -Arguments @("compose", "config", "--quiet")
    Invoke-DockerCommand -Arguments @("compose", "down", "--volumes", "--remove-orphans")

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    Invoke-DockerCommand -Arguments @(
        "compose",
        "up",
        "--build",
        "--wait",
        "--wait-timeout",
        "360"
    )
    $stopwatch.Stop()
    $composeStarted = $true

    Assert-ServiceState
    Assert-HttpStatus -Uri "http://127.0.0.1:$apiPort/health/live" -ExpectedStatus 200
    Assert-HttpStatus -Uri "http://127.0.0.1:$apiPort/health/ready" -ExpectedStatus 200
    Assert-MigrationHistory
    Invoke-DockerCommand -Arguments @(
        "compose",
        "run",
        "--rm",
        "--no-deps",
        "--entrypoint",
        "/bin/bash",
        "db-init",
        "/opt/cms-sync/verify-read-only.sh"
    )
    $apiSmoke = @{
        ApiPort = $apiPort
        CmsUsername = $cmsUsername
        CmsPassword = $cmsPassword
        ConsumerUsername = $consumerUsername
        ConsumerPassword = $consumerPassword
    }
    Assert-ApiSmoke @apiSmoke

    Write-Output (
        "Compose clean-volume validation passed in {0:N2} seconds." -f
        $stopwatch.Elapsed.TotalSeconds)
    Write-Output "Liveness, readiness, migration, read-only SQL, consumer read, and webhook write checks passed."
}
finally {
    try {
        if ($composeStarted) {
            Invoke-DockerCommand -Arguments @("compose", "down", "--volumes", "--remove-orphans")
        }
        else {
            Invoke-DockerCommand -Arguments @("compose", "down", "--volumes", "--remove-orphans")
        }

        Assert-NoProjectResources
    }
    finally {
        foreach ($variableName in $managedEnvironmentVariables) {
            [Environment]::SetEnvironmentVariable(
                $variableName,
                $originalEnvironment[$variableName],
                [EnvironmentVariableTarget]::Process)
        }
    }
}
