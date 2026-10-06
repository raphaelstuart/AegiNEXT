#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}

Describe 'Ordinary NuGet restore accepts changed project references' {
    It 'restores a new application reference without creating lock files for <Mode>' -ForEach @(
        @{ Mode = 'Portable'; RuntimeIdentifier = '' }
        @{ Mode = 'Windows x64'; RuntimeIdentifier = 'win-x64' }
    ) {
        param($Mode, $RuntimeIdentifier)

        $fixture = Join-Path $TestDrive $Mode
        foreach ($name in @('Core', 'Application', 'Entry'))
        {
            [IO.Directory]::CreateDirectory((Join-Path $fixture $name)) | Out-Null
        }
        foreach ($name in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.Config'))
        {
            Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $fixture
        }
        Set-Content -LiteralPath (Join-Path $fixture 'Core/AegiNext.Core.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk" />'
        $coreReference = '<ProjectReference Include="../Core/AegiNext.Core.csproj" />'
        Set-Content -LiteralPath (Join-Path $fixture 'Application/AegiNext.Application.csproj') -Value "<Project Sdk=`"Microsoft.NET.Sdk`"><ItemGroup>$coreReference</ItemGroup></Project>"
        $projectFile = Join-Path $fixture 'Entry/Entry.csproj'
        $source = "<Project Sdk=`"Microsoft.NET.Sdk`"><ItemGroup>$coreReference</ItemGroup></Project>"
        Set-Content -LiteralPath $projectFile -Value $source
        $arguments = @('restore', $projectFile)
        $assetsDirectory = Join-Path $fixture 'Entry/obj'
        if ($RuntimeIdentifier)
        {
            $arguments += @('-r', $RuntimeIdentifier, "-p:AegiNextRuntimeIdentifier=$RuntimeIdentifier")
            $assetsDirectory = Join-Path $assetsDirectory $RuntimeIdentifier
        }
        else
        {
            $arguments += '-p:RuntimeIdentifier='
        }
        $PSNativeCommandUseErrorActionPreference = $false
        $output = & dotnet @arguments 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        @(Get-ChildItem -LiteralPath $fixture -Filter 'packages*.lock.json' -File -Recurse).Count | Should -Be 0
        $assetsFile = Join-Path $assetsDirectory 'project.assets.json'
        $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json -AsHashtable
        @($assets.libraries.Keys | Where-Object { $_ -like 'AegiNext.Application/*' }).Count | Should -Be 0

        $applicationReference = '<ProjectReference Include="../Application/AegiNext.Application.csproj" />'
        Set-Content -LiteralPath $projectFile -Value $source.Replace('</ItemGroup>', "$applicationReference</ItemGroup>")
        $output = & dotnet @arguments 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        @(Get-ChildItem -LiteralPath $fixture -Filter 'packages*.lock.json' -File -Recurse).Count | Should -Be 0
        $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json -AsHashtable
        $applicationLibraries = @($assets.libraries.Keys | Where-Object { $_ -like 'AegiNext.Application/*' })
        $applicationLibraries.Count | Should -Be 1
        $assets.libraries[$applicationLibraries[0]].type | Should -Be 'project'
    }
}

Describe 'Publishing entry points reload changed nested build modules' {
    It 'reloads the current build plan through <EntryPoint> in the same PowerShell session' -ForEach @(
        @{ EntryPoint = 'Publish' }
        @{ EntryPoint = 'Release' }
    ) {
        param($EntryPoint)

        $fixture = Join-Path $TestDrive $EntryPoint
        [IO.Directory]::CreateDirectory($fixture) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository 'scripts') -Destination $fixture -Recurse
        $runner = Join-Path $fixture 'reload.ps1'
        @'
param([string] $Fixture, [string] $EntryPoint)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$buildPath = Join-Path $Fixture 'scripts/build/AegiNext.Build.psm1'
$publishPath = Join-Path $Fixture 'scripts/publish/AegiNext.Publish.psm1'
$currentSource = Get-Content -LiteralPath $buildPath -Raw
$staleSource = $currentSource.Replace("'AegiNext.Product.slnf'", "'AegiNext.sln'")
Set-Content -LiteralPath $buildPath -Value $staleSource -Encoding utf8NoBOM
$publish = Import-Module $publishPath -PassThru
$hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
$before = & $publish {
    param($Root, $HostInfo)
    $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $Root -HostInfo $HostInfo -Configuration Release)
    $plan[0].Arguments[1]
} $Fixture $hostInfo
if ($before -ne (Join-Path $Fixture 'AegiNext.sln'))
{
    throw "The fixture did not load the stale solution plan: $before"
}
Set-Content -LiteralPath $buildPath -Value $currentSource -Encoding utf8NoBOM
if ($EntryPoint -eq 'Release')
{
    $release = Import-Module (Join-Path $Fixture 'scripts/publish/AegiNext.Release.psm1') -Force -PassThru
    & $release { Get-AegiNextHost } | Out-Null
    $publish = & $release { (Get-Command Invoke-AegiNextPublish).Module }
}
else
{
    $publish = Import-Module $publishPath -Force -PassThru
}
$after = & $publish {
    param($Root, $HostInfo)
    $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $Root -HostInfo $HostInfo -Configuration Release)
    $plan[0].Arguments[1]
} $Fixture $hostInfo
if ($after -ne (Join-Path $Fixture 'AegiNext.Product.slnf'))
{
    throw "$EntryPoint kept the stale nested build module: $after"
}
Write-Output "$EntryPoint reloaded the current product build plan."
'@ | Set-Content -LiteralPath $runner -Encoding utf8NoBOM
        $pwsh = (Get-Process -Id $PID).Path
        $PSNativeCommandUseErrorActionPreference = $false
        $output = & $pwsh -NoProfile -File $runner -Fixture $fixture -EntryPoint $EntryPoint 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
    }
}
