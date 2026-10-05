#Requires -Version 7.2
<#
.SYNOPSIS
Runs static analysis and isolated build-script tests. No toolchain packages are installed by the tests.
.PARAMETER RestoreTools
Downloads pinned QA modules from PowerShell Gallery into this repository's ignored artifacts directory.
#>
[CmdletBinding()]
param([switch] $RestoreTools)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$moduleDirectory = Join-Path $repositoryRoot 'artifacts/tools/powershell'
$qaModules = @{ Pester = '5.7.1'; PSScriptAnalyzer = '1.24.0' }

foreach ($name in $qaModules.Keys)
{
    $version = $qaModules[$name]
    $manifest = Join-Path $moduleDirectory "$name/$version/$name.psd1"
    if (!(Test-Path -LiteralPath $manifest))
    {
        if (!$RestoreTools)
        {
            throw "Missing repository QA module $name $version. Run scripts/test-build.ps1 -RestoreTools."
        }

        [IO.Directory]::CreateDirectory($moduleDirectory) | Out-Null
        if (Get-Command Save-PSResource -ErrorAction SilentlyContinue)
        {
            Save-PSResource -Name $name -Version $version -Path $moduleDirectory -Repository PSGallery -TrustRepository
        }
        else
        {
            Save-Module -Name $name -RequiredVersion $version -Path $moduleDirectory -Repository PSGallery -Force
        }
    }

    Import-Module $manifest -Force
}

$findings = @(
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'build.ps1') -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'build-debug-native.ps1') -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'publish.ps1') -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'scripts/build') -Recurse -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'scripts/publish') -Recurse -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path $PSCommandPath -Severity Warning, Error
    Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot 'Tests/Build') -Recurse -Severity Warning, Error `
        -ExcludeRule PSUseDeclaredVarsMoreThanAssignments, PSUseShouldProcessForStateChangingFunctions
)
if ($findings.Count)
{
    $findings | Format-Table RuleName, ScriptName, Line, Message -Wrap | Out-Host
    throw 'PowerShell static analysis failed.'
}

$resultsDirectory = Join-Path $repositoryRoot 'artifacts/verification'
[IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null
$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path $repositoryRoot 'Tests/Build'
$configuration.Run.PassThru = $true
$configuration.TestResult.Enabled = $true
$configuration.TestResult.OutputPath = Join-Path $resultsDirectory 'build-scripts-tests.xml'
$configuration.Output.Verbosity = 'Detailed'
$result = Invoke-Pester -Configuration $configuration
if ($result.FailedCount -gt 0 -or $result.FailedContainersCount -gt 0 -or $result.TotalCount -eq 0)
{
    exit 1
}

exit 0
