#Requires -Version 7.2

function Get-AegiNextDependencyManifest
{
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../native/sdk-dependencies.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($manifest.schemaVersion -ne 1)
    {
        throw 'Unsupported project SDK manifest schema.'
    }
    return $manifest
}

function Get-AegiNextProjectSdkRoot
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $Name)
    $manifest = Get-AegiNextDependencyManifest
    if (!$manifest.packages.ContainsKey($Name))
    {
        throw "Unknown project SDK: $Name."
    }
    $rid = Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo
    return Join-Path $RepositoryRoot ".dependencies/$rid/$Name/$($manifest.packages[$Name].version)"
}

function Find-AegiNextProjectSdk
{
    param([string] $RepositoryRoot, [object] $HostInfo, [string] $Name)
    if (!$RepositoryRoot -or !(Test-Path -LiteralPath (Join-Path $RepositoryRoot '.dependencies') -PathType Container))
    {
        return $null
    }

    $root = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $Name
    if (Test-Path -LiteralPath $root)
    {
        return $root
    }
    return $null
}

function ConvertTo-AegiNextCanonicalValue
{
    param([object] $Value)
    if ($Value -is [Collections.IDictionary])
    {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys | Sort-Object)
        {
            $result[$key] = ConvertTo-AegiNextCanonicalValue $Value[$key]
        }
        return $result
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [string])
    {
        $result = @($Value | ForEach-Object { ConvertTo-AegiNextCanonicalValue $_ })
        return ,$result
    }
    return $Value
}

function Get-AegiNextSdkFingerprint
{
    param([hashtable] $Manifest, [string] $Name, [string] $Platform)
    $packages = [ordered]@{}
    $sources = [ordered]@{}
    foreach ($dependency in Get-AegiNextSdkInstallOrder $Manifest @($Name) $Platform)
    {
        $package = $Manifest.packages[$dependency]
        $packages[$dependency] = $package
        $sourceNames = if ($Platform -eq 'Windows') { @($package.windows.source) } else { $package.sources }
        foreach ($source in $sourceNames)
        {
            $sources[$source] = $Manifest.sources[$source]
        }
    }
    $content = ConvertTo-AegiNextCanonicalValue @{ RecipeRevision = $Manifest.recipeRevision; MinimumMacOS = $Manifest.minimumMacOS; Packages = $packages; Sources = $sources }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($content | ConvertTo-Json -Depth 20 -Compress))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Test-AegiNextInstalledSdk
{
    param([string] $Root, [hashtable] $Manifest, [string] $Name, [object] $HostInfo)
    $receiptPath = Join-Path $Root '.aeginext-sdk.json'
    if (!(Test-Path -LiteralPath $receiptPath -PathType Leaf))
    {
        return $false
    }
    try
    {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
        $rid = Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo
        if ($receipt.owner -ne 'AegiNext' -or $receipt.name -ne $Name -or $receipt.rid -ne $rid -or
            $receipt.fingerprint -ne (Get-AegiNextSdkFingerprint $Manifest $Name $HostInfo.Platform) -or
            $receipt.installPrefix -ne [IO.Path]::GetFullPath($Root) -or !$receipt.files.Count)
        {
            return $false
        }
        foreach ($file in $receipt.files.Keys)
        {
            $path = [IO.Path]::GetFullPath((Join-Path $Root $file))
            $boundary = [IO.Path]::GetFullPath($Root) + [IO.Path]::DirectorySeparatorChar
            if (!$path.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
                !(Test-Path -LiteralPath $path -PathType Leaf) -or
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $receipt.files[$file])
            {
                return $false
            }
        }
        return $true
    }
    catch
    {
        return $false
    }
}

function Get-AegiNextNativePkgEnvironment
{
    param([string] $RepositoryRoot, [object] $HostInfo, [hashtable] $NativePrefixes)
    $path = Join-Path $NativePrefixes['libplacebo'] 'lib/pkgconfig'
    $root = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo 'libplacebo'
    if ([IO.Path]::GetFullPath($root) -ne [IO.Path]::GetFullPath($NativePrefixes['libplacebo']))
    {
        return @{ PKG_CONFIG_PATH = $path + [IO.Path]::PathSeparator + $env:PKG_CONFIG_PATH }
    }
    $manifest = Get-AegiNextDependencyManifest
    $names = @(Get-AegiNextSdkInstallOrder $manifest @('libplacebo') $HostInfo.Platform)
    $paths = @($names | ForEach-Object {
        $prefix = Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $_
        Join-Path $prefix 'lib/pkgconfig'
        Join-Path $prefix 'share/pkgconfig'
    })
    return @{ PKG_CONFIG_PATH = ''; PKG_CONFIG_LIBDIR = $paths -join [IO.Path]::PathSeparator }
}

function Assert-AegiNextSdkReceipt
{
    param([string] $Root, [string] $RepositoryRoot, [object] $HostInfo, [string] $Name)
    if (!$RepositoryRoot -or !(Test-Path -LiteralPath (Join-Path $Root '.aeginext-sdk.json') -PathType Leaf))
    {
        return
    }
    $manifest = Get-AegiNextDependencyManifest
    foreach ($dependency in Get-AegiNextSdkInstallOrder $manifest @($Name) $HostInfo.Platform)
    {
        $prefix = if ($dependency -eq $Name) { $Root } else { Get-AegiNextProjectSdkRoot $RepositoryRoot $HostInfo $dependency }
        if (!(Test-AegiNextInstalledSdk $prefix $manifest $dependency $HostInfo))
        {
            throw "Project SDK $dependency is incomplete or its lock changed. Run -InstallDependencies to repair managed SDKs."
        }
    }
}
