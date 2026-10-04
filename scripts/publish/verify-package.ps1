#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PackageDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AegiNext.Publish.psm1') -Force
Test-AegiNextPublishedPackage -PackageDirectory $PackageDirectory
