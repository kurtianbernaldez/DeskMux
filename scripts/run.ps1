param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    Invoke-DeskMuxDotnet @('build', 'src/DeskMux.App', '--configuration', $Configuration, "-p:RestoreConfigFile=$DeskMuxRoot\NuGet.Config", '-m:1', '-nr:false', '-p:BuildInParallel=false')
    $DeskMuxApp = Join-Path $DeskMuxRoot "src\DeskMux.App\bin\$Configuration\net10.0-windows\DeskMux.exe"
    Start-Process -FilePath $DeskMuxApp -WorkingDirectory (Split-Path -Parent $DeskMuxApp) -WindowStyle Normal
} finally { Pop-Location }
