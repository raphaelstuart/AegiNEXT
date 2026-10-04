#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('osx-arm64', 'osx-x64', 'win-x64')][string] $RuntimeIdentifier,
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $FfmpegRoot,
    [string] $SdlRoot,
    [string] $OutputDirectory,
    [string] $LicenseDirectory,
    [string[]] $RuntimeDependencyDirectory = @(),
    [string] $SigningIdentity = '-',
    [switch] $SkipBuild,
    [ValidateRange(1, 128)][int] $Jobs = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try
{
    Import-Module (Join-Path $PSScriptRoot 'scripts/publish/AegiNext.Publish.psm1') -Force
    $arguments = @{} + $PSBoundParameters
    $arguments.RepositoryRoot = $PSScriptRoot
    Invoke-AegiNextPublish @arguments
    exit 0
}
catch
{
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
