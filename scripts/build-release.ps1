param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$DisplayVersion = '0.1.0-alpha.1',
    [string]$FileVersion = '0.1.0.0',
    [switch]$SkipPublish,
    [switch]$RequireInstaller
)
. "$PSScriptRoot\common.ps1"
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $DeskMuxRoot
try {
    if (-not $SkipPublish) { & "$PSScriptRoot\package.ps1" -Configuration $Configuration -Version $DisplayVersion }
    $published = Join-Path $DeskMuxRoot 'artifacts\DeskMux-win-x64'
    if (-not (Test-Path -LiteralPath (Join-Path $published 'DeskMux.exe'))) { throw 'Published DeskMux package was not found.' }

    $release = Join-Path $DeskMuxRoot 'artifacts\release'
    $portable = Join-Path $DeskMuxRoot 'artifacts\DeskMux-portable-x64'
    $resolvedRoot = [IO.Path]::GetFullPath($DeskMuxRoot).TrimEnd('\') + '\'
    foreach ($target in @($release, $portable)) {
        $resolved = [IO.Path]::GetFullPath($target)
        if (-not $resolved.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Release path is outside the DeskMux workspace: $resolved" }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        New-Item -ItemType Directory -Path $target | Out-Null
    }

    Copy-Item -Path (Join-Path $published '*') -Destination $portable -Recurse
    New-Item -ItemType File -Path (Join-Path $portable 'portable.mode') -Force | Out-Null
    @'
DeskMux portable edition

Keep portable.mode beside DeskMux.exe. DeskMux will store sessions, settings, and logs in the Data folder beside the application. Move the whole folder together. Applications themselves are not bundled.
'@ | Set-Content -LiteralPath (Join-Path $portable 'PORTABLE.txt') -Encoding utf8
    $portableZip = Join-Path $release 'DeskMux-portable-x64.zip'
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableZip -CompressionLevel Optimal
    Remove-Item -LiteralPath $portable -Recurse -Force

    $iscc = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if ($iscc) {
        & $iscc "/DMyAppVersion=$FileVersion" "/DMyAppDisplayVersion=$DisplayVersion" (Join-Path $DeskMuxRoot 'installer\DeskMux.iss')
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    } elseif ($RequireInstaller) {
        throw 'Inno Setup 6 is required to build the installer.'
    } else {
        Write-Warning 'Inno Setup 6 is not installed. The portable ZIP was built; the installer will be built by the release workflow.'
    }

    & "$PSScriptRoot\write-checksums.ps1"
    Write-Host "Release assets: $release"
} finally { Pop-Location }
