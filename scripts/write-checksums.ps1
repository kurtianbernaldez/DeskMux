. "$PSScriptRoot\common.ps1"
$release = Join-Path $DeskMuxRoot 'artifacts\release'
$checksumFile = Join-Path $release 'SHA256SUMS.txt'
$assets = Get-ChildItem -LiteralPath $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name
$lines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($asset.Name)"
}
$lines | Set-Content -LiteralPath $checksumFile -Encoding ascii
