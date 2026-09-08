using System.Diagnostics;
using System.Text.Json;
namespace DeskMux.App.UI;
internal static class UpdateInstaller
{
    internal static void Choose(AppController controller)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="DeskMux update (*.zip)|*.zip"};if(dialog.ShowDialog()!=true)return;
        try {
            var work=Path.Combine(controller.DataDirectory,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
            var source=UpdatePackage.Extract(dialog.FileName,Path.Combine(work,"package"));var manifest=UpdatePackage.Verify(source);
            if(MessageBox.Show("Install DeskMux "+manifest.Version+" (built "+manifest.BuiltUtc+")?\n\nDeskMux will restart. Sessions and settings stay in "+controller.DataDirectory+". Only install packages from a source you trust.","Install update",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            var target=Path.GetFullPath(AppContext.BaseDirectory);
            var probe=Path.Combine(target,".update-probe-"+Guid.NewGuid());File.WriteAllText(probe,"");File.Delete(probe);
            var config=Path.Combine(work,"update.json");File.WriteAllText(config,JsonSerializer.Serialize(new{Source=source,Target=target,Data=controller.DataDirectory,Parent=Environment.ProcessId,Restart=true,Files=manifest.Files.Keys.Append("build.json").ToArray()}));
            var script=Path.Combine(work,"apply.ps1");File.WriteAllText(script,Script);
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-Config",config})start.ArgumentList.Add(arg);
            Process.Start(start);controller.Exit();
        } catch(Exception e){MessageBox.Show("Update could not be prepared. Your current installation is unchanged.\n\n"+e.Message,"DeskMux update");}
    }
    private const string Script="""
param([string]$Config)
$ErrorActionPreference='Stop'
$c=Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
$work=Split-Path -Parent $Config
$backup=Join-Path $work 'backup'
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Wait-Process -Id $c.Parent -ErrorAction SilentlyContinue
$written=[Collections.Generic.List[string]]::new()
try {
    foreach($name in $c.Files) {
        $to=[IO.Path]::GetFullPath((Join-Path $c.Target $name))
        if(-not $to.StartsWith(([IO.Path]::GetFullPath($c.Target).TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe target'}
        $old=Join-Path $backup $name
        if(Test-Path -LiteralPath $to){New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force | Out-Null;Copy-Item -LiteralPath $to -Destination $old}
        $written.Add($name)
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        $done=$false
        for($attempt=0;$attempt -lt 20;$attempt++) {
            try{Copy-Item -LiteralPath (Join-Path $c.Source $name) -Destination $to -Force;$done=$true;break}catch{Start-Sleep -Milliseconds 500}
        }
        if(-not $done){throw "Could not replace $name"}
    }
    'Update installed' | Set-Content -LiteralPath (Join-Path $work 'result.txt')
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $work 'result.txt')
    foreach($name in $written){$old=Join-Path $backup $name;$to=Join-Path $c.Target $name;if(Test-Path -LiteralPath $old){Copy-Item -LiteralPath $old -Destination $to -Force}else{Remove-Item -LiteralPath $to -ErrorAction SilentlyContinue}}
}
if($c.Restart){Start-Process -FilePath (Join-Path $c.Target 'DeskMux.exe') -ArgumentList @('--data-dir',('"'+$c.Data+'"')) -WindowStyle Hidden}
""";
}
