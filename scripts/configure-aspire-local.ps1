[CmdletBinding()]
param(
    [switch] $RotateSecrets,
    [string] $CmsUsername = "cmssvc-local1",
    [string] $ConsumerUsername = "consumer-local1",
    [string] $AdministratorUsername = "admin-local1"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appHostPath = Join-Path $repositoryRoot "apphost.cs"
$requiredAspireVersionPrefix = "13.4.0"

if (!(Test-Path -Path $appHostPath -PathType Leaf)) {
    throw "The AppHost file was not found at '$appHostPath'."
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

function Get-ExistingParameterSecret {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $secretOutput = & aspire secret get "Parameters:$Name" --apphost $appHostPath --non-interactive 2>$null
        $secretExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($secretExitCode -ne 0) {
        return $null
    }

    $value = ($secretOutput -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value
}

function Set-ParameterSecret {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    & aspire secret set "Parameters:$Name" $Value --apphost $appHostPath --non-interactive 1>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set secret value for parameter '$Name'."
    }
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

function New-GuidPassword {
    return [Guid]::NewGuid().ToString("D")
}

function Assert-ValidSqlPassword {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $isValid =
        $Value.Length -ge 20 -and
        $Value.Length -le 128 -and
        $Value -cmatch "[-A-Za-z0-9!@#%^*_.+=,?]+" -and
        $Value -cmatch "[A-Z]" -and
        $Value -cmatch "[a-z]" -and
        $Value -cmatch "[0-9]" -and
        $Value -cmatch "[!@#%^*_.+=,?-]"

    if (!$isValid) {
        throw "Parameter '$Name' does not satisfy the local SQL password policy."
    }
}

function Assert-ValidGuidPassword {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $guidValue = [Guid]::Empty
    if (![Guid]::TryParseExact($Value, "D", [ref] $guidValue)) {
        throw "Parameter '$Name' must use GUID D format."
    }
}

function Assert-ValidUsername {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value,

        [int] $MinimumLength = 1,

        [int] $MaximumLength = 128
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Parameter '$Name' is required."
    }

    if (!($Value.Equals($Value.Trim(), [StringComparison]::Ordinal))) {
        throw "Parameter '$Name' must not contain leading or trailing whitespace."
    }

    if ($Value.IndexOf(':', [StringComparison]::Ordinal) -ge 0) {
        throw "Parameter '$Name' must not contain ':'."
    }

    if ($Value.Length -lt $MinimumLength -or $Value.Length -gt $MaximumLength) {
        throw "Parameter '$Name' length is invalid."
    }
}

function Assert-DistinctValues {
    param(
        [Parameter(Mandatory)]
        [string] $ErrorMessage,

        [Parameter(Mandatory)]
        [string[]] $Values
    )

    if ($Values.Length -ne ($Values | Select-Object -Unique).Length) {
        throw $ErrorMessage
    }
}

function Resolve-SecretValue {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Generator,

        [Parameter(Mandatory)]
        [scriptblock] $Validator
    )

    $value = $null
    if (!$RotateSecrets) {
        $value = Get-ExistingParameterSecret -Name $Name
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = & $Generator
    }

    & $Validator $Name $value
    return $value
}

$aspireVersion = Get-AspireVersion
Assert-AspireVersion -Version $aspireVersion -ExpectedPrefix $requiredAspireVersionPrefix

Assert-ValidUsername -Name "cms-username" -Value $CmsUsername -MinimumLength 10 -MaximumLength 20
Assert-ValidUsername -Name "consumer-username" -Value $ConsumerUsername
Assert-ValidUsername -Name "administrator-username" -Value $AdministratorUsername
Assert-DistinctValues `
    -ErrorMessage "CMS, consumer, and administrator usernames must be distinct." `
    -Values @($CmsUsername, $ConsumerUsername, $AdministratorUsername)

$mssqlSaPassword = Resolve-SecretValue `
    -Name "mssql-sa-password" `
    -Generator { New-SqlPassword } `
    -Validator ${function:Assert-ValidSqlPassword}
$migrationSqlPassword = Resolve-SecretValue `
    -Name "migration-sql-password" `
    -Generator { New-SqlPassword } `
    -Validator ${function:Assert-ValidSqlPassword}
$writeSqlPassword = Resolve-SecretValue `
    -Name "write-sql-password" `
    -Generator { New-SqlPassword } `
    -Validator ${function:Assert-ValidSqlPassword}
$readSqlPassword = Resolve-SecretValue `
    -Name "read-sql-password" `
    -Generator { New-SqlPassword } `
    -Validator ${function:Assert-ValidSqlPassword}

$cmsPassword = Resolve-SecretValue `
    -Name "cms-password" `
    -Generator { New-GuidPassword } `
    -Validator ${function:Assert-ValidGuidPassword}
$consumerPassword = Resolve-SecretValue `
    -Name "consumer-password" `
    -Generator { New-GuidPassword } `
    -Validator ${function:Assert-ValidGuidPassword}
$administratorPassword = Resolve-SecretValue `
    -Name "administrator-password" `
    -Generator { New-GuidPassword } `
    -Validator ${function:Assert-ValidGuidPassword}

Assert-DistinctValues `
    -ErrorMessage "SQL passwords must be distinct." `
    -Values @($mssqlSaPassword, $migrationSqlPassword, $writeSqlPassword, $readSqlPassword)
Assert-DistinctValues `
    -ErrorMessage "Actor passwords must be distinct." `
    -Values @($cmsPassword, $consumerPassword, $administratorPassword)

Set-ParameterSecret -Name "mssql-sa-password" -Value $mssqlSaPassword
Set-ParameterSecret -Name "migration-sql-password" -Value $migrationSqlPassword
Set-ParameterSecret -Name "write-sql-password" -Value $writeSqlPassword
Set-ParameterSecret -Name "read-sql-password" -Value $readSqlPassword

Set-ParameterSecret -Name "cms-username" -Value $CmsUsername
Set-ParameterSecret -Name "cms-password" -Value $cmsPassword
Set-ParameterSecret -Name "consumer-username" -Value $ConsumerUsername
Set-ParameterSecret -Name "consumer-password" -Value $consumerPassword
Set-ParameterSecret -Name "administrator-username" -Value $AdministratorUsername
Set-ParameterSecret -Name "administrator-password" -Value $administratorPassword

$secretPathOutput = & aspire secret path --apphost $appHostPath --non-interactive
if ($LASTEXITCODE -ne 0) {
    throw "Configured secrets successfully, but failed to resolve Aspire secret path."
}

$secretPath = ($secretPathOutput | Select-Object -First 1).Trim()
Write-Output "Aspire local secrets configured for apphost.cs."
Write-Output "Secrets file path: $secretPath"
Write-Output "Configured parameters: mssql-sa-password, migration-sql-password, write-sql-password, read-sql-password, cms-username, cms-password, consumer-username, consumer-password, administrator-username, administrator-password."
