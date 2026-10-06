#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Compiler changes reset only the matching standalone CMake cache' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository [cache switching]'
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $prefixes = @{ ffmpeg = '/selected ffmpeg'; sdl3 = '/selected sdl'; cc = '/selected/clang'; cxx = '/selected/clang++'; cmake = 'cmake'; ninja = 'ninja' }
    }

    It 'requests fresh <Target> configuration before CMake can discard explicit SDK arguments' -TestCases @(
        @{ Target = 'Decoder' }, @{ Target = 'Audio' }, @{ Target = 'Export' }
    ) {
        param($Target)
        $component = $Target.ToLowerInvariant()
        $directory = Join-Path $repository "artifacts/native/build-$component-osx-arm64-release"
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $cachePath = Join-Path $directory 'CMakeCache.txt'
        $cache = @"
CMAKE_HOME_DIRECTORY:INTERNAL=$(Join-Path $repository "native/$component")
CMAKE_CACHEFILE_DIR:INTERNAL=$directory
CMAKE_GENERATOR:INTERNAL=Ninja
CMAKE_C_COMPILER:FILEPATH=/old/clang
CMAKE_CXX_COMPILER:FILEPATH=/old/clang++
"@
        Set-Content -LiteralPath $cachePath -Value $cache
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target $Target -Configuration Release -HostInfo $hostInfo -NativePrefixes $prefixes)
        $plan[0].Arguments | Should -Contain '--fresh'
        $plan[0].Arguments | Should -Contain '-DAEGINEXT_FFMPEG_ROOT=/selected ffmpeg'
        $plan[0].Arguments | Should -Contain '-DCMAKE_C_COMPILER=/selected/clang'
        $plan[0].Arguments | Should -Contain '-DCMAKE_CXX_COMPILER=/selected/clang++'
        if ($Target -eq 'Audio')
        {
            $plan[0].Arguments | Should -Contain '-DAEGINEXT_SDL_ROOT=/selected sdl'
        }
        (Get-Content -LiteralPath $cachePath -Raw).Trim() | Should -Be $cache.Trim()
    }

    It 'preserves incremental configuration when both compiler paths and generator match' {
        $directory = Join-Path $repository 'artifacts/native/build-audio-osx-arm64-release'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Set-Content -LiteralPath (Join-Path $directory 'CMakeCache.txt') -Value @"
CMAKE_HOME_DIRECTORY:INTERNAL=$(Join-Path $repository 'native/audio')
CMAKE_CACHEFILE_DIR:INTERNAL=$directory
CMAKE_GENERATOR:INTERNAL=Ninja
CMAKE_C_COMPILER:FILEPATH=/selected/clang
CMAKE_CXX_COMPILER:STRING=/selected/clang++
"@
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Audio -Configuration Release -HostInfo $hostInfo -NativePrefixes $prefixes)
        $plan[0].Arguments | Should -Not -Contain '--fresh'
    }

    It 'detects a CXX-only compiler change' {
        $directory = Join-Path $repository 'artifacts/native/build-export-osx-arm64-release'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Set-Content -LiteralPath (Join-Path $directory 'CMakeCache.txt') -Value @"
CMAKE_HOME_DIRECTORY:INTERNAL=$(Join-Path $repository 'native/export')
CMAKE_CACHEFILE_DIR:INTERNAL=$directory
CMAKE_GENERATOR:INTERNAL=Ninja
CMAKE_C_COMPILER:FILEPATH=/selected/clang
CMAKE_CXX_COMPILER:FILEPATH=/other/clang++
"@
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Export -Configuration Release -HostInfo $hostInfo -NativePrefixes $prefixes)
        $plan[0].Arguments | Should -Contain '--fresh'
    }

    It 'refuses to reset an unrelated or unidentifiable cache' -TestCases @(
        @{ Source = '/another/project' }, @{ Source = '' }
    ) {
        param($Source)
        $directory = Join-Path $repository 'artifacts/native/build-decoder-osx-arm64-release'
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $marker = Join-Path $directory 'user-data.txt'
        Set-Content -LiteralPath $marker -Value 'preserve me'
        Set-Content -LiteralPath (Join-Path $directory 'CMakeCache.txt') -Value "CMAKE_HOME_DIRECTORY:INTERNAL=$Source`nCMAKE_CACHEFILE_DIR:INTERNAL=$directory"
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -NativePrefixes $prefixes } | Should -Throw '*not*reset automatically*'
        (Get-Content -LiteralPath $marker -Raw).Trim() | Should -Be 'preserve me'
    }
}
