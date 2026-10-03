#Requires -Version 7.2

function Get-AegiNextSdlEnvironment
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $ManagerCommand, [string] $SdlRoot)
    $platform = $HostInfo.Platform
    $manager = if ($platform -eq 'Windows') { 'scoop' } else { 'brew' }
    $package = if ($platform -eq 'Windows') { Join-Path $RepositoryRoot 'scripts/build/scoop/aeginext-sdl3.json' } else { 'sdl3' }
    $root = if ($SdlRoot) { $SdlRoot } else { $env:SDL3_DIR }
    $explicitRoot = ![string]::IsNullOrWhiteSpace($root)
    if (!$root -and $ManagerCommand)
    {
        $arguments = if ($platform -eq 'Windows') { @('prefix', 'aeginext-sdl3') } else { @('--prefix', 'sdl3') }
        $result = Invoke-AegiNextCommand $ManagerCommand $arguments $RepositoryRoot
        if ($result.ExitCode -eq 0)
        {
            $root = $result.Output.Trim()
        }
    }
    if (!$root -or !(Test-Path -LiteralPath $root -PathType Container))
    {
        $status = if ($explicitRoot) { 'Invalid' } else { 'Missing' }
        return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' $status 'SDL3 3.4.16 development SDK is required; select -SdlRoot or SDL3_DIR.' $package $manager); Root = $null }
    }
    $root = (Resolve-Path -LiteralPath $root).Path
    $files = @('include/SDL3/SDL_version.h', 'include/SDL3/SDL_audio.h', 'lib/cmake/SDL3/SDL3Config.cmake')
    $files += if ($platform -eq 'Windows') { @('lib/libSDL3.dll.a', 'bin/SDL3.dll') } else { @('lib/libSDL3.dylib') }
    foreach ($file in $files)
    {
        if (!(Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf))
        {
            return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Invalid' "Incomplete SDL3 SDK: $root/$file"); Root = $null }
        }
    }
    $header = Get-Content -LiteralPath (Join-Path $root 'include/SDL3/SDL_version.h') -Raw
    $parts = foreach ($part in @('MAJOR', 'MINOR', 'MICRO'))
    {
        [regex]::Match($header, "(?m)^\s*#define\s+SDL_$($part)_VERSION\s+(\d+)\s*$").Groups[1].Value
    }
    if (($parts -join '.') -cne '3.4.16')
    {
        return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Invalid' "SDL3 headers require 3.4.16; found $($parts -join '.')."); Root = $null }
    }
    [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Ready' "SDL3 3.4.16 headers, CMake package and runtime at $root; runtime identity is checked on load."); Root = $root }
}
