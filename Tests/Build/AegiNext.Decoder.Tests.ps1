#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force

    function New-DecoderSdkFixture
    {
        param([string] $Root)
        foreach ($library in @(@{ Name = 'avutil'; Major = 61 }, @{ Name = 'avcodec'; Major = 63 }, @{ Name = 'avformat'; Major = 63 }, @{ Name = 'swscale'; Major = 10 }, @{ Name = 'swresample'; Major = 7 }))
        {
            $directory = Join-Path $Root "include/lib$($library.Name)"
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            $prefix = "LIB$($library.Name.ToUpperInvariant())"
            Set-Content -LiteralPath (Join-Path $directory 'version.h') -Value @"
#define $($prefix)_VERSION_MINOR 1
#define $($prefix)_VERSION_MICRO 102
"@
            Set-Content -LiteralPath (Join-Path $directory 'version_major.h') -Value "#define $($prefix)_VERSION_MAJOR $($library.Major)"
            $header = if ($library.Name -eq 'avutil') { 'frame.h' } else { "$($library.Name).h" }
            Set-Content -LiteralPath (Join-Path $directory $header) -Value ''
            [IO.Directory]::CreateDirectory((Join-Path $Root 'lib')) | Out-Null
            [IO.Directory]::CreateDirectory((Join-Path $Root 'bin')) | Out-Null
            foreach ($file in @("lib/lib$($library.Name).dll.a", "lib/lib$($library.Name).dylib", "bin/$($library.Name)-$($library.Major).dll"))
            {
                Set-Content -LiteralPath (Join-Path $Root $file) -Value "fixture $file"
            }
        }
        foreach ($name in @('ffmpeg', 'ffprobe'))
        {
            Set-Content -LiteralPath (Join-Path $Root "bin/$name") -Value ''
            Set-Content -LiteralPath (Join-Path $Root "bin/$name.exe") -Value ''
        }
    }

    function Get-DecoderToolFixtureVersion
    {
        param([string] $Name)
        @"
$Name version 9.0.2
libavutil 61.1.102 / 61.1.102
libavcodec 63.1.102 / 63.1.102
libavformat 63.1.102 / 63.1.102
libswscale 10.1.102 / 10.1.102
libswresample 7.1.102 / 7.1.102
"@
    }
}

Describe 'Decoder build targets retain independent platform and output boundaries' {
    BeforeEach {
        $repository = Join-Path $TestDrive 'repository with spaces [decoder plan]'
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        $prefixes = @{ ffmpeg = (Join-Path $TestDrive 'FFmpeg SDK [selected]'); cc = 'fixture-cc'; cxx = 'fixture-cxx'; cmake = 'fixture-cmake'; ninja = 'fixture-ninja'; ctest = 'fixture-ctest' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { throw 'Planning must be pure.' }
    }

    It 'plans only the decoder on <Platform> and isolates <Configuration> outputs' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; Rid = 'osx-arm64'; Configuration = 'Release' }
        @{ Platform = 'MacOS'; Architecture = 'X64'; Rid = 'osx-x64'; Configuration = 'Debug' }
        @{ Platform = 'Windows'; Architecture = 'X64'; Rid = 'win-x64'; Configuration = 'Release' }
        @{ Platform = 'Windows'; Architecture = 'X64'; Rid = 'win-x64'; Configuration = 'Debug' }
    ) {
        param($Platform, $Architecture, $Rid, $Configuration)
        $hostInfo.Platform = $Platform
        $hostInfo.Architecture = $Architecture
        $hostInfo.ProcessArchitecture = $Architecture
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -Configuration $Configuration -NativePrefixes $prefixes -RunTests -Jobs 3)
        $configure = $plan | Where-Object Label -eq 'Configure decoder'
        $buildDirectory = Join-Path $repository "artifacts/native/build-decoder-$Rid-$($Configuration.ToLowerInvariant())"
        $configure.Arguments | Should -Contain (Join-Path $repository 'native/decoder')
        $configure.Arguments | Should -Contain $buildDirectory
        $configure.Arguments | Should -Contain "-DAEGINEXT_FFMPEG_ROOT=$($prefixes.ffmpeg)"
        $configure.Arguments | Should -Contain "-DAEGINEXT_NATIVE_OUTPUT_DIR=$(Join-Path $repository "artifacts/native/$Rid/$Configuration")"
        $configure.Arguments | Should -Contain '-DCMAKE_CXX_COMPILER=fixture-cxx'
        $configure.Arguments | Should -Contain '-DCMAKE_MAKE_PROGRAM=fixture-ninja'
        ($plan | Where-Object Label -eq 'Build decoder').Arguments | Should -Contain '3'
        $plan[-1].Label | Should -Be 'Test decoder'
        $plan[-1].Arguments | Should -Contain $buildDirectory
        @($plan | Where-Object FilePath -eq 'dotnet').Count | Should -Be 0
        @($plan | Where-Object { $_.Arguments -match 'MOLTENVK|libplacebo' }).Count | Should -Be 0
        if ($Platform -eq 'Windows')
        {
            $plan.Count | Should -Be 4
            $plan[0].Label | Should -Be 'Stage decoder development runtime'
            $plan[0].Arguments | Should -Contain (Join-Path $repository 'scripts/build/copy-decoder-runtime.cmake')
            @($configure.Arguments | Where-Object { $_ -like '*OSX*' }).Count | Should -Be 0
        }
        else
        {
            $plan.Count | Should -Be 3
            $configure.Arguments | Should -Contain '-DCMAKE_OSX_DEPLOYMENT_TARGET=14.0'
        }
        Test-Path -LiteralPath $repository | Should -BeFalse
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'rejects deferred Linux decoder builds before planning work' {
        $hostInfo.Platform = 'Linux'
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -NativePrefixes $prefixes } | Should -Throw '*Linux decoder*'
    }

    It 'requires resolved dependencies instead of silently selecting PATH packages' {
        $prefixes.Remove('ffmpeg')
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -NativePrefixes $prefixes } | Should -Throw '*ffmpeg*'
    }

    It 'requires CTest only when tests were requested' {
        $prefixes.Remove('ctest')
        @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -NativePrefixes $prefixes).Count | Should -Be 2
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -NativePrefixes $prefixes -RunTests } | Should -Throw '*ctest*'
    }
}

Describe 'Decoder environment checks verify the selected development SDK' {
    BeforeEach {
        $script:savedFfmpegDir = $env:FFMPEG_DIR
        $env:FFMPEG_DIR = $null
        $repository = Join-Path $TestDrive 'repository with spaces [decoder environment]'
        $manifestDirectory = Join-Path $repository 'src/AegiNext.Media/Probing'
        $nativeDirectory = Join-Path $repository 'native/decoder'
        [IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($nativeDirectory) | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Destination $manifestDirectory
        Set-Content -LiteralPath (Join-Path $nativeDirectory 'CMakeLists.txt') -Value 'cmake_minimum_required(VERSION 3.25)'
        $script:decoderSdk = Join-Path $TestDrive 'FFmpeg SDK with spaces [selected]'
        New-DecoderSdkFixture $script:decoderSdk
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        $script:compilerMachine = 'x86_64-w64-mingw32'
        $script:decoderToolOutput = @{
            ffmpeg = Get-DecoderToolFixtureVersion 'ffmpeg'
            ffprobe = Get-DecoderToolFixtureVersion 'ffprobe'
        }
        $script:decoderAv1Help = 'Decoder libdav1d [dav1d AV1 decoder by VideoLAN]:'
        $script:decoderAv1ExitCode = 0
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build {
            if ($Name -in @('scoop', 'brew', 'cmake', 'ninja', 'ctest', 'gcc', 'g++')) { return "fixture-$Name" }
            return $null
        }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            if ($FilePath -in @('fixture-scoop', 'fixture-brew')) { return [pscustomobject]@{ ExitCode = 0; Output = $script:decoderSdk } }
            if ($FilePath -eq 'fixture-cmake') { return [pscustomobject]@{ ExitCode = 0; Output = 'cmake version 3.31.6' } }
            if ($FilePath -eq 'fixture-ninja') { return [pscustomobject]@{ ExitCode = 0; Output = '1.13.0' } }
            if ($FilePath -in @('fixture-gcc', 'fixture-g++')) { return [pscustomobject]@{ ExitCode = 0; Output = $script:compilerMachine } }
            if ('decoder=libdav1d' -in $Arguments) { return [pscustomobject]@{ ExitCode = $script:decoderAv1ExitCode; Output = $script:decoderAv1Help } }
            $name = [IO.Path]::GetFileNameWithoutExtension($FilePath)
            if ($script:decoderToolOutput.ContainsKey($name)) { return [pscustomobject]@{ ExitCode = 0; Output = $script:decoderToolOutput[$name] } }
            throw "Unexpected environment command: $FilePath"
        }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { throw 'Installation was not requested.' }
    }

    AfterEach {
        $env:FFMPEG_DIR = $script:savedFfmpegDir
    }

    It 'checks shared SDK files and executes its own tools without requiring dotnet or HDR dependencies' {
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -RunTests -WithMediaTools
        $report.Ready | Should -BeTrue
        $report.NativePrefixes.ffmpeg | Should -Be $script:decoderSdk
        @($report.Checks | Where-Object Id -eq 'DotNetSdk').Count | Should -Be 0
        Should -Invoke Find-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $Name -in @('dotnet', 'ffmpeg', 'ffprobe', 'pkg-config') }
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 2 -Exactly -ParameterFilter { $FilePath.StartsWith((Join-Path $script:decoderSdk 'bin')) -and $Arguments[0] -eq '-version' }
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            $FilePath -eq (Join-Path $script:decoderSdk 'bin/ffmpeg.exe') -and 'decoder=libdav1d' -in $Arguments
        }
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Test-Path -LiteralPath (Join-Path $repository 'artifacts') | Should -BeFalse
    }

    It 'rejects hardware-only AV1 registration and failed software decoder inspection' -TestCases @(
        @{ Help = 'Decoder av1 [Alliance for Open Media AV1]:'; ExitCode = 0 }
        @{ Help = "Unknown decoder 'libdav1d'."; ExitCode = 0 }
        @{ Help = 'Decoder libdav1d [dav1d AV1 decoder by VideoLAN]:'; ExitCode = 1 }
    ) {
        param($Help, $ExitCode)
        $script:decoderAv1Help = $Help
        $script:decoderAv1ExitCode = $ExitCode
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Detail | Should -BeLike '*software AV1 decoder libdav1d*'
    }

    It 'reports software AV1 inspection exceptions as an invalid selected SDK' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { throw 'Cannot load decoder runtime' } -ParameterFilter {
            'decoder=libdav1d' -in $Arguments
        }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Detail | Should -BeLike '*Cannot inspect software AV1 decoder libdav1d*Cannot load decoder runtime*'
    }

    It 'prioritizes explicit root over FFMPEG_DIR and package manager discovery' {
        $env:FFMPEG_DIR = Join-Path $TestDrive 'stale SDK'
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -FfmpegRoot $script:decoderSdk
        $report.Ready | Should -BeTrue
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $FilePath -eq 'fixture-scoop' }
    }

    It 'uses FFMPEG_DIR without requiring a package manager when all tools are available' {
        $env:FFMPEG_DIR = $script:decoderSdk
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -eq 'scoop' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeTrue
        ($report.Checks | Where-Object Id -eq 'PackageManager').Status | Should -Be 'Optional'
    }

    It 'does not replace an invalid explicitly selected SDK' {
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -FfmpegRoot (Join-Path $TestDrive 'missing explicit SDK')
        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Status | Should -Be 'Invalid'
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Package | Should -BeNullOrEmpty
        Should -Invoke Invoke-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $FilePath -eq 'fixture-scoop' }
    }

    It 'maps a missing Windows SDK to the locked project shared SDK' {
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 1; Output = '' } } -ParameterFilter { $FilePath -eq 'fixture-scoop' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $check = $report.Checks | Where-Object Id -eq 'FfmpegSdk'
        $check.Status | Should -Be 'Missing'
        $check.Package | Should -Be 'ffmpeg'
        $check.Manager | Should -Be 'project'
    }

    It 'rejects an SDK missing <RelativePath>' -TestCases @(
        @{ RelativePath = 'include/libavcodec/version.h' }
        @{ RelativePath = 'include/libavformat/avformat.h' }
        @{ RelativePath = 'lib/libavcodec.dll.a' }
        @{ RelativePath = 'bin/avutil-61.dll' }
        @{ RelativePath = 'include/libswscale/swscale.h' }
        @{ RelativePath = 'lib/libswscale.dll.a' }
        @{ RelativePath = 'bin/swscale-10.dll' }
        @{ RelativePath = 'bin/ffprobe.exe' }
    ) {
        param($RelativePath)
        Remove-Item -LiteralPath (Join-Path $script:decoderSdk $RelativePath)
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Status | Should -Be 'Invalid'
    }

    It 'accepts Windows import libraries with the alternate .lib naming' {
        foreach ($name in @('avutil', 'avcodec', 'avformat', 'swscale'))
        {
            Move-Item -LiteralPath (Join-Path $script:decoderSdk "lib/lib$name.dll.a") -Destination (Join-Path $script:decoderSdk "lib/$name.lib")
        }
        (Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo).Ready | Should -BeTrue
    }

    It 'rejects headers differing from the shared version manifest' {
        Set-Content -LiteralPath (Join-Path $script:decoderSdk 'include/libavutil/version_major.h') -Value '#define LIBAVUTIL_VERSION_MAJOR 60'
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Detail | Should -BeLike '*60.1.102*61.1.102*'
        $report.Ready | Should -BeFalse
    }

    It 'rejects a runtime mismatch even when all SDK files exist' {
        $script:decoderToolOutput.ffprobe = $script:decoderToolOutput.ffprobe.Replace('63.1.102 / 63.1.102', '63.1.102 / 63.2.102')
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        ($report.Checks | Where-Object Id -eq 'FfmpegSdk').Detail | Should -BeLike '*runtime 63.2.102*'
    }

    It 'rejects a wrong compiler target instead of accepting any gcc on PATH' {
        $script:compilerMachine = 'x86_64-pc-msys'
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        @($report.Checks | Where-Object { $_.Id -in @('gcc', 'g++') -and $_.Status -eq 'Invalid' }).Count | Should -Be 2
    }

    It 'reports missing Windows build tools with their Scoop packages' {
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -in @('cmake', 'ninja', 'gcc', 'g++') }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 1; Output = '' } } -ParameterFilter { $FilePath -eq 'fixture-scoop' -and $Arguments[1] -ne 'ffmpeg-shared' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        @($report.Checks | Where-Object Status -eq 'Missing' | Select-Object -ExpandProperty Package -Unique | Sort-Object) | Should -Be @('main/cmake', 'main/mingw', 'main/ninja')
    }

    It 'checks macOS SDK tools and selected dylibs without requiring Homebrew for an explicit root' {
        $hostInfo.Platform = 'MacOS'
        $hostInfo.Architecture = 'Arm64'
        $hostInfo.ProcessArchitecture = 'Arm64'
        $script:appleFixture = Join-Path $TestDrive 'Apple developer tools [fixture]'
        [IO.Directory]::CreateDirectory((Join-Path $script:appleFixture 'sdk')) | Out-Null
        foreach ($name in @('clang', 'clang++'))
        {
            Set-Content -LiteralPath (Join-Path $script:appleFixture $name) -Value ''
        }
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return $null } -ParameterFilter { $Name -eq 'brew' }
        Mock Find-AegiNextCommand -ModuleName AegiNext.Build { return 'fixture-xcrun' } -ParameterFilter { $Name -eq 'xcrun' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '14.5' } } -ParameterFilter { $FilePath -eq '/usr/bin/sw_vers' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            $path = if ($Arguments[0] -eq '--find') { Join-Path $script:appleFixture $Arguments[1] } else { Join-Path $script:appleFixture 'sdk' }
            [pscustomobject]@{ ExitCode = 0; Output = $path }
        } -ParameterFilter { $FilePath -eq 'fixture-xcrun' }

        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo -FfmpegRoot $script:decoderSdk -RunTests

        $report.Ready | Should -BeTrue
        $report.NativePrefixes.cc | Should -Be (Join-Path $script:appleFixture 'clang')
        $report.NativePrefixes.cxx | Should -Be (Join-Path $script:appleFixture 'clang++')
        ($report.Checks | Where-Object Id -eq 'PackageManager').Status | Should -Be 'Optional'
        Should -Invoke Find-AegiNextCommand -ModuleName AegiNext.Build -Times 0 -Exactly -ParameterFilter { $Name -in @('gcc', 'g++', 'dotnet', 'pkg-config') }
    }

    It 'reports an absent Apple SDK without suggesting a Homebrew replacement' {
        $hostInfo.Platform = 'MacOS'
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '14.5' } } -ParameterFilter { $FilePath -eq '/usr/bin/sw_vers' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeFalse
        $check = $report.Checks | Where-Object Id -eq 'AppleToolchain'
        $check.Status | Should -Be 'Missing'
        $check.Package | Should -BeNullOrEmpty
    }

    It 'maps missing macOS development packages to the project installer' {
        $hostInfo.Platform = 'MacOS'
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 0; Output = '14.5' } } -ParameterFilter { $FilePath -eq '/usr/bin/sw_vers' }
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build { [pscustomobject]@{ ExitCode = 1; Output = '' } } -ParameterFilter { $FilePath -eq 'fixture-brew' }
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $check = $report.Checks | Where-Object Id -eq 'FfmpegSdk'
        $check.Status | Should -Be 'Missing'
        $check.Package | Should -Be 'ffmpeg'
        $check.Manager | Should -Be 'project'
    }

    It 'allows Windows arm64 hosts with verified x64 compilers and runtime packages' {
        $hostInfo.Architecture = 'Arm64'
        $hostInfo.ProcessArchitecture = 'Arm64'
        $report = Get-AegiNextEnvironment -RepositoryRoot $repository -Target Decoder -HostInfo $hostInfo
        $report.Ready | Should -BeTrue
        ($report.Checks | Where-Object Id -eq 'Architecture').Status | Should -Be 'Ready'
    }

    It 'does not install or build in a successful Decoder environment-only invocation' {
        Mock Get-AegiNextHost -ModuleName AegiNext.Build { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' } }
        Mock Get-AegiNextBuildPlan -ModuleName AegiNext.Build { throw 'CheckEnvironment must not build.' }
        Invoke-AegiNextBuild -RepositoryRoot $repository -Target Decoder -CheckEnvironment -FfmpegRoot $script:decoderSdk | Should -Be 0
        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 0 -Exactly
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 0 -Exactly
    }

    It 'rejects FfmpegRoot on unrelated targets' {
        { Invoke-AegiNextBuild -RepositoryRoot $repository -Target Managed -FfmpegRoot $script:decoderSdk } | Should -Throw '*only to -Target Decoder*'
    }

    It 'rechecks the selected SDK after an explicitly requested installation' {
        Mock Get-AegiNextHost -ModuleName AegiNext.Build { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' } }
        $script:decoderInstalled = $false
        Mock Invoke-AegiNextCommand -ModuleName AegiNext.Build {
            if ($script:decoderInstalled) { return [pscustomobject]@{ ExitCode = 0; Output = $script:decoderSdk } }
            [pscustomobject]@{ ExitCode = 1; Output = '' }
        } -ParameterFilter { $FilePath -eq 'fixture-scoop' }
        Mock Install-AegiNextDependency -ModuleName AegiNext.Build { $script:decoderInstalled = $true }
        Mock Get-AegiNextBuildPlan -ModuleName AegiNext.Build { return @() }

        Invoke-AegiNextBuild -RepositoryRoot $repository -Target Decoder -InstallDependencies | Should -Be 0

        Should -Invoke Install-AegiNextDependency -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter {
            @($Report.Checks | Where-Object { $_.Id -eq 'FfmpegSdk' -and $_.Status -eq 'Missing' -and $_.Package -eq 'ffmpeg' -and $_.Manager -eq 'project' }).Count -eq 1
        }
        Should -Invoke Get-AegiNextBuildPlan -ModuleName AegiNext.Build -Times 1 -Exactly -ParameterFilter { $NativePrefixes.ffmpeg -eq $script:decoderSdk }
    }
}

Describe 'Decoder runtime staging includes media tools' {
    It 'copies tools with their DLLs from a literal SDK path with spaces' {
        $sdk = Join-Path $TestDrive 'selected SDK [runtime]'
        $output = Join-Path $TestDrive 'output [Release]'
        New-DecoderSdkFixture $sdk
        $result = Invoke-AegiNextCommand -FilePath 'cmake' -WorkingDirectory $sourceRoot -Arguments @(
            "-DAEGINEXT_FFMPEG_ROOT=$sdk", "-DAEGINEXT_NATIVE_OUTPUT_DIR=$output", '-P', (Join-Path $sourceRoot 'scripts/build/copy-decoder-runtime.cmake'))
        $result.ExitCode | Should -Be 0 -Because $result.Output
        @(Get-ChildItem -LiteralPath $output -File | Select-Object -ExpandProperty Name | Sort-Object) | Should -Be @('avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll', 'swscale-10.dll')
        (Get-Content -LiteralPath (Join-Path $output 'avcodec-63.dll') -Raw) | Should -Be (Get-Content -LiteralPath (Join-Path $sdk 'bin/avcodec-63.dll') -Raw)
        @(Get-ChildItem -LiteralPath (Join-Path $output 'tools') -File | Select-Object -ExpandProperty Name | Sort-Object) | Should -Be @('avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'ffmpeg.exe', 'ffprobe.exe', 'swresample-7.dll', 'swscale-10.dll')
        foreach ($tool in @('ffmpeg', 'ffprobe'))
        {
            (Get-Content -LiteralPath (Join-Path $output "tools/$tool.exe") -Raw) | Should -Be (Get-Content -LiteralPath (Join-Path $sdk "bin/$tool.exe") -Raw)
        }
    }

    It 'rejects an SDK missing a required tool' {
        $sdk = Join-Path $TestDrive 'incomplete SDK'
        New-DecoderSdkFixture $sdk
        Remove-Item -LiteralPath (Join-Path $sdk 'bin/ffprobe.exe')
        $result = Invoke-AegiNextCommand -FilePath 'cmake' -WorkingDirectory $sourceRoot -Arguments @(
            "-DAEGINEXT_FFMPEG_ROOT=$sdk", "-DAEGINEXT_NATIVE_OUTPUT_DIR=$(Join-Path $TestDrive 'incomplete output')", '-P', (Join-Path $sourceRoot 'scripts/build/copy-decoder-runtime.cmake'))
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -BeLike '*ffprobe.exe*'
    }
}
