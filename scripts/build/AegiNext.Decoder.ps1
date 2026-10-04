#Requires -Version 7.2

function Find-AegiNextDecoderTool
{
    param([string] $Name, [string] $Package, [string] $RepositoryRoot, [string] $ManagerCommand, [string] $Platform)
    $command = Find-AegiNextCommand $Name
    if ($command -or $Platform -ne 'Windows' -or !$ManagerCommand)
    {
        return $command
    }

    $prefix = Invoke-AegiNextCommand $ManagerCommand @('prefix', $Package) $RepositoryRoot
    if ($prefix.ExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($prefix.Output))
    {
        foreach ($relativePath in @("bin/$Name.exe", "$Name.exe"))
        {
            $candidate = Join-Path $prefix.Output.Trim() $relativePath
            if (Test-Path -LiteralPath $candidate -PathType Leaf)
            {
                return $candidate
            }
        }
    }
}

function Get-AegiNextFfmpegSdkCheck
{
    param([string] $Root, [string] $RepositoryRoot, [string] $Platform, [object] $Toolchain)
    foreach ($library in $Toolchain.libraries.PSObject.Properties)
    {
        $directory = Join-Path $Root "include/$($library.Name)"
        $header = Join-Path $directory 'version.h'
        if (!(Test-Path -LiteralPath $header -PathType Leaf))
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "Missing development header: $header. A CLI-only FFmpeg installation is insufficient."
        }

        $source = Get-Content -LiteralPath $header -Raw
        $majorHeader = Join-Path $directory 'version_major.h'
        if (Test-Path -LiteralPath $majorHeader -PathType Leaf)
        {
            $source += "`n" + (Get-Content -LiteralPath $majorHeader -Raw)
        }
        $macroPrefix = $library.Name.ToUpperInvariant()
        $parts = foreach ($part in @('MAJOR', 'MINOR', 'MICRO'))
        {
            [regex]::Match($source, "(?m)^\s*#define\s+$($macroPrefix)_VERSION_$part\s+(\d+)\s*$").Groups[1].Value
        }
        $version = $parts -join '.'
        if ($version -cne $library.Value)
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "$($library.Name) headers are '$version'; requires $($library.Value). Existing SDKs are not automatically replaced."
        }

        $name = $library.Name.Substring(3)
        $publicHeader = if ($name -eq 'avutil') { 'frame.h' } else { "$name.h" }
        if (!(Test-Path -LiteralPath (Join-Path $directory $publicHeader) -PathType Leaf))
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "Incomplete $($library.Name) development headers at $directory."
        }

        if ($Platform -eq 'Windows')
        {
            $importLibraries = @("lib/lib$name.dll.a", "lib/$name.lib")
            if (!@($importLibraries | Where-Object { Test-Path -LiteralPath (Join-Path $Root $_) -PathType Leaf }).Count)
            {
                return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "Missing $name import library in $Root/lib. Select the shared development package."
            }
            $runtime = Join-Path $Root "bin/$name-$($parts[0]).dll"
        }
        else
        {
            $runtime = Join-Path $Root "lib/lib$name.dylib"
        }
        if (!(Test-Path -LiteralPath $runtime -PathType Leaf))
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "Missing FFmpeg runtime library: $runtime."
        }
    }

    foreach ($name in @('ffmpeg', 'ffprobe'))
    {
        $suffix = if ($Platform -eq 'Windows') { '.exe' } else { '' }
        $command = Join-Path $Root "bin/$name$suffix"
        if (!(Test-Path -LiteralPath $command -PathType Leaf))
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "The selected development package must include $command for runtime version verification."
        }
        $check = Get-AegiNextMediaToolCheck -Name $name -RepositoryRoot $RepositoryRoot -Toolchain $Toolchain -FilePath $command
        if ($check.Status -ne 'Ready')
        {
            return Get-AegiNextCheck 'FfmpegSdk' 'Invalid' $check.Detail
        }
    }

    Get-AegiNextCheck 'FfmpegSdk' 'Ready' "$($Toolchain.version) development headers, link libraries and tool runtime versions at $Root"
}

function Get-AegiNextDecoderEnvironment
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $ManagerCommand, [string] $FfmpegRoot, [switch] $RunTests)
    $checks = [Collections.Generic.List[object]]::new()
    $prefixes = @{}
    $platform = $HostInfo.Platform
    $manager = if ($platform -eq 'Windows') { 'scoop' } else { 'brew' }
    $isSupportedArchitecture = ($platform -eq 'Windows' -and $HostInfo.Architecture -in @('Arm64', 'X64') -and
        $HostInfo.ProcessArchitecture -in @('Arm64', 'X64')) -or
        ($platform -eq 'MacOS' -and $HostInfo.Architecture -eq $HostInfo.ProcessArchitecture -and
         $HostInfo.Architecture -in @('Arm64', 'X64'))
    if (!$isSupportedArchitecture)
    {
        $checks.Add((Get-AegiNextCheck 'Architecture' 'Unsupported' 'Decoder requires native macOS arm64/x64 or Windows x64 target on an x64/arm64 host.'))
    }
    else
    {
        $checks.Add((Get-AegiNextCheck 'Architecture' 'Ready' "$($HostInfo.Architecture) host; Windows native output is always x64, including ARM64 emulation."))
    }

    $cmakeSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'native/decoder/CMakeLists.txt') -Raw
    $minimumCmake = [regex]::Match($cmakeSource, 'cmake_minimum_required\(VERSION ([\d.]+)\)').Groups[1].Value
    foreach ($tool in @(
        @{ Id = 'CMake'; Name = 'cmake'; Package = 'cmake'; Minimum = $minimumCmake },
        @{ Id = 'Ninja'; Name = 'ninja'; Package = 'ninja'; Minimum = '1.10' }
    ))
    {
        $package = if ($platform -eq 'Windows') { "main/$($tool.Package)" } else { $tool.Package }
        $command = Find-AegiNextDecoderTool $tool.Name $tool.Package $RepositoryRoot $ManagerCommand $platform
        if (!$command)
        {
            $checks.Add((Get-AegiNextCheck $tool.Id 'Missing' "Missing $($tool.Name) >= $($tool.Minimum)." $package $manager))
            continue
        }
        $result = Invoke-AegiNextCommand $command @('--version') $RepositoryRoot
        $version = [regex]::Match($result.Output, '\d+\.\d+(\.\d+)?').Value
        if ($result.ExitCode -eq 0 -and $version -and [version]$version -ge [version]$tool.Minimum)
        {
            $checks.Add((Get-AegiNextCheck $tool.Id 'Ready' "$version via $command"))
            $prefixes[$tool.Name] = $command
        }
        else
        {
            $checks.Add((Get-AegiNextCheck $tool.Id 'Invalid' "Requires $($tool.Minimum) or newer. $($result.Output)"))
        }
    }
    if ($RunTests)
    {
        $ctest = Find-AegiNextDecoderTool 'ctest' 'cmake' $RepositoryRoot $ManagerCommand $platform
        if ($ctest)
        {
            $prefixes['ctest'] = $ctest
        }
        else
        {
            $package = if ($platform -eq 'Windows') { 'main/cmake' } else { 'cmake' }
            $checks.Add((Get-AegiNextCheck 'CTest' 'Missing' 'CTest is required for -RunTests.' $package $manager))
        }
    }

    if ($platform -eq 'MacOS')
    {
        $os = Invoke-AegiNextCommand '/usr/bin/sw_vers' @('-productVersion') $RepositoryRoot
        if ($os.ExitCode -eq 0 -and $os.Output.Trim() -match '^\d+\.\d+(\.\d+)?$' -and [version]$os.Output.Trim() -ge [version]'14.0')
        {
            $checks.Add((Get-AegiNextCheck 'MacOS' 'Ready' $os.Output.Trim()))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'MacOS' 'Unsupported' 'Decoder requires macOS 14 or newer.'))
        }
        $xcrun = Find-AegiNextCommand 'xcrun'
        $appleReady = $false
        if ($xcrun)
        {
            $cc = Invoke-AegiNextCommand $xcrun @('--find', 'clang') $RepositoryRoot
            $cxx = Invoke-AegiNextCommand $xcrun @('--find', 'clang++') $RepositoryRoot
            $sdk = Invoke-AegiNextCommand $xcrun @('--sdk', 'macosx', '--show-sdk-path') $RepositoryRoot
            $appleReady = $cc.ExitCode -eq 0 -and $cxx.ExitCode -eq 0 -and $sdk.ExitCode -eq 0 -and
                (Test-Path -LiteralPath $cc.Output.Trim() -PathType Leaf) -and
                (Test-Path -LiteralPath $cxx.Output.Trim() -PathType Leaf) -and
                (Test-Path -LiteralPath $sdk.Output.Trim() -PathType Container)
        }
        if ($appleReady)
        {
            $prefixes['cc'] = $cc.Output.Trim()
            $prefixes['cxx'] = $cxx.Output.Trim()
            $checks.Add((Get-AegiNextCheck 'AppleToolchain' 'Ready' $sdk.Output.Trim()))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'AppleToolchain' 'Missing' 'Install/select Xcode or Command Line Tools. Homebrew cannot replace the Apple SDK.'))
        }
    }
    else
    {
        foreach ($compiler in @(@{ Name = 'gcc'; Key = 'cc' }, @{ Name = 'g++'; Key = 'cxx' }))
        {
            $command = Find-AegiNextDecoderTool $compiler.Name 'mingw' $RepositoryRoot $ManagerCommand $platform
            if (!$command)
            {
                $checks.Add((Get-AegiNextCheck $compiler.Name 'Missing' 'Requires the x64 MinGW-w64 compiler from Scoop main/mingw.' 'main/mingw' 'scoop'))
                continue
            }
            $machine = Invoke-AegiNextCommand $command @('-dumpmachine') $RepositoryRoot
            if ($machine.ExitCode -eq 0 -and $machine.Output.Trim() -ceq 'x86_64-w64-mingw32')
            {
                $prefixes[$compiler.Key] = $command
                $checks.Add((Get-AegiNextCheck $compiler.Name 'Ready' "$($machine.Output.Trim()) via $command"))
            }
            else
            {
                $checks.Add((Get-AegiNextCheck $compiler.Name 'Invalid' "Requires x86_64-w64-mingw32; got '$($machine.Output.Trim())' via $command."))
            }
        }
    }

    $package = if ($platform -eq 'Windows') { 'main/ffmpeg-shared' } else { 'ffmpeg' }
    $root = if ($FfmpegRoot) { $FfmpegRoot } else { $env:FFMPEG_DIR }
    $explicitRoot = ![string]::IsNullOrWhiteSpace($root)
    if (!$explicitRoot -and $ManagerCommand)
    {
        $arguments = if ($platform -eq 'Windows') { @('prefix', 'ffmpeg-shared') } else { @('--prefix', 'ffmpeg') }
        $resolved = Invoke-AegiNextCommand $ManagerCommand $arguments $RepositoryRoot
        if ($resolved.ExitCode -eq 0 -and ![string]::IsNullOrWhiteSpace($resolved.Output))
        {
            $root = $resolved.Output.Trim()
        }
    }
    if (!$root -or !(Test-Path -LiteralPath $root -PathType Container))
    {
        if ($explicitRoot)
        {
            $checks.Add((Get-AegiNextCheck 'FfmpegSdk' 'Invalid' "The selected FFmpeg SDK directory does not exist: $root. Correct -FfmpegRoot or FFMPEG_DIR."))
        }
        else
        {
            $checks.Add((Get-AegiNextCheck 'FfmpegSdk' 'Missing' 'Requires the locked FFmpeg shared development package, including headers, link libraries and runtime libraries.' $package $manager))
        }
    }
    else
    {
        $root = (Resolve-Path -LiteralPath $root).Path
        $toolchain = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'src/AegiNext.Media/Probing/ffmpeg-toolchain.json') -Raw | ConvertFrom-Json
        $check = Get-AegiNextFfmpegSdkCheck $root $RepositoryRoot $platform $toolchain
        $checks.Add($check)
        if ($check.Status -eq 'Ready')
        {
            $prefixes['ffmpeg'] = $root
        }
    }
    [pscustomobject]@{ Checks = $checks.ToArray(); NativePrefixes = $prefixes }
}

function Test-AegiNextNativeCacheReset
{
    param([string] $SourceDirectory, [string] $BuildDirectory, [hashtable] $NativePrefixes, [string] $Platform)
    $cachePath = Join-Path $BuildDirectory 'CMakeCache.txt'
    if (!(Test-Path -LiteralPath $cachePath -PathType Leaf))
    {
        return $false
    }
    $cache = @{}
    foreach ($match in [regex]::Matches((Get-Content -LiteralPath $cachePath -Raw), '(?m)^([^/#\r\n][^:\r\n]*):[^=\r\n]*=([^\r\n]*)'))
    {
        $cache[$match.Groups[1].Value] = $match.Groups[2].Value
    }
    $comparison = if ($Platform -eq 'Windows') { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    foreach ($owner in @(
        @{ Key = 'CMAKE_HOME_DIRECTORY'; Expected = $SourceDirectory },
        @{ Key = 'CMAKE_CACHEFILE_DIR'; Expected = $BuildDirectory }
    ))
    {
        if (!$cache.ContainsKey($owner.Key) -or !$cache[$owner.Key])
        {
            throw "Cannot verify ownership of CMake cache $cachePath ($($owner.Key) missing). Inspect this directory; it will not be reset automatically."
        }
        $actual = [IO.Path]::GetFullPath($cache[$owner.Key].Replace('\', '/')).TrimEnd([char[]]@('/', '\'))
        $expected = [IO.Path]::GetFullPath($owner.Expected.Replace('\', '/')).TrimEnd([char[]]@('/', '\'))
        if (!$actual.Equals($expected, $comparison))
        {
            throw "CMake cache $cachePath belongs to another source or build directory. Existing files will not be reset automatically."
        }
    }
    if (!$cache.ContainsKey('CMAKE_GENERATOR') -or $cache['CMAKE_GENERATOR'] -cne 'Ninja')
    {
        return $true
    }
    foreach ($compiler in @(@{ Key = 'CMAKE_C_COMPILER'; Prefix = 'cc' }, @{ Key = 'CMAKE_CXX_COMPILER'; Prefix = 'cxx' }))
    {
        if (!$cache.ContainsKey($compiler.Key) -or !$cache[$compiler.Key])
        {
            return $true
        }
        $actual = $cache[$compiler.Key].Replace('\', '/')
        $expected = $NativePrefixes[$compiler.Prefix].Replace('\', '/')
        if (!$actual.Equals($expected, $comparison))
        {
            return $true
        }
    }
    return $false
}

function Get-AegiNextDecoderBuildPlan
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $Configuration, [switch] $RunTests, [int] $Jobs, [hashtable] $NativePrefixes,
        [ValidateSet('Decoder', 'Audio', 'Export')][string] $Target = 'Decoder')
    $component = $Target.ToLowerInvariant()
    foreach ($key in @('ffmpeg', 'cc', 'cxx', 'cmake', 'ninja'))
    {
        if (!$NativePrefixes.ContainsKey($key))
        {
            throw "Decoder dependency is missing: $key. Run the environment check first."
        }
    }
    if ($RunTests -and !$NativePrefixes.ContainsKey('ctest'))
    {
        throw 'Decoder dependency is missing: ctest. Run the environment check with -RunTests.'
    }
    $architecture = $HostInfo.Architecture.ToLowerInvariant()
    $rid = if ($HostInfo.Platform -eq 'Windows') { 'win-x64' } else { "osx-$architecture" }
    if ($Target -eq 'Audio' -and !$NativePrefixes.ContainsKey('sdl3'))
    {
        throw 'Audio dependency is missing: sdl3. Run the environment check first.'
    }
    $directory = Join-Path $RepositoryRoot "artifacts/native/build-$component-$rid-$($Configuration.ToLowerInvariant())"
    $output = Join-Path $RepositoryRoot "artifacts/native/$rid/$Configuration"
    $plan = [Collections.Generic.List[object]]::new()
    if ($HostInfo.Platform -eq 'Windows')
    {
        $plan.Add([pscustomobject]@{
            Label = "Stage $component development runtime"; FilePath = $NativePrefixes['cmake']; WorkingDirectory = $RepositoryRoot; Environment = @{}
            Arguments = [string[]]@("-DAEGINEXT_FFMPEG_ROOT=$($NativePrefixes['ffmpeg'])", "-DAEGINEXT_NATIVE_OUTPUT_DIR=$output",
                '-P', (Join-Path $RepositoryRoot 'scripts/build/copy-decoder-runtime.cmake'))
        })
        if ($Target -eq 'Audio')
        {
            $plan[0].Arguments = [string[]]@("-DAEGINEXT_SDL_ROOT=$($NativePrefixes['sdl3'])") + $plan[0].Arguments
        }
    }
    $arguments = [Collections.Generic.List[string]]::new()
    if (Test-AegiNextNativeCacheReset -SourceDirectory (Join-Path $RepositoryRoot "native/$component") -BuildDirectory $directory -NativePrefixes $NativePrefixes -Platform $HostInfo.Platform)
    {
        $arguments.Add('--fresh')
    }
    $arguments.AddRange([string[]]@('-S', (Join-Path $RepositoryRoot "native/$component"), '-B', $directory, '-G', 'Ninja',
        "-DCMAKE_BUILD_TYPE=$Configuration", "-DAEGINEXT_FFMPEG_ROOT=$($NativePrefixes['ffmpeg'])", "-DAEGINEXT_NATIVE_OUTPUT_DIR=$output",
        "-DCMAKE_C_COMPILER=$($NativePrefixes['cc'])", "-DCMAKE_CXX_COMPILER=$($NativePrefixes['cxx'])", "-DCMAKE_MAKE_PROGRAM=$($NativePrefixes['ninja'])", '-DBUILD_TESTING=ON'))
    if ($Target -eq 'Audio')
    {
        $arguments.Add("-DAEGINEXT_SDL_ROOT=$($NativePrefixes['sdl3'])")
    }
    if ($HostInfo.Platform -eq 'MacOS')
    {
        $cmakeArchitecture = if ($architecture -eq 'arm64') { 'arm64' } else { 'x86_64' }
        $arguments.AddRange([string[]]@('-DCMAKE_OSX_DEPLOYMENT_TARGET=14.0', "-DCMAKE_OSX_ARCHITECTURES=$cmakeArchitecture"))
    }
    $plan.Add([pscustomobject]@{
        Label = "Configure $component"; FilePath = $NativePrefixes['cmake']; WorkingDirectory = $RepositoryRoot; Environment = @{}
        Arguments = $arguments.ToArray()
    })
    $plan.Add([pscustomobject]@{
        Label = "Build $component"; FilePath = $NativePrefixes['cmake']; WorkingDirectory = $RepositoryRoot; Environment = @{}
        Arguments = [string[]]@('--build', $directory, '--config', $Configuration, '--parallel', "$Jobs")
    })
    if ($RunTests)
    {
        $plan.Add([pscustomobject]@{
            Label = "Test $component"; FilePath = $NativePrefixes['ctest']; WorkingDirectory = $RepositoryRoot; Environment = @{}
            Arguments = [string[]]@('--test-dir', $directory, '--build-config', $Configuration, '--output-on-failure')
        })
    }
    $plan.ToArray()
}
