#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/build/AegiNext.Build.psm1') -Force
    function New-SdlSdkFixture
    {
        param([string] $Root)
        foreach ($name in @('include/SDL3/SDL_version.h', 'include/SDL3/SDL_audio.h', 'lib/cmake/SDL3/SDL3Config.cmake',
            'lib/libSDL3.dll.a', 'bin/SDL3.dll', 'lib/libSDL3.dylib'))
        {
            $path = Join-Path $Root $name
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            Set-Content -LiteralPath $path -Value 'fixture'
        }
        Set-Content -LiteralPath (Join-Path $Root 'include/SDL3/SDL_version.h') -Value "#define SDL_MAJOR_VERSION 3`n#define SDL_MINOR_VERSION 4`n#define SDL_MICRO_VERSION 16"
    }
}

Describe 'SDL SDK selection and validation' {
    BeforeEach {
        $originalSdl = $env:SDL3_DIR
        $env:SDL3_DIR = ''
        $selected = Join-Path $TestDrive 'SDL SDK [selected]'
        New-SdlSdkFixture $selected
    }
    AfterEach {
        $env:SDL3_DIR = $originalSdl
    }

    It 'accepts complete selected SDKs on <Platform> without consulting a manager' -TestCases @(
        @{ Platform = 'Windows' }
        @{ Platform = 'MacOS' }
    ) {
        param($Platform)
        $env:SDL3_DIR = Join-Path $TestDrive 'wrong environment root'
        InModuleScope AegiNext.Build -Parameters @{ Root = $selected; Platform = $Platform } {
            param($Root, $Platform)
            Mock Invoke-AegiNextCommand { throw 'Explicit SDK selection must not query a package manager.' }
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Root -HostInfo ([pscustomobject]@{ Platform = $Platform }) -SdlRoot $Root -ManagerCommand 'manager'
            $result.Check.Status | Should -Be 'Ready'
            $result.Root | Should -Be $Root
        }
    }

    It 'rejects missing Windows runtime and incompatible headers instead of installing over an existing SDK' {
        Remove-Item -LiteralPath (Join-Path $selected 'bin/SDL3.dll')
        InModuleScope AegiNext.Build -Parameters @{ Root = $selected } {
            param($Root)
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Root -HostInfo ([pscustomobject]@{ Platform = 'Windows' }) -SdlRoot $Root
            $result.Check.Status | Should -Be 'Invalid'
            $result.Root | Should -BeNullOrEmpty
        }
        New-SdlSdkFixture $selected
        Set-Content -LiteralPath (Join-Path $selected 'include/SDL3/SDL_version.h') -Value "#define SDL_MAJOR_VERSION 3`n#define SDL_MINOR_VERSION 4`n#define SDL_MICRO_VERSION 17"
        InModuleScope AegiNext.Build -Parameters @{ Root = $selected } {
            param($Root)
            $result = Get-AegiNextSdlEnvironment -HostInfo ([pscustomobject]@{ Platform = 'MacOS' }) -SdlRoot $Root
            $result.Check.Status | Should -Be 'Invalid'
            $result.Root | Should -BeNullOrEmpty
        }
    }

    It 'reports a missing SDK for the project installer' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Repository -HostInfo ([pscustomobject]@{ Platform = 'Windows' })
            $result.Check.Status | Should -Be 'Missing'
            $result.Check.Package | Should -Be 'sdl3'
            $result.Check.Manager | Should -Be 'project'
        }
    }

    It 'selects the locked project SDK ahead of package-manager environment variables' {
        $project = Join-Path $TestDrive 'project SDK priority'
        $root = Join-Path $project '.dependencies/win-x64/sdl3/3.4.16'
        New-SdlSdkFixture $root
        $env:SDL3_DIR = Join-Path $TestDrive 'incompatible system SDK'
        InModuleScope AegiNext.Build -Parameters @{ Repository = $project; Root = $root } {
            param($Repository, $Root)
            Mock Invoke-AegiNextCommand { throw 'A project SDK must not query the package manager.' }
            $result = Get-AegiNextSdlEnvironment -RepositoryRoot $Repository -HostInfo ([pscustomobject]@{ Platform = 'Windows' }) -ManagerCommand 'manager'
            $result.Check.Status | Should -Be 'Ready'
            $result.Root | Should -Be $Root
        }
    }
}

Describe 'Independent audio and export native plans' {
    It 'keeps <Target> separate and stages only selected SDK runtimes on Windows' -TestCases @(
        @{ Target = 'Audio' }
        @{ Target = 'Export' }
    ) {
        param($Target)
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        $prefixes = @{ ffmpeg = 'C:/FFmpeg SDK'; sdl3 = 'C:/SDL SDK'; cc = 'gcc'; cxx = 'g++'; cmake = 'cmake'; ninja = 'ninja'; ctest = 'ctest' }
        $planningRepository = Join-Path $TestDrive 'native planning with spaces'
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $planningRepository -Target $Target -Configuration Release -HostInfo $hostInfo -NativePrefixes $prefixes -RunTests)
        $plan.Count | Should -Be 4
        $plan[1].Arguments | Should -Contain (Join-Path $planningRepository "native/$($Target.ToLowerInvariant())")
        $plan[1].Arguments | Should -Contain '-DAEGINEXT_FFMPEG_ROOT=C:/FFmpeg SDK'
        if ($Target -eq 'Audio')
        {
            $plan[0].Arguments | Should -Contain '-DAEGINEXT_SDL_ROOT=C:/SDL SDK'
            $plan[1].Arguments | Should -Contain '-DAEGINEXT_SDL_ROOT=C:/SDL SDK'
        }
        @($plan | Where-Object FilePath -eq 'dotnet').Count | Should -Be 0
    }

    It 'requires an explicit resolved SDL SDK for audio but not export' {
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $prefixes = @{ ffmpeg = '/sdk'; cc = 'cc'; cxx = 'c++'; cmake = 'cmake'; ninja = 'ninja' }
        $planningRepository = Join-Path $TestDrive 'native dependency planning'
        { Get-AegiNextBuildPlan -RepositoryRoot $planningRepository -Target Audio -HostInfo $hostInfo -NativePrefixes $prefixes } | Should -Throw '*sdl3*'
        @(Get-AegiNextBuildPlan -RepositoryRoot $planningRepository -Target Export -HostInfo $hostInfo -NativePrefixes $prefixes).Count | Should -Be 2
    }

    It 'pins the official Windows SDK source and hash without installing it' {
        $manifest = Get-Content -LiteralPath (Join-Path $repository 'scripts/build/scoop/aeginext-sdl3.json') -Raw | ConvertFrom-Json
        $manifest.version | Should -Be '3.4.16'
        $manifest.architecture.'64bit'.hash | Should -Be '9828bb735cf8a007bcf0ac5aa9f01f3fcb54b7ca67c932e775c905c5d5053a60'
        $manifest.architecture.'64bit'.extract_dir | Should -Be 'SDL3-3.4.16/x86_64-w64-mingw32'
    }
}
