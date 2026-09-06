param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    Invoke-DeskMuxDotnet @('restore', 'DeskMux.slnx', '--configfile', "$DeskMuxRoot\NuGet.Config", '--disable-parallel', '-m:1', '-nr:false', '-p:BuildInParallel=false')
    Invoke-DeskMuxDotnet @('build', 'DeskMux.slnx', '--no-restore', '--configuration', $Configuration, '-m:1', '-nr:false', '-p:BuildInParallel=false')
} finally { Pop-Location }
