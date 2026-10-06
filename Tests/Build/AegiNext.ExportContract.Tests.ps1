#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $targets = Join-Path $repository 'scripts/build/ValidateExportContract.targets'
    $header = Join-Path $repository 'native/export/include/aeginext_export.h'
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source

    function Invoke-ExportContractFixture
    {
        param([switch] $DesignTime)
        $arguments = @('msbuild', $fixtureProject, '-nologo', '-t:ValidateAegiNextExportContract')
        if ($DesignTime)
        {
            $arguments += '-p:DesignTimeBuild=true'
        }
        $output = & $dotnet @arguments 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    }
}

Describe 'Native export contract for <Rid>' -ForEach @(
    @{ Rid = 'osx-arm64'; LibraryName = 'libaeginext_export.dylib'; Configuration = 'Debug' }
    @{ Rid = 'win-x64'; LibraryName = 'aeginext_export.dll'; Configuration = 'Release' }
) {
    BeforeEach {
        $fixtureDirectory = Join-Path $TestDrive ([Guid]::NewGuid().ToString())
        [IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
        $library = Join-Path $fixtureDirectory $LibraryName
        $stamp = Join-Path $fixtureDirectory 'aeginext_export.contract.sha256'
        $fixtureProject = Join-Path $fixtureDirectory 'contract.proj'
        $escapedDirectory = [Security.SecurityElement]::Escape($fixtureDirectory)
        $escapedTargets = [Security.SecurityElement]::Escape($targets)
        [IO.File]::WriteAllText($fixtureProject, @"
<Project>
  <PropertyGroup>
    <AegiNextNativeRuntimeIdentifier>$Rid</AegiNextNativeRuntimeIdentifier>
    <AegiNextNativeDirectory>$escapedDirectory</AegiNextNativeDirectory>
    <Configuration>$Configuration</Configuration>
  </PropertyGroup>
  <Import Project="$escapedTargets" />
</Project>
"@)
    }

    It 'allows managed-only compilation when the native module has not been built' {
        (Invoke-ExportContractFixture).ExitCode | Should -Be 0
    }

    It 'rejects an old library with no contract stamp before copying it' {
        [IO.File]::WriteAllBytes($library, [byte[]]@())
        $result = Invoke-ExportContractFixture
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -BeLike '*ANX1001*'
        $result.Output | Should -BeLike "*-Configuration $Configuration -RuntimeIdentifier $Rid*"
    }

    It 'rejects a library built against a different interface' {
        [IO.File]::WriteAllBytes($library, [byte[]]@())
        [IO.File]::WriteAllText($stamp, '0' * 64)
        $result = Invoke-ExportContractFixture
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -BeLike '*ANX1002*outdated ABI contract*'
    }

    It 'accepts the matching CMake lowercase SHA256 without requiring a host DLL load' {
        [IO.File]::WriteAllBytes($library, [byte[]]@())
        [IO.File]::WriteAllText($stamp, (Get-FileHash -LiteralPath $header -Algorithm SHA256).Hash.ToLowerInvariant())
        (Invoke-ExportContractFixture).ExitCode | Should -Be 0
    }

    It 'rejects malformed multiline contract metadata' {
        [IO.File]::WriteAllBytes($library, [byte[]]@())
        $hash = (Get-FileHash -LiteralPath $header -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($stamp, "$hash`n$hash")
        $result = Invoke-ExportContractFixture
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -BeLike '*ANX1002*'
    }

    It 'keeps IDE design-time project analysis available while native output is stale' {
        [IO.File]::WriteAllBytes($library, [byte[]]@())
        (Invoke-ExportContractFixture -DesignTime).ExitCode | Should -Be 0
    }
}
