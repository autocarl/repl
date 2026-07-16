[CmdletBinding()]
param(
    [string] $NbgvCommand = 'nbgv'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(ValueFromRemainingArguments)]
        [string[]] $Arguments
    )

    $output = @(& $Command @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }

    return $output
}

$repositoryRoot = ((Invoke-Native git rev-parse --show-toplevel) -join "`n").Trim()
Push-Location $repositoryRoot
try {
    $mainBranch = ((Invoke-Native git branch --show-current) -join "`n").Trim()
    if ($mainBranch -cne 'main') {
        throw "Release preparation must run from 'main'; current branch is '$mainBranch'."
    }

    $status = ((Invoke-Native git status --porcelain) -join "`n").Trim()
    if ($status.Length -ne 0) {
        throw 'Release preparation requires a clean working tree.'
    }

    $developmentVersion = [string](Get-Content -LiteralPath version.json -Raw | ConvertFrom-Json).version
    $developmentMatch = [regex]::Match(
        $developmentVersion,
        '^(?<major>\d+)\.(?<minor>\d+)\.0-dev\.\{height\}$')
    if (-not $developmentMatch.Success) {
        throw "Expected main to use exactly 'x.y.0-dev.{height}', but found '$developmentVersion'."
    }

    $majorVersion = $developmentMatch.Groups['major'].Value
    $minorVersion = $developmentMatch.Groups['minor'].Value
    $expectedReleaseVersion = "$majorVersion.$minorVersion.0"
    $servicingVersion = "$majorVersion.$minorVersion"

    $planOutput = (Invoke-Native $NbgvCommand prepare-release --format json --what-if) -join "`n"
    $releasePlan = $planOutput | ConvertFrom-Json
    if ($null -eq $releasePlan.NewBranch) {
        throw 'NBGV did not plan a new release branch.'
    }

    $releaseBranch = [string]$releasePlan.NewBranch.Name
    $releaseVersion = [string]$releasePlan.NewBranch.Version
    if ($releaseVersion -cne $expectedReleaseVersion) {
        throw "Expected NBGV to plan release '$expectedReleaseVersion', but it planned '$releaseVersion'."
    }

    $prepareOutput = (Invoke-Native $NbgvCommand prepare-release --format json) -join "`n"
    $preparedRelease = $prepareOutput | ConvertFrom-Json
    if ($null -eq $preparedRelease.NewBranch -or
        [string]$preparedRelease.NewBranch.Name -cne $releaseBranch -or
        [string]$preparedRelease.NewBranch.Version -cne $releaseVersion) {
        throw 'The release created by NBGV did not match the validated release plan.'
    }

    $currentBranch = ((Invoke-Native git branch --show-current) -join "`n").Trim()
    if ($currentBranch -cne $mainBranch) {
        throw "NBGV unexpectedly left the repository on '$currentBranch' instead of '$mainBranch'."
    }

    Invoke-Native git switch $releaseBranch | Out-Null
    Invoke-Native $NbgvCommand set-version $servicingVersion | Out-Null

    $configuredVersion = [string](Get-Content -LiteralPath version.json -Raw | ConvertFrom-Json).version
    if ($configuredVersion -cne $servicingVersion) {
        throw "NBGV set '$configuredVersion' instead of '$servicingVersion' on '$releaseBranch'."
    }

    Invoke-Native git add version.json | Out-Null
    Invoke-Native git commit -m "Set servicing version to '$servicingVersion'" | Out-Null
    Invoke-Native git switch $mainBranch | Out-Null

    Write-Host "Prepared '$releaseBranch' with servicing version '$servicingVersion'."
    Write-Host "Main remains on the explicit development height pattern."
}
finally {
    Pop-Location
}
