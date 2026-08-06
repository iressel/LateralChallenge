[CmdletBinding()]
param(
    [int] $ResourceWaitTimeoutSeconds = 480,
    [int] $ApiReadyTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appHostPath = Join-Path $repositoryRoot "apphost.cs"
$stopScriptPath = Join-Path $PSScriptRoot "stop-aspire-local.ps1"
$requiredAspireVersionPrefix = "13.4.0"

if (!(Test-Path -Path $appHostPath -PathType Leaf)) {
    throw "The AppHost file was not found at '$appHostPath'."
}

if (!(Test-Path -Path $stopScriptPath -PathType Leaf)) {
    throw "The stop script was not found at '$stopScriptPath'."
}

$managedEnvironmentVariables = @(
    "Parameters__mssql-sa-password",
    "Parameters__migration-sql-password",
    "Parameters__write-sql-password",
    "Parameters__read-sql-password",
    "Parameters__cms-username",
    "Parameters__cms-password",
    "Parameters__consumer-username",
    "Parameters__consumer-password",
    "Parameters__administrator-username",
    "Parameters__administrator-password",
    "Aspire__SqlDataVolumeName"
)

$originalEnvironment = @{}
foreach ($variableName in $managedEnvironmentVariables) {
    $originalEnvironment[$variableName] = [Environment]::GetEnvironmentVariable(
        $variableName,
        [EnvironmentVariableTarget]::Process)
}

function Get-AspireVersion {
    $versionOutput = & aspire --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "The Aspire CLI is required but was not found in PATH."
    }

    $firstLine = ($versionOutput | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($firstLine)) {
        throw "The Aspire CLI did not return a version string."
    }

    return $firstLine.Trim()
}

function Assert-AspireVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $ExpectedPrefix
    )

    if (!$Version.StartsWith($ExpectedPrefix, [StringComparison]::Ordinal)) {
        throw "Aspire CLI version $ExpectedPrefix is required. Current version: $Version."
    }
}

function Invoke-Aspire {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [switch] $CaptureOutput,

        [switch] $AllowFailure
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"

        if ($CaptureOutput) {
            $output = & aspire @Arguments
        }
        else {
            & aspire @Arguments
            $output = @()
        }

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if (!$AllowFailure -and $exitCode -ne 0) {
        throw "Aspire command failed: aspire $($Arguments -join ' ')"
    }

    $global:LASTEXITCODE = $exitCode
    return ,$output
}

function Invoke-AspireWait {
    param(
        [Parameter(Mandatory)]
        [string] $Resource,

        [Parameter(Mandatory)]
        [string] $Status,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds,

        [switch] $AllowFailure
    )

    $waitArguments = @(
        "wait",
        $Resource,
        "--status",
        $Status,
        "--timeout",
        $TimeoutSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),
        "--apphost",
        $appHostPath,
        "--non-interactive"
    )

    Invoke-Aspire -Arguments $waitArguments -AllowFailure:$AllowFailure
    return $LASTEXITCODE -eq 0
}

function Get-AspireSnapshot {
    $json = Invoke-Aspire -Arguments @(
        "describe",
        "--apphost",
        $appHostPath,
        "--format",
        "Json",
        "--non-interactive"
    ) -CaptureOutput

    $text = ($json -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Aspire describe returned empty output."
    }

    return $text | ConvertFrom-Json
}

function Get-ResourceByDisplayName {
    param(
        [Parameter(Mandatory)]
        [psobject] $Snapshot,

        [Parameter(Mandatory)]
        [string] $DisplayName
    )

    $resource = $Snapshot.resources | Where-Object { $_.displayName -eq $DisplayName } | Select-Object -First 1
    if ($null -eq $resource) {
        throw "Resource '$DisplayName' was not found in Aspire describe output."
    }

    return $resource
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

function New-BasicAuthorization {
    param(
        [Parameter(Mandatory)]
        [string] $Username,

        [Parameter(Mandatory)]
        [string] $Password
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes("${Username}:${Password}")
    try {
        return "Basic " + [Convert]::ToBase64String($bytes)
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Invoke-ApiRequest {
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient] $Client,

        [Parameter(Mandatory)]
        [string] $Method,

        [Parameter(Mandatory)]
        [string] $Path,

        [string] $Username,

        [string] $Password,

        [string] $JsonBody
    )

    $httpMethod = [Net.Http.HttpMethod]::new($Method)
    $request = [Net.Http.HttpRequestMessage]::new($httpMethod, $Path)
    try {
        if ($PSBoundParameters.ContainsKey("Username") -and
            $PSBoundParameters.ContainsKey("Password")) {
            $request.Headers.TryAddWithoutValidation(
                "Authorization",
                (New-BasicAuthorization -Username $Username -Password $Password)) | Out-Null
        }

        if ($PSBoundParameters.ContainsKey("JsonBody")) {
            $request.Content = [Net.Http.StringContent]::new($JsonBody, [Text.Encoding]::UTF8, "application/json")
        }

        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $challenges = @($response.Headers.WwwAuthenticate | ForEach-Object { $_.ToString() })

            return [pscustomobject]@{
                StatusCode = [int]$response.StatusCode
                Content = $content
                ChallengeHeaders = $challenges
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

function Wait-HttpStatus {
    param(
        [Parameter(Mandatory)]
        [Net.Http.HttpClient] $Client,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [int] $ExpectedStatusCode,

        [Parameter(Mandatory)]
        [int] $TimeoutSeconds
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        try {
            $response = $Client.GetAsync($Path).GetAwaiter().GetResult()
            try {
                if ([int]$response.StatusCode -eq $ExpectedStatusCode) {
                    return
                }
            }
            finally {
                $response.Dispose()
            }
        }
        catch {
            # Keep polling until timeout.
        }

        Start-Sleep -Milliseconds 500
    }

    throw "HTTP probe '$Path' did not return status $ExpectedStatusCode within $TimeoutSeconds seconds."
}

function Assert-StatusCode {
    param(
        [Parameter(Mandatory)]
        [string] $Operation,

        [Parameter(Mandatory)]
        [int] $Actual,

        [Parameter(Mandatory)]
        [int] $Expected
    )

    if ($Actual -ne $Expected) {
        throw "$Operation returned HTTP $Actual instead of $Expected."
    }
}

function Assert-ContainsText {
    param(
        [Parameter(Mandatory)]
        [string] $Operation,

        [Parameter(Mandatory)]
        [string[]] $Values,

        [Parameter(Mandatory)]
        [string] $ExpectedFragment
    )

    if (!($Values | Where-Object { $_ -like "*$ExpectedFragment*" })) {
        throw "$Operation did not contain expected fragment '$ExpectedFragment'."
    }
}

function Assert-OpenApiPaths {
    param(
        [Parameter(Mandatory)]
        [string] $OpenApiJson
    )

    $document = $OpenApiJson | ConvertFrom-Json
    $paths = @($document.paths.PSObject.Properties.Name)
    $requiredPaths = @(
        "/cms/events",
        "/api/entities",
        "/api/entities/{entityId}",
        "/api/entities/{entityId}/administrative-state"
    )

    foreach ($requiredPath in $requiredPaths) {
        if ($requiredPath -notin $paths) {
            throw "OpenAPI JSON is missing required path '$requiredPath'."
        }
    }
}

$httpClient = $null
$validationError = $null
$validationVolumeName = "cms-sync-aspire-validation-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"

try {
    $aspireVersion = Get-AspireVersion
    Assert-AspireVersion -Version $aspireVersion -ExpectedPrefix $requiredAspireVersionPrefix

    $mssqlSaPassword = New-SqlPassword
    $migrationSqlPassword = New-SqlPassword
    $writeSqlPassword = New-SqlPassword
    $readSqlPassword = New-SqlPassword

    $cmsUsername = "cmssvc-local1"
    $consumerUsername = "consumer-local1"
    $administratorUsername = "admin-local1"

    $cmsPassword = [Guid]::NewGuid().ToString("D")
    $consumerPassword = [Guid]::NewGuid().ToString("D")
    $administratorPassword = [Guid]::NewGuid().ToString("D")

    [Environment]::SetEnvironmentVariable("Parameters__mssql-sa-password", $mssqlSaPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__migration-sql-password", $migrationSqlPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__write-sql-password", $writeSqlPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__read-sql-password", $readSqlPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__cms-username", $cmsUsername, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__cms-password", $cmsPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__consumer-username", $consumerUsername, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__consumer-password", $consumerPassword, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__administrator-username", $administratorUsername, "Process")
    [Environment]::SetEnvironmentVariable("Parameters__administrator-password", $administratorPassword, "Process")
    [Environment]::SetEnvironmentVariable("Aspire__SqlDataVolumeName", $validationVolumeName, "Process")

    & $stopScriptPath -AppHostPath $appHostPath -SqlDataVolumeName $validationVolumeName -RemoveData

    $startOutput = Invoke-Aspire -Arguments @(
        "start",
        "--apphost",
        $appHostPath,
        "--format",
        "Json",
        "--isolated",
        "--non-interactive"
    ) -CaptureOutput

    $startText = ($startOutput -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($startText)) {
        throw "Aspire start returned no output."
    }

    $start = $startText | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($start.dashboardUrl)) {
        throw "Aspire start did not return a dashboard URL."
    }

    $null = Invoke-AspireWait -Resource "sql" -Status "healthy" -TimeoutSeconds $ResourceWaitTimeoutSeconds
    $null = Invoke-AspireWait -Resource "db-init" -Status "down" -TimeoutSeconds $ResourceWaitTimeoutSeconds
    $null = Invoke-AspireWait -Resource "migration" -Status "down" -TimeoutSeconds $ResourceWaitTimeoutSeconds

    $apiWaitSucceeded = Invoke-AspireWait `
        -Resource "api" `
        -Status "healthy" `
        -TimeoutSeconds $ResourceWaitTimeoutSeconds `
        -AllowFailure

    $snapshot = Get-AspireSnapshot
    $sqlResource = Get-ResourceByDisplayName -Snapshot $snapshot -DisplayName "sql"
    $dbInitResource = Get-ResourceByDisplayName -Snapshot $snapshot -DisplayName "db-init"
    $migrationResource = Get-ResourceByDisplayName -Snapshot $snapshot -DisplayName "migration"
    $apiResource = Get-ResourceByDisplayName -Snapshot $snapshot -DisplayName "api"

    if ($sqlResource.state -ne "Running" -or $sqlResource.healthStatus -ne "Healthy") {
        throw "The sql resource did not reach Running/Healthy state."
    }

    if ($dbInitResource.state -ne "Exited" -or [int]$dbInitResource.exitCode -ne 0) {
        throw "The db-init resource did not complete successfully."
    }

    if ($migrationResource.state -ne "Exited" -or [int]$migrationResource.exitCode -ne 0) {
        throw "The migration resource did not complete successfully."
    }

    if ($apiWaitSucceeded -and $apiResource.state -ne "Running") {
        throw "The api resource was expected to be running after healthy wait success."
    }

    $httpClient = [Net.Http.HttpClient]::new()
    $httpClient.BaseAddress = [Uri]::new("http://localhost:8080")

    Wait-HttpStatus -Client $httpClient -Path "/health/live" -ExpectedStatusCode 200 -TimeoutSeconds $ApiReadyTimeoutSeconds
    Wait-HttpStatus -Client $httpClient -Path "/health/ready" -ExpectedStatusCode 200 -TimeoutSeconds $ApiReadyTimeoutSeconds

    $liveResponse = Invoke-ApiRequest -Client $httpClient -Method "GET" -Path "/health/live"
    Assert-StatusCode -Operation "GET /health/live" -Actual $liveResponse.StatusCode -Expected 200

    $readyResponse = Invoke-ApiRequest -Client $httpClient -Method "GET" -Path "/health/ready"
    Assert-StatusCode -Operation "GET /health/ready" -Actual $readyResponse.StatusCode -Expected 200

    $swaggerHtml = Invoke-ApiRequest -Client $httpClient -Method "GET" -Path "/swagger/index.html"
    Assert-StatusCode -Operation "GET /swagger/index.html" -Actual $swaggerHtml.StatusCode -Expected 200

    $openApiResponse = Invoke-ApiRequest -Client $httpClient -Method "GET" -Path "/swagger/v1/swagger.json"
    Assert-StatusCode -Operation "GET /swagger/v1/swagger.json" -Actual $openApiResponse.StatusCode -Expected 200
    Assert-OpenApiPaths -OpenApiJson $openApiResponse.Content

    $entityId = "aspire-entity-$([Guid]::NewGuid().ToString('N'))"
    $publishEvent = [ordered]@{
        eventId = "aspire-event-$([Guid]::NewGuid().ToString('N'))"
        type = "publish"
        id = $entityId
        version = 1
        timestamp = [DateTimeOffset]::UtcNow.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
        payload = [ordered]@{
            source = "aspire-validation"
            value = 1
        }
    }
    $eventBody = ConvertTo-Json -InputObject @($publishEvent) -Depth 5 -Compress

    $cmsPublish = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "POST" `
        -Path "/cms/events" `
        -Username $cmsUsername `
        -Password $cmsPassword `
        -JsonBody $eventBody
    Assert-StatusCode -Operation "CMS publish event" -Actual $cmsPublish.StatusCode -Expected 200

    $consumerRead = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "GET" `
        -Path "/api/entities/$entityId" `
        -Username $consumerUsername `
        -Password $consumerPassword
    Assert-StatusCode -Operation "Consumer read entity" -Actual $consumerRead.StatusCode -Expected 200

    $cmsOnReadApi = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "GET" `
        -Path "/api/entities" `
        -Username $cmsUsername `
        -Password $cmsPassword
    Assert-StatusCode -Operation "CMS credentials on consumer API" -Actual $cmsOnReadApi.StatusCode -Expected 401
    Assert-ContainsText `
        -Operation "CMS credentials consumer challenge" `
        -Values $cmsOnReadApi.ChallengeHeaders `
        -ExpectedFragment 'realm="ConsumerBasic"'

    $consumerOnWebhook = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "POST" `
        -Path "/cms/events" `
        -Username $consumerUsername `
        -Password $consumerPassword `
        -JsonBody "[]"
    Assert-StatusCode -Operation "Consumer credentials on webhook" -Actual $consumerOnWebhook.StatusCode -Expected 401
    Assert-ContainsText `
        -Operation "Consumer credentials webhook challenge" `
        -Values $consumerOnWebhook.ChallengeHeaders `
        -ExpectedFragment 'realm="CmsBasic"'

    $administrativeStateBody = '{"Disabled":true}'

    $consumerAdminUpdate = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "PUT" `
        -Path "/api/entities/$entityId/administrative-state" `
        -Username $consumerUsername `
        -Password $consumerPassword `
        -JsonBody $administrativeStateBody
    Assert-StatusCode -Operation "Consumer administrative update" -Actual $consumerAdminUpdate.StatusCode -Expected 403

    $administratorAdminUpdate = Invoke-ApiRequest `
        -Client $httpClient `
        -Method "PUT" `
        -Path "/api/entities/$entityId/administrative-state" `
        -Username $administratorUsername `
        -Password $administratorPassword `
        -JsonBody $administrativeStateBody
    Assert-StatusCode -Operation "Administrator administrative update" -Actual $administratorAdminUpdate.StatusCode -Expected 200

    if (!$apiWaitSucceeded) {
        Write-Warning "Aspire 'wait api --status healthy' did not succeed, but HTTP readiness and API smoke checks succeeded."
    }

    Write-Output "Aspire validation succeeded."
    Write-Output "Verified sql healthy, db-init and migration completed, API health endpoints, Swagger/OpenAPI, and authenticated publish/read/admin flows."
}
catch {
    $validationError = $_
    throw
}
finally {
    try {
        if ($httpClient -is [IDisposable]) {
            $httpClient.Dispose()
        }

        & $stopScriptPath -AppHostPath $appHostPath -SqlDataVolumeName $validationVolumeName -RemoveData
    }
    catch {
        if ($null -eq $validationError) {
            throw
        }

        Write-Warning "Aspire cleanup also failed; the original validation error is being preserved."
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
