param([string]$Release=(Join-Path $PSScriptRoot '..\artifacts\release'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($name in @('DeskMux-update-x64.zip','DeskMux-portable-x64.zip')) {
    $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $Release $name))
    try {
        if($zip.Entries.FullName|Where-Object {$_ -match '(^|[/\\])(Data|logs)([/\\]|$)|sessions\.json'}){throw 'User data present in release'}
        $reader=[IO.StreamReader]::new($zip.GetEntry('build.json').Open());try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
        foreach($file in $manifest.Files.PSObject.Properties){
            $entry=$zip.GetEntry($file.Name.Replace('\','/'));if(-not $entry){$entry=$zip.GetEntry($file.Name)}
            if(-not $entry){throw "Missing packaged file: $($file.Name)"}
            $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
            try{$hash=[Convert]::ToHexString($sha.ComputeHash($stream));if($hash -ne $file.Value){throw "Bad packaged hash: $($file.Name)"}}finally{$stream.Dispose();$sha.Dispose()}
        }
        if($name -eq 'DeskMux-update-x64.zip' -and $zip.GetEntry('portable.mode')){throw 'Update must preserve the existing data mode'}
        if($name -eq 'DeskMux-portable-x64.zip' -and -not $zip.GetEntry('portable.mode')){throw 'Portable mode marker missing'}
        Write-Host "PASS verified $name"
    } finally {$zip.Dispose()}
}
foreach($line in Get-Content -LiteralPath (Join-Path $Release 'SHA256SUMS.txt')){
    $hash,$name=$line -split '  ',2
    if((Get-FileHash -LiteralPath (Join-Path $Release $name) -Algorithm SHA256).Hash -ne $hash){throw "Asset checksum mismatch: $name"}
}
Write-Host 'PASS release asset checksums'
