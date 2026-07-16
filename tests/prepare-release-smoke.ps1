param(
    [string] $PrepareReleaseScript = (Join-Path $PSScriptRoot '..' 'eng' 'prepare-release.ps1'),
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

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Expected,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $Context
    )

    if ($Actual -cne $Expected) {
        throw "$Context. Expected '$Expected', actual '$Actual'."
    }
}

$PrepareReleaseScript = [System.IO.Path]::GetFullPath($PrepareReleaseScript)
if (-not (Test-Path -LiteralPath $PrepareReleaseScript -PathType Leaf)) {
    throw "Release preparation script not found: $PrepareReleaseScript"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "repl-prepare-release-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

Push-Location $tempRoot
try {
    Invoke-Native git init -b main | Out-Null
    Invoke-Native git config user.name 'Release Smoke Test' | Out-Null
    Invoke-Native git config user.email 'release-smoke@example.invalid' | Out-Null

    $versionConfig = @{
        '$schema' = 'https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json'
        version = '1.2.0-dev.{height}'
        nuGetPackageVersion = @{
            semVer = 2.0
        }
        publicReleaseRefSpec = @(
            '^refs/heads/main$'
            '^refs/heads/release/.*$'
        )
        release = @{
            branchName = 'release/{version}'
            firstUnstableTag = 'dev'
        }
    }

    $versionConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath version.json
    'release smoke fixture' | Set-Content -LiteralPath README.md
    Invoke-Native git add version.json README.md | Out-Null
    Invoke-Native git commit -m 'Initial development version' | Out-Null

    & $PrepareReleaseScript -NbgvCommand $NbgvCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Release preparation script failed with exit code $LASTEXITCODE."
    }

    $currentBranch = (Invoke-Native git branch --show-current) -join "`n"
    Assert-Equal 'main' $currentBranch 'Wrapper should return to the original branch'

    $mainVersion = (Get-Content -LiteralPath version.json -Raw | ConvertFrom-Json).version
    Assert-Equal '1.3.0-dev.{height}' $mainVersion 'Main should retain the explicit dev height pattern'

    $releaseJson = (Invoke-Native git show 'release/1.2.0:version.json') -join "`n"
    $releaseVersion = ($releaseJson | ConvertFrom-Json).version
    Assert-Equal '1.2' $releaseVersion 'Release branch should use Git height as the patch component'

    $status = (Invoke-Native git status --porcelain) -join "`n"
    Assert-Equal '' $status 'Repository should be clean after release preparation'

    Invoke-Native git switch release/1.2.0 | Out-Null
    $firstPatch = (Invoke-Native $NbgvCommand get-version '--variable=NuGetPackageVersion' '--public-release=true') -join "`n"
    Assert-Equal '1.2.1' $firstPatch 'Release conversion commit should produce the first patch'

    Invoke-Native git commit --allow-empty -m 'Backport fix' | Out-Null
    $secondPatch = (Invoke-Native $NbgvCommand get-version '--variable=NuGetPackageVersion' '--public-release=true') -join "`n"
    Assert-Equal '1.2.2' $secondPatch 'Next servicing commit should increment the patch version'

    foreach ($invalidVersion in @('1.2.7-dev.{height}', '1.2.0-rc.{height}')) {
        $invalidRoot = Join-Path ([System.IO.Path]::GetTempPath()) "repl-invalid-release-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $invalidRoot | Out-Null

        Push-Location $invalidRoot
        try {
            Invoke-Native git init -b main | Out-Null
            Invoke-Native git config user.name 'Release Smoke Test' | Out-Null
            Invoke-Native git config user.email 'release-smoke@example.invalid' | Out-Null

            $invalidConfig = $versionConfig.Clone()
            $invalidConfig.version = $invalidVersion
            $invalidConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath version.json
            'invalid release smoke fixture' | Set-Content -LiteralPath README.md
            Invoke-Native git add version.json README.md | Out-Null
            Invoke-Native git commit -m 'Invalid development version' | Out-Null

            $headBefore = (Invoke-Native git rev-parse HEAD) -join "`n"
            $errorMessage = $null
            try {
                & $PrepareReleaseScript -NbgvCommand $NbgvCommand
            }
            catch {
                $errorMessage = $_.Exception.Message
            }

            if ($null -eq $errorMessage) {
                throw "Wrapper accepted invalid development version '$invalidVersion'."
            }

            if ($errorMessage -cnotlike "Expected main to use exactly*") {
                throw "Unexpected validation error for '$invalidVersion': $errorMessage"
            }

            $currentBranch = (Invoke-Native git branch --show-current) -join "`n"
            Assert-Equal 'main' $currentBranch "Invalid version '$invalidVersion' should remain on main"

            $headAfter = (Invoke-Native git rev-parse HEAD) -join "`n"
            Assert-Equal $headBefore $headAfter "Invalid version '$invalidVersion' should not create commits"

            $releaseBranches = (Invoke-Native git branch --list 'release/*') -join "`n"
            Assert-Equal '' $releaseBranches "Invalid version '$invalidVersion' should not create a release branch"

            $invalidStatus = (Invoke-Native git status --porcelain) -join "`n"
            Assert-Equal '' $invalidStatus "Invalid version '$invalidVersion' should leave a clean repository"
        }
        finally {
            Pop-Location
            Remove-Item -LiteralPath $invalidRoot -Recurse -Force
        }
    }

    Write-Host 'prepare-release smoke test passed.'
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}
