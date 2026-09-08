. "$PSScriptRoot\common.ps1"
$testRoot = Join-Path $DeskMuxRoot ('artifacts\update-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$sourceText = Get-Content -LiteralPath (Join-Path $DeskMuxRoot 'src\DeskMux.App\UI\UpdateInstaller.cs') -Raw
$script = [regex]::Match($sourceText, '(?s)private const string Script="""\r?\n(.*?)\r?\n""";').Groups[1].Value
if (-not $script) { throw 'Installer script was not found.' }
$scriptPath = Join-Path $testRoot 'apply.ps1'
Set-Content -LiteralPath $scriptPath -Value $script
foreach ($scenario in @('success', 'rollback')) {
    $work = Join-Path $testRoot $scenario
    $source = Join-Path $work 'source'
    $target = Join-Path $work 'target'
    $data = Join-Path $target 'Data'
    New-Item -ItemType Directory -Path $source, $target, $data -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'DeskMux.exe') -Value 'new app'
    Set-Content -LiteralPath (Join-Path $target 'DeskMux.exe') -Value 'old app'
    Set-Content -LiteralPath (Join-Path $data 'sessions.json') -Value 'saved sessions'
    $files = @('DeskMux.exe')
    if ($scenario -eq 'rollback') { $files += 'missing.dll' }
    $config = Join-Path $work 'update.json'
    @{Source=$source;Target=$target;Data=$data;Parent=2147483647;Restart=$false;Files=$files} | ConvertTo-Json | Set-Content -LiteralPath $config
    & powershell.exe -NoProfile -File $scriptPath -Config $config
    $expected = if ($scenario -eq 'success') { 'new app' } else { 'old app' }
    if ((Get-Content -LiteralPath (Join-Path $target 'DeskMux.exe') -Raw).Trim() -ne $expected) { throw "$scenario did not preserve the expected application." }
    if ((Get-Content -LiteralPath (Join-Path $data 'sessions.json') -Raw).Trim() -ne 'saved sessions') { throw 'User data changed.' }
    if ($scenario -eq 'success' -and (Get-Content -LiteralPath (Join-Path $work 'result.txt') -Raw).Trim() -ne 'Update installed') { throw 'Update did not report success.' }
    Write-Host "PASS updater $scenario preserves user data"
}
