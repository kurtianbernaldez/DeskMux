param([switch]$Integration, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration
    Invoke-DeskMuxDotnet @('run', '--project', 'tests/DeskMux.Tests', '--configuration', $Configuration, '--no-build')
    if ($Integration) {
        Invoke-DeskMuxDotnet @('run', '--project', 'tests/DeskMux.Integration', '--configuration', $Configuration, '--no-build')
    }
} finally { Pop-Location }
