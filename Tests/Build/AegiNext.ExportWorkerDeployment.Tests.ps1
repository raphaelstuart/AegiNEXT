#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $desktop = [xml](Get-Content -LiteralPath (Join-Path $repository 'src/AegiNext.Desktop/AegiNext.Desktop.csproj') -Raw)
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
}

Describe 'Adjacent export worker deployment' {
    It 'copies and publishes a renamed worker once and preserves its runnable sidecars' {
        $root = Join-Path $TestDrive 'renamed worker deployment'
        $mainDirectory = Join-Path $root 'main'
        $workerDirectory = Join-Path $root 'worker'
        foreach ($directory in @($mainDirectory, $workerDirectory))
        {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
        $workerProject = Join-Path $workerDirectory 'worker.csproj'
        [IO.File]::WriteAllText($workerProject, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>aegn-exporter</AssemblyName>
    <UseAppHost>true</UseAppHost>
  </PropertyGroup>
</Project>
'@)
        [IO.File]::WriteAllText((Join-Path $workerDirectory 'Program.cs'), 'System.Console.WriteLine("fixture worker");')
        $fixture = [xml]@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>fixture-desktop</AssemblyName>
  </PropertyGroup>
  <ItemGroup />
</Project>
'@
        $reference = $desktop.SelectSingleNode('//ProjectReference[contains(@Include,"AegiNext.ExportWorker")]')
        $reference = $fixture.ImportNode($reference, $true)
        $reference.SetAttribute('Include', $workerProject)
        $null = $fixture.SelectSingleNode('/Project/ItemGroup').AppendChild($reference)
        foreach ($target in $desktop.Project.Target | Where-Object Name -in @('ResolveExportWorkerTarget', 'CopyExportWorker', 'PublishExportWorker'))
        {
            $target = $fixture.ImportNode($target, $true)
            foreach ($build in $target.SelectNodes('.//MSBuild'))
            {
                $build.SetAttribute('Projects', $workerProject)
            }
            $null = $fixture.Project.AppendChild($target)
        }
        $mainProject = Join-Path $mainDirectory 'main.csproj'
        $fixture.Save($mainProject)
        [IO.File]::WriteAllText((Join-Path $mainDirectory 'Program.cs'), 'System.Console.WriteLine("fixture desktop");')

        $output = & $dotnet build $mainProject -c Release --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        $buildDirectory = Join-Path $mainDirectory 'bin/Release/net10.0'
        $suffix = if ($IsWindows) { '.exe' } else { '' }
        $files = @("aegn-exporter$suffix", 'aegn-exporter.dll', 'aegn-exporter.deps.json', 'aegn-exporter.runtimeconfig.json')
        foreach ($name in $files)
        {
            Test-Path -LiteralPath (Join-Path $buildDirectory $name) -PathType Leaf | Should -BeTrue
        }

        $payload = Join-Path $root 'publish'
        $output = & $dotnet publish $mainProject -c Release --no-restore -o $payload --verbosity minimal 2>&1
        $LASTEXITCODE | Should -Be 0 -Because ($output | Out-String)
        foreach ($name in $files)
        {
            Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf | Should -BeTrue
        }
        $output = & (Join-Path $payload "aegn-exporter$suffix")
        $LASTEXITCODE | Should -Be 0
        ($output | Out-String).Trim() | Should -Be 'fixture worker'
    }
}
