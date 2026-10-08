#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Project native SDK selection' {
    BeforeEach {
        $repository = Join-Path $TestDrive ([guid]::NewGuid().ToString())
        $manifestDirectory = Join-Path $repository 'src/AegiNext.Media/Probing'
        [IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Destination $manifestDirectory
        $originalSdl = $env:SDL3_DIR
        $env:SDL3_DIR = ''
    }
    AfterEach {
        $env:SDL3_DIR = $originalSdl
    }

    It 'isolates the locked SDK for <Platform> <Architecture>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; Rid = 'osx-arm64' }
        @{ Platform = 'MacOS'; Architecture = 'X64'; Rid = 'osx-x64' }
        @{ Platform = 'Windows'; Architecture = 'Arm64'; Rid = 'win-x64' }
    ) {
        param($Platform, $Architecture, $Rid)
        $root = Join-Path $repository ".dependencies/$Rid/ffmpeg/9.0.2"
        [IO.Directory]::CreateDirectory($root) | Out-Null
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository; Root = $root; Platform = $Platform; Architecture = $Architecture } {
            param($Repository, $Root, $Platform, $Architecture)
            $hostInfo = [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture }
            Find-AegiNextProjectSdk $Repository $hostInfo 'ffmpeg' | Should -Be $Root
            Find-AegiNextProjectSdk $Repository $hostInfo 'sdl3' | Should -BeNullOrEmpty
        }
    }

    It 'does not select a different installed project version' {
        [IO.Directory]::CreateDirectory((Join-Path $repository '.dependencies/win-x64/ffmpeg/8.0.0')) | Out-Null
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            Find-AegiNextProjectSdk $Repository ([pscustomobject]@{ Platform = 'Windows' }) 'ffmpeg' | Should -BeNullOrEmpty
        }
    }

    It 'rejects an incomplete project SDL SDK without falling back to the manager' {
        $root = Join-Path $repository '.dependencies/win-x64/sdl3/3.4.16'
        [IO.Directory]::CreateDirectory($root) | Out-Null
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            Mock Invoke-AegiNextCommand { throw 'Project SDK must not fall back to a system installation.' }
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Repository -HostInfo ([pscustomobject]@{ Platform = 'Windows' }) -ManagerCommand 'manager'
            $result.Check.Status | Should -Be 'Invalid'
            $result.Root | Should -BeNullOrEmpty
        }
    }

    It 'keeps an explicit SDL path ahead of project selection' {
        [IO.Directory]::CreateDirectory((Join-Path $repository '.dependencies/win-x64/sdl3/3.4.16')) | Out-Null
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            Mock Find-AegiNextProjectSdk { throw 'Explicit SDK must take precedence.' }
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Repository -HostInfo ([pscustomobject]@{ Platform = 'Windows' }) -SdlRoot (Join-Path $Repository 'explicit-missing')
            $result.Check.Status | Should -Be 'Invalid'
        }
    }
}
