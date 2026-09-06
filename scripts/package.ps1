param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$Version
)
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    $DeskMuxPackage = Join-Path $DeskMuxRoot 'artifacts\DeskMux-win-x64'
    $resolvedRoot = [IO.Path]::GetFullPath($DeskMuxRoot).TrimEnd('\') + '\'
    $resolvedPackage = [IO.Path]::GetFullPath($DeskMuxPackage)
    if (-not $resolvedPackage.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package path is outside the DeskMux workspace.' }
    if (Test-Path -LiteralPath $DeskMuxPackage) { Remove-Item -LiteralPath $DeskMuxPackage -Recurse -Force }
    $publishArguments = @('publish', 'src/DeskMux.App', '--configuration', $Configuration, '--runtime', 'win-x64', '--self-contained', 'true', '--output', $DeskMuxPackage, '-p:PublishSingleFile=false', "-p:RestoreConfigFile=$DeskMuxRoot\NuGet.Config", '-m:1', '-nr:false', '-p:BuildInParallel=false')
    if ($Version) { $publishArguments += "-p:Version=$Version" }
    Invoke-DeskMuxDotnet $publishArguments
    Copy-Item -LiteralPath (Join-Path $DeskMuxRoot 'README.md') -Destination $DeskMuxPackage
    Write-Host "DeskMux package: $DeskMuxPackage"
} finally { Pop-Location }
