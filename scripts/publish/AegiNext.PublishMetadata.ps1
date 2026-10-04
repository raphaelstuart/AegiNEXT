#Requires -Version 7.2

function Get-AegiNextSourceIdentity
{
    param([string] $RepositoryRoot)
    $git = Find-AegiNextCommand 'git'
    if (!$git) { return [pscustomobject]@{ GitSha = $null; WorkingTreeDirty = $null; Status = 'GitUnavailable' } }
    $configuration = @('-c', "safe.directory=$RepositoryRoot")
    $revision = Invoke-AegiNextCommand $git ($configuration + @('rev-parse', '--verify', 'HEAD')) $RepositoryRoot
    $status = Invoke-AegiNextCommand $git ($configuration + @('status', '--porcelain=v1', '--untracked-files=normal')) $RepositoryRoot
    if ($revision.ExitCode -ne 0 -or $status.ExitCode -ne 0 -or $revision.Output.Trim() -notmatch '^[0-9a-fA-F]{40,64}$')
    {
        return [pscustomobject]@{ GitSha = $null; WorkingTreeDirty = $null; Status = 'GitMetadataUnavailable' }
    }
    return [pscustomobject]@{ GitSha = $revision.Output.Trim().ToLowerInvariant(); WorkingTreeDirty = ![string]::IsNullOrWhiteSpace($status.Output); Status = 'Recorded' }
}

function Get-AegiNextPublishedToolVersion
{
    param([string] $Payload, [string] $RuntimeIdentifier)
    $suffix = if ($RuntimeIdentifier -eq 'win-x64') { '.exe' } else { '' }
    foreach ($name in @('ffmpeg', 'ffprobe'))
    {
        $relativePath = "tools/$name$suffix"
        $output = Invoke-AegiNextPublishCommand (Join-Path $Payload $relativePath) @('-version') $Payload
        $line = ($output -split '\r?\n', 2)[0].Trim()
        if ($line -notmatch "^$name version (\S+)(?:\s|$)") { throw "Unexpected packaged $name version output: $line" }
        [pscustomobject]@{ Name = $name; Path = $relativePath; Version = $Matches[1]; VersionLine = $line }
    }
}

function Get-AegiNextPublishedRuntimeFramework
{
    param([string] $Payload)
    foreach ($name in @('aegi-next', 'AegiNext.ExportWorker'))
    {
        $filename = "$name.runtimeconfig.json"
        $configuration = Get-Content -LiteralPath (Join-Path $Payload $filename) -Raw | ConvertFrom-Json -AsHashtable
        $options = $configuration.runtimeOptions
        if ($options.ContainsKey('framework') -or $options.ContainsKey('frameworks') -or !$options.ContainsKey('includedFrameworks'))
        {
            throw "$filename is not a self-contained runtime configuration."
        }
        $frameworks = @($options.includedFrameworks | ForEach-Object { [pscustomobject]@{ Name = $_.name; Version = $_.version } })
        if (!@($frameworks | Where-Object { $_.Name -eq 'Microsoft.NETCore.App' -and ([version]$_.Version).Major -eq 10 }).Count)
        {
            throw "$filename does not include the required .NET 10 runtime."
        }
        [pscustomobject]@{ Application = $name; RuntimeConfig = $filename; TargetFramework = $options.tfm; IncludedFrameworks = $frameworks }
    }
}

function Get-AegiNextRuntimeOperatingSystemPolicy
{
    param([object] $HostInfo)
    $policy = [ordered]@{
        Platform = $HostInfo.Platform
        BuildHostDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        BuildHostVersion = [Environment]::OSVersion.Version.ToString()
        BuildHostArchitecture = $HostInfo.Architecture
        ProductMinimumOSVerified = $false
        RequiredRuntimePolicy = $null
    }
    if ($HostInfo.Platform -eq 'Windows')
    {
        $policy.RequiredRuntimePolicy = [ordered]@{
            RuntimeFamily = '.NET 10'
            Source = 'https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md'
            PolicyVerifiedOn = '2026-10-04'
            PolicySourceUpdatedOn = '2026-09-28'
            ClientReleases = @('11 26H1', '11 25H2', '11 24H2 (IoT)', '11 24H2 (E)', '11 24H2', '11 23H2 (E)', '10 21H2 (E)', '10 21H2 (IoT)', '10 1809 (E)', '10 1607 (E)')
            LifecycleRequirement = 'Only editions and releases still supported by the OS publisher are supported by .NET. E denotes Enterprise/Education; listed Windows 10 editions are LTSC/Enterprise.'
            TargetArchitecture = 'x64'
            Arm64Execution = 'Windows 11 supports x64 emulation; this package remains x64.'
            ProductValidation = 'Runtime support policy does not establish the minimum OS of the complete native application. Build host metadata is not a test result.'
        }
    }
    return $policy
}
