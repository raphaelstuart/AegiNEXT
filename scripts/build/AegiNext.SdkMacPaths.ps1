#Requires -Version 7.2

function Test-AegiNextSdkMachO
{
    param([string] $Path)
    $stream = [IO.File]::OpenRead($Path)
    try
    {
        $bytes = [byte[]]::new(4)
        if ($stream.Read($bytes, 0, 4) -ne 4)
        {
            return $false
        }
        return [Convert]::ToHexString($bytes) -in @('CFFAEDFE', 'CEFAEDFE', 'FEEDFACF', 'FEEDFACE', 'CAFEBABE', 'BEBAFECA', 'CAFEBABF', 'BFBAFECA')
    }
    finally
    {
        $stream.Dispose()
    }
}

function Repair-AegiNextSdkPath
{
    param([string] $Stage, [string] $FinalRoot, [string] $RepositoryRoot, [object] $HostInfo)
    $ridRoot = Join-Path $RepositoryRoot ".dependencies/$(Get-AegiNextRuntimeIdentifier -HostInfo $HostInfo)"
    $architecture = if ($HostInfo.Architecture -eq 'Arm64') { 'arm64' } else { 'x86_64' }
    foreach ($file in Get-ChildItem -LiteralPath $Stage -File -Recurse)
    {
        if ($file.LinkTarget)
        {
            $target = $file.ResolveLinkTarget($true)
            if (!$target -or !$target.FullName.StartsWith($Stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal))
            {
                throw "SDK symlink leaves its installation: $($file.FullName)."
            }
            continue
        }
        if ($file.Extension -in @('.pc', '.cmake', '.la'))
        {
            $replacement = if ($file.Extension -eq '.pc') { '${pcfiledir}/../..' } else { $FinalRoot }
            $content = Get-Content -LiteralPath $file.FullName -Raw
            if ($content.Contains($Stage))
            {
                [IO.File]::WriteAllText($file.FullName, $content.Replace($Stage, $replacement), [Text.UTF8Encoding]::new($false))
            }
        }
        if (!(Test-AegiNextSdkMachO $file.FullName))
        {
            continue
        }
        $archs = Invoke-AegiNextCommand '/usr/bin/lipo' @('-archs', $file.FullName) $Stage
        if ($archs.ExitCode -ne 0 -or $architecture -notin ($archs.Output.Trim() -split '\s+'))
        {
            throw "SDK architecture mismatch: $($file.FullName) requires $architecture."
        }
        $commands = Invoke-AegiNextCommand '/usr/bin/otool' @('-l', $file.FullName) $Stage
        $minimumVersions = @([regex]::Matches($commands.Output, '(?m)^\s*minos (\d+\.\d+(?:\.\d+)?)') | ForEach-Object { [version]$_.Groups[1].Value })
        $minimumVersions += @([regex]::Matches($commands.Output, '(?s)cmd LC_VERSION_MIN_MACOSX\s+cmdsize \d+\s+version (\d+\.\d+(?:\.\d+)?)') | ForEach-Object { [version]$_.Groups[1].Value })
        foreach ($minimum in $minimumVersions)
        {
            if ($minimum -gt [version](Get-AegiNextDependencyManifest).minimumMacOS)
            {
                throw "SDK $($file.FullName) requires macOS $minimum, above the project deployment target."
            }
        }
        $ids = Invoke-AegiNextCommand '/usr/bin/otool' @('-D', $file.FullName) $Stage
        $identityLines = @($ids.Output -split '\r?\n' | Select-Object -Skip 1 | Where-Object { $_.Trim() })
        $identity = if ($identityLines.Count) { $identityLines[0].Trim() } else { '' }
        $links = Invoke-AegiNextCommand '/usr/bin/otool' @('-L', $file.FullName) $Stage
        if ($links.ExitCode -ne 0 -or $commands.ExitCode -ne 0)
        {
            throw "Cannot inspect SDK Mach-O: $($file.FullName)."
        }
        $references = @([regex]::Matches($links.Output, '(?m)^\s+(.+?) \(compatibility version') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne $identity })
        $finalBinary = Join-Path $FinalRoot ([IO.Path]::GetRelativePath($Stage, $file.FullName))
        foreach ($reference in $references)
        {
            if ($reference.StartsWith('/usr/lib/') -or $reference.StartsWith('/System/Library/'))
            {
                continue
            }
            if ($reference.StartsWith($Stage + '/'))
            {
                $target = $reference.Replace($Stage, $FinalRoot)
            }
            elseif ($reference.StartsWith($ridRoot + '/'))
            {
                $target = $reference
                if (!(Test-Path -LiteralPath $target -PathType Leaf))
                {
                    throw "Missing project SDK dependency: $target."
                }
            }
            elseif ($reference.StartsWith('@loader_path/'))
            {
                $stagedTarget = [IO.Path]::GetFullPath($reference.Replace('@loader_path', (Split-Path $file.FullName)))
                if (!(Test-Path -LiteralPath $stagedTarget -PathType Leaf))
                {
                    throw "Missing SDK loader dependency: $reference."
                }
                $target = $stagedTarget.Replace($Stage, $FinalRoot)
            }
            elseif ($reference.StartsWith('@rpath/'))
            {
                $candidates = @(Join-Path $Stage "lib/$($reference.Substring(7))")
                $candidates += @(Get-ChildItem -LiteralPath $ridRoot -Filter ([IO.Path]::GetFileName($reference)) -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName)
                $found = @($candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
                if (!$found.Count)
                {
                    throw "Cannot resolve project SDK rpath dependency: $reference."
                }
                $target = $found[0].Replace($Stage, $FinalRoot)
            }
            else
            {
                throw "SDK retains a non-project dependency: $reference in $($file.FullName)."
            }
            $relative = [IO.Path]::GetRelativePath((Split-Path $finalBinary), $target).Replace('\', '/')
            Invoke-AegiNextSdkCommand '/usr/bin/install_name_tool' @('-change', $reference, "@loader_path/$relative", $file.FullName) $Stage
        }
        if ($identity)
        {
            Invoke-AegiNextSdkCommand '/usr/bin/install_name_tool' @('-id', "@rpath/$($file.Name)", $file.FullName) $Stage
        }
        foreach ($rpath in [regex]::Matches($commands.Output, '(?s)cmd LC_RPATH\s+cmdsize \d+\s+path (.*?) \(offset \d+\)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        {
            if ([IO.Path]::IsPathFullyQualified($rpath))
            {
                Invoke-AegiNextSdkCommand '/usr/bin/install_name_tool' @('-delete_rpath', $rpath, $file.FullName) $Stage
            }
        }
        Invoke-AegiNextSdkCommand '/usr/bin/codesign' @('--force', '--sign', '-', $file.FullName) $Stage
    }
}
