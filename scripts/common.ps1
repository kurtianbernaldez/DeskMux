$ErrorActionPreference = 'Stop'
$DeskMuxRoot = Split-Path -Parent $PSScriptRoot
$DeskMuxLocalDotnet = Join-Path $DeskMuxRoot '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $DeskMuxLocalDotnet) {
    $DeskMuxDotnet = $DeskMuxLocalDotnet
    $env:DOTNET_ROOT = Split-Path -Parent $DeskMuxDotnet
    $env:DOTNET_CLI_HOME = Join-Path $DeskMuxRoot '.tools\dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $DeskMuxRoot '.tools\nuget'
} else {
    $DeskMuxDotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Invoke-DeskMuxDotnet {
    param([string[]]$Arguments)
    & $DeskMuxDotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
