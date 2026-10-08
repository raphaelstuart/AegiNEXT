#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $sourceRoot 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Locked dependency manifest' {
    It 'matches the media and rendering version contracts and locks every source' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $sourceRoot } {
            param($Repository)
            $manifest = Get-AegiNextDependencyManifest
            $ffmpeg = Get-Content (Join-Path $Repository 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Raw | ConvertFrom-Json
            $render = Get-Content (Join-Path $Repository 'native/dependencies.json') -Raw | ConvertFrom-Json
            $sdl = Get-Content (Join-Path $Repository 'scripts/build/scoop/aeginext-sdl3.json') -Raw | ConvertFrom-Json
            $manifest.packages.ffmpeg.version | Should -Be $ffmpeg.version
            $manifest.packages.sdl3.version | Should -Be $sdl.version
            $manifest.packages.libplacebo.version | Should -Be $render.directDependencies.libplacebo.version
            $manifest.packages['molten-vk'].version | Should -Be $render.directDependencies.MoltenVK.version
            $manifest.packages['vulkan-headers'].version | Should -Be $render.directDependencies.'Vulkan-Headers'.version
            foreach ($source in $manifest.sources.Values)
            {
                $source.url | Should -Match '^https://'
                $source.sha256 | Should -Match '^[a-f0-9]{64}$'
            }
        }
    }

    It 'resolves macOS dependencies first while Windows uses complete binary SDKs' {
        InModuleScope AegiNext.Build {
            $manifest = Get-AegiNextDependencyManifest
            $order = @(Get-AegiNextSdkInstallOrder $manifest @('ffmpeg', 'libplacebo', 'molten-vk', 'sdl3') 'MacOS')
            [array]::IndexOf($order, 'x264') | Should -BeLessThan ([array]::IndexOf($order, 'ffmpeg'))
            [array]::IndexOf($order, 'shaderc') | Should -BeLessThan ([array]::IndexOf($order, 'libplacebo'))
            [array]::IndexOf($order, 'vulkan-headers') | Should -BeLessThan ([array]::IndexOf($order, 'vulkan-loader'))
            @($order | Select-Object -Unique).Count | Should -Be $order.Count
            @(Get-AegiNextSdkInstallOrder $manifest @('ffmpeg', 'sdl3') 'Windows') | Should -Be @('ffmpeg', 'sdl3')
            { Get-AegiNextSdkInstallOrder $manifest @('libplacebo') 'Windows' } | Should -Throw '*does not support Windows*'
            $manifest.packages.ffmpeg.windows.source | Should -Be 'ffmpeg-windows'
            $manifest.packages.sdl3.windows.source | Should -Be 'sdl3-windows'
        }
    }

    It 'rejects dependency cycles and fingerprints dependency source changes deterministically' {
        InModuleScope AegiNext.Build {
            $manifest = Get-AegiNextDependencyManifest
            $first = Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS'
            $source = $manifest.sources.x264
            $manifest.sources.x264 = @{ archive = $source.archive; sha256 = $source.sha256; url = $source.url }
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Be $first
            $manifest.sources.x264.sha256 = 'a' * 64
            Get-AegiNextSdkFingerprint $manifest 'ffmpeg' 'MacOS' | Should -Not -Be $first
            $manifest.packages.x264.dependencies = @('ffmpeg')
            { Get-AegiNextSdkInstallOrder $manifest @('ffmpeg') 'MacOS' } | Should -Throw '*cyclic*'
        }
    }
}

Describe 'Verified SDK downloads' {
    It 'reuses only checksum-matching archives and rejects bad downloads before extraction' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $content = [Text.Encoding]::UTF8.GetBytes('verified archive')
            $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($content)).ToLowerInvariant()
            $source = @{ url = 'https://example.invalid/sdk.tar.gz'; sha256 = $sha; archive = 'sdk.tar.gz' }
            $directory = Join-Path $Repository '.dependencies/downloads'
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            $path = Join-Path $directory "$sha-sdk.tar.gz"
            [IO.File]::WriteAllBytes($path, $content)
            Mock Invoke-WebRequest { throw 'A verified cache must not access the network.' }
            Get-AegiNextSdkArchive $source $Repository | Should -Be $path
            Should -Invoke Invoke-WebRequest -Times 0 -Exactly
            Set-Content -LiteralPath $path -Value 'corrupt archive'
            Mock Invoke-WebRequest { Set-Content -LiteralPath $OutFile -Value 'untrusted bytes' }
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*SHA256 mismatch*'
            Test-Path -LiteralPath $path | Should -BeFalse
            @(Get-ChildItem -LiteralPath $directory -File).Count | Should -Be 0
        }
    }

    It 'removes partial downloads on network failures and refuses unlocked URLs' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $source = @{ url = 'https://example.invalid/sdk.zip'; sha256 = 'a' * 64; archive = 'sdk.zip' }
            Mock Invoke-WebRequest {
                Set-Content -LiteralPath $OutFile -Value 'partial'
                throw 'network failure'
            }
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*network failure*'
            @(Get-ChildItem -LiteralPath (Join-Path $Repository '.dependencies/downloads') -File).Count | Should -Be 0
            $source.url = 'http://example.invalid/sdk.zip'
            { Get-AegiNextSdkArchive $source $Repository } | Should -Throw '*HTTPS*'
        }
    }
}

Describe 'Windows development SDK extraction' {
    It 'copies only the selected architecture and retains upstream licenses from the archive' {
        $archiveRoot = Join-Path $TestDrive 'archive/SDL3-3.4.16'
        foreach ($entry in @('x86_64-w64-mingw32/bin/SDL3.dll', 'x86_64-w64-mingw32/lib/libSDL3.dll.a', 'i686-w64-mingw32/bin/SDL3.dll', 'LICENSE.txt'))
        {
            $path = Join-Path $archiveRoot $entry
            [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
            Set-Content -LiteralPath $path -Value $entry
        }
        $archive = Join-Path $TestDrive 'SDL SDK [archive].zip'
        Compress-Archive -LiteralPath $archiveRoot -DestinationPath $archive
        InModuleScope AegiNext.Build -Parameters @{ Archive = $archive; Repository = $TestDrive } {
            param($Archive, $Repository)
            $work = Join-Path $Repository 'work [selected]'
            $stage = Join-Path $work 'sdk'
            [IO.Directory]::CreateDirectory($stage) | Out-Null
            $script:fixtureSdkArchivePath = $Archive
            Mock Get-AegiNextSdkArchive { return $script:fixtureSdkArchivePath }
            Invoke-AegiNextSdkRecipe $Repository ([pscustomobject]@{ Platform = 'Windows' }) (Get-AegiNextDependencyManifest) sdl3 $work $stage 2
            (Get-Content -LiteralPath (Join-Path $stage 'bin/SDL3.dll') -Raw).Trim() | Should -Be 'x86_64-w64-mingw32/bin/SDL3.dll'
            Test-Path -LiteralPath (Join-Path $stage 'lib/libSDL3.dll.a') | Should -BeTrue
            Test-Path -LiteralPath (Join-Path $stage 'i686-w64-mingw32') | Should -BeFalse
            @(Get-ChildItem -LiteralPath (Join-Path $stage 'share/licenses') -Filter 'LICENSE.txt' -File -Recurse).Count | Should -BeGreaterThan 0
        }
    }
}

Describe 'Project SDK installation transactions' {
    BeforeEach {
        $repository = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + ' project [sdk]')
    }

    It 'installs, verifies and reuses a complete SDK without rebuilding or installing tools twice' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'complete SDK' }
            Mock Test-AegiNextPreparedSdk {
                if (!(Test-Path -LiteralPath (Join-Path $Root 'runtime.dll')))
                {
                    throw 'SDK fixture is incomplete'
                }
            }
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 1 -Exactly
            Should -Invoke Install-AegiNextSdkTool -Times 1 -Exactly
            Set-Content -LiteralPath (Join-Path $root 'runtime.dll') -Value 'damaged'
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeFalse
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 2 -Exactly
        }
    }

    It 'restores the previous SDK when validation fails after promotion and retains failure logs' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'original' }
            Mock Test-AegiNextPreparedSdk {}
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Set-Content -LiteralPath (Join-Path $root 'runtime.dll') -Value 'previous bytes'
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'replacement' }
            Mock Test-AegiNextPreparedSdk { throw 'runtime verification failure' } -ParameterFilter { !$SkipRuntime }
            { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*runtime verification failure*'
            (Get-Content -LiteralPath (Join-Path $root 'runtime.dll') -Raw).Trim() | Should -Be 'previous bytes'
            Test-Path -LiteralPath "$root.previous" | Should -BeFalse
            @(Get-ChildItem -LiteralPath (Join-Path $Repository '.dependencies/.work') -Directory).Count | Should -Be 1
        }
    }

    It 'preserves unmanaged SDKs and serializes concurrent installations' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            [IO.Directory]::CreateDirectory($root) | Out-Null
            Set-Content -LiteralPath (Join-Path $root 'user-file') -Value 'keep'
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { throw 'Unmanaged SDKs must be preserved' }
            { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*unmanaged SDK*'
            Test-Path -LiteralPath (Join-Path $root 'user-file') | Should -BeTrue
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 0 -Exactly
            $lock = [IO.File]::Open((Join-Path $Repository '.dependencies/.install.lock'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try
            {
                { Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3') } | Should -Throw '*Another project SDK installation*'
            }
            finally
            {
                $lock.Dispose()
            }
        }
    }

    It 'recovers a managed SDK when an interrupted promotion left only its backup' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $repository } {
            param($Repository)
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
            Mock Install-AegiNextSdkTool {}
            Mock Invoke-AegiNextSdkRecipe { Set-Content -LiteralPath (Join-Path $Stage 'runtime.dll') -Value 'complete' }
            Mock Test-AegiNextPreparedSdk {}
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            $root = Get-AegiNextProjectSdkRoot $Repository $hostInfo 'sdl3'
            Move-Item -LiteralPath $root -Destination "$root.previous"
            Install-AegiNextProjectSdk $Repository $hostInfo @('sdl3')
            Test-AegiNextInstalledSdk $root (Get-AegiNextDependencyManifest) 'sdl3' $hostInfo | Should -BeTrue
            Test-Path -LiteralPath "$root.previous" | Should -BeFalse
            Should -Invoke Invoke-AegiNextSdkRecipe -Times 1 -Exactly
        }
    }
}

Describe 'Build dependency installation routing' {
    It 'prepares project SDKs even when system SDKs are ready, without replacing explicit roots' {
        InModuleScope AegiNext.Build -Parameters @{ Repository = $TestDrive } {
            param($Repository)
            $originalFfmpeg = $env:FFMPEG_DIR
            $originalSdl = $env:SDL3_DIR
            try
            {
                $env:FFMPEG_DIR = ''; $env:SDL3_DIR = ''
                $report = [pscustomobject]@{
                    HostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64' }
                    Checks = @((Get-AegiNextCheck 'FfmpegSdk' 'Ready' 'system FFmpeg'), (Get-AegiNextCheck 'SdlSdk' 'Invalid' 'wrong system SDL'))
                }
                Mock Install-AegiNextProjectSdk {}
                Mock Invoke-AegiNextCommand { throw 'System libraries must not be reinstalled' }
                Install-AegiNextDependency $report $Repository -Jobs 3
                Should -Invoke Install-AegiNextProjectSdk -Times 1 -Exactly -ParameterFilter { 'ffmpeg' -in $Names -and 'sdl3' -in $Names -and $Jobs -eq 3 }
                $env:FFMPEG_DIR = 'package-manager-ffmpeg'
                $env:SDL3_DIR = 'package-manager-sdl'
                Install-AegiNextDependency $report $Repository
                Should -Invoke Install-AegiNextProjectSdk -Times 2 -Exactly
                Install-AegiNextDependency $report $Repository -FfmpegRoot 'explicit-ffmpeg' -SdlRoot 'explicit-sdl'
                Should -Invoke Install-AegiNextProjectSdk -Times 2 -Exactly
            }
            finally
            {
                $env:FFMPEG_DIR = $originalFfmpeg; $env:SDL3_DIR = $originalSdl
            }
        }
    }

    It 'keeps the default Workbench restore, build and tests on the native SDK RID for <Platform>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64'; Rid = 'osx-arm64' }
        @{ Platform = 'MacOS'; Architecture = 'X64'; Rid = 'osx-x64' }
        @{ Platform = 'Windows'; Architecture = 'Arm64'; Rid = 'win-x64' }
    ) {
        param($Platform, $Architecture, $Rid)
        $prefixes = @{ ffmpeg = '/sdk/ffmpeg'; sdl3 = '/sdk/sdl'; cc = 'cc'; cxx = 'c++'; cmake = 'cmake'; ninja = 'ninja'; ctest = 'ctest' }
        $hostInfo = [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture; ProcessArchitecture = $Architecture }
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $TestDrive -HostInfo $hostInfo -Target Workbench -Configuration Debug -NativePrefixes $prefixes -RunTests -TestProjects Media)
        $restore = $plan | Where-Object Label -eq 'Restore managed'
        $build = $plan | Where-Object Label -eq 'Build managed'
        $test = $plan | Where-Object Label -eq 'Test Media'
        $restore.Arguments | Should -Contain '-r'
        $restore.Arguments | Should -Contain $Rid
        foreach ($step in @($restore, $build, $test))
        {
            $step.Arguments | Should -Contain "-p:AegiNextRuntimeIdentifier=$Rid"
        }
    }
}
