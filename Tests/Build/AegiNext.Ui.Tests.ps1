#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repository 'scripts/build/AegiNext.Build.psm1') -Force
}

Describe 'Explicit headless UI test selection' {
    It 'plans only Desktop.Ui tests on <Platform>' -TestCases @(
        @{ Platform = 'MacOS'; Architecture = 'Arm64' }
        @{ Platform = 'Windows'; Architecture = 'X64' }
    ) {
        param($Platform, $Architecture)
        $hostInfo = [pscustomobject]@{ Platform = $Platform; Architecture = $Architecture; ProcessArchitecture = $Architecture }
        $plan = @(Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -RunTests -TestProjects 'Desktop.Ui')
        $plan.Count | Should -Be 4
        $plan[-2].Label | Should -Be 'Restore test Desktop.Ui'
        $plan[-2].Arguments | Should -Not -Contain '--locked-mode'
        $plan[-1].Label | Should -Be 'Test Desktop.Ui'
        $plan[-1].Arguments | Should -Contain (Join-Path $repository 'Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj')
        $plan[-1].Arguments | Should -Not -Contain '--no-build'
        $plan[-1].Arguments | Should -Contain '--no-restore'
    }

    It 'rejects a Linux UI run until its rendering native assets are supported' {
        $hostInfo = [pscustomobject]@{ Platform = 'Linux'; Architecture = 'X64'; ProcessArchitecture = 'X64' }
        { Get-AegiNextBuildPlan -RepositoryRoot $repository -Target Managed -HostInfo $hostInfo -RunTests -TestProjects 'Desktop.Ui' } | Should -Throw '*native asset*'
    }
}
