param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference='Stop'
$manifestPath=Join-Path $Package 'build.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$hashes=[ordered]@{}
$root=[IO.Path]::GetFullPath($Package).TrimEnd('\')+'\'
foreach($property in $manifest.Files.PSObject.Properties){
    $name=$property.Name;$path=[IO.Path]::GetFullPath((Join-Path $Package $name))
    if(-not $path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe manifest path'}
    $hashes[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$manifest.Files=$hashes
$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $manifestPath -Encoding utf8
