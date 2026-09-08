param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$NoPackage)
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    Invoke-DeskMuxDotnet @('restore', 'DeskMux.slnx', '--configfile', "$DeskMuxRoot\NuGet.Config", '--disable-parallel', '-m:1', '-nr:false', '-p:BuildInParallel=false')
    Invoke-DeskMuxDotnet @('build', 'DeskMux.slnx', '--no-restore', '--configuration', $Configuration, '-m:1', '-nr:false', '-p:BuildInParallel=false')
    if (-not $NoPackage) { & "$PSScriptRoot\package.ps1" -Configuration $Configuration }
} finally { Pop-Location }
