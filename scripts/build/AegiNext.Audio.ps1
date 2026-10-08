#Requires -Version 7.2

function Get-AegiNextSdlEnvironment
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $ManagerCommand, [string] $SdlRoot)
    $platform = $HostInfo.Platform
    $package = 'sdl3'
    $root = $SdlRoot
    $explicitRoot = ![string]::IsNullOrWhiteSpace($root)
    if (!$explicitRoot)
    {
        $root = Find-AegiNextProjectSdk $RepositoryRoot $HostInfo 'sdl3'
    }
    if (!$root)
    {
        $root = $env:SDL3_DIR
    }
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
        $status = if ($explicitRoot -or $root) { 'Invalid' } else { 'Missing' }
        return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' $status 'A locked SDL3 development SDK is required; use -InstallDependencies or select -SdlRoot / SDL3_DIR.' $package 'project'); Root = $null }
    }
    $root = (Resolve-Path -LiteralPath $root).Path
    try
    {
        Assert-AegiNextSdkReceipt $root $RepositoryRoot $HostInfo 'sdl3'
    }
    catch
    {
        return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Invalid' $_.Exception.Message); Root = $null }
    }
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
    $version = (Get-AegiNextDependencyManifest).packages.sdl3.version
    if (($parts -join '.') -cne $version)
    {
        return [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Invalid' "SDL3 headers require $version; found $($parts -join '.')."); Root = $null }
    }
    [pscustomobject]@{ Check = (Get-AegiNextCheck 'SdlSdk' 'Ready' "SDL3 $version headers, CMake package and runtime at $root; runtime identity is checked on load."); Root = $root }
}
