#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/build/AegiNext.Build.psm1') -Force
    Import-Module (Join-Path $repository 'scripts/publish/AegiNext.Publish.psm1') -Force
}

Describe 'Platform publishing boundaries' {
    It 'forwards streaming requests to the command runner and retains parser output' {
        InModuleScope AegiNext.Publish {
            Mock Invoke-AegiNextCommand { [pscustomobject]@{ ExitCode = 0; Output = 'fixture command output' } }

            Invoke-AegiNextPublishCommand -FilePath 'fixture-publisher' -Arguments @('publish') -WorkingDirectory '.' -StreamOutput |
                Should -Be 'fixture command output'
            Should -Invoke Invoke-AegiNextCommand -Times 1 -Exactly -ParameterFilter { $StreamOutput }
        }
    }

    It 'reports command failures with the captured diagnostics even when streaming is enabled' {
        InModuleScope AegiNext.Publish {
            Mock Invoke-AegiNextCommand { [pscustomobject]@{ ExitCode = 7; Output = 'fixture tool failure' } }

            { Invoke-AegiNextPublishCommand -FilePath 'fixture-publisher' -WorkingDirectory '.' -StreamOutput } |
                Should -Throw '*failed (7): fixture tool failure*'
        }
    }

    It 'evaluates the worker output identity used by the export launcher' {
        $project = Join-Path $repository 'src/AegiNext.ExportWorker/AegiNext.ExportWorker.csproj'
        $output = & dotnet msbuild $project -nologo '-getProperty:AssemblyName'
        $LASTEXITCODE | Should -Be 0
        ($output | Out-String).Trim() | Should -Be 'aegn-exporter'
    }

    It 'targets Windows x64 when the VM host and PowerShell are arm64' {
        $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo | Should -Be 'win-x64'
        Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier win-x64 | Should -Be 'win-x64'
        { Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier osx-arm64 } | Should -Throw '*does not match*'
        $prefixes = @{ ffmpeg = '/ffmpeg'; sdl3 = '/sdl'; cc = 'gcc'; cxx = 'g++'; cmake = 'cmake'; ninja = 'ninja'; ctest = 'ctest' }
        $planningRepository = Join-Path $TestDrive 'publishing plan with spaces'
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $planningRepository -Target Workbench -HostInfo $hostInfo -NativePrefixes $prefixes -RuntimeIdentifier win-x64 -RunTests -TestProjects Media)
        ($plan | Where-Object Label -eq 'Build managed').Arguments | Should -Contain '-p:AegiNextRuntimeIdentifier=win-x64'
        ($plan | Where-Object Label -eq 'Build managed').Arguments | Should -Not -Contain '-r'
        ($plan | Where-Object Label -eq 'Restore managed').Arguments | Should -Contain 'win-x64'
        ($plan | Where-Object Label -eq 'Restore managed').Arguments | Should -Contain '-p:AegiNextRuntimeIdentifier=win-x64'
        ($plan | Where-Object Label -eq 'Configure decoder').Arguments | Should -Contain "-DAEGINEXT_NATIVE_OUTPUT_DIR=$(Join-Path $planningRepository 'artifacts/native/win-x64/Release')"
        ($plan | Where-Object Label -eq 'Test Media').Arguments | Should -Contain 'win-x64'
        ($plan | Where-Object Label -eq 'Test Media').Arguments | Should -Contain '-p:AegiNextRuntimeIdentifier=win-x64'
        ($plan | Where-Object Label -eq 'Test Media').Arguments | Should -Contain '--no-restore'
        ($plan | Where-Object Label -eq 'Test Media').Arguments | Should -Not -Contain '--no-build'
    }

    It 'does not publish an x64 Mac native payload from an arm64 Mac' {
        $hostInfo = [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
        { Get-AegiNextRuntimeIdentifier -HostInfo $hostInfo -RuntimeIdentifier osx-x64 } | Should -Throw '*does not match*'
    }

    It 'allows an arm64 Windows SDK host to produce the explicit x64 managed target' {
        InModuleScope AegiNext.Build {
            Mock Invoke-AegiNextCommand { [pscustomobject]@{ ExitCode = 0; Output = 'win-arm64' } }
            $hostInfo = [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' }
            (Get-AegiNextSdkArchitectureCheck -RepositoryRoot '.' -Dotnet 'dotnet' -HostInfo $hostInfo).Status | Should -Be 'Ready'
        }
    }

    It 'publishes desktop and worker, including an installer when requested (<CreateInstaller>)' -TestCases @(
        @{ CreateInstaller = $false; ReleaseVersion = $null; ExpectedVersion = '0.1.0'; ExpectedAssemblyVersion = '0.1.0.0' }
        @{ CreateInstaller = $true; ReleaseVersion = '2.7.3'; ExpectedVersion = '2.7.3'; ExpectedAssemblyVersion = '2.7.3.0' }
    ) {
        param($CreateInstaller, $ReleaseVersion, $ExpectedVersion, $ExpectedAssemblyVersion)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = (Join-Path $TestDrive "windows publish $ExpectedVersion"); CreateInstaller = $CreateInstaller; ReleaseVersion = $ReleaseVersion; ExpectedVersion = $ExpectedVersion; ExpectedAssemblyVersion = $ExpectedAssemblyVersion } {
            param($Fixture, $CreateInstaller, $ReleaseVersion, $ExpectedVersion, $ExpectedAssemblyVersion)
            $root = Join-Path $Fixture 'publish command repository'
            $ffmpeg = Join-Path $root 'sdk/ffmpeg'
            $sdl = Join-Path $root 'sdk/sdl'
            $native = Join-Path $root 'artifacts/native/win-x64/Release'
            $assets = Join-Path $root 'src/AegiNext.Desktop/obj/win-x64'
            $compilerPrefix = Join-Path $root 'sdk/mingw'
            $compiler = Join-Path $compilerPrefix 'bin/g++.exe'
            foreach ($directory in @($root, "$ffmpeg/bin", $sdl, $native, $assets, "$compilerPrefix/bin", "$compilerPrefix/licenses/gcc"))
            {
                [IO.Directory]::CreateDirectory($directory) | Out-Null
            }
            Set-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Value '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>'
            Set-Content -LiteralPath (Join-Path $assets 'project.assets.json') -Value '{"libraries":{},"packageFolders":{}}'
            Set-Content -LiteralPath $compiler -Value 'validated compiler fixture'
            Set-Content -LiteralPath (Join-Path $compilerPrefix 'licenses/gcc/COPYING.RUNTIME') -Value 'original compiler runtime notice'
            foreach ($directory in @($ffmpeg, $sdl))
            {
                Set-Content -LiteralPath (Join-Path $directory 'LICENSE.txt') -Value 'fixture SDK notice'
            }
            foreach ($name in @('ffmpeg', 'ffprobe'))
            {
                Set-Content -LiteralPath (Join-Path $ffmpeg "bin/$name.exe") -Value 'fixture tool'
            }
            foreach ($name in @('decode', 'audio', 'export'))
            {
                Set-Content -LiteralPath (Join-Path $native "aeginext_$name.dll") -Value 'fixture native module'
            }
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' } }
            Mock Get-AegiNextEnvironment { [pscustomobject]@{ Ready = $true; NativePrefixes = @{ ffmpeg = $ffmpeg; sdl3 = $sdl; cxx = $compiler } } }
            Mock Get-AegiNextSourceIdentity { [pscustomobject]@{ GitSha = ('a' * 40); WorkingTreeDirty = $true; Status = 'Recorded' } }
            Mock Find-AegiNextCommand { 'fixture-dotnet' }
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments)
                if ($Arguments[0] -eq 'publish')
                {
                    $FilePath | Should -Be 'fixture-dotnet'
                    $name = if ($Arguments[1].EndsWith('AegiNext.Desktop.csproj')) { 'aegi-next' } else { 'aegn-exporter' }
                    Set-Content -LiteralPath (Join-Path $Arguments[3] "$name.runtimeconfig.json") -Value '{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"}]}}'
                    if ($name -eq 'aegi-next')
                    {
                        $languages = Join-Path $Arguments[3] 'i18n'
                        [IO.Directory]::CreateDirectory($languages) | Out-Null
                        foreach ($identifier in @('en-US', 'zh-CN'))
                        {
                            @{ LanguageName = $identifier; LanguageID = $identifier; Strings = @{ 'Workbench.Export' = 'Encode' } } |
                                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $languages "$identifier.json") -Encoding utf8NoBOM
                        }
                    }
                    return ''
                }
                if ($Arguments[0] -eq '-version')
                {
                    return "$([IO.Path]::GetFileNameWithoutExtension($FilePath)) version 9.0.2 fixture"
                }
                throw 'Unexpected publish command.'
            }
            Mock Copy-AegiNextWindowsDependencyClosure { @() }
            Mock Get-AegiNextNsisCompiler { [pscustomobject]@{ Path = 'fixture-makensis'; Version = '3.13' } }
            Mock New-AegiNextWindowsInstaller {
                param($PayloadDirectory, $PublishDirectory, $ProductVersion, $RuntimeIdentifier, $NsisCompiler)
                $NsisCompiler | Should -Be 'fixture-makensis'
                Test-Path -LiteralPath (Join-Path $PayloadDirectory 'media-runtime.json') | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $PayloadDirectory 'licenses') | Should -BeTrue
                Test-Path -LiteralPath (Join-Path $PublishDirectory 'package-manifest.json') | Should -BeFalse
                $installer = Join-Path $PublishDirectory "AegiNext-$ProductVersion-$RuntimeIdentifier-setup.exe"
                Set-Content -LiteralPath $installer -Value 'compiled installer fixture'
                return $installer
            }
            $versionArguments = if ($null -ne $ReleaseVersion) { @{ Version = $ReleaseVersion } } else { @{} }
            $assemblyVersionArgument = "-p:AssemblyVersion=$ExpectedAssemblyVersion"
            $fileVersionArgument = "-p:FileVersion=$ExpectedAssemblyVersion"
            Invoke-AegiNextPublish -RepositoryRoot $root -RuntimeIdentifier win-x64 -SkipBuild -CreateInstaller:$CreateInstaller -OutputDirectory (Join-Path $Fixture 'published command fixture') @versionArguments
            Should -Invoke Invoke-AegiNextPublishCommand -Times 2 -Exactly -ParameterFilter {
                $FilePath -eq 'fixture-dotnet' -and $Arguments[0] -eq 'publish' -and
                $Arguments -contains 'win-x64' -and $Arguments -contains '-p:AegiNextRuntimeIdentifier=win-x64' -and
                $Arguments -contains '-p:RestoreLockedMode=true' -and $Arguments -contains '--self-contained' -and
                $Arguments -contains "-p:Version=$ExpectedVersion" -and $Arguments -contains "-p:InformationalVersion=$ExpectedVersion" -and
                $Arguments -contains $assemblyVersionArgument -and $Arguments -contains $fileVersionArgument
            }
            foreach ($project in @('AegiNext.Desktop', 'AegiNext.ExportWorker'))
            {
                Should -Invoke Invoke-AegiNextPublishCommand -Times 1 -Exactly -ParameterFilter {
                    $Arguments[0] -eq 'publish' -and $Arguments[1] -eq (Join-Path $root "src/$project/$project.csproj")
                }
            }
            Should -Invoke Copy-AegiNextWindowsDependencyClosure -Times 1 -Exactly -ParameterFilter {
                $SearchDirectories -contains (Split-Path $compiler)
            }
            $manifest = Get-Content -LiteralPath (Join-Path $Fixture 'published command fixture/package-manifest.json') -Raw | ConvertFrom-Json
            $manifest.ProductVersion | Should -Be $ExpectedVersion
            $manifest.ProductVersion | Should -BeOfType string
            (Test-AegiNextPublishedPackage (Join-Path $Fixture 'published command fixture')).ProductVersion | Should -Be $ExpectedVersion
            $manifest.GitSha | Should -Be ('a' * 40)
            $manifest.WorkingTreeDirty | Should -BeTrue
            $manifest.ToolVersions.Count | Should -Be 2
            $manifest.ToolVersions[0].VersionLine | Should -Be 'ffmpeg version 9.0.2 fixture'
            $manifest.ToolVersions[1].VersionLine | Should -Be 'ffprobe version 9.0.2 fixture'
            $manifest.RuntimeFrameworks.Count | Should -Be 2
            $manifest.RuntimeFrameworks[0].IncludedFrameworks[0].Version | Should -Be '10.0.12'
            @($manifest.Licenses | Where-Object PackageRoot -eq $compilerPrefix).Count | Should -Be 1
            $manifest.OperatingSystemPolicy.ProductMinimumOSVerified | Should -BeFalse
            $manifest.OperatingSystemPolicy.RequiredRuntimePolicy.Source | Should -Be 'https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md'
            if ($CreateInstaller)
            {
                Should -Invoke Get-AegiNextNsisCompiler -Times 1 -Exactly
                Should -Invoke New-AegiNextWindowsInstaller -Times 1 -Exactly
                @($manifest.Files | Where-Object Path -eq "AegiNext-$ExpectedVersion-win-x64-setup.exe").Count | Should -Be 1
                $installer = Join-Path $Fixture "published command fixture/AegiNext-$ExpectedVersion-win-x64-setup.exe"
                Add-Content -LiteralPath $installer -Value 'tampered'
                { Test-AegiNextPublishedPackage (Split-Path $installer) } | Should -Throw '*hash mismatch*'
            }
            else
            {
                Should -Invoke Get-AegiNextNsisCompiler -Times 0 -Exactly
                Should -Invoke New-AegiNextWindowsInstaller -Times 0 -Exactly
                @($manifest.Files | Where-Object Path -like '*-setup.exe').Count | Should -Be 0
            }
        }
    }

    It 'rejects incomplete or oversized product versions before inspecting the environment (<InvalidVersion>)' -TestCases @(
        @{ InvalidVersion = '1.2' }
        @{ InvalidVersion = '1.2.3.4' }
        @{ InvalidVersion = '65536.0.0' }
        @{ InvalidVersion = '1.2.3.65536' }
    ) {
        param($InvalidVersion)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; InvalidVersion = $InvalidVersion } {
            param($Fixture, $InvalidVersion)
            $repositoryArgument = $Fixture
            $versionArgument = $InvalidVersion
            Mock Get-AegiNextHost { throw 'Environment must not be inspected.' }
            { Invoke-AegiNextPublish -RepositoryRoot $repositoryArgument -Version $versionArgument -SkipBuild } | Should -Throw '*Invalid product version*'
            Should -Invoke Get-AegiNextHost -Times 0 -Exactly
        }
    }

    It 'collects real notices and records exact source hashes' {
        $root = Join-Path $TestDrive 'sdk with spaces'
        [IO.Directory]::CreateDirectory($root) | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'LICENSE.txt') -Value 'Actual notice text.'
        $records = @(Copy-AegiNextLicenseFile -Roots @($root, $root) -Destination (Join-Path $TestDrive 'licenses'))
        $records.Count | Should -Be 1
        $records[0].Sha256 | Should -Be ((Get-FileHash -LiteralPath (Join-Path $root 'LICENSE.txt')).Hash.ToLowerInvariant())
        Get-Content -LiteralPath (Join-Path $TestDrive "licenses/$($records[0].File)") -Raw | Should -Match 'Actual notice text'
    }

    It 'verifies relocation and rejects a missing or modified packaged file' {
        $root = Join-Path $TestDrive "relocated package $([char]0x4e2d)$([char]0x6587)"
        [IO.Directory]::CreateDirectory($root) | Out-Null
        $tool = Join-Path $root 'tool.exe'
        Set-Content -LiteralPath $tool -Value 'packaged tool'
        $languages = Join-Path $root 'AegiNext/i18n'
        [IO.Directory]::CreateDirectory($languages) | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository 'src/AegiNext.Desktop/I18n/Languages/en-US.json'), (Join-Path $repository 'src/AegiNext.Desktop/I18n/Languages/zh-CN.json') -Destination $languages
        $files = @(foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse)
        {
            @{ Path = [IO.Path]::GetRelativePath($root, $file.FullName); Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant(); Bytes = $file.Length }
        })
        $manifest = @{ SchemaVersion = 1; ProductVersion = '0.1.0'; RuntimeIdentifier = 'win-x64'; SelfContained = $true; Files = $files }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'package-manifest.json')
        (Test-AegiNextPublishedPackage $root).HashesVerified | Should -BeTrue
        Add-Content -LiteralPath $tool -Value 'tampered'
        { Test-AegiNextPublishedPackage $root } | Should -Throw '*hash mismatch*'
        Remove-Item -LiteralPath $tool
        { Test-AegiNextPublishedPackage $root } | Should -Throw '*Missing package*'
    }
}

Describe 'macOS app icon and disk image publishing' {
    It 'installs the icon and writes a bundle that names it' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $root = Join-Path $Fixture 'app icon repository'
            $assets = Join-Path $root 'src/AegiNext.Desktop/Assets'
            [IO.Directory]::CreateDirectory($assets) | Out-Null
            $source = Join-Path $assets 'AppIcon.icns'
            [IO.File]::WriteAllBytes($source, [byte[]](105, 99, 110, 115, 0, 0, 0, 8))
            $contents = Join-Path $Fixture 'app with spaces/AegiNext.app/Contents'

            Set-AegiNextMacAppBundle -RepositoryRoot $root -ContentsDirectory $contents -ProductVersion '0.1.0' -MinimumOSVersion '14.0'

            $icon = Join-Path $contents 'Resources/AppIcon.icns'
            (Get-FileHash -LiteralPath $icon).Hash | Should -Be (Get-FileHash -LiteralPath $source).Hash
            $plist = [xml](Get-Content -LiteralPath (Join-Path $contents 'Info.plist') -Raw)
            $keys = @($plist.SelectNodes('/plist/dict/key') | ForEach-Object InnerText)
            $keys | Should -Contain 'CFBundleIconFile'
            $plist.SelectSingleNode('/plist/dict/key[text()="CFBundleIconFile"]/following-sibling::string[1]').InnerText | Should -Be 'AppIcon.icns'
            $plist.SelectSingleNode('/plist/dict/key[text()="CFBundleShortVersionString"]/following-sibling::string[1]').InnerText | Should -Be '0.1.0'
            $plist.SelectSingleNode('/plist/dict/key[text()="LSMinimumSystemVersion"]/following-sibling::string[1]').InnerText | Should -Be '14.0'
        }
    }

    It 'fails before writing a bundle when its icon is missing' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $contents = Join-Path $Fixture 'missing icon/AegiNext.app/Contents'
            { Set-AegiNextMacAppBundle -RepositoryRoot $Fixture -ContentsDirectory $contents -ProductVersion '0.1.0' -MinimumOSVersion '14.0' } | Should -Throw '*Missing macOS app icon*'
            Test-Path -LiteralPath $contents | Should -BeFalse
        }
    }

    It 'rejects DMG creation on Windows before environment checks or publishing' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $root = $Fixture
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'Windows'; Architecture = 'X64'; ProcessArchitecture = 'X64' } }
            Mock Get-AegiNextEnvironment { throw 'Environment must not be inspected.' }
            Mock Invoke-AegiNextPublishCommand { throw 'No command may run.' }
            { Invoke-AegiNextPublish -RepositoryRoot $root -RuntimeIdentifier win-x64 -CreateDmg } | Should -Throw '*CreateDmg is only supported on macOS*'
            Should -Invoke Get-AegiNextEnvironment -Times 0 -Exactly
            Should -Invoke Invoke-AegiNextPublishCommand -Times 0 -Exactly
        }
    }

    It 'stages the signed app outside the output, creates a compressed DMG and cleans up' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $publishRoot = Join-Path $Fixture 'disk image output'
            $app = Join-Path $publishRoot 'AegiNext.app'
            [IO.Directory]::CreateDirectory($app) | Out-Null
            $script:diskImageCommands = [Collections.Generic.List[object]]::new()
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments, $WorkingDirectory)
                $script:diskImageCommands.Add([pscustomobject]@{ FilePath = $FilePath; Arguments = $Arguments; WorkingDirectory = $WorkingDirectory })
                if ($FilePath -eq '/usr/bin/hdiutil' -and $Arguments[0] -eq 'create')
                {
                    $staging = $Arguments[2]
                    Test-Path -LiteralPath $staging -PathType Container | Should -BeTrue
                    $staging.StartsWith($publishRoot, [StringComparison]::Ordinal) | Should -BeFalse
                    Set-Content -LiteralPath $Arguments[-1] -Value 'compressed disk image fixture'
                }
                return ''
            }

            $image = New-AegiNextMacDiskImage -AppDirectory $app -PublishDirectory $publishRoot -ProductVersion '0.1.0' -RuntimeIdentifier osx-arm64

            $image | Should -Be (Join-Path $publishRoot 'AegiNext-0.1.0-osx-arm64.dmg')
            Test-Path -LiteralPath $image -PathType Leaf | Should -BeTrue
            $commands = $script:diskImageCommands.ToArray()
            $commands.Count | Should -Be 5
            $commands[0].FilePath | Should -Be '/usr/bin/ditto'
            $commands[0].Arguments | Should -Contain '--rsrc'
            $commands[0].Arguments | Should -Contain '--extattr'
            $commands[0].Arguments | Should -Contain '--acl'
            $stagedApp = $commands[0].Arguments[-1]
            $staging = Split-Path $stagedApp
            $commands[1].FilePath | Should -Be '/bin/ln'
            ($commands[1].Arguments -join '|') | Should -Be "-s|/Applications|$(Join-Path $staging 'Applications')"
            $commands[2].FilePath | Should -Be '/usr/bin/codesign'
            $commands[2].Arguments | Should -Contain '--verify'
            $commands[2].Arguments[-1] | Should -Be $stagedApp
            $commands[3].FilePath | Should -Be '/usr/bin/hdiutil'
            $commands[3].Arguments | Should -Contain 'UDZO'
            $commands[3].Arguments[2] | Should -Be $staging
            ($commands[4].Arguments -join '|') | Should -Be "verify|$image"
            Test-Path -LiteralPath $staging | Should -BeFalse
        }
    }

    It 'cleans the staging directory when <Failure> fails' -TestCases @(
        @{ Failure = 'copy' }
        @{ Failure = 'create' }
        @{ Failure = 'verify' }
    ) {
        param($Failure)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Failure = $Failure } {
            param($Fixture, $Failure)
            $publishRoot = Join-Path $Fixture "failed disk image $Failure"
            $app = Join-Path $publishRoot 'AegiNext.app'
            [IO.Directory]::CreateDirectory($app) | Out-Null
            $script:failedDiskImageStaging = $null
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments)
                if ($FilePath -eq '/usr/bin/ditto')
                {
                    $script:failedDiskImageStaging = Split-Path $Arguments[-1]
                    if ($Failure -eq 'copy') { throw 'Fixture disk image failure.' }
                }
                if ($FilePath -eq '/usr/bin/hdiutil')
                {
                    if ($Arguments[0] -eq $Failure) { throw 'Fixture disk image failure.' }
                    if ($Arguments[0] -eq 'create') { Set-Content -LiteralPath $Arguments[-1] -Value 'partial image fixture' }
                }
                return ''
            }

            { New-AegiNextMacDiskImage -AppDirectory $app -PublishDirectory $publishRoot -ProductVersion '0.1.0' -RuntimeIdentifier osx-arm64 } | Should -Throw '*Fixture disk image failure*'
            $script:failedDiskImageStaging | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $script:failedDiskImageStaging | Should -BeFalse
        }
    }

    It 'writes the icon before signing and includes a requested DMG in the manifest (<CreateDmg>)' -TestCases @(
        @{ CreateDmg = $true }
        @{ CreateDmg = $false }
    ) {
        param($CreateDmg)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; CreateDmg = $CreateDmg } {
            param($Fixture, $CreateDmg)
            $root = Join-Path $Fixture "mac publish repository $CreateDmg"
            $ffmpeg = Join-Path $root 'sdk/ffmpeg'
            $sdl = Join-Path $root 'sdk/sdl'
            $native = Join-Path $root 'artifacts/native/osx-arm64/Release'
            $assets = Join-Path $root 'src/AegiNext.Desktop/Assets'
            $output = Join-Path $Fixture "published mac fixture $CreateDmg"
            foreach ($directory in @($root, "$ffmpeg/bin", $sdl, $native, $assets))
            {
                [IO.Directory]::CreateDirectory($directory) | Out-Null
            }
            Set-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Value '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>'
            [IO.File]::WriteAllBytes((Join-Path $assets 'AppIcon.icns'), [byte[]](105, 99, 110, 115, 0, 0, 0, 8))
            foreach ($directory in @($ffmpeg, $sdl))
            {
                Set-Content -LiteralPath (Join-Path $directory 'LICENSE.txt') -Value 'fixture SDK notice'
            }
            foreach ($name in @('ffmpeg', 'ffprobe'))
            {
                Set-Content -LiteralPath (Join-Path $ffmpeg "bin/$name") -Value 'fixture tool'
            }
            foreach ($name in @('decode', 'audio', 'export', 'media'))
            {
                Set-Content -LiteralPath (Join-Path $native "libaeginext_$name.dylib") -Value 'fixture native module'
            }
            $script:macPublishStages = [Collections.Generic.List[string]]::new()
            Mock Get-AegiNextHost { [pscustomobject]@{ Platform = 'MacOS'; Architecture = 'Arm64'; ProcessArchitecture = 'Arm64' } }
            Mock Get-AegiNextEnvironment { [pscustomobject]@{ Ready = $true; NativePrefixes = @{ ffmpeg = $ffmpeg; sdl3 = $sdl } } }
            Mock Get-AegiNextNugetLicenseRoot { @() }
            Mock Get-AegiNextSourceIdentity { [pscustomobject]@{ GitSha = ('a' * 40); WorkingTreeDirty = $true; Status = 'Recorded' } }
            Mock Find-AegiNextCommand { 'fixture-dotnet' }
            Mock Copy-AegiNextMacDependencyClosure { [pscustomobject]@{ Dependencies = @(); MinimumOSVersion = '14.0'; PackageRoots = @() } }
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments)
                if ($Arguments[0] -eq 'publish')
                {
                    $name = if ($Arguments[1].EndsWith('AegiNext.Desktop.csproj')) { 'aegi-next' } else { 'aegn-exporter' }
                    Set-Content -LiteralPath (Join-Path $Arguments[3] "$name.runtimeconfig.json") -Value '{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"}]}}'
                    if ($name -eq 'aegi-next')
                    {
                        $languages = Join-Path $Arguments[3] 'i18n'
                        [IO.Directory]::CreateDirectory($languages) | Out-Null
                        foreach ($identifier in @('en-US', 'zh-CN'))
                        {
                            @{ LanguageName = $identifier; LanguageID = $identifier; Strings = @{} } |
                                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $languages "$identifier.json") -Encoding utf8NoBOM
                        }
                    }
                    return ''
                }
                if ($FilePath -eq '/usr/bin/codesign')
                {
                    $contents = Join-Path $Arguments[-1] 'Contents'
                    Test-Path -LiteralPath (Join-Path $contents 'Resources/AppIcon.icns') -PathType Leaf | Should -BeTrue
                    Get-Content -LiteralPath (Join-Path $contents 'Info.plist') -Raw | Should -Match '<key>CFBundleIconFile</key><string>AppIcon.icns</string>'
                    $script:macPublishStages.Add($(if ($Arguments -contains '--sign') { 'sign' } else { 'verify' }))
                    return ''
                }
                if ($Arguments[0] -eq '-version') { return "$([IO.Path]::GetFileName($FilePath)) version 9.0.2 fixture" }
                throw 'Unexpected publish command.'
            }
            Mock New-AegiNextMacDiskImage {
                param($AppDirectory, $PublishDirectory, $ProductVersion, $RuntimeIdentifier)
                $AppDirectory | Should -Be (Join-Path $output 'AegiNext.app')
                $PublishDirectory | Should -Be $output
                $ProductVersion.ToString() | Should -Be '0.1.0'
                $RuntimeIdentifier | Should -Be 'osx-arm64'
                ($script:macPublishStages -join '|') | Should -Be 'sign|verify'
                Test-Path -LiteralPath (Join-Path $output 'package-manifest.json') | Should -BeFalse
                $script:macPublishStages.Add('dmg')
                $image = Join-Path $output 'AegiNext-0.1.0-osx-arm64.dmg'
                Set-Content -LiteralPath $image -Value 'verified compressed disk image fixture'
                return $image
            }

            Invoke-AegiNextPublish -RepositoryRoot $root -RuntimeIdentifier osx-arm64 -SkipBuild -CreateDmg:$CreateDmg -OutputDirectory $output

            $manifest = Get-Content -LiteralPath (Join-Path $output 'package-manifest.json') -Raw | ConvertFrom-Json
            $paths = @($manifest.Files | ForEach-Object { $_.Path.Replace('\', '/') })
            $paths | Should -Contain 'AegiNext.app/Contents/Resources/AppIcon.icns'
            if ($CreateDmg)
            {
                $paths | Should -Contain 'AegiNext-0.1.0-osx-arm64.dmg'
                ($script:macPublishStages -join '|') | Should -Be 'sign|verify|dmg'
                Should -Invoke New-AegiNextMacDiskImage -Times 1 -Exactly
            }
            else
            {
                $paths | Should -Not -Contain 'AegiNext-0.1.0-osx-arm64.dmg'
                ($script:macPublishStages -join '|') | Should -Be 'sign|verify'
                Should -Invoke New-AegiNextMacDiskImage -Times 0 -Exactly
            }
            (Test-AegiNextPublishedPackage -PackageDirectory $output).HashesVerified | Should -BeTrue
        }
    }
}

Describe 'Published provenance metadata' {
    It 'records the real Git revision and <Dirty> working tree state' -TestCases @(
        @{ Dirty = $false }
        @{ Dirty = $true }
    ) {
        param($Dirty)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Dirty = $Dirty } {
            param($Fixture, $Dirty)
            Mock Find-AegiNextCommand { 'fixture-git' }
            Mock Invoke-AegiNextCommand {
                param($FilePath, $Arguments)
                $FilePath | Should -Be 'fixture-git'
                $output = if ($Arguments -contains 'rev-parse') { 'b' * 40 } elseif ($Dirty) { ' M native/source.cpp' } else { '' }
                [pscustomobject]@{ ExitCode = 0; Output = $output }
            }
            $identity = Get-AegiNextSourceIdentity $Fixture
            $identity.GitSha | Should -Be ('b' * 40)
            $identity.WorkingTreeDirty | Should -Be $Dirty
            $identity.Status | Should -Be 'Recorded'
        }
    }

    It 'does not invent a clean source revision when Git is unavailable' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            Mock Find-AegiNextCommand { $null }
            $identity = Get-AegiNextSourceIdentity $Fixture
            $identity.GitSha | Should -BeNullOrEmpty
            $identity.WorkingTreeDirty | Should -BeNullOrEmpty
            $identity.Status | Should -Be 'GitUnavailable'
        }
    }

    It 'rejects a framework-dependent worker runtime configuration' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            foreach ($name in @('aegi-next', 'aegn-exporter'))
            {
                $frameworks = if ($name -eq 'aegi-next') { '"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.12"}]' } else { '"framework":{"name":"Microsoft.NETCore.App","version":"10.0.12"}' }
                Set-Content -LiteralPath (Join-Path $Fixture "$name.runtimeconfig.json") -Value "{`"runtimeOptions`":{`"tfm`":`"net10.0`",$frameworks}}"
            }
            { Get-AegiNextPublishedRuntimeFramework $Fixture } | Should -Throw '*not a self-contained*'
        }
    }
}

Describe 'Windows dependency closure boundaries' {
    It 'uses Windows-provided <Dependency> without copying a system library' -TestCases @(
        @{ Dependency = 'NCRYPT.dll' }
        @{ Dependency = 'AVICAP32.dll' }
        @{ Dependency = 'FONTSUB.dll' }
    ) {
        param($Dependency)
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive; Dependency = $Dependency } {
            param($Fixture, $Dependency)
            $payload = Join-Path $Fixture 'windows system dependencies'
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            Set-Content -LiteralPath (Join-Path $payload 'media.dll') -Value 'fixture'
            Mock Find-AegiNextCommand { 'objdump' }
            Mock Get-AegiNextBinaryKind { 'PE-34404' }
            Mock Invoke-AegiNextPublishCommand { "DLL Name: $Dependency" }
            $records = @(Copy-AegiNextWindowsDependencyClosure $payload @())
            $records.Count | Should -Be 1
            $records[0].Dependencies | Should -Contain $Dependency
            Test-Path -LiteralPath (Join-Path $payload $Dependency) | Should -BeFalse
        }
    }

    It 'requires a redistributable runtime to be supplied explicitly' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $payload = Join-Path $Fixture 'windows redistributable dependencies'
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            Set-Content -LiteralPath (Join-Path $payload 'media.dll') -Value 'fixture'
            Mock Find-AegiNextCommand { 'objdump' }
            Mock Get-AegiNextBinaryKind { 'PE-34404' }
            Mock Invoke-AegiNextPublishCommand { 'DLL Name: VCRUNTIME140.dll' }
            { Copy-AegiNextWindowsDependencyClosure $payload @() } | Should -Throw '*Missing dependency VCRUNTIME140.dll*'
        }
    }
}

Describe 'Mac closure correctness' {
    It 'resolves both directory and file links without requiring coreutils' -Skip:$IsWindows {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $versionRoot = Join-Path $Fixture 'Cellar/media/1.0/lib'
            $optRoot = Join-Path $Fixture 'opt'
            [IO.Directory]::CreateDirectory($versionRoot) | Out-Null
            [IO.Directory]::CreateDirectory($optRoot) | Out-Null
            $library = Join-Path $versionRoot 'libmedia.1.dylib'
            Set-Content -LiteralPath $library -Value 'fixture'
            $null = [IO.File]::CreateSymbolicLink((Join-Path $versionRoot 'libmedia.dylib'), 'libmedia.1.dylib')
            $null = [IO.Directory]::CreateSymbolicLink((Join-Path $optRoot 'media'), '../Cellar/media/1.0')
            Resolve-AegiNextCanonicalPath (Join-Path $optRoot 'media/lib/libmedia.dylib') | Should -Be $library
        }
    }

    It 'resolves loader paths and refuses an external missing dependency without fallback' {
        InModuleScope AegiNext.Publish -Parameters @{ Fixture = $TestDrive } {
            param($Fixture)
            $payload = Join-Path $Fixture 'Contents/MacOS'
            $frameworks = Join-Path $Fixture 'Contents/Frameworks'
            [IO.Directory]::CreateDirectory($payload) | Out-Null
            [IO.Directory]::CreateDirectory($frameworks) | Out-Null
            $library = Join-Path $frameworks 'libmedia.dylib'
            Set-Content -LiteralPath $library -Value 'fixture'
            Resolve-AegiNextMacDependency '@loader_path/../Frameworks/libmedia.dylib' (Join-Path $payload 'aegi-next') @() $payload $frameworks | Should -Be $library
            Resolve-AegiNextMacDependency '/usr/lib/libSystem.B.dylib' (Join-Path $payload 'aegi-next') @() $payload $frameworks | Should -BeNullOrEmpty
            $missing = Join-Path $Fixture 'missing/homebrew/library.dylib'
            { Resolve-AegiNextMacDependency $missing (Join-Path $payload 'aegi-next') @() $payload $frameworks } | Should -Throw '*Missing Mach-O*'
        }
    }

    It 'reads the real minimum OS rather than trusting the project deployment target' {
        InModuleScope AegiNext.Publish {
            Mock Invoke-AegiNextPublishCommand {
                param($FilePath, $Arguments)
                if ($FilePath -notin @('/usr/bin/otool', '/usr/bin/lipo')) { throw 'Unexpected inspection tool.' }
                switch ($Arguments[0]) {
                    '-L' { "file:`n`t/usr/lib/libSystem.B.dylib (compatibility version 1.0.0, current version 1.0.0)" }
                    '-D' { 'file:' }
                    '-l' { "cmd LC_BUILD_VERSION`ncmdsize 32`nplatform 1`nminos 27.0`nsdk 27.0" }
                    '-archs' { 'arm64' }
                    default { throw 'Unexpected inspection.' }
                }
            }
            (Get-AegiNextMacBinaryInfo '/fixture/native.dylib').MinimumOSVersion | Should -Be ([version]'27.0')
        }
    }
}


Describe 'Published localization resources' {
    BeforeAll {
        function Update-AegiNextLocalizationFixtureManifest
        {
            param([string] $Root, [string] $RuntimeIdentifier = 'win-x64')
            $manifestPath = Join-Path $Root 'package-manifest.json'
            $files = @(foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse)
            {
                if ($file.FullName -eq $manifestPath) { continue }
                @{ Path = [IO.Path]::GetRelativePath($Root, $file.FullName); Sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant(); Bytes = $file.Length }
            })
            @{ SchemaVersion = 1; ProductVersion = '0.1.0'; RuntimeIdentifier = $RuntimeIdentifier; SelfContained = $true; Files = $files } |
                ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
        }

        function New-AegiNextLocalizationFixture
        {
            param([string] $Root, [string] $RuntimeIdentifier = 'win-x64')
            $payload = if ($RuntimeIdentifier.StartsWith('osx-')) { 'AegiNext.app/Contents/MacOS' } else { 'AegiNext' }
            $directory = Join-Path $Root "$payload/i18n"
            [IO.Directory]::CreateDirectory($directory) | Out-Null
            foreach ($identifier in @('en-US', 'zh-CN'))
            {
                @{ LanguageName = $identifier; LanguageID = $identifier; Strings = @{ 'Workbench.Export' = 'Encode' } } |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory "$identifier.json") -Encoding utf8NoBOM
            }
            Update-AegiNextLocalizationFixtureManifest -Root $Root -RuntimeIdentifier $RuntimeIdentifier
            return $directory
        }
    }

    It 'verifies built-in language resources in the <RuntimeIdentifier> payload after relocation' -TestCases @(
        @{ RuntimeIdentifier = 'win-x64' }
        @{ RuntimeIdentifier = 'osx-arm64' }
        @{ RuntimeIdentifier = 'osx-x64' }
    ) {
        param($RuntimeIdentifier)
        $root = Join-Path $TestDrive "relocated localization $RuntimeIdentifier"
        $null = New-AegiNextLocalizationFixture -Root $root -RuntimeIdentifier $RuntimeIdentifier
        $result = Test-AegiNextPublishedPackage -PackageDirectory $root
        $result.HashesVerified | Should -BeTrue
        $result.LocalizationVerified | Should -BeTrue
        $result.LanguageIDs.Count | Should -Be 2
        $result.LanguageIDs | Should -Contain 'en-US'
        $result.LanguageIDs | Should -Contain 'zh-CN'
    }

    It 'rejects an absent language directory even when the manifest matches remaining files' {
        $root = Join-Path $TestDrive 'missing localization directory'
        $directory = New-AegiNextLocalizationFixture -Root $root
        Remove-Item -LiteralPath $directory -Recurse
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Missing published language directory*'
    }

    It 'rejects an absent <LanguageID> built-in package' -TestCases @(
        @{ LanguageID = 'en-US' }
        @{ LanguageID = 'zh-CN' }
    ) {
        param($LanguageID)
        $root = Join-Path $TestDrive "missing localization $LanguageID"
        $directory = New-AegiNextLocalizationFixture -Root $root
        Remove-Item -LiteralPath (Join-Path $directory "$LanguageID.json")
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Missing published language package*'
    }

    It 'rejects invalid <Field> metadata' -TestCases @(
        @{ Field = 'LanguageName'; Value = '' }
        @{ Field = 'LanguageID'; Value = '' }
        @{ Field = 'LanguageID'; Value = 'system' }
        @{ Field = 'LanguageID'; Value = 'fr-FR' }
        @{ Field = 'Strings'; Value = $null }
        @{ Field = 'Strings'; Value = @{ Text = 42 } }
    ) {
        param($Field, $Value)
        $root = Join-Path $TestDrive "invalid language metadata $([Guid]::NewGuid().ToString('N'))"
        $directory = New-AegiNextLocalizationFixture -Root $root
        $path = Join-Path $directory 'en-US.json'
        $data = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        $data[$Field] = $Value
        $data | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8NoBOM
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Invalid published language package*'
    }

    It 'rejects malformed JSON and duplicate <ContentKind>' -TestCases @(
        @{ ContentKind = 'JSON'; Json = '{' }
        @{ ContentKind = 'fields'; Json = '{"LanguageName":"English","LanguageName":"Duplicate","LanguageID":"en-US","Strings":{}}' }
        @{ ContentKind = 'keys'; Json = '{"LanguageName":"English","LanguageID":"en-US","Strings":{"Text":"First","Text":"Second"}}' }
    ) {
        param($Json)
        $root = Join-Path $TestDrive "invalid language JSON $([Guid]::NewGuid().ToString('N'))"
        $directory = New-AegiNextLocalizationFixture -Root $root
        Set-Content -LiteralPath (Join-Path $directory 'en-US.json') -Value $Json -Encoding utf8NoBOM
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Invalid published language package*'
    }

    It 'rejects duplicate LanguageID values regardless of case or filename' {
        $root = Join-Path $TestDrive 'duplicate language identifier'
        $directory = New-AegiNextLocalizationFixture -Root $root
        @{ LanguageName = 'Duplicate English'; LanguageID = 'EN-us'; Strings = @{} } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'another-language.json') -Encoding utf8NoBOM
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Duplicate published LanguageID*'
    }

    It 'includes additional valid language packages in verification' {
        $root = Join-Path $TestDrive 'additional published language'
        $directory = New-AegiNextLocalizationFixture -Root $root
        @{ LanguageName = "Fran$([char]0xe7)ais"; LanguageID = 'fr-FR'; Strings = @{ 'Workbench.Export' = 'Encoder' } } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'french.json') -Encoding utf8NoBOM
        Update-AegiNextLocalizationFixtureManifest -Root $root
        (Test-AegiNextPublishedPackage -PackageDirectory $root).LanguageIDs | Should -Contain 'fr-FR'
    }

    It 'rejects UTF-8 BOM language files' {
        $root = Join-Path $TestDrive 'language encoding'
        $directory = New-AegiNextLocalizationFixture -Root $root
        $path = Join-Path $directory 'en-US.json'
        $bytes = [IO.File]::ReadAllBytes($path)
        [IO.File]::WriteAllBytes($path, [byte[]](0xef, 0xbb, 0xbf) + $bytes)
        Update-AegiNextLocalizationFixtureManifest -Root $root
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*UTF-8 without a BOM*'
    }

    It 'keeps language files in the existing package hash inventory' {
        $root = Join-Path $TestDrive 'tampered language contents'
        $directory = New-AegiNextLocalizationFixture -Root $root
        Add-Content -LiteralPath (Join-Path $directory 'en-US.json') -Value ' ' -Encoding utf8NoBOM
        { Test-AegiNextPublishedPackage -PackageDirectory $root } | Should -Throw '*Package hash mismatch*'
    }
}
