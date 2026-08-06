[CmdletBinding()]
param(
    [switch] $RotateSecrets,
    [AllowNull()]
    [string] $CmsUsername = $null,

    [AllowNull()]
    [string] $ConsumerUsername = $null,

    [AllowNull()]
    [string] $AdministratorUsername = $null
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

function Convert-ToGuidD {
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

    return $guidValue
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

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Values) {
        if (!$seen.Add($value)) {
            throw $ErrorMessage
        }
    }
}

function Resolve-RequiredUsername {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [AllowNull()]
        [string] $Value,

        [int] $MinimumLength = 1,

        [int] $MaximumLength = 128
    )

    $resolvedValue = $Value
    if ([string]::IsNullOrWhiteSpace($resolvedValue)) {
        $resolvedValue = Read-Host -Prompt "Enter value for '$Name'"
    }

    Assert-ValidUsername `
        -Name $Name `
        -Value $resolvedValue `
        -MinimumLength $MinimumLength `
        -MaximumLength $MaximumLength

    return $resolvedValue
}

function Read-GuidPasswordFromPrompt {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    $secureValue = $null
    $bstr = [IntPtr]::Zero
    $plaintext = $null
    $plaintextCharacters = $null

    try {
        $secureValue = Read-Host -Prompt "Enter value for '$Name' (GUID D format)" -AsSecureString
        if ($null -eq $secureValue) {
            throw "Parameter '$Name' is required."
        }

        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
        $plaintext = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        if ([string]::IsNullOrWhiteSpace($plaintext)) {
            throw "Parameter '$Name' is required."
        }

        $plaintextCharacters = $plaintext.ToCharArray()
        $candidate = [string]::new($plaintextCharacters)
        return Convert-ToGuidD -Name $Name -Value $candidate
    }
    finally {
        if ($null -ne $plaintextCharacters) {
            [Array]::Clear($plaintextCharacters, 0, $plaintextCharacters.Length)
        }

        $plaintext = $null

        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }

        if ($secureValue -is [IDisposable]) {
            $secureValue.Dispose()
        }
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

function Resolve-ActorPassword {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    if (!$RotateSecrets) {
        $existing = Get-ExistingParameterSecret -Name $Name
        if (!([string]::IsNullOrWhiteSpace($existing))) {
            return (Convert-ToGuidD -Name $Name -Value $existing).ToString("D")
        }
    }

    return (Read-GuidPasswordFromPrompt -Name $Name).ToString("D")
}

$aspireVersion = Get-AspireVersion
Assert-AspireVersion -Version $aspireVersion -ExpectedPrefix $requiredAspireVersionPrefix

$CmsUsername = Resolve-RequiredUsername -Name "cms-username" -Value $CmsUsername -MinimumLength 10 -MaximumLength 20
$ConsumerUsername = Resolve-RequiredUsername -Name "consumer-username" -Value $ConsumerUsername
$AdministratorUsername = Resolve-RequiredUsername -Name "administrator-username" -Value $AdministratorUsername

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

$cmsPassword = Resolve-ActorPassword -Name "cms-password"
$consumerPassword = Resolve-ActorPassword -Name "consumer-password"
$administratorPassword = Resolve-ActorPassword -Name "administrator-password"

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

Write-Output "Aspire local secrets configured for apphost.cs."
Write-Output "Configured parameters: mssql-sa-password, migration-sql-password, write-sql-password, read-sql-password, cms-username, cms-password, consumer-username, consumer-password, administrator-username, administrator-password."
