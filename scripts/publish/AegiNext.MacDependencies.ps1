#Requires -Version 7.2

function Get-AegiNextMacBinaryInfo
{
    param([string] $Path)
    $directory = Split-Path $Path
    $links = Invoke-AegiNextPublishCommand '/usr/bin/otool' @('-L', $Path) $directory
    $commands = Invoke-AegiNextPublishCommand '/usr/bin/otool' @('-l', $Path) $directory
    $ids = Invoke-AegiNextPublishCommand '/usr/bin/otool' @('-D', $Path) $directory
    $idLines = @($ids -split '\r?\n' | Select-Object -Skip 1 | Where-Object { $_.Trim() })
    $identity = if ($idLines.Count) { $idLines[0].Trim() } else { $null }
    $dependencies = @([regex]::Matches($links, '(?m)^\s+(.+?) \(compatibility version') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne $identity })
    $rpaths = @([regex]::Matches($commands, '(?s)cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset \d+\)') | ForEach-Object { $_.Groups[1].Value })
    $versions = @([regex]::Matches($commands, '(?m)^\s*minos (\d+\.\d+(?:\.\d+)?)') | ForEach-Object { [version]$_.Groups[1].Value })
    $versions += @([regex]::Matches($commands, '(?s)cmd LC_VERSION_MIN_MACOSX\s+cmdsize \d+\s+version (\d+\.\d+(?:\.\d+)?)') | ForEach-Object { [version]$_.Groups[1].Value })
    $minimum = if ($versions.Count) { ($versions | Sort-Object -Descending)[0] } else { [version]'14.0' }
    $architectures = (Invoke-AegiNextPublishCommand '/usr/bin/lipo' @('-archs', $Path) $directory).Trim() -split '\s+'
    [pscustomobject]@{ Identity = $identity; Dependencies = $dependencies; RPaths = $rpaths; MinimumOSVersion = $minimum; Architectures = $architectures }
}

function Resolve-AegiNextMacDependency
{
    param([string] $Reference, [string] $Source, [string[]] $RPaths, [string] $Payload, [string] $Frameworks)
    $loader = Split-Path $Source
    if ($Reference.StartsWith('/usr/lib/') -or $Reference.StartsWith('/System/Library/')) { return $null }
    if ($Reference.StartsWith('@loader_path/')) { $candidates = @($Reference.Replace('@loader_path', $loader)) }
    elseif ($Reference.StartsWith('@executable_path/')) { $candidates = @($Reference.Replace('@executable_path', $Payload)) }
    elseif ($Reference.StartsWith('@rpath/'))
    {
        $name = $Reference.Substring(7)
        $candidates = @($RPaths | ForEach-Object { Join-Path ($_.Replace('@loader_path', $loader).Replace('@executable_path', $Payload)) $name })
        $candidates += @(Join-Path $loader $name; Join-Path $Payload $name; Join-Path $Frameworks $name)
    }
    elseif ([IO.Path]::IsPathFullyQualified($Reference)) { $candidates = @($Reference) }
    else { throw "Unresolved relative Mach-O dependency '$Reference' in $Source." }
    foreach ($candidate in $candidates)
    {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) }
    }
    throw "Missing Mach-O dependency '$Reference' in $Source. Candidates: $($candidates -join ', ')"
}

function Resolve-AegiNextCanonicalPath
{
    param([string] $Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $current = [IO.Path]::GetPathRoot($fullPath)
    foreach ($component in ($fullPath.Substring($current.Length) -split '[\\/]'))
    {
        if (!$component) { continue }
        $current = Join-Path $current $component
        $entry = if ([IO.Directory]::Exists($current)) { [IO.DirectoryInfo]::new($current) } else { [IO.FileInfo]::new($current) }
        if ($entry.LinkTarget)
        {
            $target = $entry.ResolveLinkTarget($true)
            if (!$target) { throw "Cannot resolve symbolic link $current." }
            $current = $target.FullName
        }
    }
    return $current
}

function Copy-AegiNextMacDependencyClosure
{
    param([string] $Payload, [string] $RuntimeIdentifier, [hashtable] $SourcePaths = @{})
    $architecture = if ($RuntimeIdentifier -eq 'osx-arm64') { 'arm64' } else { 'x86_64' }
    $frameworks = Join-Path (Split-Path $Payload) 'Frameworks'
    [IO.Directory]::CreateDirectory($frameworks) | Out-Null
    $queue = [Collections.Generic.Queue[object]]::new()
    $binaries = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $destinations = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in Get-ChildItem -LiteralPath $Payload -File -Recurse)
    {
        if ((Get-AegiNextBinaryKind $file.FullName) -eq 'MachO')
        {
            $source = if ($SourcePaths.ContainsKey($file.FullName)) { [IO.Path]::GetFullPath($SourcePaths[$file.FullName]) } else { $file.FullName }
            $item = [pscustomobject]@{ Source = $source; Destination = $file.FullName; Info = $null; References = @(); SourceSha256 = (Get-FileHash -LiteralPath $source).Hash }
            $binaries.Add($item.Source, $item)
            $destinations[$item.Destination] = $item.Source
            $queue.Enqueue($item)
        }
    }
    $minimum = [version]'14.0'
    $packageRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    while ($queue.Count)
    {
        $binary = $queue.Dequeue()
        $binary.Info = Get-AegiNextMacBinaryInfo $binary.Source
        if ($architecture -notin $binary.Info.Architectures) { throw "Wrong native architecture for ${RuntimeIdentifier}: $($binary.Source) ($($binary.Info.Architectures -join ', '))." }
        if ($binary.Info.MinimumOSVersion -gt $minimum) { $minimum = $binary.Info.MinimumOSVersion }
        foreach ($reference in $binary.Info.Dependencies)
        {
            $source = Resolve-AegiNextMacDependency $reference $binary.Source $binary.Info.RPaths $Payload $frameworks
            if (!$source) { continue }
            if (!$binaries.ContainsKey($source))
            {
                $destination = Join-Path $frameworks ([IO.Path]::GetFileName($source))
                $hash = (Get-FileHash -LiteralPath $source).Hash
                if ($destinations.ContainsKey($destination))
                {
                    $known = $binaries[$destinations[$destination]]
                    if ($known.SourceSha256 -ne $hash) { throw "Conflicting native library basename: $source and $($known.Source)." }
                    $binaries[$source] = $known
                }
                else
                {
                    Copy-Item -LiteralPath $source -Destination $destination
                    $item = [pscustomobject]@{ Source = $source; Destination = $destination; Info = $null; References = @(); SourceSha256 = $hash }
                    $binaries.Add($source, $item)
                    $destinations[$destination] = $source
                    $queue.Enqueue($item)
                    $canonical = Resolve-AegiNextCanonicalPath $source
                    if ($canonical -match '^(.*?/Cellar/[^/]+/[^/]+)(?:/|$)') { $null = $packageRoots.Add($Matches[1]) }
                    else
                    {
                        $directory = Split-Path (Split-Path $source)
                        $null = $packageRoots.Add($directory)
                    }
                }
            }
            $binary.References += [pscustomobject]@{ Reference = $reference; Target = $binaries[$source].Destination }
        }
    }
    $records = [Collections.Generic.List[object]]::new()
    foreach ($binary in $binaries.Values | Sort-Object Destination -Unique)
    {
        $directory = Split-Path $binary.Destination
        foreach ($reference in $binary.References)
        {
            $relative = [IO.Path]::GetRelativePath($directory, $reference.Target).Replace('\', '/')
            $rewritten = "@loader_path/$relative"
            if ($reference.Reference -ne $rewritten)
            {
                $null = Invoke-AegiNextPublishCommand '/usr/bin/install_name_tool' @('-change', $reference.Reference, $rewritten, $binary.Destination) $Payload
            }
        }
        if ($binary.Info.Identity)
        {
            $null = Invoke-AegiNextPublishCommand '/usr/bin/install_name_tool' @('-id', "@rpath/$([IO.Path]::GetFileName($binary.Destination))", $binary.Destination) $Payload
        }
        foreach ($rpath in $binary.Info.RPaths | Select-Object -Unique)
        {
            if ([IO.Path]::IsPathFullyQualified($rpath))
            {
                $null = Invoke-AegiNextPublishCommand '/usr/bin/install_name_tool' @('-delete_rpath', $rpath, $binary.Destination) $Payload
            }
        }
        $verification = Get-AegiNextMacBinaryInfo $binary.Destination
        foreach ($reference in $verification.Dependencies)
        {
            $null = Resolve-AegiNextMacDependency $reference $binary.Destination $verification.RPaths $Payload $frameworks
            if ($reference.StartsWith('/') -and !$reference.StartsWith('/usr/lib/') -and !$reference.StartsWith('/System/Library/')) { throw "Package retains external native dependency $reference." }
        }
        $records.Add([pscustomobject]@{ Source = $binary.Source; SourceSha256 = $binary.SourceSha256.ToLowerInvariant(); Path = [IO.Path]::GetRelativePath($Payload, $binary.Destination); Architectures = $binary.Info.Architectures; MinimumOSVersion = $binary.Info.MinimumOSVersion.ToString(); Dependencies = $verification.Dependencies })
    }
    return [pscustomobject]@{ Dependencies = $records.ToArray(); MinimumOSVersion = $minimum.ToString(); PackageRoots = @($packageRoots) }
}
