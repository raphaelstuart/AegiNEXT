#Requires -Version 7.2
<#
.SYNOPSIS
Exports the application PNG sizes, Windows ICO and macOS ICNS from one square PNG on macOS.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SourcePng,
    [string] $DestinationDirectory = (Join-Path $PSScriptRoot '../../src/AegiNext.Desktop/Assets')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$IsMacOS) { throw 'Icon export requires the macOS sips and iconutil tools.' }
$source = (Resolve-Path -LiteralPath $SourcePng).Path
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$metadata = & /usr/bin/sips -g pixelWidth -g pixelHeight $source 2>&1
if ($LASTEXITCODE -ne 0) { throw "Unable to inspect source PNG: $metadata" }
$dimensions = [regex]::Matches(($metadata | Out-String), 'pixel(?:Width|Height):\s+(\d+)')
if ($dimensions.Count -ne 2 -or $dimensions[0].Groups[1].Value -ne $dimensions[1].Groups[1].Value)
{
    throw 'The icon source must be a square PNG.'
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) "aeginext-icons-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporary) | Out-Null
try
{
    $master = Join-Path $temporary 'AppIcon.png'
    $null = & /usr/bin/swift (Join-Path $PSScriptRoot 'prepare-app-icon.swift') $source $master 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to export the 1024-pixel icon master.' }
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256, 512)
    foreach ($size in $sizes)
    {
        $null = & /usr/bin/sips -z $size $size $master --out (Join-Path $temporary "AppIcon-$size.png") 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Unable to export the $size-pixel icon." }
    }

    $windowsSizes = @($sizes | Where-Object { $_ -le 256 })
    $frames = @($windowsSizes | ForEach-Object { ,([IO.File]::ReadAllBytes((Join-Path $temporary "AppIcon-$_.png"))) })
    $stream = [IO.File]::Create((Join-Path $temporary 'AppIcon.ico'))
    $writer = [IO.BinaryWriter]::new($stream)
    try
    {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$windowsSizes.Count)
        $offset = [uint32](6 + 16 * $windowsSizes.Count)
        for ($index = 0; $index -lt $windowsSizes.Count; $index++)
        {
            $dimension = if ($windowsSizes[$index] -eq 256) { 0 } else { $windowsSizes[$index] }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write($offset)
            $offset += [uint32]$frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose() }

    $iconset = Join-Path $temporary 'AppIcon.iconset'
    [IO.Directory]::CreateDirectory($iconset) | Out-Null
    foreach ($size in @(16, 32, 128, 256, 512))
    {
        foreach ($scale in @(1, 2))
        {
            $pixels = $size * $scale
            $name = "icon_${size}x${size}" + $(if ($scale -eq 2) { '@2x' } else { '' }) + '.png'
            $image = if ($pixels -eq 1024) { $master } else { Join-Path $temporary "AppIcon-$pixels.png" }
            Copy-Item -LiteralPath $image -Destination (Join-Path $iconset $name)
        }
    }
    $null = & /usr/bin/iconutil -c icns $iconset -o (Join-Path $temporary 'AppIcon.icns') 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Unable to encode the macOS icon set.' }
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Get-ChildItem -LiteralPath $temporary -File | Copy-Item -Destination $destination -Force
    Write-Information -InformationAction Continue -MessageData "Exported PNG sizes, 9-frame ICO and Retina ICNS to $destination"
}
finally
{
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
