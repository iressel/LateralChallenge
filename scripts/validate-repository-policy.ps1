[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$expectedSqlServerImage =
    "mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04@sha256:" +
    "ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89"
$findings = [Collections.Generic.List[object]]::new()

function Add-PolicyFinding {
    param(
        [Parameter(Mandatory)]
        [string] $Rule,

        [Parameter(Mandatory)]
        [string] $Path,

        [int] $LineNumber
    )

    $findings.Add([pscustomobject]@{
        Rule = $Rule
        Path = $Path
        LineNumber = $LineNumber
    })
}

function Get-TrackedFiles {
    $paths = & git -C $repositoryRoot ls-files
    if ($LASTEXITCODE -ne 0) {
        throw "The tracked-file boundary could not be read."
    }

    return @($paths | Where-Object { ![string]::IsNullOrWhiteSpace($_) })
}

function Test-IsTextFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $fileName = [IO.Path]::GetFileName($Path)
    if ($fileName -in @("Dockerfile", ".editorconfig", ".gitignore", ".dockerignore")) {
        return $true
    }

    return [IO.Path]::GetExtension($Path).ToLowerInvariant() -in @(
        ".cs",
        ".csproj",
        ".config",
        ".example",
        ".json",
        ".md",
        ".props",
        ".ps1",
        ".sh",
        ".sln",
        ".sql",
        ".targets",
        ".txt",
        ".yaml",
        ".yml"
    )
}

function Test-IsApprovedCredentialPlaceholder {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $rawCandidate = $Value.Trim().Trim(',', ';')
    if ($rawCandidate -match '^N?''\$\([A-Za-z_][A-Za-z0-9_]*\)''$') {
        return $true
    }

    $candidate = $rawCandidate.Trim('"', "'", ')')
    return $candidate -match '^<(?:generate|choose)-a-distinct-[^>]+>$' -or
        $candidate -eq '<non-secret-test-sentinel>' -or
        $candidate -match '^\$\{[A-Za-z_][A-Za-z0-9_]*(?::[^}]*)?\}$' -or
        $candidate -match '^\$\$?[A-Za-z_][A-Za-z0-9_]*$' -or
        $candidate -match '^\{[A-Za-z_][A-Za-z0-9_.():''-]*\}$'
}

function Test-ContainsPattern {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $Lines,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Rule,

        [Parameter(Mandatory)]
        [string] $Path
    )

    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        if ($Lines[$lineIndex] -match $Pattern) {
            Add-PolicyFinding -Rule $Rule -Path $Path -LineNumber ($lineIndex + 1)
        }
    }
}

function Get-StepBlock {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $Lines,

        [Parameter(Mandatory)]
        [int] $ContainingLine
    )

    $start = $ContainingLine
    while ($start -gt 0 -and $Lines[$start] -notmatch '^\s*-\s+name:') {
        $start--
    }

    $end = $ContainingLine + 1
    while ($end -lt $Lines.Count -and $Lines[$end] -notmatch '^\s*-\s+name:') {
        $end++
    }

    return ($Lines[$start..($end - 1)] -join "`n")
}

$trackedFiles = Get-TrackedFiles

foreach ($trackedPath in $trackedFiles) {
    $normalizedPath = $trackedPath.Replace('\', '/')
    $fileName = [IO.Path]::GetFileName($normalizedPath)
    $extension = [IO.Path]::GetExtension($normalizedPath).ToLowerInvariant()

    if ($normalizedPath -match '(^|/)\.env($|\.)' -and
        $normalizedPath -ne '.env.example') {
        Add-PolicyFinding -Rule "SEC001_TRACKED_ENV" -Path $normalizedPath
    }

    if ($extension -in @(".cer", ".crt", ".der", ".jks", ".key", ".keystore", ".p12", ".pem", ".pfx", ".snk") -or
        $fileName -in @("id_dsa", "id_ecdsa", "id_ed25519", "id_rsa", "secrets.json") -or
        $fileName.EndsWith(".secrets.json", [StringComparison]::OrdinalIgnoreCase)) {
        Add-PolicyFinding -Rule "SEC002_TRACKED_SECRET_FILE" -Path $normalizedPath
    }

    if ($normalizedPath -match '(^|/)(artifacts|TestResults|coverage)(/|$)' -or
        $extension -in @(".coverage", ".coveragexml", ".trx") -or
        $fileName -match '^(?:coverage|cobertura).+\.xml$') {
        Add-PolicyFinding -Rule "SEC003_TRACKED_TEST_EVIDENCE" -Path $normalizedPath
    }

    if (!(Test-IsTextFile -Path $normalizedPath)) {
        continue
    }

    $fullPath = Join-Path $repositoryRoot $trackedPath
    $lines = [IO.File]::ReadAllLines($fullPath)

    $privateKeyPattern = '-----BEGIN ' + '(?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
    Test-ContainsPattern `
        -Lines $lines `
        -Pattern $privateKeyPattern `
        -Rule "SEC004_PRIVATE_KEY_CONTENT" `
        -Path $normalizedPath

    Test-ContainsPattern `
        -Lines $lines `
        -Pattern '(?i)(?:^|["''])Authorization(?:["'']|\s)*\s*[:=]\s*["'']?(?:Basic|Bearer)\s+[A-Za-z0-9._~+/=-]{8,}' `
        -Rule "SEC005_USABLE_AUTHORIZATION" `
        -Path $normalizedPath

    Test-ContainsPattern `
        -Lines $lines `
        -Pattern '(?i)\bBasic\s+(?=[A-Za-z0-9+/=]{16,}\b)(?=[A-Za-z0-9+/=]*[0-9=])[A-Za-z0-9+/]{16,}={0,2}\b' `
        -Rule "SEC006_COMMITTED_BASIC_CREDENTIAL" `
        -Path $normalizedPath

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = $lines[$lineIndex]
        $connectionPassword = [regex]::Match(
            $line,
            '(?:\bPassword=|\bPASSWORD\s*=\s*)(?<value>N?''\$\([A-Za-z_][A-Za-z0-9_]*\)''|[^\s;,"''][^;,"'']*)')
        if ($connectionPassword.Success -and
            !(Test-IsApprovedCredentialPlaceholder -Value $connectionPassword.Groups["value"].Value)) {
            Add-PolicyFinding `
                -Rule "SEC007_USABLE_CONNECTION_PASSWORD" `
                -Path $normalizedPath `
                -LineNumber ($lineIndex + 1)
        }

        if ($extension -in @(".config", ".env", ".example", ".json", ".props", ".targets", ".yaml", ".yml")) {
            $configuredPassword = [regex]::Match(
                $line,
                '(?i)^\s*["'']?[A-Za-z0-9_.:-]*password[A-Za-z0-9_.:-]*["'']?\s*[:=]\s*(?<value>\$\{[^}]+\}|"[^"]*"|''[^'']*''|[^\s,#]+)')
            if ($configuredPassword.Success -and
                !(Test-IsApprovedCredentialPlaceholder -Value $configuredPassword.Groups["value"].Value)) {
                Add-PolicyFinding `
                    -Rule "SEC008_USABLE_CONFIG_PASSWORD" `
                    -Path $normalizedPath `
                    -LineNumber ($lineIndex + 1)
            }
        }

        if ($fileName -eq "Dockerfile") {
            $dockerPassword = [regex]::Match(
                $line,
                '(?i)^\s*(?:ARG|ENV)\s+[A-Z0-9_]*PASSWORD[A-Z0-9_]*(?:=|\s+)\s*(?<value>\$\{[^}]+\}|"[^"]*"|''[^'']*''|\S+)')
            if ($dockerPassword.Success -and
                !(Test-IsApprovedCredentialPlaceholder -Value $dockerPassword.Groups["value"].Value)) {
                Add-PolicyFinding `
                    -Rule "SEC009_USABLE_DOCKER_PASSWORD" `
                    -Path $normalizedPath `
                    -LineNumber ($lineIndex + 1)
            }
        }
    }
}

$containerSourcePaths = @(
    ".env.example",
    "compose.yaml",
    "Dockerfile",
    "scripts/validate-container-setup.ps1",
    "tests/CmsSync.IntegrationTests/Infrastructure/SqlServerTestConstants.cs"
)
foreach ($containerPath in $containerSourcePaths) {
    if ($containerPath -notin $trackedFiles) {
        Add-PolicyFinding -Rule "IMG001_MISSING_PIN_SOURCE" -Path $containerPath
        continue
    }

    $containerLines = [IO.File]::ReadAllLines((Join-Path $repositoryRoot $containerPath))
    $containerText = $containerLines -join "`n"
    $normalizedContainerText = ($containerText -replace '\s', '') -replace '["''+;]', ''
    if ($normalizedContainerText.IndexOf(
            $expectedSqlServerImage,
            [StringComparison]::Ordinal) -lt 0) {
        Add-PolicyFinding -Rule "IMG002_EXPECTED_PIN_MISSING" -Path $containerPath
    }

    Test-ContainsPattern `
        -Lines $containerLines `
        -Pattern '(?i)(?:mssql/server:(?:latest|2022-latest)|azure-sql-edge|\(localdb\)|platform\s*:\s*linux/amd64)' `
        -Rule "IMG003_PROHIBITED_CONTAINER_TARGET" `
        -Path $containerPath
}

foreach ($containerPath in $containerSourcePaths | Where-Object { $_ -ne "tests/CmsSync.IntegrationTests/Infrastructure/SqlServerTestConstants.cs" }) {
    if ($containerPath -notin $trackedFiles) {
        continue
    }

    $containerText = [IO.File]::ReadAllText((Join-Path $repositoryRoot $containerPath))
    $containerTextWithoutExpectedImage = $containerText.Replace($expectedSqlServerImage, "")
    if ($containerTextWithoutExpectedImage.IndexOf(
            "mcr.microsoft.com/mssql/server:",
            [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        Add-PolicyFinding -Rule "IMG004_DIFFERENT_OR_UNPINNED_SQL_IMAGE" -Path $containerPath
    }
}

$dockerfileLines = [IO.File]::ReadAllLines((Join-Path $repositoryRoot "Dockerfile"))
for ($lineIndex = 0; $lineIndex -lt $dockerfileLines.Count; $lineIndex++) {
    $imageArgument = [regex]::Match(
        $dockerfileLines[$lineIndex],
        '^ARG\s+[A-Z0-9_]+_IMAGE=(?<image>\S+)$')
    if ($imageArgument.Success -and
        $imageArgument.Groups["image"].Value -notmatch '@sha256:[0-9a-f]{64}$') {
        Add-PolicyFinding `
            -Rule "IMG005_MUTABLE_CONTAINER_DEFAULT" `
            -Path "Dockerfile" `
            -LineNumber ($lineIndex + 1)
    }
}

$workflowPaths = @(
    $trackedFiles |
        Where-Object { $_ -match '^\.github/workflows/.+\.ya?ml$' }
)
foreach ($workflowPath in $workflowPaths) {
    $workflowLines = [IO.File]::ReadAllLines((Join-Path $repositoryRoot $workflowPath))
    $workflowText = $workflowLines -join "`n"

    $workflowRules = @(
        @{ Pattern = '(?i)\bpull_request_target\b'; Rule = "WF001_PULL_REQUEST_TARGET" },
        @{ Pattern = '(?i)\bself-hosted\b'; Rule = "WF002_SELF_HOSTED" },
        @{ Pattern = '(?i)\bubuntu-latest\b'; Rule = "WF003_MUTABLE_RUNNER" },
        @{ Pattern = '(?i)\bcontinue-on-error\s*:'; Rule = "WF004_CONTINUE_ON_ERROR" },
        @{ Pattern = '\|\|\s*true\b'; Rule = "WF005_IGNORED_EXIT_CODE" },
        @{ Pattern = '(?i)\$\{\{\s*secrets(?:\.|\[|\s|\})'; Rule = "WF006_SECRET_INTERPOLATION" },
        @{ Pattern = '(?i)\b(?:toJson\s*\(\s*(?:env|secrets)|docker\s+inspect)'; Rule = "WF007_ENVIRONMENT_DUMP" },
        @{ Pattern = '(?im)^\s*(?:env|printenv)\s*$'; Rule = "WF007_ENVIRONMENT_DUMP" },
        @{ Pattern = '(?i)(?:mssql/server:(?:latest|2022-latest)|azure-sql-edge|\(localdb\)|platform\s*:\s*linux/amd64)'; Rule = "WF008_PROHIBITED_RUNTIME" },
        @{ Pattern = '(?im)^\s*(?:[A-Za-z-]+\s*:\s*write|write-all\b|read-all\b)'; Rule = "WF009_BROAD_PERMISSION" }
    )
    foreach ($workflowRule in $workflowRules) {
        Test-ContainsPattern `
            -Lines $workflowLines `
            -Pattern $workflowRule.Pattern `
            -Rule $workflowRule.Rule `
            -Path $workflowPath
    }

    if ([regex]::Matches($workflowText, '(?m)^permissions:\s*$').Count -ne 1 -or
        [regex]::Matches($workflowText, '(?m)^  contents: read\s*$').Count -ne 1) {
        Add-PolicyFinding -Rule "WF010_PERMISSIONS_NOT_CONTENTS_READ_ONLY" -Path $workflowPath
    }

    for ($lineIndex = 0; $lineIndex -lt $workflowLines.Count; $lineIndex++) {
        $usesMatch = [regex]::Match(
            $workflowLines[$lineIndex],
            '^\s*uses:\s*(?<repository>[^@\s]+)@(?<reference>\S+)\s*(?<comment>#.*)?$')
        if (!$usesMatch.Success) {
            continue
        }

        $repository = $usesMatch.Groups["repository"].Value
        $reference = $usesMatch.Groups["reference"].Value
        $comment = $usesMatch.Groups["comment"].Value
        if ($repository -notin @("actions/checkout", "actions/setup-dotnet", "actions/upload-artifact")) {
            Add-PolicyFinding `
                -Rule "WF011_THIRD_PARTY_ACTION" `
                -Path $workflowPath `
                -LineNumber ($lineIndex + 1)
        }
        if ($reference -notmatch '^[0-9a-f]{40}$') {
            Add-PolicyFinding `
                -Rule "WF012_ACTION_NOT_PINNED" `
                -Path $workflowPath `
                -LineNumber ($lineIndex + 1)
        }
        if ($comment -notmatch '^#\s+v\d+\.\d+\.\d+\s*$') {
            Add-PolicyFinding `
                -Rule "WF013_ACTION_RELEASE_COMMENT_MISSING" `
                -Path $workflowPath `
                -LineNumber ($lineIndex + 1)
        }

        if ($repository -eq "actions/upload-artifact") {
            $uploadBlock = Get-StepBlock -Lines $workflowLines -ContainingLine $lineIndex
            if ($uploadBlock -notmatch '(?m)^\s*if:\s*always\(\)\s*$') {
                Add-PolicyFinding -Rule "WF014_ARTIFACT_NOT_ALWAYS" -Path $workflowPath
            }
            if ($uploadBlock -notmatch '(?m)^\s*if-no-files-found:\s*error\s*$') {
                Add-PolicyFinding -Rule "WF015_ARTIFACT_MISSING_EVIDENCE_ALLOWED" -Path $workflowPath
            }
        }
    }
}

if ($findings.Count -gt 0) {
    foreach ($finding in $findings) {
        $location = $finding.Path
        if ($finding.LineNumber -gt 0) {
            $location = "$location`:$($finding.LineNumber)"
        }

        [Console]::Error.WriteLine("[$($finding.Rule)] $location")
    }

    throw "Tracked repository policy validation failed with $($findings.Count) finding(s)."
}

Write-Output "Tracked repository policy validation passed."
Write-Output "This narrow deterministic scan does not replace GitHub secret scanning or a full security product."
