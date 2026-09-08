param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$Version)
. "$PSScriptRoot\common.ps1"
Push-Location -LiteralPath $DeskMuxRoot
try {
    $package=Join-Path $DeskMuxRoot 'artifacts\DeskMux-win-x64'
    $stage=Join-Path $DeskMuxRoot ('artifacts\package-stage-'+[Guid]::NewGuid().ToString('N'))
    $arguments=@('publish','src/DeskMux.App','--configuration',$Configuration,'--runtime','win-x64','--self-contained','true','--output',$stage,'-p:PublishSingleFile=false',"-p:RestoreConfigFile=$DeskMuxRoot\NuGet.Config",'-m:1','-nr:false','-p:BuildInParallel=false','-p:UseSharedCompilation=false')
    if($Version){$arguments+="-p:Version=$Version"}
    Invoke-DeskMuxDotnet $arguments
    Copy-Item -LiteralPath (Join-Path $DeskMuxRoot 'README.md') -Destination $stage
    $versionInfo=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'DeskMux.dll'))
    $hashes=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $stage -File -Recurse){$relative=[IO.Path]::GetRelativePath($stage,$file.FullName);$hashes[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    @{Product='DeskMux';Version=$versionInfo.ProductVersion;BuiltUtc=[DateTime]::UtcNow.ToString('u');Files=$hashes}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $stage 'build.json') -Encoding utf8
    New-Item -ItemType Directory -Path $package -Force|Out-Null
    # Detect a running build before replacing any file. Data and portable.mode are never touched.
    foreach($file in Get-ChildItem -LiteralPath $stage -File -Recurse){$to=Join-Path $package ([IO.Path]::GetRelativePath($stage,$file.FullName));if(Test-Path -LiteralPath $to){$probe=[IO.File]::Open($to,'Open','ReadWrite','None');$probe.Dispose()}}
    $backup=Join-Path $DeskMuxRoot ('artifacts\package-backup-'+[Guid]::NewGuid().ToString('N'))
    $written=[Collections.Generic.List[string]]::new()
    try {
        foreach($file in Get-ChildItem -LiteralPath $stage -File -Recurse){
            $relative=[IO.Path]::GetRelativePath($stage,$file.FullName);$to=Join-Path $package $relative;$old=Join-Path $backup $relative
            if(Test-Path -LiteralPath $to){New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force|Out-Null;Copy-Item -LiteralPath $to -Destination $old}
            $written.Add($relative);New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $to -Force
        }
    } catch {
        foreach($relative in $written){$old=Join-Path $backup $relative;$to=Join-Path $package $relative;if(Test-Path -LiteralPath $old){Copy-Item -LiteralPath $old -Destination $to -Force}else{Remove-Item -LiteralPath $to -ErrorAction SilentlyContinue}}
        throw
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $DeskMuxRoot 'artifacts\DeskMux-win-x64.zip') -Force
    Write-Host "Updated package: $package"
} finally { Pop-Location }
